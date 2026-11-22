namespace RainHarvest.DeviceSimulator;

/// <summary>Weather and water conditions the simulated node lives in.</summary>
/// <param name="ForcedRain">null = hourly rain cycle; true/false = always/never raining.</param>
/// <param name="SoilDryPerMin">Soil moisture lost per simulated minute without rain or watering.</param>
/// <param name="Flaky">Drop the connection abruptly now and then (broker publishes the Last Will).</param>
public sealed record Scenario(
    string Name,
    string Description,
    bool? ForcedRain,
    double SoilDryPerMin,
    (double Min, double Max) StartSoil,
    (double Min, double Max) StartLevel,
    double PhBase,
    double TurbidityBase,
    bool Flaky = false)
{
    public static readonly IReadOnlyDictionary<string, Scenario> All = new[]
    {
        new Scenario("normal", "hourly showers, slow drying", null, 0.12, (45, 65), (40, 70), 6.9, 4),
        new Scenario("rain", "raining all the time; tank fills, no irrigation", true, 0.12, (45, 65), (30, 50), 6.8, 14),
        new Scenario("drought", "no rain, soil dries fast; the backend should start irrigating", false, 1.0, (34, 36), (65, 75), 6.9, 4),
        new Scenario("bad-water", "dry soil but pH and turbidity out of range; irrigation must be held back", false, 0.5, (31, 33), (60, 70), 5.2, 60),
        new Scenario("flaky", "normal weather, connection drops every ~45 s", null, 0.12, (45, 65), (40, 70), 6.9, 4, Flaky: true),
    }.ToDictionary(s => s.Name);
}
