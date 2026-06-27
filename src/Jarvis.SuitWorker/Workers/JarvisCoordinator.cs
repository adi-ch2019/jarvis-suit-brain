using Azure.Messaging.ServiceBus;
using System.Text.Json;
using Jarvis.Shared;

namespace Jarvis.SuitWorker.Workers;

public class JarvisCoordinator : BackgroundService
{
    private readonly ServiceBusProcessor? _processor;
    private readonly ILogger<JarvisCoordinator> _logger;

    public JarvisCoordinator(IConfiguration configuration, ILogger<JarvisCoordinator> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("ServiceBus");

        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("⚠️ JARVIS: Service Bus disabled - simulation mode");
            _processor = null;
            return;
        }

        var client = new ServiceBusClient(connectionString);
        _processor = client.CreateProcessor("suit-events", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_processor == null)
        {
            // Simulation mode
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("🦾 JARVIS online (simulation)");
                await Task.Delay(30000, stoppingToken);
            }
            return;
        }

        _processor.ProcessMessageAsync += ProcessSuitEventAsync;
        _processor.ProcessErrorAsync += ErrorHandlerAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("🦾 JARVIS Coordinator online - consuming Service Bus messages");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessSuitEventAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var suitEvent = JsonSerializer.Deserialize<SuitStatusEvent>(body);

        if (suitEvent == null)
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        _logger.LogInformation("📡 JARVIS: {SuitId} - Power={PowerLevel}%, Status={Status}, Threat={ThreatLevel}",
            suitEvent.SuitId, suitEvent.PowerLevel, suitEvent.Status, suitEvent.ThreatLevel);

        // JARVIS Intelligence Logic
        switch (suitEvent.ThreatLevel)
        {
            case "Thanos":
                _logger.LogCritical("💀 THANOS DETECTED! Activating House Party Protocol!");
                break;
            case "High" when suitEvent.PowerLevel < 20:
                _logger.LogCritical("⚠️ {SuitId} damaged in battle! Dispatching backup!", suitEvent.SuitId);
                break;
            case "Low" when suitEvent.PowerLevel > 80:
                _logger.LogInformation("✅ {SuitId} operating at optimal capacity", suitEvent.SuitId);
                break;
            default:
                _logger.LogInformation("✅ {SuitId} nominal", suitEvent.SuitId);
                break;
        }

        await args.CompleteMessageAsync(args.Message);
    }

    private Task ErrorHandlerAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "JARVIS error");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(stoppingToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(stoppingToken);
    }
}