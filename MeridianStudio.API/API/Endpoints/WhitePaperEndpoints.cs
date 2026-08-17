using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Services;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.API.Endpoints;

public static class WhitePaperEndpoints
{
    public static IEndpointRouteBuilder MapWhitePaperEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/whitepaper", Generate)
              .WithName("GenerateWhitePaper")
              .WithTags("WhitePaper")
              .WithSummary("Synthesize an LLM-written white paper from stored artifacts");
        return routes;
    }

    private static async Task<IResult> Generate(
        WhitePaperRequest request, WhitePaperService service, ITenantAccessor tenant,
        ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        var hasDriver = !string.IsNullOrWhiteSpace(request.ResearchArtifactId)
                        || !string.IsNullOrWhiteSpace(request.AssessmentId)
                        || request.ArtifactIds is { Count: > 0 };
        if (!hasDriver)
            return Results.BadRequest(new { error = "Provide a researchArtifactId (+optional opportunityId), an assessmentId, or artifactIds." });

        try
        {
            var paper = await service.SynthesizeAsync(request, ct);
            if (paper is null)
                return Results.BadRequest(new { error = "The requested research / assessment / artifacts were not found in your tenant." });

            log.LogInformation("[Audit] whitepaper '{Title}' by {User}@{Tenant} — {Model}",
                paper.Title, tenant.UserId ?? "anon", tenant.TenantId, paper.ModelUsed);
            return Results.Ok(paper);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[WhitePaper] synthesis failed.");
            return Results.Problem(title: "White paper synthesis failed", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
