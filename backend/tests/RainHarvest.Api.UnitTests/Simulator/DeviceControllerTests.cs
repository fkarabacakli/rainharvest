using RainHarvest.Contracts;
using RainHarvest.DeviceSimulator;

namespace RainHarvest.Api.UnitTests.Simulator;

/// <summary>
/// The simulator's firmware logic doubles as an executable spec for the ESP32
/// team (docs/mqtt.md): sequence, interlock, safety limits, state reporting.
/// </summary>
public sealed class DeviceControllerTests
{
    private static readonly TelemetryPayload Good = new(70, 56, 5, 7, false, 20, 0, 0, -60);
    private static readonly TimeSpan T0 = TimeSpan.FromMinutes(10);

    [Fact]
    public void Irrigate_opens_valve_runs_pump_then_closes_everything()
    {
        var c = new DeviceController();

        var accepted = Assert.Single(c.Handle(new CommandPayload("c1", CommandActions.Irrigate, 60), Good, T0));
        Assert.Equal(new StatePayload(true, true, true, "c1", CommandResults.Accepted), accepted);

        Assert.Empty(c.Tick(Good, T0 + TimeSpan.FromSeconds(59)));
        var done = Assert.Single(c.Tick(Good, T0 + TimeSpan.FromSeconds(60)));
        Assert.Equal(new StatePayload(false, false, false, "c1", CommandResults.Completed), done);
    }

    [Theory]
    [InlineData(10, 7, 5, RejectReasons.TankEmpty)]
    [InlineData(70, 5.0, 5, RejectReasons.WaterQuality)]
    [InlineData(70, 7, 80, RejectReasons.WaterQuality)]
    public void Irrigate_is_refused_when_unsafe(double level, double ph, double turbidity, string reason)
    {
        var c = new DeviceController();
        var reading = Good with { LevelPct = level, Ph = ph, TurbidityNtu = turbidity };

        var state = Assert.Single(c.Handle(new CommandPayload("c1", CommandActions.Irrigate, 60), reading, T0));

        Assert.Equal(CommandResults.Rejected, state.LastCommandResult);
        Assert.Equal(reason, state.Reason);
        Assert.False(state.PumpOn);
        Assert.False(state.ValveOpen);
    }

    [Fact]
    public void Pump_never_runs_against_a_closed_valve_and_valve_never_closes_under_a_running_pump()
    {
        var c = new DeviceController();

        Assert.Equal(RejectReasons.ValveClosed, c.Handle(new CommandPayload("p1", CommandActions.PumpOn, 30), Good, T0)[^1].Reason);

        c.Handle(new CommandPayload("v1", CommandActions.ValveOpen), Good, T0);
        Assert.Equal(CommandResults.Accepted, c.Handle(new CommandPayload("p2", CommandActions.PumpOn, 30), Good, T0)[^1].LastCommandResult);
        Assert.Equal(RejectReasons.PumpRunning, c.Handle(new CommandPayload("v2", CommandActions.ValveClose), Good, T0)[^1].Reason);
    }

    [Fact]
    public void Stop_completes_the_running_irrigation_and_itself()
    {
        var c = new DeviceController();
        c.Handle(new CommandPayload("c1", CommandActions.Irrigate, 60), Good, T0);

        var states = c.Handle(new CommandPayload("s1", CommandActions.Stop), Good, T0 + TimeSpan.FromSeconds(5));

        Assert.Equal(["c1", "s1"], states.Select(s => s.LastCommandId));
        Assert.All(states, s => Assert.Equal(CommandResults.Completed, s.LastCommandResult));
        Assert.False(c.PumpOn || c.ValveOpen || c.Irrigating);
    }

    [Fact]
    public void Pump_stops_when_the_tank_runs_dry_and_runs_are_capped_by_config()
    {
        var c = new DeviceController { Config = new ConfigPayload(6, 8.5, 25, 15, PumpMaxS: 30) };
        c.Handle(new CommandPayload("c1", CommandActions.Irrigate, 600), Good, T0);

        Assert.Equal(30, c.PumpSecondsBetween(T0, T0 + TimeSpan.FromMinutes(5)));

        var stopped = Assert.Single(c.Tick(Good with { LevelPct = 10 }, T0 + TimeSpan.FromSeconds(10)));
        Assert.Equal(RejectReasons.TankEmpty, stopped.Reason);
        Assert.False(stopped.PumpOn);
    }

    [Fact]
    public void A_second_irrigate_while_busy_is_refused()
    {
        var c = new DeviceController();
        c.Handle(new CommandPayload("c1", CommandActions.Irrigate, 60), Good, T0);

        Assert.Equal(RejectReasons.Busy, c.Handle(new CommandPayload("c2", CommandActions.Irrigate, 60), Good, T0)[^1].Reason);
    }
}
