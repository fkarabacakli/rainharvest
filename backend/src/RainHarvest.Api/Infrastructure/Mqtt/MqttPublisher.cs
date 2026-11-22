using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;

namespace RainHarvest.Api.Infrastructure.Mqtt;

public interface IMqttPublisher
{
    /// <summary>Publishes <paramref name="payload"/> as camelCase JSON with QoS 1.</summary>
    /// <exception cref="MqttNotConnectedException">The broker is not reachable right now.</exception>
    Task PublishJsonAsync<T>(string topic, T payload, bool retain, CancellationToken cancellationToken);
}

public sealed class MqttNotConnectedException() : Exception("The MQTT broker is not connected.");

public sealed class MqttPublisher(MqttConnection connection) : IMqttPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishJsonAsync<T>(string topic, T payload, bool retain, CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
        {
            throw new MqttNotConnectedException();
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
            .WithContentType("application/json")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build();

        await connection.Client.PublishAsync(message, cancellationToken);
    }
}
