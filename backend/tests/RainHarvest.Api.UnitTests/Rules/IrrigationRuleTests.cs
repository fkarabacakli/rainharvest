using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Rules;
using static RainHarvest.Api.Features.Rules.IrrigationRule;

namespace RainHarvest.Api.UnitTests.Rules;

public sealed class IrrigationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceSettings Settings = DeviceSettings.Defaults("node1"); // dry 30, wet 60, level ≥15, pH 6–8.5, turbidity ≤25

    private static Reading Reading(double soil = 20, double level = 70, double ph = 7, double turbidity = 5, bool raining = false) => new()
    {
        DeviceId = "node1", ReceivedAt = Now, SoilPct = soil, LevelPct = level, Ph = ph, TurbidityNtu = turbidity, Raining = raining,
    };

    private static Input In(
        Reading? reading = null,
        DeviceSettings? settings = null,
        bool irrigating = false,
        bool autoRunning = false,
        bool pending = false,
        DateTimeOffset? lastAuto = null) =>
        new(reading ?? Reading(), settings ?? Settings, irrigating, autoRunning, pending, lastAuto, Now);

    [Fact]
    public void Dry_soil_with_good_water_irrigates_for_the_configured_duration()
    {
        var decision = Assert.IsType<Irrigate>(Decide(In()));

        Assert.Equal(Settings.IrrigationDurationS, decision.DurationS);
        Assert.Equal("soil 20% < 30%", decision.Reason);
    }

    public static TheoryData<Input, string> HoldBacks => new()
    {
        { In(Reading(soil: 30)), "not below" },
        { In(Reading(raining: true)), "raining" },
        { In(Reading(level: 14.9)), "tank" },
        { In(Reading(ph: 5.5)), "water quality" },
        { In(Reading(ph: 8.6)), "water quality" },
        { In(Reading(turbidity: 26)), "water quality" },
        { In(pending: true), "waiting" },
        { In(lastAuto: Now.AddSeconds(-599)), "cooldown" },
        { In(irrigating: true), "in progress" },
        { In(settings: Off()), "off" },
    };

    [Theory]
    [MemberData(nameof(HoldBacks))]
    public void Holds_back_when_watering_is_not_wanted_or_not_safe(Input input, string reasonFragment)
    {
        var decision = Assert.IsType<NoAction>(Decide(input));

        Assert.Contains(reasonFragment, decision.Reason);
    }

    [Fact]
    public void Cooldown_ends_after_the_configured_seconds() =>
        Assert.IsType<Irrigate>(Decide(In(lastAuto: Now.AddSeconds(-600))));

    [Fact]
    public void Stops_its_own_irrigation_once_soil_is_wet() =>
        Assert.IsType<Stop>(Decide(In(Reading(soil: 60), irrigating: true, autoRunning: true)));

    [Fact]
    public void Never_stops_a_users_manual_irrigation() =>
        Assert.IsType<NoAction>(Decide(In(Reading(soil: 90), irrigating: true, autoRunning: false)));

    [Fact]
    public void Between_dry_and_wet_thresholds_it_keeps_watering() =>
        Assert.IsType<NoAction>(Decide(In(Reading(soil: 45), irrigating: true, autoRunning: true)));

    private static DeviceSettings Off()
    {
        var s = DeviceSettings.Defaults("node1");
        s.AutoIrrigation = false;
        return s;
    }
}
