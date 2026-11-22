using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Settings;
using RainHarvest.Api.Infrastructure.Mqtt;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Commands;

/// <summary>
/// POST /api/v1/devices/{deviceId}/commands — manual irrigation or maintenance.
/// Returns 202: the device decides. Follow the outcome with GET .../commands/{id}
/// (or, from M4, the SignalR <c>CommandUpdated</c> event).
/// </summary>
public sealed class SendCommand : IEndpoint
{
    /// <param name="Action">irrigate, stop, pump_on, pump_off, valve_open, valve_close.</param>
    /// <param name="DurationS">Seconds; required for irrigate and pump_on, at most the device's pumpMaxS.</param>
    public sealed record Request(string Action, int? DurationS = null);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/devices/{deviceId}/commands", Handle)
            .WithName(nameof(SendCommand))
            .WithTags("Commands")
            .WithSummary("Send a command to a device (202; the device decides)");

    private static async Task<Results<Accepted<CommandDto>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId,
        Request request,
        ClaimsPrincipal principal,
        AppDbContext db,
        DeviceSettingsService settingsService,
        CommandSender sender,
        CancellationToken cancellationToken)
    {
        var action = (request.Action ?? "").Trim().ToLowerInvariant();
        if (!CommandActions.All.Contains(action))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["action"] = [$"Must be one of: {string.Join(", ", CommandActions.All.Order())}."],
            });
        }

        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        if (CommandActions.NeedsDuration(action))
        {
            var settings = await settingsService.GetEffectiveAsync(deviceId, cancellationToken);
            if (request.DurationS is not { } d || d < 1 || d > settings.PumpMaxS)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["durationS"] = [$"Required for '{action}': 1–{settings.PumpMaxS} seconds (pumpMaxS)."],
                });
            }
        }

        if (!device.IsOnline)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Device offline",
                detail: $"Device '{deviceId}' is offline; the command would not be delivered.");
        }

        try
        {
            var command = await sender.SendAsync(
                deviceId, action, request.DurationS, CommandSource.Manual,
                CurrentUser.From(principal).Id, reason: null, cancellationToken);

            return TypedResults.Accepted($"/api/v1/devices/{deviceId}/commands/{command.Id}", CommandDto.From(command));
        }
        catch (MqttNotConnectedException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Broker unavailable",
                detail: "The backend is not connected to the MQTT broker; try again shortly.");
        }
    }
}
