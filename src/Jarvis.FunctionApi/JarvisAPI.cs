using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;
using Jarvis.Shared;

namespace Jarvis.FunctionApi;

public static class JarvisAPI
{
    [FunctionName("SuitStatus")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "suit/status")] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("🦾 JARVIS processing suit status");

        string requestBody = await req.ReadAsStringAsync();
        var suitEvent = JsonSerializer.Deserialize<SuitStatusEvent>(requestBody);
        
        if (suitEvent == null)
        {
            return new BadRequestObjectResult("Invalid suit data");
        }

        // Store in Redis
        var redis = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("RedisConnection"));
        var db = redis.GetDatabase();
        await db.StringSetAsync($"suit:{suitEvent.SuitId}:status", JsonSerializer.Serialize(suitEvent));

        // Store in SQL (via SQL connection)
        // ... (same logic as before)

        log.LogInformation($"✅ JARVIS recorded {suitEvent.SuitId} - Power: {suitEvent.PowerLevel}%");

        return new OkObjectResult(new { 
            message = $"JARVIS recorded {suitEvent.SuitId}",
            eventId = suitEvent.EventId
        });
    }
}