using System.Text.Json;
using Jarvis.Api.Data;
using Jarvis.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Jarvis.Api.Endpoints;

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // Cache-first read. Demonstrates distributed caching for AZ-400.
        app.MapGet("/status/{suitId}", async (
            string suitId,
            IDistributedCache cache,
            JarvisDbContext db,
            CancellationToken ct) =>
        {
            var cached = await cache.GetStringAsync($"status:{suitId}", ct);
            if (cached is not null)
                return Results.Ok(new { source = "redis",
                    data = JsonSerializer.Deserialize<SuitStatus>(cached) });

            var latest = await db.Telemetry
                .Where(t => t.SuitId == suitId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync(ct);

            if (latest is null) return Results.NotFound();

            var status = new SuitStatus(latest.SuitId, latest.PowerLevel,
                latest.CoreTempCelsius, latest.Timestamp,
                AlertActive: latest.PowerLevel < 20);

            await cache.SetStringAsync($"status:{suitId}",
                JsonSerializer.Serialize(status),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                }, ct);

            return Results.Ok(new { source = "sql", data = status });
        });

        // History read straight from SQL (bypasses cache).
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