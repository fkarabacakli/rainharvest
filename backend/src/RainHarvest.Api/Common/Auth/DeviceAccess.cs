using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Common.Auth;

/// <summary>
/// Who may see which device: admins see all; users see devices whose site they
/// are a member of. Unassigned devices are admin-only. Checked against the
/// database on every request, so removing a membership takes effect at once.
/// </summary>
public static class DeviceAccess
{
    public const string RouteParameter = "deviceId";

    public static IQueryable<Device> VisibleTo(this IQueryable<Device> devices, AppDbContext db, CurrentUser user) =>
        user.IsAdmin
            ? devices
            : devices.Where(d => d.SiteId != null &&
                                 db.SiteMembers.Any(m => m.SiteId == d.SiteId && m.UserId == user.Id));

    /// <summary>
    /// Group-wide endpoint filter factory. Any handler with a <c>deviceId</c>
    /// parameter gets the access check automatically; an outsider receives the
    /// same 404 as for a device that does not exist, so ids cannot be probed.
    /// </summary>
    public static EndpointFilterDelegate FilterFactory(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        if (!context.MethodInfo.GetParameters().Any(IsDeviceIdParameter))
        {
            return next;
        }

        return async invocation =>
        {
            var http = invocation.HttpContext;
            var user = CurrentUser.From(http.User);
            if (user.IsAdmin)
            {
                return await next(invocation);
            }

            var deviceId = http.GetRouteValue(RouteParameter) as string ?? "";
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var allowed = await db.Devices.VisibleTo(db, user).AnyAsync(d => d.Id == deviceId, http.RequestAborted);

            return allowed ? await next(invocation) : ApiProblems.DeviceNotFound(deviceId);
        };
    }

    private static bool IsDeviceIdParameter(ParameterInfo p) =>
        p.Name == RouteParameter && p.ParameterType == typeof(string);
}
