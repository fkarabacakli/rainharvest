using RainHarvest.Contracts;

namespace RainHarvest.Api.UnitTests.Mqtt;

public sealed class MqttTopicsTests
{
    [Fact]
    public void For_and_AllDevices_build_the_expected_topics()
    {
        Assert.Equal("rainwater/node1/telemetry", MqttTopics.For("node1", MqttTopics.Telemetry));
        Assert.Equal("rainwater/+/state", MqttTopics.AllDevices(MqttTopics.State));
    }

    [Theory]
    [InlineData("rainwater/node1/telemetry", true, "node1")]
    [InlineData("rainwater/node-7/status", true, "node-7")]
    [InlineData("rainwater//telemetry", false, "")]
    [InlineData("other/node1/telemetry", false, "")]
    [InlineData("rainwater/node1", false, "")]
    public void TryGetDeviceId_extracts_the_second_level(string topic, bool ok, string deviceId)
    {
        Assert.Equal(ok, MqttTopics.TryGetDeviceId(topic, out var actual));
        Assert.Equal(deviceId, actual);
    }
}
