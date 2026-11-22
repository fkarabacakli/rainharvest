namespace RainHarvest.Api.Domain;

public enum CommandSource
{
    /// <summary>A user pressed a button in the panel.</summary>
    Manual,

    /// <summary>The backend's irrigation rule decided.</summary>
    Rule,
}

public enum CommandStatus
{
    /// <summary>Published to MQTT; no answer from the device yet.</summary>
    Sent,

    /// <summary>Device started executing it.</summary>
    Accepted,

    /// <summary>Device refused; see <see cref="DeviceCommand.StatusReason"/>.</summary>
    Rejected,

    /// <summary>Timed command finished (or was stopped).</summary>
    Completed,

    /// <summary>Device never answered within the timeout.</summary>
    Expired,
}

/// <summary>A command sent to a device and what became of it.</summary>
public sealed class DeviceCommand
{
    public Guid Id { get; init; }

    public required string DeviceId { get; init; }

    /// <summary>One of <c>RainHarvest.Contracts.CommandActions</c>.</summary>
    public required string Action { get; init; }

    public int? DurationS { get; init; }

    public CommandSource Source { get; init; }

    /// <summary>User who sent a manual command.</summary>
    public Guid? RequestedBy { get; init; }

    /// <summary>Why the rule issued it, e.g. "soil 22% < 30%".</summary>
    public string? Reason { get; init; }

    public CommandStatus Status { get; set; }

    /// <summary>Device's reject reason, or "timeout".</summary>
    public string? StatusReason { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}
