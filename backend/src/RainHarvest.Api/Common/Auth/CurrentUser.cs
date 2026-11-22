using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Auth;

namespace RainHarvest.Api.Common.Auth;

/// <summary>The authenticated caller, read from the validated JWT.</summary>
public sealed record CurrentUser(Guid Id, string Username, bool IsAdmin)
{
    public static CurrentUser From(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out var id))
        {
            // Only reachable if an endpoint that allows anonymous access asks for the user.
            throw new InvalidOperationException("The request is not authenticated.");
        }

        return new CurrentUser(
            id,
            principal.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "",
            principal.FindFirstValue(AuthClaims.Role) == nameof(UserRole.Admin));
    }
}

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);
}
