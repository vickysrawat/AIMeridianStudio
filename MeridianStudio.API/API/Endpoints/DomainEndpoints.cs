using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.API.Endpoints;

public static class DomainEndpoints
{
    public static IEndpointRouteBuilder MapDomainEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/domains/discover", HandleAsync)
              .WithName("DiscoverDomains")
              .WithSummary("Return an LLM-curated list of AI-applicable business domains")
              .WithDescription(
                  "Generates a comprehensive list of ~40 distinct industry verticals where AI creates " +
                  "measurable business value. Results are cached for 24 hours. " +
                  "Falls back to the heuristic engine when no LLM providers are configured.")
              .WithTags("Domains")
              .Produces<DomainSuggestions>(StatusCodes.Status200OK);

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        IDomainService service,
        CancellationToken ct)
    {
        var result = await service.DiscoverDomainsAsync(ct);
        return Results.Ok(result);
    }
}
