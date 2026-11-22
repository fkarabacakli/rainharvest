using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Commands;

namespace RainHarvest.Api.UnitTests.Rules;

public sealed class SettingsAndCommandStatusTests
{
    [Fact]
    public void Defaults_are_valid() => Assert.Empty(DeviceSettings.Defaults("x").Validate());

    [Theory]
    [InlineData("phMax", 7.0, 7.0, 30, 60, 60, 300)]
    [InlineData("soilWetPct", 6.0, 8.5, 60, 60, 60, 300)]
    [InlineData("irrigationDurationS", 6.0, 8.5, 30, 60, 400, 300)]
    [InlineData("phMin", -1, 8.5, 30, 60, 60, 300)]
    public void Inconsistent_settings_are_rejected(string field, double phMin, double phMax, double dry, double wet, int duration, int pumpMax)
    {
        var s = DeviceSettings.Defaults("x");
        s.PhMin = phMin;
        s.PhMax = phMax;
        s.SoilDryPct = dry;
        s.SoilWetPct = wet;
        s.IrrigationDurationS = duration;
        s.PumpMaxS = pumpMax;

        Assert.Contains(field, s.Validate().Keys);
    }

    [Theory]
    [InlineData(CommandStatus.Sent, CommandStatus.Accepted, true)]
    [InlineData(CommandStatus.Sent, CommandStatus.Rejected, true)]
    [InlineData(CommandStatus.Sent, CommandStatus.Completed, true)]
    [InlineData(CommandStatus.Accepted, CommandStatus.Completed, true)]
    [InlineData(CommandStatus.Expired, CommandStatus.Accepted, true)]
    [InlineData(CommandStatus.Completed, CommandStatus.Accepted, false)] // retained replay after restart
    [InlineData(CommandStatus.Accepted, CommandStatus.Accepted, false)]
    [InlineData(CommandStatus.Rejected, CommandStatus.Completed, false)]
    [InlineData(CommandStatus.Accepted, CommandStatus.Rejected, false)]
    public void Command_status_only_moves_forward(CommandStatus from, CommandStatus to, bool allowed) =>
        Assert.Equal(allowed, HandleDeviceState.CanMove(from, to));
}
