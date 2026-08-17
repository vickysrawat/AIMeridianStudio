using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Domain.Artifacts;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Persistence;

namespace MeridianStudio.API.Application.Services.Persistence;

/// <summary>
/// Projects generated results + their originating request into <see cref="ArtifactMetadata"/>.
/// LineageId is a coarse, stable slug (so re-runs version the same logical artifact); RequestHash
/// is fine-grained (so exact double-submits dedup). Tags carry cross-references (e.g. source blueprint).
/// </summary>
public static class ArtifactProjection
{
    public static ArtifactMetadata ForResearch(
        ResearchResponse r, ResearchRequest req, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.Research, requestHash, tenantId, userId)
            with
        {
            Domain = r.Domain,
            SubDomain = req.SubDomain,
            Title = string.IsNullOrWhiteSpace(req.SubDomain) ? req.Keywords : req.SubDomain,
            ModelUsed = r.ModelUsed,
            LineageId = Slug("research", r.Domain, req.SubDomain ?? req.Keywords)
        };

    public static ArtifactMetadata ForBlueprint(
        SystemBlueprint b, GenerateBlueprintRequest req, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.Blueprint, requestHash, tenantId, userId)
            with
        {
            Domain = b.Domain,
            SubDomain = string.IsNullOrWhiteSpace(b.SubDomain) ? req.SubDomain : b.SubDomain,
            Title = b.SolutionName,
            ModelUsed = b.ModelUsed,
            LineageId = Slug("blueprint", req.SolutionId),
            Tags = string.IsNullOrWhiteSpace(b.SolutionType) ? [] : [$"solutionType:{b.SolutionType}"]
        };

    /// <summary>
    /// A revised blueprint (patch / topology regen). Same lineage as the original (Slug on SolutionId) so
    /// it mints version N+1; RequestHash is the content fingerprint so an unchanged re-patch dedups and a
    /// changed one versions. No originating GenerateBlueprintRequest exists on the patch path.
    /// </summary>
    public static ArtifactMetadata ForBlueprintRevision(
        SystemBlueprint b, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.Blueprint, requestHash, tenantId, userId)
            with
        {
            Domain = b.Domain,
            SubDomain = b.SubDomain,
            Title = b.SolutionName,
            ModelUsed = b.ModelUsed,
            LineageId = Slug("blueprint", b.SolutionId),
            Tags = string.IsNullOrWhiteSpace(b.SolutionType) ? [] : [$"solutionType:{b.SolutionType}"]
        };

    public static ArtifactMetadata ForTask(
        TaskSpec t, ExecuteTaskRequest req, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.TaskSpec, requestHash, tenantId, userId)
            with
        {
            Title = t.TaskName,
            ModelUsed = t.ModelUsed,
            LineageId = Slug("task", req.TaskName, req.Language ?? "csharp"),
            ParentArtifactId = string.IsNullOrWhiteSpace(req.BlueprintId) ? req.AssessmentId : req.BlueprintId,
            Tags = BuildTaskTags(req)
        };

    public static ArtifactMetadata ForDocument(
        CorporateDocument d, GenerateDocumentRequest req, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.Document, requestHash, tenantId, userId)
            with
        {
            Domain = req.Domain,
            SubDomain = req.SubDomain,
            Title = d.Title,
            ModelUsed = d.ModelUsed,
            LineageId = Slug("document", req.SourceId, d.TemplateType),
            Tags = BuildDocTags(req, d)
        };

    public static ArtifactMetadata ForAssessment(
        Assessment a, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.Assessment, requestHash, tenantId, userId)
            with
        {
            Domain = a.Domain,
            Title = a.Title,
            ModelUsed = a.ModelUsed,
            LineageId = Slug("assessment", a.Id)
        };

    public static ArtifactMetadata ForPrompt(
        DeveloperPrompt p, GenerateComponentPromptRequest req, string requestHash, string tenantId, string? userId)
        => Base(ArtifactKind.DeveloperPrompt, requestHash, tenantId, userId)
            with
        {
            Title = p.ComponentName,
            ModelUsed = p.ModelUsed,
            LineageId = Slug("prompt", req.ComponentName, p.TargetLLM)
        };

    // ── helpers ────────────────────────────────────────────────────────────────

    private static ArtifactMetadata Base(ArtifactKind kind, string requestHash, string tenantId, string? userId)
        => new()
        {
            ArtifactId = ArtifactSerialization.NewArtifactId(),
            Kind = kind,
            RequestHash = requestHash,
            LineageId = "",             // set by each projector
            TenantId = tenantId,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static string[] BuildTaskTags(ExecuteTaskRequest req)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.BlueprintId)) tags.Add($"blueprint:{req.BlueprintId}");
        if (!string.IsNullOrWhiteSpace(req.AssessmentId)) tags.Add($"assessment:{req.AssessmentId}");
        return [.. tags];
    }

    private static string[] BuildDocTags(GenerateDocumentRequest req, CorporateDocument d)
    {
        var tags = new List<string> { $"template:{d.TemplateType}" };
        if (!string.IsNullOrWhiteSpace(req.BlueprintId)) tags.Add($"blueprint:{req.BlueprintId}");
        if (!string.IsNullOrWhiteSpace(req.AssessmentId)) tags.Add($"assessment:{req.AssessmentId}");
        return [.. tags];
    }

    private static string Slug(params string?[] parts)
        => string.Join(":", parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim().ToLowerInvariant()));
}
