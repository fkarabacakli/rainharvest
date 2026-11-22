using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// PUT /api/v1/devices/{deviceId}/site — move a device to a site, or
/// <c>{"siteId": null}</c> to unassign it. Its reading history moves with it. Admin only.
/// </summary>
public sealed class AssignDeviceSite : IEndpoint
{
    public sealed record Request(Guid? SiteId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/devices/{deviceId}/site", Handle)
            .WithName(nameof(AssignDeviceSite))
            .WithTags("Devices")
            .WithSummary("Assign a device to a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<DeviceDto>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId, Request request, AppDbContext db, CancellationToken cancellationToken)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        if (request.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["siteId"] = ["Site does not exist."],
            });
        }

        device.SiteId = request.SiteId;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(DeviceDto.From(device));
    }
}
