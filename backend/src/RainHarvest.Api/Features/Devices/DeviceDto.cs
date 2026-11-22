using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Devices;

public sealed record DeviceDto(
    string Id,
    string Name,
    Guid? SiteId,
    bool IsOnline,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt)
{
    public static DeviceDto From(Device d) => new(d.Id, d.Name, d.SiteId, d.IsOnline, d.LastSeenAt, d.CreatedAt);
}
