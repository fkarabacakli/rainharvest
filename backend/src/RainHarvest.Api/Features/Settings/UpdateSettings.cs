using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Settings;

/// <summary>
/// PUT /api/v1/devices/{deviceId}/settings — replace all thresholds, then push the
/// device-side subset as retained MQTT config. Admin only.
/// </summary>
public sealed class UpdateSettings : IEndpoint
{
    public sealed record Request(
        double PhMin,
        double PhMax,
        double TurbidityMaxNtu,
        double LevelMinPct,
        double SoilDryPct,
        double SoilWetPct,
        int IrrigationDurationS,
        int IrrigationCooldownS,
        int PumpMaxS,
        bool AutoIrrigation);

    /// <param name="ConfigPublished">False if MQTT was down; the device gets it when it next comes online.</param>
    public sealed record Response(DeviceSettingsDto Settings, bool ConfigPublished);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/devices/{deviceId}/settings", Handle)
            .WithName(nameof(UpdateSettings))
            .WithTags("Settings")
            .WithSummary("Replace device thresholds (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId,
        Request request,
        ClaimsPrincipal principal,
        AppDbContext db,
        DeviceSettingsService service,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        var candidate = new DeviceSettings
        {
            DeviceId = deviceId,
            PhMin = request.PhMin,
            PhMax = request.PhMax,
            TurbidityMaxNtu = request.TurbidityMaxNtu,
            LevelMinPct = request.LevelMinPct,
            SoilDryPct = request.SoilDryPct,
            SoilWetPct = request.SoilWetPct,
            IrrigationDurationS = request.IrrigationDurationS,
            IrrigationCooldownS = request.IrrigationCooldownS,
            PumpMaxS = request.PumpMaxS,
            AutoIrrigation = request.AutoIrrigation,
            UpdatedAt = timeProvider.GetUtcNow(),
            UpdatedBy = CurrentUser.From(principal).Id,
        };

        var errors = candidate.Validate();
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var existing = await db.DeviceSettings.SingleOrDefaultAsync(s => s.DeviceId == deviceId, cancellationToken);
        if (existing is null)
        {
            db.DeviceSettings.Add(candidate);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(candidate);
        }

        await db.SaveChangesAsync(cancellationToken);
        var published = await service.PublishConfigAsync(candidate, cancellationToken);

        return TypedResults.Ok(new Response(DeviceSettingsDto.From(candidate), published));
    }
}
