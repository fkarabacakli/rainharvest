using RainHarvest.Contracts;

namespace RainHarvest.DeviceSimulator;

/// <summary>
/// Physical model of one rainwater node: a 200 L tank, a planter and the
/// sensors. Time is simulated time, so <c>--speed</c> compresses hours into minutes.
/// </summary>
public sealed class DeviceModel(Random random, Scenario scenario)
{
    private const double TankHeightCm = 80;
    private const double TankLiters = 200;
    private const double RainMinutesPerHour = 8;

    /// <summary>Outlet flow while the pump runs, L/min.</summary>
    public const double PumpFlowLpm = 4;

    private double _levelPct = Between(random, scenario.StartLevel);
    private double _soilPct = Between(random, scenario.StartSoil);
    private double _flowTotalL;

    /// <summary>Phase offset so several devices do not rain in lockstep.</summary>
    private readonly double _phaseMinutes = random.NextDouble() * 60;

    public bool Raining { get; private set; }

    /// <param name="pumpSeconds">How long the pump ran during <paramref name="elapsed"/> (simulated seconds).</param>
    public void Advance(TimeSpan simulatedNow, TimeSpan elapsed, double pumpSeconds)
    {
        var minutes = elapsed.TotalMinutes;
        var pumpMinutes = Math.Min(pumpSeconds / 60, minutes);
        Raining = scenario.ForcedRain ?? (simulatedNow.TotalMinutes + _phaseMinutes) % 60 < RainMinutesPerHour;

        if (Raining)
        {
            _levelPct += 0.5 * minutes;
            _soilPct += 1.2 * minutes;
        }
        else
        {
            _levelPct -= 0.005 * minutes; // evaporation
            _soilPct -= scenario.SoilDryPerMin * minutes;
        }

        // Watering: the pump moves water from the tank into the planter.
        var liters = PumpFlowLpm * pumpMinutes;
        _levelPct -= liters / TankLiters * 100;
        _soilPct += 3.0 * pumpMinutes;
        _flowTotalL += liters;

        _levelPct = Math.Clamp(_levelPct, 0, 100);
        _soilPct = Math.Clamp(_soilPct, 5, 95);
    }

    public TelemetryPayload Sample(TimeSpan simulatedNow, DateTimeOffset wallClock, bool pumpOn)
    {
        var hours = simulatedNow.TotalHours;
        var level = Math.Clamp(_levelPct + Noise(0.3), 0, 100);
        var turbidity = scenario.TurbidityBase + (Raining && scenario.TurbidityBase < 14 ? 10 : 0);

        return new TelemetryPayload(
            LevelPct: Round(level, 1),
            LevelCm: Round(level / 100 * TankHeightCm, 1),
            TurbidityNtu: Round(Math.Max(0, turbidity + Noise(1.5)), 1),
            Ph: Round(Math.Clamp(scenario.PhBase + 0.15 * Math.Sin(hours / 6) + Noise(0.04), 0, 14), 2),
            Raining: Raining,
            SoilPct: Round(Math.Clamp(_soilPct + Noise(0.5), 0, 100), 1),
            FlowLpm: pumpOn ? Round(PumpFlowLpm + Noise(0.1), 2) : 0,
            FlowTotalL: Round(_flowTotalL, 2),
            Rssi: (int)Math.Round(-60 + Noise(3)),
            Ts: wallClock.ToUnixTimeSeconds(),
            WaterTempC: Round(18 + 2 * Math.Sin(hours * Math.PI / 12) + Noise(0.1), 2));
    }

    private double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;

    private static double Between(Random random, (double Min, double Max) range) =>
        range.Min + random.NextDouble() * (range.Max - range.Min);

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
