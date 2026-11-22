using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// GET /api/v1/devices?siteId — devices the caller may see. Admins see every
/// device, including unassigned ones (<c>unassigned=true</c> lists only those).
/// </summary>
public sealed class ListDevices : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices", Handle)
            .WithName(nameof(ListDevices))
            .WithTags("Devices")
            .WithSummary("List the devices you can access");

    private static async Task<IReadOnlyList<DeviceDto>> Handle(
        Guid? siteId,
        bool? unassigned,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.Devices.AsNoTracking().VisibleTo(db, CurrentUser.From(principal));

        if (siteId is { } site)
        {
            query = query.Where(d => d.SiteId == site);
        }

        if (unassigned == true)
        {
            query = query.Where(d => d.SiteId == null);
        }

        var devices = await query.OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return devices.Select(DeviceDto.From).ToList();
    }
}
