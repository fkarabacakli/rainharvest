using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Issues the API's own access tokens (PLAN.md D9). Site membership is deliberately not a claim.</summary>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(User user)
    {
        var o = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now + o.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Username),
                new Claim(AuthClaims.Role, user.Role.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(CreateKey(o.SigningKey), SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, expires);
    }

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));
}

public static class AuthClaims
{
    public const string Role = "role";
}
