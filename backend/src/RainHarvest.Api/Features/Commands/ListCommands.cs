using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Commands;

/// <summary>GET /api/v1/devices/{deviceId}/commands?limit — newest first (manual and automatic).</summary>
public sealed class ListCommands : IEndpoint
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/commands", Handle)
            .WithName(nameof(ListCommands))
            .WithTags("Commands")
            .WithSummary("Command history of a device");

    private static async Task<Results<Ok<IReadOnlyList<CommandDto>>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId, int? limit, AppDbContext db, CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"'limit' must be between 1 and {MaxLimit}."],
            });
        }

        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        var commands = await db.DeviceCommands.AsNoTracking()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CommandDto>>(commands.Select(CommandDto.From).ToList());
    }
}
