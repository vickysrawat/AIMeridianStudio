using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.API.Endpoints;

public static class ProviderStatusEndpoints
{
    public static IEndpointRouteBuilder MapProviderStatusEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/providers/status", Handle)
              .WithName("GetProviderStatuses")
              .WithSummary("Configuration and runtime status of all LLM providers")
              .WithDescription(
                  "Returns each provider in priority order with its configured state " +
                  "and the last known runtime status (active, idle, failed, quota, not-configured, fallback).")
              .WithTags("Providers")
              .Produces<ProviderStatusItem[]>(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult Handle(LLMOrchestrator orchestrator) =>
        Results.Ok(orchestrator.GetProviderStatuses());
}
