using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SengokuProvider.Library.Services.Feeds;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;
using System.Security.Claims;
using SengokuProvider.API.Authentication;

namespace SengokuProvider.API.Controllers;

[ApiController, Authorize, Route("api/event-feeds")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EventFeedsController(IEventFeedStore store, IEventFeedQueryService events, IDiscordFeedConnection discord, IConfiguration config, ILogger<EventFeedsController> log) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
    [HttpGet("options")]
    public Task<IActionResult> Options(CancellationToken ct) => Handle(async () => {
        var catalogue = await store.Options(ct);
        return Ok(new { catalogue.Games, catalogue.Regions, catalogue.Schedules,
            discordConnectionAvailable = discord.Enabled, deliveryEnabled = config.GetValue<bool>("EventFeeds:DeliveryEnabled") });
    });
    [HttpPost("{id:guid}/discord/connect"), ValidateAntiForgeryToken, EnableRateLimiting("account-entry")]
    public Task<IActionResult> ConnectDiscord(Guid id, CancellationToken ct) => Handle(async () => {
        if (!discord.Enabled) return Problem("Discord connection is not configured.", statusCode: 503);
        var session = Request.Cookies[SessionAuthenticationHandler.CookieName];
        if (session == null) return Unauthorized();
        return Ok(new { authorizationUrl = await discord.Begin(UserId, id, session, ct) });
    });
    [HttpGet("discord/callback"), EnableRateLimiting("account-entry")]
    public Task<IActionResult> DiscordCallback(string? state, string? code, string? error, CancellationToken ct) => Handle(async () => {
        if (!discord.Enabled) return Problem("Discord connection is not configured.", statusCode: 503);
        var session = Request.Cookies[SessionAuthenticationHandler.CookieName];
        if (session == null) return Unauthorized();
        return Ok(await discord.Complete(UserId, session, state, code, error, ct));
    });
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => Handle(async () => Ok(await store.List(UserId, ct)));
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Handle(async () => {
        var feed = await store.Get(UserId, id, ct); return feed == null ? NotFound() : Ok(feed);
    });
    [HttpGet("{id:guid}/deliveries")]
    public Task<IActionResult> History(Guid id, CancellationToken ct) => Handle(async () => {
        if (await store.Get(UserId, id, ct) == null) return NotFound();
        return Ok(await store.History(UserId, id, ct));
    });
    [HttpPost("preview"), ValidateAntiForgeryToken, EnableRateLimiting("account-entry")]
    public Task<IActionResult> Preview(EventFeedFilter filter, CancellationToken ct) => Handle(async () =>
        Ok(await events.GetUpcomingFeedEvents(filter, DateTime.UtcNow, ct)));
    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-entry")]
    public Task<IActionResult> Create(SaveEventFeedRequest request, CancellationToken ct) => Handle(async () => {
        var feed = await store.Save(UserId, null, request, ct);
        return CreatedAtAction(nameof(Get), new { id = feed!.Id }, feed);
    });
    [HttpPut("{id:guid}"), ValidateAntiForgeryToken, EnableRateLimiting("account-entry")]
    public Task<IActionResult> Update(Guid id, SaveEventFeedRequest request, CancellationToken ct) => Handle(async () => {
        var feed = await store.Save(UserId, id, request, ct); return feed == null ? NotFound() : Ok(feed);
    });
    [HttpDelete("{id:guid}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) => Handle(async () =>
        await store.Delete(UserId, id, ct) ? NoContent() : NotFound());
    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException ex) { return BadRequest(new { response = ex.Message }); }
        catch (FeedConflictException ex) { return Conflict(new { response = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError("Event feed request failed ({ErrorType}).", ex.GetType().Name);
            return Problem("Unable to process the event feed request.");
        }
    }
}

