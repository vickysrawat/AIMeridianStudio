namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Static registry of the two stable fields per document template type:
/// Persona (who writes it) and SecondaryAudience (who else reads it).
/// Tone, Goal, and Criteria are NOT stored here — they are LLM-generated
/// per request by MissionSuggestionService.
/// </summary>
public static class PersonaRegistry
{
    public sealed record PersonaEntry(string Persona, string SecondaryAudience);

    private static readonly Dictionary<string, PersonaEntry> _entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["proposal"]                = new("Account Executive / Partner",                                                                      "Sr. Delivery Manager, Client Sponsor"),
        ["executive-summary"]       = new("Chief Technology Officer",                                                                                 "Board, VP Engineering"),
        ["technical-specification"] = new("Enterprise Architect and AI Architect",                                                                    "Lead Engineers, Security Architect, QA Lead"),
        ["technical-spec"]          = new("Enterprise Architect and AI Architect",                                                                    "Lead Engineers, Security Architect, QA Lead"),
        ["market-analysis"]         = new("VP Product / Head of Strategy",                                                                            "CEO, Sales Leadership"),
        ["detailed-design"]         = new("Enterprise Architect, AI Architect and Business Analyst",                                                  "Development team, DevOps, Business stakeholders"),
        ["developer-handbook"]      = new("Solution Architect with 20 years of enterprise delivery experience, working alongside the Product Owner",  "Engineering team, Onboarding developers"),
        ["governance-adr"]          = new("Enterprise Architect",                                                                                     "Compliance, Audit"),
    };

    private static readonly PersonaEntry _default =
        new("Senior Consultant", "Executive Stakeholders");

    public static PersonaEntry Get(string templateType) =>
        _entries.TryGetValue(templateType.Trim().ToLowerInvariant().Replace("_", "-"), out var entry)
            ? entry
            : _default;
}
