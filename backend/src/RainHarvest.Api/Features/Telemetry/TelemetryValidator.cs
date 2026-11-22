using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>
/// Physical-range checks. A reading outside these bounds means a wiring,
/// calibration or firmware fault; storing it would corrupt averages and rules.
/// </summary>
public static class TelemetryValidator
{
    public static IReadOnlyList<string> Validate(TelemetryPayload p)
    {
        var errors = new List<string>();

        Check(errors, nameof(p.LevelPct), p.LevelPct, 0, 100);
        Check(errors, nameof(p.LevelCm), p.LevelCm, 0, 1000);
        Check(errors, nameof(p.TurbidityNtu), p.TurbidityNtu, 0, 5000);
        Check(errors, nameof(p.Ph), p.Ph, 0, 14);
        Check(errors, nameof(p.SoilPct), p.SoilPct, 0, 100);
        Check(errors, nameof(p.FlowLpm), p.FlowLpm, 0, 100);
        Check(errors, nameof(p.FlowTotalL), p.FlowTotalL, 0, 1_000_000);
        Check(errors, nameof(p.Rssi), p.Rssi, -127, 0);

        if (p.WaterTempC is { } temp)
        {
            Check(errors, nameof(p.WaterTempC), temp, -20, 80);
        }

        return errors;
    }

    private static void Check(List<string> errors, string field, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            errors.Add($"{field}={value} outside [{min}, {max}]");
        }
    }
}
