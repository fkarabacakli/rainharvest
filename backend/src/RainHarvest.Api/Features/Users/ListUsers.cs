using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Users;

/// <summary>GET /api/v1/users — Admin only.</summary>
public sealed class ListUsers : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users", Handle)
            .WithName(nameof(ListUsers))
            .WithTags("Users")
            .WithSummary("List users (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<IReadOnlyList<UserDto>> Handle(AppDbContext db, CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);
        return users.Select(UserDto.From).ToList();
    }
}
