using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Mqtt;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Settings;

/// <summary>
/// Reads a device's effective settings (stored row or defaults) and pushes the
/// device-side safety subset as retained MQTT <c>config</c>.
/// </summary>
public sealed partial class DeviceSettingsService(
    AppDbContext db,
    IMqttPublisher publisher,
    ILogger<DeviceSettingsService> logger)
{
    public async Task<DeviceSettings> GetEffectiveAsync(string deviceId, CancellationToken cancellationToken) =>
        await db.DeviceSettings.AsNoTracking().SingleOrDefaultAsync(s => s.DeviceId == deviceId, cancellationToken)
        ?? DeviceSettings.Defaults(deviceId);

    /// <summary>
    /// Best effort: if the broker is down the config is published again the next
    /// time the device reports "online". Returns whether it was published now.
    /// </summary>
    public async Task<bool> PublishConfigAsync(DeviceSettings settings, CancellationToken cancellationToken)
    {
        var payload = new ConfigPayload(settings.PhMin, settings.PhMax, settings.TurbidityMaxNtu, settings.LevelMinPct, settings.PumpMaxS);
        try
        {
            await publisher.PublishJsonAsync(MqttTopics.For(settings.DeviceId, MqttTopics.Config), payload, retain: true, cancellationToken);
            return true;
        }
        catch (MqttNotConnectedException)
        {
            LogNotPublished(settings.DeviceId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config for {DeviceId} not published: MQTT disconnected; will retry when the device comes online")]
    private partial void LogNotPublished(string deviceId);
}
