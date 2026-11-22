using System.Globalization;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Rules;

/// <summary>
/// Automatic irrigation decision, as a pure function so every branch is unit
/// tested (PLAN.md §8). Safety limits are enforced again on the device; this
/// rule only decides when watering is worthwhile.
/// </summary>
public static class IrrigationRule
{
    /// <param name="Irrigating">Device reports an irrigation sequence running.</param>
    /// <param name="AutoIrrigationRunning">The running irrigation was started by this rule (not by a user).</param>
    /// <param name="CommandPending">A command is still waiting for the device's answer.</param>
    /// <param name="LastAutoIrrigationAt">When the rule last asked to irrigate (any outcome).</param>
    public sealed record Input(
        Reading Reading,
        DeviceSettings Settings,
        bool Irrigating,
        bool AutoIrrigationRunning,
        bool CommandPending,
        DateTimeOffset? LastAutoIrrigationAt,
        DateTimeOffset Now);

    public abstract record Decision(string Reason);

    public sealed record Irrigate(int DurationS, string Reason) : Decision(Reason);

    /// <summary>Stop an automatic irrigation early because the soil is wet enough.</summary>
    public sealed record Stop(string Reason) : Decision(Reason);

    /// <summary>Nothing to do (soil fine, or a reason not to water right now).</summary>
    public sealed record NoAction(string Reason) : Decision(Reason);

    public static Decision Decide(Input i)
    {
        var s = i.Settings;
        var r = i.Reading;

        if (!s.AutoIrrigation)
        {
            return new NoAction("automatic irrigation is off");
        }

        if (i.CommandPending)
        {
            return new NoAction("waiting for the device to answer a command");
        }

        if (i.Irrigating)
        {
            // Hysteresis upper bound: end our own watering once the soil is wet.
            // A user's manual irrigation is left alone.
            return i.AutoIrrigationRunning && r.SoilPct >= s.SoilWetPct
                ? new Stop($"soil {F(r.SoilPct)}% reached {F(s.SoilWetPct)}%")
                : new NoAction("irrigation in progress");
        }

        if (r.SoilPct >= s.SoilDryPct)
        {
            return new NoAction($"soil {F(r.SoilPct)}% is not below {F(s.SoilDryPct)}%");
        }

        if (r.Raining)
        {
            return new NoAction("it is raining");
        }

        if (r.LevelPct < s.LevelMinPct)
        {
            return new NoAction($"tank {F(r.LevelPct)}% is below {F(s.LevelMinPct)}%");
        }

        if (!s.WaterQualityOk(r.Ph, r.TurbidityNtu))
        {
            return new NoAction($"water quality out of range (pH {F(r.Ph)}, turbidity {F(r.TurbidityNtu)} NTU)");
        }

        if (i.LastAutoIrrigationAt is { } last && i.Now - last < TimeSpan.FromSeconds(s.IrrigationCooldownS))
        {
            return new NoAction("cooldown since the last automatic irrigation");
        }

        return new Irrigate(s.IrrigationDurationS, $"soil {F(r.SoilPct)}% < {F(s.SoilDryPct)}%");
    }

    private static string F(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
