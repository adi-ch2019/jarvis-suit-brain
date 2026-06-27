namespace Jarvis.Shared;

public class SuitStatusEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string SuitId { get; set; } = string.Empty;      // "Mark85", "Mark42"
    public string SuitModel { get; set; } = string.Empty;   // "Mark LXXXV"
    public int PowerLevel { get; set; }                     // 0-100
    public string Status { get; set; } = string.Empty;      // "Active", "Damaged"
    public string Location { get; set; } = string.Empty;    // "AvengersTower"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsInBattle { get; set; }
    public string ThreatLevel { get; set; } = "Low";        // "Low", "High", "Thanos"
}