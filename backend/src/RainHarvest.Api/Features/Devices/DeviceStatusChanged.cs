namespace RainHarvest.Api.Features.Devices;

/// <summary>Raised when a device reports "online" or "offline" (including its Last Will).</summary>
public sealed record DeviceStatusChanged(string DeviceId, bool IsOnline, DateTimeOffset At);
