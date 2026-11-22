using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using Npgsql;
using RainHarvest.Contracts;

namespace RainHarvest.Api.IntegrationTests.Infrastructure;

public static class TestHelpers
{
    /// <summary>Matches the API: camelCase, enums as strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>A fresh device id per test, so tests never see each other's data.</summary>
    public static string NewDeviceId() => $"t-{Guid.NewGuid():N}"[..18];

    /// <summary>Polls until <paramref name="probe"/> returns a value or the timeout expires.</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(200);
        }
    }

    public static TelemetryPayload SampleTelemetry(double levelPct = 64.2) =>
        new(levelPct, 38.5, 12, 6.9, true, 31, 0, 142.7, -61, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 18.4);
}

/// <summary>Publishes to the test broker the way a device would.</summary>
public sealed class TestDevice(ApiFixture fixture) : IAsyncDisposable
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();

    public async Task ConnectAsync()
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(fixture.BrokerHost, fixture.BrokerPort)
            .WithClientId($"test-{Guid.NewGuid():N}")
            .Build();
        await _client.ConnectAsync(options);
    }

    public Task PublishTelemetryAsync(string deviceId, TelemetryPayload payload) =>
        PublishAsync(MqttTopics.For(deviceId, MqttTopics.Telemetry), JsonSerializer.SerializeToUtf8Bytes(payload, MqttJson.Options));

    public Task PublishRawAsync(string deviceId, string channel, string payload, bool retain = false) =>
        PublishAsync(MqttTopics.For(deviceId, channel), Encoding.UTF8.GetBytes(payload), retain);

    private Task PublishAsync(string topic, byte[] payload, bool retain = false) =>
        _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build());

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
    }
}

/// <summary>Writes readings straight into the database for query tests.</summary>
public static class TestData
{
    public static async Task InsertReadingsAsync(string connectionString, string deviceId, params (DateTimeOffset At, double LevelPct)[] rows)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var device = new NpgsqlCommand(
            "INSERT INTO devices (id, name, is_online, created_at) VALUES (@id, @id, FALSE, now())", connection))
        {
            device.Parameters.AddWithValue("id", deviceId);
            await device.ExecuteNonQueryAsync();
        }

        foreach (var (at, level) in rows)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO readings (device_id, received_at, level_pct, level_cm, turbidity_ntu, ph, raining,
                                      soil_pct, flow_lpm, flow_total_l, rssi)
                VALUES (@id, @at, @level, 10, 5, 7, FALSE, 50, 0, 0, -60)
                """,
                connection);
            insert.Parameters.AddWithValue("id", deviceId);
            insert.Parameters.AddWithValue("at", at);
            insert.Parameters.AddWithValue("level", level);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
