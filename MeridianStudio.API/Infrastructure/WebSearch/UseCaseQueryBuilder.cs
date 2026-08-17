using MeridianStudio.API.Application.Contracts;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// Deterministic fallback that derives use-case search queries from the brief when the LLM
/// extraction step is unavailable (offline, parse failure). Keeps the search-first pipeline
/// working without an extra model call. Confidence is 0 to mark it as the non-LLM path.
/// </summary>
public static class UseCaseQueryBuilder
{
    public static UseCaseExtraction From(AssessmentRequest req, string domain, string subDomain)
    {
        var topic = Trim(FirstNonEmpty(
            req.UseCase, req.Objective, req.ProblemStatement, req.UseCaseScenario, subDomain, domain)
            ?? "AI solution", 160);

        var prefix = string.Join(" ", new[] { domain, subDomain }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        string Q(string suffix) =>
            (string.IsNullOrWhiteSpace(prefix) ? $"{topic} {suffix}" : $"{prefix}: {topic} {suffix}").Trim();

        var hasScope = !string.IsNullOrWhiteSpace(req.ScopeOfWork);

        return new UseCaseExtraction
        {
            Domain         = domain,
            SubDomain      = subDomain,
            Confidence     = 0,
            CoreQuery      = Q(""),
            ChallengeQuery = Q("challenges risks pitfalls"),
            CaseStudyQuery = Q("case study real-world implementation"),
            OptionQueries  = hasScope ? [Q("approaches options comparison")] : []
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Trim(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
