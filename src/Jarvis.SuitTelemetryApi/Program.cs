using Microsoft.EntityFrameworkCore;
using Azure.Messaging.ServiceBus;
using Jarvis.SuitTelemetryApi.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HealthChecks.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "JARVIS Suit Telemetry API",
        Version = "v1",
        Description = "Iron Man Suit Management System with 3 Backing Services"
    });
});

// ===== BACKING SERVICE #1: SQL Database =====
var sqlConnection = builder.Configuration.GetConnectionString("SuitDatabase")
    ?? throw new InvalidOperationException("SQL connection required");
builder.Services.AddDbContext<SuitDbContext>(options =>
    options.UseSqlServer(sqlConnection));

// ===== BACKING SERVICE #2: Redis Cache =====
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "JarvisCache:";
});

// ===== BACKING SERVICE #3: Service Bus =====
var serviceBusConnection = builder.Configuration.GetConnectionString("ServiceBus");
if (!string.IsNullOrEmpty(serviceBusConnection))
{
    builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusConnection));
    builder.Services.AddSingleton(p => p.GetRequiredService<ServiceBusClient>().CreateSender("suit-events"));
    Console.WriteLine("✅ JARVIS: All 3 Backing Services enabled");
}
else
{
    // Local development fallback: register a factory that returns null so the Func overload is chosen
    builder.Services.AddSingleton(typeof(ServiceBusSender), _ => null!);
    Console.WriteLine("⚠️ JARVIS: Running with SQL+Redis only (Service Bus disabled)");
}

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SuitDbContext>("SQL Database")
    .AddRedis(redisConnection, name: "Redis Cache");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AvengersTower", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure SQL database exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SuitDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AvengersTower");
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();