using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>Raised after a valid reading has been stored.</summary>
public sealed record TelemetryReceived(Reading Reading);
