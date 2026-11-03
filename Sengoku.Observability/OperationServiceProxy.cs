using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Sengoku.Observability;

/// <summary>Times the actual await, preserving exceptions and service lifetimes.</summary>
public class OperationServiceProxy : DispatchProxy, IDisposable, IAsyncDisposable
{
    private object target = null!;
    private OperationLogger logger = null!;
    private bool ownsTarget;
    private OperationResultPolicy results = null!;
    private int disposed;
    internal static object Wrap(Type contract, object target, OperationLogger logger, OperationResultPolicy results, bool ownsTarget)
    {
        var proxy = (OperationServiceProxy)Create(contract, typeof(OperationServiceProxy));
        proxy.target = target; proxy.logger = logger; proxy.ownsTarget = ownsTarget;
        proxy.results = results;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var type = method.ReturnType;
        if (type == typeof(Task)) return InvokeTask(method, args);
        if (type == typeof(ValueTask)) return new ValueTask(InvokeValueTask(method, args));
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(OperationServiceProxy).GetMethod(nameof(InvokeTaskOf), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(type.GenericTypeArguments).Invoke(this, [method, args]);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            return typeof(OperationServiceProxy).GetMethod(nameof(InvokeValueTaskOf), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(type.GenericTypeArguments).Invoke(this, [method, args]);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            return typeof(OperationServiceProxy).GetMethod(nameof(InvokeStream), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(type.GenericTypeArguments).Invoke(this, [method, args, default(CancellationToken)]);
        using var operation = Begin(method, args);
        try { var result = Call(method, args); RecordResult(operation, method, result); operation.Complete(); return result; }
        catch (Exception ex) { operation.Fail(ex); throw; }
    }
    private object? Call(MethodInfo method, object?[]? args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private OperationScope Begin(MethodInfo method, object?[]? args)
    {
        var scope = logger.Begin(target.GetType().Name + "." + method.Name);
        // Only explicitly allowed scalar argument names; never walk DTOs or serialize results.
        scope.Add(method.GetParameters().Select((p, i) => new KeyValuePair<string, object?>(p.Name ?? "", args?[i])));
        foreach (var value in args ?? []) scope.AddObject(value);
        return scope;
    }
    private async Task InvokeTask(MethodInfo method, object?[]? args)
    {
        using var scope = Begin(method, args);
        try { await (Task)Call(method, args)!; scope.Complete(); }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }
    private async Task<T> InvokeTaskOf<T>(MethodInfo method, object?[]? args)
    {
        using var scope = Begin(method, args);
        try { var result = await (Task<T>)Call(method, args)!; RecordResult(scope, method, result); scope.Complete(); return result; }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }
    private async Task InvokeValueTask(MethodInfo method, object?[]? args)
    {
        using var scope = Begin(method, args);
        try { await (ValueTask)Call(method, args)!; scope.Complete(); }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }
    private async ValueTask<T> InvokeValueTaskOf<T>(MethodInfo method, object?[]? args)
    {
        using var scope = Begin(method, args);
        try { var result = await (ValueTask<T>)Call(method, args)!; RecordResult(scope, method, result); scope.Complete(); return result; }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }
    private void RecordResult(OperationScope scope, MethodInfo method, object? result)
    {
        scope.AddObject(result);
        results.Apply(scope, result);
        if (result is bool flag)
        {
            scope.Add(OperationFields.Of(("Result", flag)));
            // False is normal for predicates, empty queues, and lookups. Only these command contracts declare failure.
            if (!flag && (method.Name.StartsWith("Send", StringComparison.Ordinal) || method.Name == "UpdateEventData")) scope.Reject("CommandNotCompleted");
        }
        else if (result is ICollection collection) scope.Add(OperationFields.Of(("ResultCount", collection.Count)));
        else if (result is int number) scope.Add(OperationFields.Of(("ResultCount", number)));
        else scope.Add(OperationFields.Of(("HasResult", result != null)));
    }
    private async IAsyncEnumerable<T> InvokeStream<T>(MethodInfo method, object?[]? args, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var scope = Begin(method, args);
        IAsyncEnumerator<T> enumerator;
        try { enumerator = ((IAsyncEnumerable<T>)Call(method, args)!).GetAsyncEnumerator(ct); }
        catch (Exception ex) { scope.Fail(ex); throw; }
        await using (enumerator)
        {
            while (true)
            {
                bool hasNext;
                try { hasNext = await enumerator.MoveNextAsync(); }
                catch (Exception ex) { scope.Fail(ex); throw; }
                if (!hasNext) { scope.Complete(); yield break; }
                yield return enumerator.Current;
            }
        }
    }
    public void Dispose() { if (ownsTarget && Interlocked.Exchange(ref disposed, 1) == 0 && target is IDisposable disposable) disposable.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        if (!ownsTarget || Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (target is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else if (target is IDisposable disposable) disposable.Dispose();
    }
}

public static class OperationServiceRegistration
{
    /// <summary>Call after all business registrations. New interfaces in these assemblies are included automatically.</summary>
    public static IServiceCollection InstrumentOperations(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (services.Any(d => d.ServiceType == typeof(InstrumentationMarker))) return services;
        services.AddSingleton(new InstrumentationMarker());
        var selected = assemblies.ToHashSet();
        var count = services.Count;
        for (var i = 0; i < count; i++)
        {
            var descriptor = services[i];
            var contract = descriptor.ServiceType;
            if (!contract.IsInterface || contract.IsGenericTypeDefinition || descriptor.IsKeyedService || !selected.Contains(contract.Assembly) || contract.Namespace?.Contains("Factories", StringComparison.Ordinal) == true) continue;
            var key = new object();
            // DI owns the original service, including factory-created disposables. The proxy never disposes it twice.
            if (descriptor.ImplementationInstance is { } instance)
                services.Add(ServiceDescriptor.KeyedSingleton(contract, key, instance));
            else services.Add(ServiceDescriptor.DescribeKeyed(contract, key, (provider, _) =>
                descriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!), descriptor.Lifetime));
            services[i] = ServiceDescriptor.Describe(contract, provider => OperationServiceProxy.Wrap(contract,
                provider.GetRequiredKeyedService(contract, key), provider.GetRequiredService<OperationLogger>(), provider.GetRequiredService<OperationResultPolicy>(), false), descriptor.Lifetime);
        }
        return services;
    }
    private sealed class InstrumentationMarker;
}
