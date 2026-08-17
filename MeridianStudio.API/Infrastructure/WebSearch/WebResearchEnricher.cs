using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Infrastructure.Cache;

namespace MeridianStudio.API.Infrastructure.WebSearch;

/// <summary>
/// Orchestrates live web search enrichment for the Research and Use Case tabs.
/// Routes each search group to the most appropriate provider based on domain,
/// subdomain, and user-defined dimension weights.
/// </summary>
public sealed class WebResearchEnricher(
    TavilySearchProvider tavily,
    SerperSearchProvider serper,
    PubMedSearchProvider pubMed,
    GitHubTrendingProvider gitHub,
    MeridianStudio.API.Infrastructure.LLM.GeminiGroundingProvider geminiGrounding,
    PayloadCache cache,
    ILogger<WebResearchEnricher> logger)
{
    /// <summary>True when grounding is possible — Gemini Google-Search grounding or Tavily.
    /// Lets callers skip the search pre-step cleanly when fully offline.</summary>
    public bool IsLiveSearchAvailable => geminiGrounding.IsAvailable || tavily.IsConfigured;

    // ── Document grounding (Gemini-primary → Tavily deep-fetch fallback) ───────

    /// <summary>
    /// Grounds a document's vendor/market claims. Primary: Gemini native Google-Search grounding
    /// (reads real pages); fallback: Tavily deep-fetch (general topic, rich excerpts). Shared cache
    /// keyed on the grounding subset {domain, subDomain, vendors, prompt-version} — NOT the document
    /// payload — so one fetch is reused across documents/fixes/users for the TTL window.
    /// </summary>
    /// <summary>Cache envelope so the grounded sources AND the synthesised facts-brief round-trip
    /// together (a ValueTuple would not serialise cleanly).</summary>
    private sealed record DocGroundingResult(LiveResearchContext Context, string FactsBrief);

    public async Task<(LiveResearchContext Context, string FactsBrief)> EnrichDocumentAsync(
        string domain, string subDomain, string solutionType, string templateType, string title,
        IReadOnlyList<string> vendors, CancellationToken ct = default)
    {
        var sorted   = vendors.Where(v => !string.IsNullOrWhiteSpace(v))
                              .Select(v => v.Trim())
                              .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToArray();
        // v2: cached type changed (now carries the facts-brief) — bump so old entries don't mis-deserialise.
        var cacheKey = cache.ComputeKey(new { grounding = "doc-v2", domain, subDomain, vendors = sorted });
        if (cache.TryGet<DocGroundingResult>(cacheKey, out var hit))
        {
            logger.LogDebug("[WebSearch/Doc] Cache hit for '{D}/{S}'", domain, subDomain);
            return (hit.Context, hit.FactsBrief);
        }

        var ctx   = LiveResearchContext.Empty;
        var brief = string.Empty;
        try
        {
            if (geminiGrounding.IsAvailable)
            {
                try
                {
                    var (sources, factsBrief) = await geminiGrounding.GroundAsync(domain, subDomain, sorted, ct);
                    ctx   = sources;
                    brief = factsBrief ?? string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Gemini grounding failed (e.g. 503 after retries) — leave ctx empty so the
                    // Tavily deep-fetch block below runs instead of aborting to no grounding.
                    logger.LogWarning(ex,
                        "[WebSearch/Doc] Gemini grounding failed for '{D}/{S}' — falling back to Tavily.", domain, subDomain);
                }
            }

            if (!ctx.HasData && tavily.IsConfigured)
            {
                var queries = BuildDocGroundingQueries(domain, subDomain, sorted);
                var groups  = await Task.WhenAll(queries.Select(q =>
                    tavily.SearchAsync(q, ct, daysBack: 365, topic: "general", deep: true)));
                var deduped = Deduplicate(groups.SelectMany(r => r)).Take(15).ToList();
                ctx = new LiveResearchContext(deduped, ["Tavily"], DateTimeOffset.UtcNow);
                // Tavily path has no synthesised brief — the per-source excerpts carry the substance.
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[WebSearch/Doc] Grounding failed for '{D}/{S}' — continuing without.", domain, subDomain);
            return (LiveResearchContext.Empty, string.Empty);
        }

        if (ctx.HasData)
        {
            // Capability/market facts are slow-moving → 30-day shared cache (tiering by volatility is a refinement).
            cache.Set(cacheKey, new DocGroundingResult(ctx, brief), TimeSpan.FromDays(30));
            logger.LogInformation("[WebSearch/Doc] '{D}/{S}' → {N} grounded source(s) from {P}",
                domain, subDomain, ctx.Results.Count, string.Join(", ", ctx.SourcesQueried));
        }
        return (ctx, brief);
    }

    private static string[] BuildDocGroundingQueries(string domain, string subDomain, string[] vendors)
    {
        var topic = $"{domain} {subDomain}".Trim();
        var list  = new List<string> { $"{topic} market size growth adoption" };
        foreach (var v in vendors.Take(5))
            list.Add($"{v} {subDomain} capabilities limitations pricing");
        if (vendors.Length == 0)
            list.Add($"{topic} leading vendors capabilities comparison");
        return [.. list];
    }

    // ── Research Tab Entry Point ──────────────────────────────────────────────

    public async Task<LiveResearchContext> EnrichAsync(
        string subDomain, string domain, DimensionWeights w, CancellationToken ct = default)
    {
        var cacheKey = cache.ComputeKey(new { subDomain, domain, w });
        if (cache.TryGet<LiveResearchContext>(cacheKey, out var hit))
        {
            logger.LogDebug("[WebSearch] Cache hit for '{S}'", subDomain);
            return hit;
        }

        var groups  = BuildAdaptiveSearchPlan(subDomain, domain, w);
        var results = await Task.WhenAll(groups.Select(g => RunGroupAsync(g, ct)));
        var ctx     = Aggregate(results, groups);

        cache.Set(cacheKey, ctx, TimeSpan.FromMinutes(60));
        logger.LogInformation("[WebSearch] '{S}' → {N} results from {P}",
            subDomain, ctx.Results.Count, string.Join(", ", ctx.SourcesQueried));
        return ctx;
    }

    // ── Use Case Tab Entry Point ──────────────────────────────────────────────

    public async Task<LiveResearchContext> EnrichUseCaseAsync(
        UseCaseExtraction extraction, CancellationToken ct = default)
    {
        // All use-case queries use Tavily (case studies, migration patterns, pricing)
        var allQueries = new[] { extraction.CoreQuery, extraction.ChallengeQuery, extraction.CaseStudyQuery }
            .Concat(extraction.OptionQueries ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .ToArray();

        if (allQueries.Length == 0) return LiveResearchContext.Empty;

        var cacheKey = cache.ComputeKey(new { useCase = allQueries });
        if (cache.TryGet<LiveResearchContext>(cacheKey, out var hit))
        {
            logger.LogDebug("[WebSearch/UseCase] Cache hit for '{D}'", extraction.Domain);
            return hit;
        }

        var taskResults = await Task.WhenAll(allQueries.Select(q => tavily.SearchAsync(q, ct)));
        var all         = taskResults.SelectMany(r => r).ToList();
        var deduped     = Deduplicate(all).Take(15).ToList();

        logger.LogInformation("[WebSearch/UseCase] '{D}' → {N} results (Tavily)",
            extraction.Domain, deduped.Count);

        var ctx = new LiveResearchContext(deduped, ["Tavily"], DateTimeOffset.UtcNow);
        cache.Set(cacheKey, ctx, TimeSpan.FromMinutes(60));
        return ctx;
    }

    // ── Adaptive Search Plan ──────────────────────────────────────────────────

    private SearchGroup[] BuildAdaptiveSearchPlan(
        string subDomain, string domain, DimensionWeights w)
    {
        // Normalize once so the absolute routing thresholds below (>=18, >=15, >=8) stay meaningful
        // regardless of the caller's weight scale — the UI now sends importance-derived weights, and
        // other callers may not sum to 100. Identity for already-normalized input.
        w = w.Normalised();

        var groups = new List<SearchGroup>();

        var yr = DateTime.UtcNow.Year;

        // GROUP 1: Market / Business — recency driven by urgency weight
        var marketDays = w.MarketUrgency >= 18 ? 30 : 90;
        groups.Add(new SearchGroup(new RecencyTavily(tavily, marketDays), "market", [
            $"\"{subDomain}\" AI enterprise market adoption {yr}",
            $"{domain} \"{subDomain}\" AI business value ROI impact"
        ]));

        // GROUP 2: Competitive — Serper when competitive gap is heavily weighted
        IWebSearchProvider compProvider = w.CompetitiveGap >= 15 && serper.IsConfigured
            ? serper : tavily;
        groups.Add(new SearchGroup(compProvider, "competitive", [
            $"\"{subDomain}\" AI vendors startups market leaders {yr}",
            $"{domain} \"{subDomain}\" AI company funding investment {yr}"
        ]));

        // GROUP 3: Regulatory — skip if weight too low (save cost)
        if (w.RegulatoryTailwind >= 8)
            groups.Add(new SearchGroup(tavily, "regulatory", [
                $"\"{subDomain}\" AI regulation compliance mandate {yr}",
                $"{domain} regulatory change AI adoption enforcement {yr}"
            ]));

        // GROUP 4: Implementation — provider selected by domain + weight signals
        var implProvider = SelectImplementationProvider(domain, w);
        groups.Add(new SearchGroup(implProvider, "implementation", [
            $"\"{subDomain}\" AI implementation enterprise case study feasibility",
            $"{subDomain} AI technology maturity production deployment"
        ]));

        // GROUP 5: Pain Points — always Tavily
        groups.Add(new SearchGroup(tavily, "pain-points", [
            $"\"{subDomain}\" problems challenges enterprise {yr}",
            $"{domain} \"{subDomain}\" pain points failure obstacles CIO"
        ]));

        return [.. groups];
    }

    private IWebSearchProvider SelectImplementationProvider(string domain, DimensionWeights w)
    {
        if (w.Feasibility >= 15 && domain is "Healthcare" or "Pharmaceutical")
            return pubMed;

        if (w.AIFitness >= 15
            && domain is "IT Services" or "Telecommunications" or "Manufacturing")
            return gitHub;

        return tavily;
    }

    // ── Domain Detection for Use Case heuristic fallback ─────────────────────

    /// <summary>
    /// Keyword scan against the 22-domain taxonomy — used as offline fallback
    /// when no LLM provider is configured for Use Case extraction.
    /// </summary>
    public static string ResolveDomainFrom22Taxonomy(string lowerText) => lowerText switch
    {
        _ when ContainsAny(lowerText, "clinical", "patient", "ehr", "fhir", "hospital", "medical", "diagnostic", "drug", "pharma") => "Healthcare",
        _ when ContainsAny(lowerText, "clinical trial", "pharmacovigil", "drug repurpose", "pharmaceutical") => "Pharmaceutical",
        _ when ContainsAny(lowerText, "trading", "credit risk", "fraud transaction", "portfolio", "investment", "banking", "fintech") => "Financial Services",
        _ when ContainsAny(lowerText, "insurance", "underwriting", "claims", "actuarial", "policy pricing") => "Insurance",
        _ when ContainsAny(lowerText, "government", "citizen service", "public safety", "policy", "urban planning", "infrastructure monitoring") => "Government & Public Sector",
        _ when ContainsAny(lowerText, "tax", "compliance", "audit risk", "regulatory change", "tax planning") => "Tax",
        _ when ContainsAny(lowerText, "law", "legal", "contract", "litigation", "e-discovery", "intellectual property", "attorney") => "Law",
        _ when ContainsAny(lowerText, "audit", "anomaly detection audit", "continuous control", "financial statement", "internal control") => "Audit",
        _ when ContainsAny(lowerText, "advisory", "strategic planning", "m&a", "consulting", "operational efficiency") => "Advisory",
        _ when ContainsAny(lowerText, "hr", "talent", "employee", "workforce", "churn prediction", "skill gap", "learning path") => "HR & Workforce",
        _ when ContainsAny(lowerText, "retail", "ecommerce", "inventory", "product recommendation", "supply chain", "customer sentiment") => "Retail & E-Commerce",
        _ when ContainsAny(lowerText, "manufacturing", "predictive maintenance", "quality control", "production", "robotics", "energy consumption") => "Manufacturing",
        _ when ContainsAny(lowerText, "education", "student", "learning", "curriculum", "grading", "adaptive assessment") => "Education & EdTech",
        _ when ContainsAny(lowerText, "media", "content recommendation", "audience", "ad placement", "sentiment analysis media") => "Media & Entertainment",
        _ when ContainsAny(lowerText, "energy", "smart grid", "renewable", "outage prediction", "utility") => "Energy & Utilities",
        _ when ContainsAny(lowerText, "supply chain", "logistics", "route", "warehouse", "demand forecast", "last-mile") => "Supply Chain & Logistics",
        _ when ContainsAny(lowerText, "real estate", "property", "tenant", "construction", "building", "smart building") => "Real Estate",
        _ when ContainsAny(lowerText, "construction", "project schedule", "safety hazard", "bim", "cost estimation", "site progress") => "Construction",
        _ when ContainsAny(lowerText, "agriculture", "crop", "precision farming", "irrigation", "livestock", "weather impact") => "Agriculture",
        _ when ContainsAny(lowerText, "telecom", "network traffic", "churn telecom", "service quality", "call center", "infrastructure anomaly") => "Telecommunications",
        _ when ContainsAny(lowerText, "travel", "hotel", "booking", "revenue management", "guest experience", "dynamic pricing travel") => "Travel & Hospitality",
        _ when ContainsAny(lowerText, "it service", "incident", "cybersecurity", "cloud", "service desk", "network performance", "software development") => "IT Services",
        _ => "IT Services"  // default for tech-related use cases
    };

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    // ── Execution helpers ─────────────────────────────────────────────────────

    private async Task<SearchResult[]> RunGroupAsync(SearchGroup group, CancellationToken ct)
    {
        try
        {
            var tasks   = group.Queries.Select(q => group.Provider.SearchAsync(q, ct));
            var results = await Task.WhenAll(tasks);
            return results.SelectMany(r => r).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[WebSearch] Group '{G}' failed", group.Label);
            return [];
        }
    }

    private static LiveResearchContext Aggregate(SearchResult[][] groupResults, SearchGroup[] groups)
    {
        var all     = groupResults.SelectMany(r => r).ToList();
        var deduped = Deduplicate(all).Take(15).ToList();
        var sources = groups.Select(g => g.Provider.Name).Distinct().ToArray();
        return new LiveResearchContext(deduped, sources, DateTimeOffset.UtcNow);
    }

    private static IEnumerable<SearchResult> Deduplicate(IEnumerable<SearchResult> results)
        => results
            .GroupBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue);

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed record SearchGroup(IWebSearchProvider Provider, string Label, string[] Queries);

    /// <summary>Wraps Tavily with a per-call recency override.</summary>
    private sealed class RecencyTavily(TavilySearchProvider inner, int days) : IWebSearchProvider
    {
        public string Name => inner.Name;
        public bool IsConfigured => inner.IsConfigured;
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct)
            => inner.SearchAsync(query, ct, daysBack: days);
    }
}
