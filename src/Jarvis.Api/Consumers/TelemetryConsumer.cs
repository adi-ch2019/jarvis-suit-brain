using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Jarvis.Api.Data;
using Jarvis.Shared;
using Microsoft.Extensions.Caching.Distributed;

namespace Jarvis.Api.Consumers;

/// <summary>
/// BackgroundService that drains the telemetry queue and fans out to
/// Redis (hot cache) + SQL (history) + Service Bus topic (alerts).
/// This single class IS the AZ-400 "backing services" story.
/// </summary>
public class TelemetryConsumer(
    ServiceBusProcessor processor,
    IDistributedCache cache,
    IServiceScopeFactory scopeFactory,
    ServiceBusSender alertSender,
    ILogger<TelemetryConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("TelemetryConsumer started.");

        // Keep alive until host shuts down.
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* normal shutdown */ }

        await processor.StopProcessingAsync(CancellationToken.None);
        logger.LogInformation("TelemetryConsumer stopped.");
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        TelemetryEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<TelemetryEvent>(body);
            if (evt is null) throw new JsonException("null payload");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Poison message — dead-lettering.");
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "InvalidJson", ex.Message);
            return;
        }

        logger.LogInformation("Telemetry received: {SuitId} power={Power}",
            evt.SuitId, evt.PowerLevel);

        // --- Redis: hot state (sub-10ms reads for /status) ---
        var status = new SuitStatus(
            evt.SuitId, evt.PowerLevel, evt.CoreTempCelsius,
            evt.Timestamp, AlertActive: evt.PowerLevel < 20);

        await cache.SetStringAsync(
            $"status:{evt.SuitId}",
            JsonSerializer.Serialize(status),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

        // --- SQL: durable history (scoped DbContext) ---
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
            db.Telemetry.Add(evt);
            await db.SaveChangesAsync(args.CancellationToken);
        }

        // --- Service Bus topic: alert fan-out ---
        if (evt.PowerLevel < 20)
        {
            var alert = new AlertMessage(
                evt.SuitId,
                Severity: evt.PowerLevel < 10 ? "Critical" : "Warning",
                Reason: $"Power low: {evt.PowerLevel:F1}%",
                RaisedAt: DateTimeOffset.UtcNow);

            await alertSender.SendMessageAsync(new ServiceBusMessage(
                BinaryData.FromObjectAsJson(alert))
            {
                Subject = "SuitAlert",
                ContentType = "application/json"
            }, args.CancellationToken);

            logger.LogWarning("Alert published for {SuitId}", evt.SuitId);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Service Bus error. Source={Source}", args.ErrorSource);
        return Task.CompletedTask;
    }

   
}