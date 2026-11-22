using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Settings;

public sealed record DeviceSettingsDto(
    string DeviceId,
    double PhMin,
    double PhMax,
    double TurbidityMaxNtu,
    double LevelMinPct,
    double SoilDryPct,
    double SoilWetPct,
    int IrrigationDurationS,
    int IrrigationCooldownS,
    int PumpMaxS,
    bool AutoIrrigation,
    bool IsDefault,
    DateTimeOffset? UpdatedAt)
{
    public static DeviceSettingsDto From(DeviceSettings s) => new(
        s.DeviceId, s.PhMin, s.PhMax, s.TurbidityMaxNtu, s.LevelMinPct, s.SoilDryPct, s.SoilWetPct,
        s.IrrigationDurationS, s.IrrigationCooldownS, s.PumpMaxS, s.AutoIrrigation,
        IsDefault: s.UpdatedAt is null, s.UpdatedAt);
}
