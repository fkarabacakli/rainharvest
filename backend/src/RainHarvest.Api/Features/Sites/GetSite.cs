using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>GET /api/v1/sites/{siteId} — the site and its devices. Non-members get 404.</summary>
public sealed class GetSite : IEndpoint
{
    public sealed record Response(SiteDto Site, IReadOnlyList<DeviceDto> Devices);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/sites/{siteId:guid}", Handle)
            .WithName(nameof(GetSite))
            .WithTags("Sites")
            .WithSummary("Site detail with its devices");

    private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
        Guid siteId, ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(principal);
        var site = await db.Sites.AsNoTracking()
            .Where(s => s.Id == siteId &&
                        (user.IsAdmin || db.SiteMembers.Any(m => m.SiteId == s.Id && m.UserId == user.Id)))
            .SingleOrDefaultAsync(cancellationToken);

        if (site is null)
        {
            return SiteProblems.NotFound(siteId);
        }

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.SiteId == siteId)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(SiteDto.From(site), devices.Select(DeviceDto.From).ToList()));
    }
}
