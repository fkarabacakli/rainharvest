using System.Globalization;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Alerts;

/// <summary>
/// Which reading-based alerts to raise or resolve. Pure, so every branch is
/// unit tested. Resolving needs a margin beyond the raise threshold
/// (hysteresis): a value jittering around the limit raises once instead of
/// opening and closing an alert on every reading.
/// </summary>
public static class AlertRule
{
    public const double PhResolveMargin = 0.1;
    public const double TurbidityResolveFactor = 0.9;
    public const double LevelResolveMarginPct = 5;

    public sealed record Change(AlertType Type, bool Raise, string Message);

    public static IReadOnlyList<Change> Evaluate(Reading r, DeviceSettings s, IReadOnlySet<AlertType> open)
    {
        var changes = new List<Change>();

        var waterBad = !s.WaterQualityOk(r.Ph, r.TurbidityNtu);
        var waterClear = r.Ph >= s.PhMin + PhResolveMargin
                         && r.Ph <= s.PhMax - PhResolveMargin
                         && r.TurbidityNtu <= s.TurbidityMaxNtu * TurbidityResolveFactor;

        if (waterBad && !open.Contains(AlertType.WaterQualityBad))
        {
            changes.Add(new Change(AlertType.WaterQualityBad, true,
                $"Water quality out of range: pH {F(r.Ph)} (allowed {F(s.PhMin)}–{F(s.PhMax)}), " +
                $"turbidity {F(r.TurbidityNtu)} NTU (max {F(s.TurbidityMaxNtu)}). Irrigation is held back; check the tank."));
        }
        else if (waterClear && open.Contains(AlertType.WaterQualityBad))
        {
            changes.Add(new Change(AlertType.WaterQualityBad, false, "Water quality back in range."));
        }

        if (r.LevelPct < s.LevelMinPct && !open.Contains(AlertType.TankLow))
        {
            changes.Add(new Change(AlertType.TankLow, true,
                $"Tank at {F(r.LevelPct)}%, below the {F(s.LevelMinPct)}% minimum. The pump will not run."));
        }
        else if (r.LevelPct >= s.LevelMinPct + LevelResolveMarginPct && open.Contains(AlertType.TankLow))
        {
            changes.Add(new Change(AlertType.TankLow, false, "Tank level recovered."));
        }

        return changes;
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
