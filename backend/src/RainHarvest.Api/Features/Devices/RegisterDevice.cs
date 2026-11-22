using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// POST /api/v1/devices — pre-register a device (plug and play, PLAN.md D22).
/// When the device first connects, its telemetry only updates online state,
/// so the name and site set here are kept. Admin only.
/// </summary>
public sealed class RegisterDevice : IEndpoint
{
    public sealed record Request(string Id, string? Name = null, Guid? SiteId = null);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/devices", Handle)
            .WithName(nameof(RegisterDevice))
            .WithTags("Devices")
            .WithSummary("Pre-register a device, optionally in a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Created<DeviceDto>, ValidationProblem, ProblemHttpResult>> Handle(
        Request request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var id = (request.Id ?? "").Trim();
        var name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim();
        var errors = new Dictionary<string, string[]>();

        if (!Device.IsValidId(id))
        {
            errors["id"] = [$"1–{Device.MaxIdLength} characters: letters, digits, '-' or '_'."];
        }

        if (name.Length > 128)
        {
            errors["name"] = ["At most 128 characters."];
        }

        if (request.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
        {
            errors["siteId"] = ["Site does not exist."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.Devices.AnyAsync(d => d.Id == id, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Device already exists",
                detail: $"Device '{id}' is already registered (it may have connected already). " +
                        $"Use PUT /api/v1/devices/{id}/site to assign it.");
        }

        var device = new Device
        {
            Id = id,
            Name = name,
            SiteId = request.SiteId,
            IsOnline = false,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Devices.Add(device);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The device connected between our check and the insert; assign it instead.
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Device already exists",
                detail: $"Device '{id}' connected meanwhile. Use PUT /api/v1/devices/{id}/site to assign it.");
        }

        return TypedResults.Created($"/api/v1/devices/{id}", DeviceDto.From(device));
    }
}
