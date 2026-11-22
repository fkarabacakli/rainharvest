using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Alerts;

public sealed record AlertDto(
    Guid Id,
    string DeviceId,
    AlertType Type,
    AlertSeverity Severity,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedBy)
{
    public static AlertDto From(Alert a) => new(
        a.Id, a.DeviceId, a.Type, a.Severity, a.Message, a.RaisedAt, a.ResolvedAt, a.AcknowledgedAt, a.AcknowledgedBy);
}

public enum AlertChangeKind
{
    Raised,
    Resolved,
    Acknowledged,
}

/// <summary>Raised whenever an alert is created, resolved or acknowledged.</summary>
public sealed record AlertChanged(AlertDto Alert, AlertChangeKind Kind);
