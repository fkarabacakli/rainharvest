using Microsoft.AspNetCore.Http.HttpResults;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Sites;

/// <summary>POST /api/v1/sites — Admin only.</summary>
public sealed class CreateSite : IEndpoint
{
    public sealed record Request(string Name);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/sites", Handle)
            .WithName(nameof(CreateSite))
            .WithTags("Sites")
            .WithSummary("Create a site (admin)")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Created<SiteDto>, ValidationProblem>> Handle(
        Request request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!SiteDto.TryNormalizeName(request.Name, out var name, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var site = new Site { Id = Guid.CreateVersion7(), Name = name, CreatedAt = timeProvider.GetUtcNow() };
        db.Sites.Add(site);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/sites/{site.Id}", SiteDto.From(site));
    }
}
