using MQTTnet;

namespace RainHarvest.Api.Infrastructure.Mqtt;

/// <summary>The single MQTT client shared by the worker, the publisher and the health check.</summary>
public sealed class MqttConnection : IDisposable
{
    public IMqttClient Client { get; } = new MqttClientFactory().CreateMqttClient();

    public bool IsConnected => Client.IsConnected;

    public void Dispose() => Client.Dispose();
}
