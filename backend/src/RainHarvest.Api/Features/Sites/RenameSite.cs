using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>PUT /api/v1/sites/{siteId} — rename a site. Admin only.</summary>
public sealed class RenameSite : IEndpoint
{
    public sealed record Request(string Name);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/sites/{siteId:guid}", Handle)
            .WithName(nameof(RenameSite))
            .WithTags("Sites")
            .WithSummary("Rename a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<SiteDto>, ValidationProblem, ProblemHttpResult>> Handle(
        Guid siteId, Request request, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!SiteDto.TryNormalizeName(request.Name, out var name, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId, cancellationToken);
        if (site is null)
        {
            return SiteProblems.NotFound(siteId);
        }

        site.Name = name;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(SiteDto.From(site));
    }
}
