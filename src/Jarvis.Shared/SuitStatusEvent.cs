namespace Jarvis.Shared;

public class SuitStatusEvent
{
    public string EventId{get;set;} =Guid.NewGuid().ToString();
    public string SuitId { get; set; } = string.Empty;
    public string SuitModel{get;set;}=string.Empty;
    public int PowerLevel { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsInBattle{get;set;}
    public string ThreatLevel { get; set; } = "Low";
}