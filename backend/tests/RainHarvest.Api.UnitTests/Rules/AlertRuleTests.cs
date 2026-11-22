using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Alerts;

namespace RainHarvest.Api.UnitTests.Rules;

public sealed class AlertRuleTests
{
    private static readonly DeviceSettings Settings = DeviceSettings.Defaults("node1"); // pH 6–8.5, turbidity ≤25, level ≥15

    private static Reading Reading(double ph = 7, double turbidity = 5, double level = 70) => new()
    {
        DeviceId = "node1", ReceivedAt = DateTimeOffset.UnixEpoch, Ph = ph, TurbidityNtu = turbidity, LevelPct = level,
    };

    private static IReadOnlyList<AlertRule.Change> Eval(Reading r, params AlertType[] open) =>
        AlertRule.Evaluate(r, Settings, open.ToHashSet());

    [Fact]
    public void Good_readings_change_nothing() => Assert.Empty(Eval(Reading()));

    [Theory]
    [InlineData(5.9, 5)]
    [InlineData(8.6, 5)]
    [InlineData(7, 25.1)]
    public void Bad_water_raises_once(double ph, double turbidity)
    {
        var change = Assert.Single(Eval(Reading(ph, turbidity)));
        Assert.Equal((AlertType.WaterQualityBad, true), (change.Type, change.Raise));
        Assert.Contains("Irrigation is held back", change.Message);

        Assert.Empty(Eval(Reading(ph, turbidity), AlertType.WaterQualityBad));
    }

    [Theory]
    [InlineData(6.05, 5, false)]  // back in range but inside the margin: keep it open
    [InlineData(7, 23, false)]    // turbidity above 90 % of the limit
    [InlineData(6.1, 22.5, true)] // clear with margin
    public void Water_alert_resolves_only_with_margin(double ph, double turbidity, bool resolves)
    {
        var changes = Eval(Reading(ph, turbidity), AlertType.WaterQualityBad);

        Assert.Equal(resolves, changes.Any(c => c is { Type: AlertType.WaterQualityBad, Raise: false }));
    }

    [Fact]
    public void Tank_low_raises_below_minimum_and_resolves_five_points_above()
    {
        Assert.Contains(Eval(Reading(level: 14)), c => c is { Type: AlertType.TankLow, Raise: true });
        Assert.Empty(Eval(Reading(level: 19), AlertType.TankLow));
        Assert.Contains(Eval(Reading(level: 20), AlertType.TankLow), c => c is { Type: AlertType.TankLow, Raise: false });
    }

    [Fact]
    public void Both_problems_at_once_raise_two_alerts() =>
        Assert.Equal(2, Eval(Reading(ph: 5, level: 10)).Count(c => c.Raise));

    [Fact]
    public void Offline_is_critical_others_are_warnings()
    {
        Assert.Equal(AlertSeverity.Critical, AlertService.SeverityOf(AlertType.DeviceOffline));
        Assert.Equal(AlertSeverity.Warning, AlertService.SeverityOf(AlertType.WaterQualityBad));
    }
}
