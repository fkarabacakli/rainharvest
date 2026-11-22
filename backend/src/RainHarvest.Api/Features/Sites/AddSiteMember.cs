using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>PUT /api/v1/sites/{siteId}/members/{userId} — add a member. Idempotent. Admin only.</summary>
public sealed class AddSiteMember : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/sites/{siteId:guid}/members/{userId:guid}", Handle)
            .WithName(nameof(AddSiteMember))
            .WithTags("Sites")
            .WithSummary("Add a user to a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid siteId, Guid userId, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
        {
            return SiteProblems.NotFound(siteId);
        }

        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return SiteProblems.UserNotFound(userId);
        }

        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO site_members (site_id, user_id, added_at)
            VALUES ({siteId}, {userId}, {now})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);

        return TypedResults.NoContent();
    }
}
