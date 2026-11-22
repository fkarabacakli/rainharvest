namespace RainHarvest.Api.Domain;

public enum AlertType
{
    /// <summary>pH or turbidity outside the device's settings; irrigation is held back.</summary>
    WaterQualityBad,

    /// <summary>Tank below the minimum level; the pump will not run.</summary>
    TankLow,

    /// <summary>The device went offline (Last Will or a clean "offline").</summary>
    DeviceOffline,
}

public enum AlertSeverity
{
    Warning,
    Critical,
}

/// <summary>
/// A problem on a device. At most one unresolved alert exists per device and
/// type (enforced by a partial unique index), so a condition that persists for
/// hours is one alert, not one per reading. Alerts resolve themselves when the
/// condition clears; acknowledging only records that someone has seen it.
/// </summary>
public sealed class Alert
{
    public Guid Id { get; init; }

    public required string DeviceId { get; init; }

    public AlertType Type { get; init; }

    public AlertSeverity Severity { get; init; }

    public required string Message { get; set; }

    public DateTimeOffset RaisedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public Guid? AcknowledgedBy { get; set; }
}
