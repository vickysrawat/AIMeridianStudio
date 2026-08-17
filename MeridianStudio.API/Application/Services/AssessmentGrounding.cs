using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Adapts a use-case <see cref="Assessment"/> into a <see cref="SystemBlueprint"/> purely for grounding:
/// the assessment narrative lands in CoreScenario; application-development fields are marked not-applicable.
/// Shared by document generation and grounded execution so the use-case branch grounds identically.
/// </summary>
public static class AssessmentGrounding
{
    public static SystemBlueprint Synthesise(Assessment a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {a.Title}");
        if (!string.IsNullOrWhiteSpace(a.ExecutiveSummary))
            sb.AppendLine().AppendLine(a.ExecutiveSummary);
        foreach (var s in a.Sections)
            sb.AppendLine().AppendLine($"## {s.Title}").AppendLine(s.Body);
        if (a.Recommendations.Length > 0)
            sb.AppendLine().AppendLine("## Recommendations")
              .AppendLine(string.Join("\n", a.Recommendations.Select(r => $"- {r}")));
        if (a.Risks.Length > 0)
            sb.AppendLine().AppendLine("## Risks")
              .AppendLine(string.Join("\n", a.Risks.Select(r => $"- {r}")));
        if (a.NextSteps.Length > 0)
            sb.AppendLine().AppendLine("## Next Steps")
              .AppendLine(string.Join("\n", a.NextSteps.Select(r => $"- {r}")));

        const string na = "Not applicable — grounded in a use-case assessment, not an application design.";
        return new SystemBlueprint
        {
            Id                   = a.Id,
            SolutionId           = a.Id,
            SolutionName         = a.Title,
            Domain               = string.IsNullOrWhiteSpace(a.Domain) ? "Assessment" : a.Domain,
            CoreScenario         = sb.ToString(),
            BaseTopology         = na,
            DatabaseSchemes      = na,
            EndpointManifest     = na,
            ResilienceStrategies = na,
            Feasibility          = a.Feasibility
        };
    }
}
