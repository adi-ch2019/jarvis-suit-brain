using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Jarvis.Shared;

namespace Jarvis.Worker;

/// <summary>
/// Subscribes to the alerts topic and "reacts" to suit emergencies.
/// In a real system this would call Teams/PagerDuty/push notifications.
/// </summary>
public class AlertPublisher(
    ServiceBusProcessor processor,
    ILogger<AlertPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        processor.ProcessMessageAsync += HandleAsync;
        processor.ProcessErrorAsync += err =>
        {
            logger.LogError(err.Exception,
                "Worker Service Bus error. Source={Source}", err.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("AlertPublisher started.");

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task HandleAsync(ProcessMessageEventArgs args)
    {
        var alert = JsonSerializer.Deserialize<AlertMessage>(
            args.Message.Body.ToString());

        if (alert is not null)
        {
            logger.LogWarning(
                "[ALERT] {Severity} — {SuitId}: {Reason} @ {RaisedAt}",
                alert.Severity, alert.SuitId, alert.Reason, alert.RaisedAt);

            // TODO: hook Microsoft Teams / Twilio / Push here.
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

   
}