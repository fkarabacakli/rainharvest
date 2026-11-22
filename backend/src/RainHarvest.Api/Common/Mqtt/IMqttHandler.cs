namespace RainHarvest.Api.Common.Mqtt;

/// <summary>
/// One MQTT entry point of a feature slice. Mark the class with
/// <see cref="MqttTopicAttribute"/>; it is discovered automatically, resolved
/// from a fresh DI scope per message, and may therefore depend on scoped
/// services such as the DbContext.
/// </summary>
public interface IMqttHandler
{
    Task HandleAsync(MqttMessage message, CancellationToken cancellationToken);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class MqttTopicAttribute(string filter) : Attribute
{
    /// <summary>MQTT topic filter; <c>+</c> and <c>#</c> wildcards are allowed.</summary>
    public string Filter { get; } = filter;
}

public sealed record MqttMessage(string Topic, ReadOnlyMemory<byte> Payload, bool Retain, DateTimeOffset ReceivedAt);
