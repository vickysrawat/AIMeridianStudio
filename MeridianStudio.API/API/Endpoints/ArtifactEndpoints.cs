using System.Text.Json;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.Documents;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.API.Endpoints;

/// <summary>
/// Retrieval + lifecycle for persisted artifacts. All routes are tenant-scoped via
/// <see cref="ITenantAccessor"/>. Reads are wrapped so a store failure returns 503 and never
/// takes down the generation endpoints (plan edge case #11); reads are audit-logged (#10).
/// </summary>
public static class ArtifactEndpoints
{
    public static IEndpointRouteBuilder MapArtifactEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/artifacts").WithTags("Artifacts");

        group.MapGet("/{id}", GetById)
             .WithName("GetArtifact")
             .WithSummary("Fetch one artifact (metadata + payload) by id");

        group.MapGet("/", List)
             .WithName("ListArtifacts")
             .WithSummary("List/filter artifact metadata (cheap — no payloads)");

        group.MapGet("/lineages/{lineageId}/versions", GetVersions)
             .WithName("GetArtifactVersions")
             .WithSummary("All versions of a lineage, newest first");

        group.MapGet("/{id}/export", Export)
             .WithName("ExportArtifact")
             .WithSummary("Download an artifact as markdown | pdf | docx");

        group.MapGet("/{id}/freshness", Freshness)
             .WithName("ArtifactFreshness")
             .WithSummary("Is this document still current with its grounding blueprint? (pull-based staleness)");

        group.MapDelete("/{id}", DeleteById)
             .WithName("DeleteArtifact")
             .WithSummary("Delete a single artifact version");

        group.MapDelete("/lineages/{lineageId}", DeleteLineage)
             .WithName("DeleteArtifactLineage")
             .WithSummary("Delete every version of a lineage");

