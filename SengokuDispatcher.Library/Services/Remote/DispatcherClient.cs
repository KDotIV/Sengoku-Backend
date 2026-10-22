using System.Net;
using System.Net.Http.Json;
using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Models.Messaging;

namespace SengokuDispatcher.Library.Services.Remote;

/// <summary>Private service-to-service transport. It deliberately does not retry ambiguous sends.</summary>
public sealed class DispatcherClient(HttpClient client, IConfiguration configuration, IHostEnvironment environment)
{
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        string? address = configuration["Dispatcher:BaseUrl"];
        string? key = configuration["Dispatcher:ApiKey"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            key is not { Length: >= 32 })
        {
            throw new InvalidOperationException("Configure a private HTTPS Dispatcher:BaseUrl and a Dispatcher:ApiKey of at least 32 characters.");
        }
        if (!path.StartsWith("internal/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Only internal Dispatcher routes are supported.");

        using var request = new HttpRequestMessage(method, new Uri(address!.TrimEnd('/') + "/" + path));
        request.Headers.Add("X-Dispatcher-Key", key);
        request.Headers.Accept.ParseAdd("application/json");
        if (body != null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, ct);
    }

    public async Task<T?> Post<T>(string path, object body, CancellationToken ct = default)
    {
        using var response = await Send(HttpMethod.Post, path, body, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<DispatcherError>(ct);
            string message = error?.Message ?? "The background communication request failed.";
            if (response.StatusCode == HttpStatusCode.Conflict) throw new FeedConflictException(message);
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new ArgumentException(message);
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new UnauthorizedAccessException(message);
            throw new HttpRequestException("Dispatcher could not complete the communication request.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
