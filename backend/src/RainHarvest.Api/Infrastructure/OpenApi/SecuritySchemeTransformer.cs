using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RainHarvest.Api.Infrastructure.OpenApi;

/// <summary>
/// Declares the single OAuth2 (password flow) security scheme against
/// POST /auth/token and marks every operation that is not AllowAnonymous as
/// requiring it. Clients send the token as <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
internal sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string OAuth2Scheme = "OAuth2";
    public const string TokenUrl = "/api/v1/auth/token";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[OAuth2Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Log in with username and password; send the token as 'Authorization: Bearer <token>'.",
            Flows = new OpenApiOAuthFlows
            {
                Password = new OpenApiOAuthFlow
                {
                    TokenUrl = new Uri(TokenUrl, UriKind.Relative),
                    Scopes = new Dictionary<string, string>(),
                },
            },
        };

        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(OAuth2Scheme, context.Document)] = [],
        });

        return Task.CompletedTask;
    }
}
