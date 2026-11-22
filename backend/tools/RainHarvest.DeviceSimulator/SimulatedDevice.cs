using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using RainHarvest.Contracts;

namespace RainHarvest.DeviceSimulator;

/// <summary>
/// One simulated ESP32 node. Publishes telemetry every interval and state on
/// every change; obeys <c>cmd</c> and <c>config</c> like the real firmware.
/// Has a retained "offline" Last Will and reconnects on its own.
/// </summary>
public sealed class SimulatedDevice(string deviceId, SimulatorOptions options, Scenario scenario, int seed) : IDisposable
{
    private static readonly TimeSpan FlakyUptime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan FlakyDowntime = TimeSpan.FromSeconds(10);

    private readonly DeviceModel _model = new(new Random(seed), scenario);
    private readonly DeviceController _controller = new();
    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IMqttClient? _client;
    private TelemetryPayload? _latest;
    private TimeSpan _connectedAt;
    private TimeSpan _offlineUntil;

    private TimeSpan SimulatedNow => _clock.Elapsed * options.Speed;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var lastSimulated = TimeSpan.Zero;
        using var timer = new PeriodicTimer(options.Interval);

        try
        {
            do
            {
                IReadOnlyList<StatePayload> changes;
                TelemetryPayload sample;
                lock (_gate)
                {
                    var now = SimulatedNow;
                    var pumpSeconds = _controller.PumpSecondsBetween(lastSimulated, now);
                    _model.Advance(now, now - lastSimulated, pumpSeconds);
                    lastSimulated = now;

                    sample = _model.Sample(now, DateTimeOffset.UtcNow, _controller.PumpOn);
                    _latest = sample;
                    changes = _controller.Tick(sample, now);
                }

                await MaybeDropConnectionAsync();
                if (await EnsureConnectedAsync(stoppingToken))
                {
                    await PublishAsync(MqttTopics.Telemetry, JsonSerializer.SerializeToUtf8Bytes(sample, MqttJson.Options), retain: false, stoppingToken);
                    await PublishStatesAsync(changes, stoppingToken);
                    Log($"telemetry level={sample.LevelPct}% soil={sample.SoilPct}% ph={sample.Ph} " +
                        $"turbidity={sample.TurbidityNtu} rain={(sample.Raining ? "yes" : "no")} " +
                        $"pump={(_controller.PumpOn ? "on" : "off")} flow={sample.FlowTotalL}L");
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
        if (_client is { IsConnected: true })
        {
            return true;
        }

        if (_clock.Elapsed < _offlineUntil)
        {
            return false; // flaky scenario: staying away for a while
        }

        _client?.Dispose();
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e => OnMessageAsync(e.ApplicationMessage, cancellationToken);

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
            await _client.SubscribeAsync(new MqttClientFactory().CreateSubscribeOptionsBuilder()
                .WithTopicFilter(MqttTopics.For(deviceId, MqttTopics.Command), MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter(MqttTopics.For(deviceId, MqttTopics.Config), MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(), cancellationToken);

            await PublishAsync(MqttTopics.Status, Encoding.UTF8.GetBytes(DeviceStatus.Online), retain: true, cancellationToken);
            StatePayload current;
            lock (_gate)
            {
                current = _controller.Current();
            }

            await PublishStatesAsync([current], cancellationToken);
            _connectedAt = _clock.Elapsed;
            Log($"connected to {options.Host}:{options.Port}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"connect failed: {ex.Message}");
            return false;
        }
    }

    private async Task OnMessageAsync(MqttApplicationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.Topic.EndsWith("/" + MqttTopics.Config, StringComparison.Ordinal))
            {
                var config = JsonSerializer.Deserialize<ConfigPayload>(message.Payload.ToArray(), MqttJson.Options);
                if (config is not null)
                {
                    lock (_gate)
                    {
                        _controller.Config = config;
                    }

                    Log($"config pH {config.PhMin}-{config.PhMax}, turbidity ≤{config.TurbidityMaxNtu}, level ≥{config.LevelMinPct}%, pump ≤{config.PumpMaxS}s");
                }

                return;
            }

            var cmd = JsonSerializer.Deserialize<CommandPayload>(message.Payload.ToArray(), MqttJson.Options);
            if (cmd is null)
            {
                return;
            }

            IReadOnlyList<StatePayload> states;
            lock (_gate)
            {
                states = _controller.Handle(cmd, _latest, SimulatedNow);
            }

            var answer = states[^1];
            Log($"cmd {cmd.Action}{(cmd.DurationS is { } d ? $" {d}s" : "")} -> {answer.LastCommandResult}{(answer.Reason is { } r ? $" ({r})" : "")}");
            await PublishStatesAsync(states, cancellationToken);
        }
        catch (JsonException ex)
        {
            Log($"ignored malformed {message.Topic}: {ex.Message}");
        }
    }

    private async Task PublishStatesAsync(IReadOnlyList<StatePayload> states, CancellationToken cancellationToken)
    {
        foreach (var state in states)
        {
            await PublishAsync(MqttTopics.State, JsonSerializer.SerializeToUtf8Bytes(state, MqttJson.Options), retain: true, cancellationToken);
        }
    }

    /// <summary>Flaky scenario: vanish without a DISCONNECT so the broker fires the Last Will.</summary>
    private Task MaybeDropConnectionAsync()
    {
        if (scenario.Flaky && _client is { IsConnected: true } && _clock.Elapsed - _connectedAt > FlakyUptime)
        {
            _client.Dispose(); // closes the socket without a DISCONNECT packet
            _client = null;
            _offlineUntil = _clock.Elapsed + FlakyDowntime;
            Log($"dropped connection (flaky); back in {FlakyDowntime.TotalSeconds:0}s");
        }

        return Task.CompletedTask;
    }

    private Task PublishAsync(string channel, byte[] payload, bool retain, CancellationToken cancellationToken) =>
        _client!.PublishAsync(
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
        if (_client is not { IsConnected: true })
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

    public void Dispose() => _client?.Dispose();
}
