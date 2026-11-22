using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// PUT /api/v1/devices/{deviceId}/name — set a device's display name, e.g. for
/// devices that registered themselves and are still named after their id. Admin only.
/// </summary>
public sealed class RenameDevice : IEndpoint
{
    public const int MaxNameLength = 128;

    public sealed record Request(string Name);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/devices/{deviceId}/name", Handle)
            .WithName(nameof(RenameDevice))
            .WithTags("Devices")
            .WithSummary("Rename a device (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<DeviceDto>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId, Request request, AppDbContext db, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"1–{MaxNameLength} characters."],
            });
        }

        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        device.Name = name;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(DeviceDto.From(device));
    }
}
