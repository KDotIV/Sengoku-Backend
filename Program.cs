using SengokuProvider.API.Services.Common;

var builder = WebApplication.CreateBuilder(args);

//constant config variables
var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString");
// Add services to the container.
builder.Services.AddScoped<ICommonDatabaseService, CommonDatabaseService>(provider =>
{
    return new CommonDatabaseService(connectionString);
});

builder.Services.AddTransient<CommandProcessor>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
