using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Jarvis.Api.Consumers;
using Jarvis.Api.Data;
using Jarvis.Api.Endpoints;
using Jarvis.Shared;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 12-Factor: config from environment. In Container Apps, values come from
// Key Vault references (wired by Terraform). Locally they come from
// appsettings.Development.json. No secrets in code, ever.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// BACKING SERVICE #1 — Distributed Cache (Redis)
// ---------------------------------------------------------------------------
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Connection"];
    options.InstanceName = "jarvis:";
});

// Raw multiplexer for advanced ops (pub/sub, atomic counters) if needed later.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var conn = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(conn);
});

// ---------------------------------------------------------------------------
// BACKING SERVICE #2 — Relational DB (Azure SQL / SQL Edge locally)
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<JarvisDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration["Sql:Connection"],
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
});

// ---------------------------------------------------------------------------
// BACKING SERVICE #3 — Message Broker (Azure Service Bus)
// Passwordless in Azure via Managed Identity; connection string only locally.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(sp =>
{
    var ns = builder.Configuration["ServiceBus:Namespace"];
    var conn = builder.Configuration["ServiceBus:Connection"];

    return string.IsNullOrWhiteSpace(conn)
        ? new ServiceBusClient(ns!, new DefaultAzureCredential())   // Azure path
        : new ServiceBusClient(conn);                               // local path
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<ServiceBusClient>();
    return client.CreateSender(builder.Configuration["ServiceBus:TopicName"]!);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<ServiceBusClient>();
    return client.CreateProcessor(
        builder.Configuration["ServiceBus:QueueName"]!,
        new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
            PrefetchCount = 10
        });
});

// Hosted consumer (BackgroundService) that drains the telemetry queue.
builder.Services.AddHostedService<TelemetryConsumer>();

// Health checks — required by Container Apps liveness/readiness probes.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JarvisDbContext>("sql")
    .AddRedis(builder.Configuration["Redis:Connection"]!);

// OpenAPI (nice for demo, zero exam relevance).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Auto-migrate on startup (dev only). In prod, run migrations as a separate
// step in the CD pipeline so pods stay immutable.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
    db.Database.EnsureCreated();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

// ---------------------------------------------------------------------------
// Minimal API endpoints — the "suit brain" HTTP surface
// ---------------------------------------------------------------------------
app.MapStatusEndpoints();

app.Run();