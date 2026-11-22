using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Sites;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Auth;

/// <summary>GET /api/v1/auth/me — the caller and the sites they are a member of.</summary>
public sealed class GetCurrentUser : IEndpoint
{
    public sealed record Response(Guid Id, string Username, UserRole Role, IReadOnlyList<SiteDto> Sites);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/me", Handle)
            .WithName(nameof(GetCurrentUser))
            .WithTags("Auth")
            .WithSummary("Current user and their sites");

    private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        var caller = CurrentUser.From(principal);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == caller.Id, cancellationToken);
        if (user is null)
        {
            // Token is valid but the user was deleted since it was issued.
            return TypedResults.Unauthorized();
        }

        var sites = await db.Sites.AsNoTracking()
            .Where(s => db.SiteMembers.Any(m => m.SiteId == s.Id && m.UserId == user.Id))
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(user.Id, user.Username, user.Role, sites.Select(SiteDto.From).ToList()));
    }
}
