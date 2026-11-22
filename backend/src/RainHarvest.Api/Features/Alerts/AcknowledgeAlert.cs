using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Alerts;

/// <summary>
/// POST /api/v1/alerts/{alertId}/ack — mark an alert as seen. Idempotent; does not
/// resolve it (the condition must clear for that). Same access as the device.
/// </summary>
public sealed class AcknowledgeAlert : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/alerts/{alertId:guid}/ack", Handle)
            .WithName(nameof(AcknowledgeAlert))
            .WithTags("Alerts")
            .WithSummary("Acknowledge an alert");

    private static async Task<Results<Ok<AlertDto>, ProblemHttpResult>> Handle(
        Guid alertId,
        ClaimsPrincipal principal,
        AppDbContext db,
        DomainEventPublisher events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(principal);
        var visible = db.Devices.VisibleTo(db, user).Select(d => d.Id);
        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.Id == alertId && visible.Contains(a.DeviceId), cancellationToken);
        if (alert is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Alert not found",
                detail: $"No alert with id '{alertId}'.");
        }

        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = timeProvider.GetUtcNow();
            alert.AcknowledgedBy = user.Id;
            await db.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new AlertChanged(AlertDto.From(alert), AlertChangeKind.Acknowledged), cancellationToken);
        }

        return TypedResults.Ok(AlertDto.From(alert));
    }
}
