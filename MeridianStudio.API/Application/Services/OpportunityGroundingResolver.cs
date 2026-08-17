using System.Text.Json;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.Persistence;
using MeridianStudio.API.Infrastructure.Security;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Re-fetches a persisted Research opportunity by id and renders its rich material (competitor playbooks,
/// pain points, the selected opportunity's rationale / value / feasibility) for grounding a prompt.
/// Shared by blueprint generation and the pre-blueprint readiness critic so both ground on the SAME
/// server-side material (the fidelity fix). Fail-soft — returns null when the research isn't persisted
/// or the id doesn't resolve, so callers fall back to the client-supplied description.
/// </summary>
public sealed class OpportunityGroundingResolver(
    IArtifactStore store,
    ITenantAccessor tenant,
    ILogger<OpportunityGroundingResolver> logger)
{
    /// <summary>Cap the block so it never crowds out the structure instructions in the consuming prompt.</summary>
    private const int MaterialCharCap = 2800;

    public async Task<string?> ResolveMaterialAsync(string? researchArtifactId, string? opportunityId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(researchArtifactId)) return null;
        try
        {
            var artifact = await store.GetAsync(researchArtifactId, tenant.TenantId, ct);
            var research = artifact?.Payload.Deserialize<ResearchResponse>(ArtifactSerialization.Options);
            if (research is null)
            {
                logger.LogInformation("[Grounding] Research artifact {Id} not resolvable — caller falls back.", researchArtifactId);
                return null;
            }

            PrioritizedItem? focus = string.IsNullOrWhiteSpace(opportunityId)
                ? null
                : research.Items.FirstOrDefault(i => i.Id == opportunityId);

            var material = GroundingMaterialBuilder.BuildOpportunityMaterial(
                research.CompetitorInsights, research.PainPoints, focus, research.Items);
            if (string.IsNullOrWhiteSpace(material)) return null;
            return material.Length > MaterialCharCap ? material[..MaterialCharCap] + "\n…(truncated)…" : material;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Grounding] Opportunity material re-fetch failed — caller falls back.");
            return null;
        }
    }
}
