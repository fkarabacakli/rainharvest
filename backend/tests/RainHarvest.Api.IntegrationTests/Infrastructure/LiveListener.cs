using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace RainHarvest.Api.IntegrationTests.Infrastructure;

/// <summary>A SignalR client on the in-memory test server that records every event it gets.</summary>
public sealed class LiveListener : IAsyncDisposable
{
    private static readonly string[] Events =
        ["ReadingReceived", "DeviceStatusChanged", "ActuatorStateChanged", "CommandUpdated", "AlertRaised", "AlertResolved", "AlertAcknowledged"];

    private readonly HubConnection _connection;
    private readonly ConcurrentDictionary<string, Channel<JsonElement>> _inbox = new();

    private LiveListener(HubConnection connection)
    {
        _connection = connection;
        foreach (var name in Events)
        {
            var channel = _inbox.GetOrAdd(name, _ => Channel.CreateUnbounded<JsonElement>());
            _connection.On<JsonElement>(name, payload => channel.Writer.TryWrite(payload));
        }
    }

    public static async Task<LiveListener> ConnectAsync(ApiFixture fixture, string? accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Factory.Server.BaseAddress, "hubs/live"), o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(accessToken);
                o.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // the in-memory server has no real sockets
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var listener = new LiveListener(connection);
        await connection.StartAsync();
        return listener;
    }

    /// <summary>Waits for an <paramref name="eventName"/> whose payload matches <paramref name="match"/>.</summary>
    public async Task<JsonElement> WaitAsync(string eventName, Func<JsonElement, bool> match, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var payload = await _inbox[eventName].Reader.ReadAsync(cts.Token);
            if (match(payload))
            {
                return payload;
            }
        }
    }

    /// <summary>True if no matching <paramref name="eventName"/> arrives within <paramref name="window"/>.</summary>
    public async Task<bool> NothingAsync(string eventName, Func<JsonElement, bool> match, TimeSpan window)
    {
        try
        {
            await WaitAsync(eventName, match, window);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    public static Func<JsonElement, bool> ForDevice(string deviceId) =>
        e => (e.TryGetProperty("deviceId", out var id) ? id.GetString() : null) == deviceId;

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
