using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Commands;

public sealed record CommandDto(
    Guid Id,
    string DeviceId,
    string Action,
    int? DurationS,
    CommandSource Source,
    Guid? RequestedBy,
    string? Reason,
    CommandStatus Status,
    string? StatusReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CommandDto From(DeviceCommand c) => new(
        c.Id, c.DeviceId, c.Action, c.DurationS, c.Source, c.RequestedBy, c.Reason,
        c.Status, c.StatusReason, c.CreatedAt, c.UpdatedAt);
}
