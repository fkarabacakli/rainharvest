using RainHarvest.Api.Common.Mqtt;

namespace RainHarvest.Api.UnitTests.Mqtt;

public sealed class TopicMatcherTests
{
    [Theory]
    [InlineData("rainwater/+/telemetry", "rainwater/node1/telemetry", true)]
    [InlineData("rainwater/+/telemetry", "rainwater/node1/state", false)]
    [InlineData("rainwater/+/telemetry", "rainwater/node1/telemetry/extra", false)]
    [InlineData("rainwater/+/telemetry", "rainwater/telemetry", false)]
    [InlineData("rainwater/#", "rainwater/node1/telemetry", true)]
    [InlineData("rainwater/#", "rainwater", true)]
    [InlineData("rainwater/node1/#", "rainwater/node2/telemetry", false)]
    [InlineData("rainwater/node1/status", "rainwater/node1/status", true)]
    [InlineData("rainwater/node1/status", "rainwater/node1/Status", false)]
    [InlineData("#", "anything/at/all", true)]
    public void Matches_follows_mqtt_wildcard_rules(string filter, string topic, bool expected) =>
        Assert.Equal(expected, TopicMatcher.Matches(filter, topic));
}
