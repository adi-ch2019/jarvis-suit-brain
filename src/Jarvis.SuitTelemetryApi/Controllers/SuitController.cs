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
    private readonly IDistributedCache _cache;          // Backing Service #1: Redis
    private readonly ServiceBusSender _serviceBusSender; // Backing Service #2: Service Bus
    private readonly ILogger<SuitController> _logger;

    public SuitController(
        SuitDbContext db,
        IDistributedCache cache,
        ServiceBusSender serviceBusSender,
        ILogger<SuitController> logger)
    {
        _db = db;
        _cache = cache;
        _serviceBusSender = serviceBusSender;
        _logger = logger;
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateSuitStatus([FromBody] SuitStatusEvent suitEvent)
    {
        try
        {
            // 1. Store in Redis Cache (Fast access - Backing Service #1)
            var cacheKey = $"suit:{suitEvent.SuitId}:status";
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(suitEvent),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            // 2. Store in SQL Database (History - Backing Service #2)
            await _db.SuitTelemetry.AddAsync(suitEvent);
            await _db.SaveChangesAsync();

            // 3. Send to Service Bus (Async processing - Backing Service #3)
            var message = new ServiceBusMessage(JsonSerializer.Serialize(suitEvent))
            {
                MessageId = suitEvent.EventId,
                Subject = "SuitStatusUpdated",
                ContentType = "application/json"
            };
            await _serviceBusSender.SendMessageAsync(message);

            _logger.LogInformation("✅ JARVIS: {SuitId} - Power={PowerLevel}%, Cache+SQL+ServiceBus", 
                suitEvent.SuitId, suitEvent.PowerLevel);

            return Ok(new
            {
                message = $"✅ JARVIS recorded {suitEvent.SuitId}",
                eventId = suitEvent.EventId,
                timestamp = suitEvent.Timestamp,
                services = new { cache = true, database = true, servicebus = true }
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
        // First check Redis (Fast)
        var cacheKey = $"suit:{suitId}:status";
        var cached = await _cache.GetStringAsync(cacheKey);
        
        if (!string.IsNullOrEmpty(cached))
        {
            var suitEvent = JsonSerializer.Deserialize<SuitStatusEvent>(cached);
            return Ok(new { source = "Redis Cache", data = suitEvent });
        }
        
        // Fallback to SQL
        var latest = await _db.SuitTelemetry
            .Where(s => s.SuitId == suitId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();
            
        if (latest == null)
            return NotFound(new { error = $"No suit data found for {suitId}" });
            
        return Ok(new { source = "SQL Database", data = latest });
    }
}