using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Jarvis.Api.Consumers;
using Jarvis.Api.Data;
using Jarvis.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 12-Factor config: env vars in Azure (wired by Terraform + Key Vault),
// appsettings.Development.json locally. No secrets in source.
// ---------------------------------------------------------------------------

// ===========================================================================
// BACKING SERVICE #1 — Distributed Cache (Redis)
// ===========================================================================
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
    options.InstanceName = "jarvis:";
});

// Raw multiplexer for advanced ops — lazy-connect so startup doesn't crash
// if Redis is temporarily down.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var conn = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
    var options = ConfigurationOptions.Parse(conn);
    options.AbortOnConnectFail = false;   // don't kill the app if Redis is unreachable
    options.ConnectRetry = 3;
    return ConnectionMultiplexer.Connect(options);
});

// ===========================================================================
// BACKING SERVICE #2 — Relational DB (Azure SQL / SQL Edge locally)
// ===========================================================================
var sqlConn = builder.Configuration["Sql:Connection"];
if (!string.IsNullOrWhiteSpace(sqlConn))
{
    builder.Services.AddDbContext<JarvisDbContext>(options =>
    {
        options.UseSqlServer(sqlConn, sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
    });
}
else
{
    // Local dev with no DB: use InMemory so endpoints still respond.
    builder.Services.AddDbContext<JarvisDbContext>(options =>
        options.UseInMemoryDatabase("jarvis-dev"));
}

// ===========================================================================
// BACKING SERVICE #3 — Message Broker (Azure Service Bus)
// Registered ONLY if configured. Local dev without SB boots cleanly.
// Azure path uses DefaultAzureCredential (Managed Identity / passwordless).
// ===========================================================================
var sbConn      = builder.Configuration["ServiceBus:Connection"];
var sbNamespace = builder.Configuration["ServiceBus:Namespace"];
var sbConfigured = !string.IsNullOrWhiteSpace(sbConn)
                || !string.IsNullOrWhiteSpace(sbNamespace);

if (sbConfigured)
{
    builder.Services.AddSingleton(sp =>
    {
        return string.IsNullOrWhiteSpace(sbConn)
            ? new ServiceBusClient(sbNamespace!, new DefaultAzureCredential())
            : new ServiceBusClient(sbConn);
    });

    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<ServiceBusClient>()
          .CreateSender(builder.Configuration["ServiceBus:TopicName"]!));

    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<ServiceBusClient>()
          .CreateProcessor(
              builder.Configuration["ServiceBus:QueueName"]!,
              new ServiceBusProcessorOptions
              {
                  AutoCompleteMessages = false,
                  MaxConcurrentCalls   = 4,
                  PrefetchCount        = 10
              }));

  //  builder.Services.AddHostedService<TelemetryConsumer>();
}
else
{
    // Register a null sender so the POST /telemetry endpoint can inject it
    // and skip the publish step. Keeps DI graph resolvable.
    builder.Services.AddSingleton<ServiceBusSender>(_ => null!);
}

// ===========================================================================
// Health checks — simple by default (no extra packages needed).
// Re-add AddDbContextCheck / AddRedis once you verify those packages resolve.
// ===========================================================================
builder.Services.AddHealthChecks();

// ===========================================================================
// OpenAPI + Scalar UI (modern replacement for Swagger UI)
// ===========================================================================
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Dev-only: create schema, enable interactive API explorer.
// In prod, EF migrations run in the CD pipeline so pods stay immutable.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    if (!string.IsNullOrWhiteSpace(sqlConn))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
        db.Database.EnsureCreated();
    }

    app.MapOpenApi();                    // /openapi/v1.json
    app.MapScalarApiReference();         // /scalar/v1
}

// ---------------------------------------------------------------------------
// Probes for Container Apps liveness/readiness
// ---------------------------------------------------------------------------
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

// ---------------------------------------------------------------------------
// Minimal API surface
// ---------------------------------------------------------------------------
app.MapStatusEndpoints();

app.Run();