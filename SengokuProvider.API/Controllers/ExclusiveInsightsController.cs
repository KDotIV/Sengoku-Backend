using Microsoft.AspNetCore.Mvc;
using SengokuDispatcher.Library.Services.Remote;

namespace SengokuProvider.API.Controllers;

[ApiController, Route("api/exclusights")]
public sealed class ExclusiveInsightsController(DispatcherClient dispatcher) : ControllerBase
{
    [HttpPost("AddDiscordServerListener")]
    public Task<IActionResult> AddDiscordServerListener(CancellationToken ct) => Forward(HttpMethod.Post, "AddDiscordServerListener", ct);

    [HttpGet("GetDiscordGuilds")]
    public Task<IActionResult> GetDiscordGuilds(CancellationToken ct) => Forward(HttpMethod.Get, "GetDiscordGuilds", ct);

    [HttpGet("GetRolesByGuildId")]
    public Task<IActionResult> GetRolesByGuildId(CancellationToken ct) => Forward(HttpMethod.Get, "GetRolesByGuildId", ct);

    [HttpGet("GetCustomersReportByGuildId")]
    public Task<IActionResult> GetCustomersReportByGuildId(CancellationToken ct) => Forward(HttpMethod.Get, "GetCustomersReportByGuildId", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string action, CancellationToken ct)
    {
        try
        {
            using var response = await dispatcher.Send(method, "internal/exclusights/" + action + Request.QueryString, null, ct);
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                Content = await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return Problem("Dispatcher communication is unavailable.", statusCode: 503);
        }
    }
}
