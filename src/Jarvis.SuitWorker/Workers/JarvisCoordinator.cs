using Azure.Messaging.ServiceBus;
using System.Text.Json;
using Jarvis.Shared;

namespace Jarvis.SuitWorker.Workers;

public class JarvisCoordinator : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<JarvisCoordinator> _logger;

    public JarvisCoordinator(IConfiguration configuration, ILogger<JarvisCoordinator> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("ServiceBus");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("JARVIS: Service Bus disabled - running in simulation mode");
            _processor = null!;
            return;
        }
        
        var client = new ServiceBusClient(connectionString);
        _processor = client.CreateProcessor("suit-events");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_processor == null)
        {
            // Simulation mode - just log
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("🦾 JARVIS online - monitoring suits (simulation mode)");
                await Task.Delay(30000, stoppingToken);
            }
            return;
        }
        
        _processor.ProcessMessageAsync += ProcessSuitEventAsync;
        _processor.ProcessErrorAsync += ErrorHandlerAsync;
        
        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("🦾 JARVIS Coordinator online - monitoring all suits");
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessSuitEventAsync(ProcessMessageEventArgs args)
    {
        var suitEvent = JsonSerializer.Deserialize<SuitStatusEvent>(args.Message.Body.ToString());
        
        if (suitEvent == null) return;
        
        _logger.LogInformation("📡 JARVIS: {SuitId} - Power={PowerLevel}%, Status={Status}", 
            suitEvent.SuitId, suitEvent.PowerLevel, suitEvent.Status);
        
        // JARVIS Intelligence
        if (suitEvent.PowerLevel < 20)
        {
            _logger.LogCritical("⚠️ JARVIS: {SuitId} power critical! Alerting Tony", suitEvent.SuitId);
        }
        
        if (suitEvent.Status == "Damaged" && suitEvent.ThreatLevel == "High")
        {
            _logger.LogWarning("🛡️ JARVIS: Dispatching backup for {SuitId}", suitEvent.SuitId);
        }
        
        if (suitEvent.ThreatLevel == "Thanos")
        {
            _logger.LogCritical("💀 THANOS LEVEL THREAT - Activating all suits!");
        }
        
        await args.CompleteMessageAsync(args.Message);
    }

    private Task ErrorHandlerAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "JARVIS error");
        return Task.CompletedTask;
    }
}