using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.Guard;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.API.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/generate-document", HandleAsync)
              .WithName("GenerateDocument")
              .WithSummary("Compile a corporate-ready Markdown document")
              .WithDescription(
                  "Accepts a blueprintId, title, and templateType. " +
                  "Supported template types: executive-summary, market-analysis, " +
                  "technical-specification, proposal. Returns a CorporateDocument " +
                  "with a fully formatted Markdown Content field.")
              .WithTags("Document")
              .Produces<CorporateDocument>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        routes.MapPost("/documents/fix", HandleFixAsync)
              .WithName("FixDocumentSection")
              .WithSummary("Repair one section to satisfy a single failed criterion (by-id, deterministic)")
              .WithDescription(
                  "Accepts the structured document (echoed from a prior generate) plus a criterionId. " +
                  "Regenerates only the targeted section, replaces it by id (no duplicates), re-judges " +
                  "only the affected criteria, and returns the updated CorporateDocument.")
              .WithTags("Document")
              .Produces<CorporateDocument>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        routes.MapPost("/documents/review", HandleReviewAsync)
              .WithName("ReviewDocument")
              .WithSummary("Advisory review of a finished document (domain / opportunity-fidelity / faithfulness)")
              .WithDescription(
                  "Reviews a finished document against axes the in-loop goal judge never checks and returns " +
                  "a score + specific findings. Advisory only — never gates generation.")
              .WithTags("Document")
              .Produces<DocumentReview>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        routes.MapPost("/documents/freshness", HandleFreshness)
              .WithName("DocumentFreshness")
              .WithSummary("Is a document still current with its grounding blueprint? (client-side inputs)")
              .WithTags("Document")
              .Produces(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult HandleFreshness(DocumentFreshnessRequest request, PayloadCache cache)
    {
        if (string.IsNullOrWhiteSpace(request.BlueprintId) || string.IsNullOrWhiteSpace(request.GroundedFingerprint))
            return Results.Ok(new { fresh = (bool?)null, reason = "unknown",
                detail = "This document has no grounding fingerprint (legacy or assessment-only)." });

        if (!cache.TryGet<SystemBlueprint>($"bp-by-id:{request.BlueprintId}", out var bp) || bp is null)
            return Results.Ok(new { fresh = (bool?)null, reason = "unknown",
                detail = "The grounding blueprint is no longer in cache — freshness can't be determined." });

        var currentFp = BlueprintFingerprint.Compute(bp);
        var fresh = string.Equals(currentFp, request.GroundedFingerprint, StringComparison.Ordinal);
        return Results.Ok(new
        {
            fresh,
            reason = fresh ? "current" : "stale",
            detail = fresh
                ? "This document reflects the current blueprint."
                : "The blueprint was revised after this document was generated — regenerate to refresh it."
        });
    }

    private static async Task<IResult> HandleReviewAsync(
        DocumentReviewRequest request,
        IDocumentReviewService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["Document content is required."]
            });

        var result = await service.ReviewAsync(request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleFixAsync(
        FixSectionRequest request,
        IDocumentService service,
        CancellationToken ct)
    {
        if (request.Document is null || string.IsNullOrWhiteSpace(request.CriterionId))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Document and criterionId are required."]
            });

        var result = await service.FixSectionAsync(request.Document, request.CriterionId, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleAsync(
        GenerateDocumentRequest request,
        IDocumentService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateDocument(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.GenerateDocumentAsync(request, ct);
        return Results.Ok(result);
    }
}
