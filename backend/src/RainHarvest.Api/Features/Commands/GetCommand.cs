using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Commands;

/// <summary>GET /api/v1/devices/{deviceId}/commands/{commandId} — poll a command's outcome.</summary>
public sealed class GetCommand : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/commands/{commandId:guid}", Handle)
            .WithName(nameof(GetCommand))
            .WithTags("Commands")
            .WithSummary("A single command and its status");

    private static async Task<Results<Ok<CommandDto>, ProblemHttpResult>> Handle(
        string deviceId, Guid commandId, AppDbContext db, CancellationToken cancellationToken)
    {
        var command = await db.DeviceCommands.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == commandId && c.DeviceId == deviceId, cancellationToken);

        return command is null
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Command not found",
                detail: $"No command '{commandId}' for device '{deviceId}'.")
            : TypedResults.Ok(CommandDto.From(command));
    }
}
