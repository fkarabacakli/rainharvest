using RainHarvest.Contracts;

namespace RainHarvest.DeviceSimulator;

/// <summary>
/// Simple physical model of one rainwater node ("normal" scenario).
/// Weather follows an hourly cycle in simulated time: about 8 minutes of rain
/// per hour fill the tank and wet the soil; between showers the soil dries.
/// No irrigation yet (commands arrive in M3), so outlet flow stays 0.
/// </summary>
public sealed class DeviceModel(Random random)
{
    private const double TankHeightCm = 80;
    private const double RainMinutesPerHour = 8;

    private double _levelPct = 40 + random.NextDouble() * 30;
    private double _soilPct = 45 + random.NextDouble() * 20;

    /// <summary>Phase offset so several devices do not rain in lockstep.</summary>
    private readonly double _phaseMinutes = random.NextDouble() * 60;

    public bool Raining { get; private set; }

    /// <summary>Advances the model by <paramref name="elapsed"/> of simulated time.</summary>
    public void Advance(TimeSpan simulatedNow, TimeSpan elapsed)
    {
        var minutes = elapsed.TotalMinutes;
        Raining = (simulatedNow.TotalMinutes + _phaseMinutes) % 60 < RainMinutesPerHour;

        if (Raining)
        {
            _levelPct += 0.5 * minutes;
            _soilPct += 1.2 * minutes;
        }
        else
        {
            _levelPct -= 0.005 * minutes; // evaporation
            _soilPct -= 0.12 * minutes;
        }

        _levelPct = Math.Clamp(_levelPct, 0, 100);
        _soilPct = Math.Clamp(_soilPct, 5, 95);
    }

    public TelemetryPayload Sample(TimeSpan simulatedNow, DateTimeOffset wallClock)
    {
        var hours = simulatedNow.TotalHours;
        var level = Math.Clamp(_levelPct + Noise(0.3), 0, 100);

        return new TelemetryPayload(
            LevelPct: Round(level, 1),
            LevelCm: Round(level / 100 * TankHeightCm, 1),
            TurbidityNtu: Round(Math.Max(0, (Raining ? 14 : 4) + Noise(1.5)), 1),
            Ph: Round(Math.Clamp(6.9 + 0.15 * Math.Sin(hours / 6) + Noise(0.04), 0, 14), 2),
            Raining: Raining,
            SoilPct: Round(Math.Clamp(_soilPct + Noise(0.5), 0, 100), 1),
            FlowLpm: 0,
            FlowTotalL: 0, // grows once irrigation commands exist (M3)
            Rssi: (int)Math.Round(-60 + Noise(3)),
            Ts: wallClock.ToUnixTimeSeconds(),
            WaterTempC: Round(18 + 2 * Math.Sin(hours * Math.PI / 12) + Noise(0.1), 2));
    }

    private double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
