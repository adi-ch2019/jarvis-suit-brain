using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Azure.Messaging.ServiceBus;
using System.Text.Json;
using Jarvis.Shared;
using Jarvis.SuitTelemetryApi.Data;

namespace Jarvis.SuitTelemetryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuitController : ControllerBase
{
    private readonly SuitDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ServiceBusSender? _serviceBusSender;
    private readonly ILogger<SuitController> _logger;

    public SuitController(
        SuitDbContext db,
        IDistributedCache cache,
        ServiceBusClient? serviceBusClient,
        ILogger<SuitController> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _serviceBusSender = serviceBusClient?.CreateSender("suit-events");
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateSuitStatus([FromBody] SuitStatusEvent suitEvent)
    {
        try
        {
            // 1. Store in Redis cache (Fast access for Tony's HUD)
            var cacheKey = $"suit:{suitEvent.SuitId}:status";
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(suitEvent), 
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

            // 2. Store in SQL (Sokovia Accords compliance)
            await _db.SuitTelemetry.AddAsync(suitEvent);
            await _db.SaveChangesAsync();

            // 3. Send to Service Bus (JARVIS async processing)
            if (_serviceBusSender != null)
            {
                var message = new ServiceBusMessage(JsonSerializer.Serialize(suitEvent))
                {
                    MessageId = suitEvent.EventId,
                    Subject = "SuitStatusUpdated"
                };
                await _serviceBusSender.SendMessageAsync(message);
            }

            _logger.LogInformation("JARVIS recorded {SuitId}: Power={PowerLevel}%", 
                suitEvent.SuitId, suitEvent.PowerLevel);
            
            return Ok(new { 
                message = $"✅ JARVIS recorded {suitEvent.SuitId}", 
                eventId = suitEvent.EventId 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JARVIS error for {SuitId}", suitEvent.SuitId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{suitId}/status")]
    public async Task<IActionResult> GetSuitStatus(string suitId)
    {
        var cacheKey = $"suit:{suitId}:status";
        var cached = await _cache.GetStringAsync(cacheKey);
        
        if (cached != null)
            return Ok(JsonSerializer.Deserialize<SuitStatusEvent>(cached));
        
        var latest = await _db.SuitTelemetry
            .Where(s => s.SuitId == suitId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();
            
        return latest == null ? NotFound() : Ok(latest);
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllSuits()
    {
        var suits = await _db.SuitTelemetry
            .GroupBy(s => s.SuitId)
            .Select(g => g.OrderByDescending(x => x.Timestamp).First())
            .Take(100)
            .ToListAsync();
            
        return Ok(new { totalSuits = suits.Count, suits });
    }
}