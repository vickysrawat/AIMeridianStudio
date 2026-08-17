using MeridianStudio.API.Application.Services;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.API.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/analytics").WithTags("Analytics");

        group.MapGet("/pain-points", PainPoints)
             .WithName("AnalyticsPainPoints")
             .WithSummary("Recurring pain points aggregated across stored research runs");

        group.MapGet("/competitors", Competitors)
             .WithName("AnalyticsCompetitors")
             .WithSummary("Recurring competitor patterns across stored research runs");

        return routes;
    }

    private static async Task<IResult> PainPoints(
        string? domain, DateTimeOffset? from, DateTimeOffset? to,
        CrossRunAnalyticsService service, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var result = await service.PainPointsAsync(tenant.TenantId, domain, from, to, ct);
            log.LogInformation("[Audit] analytics pain-points by {User}@{Tenant} — {Runs} run(s)",
                tenant.UserId ?? "anon", tenant.TenantId, result.RunsAnalyzed);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Analytics] pain-points failed.");
            return Results.Problem(title: "Analytics failed", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> Competitors(
        string? domain, DateTimeOffset? from, DateTimeOffset? to,
        CrossRunAnalyticsService service, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var result = await service.CompetitorsAsync(tenant.TenantId, domain, from, to, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Analytics] competitors failed.");
            return Results.Problem(title: "Analytics failed", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
