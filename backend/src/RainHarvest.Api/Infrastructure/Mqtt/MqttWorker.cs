using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using RainHarvest.Api.Common.Mqtt;

namespace RainHarvest.Api.Infrastructure.Mqtt;

/// <summary>
/// Keeps the MQTT connection alive (reconnect with exponential backoff),
/// subscribes to every registered handler filter, and feeds received messages
/// through a bounded channel to <see cref="MqttDispatcher"/> one at a time.
/// </summary>
public sealed partial class MqttWorker(
    MqttConnection connection,
    MqttHandlerRegistry registry,
    MqttDispatcher dispatcher,
    IOptions<MqttOptions> options,
    TimeProvider timeProvider,
    ILogger<MqttWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromSeconds(2);

    private readonly Channel<MqttMessage> _inbox = Channel.CreateBounded<MqttMessage>(
        new BoundedChannelOptions(1024) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly string _clientId = string.IsNullOrWhiteSpace(options.Value.ClientId)
        ? $"rainharvest-api-{Guid.NewGuid():N}"[..24]
        : options.Value.ClientId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        connection.Client.ApplicationMessageReceivedAsync += async e =>
        {
            var message = new MqttMessage(
                e.ApplicationMessage.Topic,
                e.ApplicationMessage.Payload.ToArray(),
                e.ApplicationMessage.Retain,
                timeProvider.GetUtcNow());

            await _inbox.Writer.WriteAsync(message, stoppingToken);
        };

        connection.Client.DisconnectedAsync += e =>
        {
            if (e.ClientWasConnected)
            {
                LogDisconnected(e.Reason.ToString());
            }

            return Task.CompletedTask;
        };

        var processing = ProcessInboxAsync(stoppingToken);
        await KeepConnectedAsync(stoppingToken);
        _inbox.Writer.TryComplete();
        await processing;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (connection.IsConnected)
        {
            await connection.Client.DisconnectAsync(cancellationToken: cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task KeepConnectedAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!connection.IsConnected)
            {
                try
                {
                    await ConnectAndSubscribeAsync(stoppingToken);
                    delay = TimeSpan.FromSeconds(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogConnectFailed(ex.Message, delay.TotalSeconds);
                    if (!await DelayAsync(delay, stoppingToken))
                    {
                        return;
                    }

                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.Value.MaxReconnectDelay.Ticks));
                    continue;
                }
            }

            if (!await DelayAsync(ConnectionCheckInterval, stoppingToken))
            {
                return;
            }
        }
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(o.Host, o.Port)
            .WithClientId(_clientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10));

        if (!string.IsNullOrEmpty(o.Username))
        {
            builder.WithCredentials(o.Username, o.Password);
        }

        if (o.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        await connection.Client.ConnectAsync(builder.Build(), cancellationToken);
        LogConnected(o.Host, o.Port, _clientId);

        if (registry.Filters.Count == 0)
        {
            return;
        }

        var subscribe = new MqttClientFactory().CreateSubscribeOptionsBuilder();
        foreach (var filter in registry.Filters)
        {
            subscribe.WithTopicFilter(filter, MqttQualityOfServiceLevel.AtLeastOnce);
        }

        await connection.Client.SubscribeAsync(subscribe.Build(), cancellationToken);
        LogSubscribed(string.Join(", ", registry.Filters));
    }

    private async Task ProcessInboxAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _inbox.Reader.ReadAllAsync(stoppingToken))
            {
                await dispatcher.DispatchAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT connected to {Host}:{Port} as {ClientId}")]
    private partial void LogConnected(string host, int port, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT subscribed to {Filters}")]
    private partial void LogSubscribed(string filters);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT connect failed: {Reason}. Retrying in {DelaySeconds}s")]
    private partial void LogConnectFailed(string reason, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT disconnected: {Reason}")]
    private partial void LogDisconnected(string reason);
}
