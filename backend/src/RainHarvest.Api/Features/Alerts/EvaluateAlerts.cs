using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Settings;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Alerts;

/// <summary>Turns readings and connection changes into alerts.</summary>
public sealed class EvaluateAlerts(AppDbContext db, DeviceSettingsService settingsService, AlertService alerts)
    : IDomainEventHandler<TelemetryReceived>, IDomainEventHandler<DeviceStatusChanged>
{
    public async Task HandleAsync(TelemetryReceived e, CancellationToken cancellationToken)
    {
        var deviceId = e.Reading.DeviceId;
        var settings = await settingsService.GetEffectiveAsync(deviceId, cancellationToken);
        var open = (await db.Alerts
                .Where(a => a.DeviceId == deviceId && a.ResolvedAt == null)
                .Select(a => a.Type)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var change in AlertRule.Evaluate(e.Reading, settings, open))
        {
            if (change.Raise)
            {
                await alerts.RaiseAsync(deviceId, change.Type, change.Message, cancellationToken);
            }
            else
            {
                await alerts.ResolveAsync(deviceId, change.Type, cancellationToken);
            }
        }
    }

    public Task HandleAsync(DeviceStatusChanged e, CancellationToken cancellationToken) =>
        e.IsOnline
            ? alerts.ResolveAsync(e.DeviceId, AlertType.DeviceOffline, cancellationToken)
            : alerts.RaiseAsync(e.DeviceId, AlertType.DeviceOffline,
                "Device went offline (no connection to the broker).", cancellationToken);
}
