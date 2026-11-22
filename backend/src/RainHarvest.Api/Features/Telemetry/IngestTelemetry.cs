using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Common.Mqtt;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>
/// MQTT <c>rainwater/{deviceId}/telemetry</c>: validate, register the device if
/// new, and append one row to the <c>readings</c> hypertable. Bad messages are
/// logged and dropped; they never stop the pipeline.
/// </summary>
[MqttTopic(MqttTopics.Root + "/+/" + MqttTopics.Telemetry)]
public sealed partial class IngestTelemetry(
    AppDbContext db,
    DeviceTracker tracker,
    DomainEventPublisher events,
    ILogger<IngestTelemetry> logger) : IMqttHandler
{
    /// <summary>Device timestamps before this mean the ESP32 has not synced NTP yet.</summary>
    private static readonly DateTimeOffset EarliestPlausibleDeviceTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task HandleAsync(MqttMessage message, CancellationToken cancellationToken)
    {
        if (!MqttTopics.TryGetDeviceId(message.Topic, out var deviceId) || !Device.IsValidId(deviceId))
        {
            LogInvalidTopic(message.Topic);
            return;
        }

        TelemetryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TelemetryPayload>(message.Payload.Span, MqttJson.Options);
        }
        catch (JsonException ex)
        {
            LogMalformed(deviceId, ex.Message);
            return;
        }

        if (payload is null)
        {
            LogMalformed(deviceId, "empty payload");
            return;
        }

        var errors = TelemetryValidator.Validate(payload);
        if (errors.Count > 0)
        {
            LogOutOfRange(deviceId, string.Join("; ", errors));
            return;
        }

        var receivedAt = message.ReceivedAt;
        var deviceTs = ToDeviceTime(payload.Ts, receivedAt);

        await tracker.MarkSeenAsync(deviceId, receivedAt, cancellationToken);

        // The key is (device_id, received_at); a second row with the exact same
        // timestamp is a duplicate and is skipped rather than failing the handler.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO readings (device_id, received_at, device_ts, level_pct, level_cm, turbidity_ntu, ph,
                                  water_temp_c, raining, soil_pct, flow_lpm, flow_total_l, rssi)
            VALUES ({deviceId}, {receivedAt}, {deviceTs}, {payload.LevelPct}, {payload.LevelCm},
                    {payload.TurbidityNtu}, {payload.Ph}, {payload.WaterTempC}, {payload.Raining},
                    {payload.SoilPct}, {payload.FlowLpm}, {payload.FlowTotalL}, {payload.Rssi})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);

        if (inserted == 0)
        {
            return; // duplicate; rules already ran for it
        }

        var reading = new Reading
        {
            DeviceId = deviceId,
            ReceivedAt = receivedAt,
            DeviceTs = deviceTs,
            LevelPct = payload.LevelPct,
            LevelCm = payload.LevelCm,
            TurbidityNtu = payload.TurbidityNtu,
            Ph = payload.Ph,
            WaterTempC = payload.WaterTempC,
            Raining = payload.Raining,
            SoilPct = payload.SoilPct,
            FlowLpm = payload.FlowLpm,
            FlowTotalL = payload.FlowTotalL,
            Rssi = payload.Rssi,
        };
        await events.PublishAsync(new TelemetryReceived(reading), cancellationToken);
    }

    private static DateTimeOffset? ToDeviceTime(long? unixSeconds, DateTimeOffset receivedAt)
    {
        if (unixSeconds is not { } ts || ts <= 0 || ts > receivedAt.AddDays(1).ToUnixTimeSeconds())
        {
            return null;
        }

        var deviceTime = DateTimeOffset.FromUnixTimeSeconds(ts);
        return deviceTime < EarliestPlausibleDeviceTime ? null : deviceTime;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring telemetry on invalid topic {Topic}")]
    private partial void LogInvalidTopic(string topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring malformed telemetry from {DeviceId}: {Reason}")]
    private partial void LogMalformed(string deviceId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring out-of-range telemetry from {DeviceId}: {Errors}")]
    private partial void LogOutOfRange(string deviceId, string errors);
}
