namespace RainHarvest.Api.Domain;

/// <summary>
/// Per-device thresholds. A device without a row uses <see cref="Defaults"/>.
/// The safety subset (pH, turbidity, level, pump limit) is also pushed to the
/// device as retained <c>config</c>; the rest drives the backend's irrigation rule.
/// </summary>
public sealed class DeviceSettings
{
    public required string DeviceId { get; init; }

    // Water quality: outside these, water must not be used for irrigation.
    public double PhMin { get; set; }
    public double PhMax { get; set; }
    public double TurbidityMaxNtu { get; set; }

    /// <summary>Below this tank level the pump must not run (dry-run protection).</summary>
    public double LevelMinPct { get; set; }

    /// <summary>Automatic irrigation starts when soil moisture drops below this.</summary>
    public double SoilDryPct { get; set; }

    /// <summary>Automatic irrigation is stopped early once soil moisture reaches this.</summary>
    public double SoilWetPct { get; set; }

    public int IrrigationDurationS { get; set; }

    /// <summary>Minimum gap between two automatic irrigations.</summary>
    public int IrrigationCooldownS { get; set; }

    /// <summary>Hard cap the device applies to any pump run.</summary>
    public int PumpMaxS { get; set; }

    public bool AutoIrrigation { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static DeviceSettings Defaults(string deviceId) => new()
    {
        DeviceId = deviceId,
        PhMin = 6.0,
        PhMax = 8.5,
        TurbidityMaxNtu = 25,
        LevelMinPct = 15,
        SoilDryPct = 30,
        SoilWetPct = 60,
        IrrigationDurationS = 60,
        IrrigationCooldownS = 600,
        PumpMaxS = 300,
        AutoIrrigation = true,
    };

    /// <summary>Range and consistency checks; keys are camelCase field names.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        void Range(string field, double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
            {
                errors[field] = [$"Must be between {min} and {max}."];
            }
        }

        Range("phMin", PhMin, 0, 14);
        Range("phMax", PhMax, 0, 14);
        Range("turbidityMaxNtu", TurbidityMaxNtu, 0, 5000);
        Range("levelMinPct", LevelMinPct, 0, 100);
        Range("soilDryPct", SoilDryPct, 0, 100);
        Range("soilWetPct", SoilWetPct, 0, 100);
        Range("irrigationDurationS", IrrigationDurationS, 5, 3600);
        Range("irrigationCooldownS", IrrigationCooldownS, 0, 86_400);
        Range("pumpMaxS", PumpMaxS, 5, 3600);

        if (!errors.ContainsKey("phMin") && !errors.ContainsKey("phMax") && PhMin >= PhMax)
        {
            errors["phMax"] = ["Must be greater than phMin."];
        }

        if (!errors.ContainsKey("soilDryPct") && !errors.ContainsKey("soilWetPct") && SoilDryPct >= SoilWetPct)
        {
            errors["soilWetPct"] = ["Must be greater than soilDryPct (hysteresis)."];
        }

        if (!errors.ContainsKey("irrigationDurationS") && !errors.ContainsKey("pumpMaxS") && IrrigationDurationS > PumpMaxS)
        {
            errors["irrigationDurationS"] = ["Must not exceed pumpMaxS; the device would cut it short."];
        }

        return errors;
    }

    public bool WaterQualityOk(double ph, double turbidityNtu) =>
        ph >= PhMin && ph <= PhMax && turbidityNtu <= TurbidityMaxNtu;
}
