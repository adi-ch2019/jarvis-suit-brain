namespace Jarvis.Shared;

public record TelemetryEvent(
    string SuitId,
    double PowerLevel,
    double CoreTempCelsius,
    DateTimeOffset Timestamp,
    string? Threat = null);

public record SuitStatus(
    string SuitId,
    double PowerLevel,
    double CoreTempCelsius,
    DateTimeOffset LastSeen,
    bool AlertActive);

public record AlertMessage(
    string SuitId,
    string Severity,
    string Reason,
    DateTimeOffset RaisedAt);