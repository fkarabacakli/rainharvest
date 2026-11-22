using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RainHarvest.Api.Infrastructure.Mqtt;

public sealed class MqttHealthCheck(MqttConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(connection.IsConnected
            ? HealthCheckResult.Healthy("Connected to the MQTT broker.")
            : HealthCheckResult.Unhealthy("Not connected to the MQTT broker."));
}
