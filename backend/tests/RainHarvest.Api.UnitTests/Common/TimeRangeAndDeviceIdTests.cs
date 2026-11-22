using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.UnitTests.Common;

public sealed class TimeRangeAndDeviceIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Defaults_to_the_default_span_ending_now()
    {
        Assert.True(TimeRange.TryResolve(null, null, Now, TimeSpan.FromHours(1), TimeSpan.FromDays(1), out var range, out _));
        Assert.Equal(new TimeRange(Now.AddHours(-1), Now), range);
    }

    [Fact]
    public void Missing_from_is_measured_back_from_the_given_to()
    {
        var to = Now.AddDays(-3);

        Assert.True(TimeRange.TryResolve(null, to, Now, TimeSpan.FromHours(2), TimeSpan.FromDays(1), out var range, out _));
        Assert.Equal(to.AddHours(-2), range.From);
    }

    [Fact]
    public void Converts_offsets_to_utc()
    {
        var from = new DateTimeOffset(2026, 11, 22, 13, 0, 0, TimeSpan.FromHours(3));

        Assert.True(TimeRange.TryResolve(from, Now, Now, TimeSpan.FromHours(1), TimeSpan.FromDays(1), out var range, out _));
        Assert.Equal(TimeSpan.Zero, range.From.Offset);
        Assert.Equal(Now.AddHours(-2), range.From);
    }

    [Fact]
    public void Rejects_reversed_window()
    {
        Assert.False(TimeRange.TryResolve(Now, Now.AddHours(-1), Now, TimeSpan.FromHours(1), TimeSpan.FromDays(1), out _, out var errors));
        Assert.Contains("earlier", errors["from"][0]);
    }

    [Fact]
    public void Rejects_window_longer_than_max()
    {
        Assert.False(TimeRange.TryResolve(Now.AddDays(-2), Now, Now, TimeSpan.FromHours(1), TimeSpan.FromDays(1), out _, out var errors));
        Assert.Contains("1 days", errors["from"][0]);
    }

    [Theory]
    [InlineData("node1", true)]
    [InlineData("tank_A-2", true)]
    [InlineData("", false)]
    [InlineData("node 1", false)]
    [InlineData("node/1", false)]
    [InlineData("node+", false)]
    [InlineData("çatı", false)]
    public void Device_ids_are_topic_and_url_safe(string id, bool valid) => Assert.Equal(valid, Device.IsValidId(id));

    [Fact]
    public void Device_ids_have_a_length_limit()
    {
        Assert.True(Device.IsValidId(new string('a', Device.MaxIdLength)));
        Assert.False(Device.IsValidId(new string('a', Device.MaxIdLength + 1)));
    }
}
