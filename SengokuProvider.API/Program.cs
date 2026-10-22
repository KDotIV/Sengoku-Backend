using SengokuDispatcher.Library.Configurations;
using SengokuProvider.Library.Configurations;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString")
    ?? throw new InvalidOperationException("Alexandria database connection is required.");
builder.AddAccountAuthentication(connectionString);
builder.AddDispatcher(backgroundHost: false);
// Provider manages feed definitions/subscriptions; Dispatcher alone performs delivery.
builder.AddEventFeeds(backgroundHost: false);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AccountFrontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Run();
