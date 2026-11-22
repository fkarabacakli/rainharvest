using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Alerts;

/// <summary>
/// GET /api/v1/alerts?status=active|resolved|all&amp;deviceId&amp;limit — alerts of the
/// devices you can access, newest first. "active" = not resolved yet (default).
/// </summary>
public sealed class ListAlerts : IEndpoint
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/alerts", Handle)
            .WithName(nameof(ListAlerts))
            .WithTags("Alerts")
            .WithSummary("Alerts of your devices");

    private static async Task<Results<Ok<IReadOnlyList<AlertDto>>, ValidationProblem>> Handle(
        string? status,
        string? deviceId,
        int? limit,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var filter = (status ?? "active").ToLowerInvariant();
        var take = limit ?? DefaultLimit;
        var errors = new Dictionary<string, string[]>();
        if (filter is not ("active" or "resolved" or "all"))
        {
            errors["status"] = ["Must be one of: active, resolved, all."];
        }

        if (take is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"'limit' must be between 1 and {MaxLimit}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var visible = db.Devices.VisibleTo(db, CurrentUser.From(principal)).Select(d => d.Id);
        var query = db.Alerts.AsNoTracking().Where(a => visible.Contains(a.DeviceId));

        if (deviceId is not null)
        {
            query = query.Where(a => a.DeviceId == deviceId);
        }

        query = filter switch
        {
            "active" => query.Where(a => a.ResolvedAt == null),
            "resolved" => query.Where(a => a.ResolvedAt != null),
            _ => query,
        };

        var alerts = await query.OrderByDescending(a => a.RaisedAt).Take(take).ToListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<AlertDto>>(alerts.Select(AlertDto.From).ToList());
    }
}
