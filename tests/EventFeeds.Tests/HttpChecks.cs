using SengokuProvider.Library.Models.Feeds;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SengokuProvider.API.Authentication;
using SengokuProvider.API.Controllers;
using SengokuProvider.Library.Services.Feeds;
using SengokuProvider.Library.Services.Events;

static class HttpChecks
{
    public static async Task Run(string connection, IDataProtectionProvider protection, IEventFeedQueryService query, Guid feedId)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddAccountAuthentication(connection);
        builder.Services.AddSingleton(protection);
        var store = new EventFeedStore(connection,protection);
        builder.Services.AddSingleton<IEventFeedStore>(store);
        builder.Services.AddSingleton<IDiscordFeedConnectionStore>(store);
        builder.Services.AddSingleton<IDiscordFeedConnection,DiscordFeedConnection>();
        builder.Services.AddSingleton(query);
        builder.Services.AddAuthentication("FeedTest").AddScheme<AuthenticationSchemeOptions, TestAuth>("FeedTest",_=>{});
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(EventFeedsController).Assembly);
        await using var app=builder.Build();
        app.Use((context,next)=> { context.Request.Scheme="https"; return next(context); });
        app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers();
        app.MapGet("/test-csrf",(HttpContext context,IAntiforgery csrf)=>Results.Ok(new { token=csrf.GetAndStoreTokens(context).RequestToken }));
        await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient(new HttpClientHandler { UseCookies=false }) { BaseAddress=new Uri(address) };
            Check((await client.GetAsync("/api/event-feeds")).StatusCode==HttpStatusCode.Unauthorized,"anonymous requests rejected");
            client.DefaultRequestHeaders.Add("X-Test-User","2");
            Check((await client.GetAsync($"/api/event-feeds/{feedId}")).StatusCode==HttpStatusCode.NotFound,"foreign feed hidden over HTTP");
            client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User","1");
            Check((await client.GetAsync($"/api/event-feeds/{feedId}")).IsSuccessStatusCode,"owner reads feed");
            using var options = await client.GetAsync("/api/event-feeds/options");
            using var catalogue = JsonDocument.Parse(await options.Content.ReadAsStringAsync());
            Check(catalogue.RootElement.GetProperty("games").EnumerateArray().Any(g=>g.GetProperty("id").GetInt32()==222),"HTTP options uses live game catalogue");
            Check(catalogue.RootElement.GetProperty("schedules").EnumerateArray().Any(s=>s.GetProperty("id").GetString()=="OnInsert"),"HTTP options includes insert schedule");
            Check(!catalogue.RootElement.GetProperty("discordConnectionAvailable").GetBoolean(),"unconfigured Discord connection reported to UI");
            var request=new { name="HTTP Feed",filter=new { gameIds=new[]{1},regionIds=new[]{"southeast"} },schedule=new { mode="Interval",intervalHours=6 } };
            using var noCsrf = await client.PostAsJsonAsync("/api/event-feeds",request);
            Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,$"CSRF required on write (received {(int)noCsrf.StatusCode})");
            using var csrf=await client.GetAsync("/test-csrf");
            using var json=JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",json.RootElement.GetProperty("token").GetString());
            client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",csrf.Headers.GetValues("Set-Cookie").Select(value=>value.Split(';')[0])));
            using var created=await client.PostAsJsonAsync("/api/event-feeds",request);
            Check(created.StatusCode==HttpStatusCode.Created,"authenticated CSRF-protected create");
            var body=await created.Content.ReadAsStringAsync();
            Check(!body.Contains("webhook") && !body.Contains("abcdefghijklmnopqrstuvwxyz"),"response contains no destination secret");
            using var response=JsonDocument.Parse(body);
            var id=response.RootElement.GetProperty("id").GetGuid();
            Check((await client.DeleteAsync($"/api/event-feeds/{id}")).StatusCode==HttpStatusCode.NoContent,"authenticated delete");
            Console.WriteLine("PASS HTTP authentication, ownership, antiforgery, create/delete and secret redaction");
        }
        finally { await app.StopAsync(); }
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
public sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id=Request.Headers["X-Test-User"].ToString();
        if(id is not ("1" or "2")) return Task.FromResult(AuthenticateResult.NoResult());
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id)},Scheme.Name)),Scheme.Name)));
    }
}



