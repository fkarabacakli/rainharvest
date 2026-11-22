namespace RainHarvest.Api.Features.Commands;

/// <summary>Raised when the device reports its pump/valve state.</summary>
public sealed record ActuatorStateChanged(string DeviceId, bool PumpOn, bool ValveOpen, bool Irrigating, DateTimeOffset At);

/// <summary>Raised when a command is created or its status changes.</summary>
public sealed record CommandUpdated(CommandDto Command);
