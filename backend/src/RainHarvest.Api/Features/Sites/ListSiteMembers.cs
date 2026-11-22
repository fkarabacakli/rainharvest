using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>GET /api/v1/sites/{siteId}/members — Admin only.</summary>
public sealed class ListSiteMembers : IEndpoint
{
    public sealed record Member(Guid UserId, string Username, DateTimeOffset AddedAt);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/sites/{siteId:guid}/members", Handle)
            .WithName(nameof(ListSiteMembers))
            .WithTags("Sites")
            .WithSummary("List site members (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<IReadOnlyList<Member>>, ProblemHttpResult>> Handle(
        Guid siteId, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
        {
            return SiteProblems.NotFound(siteId);
        }

        IReadOnlyList<Member> members = await (
                from m in db.SiteMembers
                join u in db.Users on m.UserId equals u.Id
                where m.SiteId == siteId
                orderby u.Username
                select new Member(u.Id, u.Username, m.AddedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(members);
    }
}
