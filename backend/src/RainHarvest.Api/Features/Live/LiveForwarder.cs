using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Features.Alerts;
using RainHarvest.Api.Features.Commands;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Live;

/// <summary>
/// Pushes domain events to SignalR: to the device's site group and to admins.
/// Unassigned devices reach admins only, mirroring the REST access rules.
/// </summary>
public sealed class LiveForwarder(AppDbContext db, IHubContext<LiveHub, ILiveClient> hub) :
    IDomainEventHandler<TelemetryReceived>,
    IDomainEventHandler<DeviceStatusChanged>,
    IDomainEventHandler<ActuatorStateChanged>,
    IDomainEventHandler<CommandUpdated>,
    IDomainEventHandler<AlertChanged>
{
    public async Task HandleAsync(TelemetryReceived e, CancellationToken cancellationToken) =>
        await (await AudienceAsync(e.Reading.DeviceId, cancellationToken))
            .ReadingReceived(new LiveReading(e.Reading.DeviceId, ReadingDto.From(e.Reading)));

    public async Task HandleAsync(DeviceStatusChanged e, CancellationToken cancellationToken) =>
        await (await AudienceAsync(e.DeviceId, cancellationToken)).DeviceStatusChanged(e);

    public async Task HandleAsync(ActuatorStateChanged e, CancellationToken cancellationToken) =>
        await (await AudienceAsync(e.DeviceId, cancellationToken)).ActuatorStateChanged(e);

    public async Task HandleAsync(CommandUpdated e, CancellationToken cancellationToken) =>
        await (await AudienceAsync(e.Command.DeviceId, cancellationToken)).CommandUpdated(e.Command);

    public async Task HandleAsync(AlertChanged e, CancellationToken cancellationToken)
    {
        var audience = await AudienceAsync(e.Alert.DeviceId, cancellationToken);
        await (e.Kind switch
        {
            AlertChangeKind.Raised => audience.AlertRaised(e.Alert),
            AlertChangeKind.Resolved => audience.AlertResolved(e.Alert),
            _ => audience.AlertAcknowledged(e.Alert),
        });
    }

    private async Task<ILiveClient> AudienceAsync(string deviceId, CancellationToken cancellationToken)
    {
        var siteId = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => d.SiteId)
            .SingleOrDefaultAsync(cancellationToken);

        return siteId is { } site
            ? hub.Clients.Groups(LiveHub.AdminsGroup, LiveHub.SiteGroup(site))
            : hub.Clients.Group(LiveHub.AdminsGroup);
    }
}