        return routes;
    }

    private static async Task<IResult> GetById(
        string id, IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var artifact = await store.GetAsync(id, tenant.TenantId, ct);
            log.LogInformation("[Audit] read artifact {Id} by {User}@{Tenant} — {Result}",
                id, tenant.UserId ?? "anon", tenant.TenantId, artifact is null ? "miss" : "hit");
            return artifact is null ? Results.NotFound() : Results.Ok(artifact);
        }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
    }

    private static async Task<IResult> List(
        string? kind, string? domain, string? subDomain,
        DateTimeOffset? from, DateTimeOffset? to, bool? latestOnly, int? skip, int? take,
        IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        ArtifactKind? parsedKind = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<ArtifactKind>(kind, ignoreCase: true, out var k))
                return Results.BadRequest(new { error = $"Unknown kind '{kind}'." });
            parsedKind = k;
        }

        var query = new ArtifactQuery
        {
            Kind = parsedKind,
            Domain = domain,
            SubDomain = subDomain,
            CreatedAfter = from,
            CreatedBefore = to,
            LatestVersionOnly = latestOnly ?? true,
            Skip = Math.Max(0, skip ?? 0),
            Take = Math.Clamp(take ?? 50, 1, 500)
        };

        try
        {
            var result = await store.QueryAsync(query, tenant.TenantId, ct);
            log.LogInformation("[Audit] list artifacts by {User}@{Tenant} — {Count} row(s)",
                tenant.UserId ?? "anon", tenant.TenantId, result.Count);
            return Results.Ok(result);
        }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
    }

    private static async Task<IResult> GetVersions(
        string lineageId, IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var versions = await store.GetVersionsAsync(lineageId, tenant.TenantId, ct);
            return Results.Ok(versions);
        }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
    }

    private static async Task<IResult> DeleteById(
        string id, IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var removed = await store.DeleteAsync(id, tenant.TenantId, ct);
            log.LogInformation("[Audit] delete artifact {Id} by {User}@{Tenant} — {Result}",
                id, tenant.UserId ?? "anon", tenant.TenantId, removed ? "deleted" : "not-found");
            return removed ? Results.NoContent() : Results.NotFound();
        }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
    }

    private static async Task<IResult> DeleteLineage(
        string lineageId, IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        try
        {
            var count = await store.DeleteLineageAsync(lineageId, tenant.TenantId, ct);
            log.LogInformation("[Audit] delete lineage {Lineage} by {User}@{Tenant} — {Count} removed",
                lineageId, tenant.UserId ?? "anon", tenant.TenantId, count);
            return Results.Ok(new { deleted = count });
        }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
    }

    private static async Task<IResult> Export(
        string id, string? format, IArtifactStore store, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        var fmt = (format ?? "markdown").Trim().ToLowerInvariant();
        if (fmt is not ("markdown" or "md" or "pdf" or "docx"))
            return Results.BadRequest(new { error = $"Unsupported format '{format}'. Use markdown | pdf | docx." });

        StoredArtifact? artifact;
        try { artifact = await store.GetAsync(id, tenant.TenantId, ct); }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
        if (artifact is null) return Results.NotFound();

        // Exportable content is the markdown "content" field (documents + white papers).
        var payload = artifact.Payload;
        var markdown = payload.TryGetProperty("content", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String
            ? c.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(markdown))
            return Results.BadRequest(new { error = $"Artifact '{id}' ({artifact.Metadata.Kind}) has no exportable markdown content." });

        markdown = MarkdownSanitizer.StripHardBreakBackslashes(markdown);

        var title = artifact.Metadata.Title
            ?? (payload.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : null)
            ?? "document";
        var slug = Slug(title);

        log.LogInformation("[Audit] export artifact {Id} as {Fmt} by {User}@{Tenant}",
            id, fmt, tenant.UserId ?? "anon", tenant.TenantId);

        try
        {
            return fmt switch
            {
                "pdf"  => Results.File(MarkdownConverter.ToPdf(markdown, title), "application/pdf", $"{slug}.pdf"),
                "docx" => Results.File(MarkdownConverter.ToDocx(markdown, title),
                              "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{slug}.docx"),
                _      => Results.File(System.Text.Encoding.UTF8.GetBytes(markdown), "text/markdown", $"{slug}.md"),
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Export] {Fmt} conversion failed for {Id}.", fmt, id);
            return Results.Problem(title: "Export conversion failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Pull-based staleness: compares the fingerprint the document was grounded on against the current
    /// blueprint's fingerprint. fresh=null means "unknown" (legacy/assessment doc or blueprint not cached) —
    /// never a false stale. No drift: freshness is derived from live content, not a stored flag.
    /// </summary>
    private static async Task<IResult> Freshness(
        string id, IArtifactStore store, PayloadCache cache, ITenantAccessor tenant, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("Artifacts.Access");
        StoredArtifact? artifact;
        try { artifact = await store.GetAsync(id, tenant.TenantId, ct); }
        catch (Exception ex) { return StoreUnavailable(log, ex); }
        if (artifact is null) return Results.NotFound();

        var p = artifact.Payload;
        var blueprintId = p.TryGetProperty("blueprintId", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        var groundedFp  = p.TryGetProperty("groundedBlueprintFingerprint", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;

        if (string.IsNullOrWhiteSpace(blueprintId) || string.IsNullOrWhiteSpace(groundedFp))
            return Results.Ok(new { fresh = (bool?)null, reason = "unknown",
                detail = "This document predates freshness tracking or has no grounding blueprint." });

        if (!cache.TryGet<SystemBlueprint>($"bp-by-id:{blueprintId}", out var current) || current is null)
            return Results.Ok(new { fresh = (bool?)null, reason = "unknown",
                detail = "The grounding blueprint is no longer in cache — freshness can't be determined." });

        var currentFp = BlueprintFingerprint.Compute(current);
        var fresh = string.Equals(currentFp, groundedFp, StringComparison.Ordinal);
        return Results.Ok(new
        {
            fresh,
            reason = fresh ? "current" : "stale",
            groundedFingerprint = groundedFp,
            currentFingerprint = currentFp,
            detail = fresh
                ? "The document reflects the current blueprint."
                : "The blueprint was revised after this document was generated — regenerate to refresh it."
        });
    }

    private static string Slug(string s)
    {
        var chars = s.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "document" : slug[..Math.Min(60, slug.Length)];
    }

    private static IResult StoreUnavailable(ILogger log, Exception ex)
    {
        log.LogError(ex, "[Artifacts] Store read/write failed.");
        return Results.Problem(
            title: "Artifact store unavailable",
            detail: "The artifact store could not be reached. Generation endpoints are unaffected.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
