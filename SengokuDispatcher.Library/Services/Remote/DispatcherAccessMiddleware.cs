using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Models.Messaging;

namespace SengokuDispatcher.Library.Services.Remote;

/// <summary>Protects the private control API independently of browser cookies.</summary>
public sealed class DispatcherAccessMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<DispatcherAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/internal"))
        {
            string? expected = configuration["Dispatcher:ApiKey"];
            string supplied = context.Request.Headers["X-Dispatcher-Key"].ToString();
            if (expected is not { Length: >= 32 } || supplied.Length > 1024 ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new DispatcherError("Dispatcher authentication required."));
                return;
            }
        }
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ex switch
            {
                FeedConflictException => StatusCodes.Status409Conflict,
                ArgumentException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            bool businessError = ex is FeedConflictException or ArgumentException or UnauthorizedAccessException;
            logger.LogError("Dispatcher request failed ({ErrorType}, status {Status})", ex.GetType().Name, context.Response.StatusCode);
            await context.Response.WriteAsJsonAsync(new DispatcherError(businessError ? ex.Message : "Dispatcher communication is unavailable."));
        }
    }
}
