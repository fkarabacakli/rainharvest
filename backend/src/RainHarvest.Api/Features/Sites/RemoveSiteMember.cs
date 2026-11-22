using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>
/// DELETE /api/v1/sites/{siteId}/members/{userId} — remove a member. Idempotent.
/// Takes effect on the user's next request (access is not cached in the token). Admin only.
/// </summary>
public sealed class RemoveSiteMember : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/sites/{siteId:guid}/members/{userId:guid}", Handle)
            .WithName(nameof(RemoveSiteMember))
            .WithTags("Sites")
            .WithSummary("Remove a user from a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid siteId, Guid userId, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
        {
            return SiteProblems.NotFound(siteId);
        }

        await db.SiteMembers.Where(m => m.SiteId == siteId && m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
