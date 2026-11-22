using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Auth;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Auth;

/// <summary>POST /api/v1/auth/login — exchanges username + password for a JWT access token.</summary>
public sealed class Login : IEndpoint
{
    public sealed record Request(string Username, string Password);

    public sealed record UserInfo(Guid Id, string Username, UserRole Role);

    public sealed record Response(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserInfo User);

    // Used to spend the same hashing time for unknown usernames, so response
    // time does not reveal which usernames exist.
    private static readonly User TimingDummy = new() { Username = "-", PasswordHash = "" };
    private static readonly string TimingDummyHash = new PasswordHasher<User>().HashPassword(TimingDummy, "timing-dummy");

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", Handle)
            .WithName(nameof(Login))
            .WithTags("Auth")
            .WithSummary("Log in and receive a bearer token")
            .AllowAnonymous();

    private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
        Request request, AppDbContext db, JwtTokenIssuer issuer, CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<User>();
        var username = User.Normalize(request.Username ?? "");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            hasher.VerifyHashedPassword(TimingDummy, TimingDummyHash, request.Password ?? "");
            return InvalidCredentials();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = issuer.Issue(user);
        return TypedResults.Ok(new Response(
            token.AccessToken, "Bearer", token.ExpiresAt, new UserInfo(user.Id, user.Username, user.Role)));
    }

    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials",
            detail: "Username or password is incorrect.");
}
