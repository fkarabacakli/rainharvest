using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Features.Alerts;
using RainHarvest.Api.Features.Commands;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Live;

/// <summary>Events the server pushes; see docs/realtime.md.</summary>
public interface ILiveClient
{
    Task ReadingReceived(LiveReading message);
    Task DeviceStatusChanged(DeviceStatusChanged message);
    Task ActuatorStateChanged(ActuatorStateChanged message);
    Task CommandUpdated(CommandDto message);
    Task AlertRaised(AlertDto message);
    Task AlertResolved(AlertDto message);
    Task AlertAcknowledged(AlertDto message);
}

public sealed record LiveReading(string DeviceId, Telemetry.ReadingDto Reading);

/// <summary>
/// /hubs/live — server-to-client only. On connect the caller joins one group per
/// site they belong to (admins join "admins"), so events reach exactly the
/// people who may see the device. Membership changes apply on reconnect.
/// </summary>
[Authorize]
public sealed class LiveHub(AppDbContext db) : Hub<ILiveClient>
{
    public const string Path = "/hubs/live";
    public const string AdminsGroup = "admins";

    public static string SiteGroup(Guid siteId) => $"site:{siteId}";

    public override async Task OnConnectedAsync()
    {
        var user = CurrentUser.From(Context.User!);
        if (user.IsAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }
        else
        {
            var siteIds = await db.SiteMembers.AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Select(m => m.SiteId)
                .ToListAsync(Context.ConnectionAborted);

            foreach (var siteId in siteIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, SiteGroup(siteId));
            }
        }

        await base.OnConnectedAsync();
    }
}
