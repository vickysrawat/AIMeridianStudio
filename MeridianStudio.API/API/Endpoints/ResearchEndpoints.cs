using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class ResearchEndpoints
{
    public static IEndpointRouteBuilder MapResearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/research", HandleAsync)
              .WithName("Research")
              .WithSummary("AI-assisted domain discovery")
              .WithDescription(
                  "Receives search keywords and optional user feedback. " +
                  "Set loadMore=true and populate existingItemIds to paginate " +
                  "without receiving duplicate PrioritizedItems.")
              .WithTags("Research")
              .Produces<ResearchResponse>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        ResearchRequest request,
        IResearchService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateResearch(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.ResearchAsync(request, ct);
        return Results.Ok(result);
    }
}
