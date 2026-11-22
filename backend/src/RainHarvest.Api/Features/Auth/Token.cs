using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Auth;

namespace RainHarvest.Api.Features.Auth;

/// <summary>
/// POST /api/v1/auth/token — the only way to log in. OAuth2 token endpoint,
/// password grant (RFC 6749 §4.3): form-urlencoded <c>grant_type=password</c>,
/// <c>username</c>, <c>password</c>. Scalar's "Authorize" and the panel both use it.
/// Same request/response format as Keycloak's token endpoint, so moving to a
/// real OAuth2 server later only changes the URL for clients.
/// </summary>
public sealed class Token : IEndpoint
{
    /// <summary>RFC 6749 §5.1 response; <c>user</c> is an allowed additional field.</summary>
    public sealed record Response(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("user")] UserInfo User);

    public sealed record UserInfo(Guid Id, string Username, UserRole Role);

    /// <summary>RFC 6749 §5.2 error body.</summary>
    public sealed record Error(
        [property: JsonPropertyName("error")] string Code,
        [property: JsonPropertyName("error_description")] string Description);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/token", Handle)
            .WithName(nameof(Token))
            .WithTags("Auth")
            .WithSummary("Log in (OAuth2 password grant) and receive a bearer token")
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.RequestBody = FormSchema;
                return Task.CompletedTask;
            })
            .DisableAntiforgery() // not a cookie-based browser form; no CSRF surface
            .AllowAnonymous();

    /// <summary>
    /// Documents the form fields explicitly. The generated schema would only say
    /// "string" for each field, without the allowed grant_type value.
    /// </summary>
    private static readonly OpenApiRequestBody FormSchema = new()
    {
        Required = true,
        Description = "OAuth2 password grant (RFC 6749 §4.3), sent as a form.",
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/x-www-form-urlencoded"] = new()
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Required = new HashSet<string> { "grant_type", "username", "password" },
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["grant_type"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Enum = [JsonValue.Create("password")],
                            Default = JsonValue.Create("password"),
                            Description = "How the token is requested. Only 'password' (log in with username and password) is supported; anything else returns 400 unsupported_grant_type.",
                        },
                        ["username"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Description = "Username (case-insensitive).",
                        },
                        ["password"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "password",
                            Description = "Password. Wrong password or unknown user both return 400 invalid_grant.",
                        },
                    },
                },
            },
        },
    };

    private static async Task<Results<Ok<Response>, BadRequest<Error>>> Handle(
        [FromForm(Name = "grant_type")] string? grantType,
        [FromForm(Name = "username")] string? username,
        [FromForm(Name = "password")] string? password,
        HttpContext http,
        CredentialVerifier verifier,
        JwtTokenIssuer issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Tokens must not be cached (RFC 6749 §5.1).
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";

        if (grantType != "password")
        {
            return TypedResults.BadRequest(new Error("unsupported_grant_type", "Only grant_type=password is supported."));
        }

        var user = await verifier.VerifyAsync(username, password, cancellationToken);
        if (user is null)
        {
            return TypedResults.BadRequest(new Error("invalid_grant", "Username or password is incorrect."));
        }

        var token = issuer.Issue(user);
        var expiresIn = (long)(token.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds;
        return TypedResults.Ok(new Response(
            token.AccessToken, "Bearer", expiresIn, new UserInfo(user.Id, user.Username, user.Role)));
    }
}
