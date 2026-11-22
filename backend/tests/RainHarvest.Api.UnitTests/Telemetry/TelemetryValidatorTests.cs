using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Contracts;

namespace RainHarvest.Api.UnitTests.Telemetry;

public sealed class TelemetryValidatorTests
{
    private static readonly TelemetryPayload Valid = new(
        LevelPct: 64.2, LevelCm: 38.5, TurbidityNtu: 12, Ph: 6.9, Raining: true,
        SoilPct: 31, FlowLpm: 0, FlowTotalL: 142.7, Rssi: -61, Ts: 1763800000, WaterTempC: 18.4);

    [Fact]
    public void Accepts_a_realistic_reading() => Assert.Empty(TelemetryValidator.Validate(Valid));

    [Fact]
    public void Accepts_missing_optional_temperature() =>
        Assert.Empty(TelemetryValidator.Validate(Valid with { WaterTempC = null }));

    public static TheoryData<TelemetryPayload, string> OutOfRange => new()
    {
        { Valid with { LevelPct = 100.1 }, "LevelPct" },
        { Valid with { LevelPct = -1 }, "LevelPct" },
        { Valid with { Ph = 14.5 }, "Ph" },
        { Valid with { TurbidityNtu = -0.1 }, "TurbidityNtu" },
        { Valid with { SoilPct = 120 }, "SoilPct" },
        { Valid with { FlowLpm = -2 }, "FlowLpm" },
        { Valid with { Rssi = 5 }, "Rssi" },
        { Valid with { WaterTempC = 150 }, "WaterTempC" },
        { Valid with { Ph = double.NaN }, "Ph" },
    };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void Rejects_out_of_range_values(TelemetryPayload payload, string field)
    {
        var errors = TelemetryValidator.Validate(payload);

        var error = Assert.Single(errors);
        Assert.StartsWith(field + "=", error);
    }
}
