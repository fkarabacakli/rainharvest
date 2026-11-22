using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Devices;

public sealed record DeviceDto(
    string Id,
    string Name,
    Guid? SiteId,
    bool IsOnline,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt,
    ActuatorStateDto Actuators)
{
    public static DeviceDto From(Device d) => new(
        d.Id, d.Name, d.SiteId, d.IsOnline, d.LastSeenAt, d.CreatedAt,
        new ActuatorStateDto(d.PumpOn, d.ValveOpen, d.Irrigating, d.StateReportedAt));
}

/// <summary>Pump/valve state as last reported by the device; ReportedAt is null until it reports.</summary>
public sealed record ActuatorStateDto(bool PumpOn, bool ValveOpen, bool Irrigating, DateTimeOffset? ReportedAt);
