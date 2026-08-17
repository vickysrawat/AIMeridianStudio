using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Renders the rich research/opportunity material (competitor strategic playbooks, pain points, and the
/// selected opportunity's rationale / real-life value / integration steps / feasibility) into a compact
/// prompt block. Extracted from the white-paper flow so blueprint generation grounds on the SAME material
/// (the fix for opportunity→blueprint fidelity) — one canonical formatter, no drift. This is the seed of
/// the eventual shared GroundingAssembler.
/// </summary>
public static class GroundingMaterialBuilder
{
    /// <summary>
    /// Build the opportunity material block. <paramref name="focus"/> is the selected opportunity (rendered
    /// in full); when it is null the top opportunities are summarised instead. Returns "" when there is nothing.
    /// </summary>
    public static string BuildOpportunityMaterial(
        IReadOnlyList<CompetitorInsight> competitors,
        IReadOnlyList<PainPoint> painPoints,
        PrioritizedItem? focus,
        IReadOnlyList<PrioritizedItem> opportunities)
    {
        var sb = new StringBuilder();

        if (competitors.Count > 0)
        {
            sb.AppendLine("COMPETITORS (what other companies are working on):");
            foreach (var c in competitors)
                sb.AppendLine($"- {c.CompetitorName}: gap = {c.FeatureGap}; impact = {c.ImpactScore}; strategic playbook = {c.StrategicPlaybook}");
            sb.AppendLine();
        }

        if (painPoints.Count > 0)
        {
            sb.AppendLine("PAIN POINTS:");
            foreach (var p in painPoints)
                sb.AppendLine($"- {p.Title} (severity {p.Severity}, {p.Frequency}, affects {p.AffectedSegment}): {p.Description}");
            sb.AppendLine();
        }

        if (focus is { } f)
        {
            sb.AppendLine("FOCUS OPPORTUNITY:");
            sb.AppendLine($"- {f.Name}: {f.Description}");
            sb.AppendLine($"  Rationale: {f.Rationale}");
            sb.AppendLine($"  Real-life value: {f.RealLifeValue}");
            sb.AppendLine($"  Integration steps: {f.IntegrationSteps}");
            if (!string.IsNullOrWhiteSpace(f.FeasibilityAnalysis))
                sb.AppendLine($"  Feasibility ({f.FeasibilityScore}/10): {f.FeasibilityAnalysis}");
            sb.AppendLine();
        }
        else if (opportunities.Count > 0)
        {
            sb.AppendLine("OPPORTUNITIES:");
            foreach (var o in opportunities.Take(6))
                sb.AppendLine($"- {o.Name} (value {o.Value}, urgency {o.Urgency}, feasibility {o.FeasibilityScore}): {o.Rationale} — {o.RealLifeValue}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
