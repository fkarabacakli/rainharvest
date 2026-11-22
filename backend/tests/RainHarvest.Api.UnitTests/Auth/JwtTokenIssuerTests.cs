using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Auth;

namespace RainHarvest.Api.UnitTests.Auth;

public sealed class JwtTokenIssuerTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "unit-test-signing-key-0123456789abcdef",
        AccessTokenLifetime = TimeSpan.FromHours(8),
    };

    private static readonly User Alice = new()
    {
        Id = Guid.Parse("0193a0b4-0000-7000-8000-000000000001"),
        Username = "alice",
        PasswordHash = "",
        Role = UserRole.User,
    };

    [Fact]
    public async Task Issued_token_validates_and_carries_identity_but_no_sites()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var issued = new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(Options), clock).Issue(Alice);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, Parameters());

        Assert.True(result.IsValid, result.Exception?.Message);
        var user = CurrentUser.From(new System.Security.Claims.ClaimsPrincipal(result.ClaimsIdentity));
        Assert.Equal(new CurrentUser(Alice.Id, "alice", IsAdmin: false), user);
        Assert.Equal(clock.GetUtcNow().AddHours(8), issued.ExpiresAt);
        Assert.DoesNotContain(result.ClaimsIdentity.Claims, c => c.Type.Contains("site", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Admin_role_is_recognised()
    {
        var admin = new User { Id = Guid.NewGuid(), Username = "root", PasswordHash = "", Role = UserRole.Admin };
        var issued = new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System).Issue(admin);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, Parameters());

        Assert.True(CurrentUser.From(new System.Security.Claims.ClaimsPrincipal(result.ClaimsIdentity)).IsAdmin);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var other = new JwtOptions { SigningKey = "a-completely-different-key-0123456789" };
        var issued = new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(other), TimeProvider.System).Issue(Alice);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, Parameters());

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("exactly-thirty-two-bytes-long!!!", true)]
    public void Signing_key_must_be_at_least_256_bits(string key, bool valid)
    {
        var errors = new JwtOptions { SigningKey = key }.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(new object()));

        Assert.Equal(valid, !errors.Any());
    }

    [Theory]
    [InlineData("  Alice ", "alice", true)]
    [InlineData("bob.smith_2", "bob.smith_2", true)]
    [InlineData("ab", "ab", false)]
    [InlineData("has space", "has space", false)]
    public void Usernames_are_normalized_and_validated(string raw, string normalized, bool valid)
    {
        Assert.Equal(normalized, User.Normalize(raw));
        Assert.Equal(valid, User.IsValidUsername(User.Normalize(raw)));
    }

    private static TokenValidationParameters Parameters() => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudience = Options.Audience,
        IssuerSigningKey = JwtTokenIssuer.CreateKey(Options.SigningKey),
    };
}
