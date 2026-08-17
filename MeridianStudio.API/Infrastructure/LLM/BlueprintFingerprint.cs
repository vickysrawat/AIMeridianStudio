using System.Security.Cryptography;
using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Short content hash over the blueprint fields that feed downstream grounding. Shared by the document
/// cache key, the blueprint-revision version dedup, and the document freshness check so all three agree
/// on "the current design" — a document is stale when its stamped fingerprint no longer matches the
/// current blueprint's. Deterministic; no LLM.
/// </summary>
public static class BlueprintFingerprint
{
    public static string Compute(SystemBlueprint b)
    {
        var basis = string.Join("␟",
            b.CoreScenario, b.BaseTopology, b.DatabaseSchemes, b.EndpointManifest,
            b.ResilienceStrategies, b.SolutionType, b.ProjectNotes,
            string.Join(";", b.TechRadar.Select(t => $"{t.Layer}={string.Join(",", t.Technologies ?? [])}")),
            string.Join(";", b.ArchDecisions.Select(a => $"{a.Decision}:{a.ChosenApproach}")),
            string.Join(";", b.QualityAttributes.Select(q => $"{q.Attribute}:{q.Target}")),
            string.Join(";", b.BuyVsBuild.Select(x => $"{x.Component}:{x.Recommendation}")));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash)[..16];
    }
}
