using System.Text;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Common.Mqtt;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Settings;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// MQTT <c>rainwater/{deviceId}/status</c>: plain-text "online" / "offline".
/// "offline" usually comes from the broker as the device's Last Will.
/// </summary>
[MqttTopic(MqttTopics.Root + "/+/" + MqttTopics.Status)]
public sealed partial class HandleDeviceStatus(
    DeviceTracker tracker,
    DeviceSettingsService settings,
    DomainEventPublisher events,
    ILogger<HandleDeviceStatus> logger) : IMqttHandler
{
    public async Task HandleAsync(MqttMessage message, CancellationToken cancellationToken)
    {
        if (!MqttTopics.TryGetDeviceId(message.Topic, out var deviceId) || !Device.IsValidId(deviceId))
        {
            LogInvalidTopic(message.Topic);
            return;
        }

        // An empty retained message just clears the retained status; nothing to do.
        if (message.Payload.IsEmpty)
        {
            return;
        }

        var status = Encoding.UTF8.GetString(message.Payload.Span).Trim().ToLowerInvariant();
        switch (status)
        {
            case DeviceStatus.Online:
                await tracker.MarkSeenAsync(deviceId, message.ReceivedAt, cancellationToken);

                // Re-send thresholds on every (re)connect: covers a new or reflashed
                // device and a config update made while the broker was unreachable.
                await settings.PublishConfigAsync(await settings.GetEffectiveAsync(deviceId, cancellationToken), cancellationToken);
                await events.PublishAsync(new DeviceStatusChanged(deviceId, true, message.ReceivedAt), cancellationToken);
                break;
            case DeviceStatus.Offline:
                await tracker.MarkOfflineAsync(deviceId, message.ReceivedAt, cancellationToken);
                await events.PublishAsync(new DeviceStatusChanged(deviceId, false, message.ReceivedAt), cancellationToken);
                break;
            default:
                LogUnknownStatus(deviceId, status);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring status on invalid topic {Topic}")]
    private partial void LogInvalidTopic(string topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring unknown status '{Status}' from device {DeviceId}")]
    private partial void LogUnknownStatus(string deviceId, string status);
}
