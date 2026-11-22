using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Telemetry;

public sealed record ReadingDto(
    DateTimeOffset ReceivedAt,
    DateTimeOffset? DeviceTs,
    double LevelPct,
    double LevelCm,
    double TurbidityNtu,
    double Ph,
    double? WaterTempC,
    bool Raining,
    double SoilPct,
    double FlowLpm,
    double FlowTotalL,
    int Rssi)
{
    public static ReadingDto From(Reading r) => new(
        r.ReceivedAt, r.DeviceTs, r.LevelPct, r.LevelCm, r.TurbidityNtu, r.Ph, r.WaterTempC,
        r.Raining, r.SoilPct, r.FlowLpm, r.FlowTotalL, r.Rssi);
}
