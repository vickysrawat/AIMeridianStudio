using MeridianStudio.API.Infrastructure.Diagnostics;

namespace MeridianStudio.API.API.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/diagnostics/self-check", Handle)
              .WithName("SelfCheck")
              .WithSummary("Deterministic offline invariant checks over the retrieval/budget/embedding machinery")
              .WithDescription(
                  "Runs without API keys. Verifies the structural guarantees the pipeline relies on " +
                  "(chunker keeps fences whole, budget stays bounded, compactor respects budget, cosine " +
                  "safety, embedding self-similarity, domain classification). Returns 200 when all pass, " +
                  "500 otherwise. Not the LLM golden-set evaluation — that requires live keys and graded briefs.")
              .WithTags("Diagnostics")
              .Produces<SelfCheckReport>(StatusCodes.Status200OK)
              .Produces<SelfCheckReport>(StatusCodes.Status500InternalServerError);

        // Mermaid self-healing review surfaces (turn samples into new catalog rules / promote fixes).
        routes.MapGet("/diagnostics/learned-fixes", (ILearnedMermaidFixStore store, int? take) =>
                  Results.Ok(store.Recent(take ?? 50)))
              .WithName("LearnedMermaidFixes")
              .WithSummary("Recently learned (verified) Mermaid repairs, reused deterministically")
              .WithTags("Diagnostics");

        routes.MapGet("/diagnostics/mermaid-failures", (ILearnedMermaidFixStore store, int? take) =>
                  Results.Ok(store.RecentUnresolved(take ?? 50)))
              .WithName("MermaidUnresolved")
              .WithSummary("Diagrams no deterministic rule (nor the LLM tier) could fix — candidates for a new rule")
              .WithTags("Diagnostics");

        return routes;
    }

    private static async Task<IResult> Handle(SelfCheckService selfCheck, CancellationToken ct)
    {
        var report = await selfCheck.RunAsync(ct);
        return report.AllPassed
            ? Results.Ok(report)
            : Results.Json(report, statusCode: StatusCodes.Status500InternalServerError);
    }
}
