using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>GET /api/v1/sites — admins see all sites, users the sites they are a member of.</summary>
public sealed class ListSites : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/sites", Handle)
            .WithName(nameof(ListSites))
            .WithTags("Sites")
            .WithSummary("List the sites you can access");

    private static async Task<IReadOnlyList<SiteDto>> Handle(
        ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(principal);
        var query = db.Sites.AsNoTracking();
        if (!user.IsAdmin)
        {
            query = query.Where(s => db.SiteMembers.Any(m => m.SiteId == s.Id && m.UserId == user.Id));
        }

        var sites = await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return sites.Select(SiteDto.From).ToList();
    }
}
