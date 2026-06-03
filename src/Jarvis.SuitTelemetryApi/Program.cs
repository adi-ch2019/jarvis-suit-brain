using Microsoft.EntityFrameworkCore;
using Azure.Messaging.ServiceBus;
using Jarvis.SuitTelemetryApi.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 1. SQL Database
var sqlConnection = builder.Configuration.GetConnectionString("SuitDatabase") 
    ?? throw new InvalidOperationException("SQL connection required");
builder.Services.AddDbContext<SuitDbContext>(options =>
    options.UseSqlServer(sqlConnection));

// 2. Redis Cache
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "JarvisCache:";
});

// 3. Service Bus (Optional)
var serviceBusConnection = builder.Configuration.GetConnectionString("ServiceBus");
if (!string.IsNullOrEmpty(serviceBusConnection))
{
    builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusConnection));
    Console.WriteLine("✅ JARVIS: Service Bus enabled");
}
else
{
    builder.Services.AddSingleton<ServiceBusClient>(_ => null!);
    Console.WriteLine("⚠️ JARVIS: Service Bus disabled (local mode)");
}

// 4. Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SuitDbContext>("SuitDatabase");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AvengersTower", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database exists
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