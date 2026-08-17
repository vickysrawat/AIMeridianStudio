using MeridianStudio.API.Infrastructure.Telemetry;

namespace MeridianStudio.API.API.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/telemetry/llm", Handle)
              .WithName("GetLlmTelemetry")
              .WithSummary("Per-session LLM cost and token telemetry")
              .WithDescription(
                  "Returns running totals plus per-provider and per-operation roll-ups and the " +
                  "most recent calls. Token counts are proxy estimates and cost is derived from an " +
                  "approximate per-model rate table — a baseline-measurement aid, not billing. " +
                  "Resets on restart.")
              .WithTags("Telemetry")
              .Produces<LlmTelemetrySnapshot>(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult Handle(ILlmTelemetry telemetry) =>
        Results.Ok(telemetry.Snapshot());
}
