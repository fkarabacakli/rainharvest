using Microsoft.EntityFrameworkCore;
using Npgsql;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Alerts;

/// <summary>Opens and resolves alerts idempotently and announces each change.</summary>
public sealed class AlertService(AppDbContext db, DomainEventPublisher events, TimeProvider timeProvider)
{
    public static AlertSeverity SeverityOf(AlertType type) =>
        type == AlertType.DeviceOffline ? AlertSeverity.Critical : AlertSeverity.Warning;

    /// <summary>Opens an alert unless one of this type is already open for the device.</summary>
    public async Task RaiseAsync(string deviceId, AlertType type, string message, CancellationToken cancellationToken)
    {
        if (await db.Alerts.AnyAsync(a => a.DeviceId == deviceId && a.Type == type && a.ResolvedAt == null, cancellationToken))
        {
            return;
        }

        var alert = new Alert
        {
            Id = Guid.CreateVersion7(),
            DeviceId = deviceId,
            Type = type,
            Severity = SeverityOf(type),
            Message = message,
            RaisedAt = timeProvider.GetUtcNow(),
        };
        db.Alerts.Add(alert);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(alert).State = EntityState.Detached; // someone else opened it first
            return;
        }

        await events.PublishAsync(new AlertChanged(AlertDto.From(alert), AlertChangeKind.Raised), cancellationToken);
    }

    /// <summary>Closes the open alert of this type, if any.</summary>
    public async Task ResolveAsync(string deviceId, AlertType type, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.SingleOrDefaultAsync(
            a => a.DeviceId == deviceId && a.Type == type && a.ResolvedAt == null, cancellationToken);
        if (alert is null)
        {
            return;
        }

        alert.ResolvedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new AlertChanged(AlertDto.From(alert), AlertChangeKind.Resolved), cancellationToken);
    }
}
