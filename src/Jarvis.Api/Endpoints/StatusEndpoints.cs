using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Jarvis.Api.Data;
using Jarvis.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Jarvis.Api.Endpoints;

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // -------------------------------------------------------------------
        // POST /telemetry — write path.
        // Local demo: writes to Redis + SQL, publishes to SB only if configured.
        // In Azure this is usually fed by Service Bus, but exposing it as HTTP
        // is convenient for demos and integration tests.
        // -------------------------------------------------------------------
        app.MapPost("/telemetry", async (
            TelemetryEvent evt,
            IDistributedCache cache,
            JarvisDbContext db,
            ServiceBusSender? alertSender,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("Telemetry");

            // 1) Hot state → Redis
            var status = new SuitStatus(
                evt.SuitId, evt.PowerLevel, evt.CoreTempCelsius,
                evt.Timestamp, AlertActive: evt.PowerLevel < 20);

            await cache.SetStringAsync(
                $"status:{evt.SuitId}",
                JsonSerializer.Serialize(status),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                }, ct);

            // 2) History → SQL (or InMemory in dev without DB)
            db.Telemetry.Add(evt);
            await db.SaveChangesAsync(ct);

            // 3) Alert fan-out → Service Bus (best-effort)
            if (evt.PowerLevel < 20 && alertSender is not null)
            {
                try
                {
                    var alert = new AlertMessage(
                        evt.SuitId,
                        evt.PowerLevel < 10 ? "Critical" : "Warning",
                        $"Power low: {evt.PowerLevel:F1}%",
                        DateTimeOffset.UtcNow);

                    await alertSender.SendMessageAsync(
                        new ServiceBusMessage(BinaryData.FromObjectAsJson(alert))
                        {
                            Subject = "SuitAlert",
                            ContentType = "application/json"
                        }, ct);

                    log.LogWarning("Alert published for {SuitId}", evt.SuitId);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "SB publish skipped (not configured locally).");
                }
            }
            else if (evt.PowerLevel < 20)
            {
                log.LogInformation(
                    "Low-power event for {SuitId} but SB not configured; skipping publish.",
                    evt.SuitId);
            }

            return Results.Accepted($"/status/{evt.SuitId}", status);
        });

        // -------------------------------------------------------------------
        // GET /status/{suitId} — cache-first read. Demo money shot.
        // -------------------------------------------------------------------
        app.MapGet("/status/{suitId}", async (
            string suitId,
            IDistributedCache cache,
            JarvisDbContext db,
            CancellationToken ct) =>
        {
            var cached = await cache.GetStringAsync($"status:{suitId}", ct);
            if (cached is not null)
            {
                return Results.Ok(new
                {
                    source = "redis",
                    data   = JsonSerializer.Deserialize<SuitStatus>(cached)
                });
            }

            var latest = await db.Telemetry
                .Where(t => t.SuitId == suitId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync(ct);

            if (latest is null) return Results.NotFound();

            var status = new SuitStatus(
                latest.SuitId, latest.PowerLevel, latest.CoreTempCelsius,
                latest.Timestamp, AlertActive: latest.PowerLevel < 20);

            await cache.SetStringAsync(
                $"status:{suitId}",
                JsonSerializer.Serialize(status),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                }, ct);

            return Results.Ok(new { source = "sql", data = status });
        });

        // -------------------------------------------------------------------
        // GET /history/{suitId} — straight from SQL, bypasses cache.
        // -------------------------------------------------------------------
        app.MapGet("/history/{suitId}", async (
            string suitId, int? take, JarvisDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Telemetry
                .Where(t => t.SuitId == suitId)
                .OrderByDescending(t => t.Timestamp)
                .Take(take ?? 50)
                .ToListAsync(ct);

            return Results.Ok(rows);
        });

        return app;
    }
}