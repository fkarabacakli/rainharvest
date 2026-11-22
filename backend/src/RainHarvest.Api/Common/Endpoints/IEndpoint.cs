namespace RainHarvest.Api.Common.Endpoints;

/// <summary>
/// One HTTP entry point of a feature slice. Implementations are discovered
/// automatically and mapped under the <c>/api/v1</c> group.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
