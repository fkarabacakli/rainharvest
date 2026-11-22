using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Users;

/// <summary>POST /api/v1/users — Admin only. Usernames are case-insensitive and unique.</summary>
public sealed class CreateUser : IEndpoint
{
    public sealed record Request(string Username, string Password, UserRole Role = UserRole.User);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users", Handle)
            .WithName(nameof(CreateUser))
            .WithTags("Users")
            .WithSummary("Create a user (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Created<UserDto>, ValidationProblem, ProblemHttpResult>> Handle(
        Request request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var username = User.Normalize(request.Username ?? "");
        var errors = new Dictionary<string, string[]>();
        if (!User.IsValidUsername(username))
        {
            errors["username"] = ["3–64 characters: letters, digits, '.', '_' or '-'."];
        }

        if ((request.Password ?? "").Length < User.MinPasswordLength)
        {
            errors["password"] = [$"At least {User.MinPasswordLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken))
        {
            return UsernameTaken(username);
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            PasswordHash = "",
            Role = request.Role,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password!);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return UsernameTaken(username); // lost a race with a concurrent create
        }

        return TypedResults.Created($"/api/v1/users/{user.Id}", UserDto.From(user));
    }

    private static ProblemHttpResult UsernameTaken(string username) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Username taken",
            detail: $"A user named '{username}' already exists.");
}
