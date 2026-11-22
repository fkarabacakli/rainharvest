using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using RainHarvest.Contracts;

namespace RainHarvest.DeviceSimulator;

/// <summary>
/// One simulated ESP32: its own MQTT connection with a retained "offline"
/// Last Will, a retained "online" status after connecting, and telemetry
/// every interval. Reconnects on its own if the broker drops it.
/// </summary>
public sealed class SimulatedDevice(string deviceId, SimulatorOptions options, int seed) : IDisposable
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly DeviceModel _model = new(new Random(seed));

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var clock = Stopwatch.StartNew();
        var lastSimulated = TimeSpan.Zero;
        using var timer = new PeriodicTimer(options.Interval);

        try
        {
            do
            {
                var simulatedNow = clock.Elapsed * options.Speed;
                _model.Advance(simulatedNow, simulatedNow - lastSimulated);
                lastSimulated = simulatedNow;

                if (await EnsureConnectedAsync(stoppingToken))
                {
                    var payload = _model.Sample(simulatedNow, DateTimeOffset.UtcNow);
                    await PublishAsync(
                        MqttTopics.Telemetry,
                        JsonSerializer.SerializeToUtf8Bytes(payload, MqttJson.Options),
                        retain: false,
                        stoppingToken);
                    Log($"telemetry level={payload.LevelPct}% soil={payload.SoilPct}% ph={payload.Ph} " +
                        $"turbidity={payload.TurbidityNtu} rain={(payload.Raining ? "yes" : "no")}");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await ShutdownAsync();
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return true;
        }

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId($"sim-{deviceId}")
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
            .WithTimeout(TimeSpan.FromSeconds(10))
            .WithWillTopic(MqttTopics.For(deviceId, MqttTopics.Status))
            .WithWillPayload(Encoding.UTF8.GetBytes(DeviceStatus.Offline))
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(options.Username))
        {
            builder.WithCredentials(options.Username, options.Password);
        }

        if (options.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        try
        {
            await _client.ConnectAsync(builder.Build(), cancellationToken);
            await PublishAsync(MqttTopics.Status, Encoding.UTF8.GetBytes(DeviceStatus.Online), retain: true, cancellationToken);
            Log($"connected to {options.Host}:{options.Port}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"connect failed: {ex.Message}");
            return false;
        }
    }

    private Task PublishAsync(string channel, byte[] payload, bool retain, CancellationToken cancellationToken) =>
        _client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(MqttTopics.For(deviceId, channel))
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retain)
                .Build(),
            cancellationToken);

    /// <summary>A clean disconnect does not trigger the Last Will, so say "offline" ourselves.</summary>
    private async Task ShutdownAsync()
    {
        if (!_client.IsConnected)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await PublishAsync(MqttTopics.Status, Encoding.UTF8.GetBytes(DeviceStatus.Offline), retain: true, timeout.Token);
            await _client.DisconnectAsync(cancellationToken: timeout.Token);
            Log("offline");
        }
        catch (Exception ex)
        {
            Log($"shutdown failed: {ex.Message}");
        }
    }

    private void Log(string message) => Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} [{deviceId}] {message}");

    public void Dispose() => _client.Dispose();
}
