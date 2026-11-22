namespace RainHarvest.Api.Domain;

/// <summary>
/// One telemetry sample. Stored in the <c>readings</c> TimescaleDB hypertable,
/// partitioned by <see cref="ReceivedAt"/>; the key is (DeviceId, ReceivedAt).
/// </summary>
public sealed class Reading
{
    public required string DeviceId { get; init; }

    /// <summary>Server receive time (UTC). The authoritative timestamp.</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Timestamp reported by the device; its clock may drift.</summary>
    public DateTimeOffset? DeviceTs { get; init; }

    public double LevelPct { get; init; }

    public double LevelCm { get; init; }

    public double TurbidityNtu { get; init; }

    public double Ph { get; init; }

    /// <summary>Optional DS18B20 sensor.</summary>
    public double? WaterTempC { get; init; }

    public bool Raining { get; init; }

    public double SoilPct { get; init; }

    public double FlowLpm { get; init; }

    public double FlowTotalL { get; init; }

    public int Rssi { get; init; }
}
