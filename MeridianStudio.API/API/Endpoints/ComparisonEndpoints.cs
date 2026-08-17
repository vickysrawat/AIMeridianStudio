using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Services;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.API.Endpoints;

public static class ComparisonEndpoints
{
    public static IEndpointRouteBuilder MapComparisonEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/artifacts/compare", Compare)
              .WithName("CompareArtifacts")
              .WithTags("Artifacts")
              .WithSummary("Compare 2..N artifacts of the same kind into a structured matrix");
        return routes;
    }

    private static async Task<IResult> Compare(
        CompareRequest request, ComparisonService service, ITenantAccessor tenant,
        ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        if (request.ArtifactIds is null || request.ArtifactIds.Count < 2)
            return Results.BadRequest(new { error = "Provide at least 2 artifactIds." });

        try
        {
            var matrix = await service.CompareAsync(request.ArtifactIds, tenant.TenantId, ct);
            log.LogInformation("[Audit] compare {Count} artifacts by {User}@{Tenant}",
                request.ArtifactIds.Count, tenant.UserId ?? "anon", tenant.TenantId);
            return matrix is null
                ? Results.BadRequest(new { error = "Fewer than 2 of the requested artifacts were found in your tenant." })
                : Results.Ok(matrix);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Compare] failed.");
            return Results.Problem(title: "Comparison failed", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
