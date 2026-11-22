using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RainHarvest.Api.Common.Mqtt;

namespace RainHarvest.Api.UnitTests.Mqtt;

public sealed class MqttDispatcherTests
{
    [Fact]
    public void Registry_discovers_attributed_handlers_with_their_filters()
    {
        var registry = MqttHandlerRegistry.FromAssembly(typeof(MqttDispatcherTests).Assembly);

        Assert.Contains(new MqttHandlerRegistration("rainwater/+/telemetry", typeof(TelemetryProbe)), registry.Registrations);
        Assert.Contains(new MqttHandlerRegistration("rainwater/+/telemetry", typeof(ThrowingProbe)), registry.Registrations);
        Assert.Contains(new MqttHandlerRegistration("rainwater/+/status", typeof(StatusProbe)), registry.Registrations);
        Assert.Equal(["rainwater/+/status", "rainwater/+/telemetry"], registry.Filters.Order());
    }

    [Fact]
    public async Task Dispatch_calls_only_matching_handlers_and_survives_a_failing_one()
    {
        var calls = new List<string>();
        var dispatcher = CreateDispatcher(calls);

        await dispatcher.DispatchAsync(Message("rainwater/node1/telemetry"), CancellationToken.None);

        // ThrowingProbe also matches and throws; TelemetryProbe must still run.
        Assert.Equal(["telemetry:rainwater/node1/telemetry"], calls);
    }

    [Fact]
    public async Task Dispatch_ignores_topics_without_handlers()
    {
        var calls = new List<string>();
        var dispatcher = CreateDispatcher(calls);

        await dispatcher.DispatchAsync(Message("rainwater/node1/unknown"), CancellationToken.None);

        Assert.Empty(calls);
    }

    private static MqttDispatcher CreateDispatcher(List<string> calls)
    {
        var registry = MqttHandlerRegistry.FromAssembly(typeof(MqttDispatcherTests).Assembly);
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        foreach (var type in registry.Registrations.Select(r => r.HandlerType).Distinct())
        {
            services.AddScoped(type);
        }

        var provider = services.BuildServiceProvider();
        return new MqttDispatcher(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttDispatcher>.Instance);
    }

    private static MqttMessage Message(string topic) => new(topic, "{}"u8.ToArray(), false, DateTimeOffset.UnixEpoch);

    [MqttTopic("rainwater/+/telemetry")]
    private sealed class TelemetryProbe(List<string> calls) : IMqttHandler
    {
        public Task HandleAsync(MqttMessage message, CancellationToken cancellationToken)
        {
            calls.Add($"telemetry:{message.Topic}");
            return Task.CompletedTask;
        }
    }

    [MqttTopic("rainwater/+/status")]
    private sealed class StatusProbe(List<string> calls) : IMqttHandler
    {
        public Task HandleAsync(MqttMessage message, CancellationToken cancellationToken)
        {
            calls.Add($"status:{message.Topic}");
            return Task.CompletedTask;
        }
    }

    [MqttTopic("rainwater/+/telemetry")]
    private sealed class ThrowingProbe : IMqttHandler
    {
        public Task HandleAsync(MqttMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
