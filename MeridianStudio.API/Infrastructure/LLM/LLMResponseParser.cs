using System.Text.Json;
using System.Text.RegularExpressions;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.LLM;

/// <summary>
/// Parses raw LLM text completions into typed domain models.
/// Handles markdown code fences, embedded JSON, escaped strings, and
/// missing / mistyped fields gracefully — ensuring a usable result even
/// when the LLM response is slightly malformed.
/// Throws <see cref="JsonException"/> for responses that are completely
/// unparseable so the orchestrator can rotate to the next provider.
/// </summary>
public static class LLMResponseParser
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas  = true,
        CommentHandling      = JsonCommentHandling.Skip
    };

    // ── Public parse methods ──────────────────────────────────────────────────

    public static ResearchResponse ParseResearch(string raw, string keywordsFallback)
    {
        using var doc  = ParseDoc(raw);
        var root = doc.RootElement;

        return new ResearchResponse
        {
            Domain             = GetStr(root, "domain", keywordsFallback),
            DomainsList        = GetStrList(root, "domainsList"),
            CompetitorInsights = GetCompetitors(root),
            Items              = GetPrioritisedItems(root),
            PainPoints         = GetPainPoints(root)
        };
    }

    public static UseCaseExtraction ParseUseCaseExtraction(string raw, AssessmentRequest req)
    {
        using var doc = ParseDoc(raw);
        var root = doc.RootElement;

        return new UseCaseExtraction
        {
            Domain         = GetStr(root, "domain", req.Domain ?? string.Empty),
            SubDomain      = GetStr(root, "subDomain"),
            Confidence     = root.TryGetProperty("confidence", out var c)
                             && c.ValueKind == JsonValueKind.Number
                             && c.TryGetDouble(out var d) ? d : 0,
            CoreQuery      = GetStr(root, "coreQuery"),
            ChallengeQuery = GetStr(root, "challengeQuery"),
            CaseStudyQuery = GetStr(root, "caseStudyQuery"),
            OptionQueries  = [.. GetStrList(root, "optionQueries")]
        };
    }

    public static SystemBlueprint ParseBlueprint(string raw, GenerateBlueprintRequest req)
    {
        using var doc  = ParseDoc(raw);
        var root = doc.RootElement;

        return new SystemBlueprint
        {
            Id                   = GetStr(root, "id", FallbackId(req.SolutionId)),
            SolutionId           = GetStr(root, "solutionId",   req.SolutionId),
            SolutionName         = GetStr(root, "solutionName", req.SolutionName),
            Domain               = GetStr(root, "domain",       req.Domain ?? req.SolutionName),
            // The generation LLM's own solution-type classification. BlueprintService canonicalises
            // and prefers this (server-side → trustworthy), falling back to the keyword heuristic.
            SolutionType           = GetStr(root, "solutionType"),
            SolutionTypeConfidence = GetDbl(root, "solutionTypeConfidence"),
            CoreScenario         = GetStr(root, "coreScenario"),
            BaseTopology         = GetStr(root, "baseTopology"),
            DatabaseSchemes      = GetStr(root, "databaseSchemes"),
            EndpointManifest     = GetStr(root, "endpointManifest"),
            ResilienceStrategies = GetStr(root, "resilienceStrategies"),
            ArchDecisions        = GetArchDecisions(root),
            QualityAttributes    = GetQualityAttributes(root),
            TechRadar            = GetTechRadar(root),
            BuyVsBuild           = GetBuyVsBuild(root),
            Feasibility          = GetFeasibility(root)
        };
    }

    public static Assessment ParseAssessment(string raw, AssessmentRequest req)
    {
        using var doc = ParseDoc(raw);
        var root = doc.RootElement;

        return new Assessment
        {
            Id               = GetStr(root, "id", FallbackId(req.Objective ?? req.UseCaseScenario ?? "assessment")),
            Title            = GetStr(root, "title", req.UseCase ?? "Use-Case Assessment"),
            Domain           = GetStr(root, "domain", req.Domain ?? string.Empty),
            UseCase          = req.UseCase ?? string.Empty,
            Context          = req.Context ?? string.Empty,
            ProblemStatement = req.ProblemStatement ?? string.Empty,
            Objective        = req.Objective ?? string.Empty,
            ScopeOfWork      = req.ScopeOfWork ?? string.Empty,
            ExpectedOutcome  = req.ExpectedOutcome ?? string.Empty,
            ExecutiveSummary = GetStr(root, "executiveSummary"),
            Sections         = GetAssessmentSections(root),
            Recommendations  = [.. GetStrList(root, "recommendations")],
            Risks            = [.. GetStrList(root, "risks")],
            NextSteps        = [.. GetStrList(root, "nextSteps")],
            Feasibility      = GetFeasibility(root),
            RecommendedDocuments = GetRecommendedDocuments(root)
        };
    }

    public static TaskSpec ParseTask(string raw, ExecuteTaskRequest req)
    {
        using var doc  = ParseDoc(raw);
        var root = doc.RootElement;

        return new TaskSpec
        {
            Id                    = GetStr(root, "id", FallbackId(req.TaskName)),
            TaskName              = GetStr(root, "taskName",         req.TaskName),
            Status                = GetStr(root, "status",           "Completed"),
            ProgressScore         = GetInt(root, "progressScore",    100),
            SystemicValue         = GetStr(root, "systemicValue",    req.SystemicValue ?? "Core platform capability."),
            EstimatedEffort       = GetStr(root, "estimatedEffort",  req.EstimatedEffort ?? "3–5 sprints"),
            GeneratedCodeTemplate = SanitizeCodeTemplate(GetStr(root, "generatedCodeTemplate")),
            OutputLogs            = GetStrList(root, "outputLogs")
        };
    }

    /// <summary>
    /// Parses the compact patch response — returns only the <c>newSections</c> Markdown string.
    /// Falls back to the raw text when JSON parsing fails so the content is never lost.
    /// </summary>
    public static string ParseDocumentPatch(string raw)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var sections = GetStr(doc.RootElement, "newSections");
            if (!string.IsNullOrWhiteSpace(sections)) return sections;
        }
        catch { }

        // Fallback: if the LLM ignored the schema and returned raw Markdown, use it directly.
        var stripped = raw.Trim();
        return stripped.StartsWith('{') ? string.Empty : stripped;
    }

    /// <summary>Parses a section-fix response into (heading, body). Falls back to raw markdown as body.</summary>
    public static (string Heading, string Body) ParseSectionFix(string raw, string fallbackHeading)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var root    = doc.RootElement;
            var heading = GetStr(root, "heading", fallbackHeading);
            var body    = UnwrapNestedContent(GetStr(root, "body"));
            if (!string.IsNullOrWhiteSpace(body))
                return (string.IsNullOrWhiteSpace(heading) ? fallbackHeading : heading, body);
        }
        catch { }

        var stripped = raw.Trim();
        return (fallbackHeading, stripped.StartsWith('{') ? string.Empty : stripped);
    }

    public static CorporateDocument ParseDocument(string raw, GenerateDocumentRequest req)
    {
        // Attempt A — clean JSON parse (works when LLM properly escapes the content)
        // Attempt B — EscapeControlsInStrings (handles literal \n inside strings)
        // These are handled inside ParseDoc itself.
        // Attempt C — boundary extraction: if JSON parsing still fails due to unescaped "
        //   inside the content field, extract metadata and content separately.
        try
        {
            using var doc  = ParseDoc(raw);
            var rootEl = doc.RootElement;
            return new CorporateDocument
            {
                Id           = GetStr(rootEl, "id",           FallbackId(req.SourceId + req.Title)),
                BlueprintId  = GetStr(rootEl, "blueprintId",  req.SourceId),
                Title        = GetStr(rootEl, "title",         req.Title),
                Content      = UnwrapNestedContent(GetStr(rootEl, "content")),
                TemplateType = GetStr(rootEl, "templateType",  req.TemplateType),
                CreatedAt    = GetStr(rootEl, "createdAt",     DateTimeOffset.UtcNow.ToString("O"))
            };
        }
        catch (JsonException) { }

        // Attempt C — boundary-based content extraction.
        var boundaryContent = ExtractContentByBoundary(raw);
        if (boundaryContent is not null)
        {
            var metaOnly = Regex.Replace(
                raw,
                @"""content""\s*:\s*""[\s\S]*""",
                @"""content"": """"",
                RegexOptions.Singleline);

            try
            {
                using var metaDoc = ParseDoc(metaOnly);
                var root = metaDoc.RootElement;
                return new CorporateDocument
                {
                    Id           = GetStr(root, "id",           FallbackId(req.SourceId + req.Title)),
                    BlueprintId  = GetStr(root, "blueprintId",  req.SourceId),
                    Title        = GetStr(root, "title",        req.Title),
                    Content      = UnwrapNestedContent(boundaryContent),
                    TemplateType = GetStr(root, "templateType", req.TemplateType),
                    CreatedAt    = GetStr(root, "createdAt",    DateTimeOffset.UtcNow.ToString("O"))
                };
            }
            catch { }
        }

        // Final fallback — if boundary extraction grabbed the entire raw LLM JSON (Content
        // starts with '{'), attempt one more extraction pass directly on the raw string.
        var fallbackContent = boundaryContent ?? raw.Trim();
        fallbackContent = UnwrapNestedContent(fallbackContent);

        return new CorporateDocument
        {
            Id           = FallbackId(req.SourceId + req.Title),
            BlueprintId  = req.SourceId,
            Title        = req.Title,
            Content      = fallbackContent,
            TemplateType = req.TemplateType,
            CreatedAt    = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    /// <summary>
    /// If the LLM double-wrapped its response (put a CorporateDocument JSON object inside the
    /// "content" field instead of the actual Markdown), recursively extracts the real content.
    /// Safe to call on any string — returns the input unchanged if it doesn't look like JSON.
    /// </summary>
    private static string UnwrapNestedContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith('{')) return content;

        try
        {
            using var inner = JsonDocument.Parse(EscapeControlsInStrings(trimmed), DocOptions);
            var innerContent = GetStr(inner.RootElement, "content");
            // Accept the unwrapped value only if it's non-empty and not another JSON object
            if (!string.IsNullOrEmpty(innerContent) && !innerContent.TrimStart().StartsWith('{'))
                return innerContent;
        }
        catch { }

        return content;
    }

    /// <summary>
    /// Extracts the value of the "content" JSON field by position rather than by parsing.
    /// Finds the opening quote after <c>"content":</c> and the last <c>"</c> before the
    /// final <c>}</c> in the response.  Works even when the value contains unescaped
    /// double-quote or newline characters that would break standard JSON parsing.
    /// Returns null if the "content" key cannot be located.
    /// </summary>
    private static string? ExtractContentByBoundary(string raw)
    {
        // Find the "content" key
        var keyIdx = raw.IndexOf(@"""content""", StringComparison.OrdinalIgnoreCase);
        if (keyIdx < 0) return null;

        // Skip past the colon and any whitespace to the opening quote of the value
        var colonIdx = raw.IndexOf(':', keyIdx + 9);
        if (colonIdx < 0) return null;

        var openQuoteIdx = raw.IndexOf('"', colonIdx + 1);
        if (openQuoteIdx < 0) return null;

        var valueStart = openQuoteIdx + 1;

        // The closing quote of the content value is the last " that precedes the
        // final } in the response.  Walk backwards from the end to find it.
        var end = raw.Length - 1;
        while (end > valueStart && raw[end] != '}') end--;
        while (end > valueStart && raw[end] != '"') end--;

        if (end <= valueStart) return null;

        // Extract the raw value and unescape standard JSON escape sequences
        return raw[valueStart..end]
            .Replace("\\n",  "\n")
            .Replace("\\r",  "\r")
            .Replace("\\t",  "\t")
            .Replace("\\\"", "\"")
            .Replace("\\'",  "'")
            .Trim();
    }

    public static DeveloperPrompt ParsePrompt(string raw, GenerateComponentPromptRequest req)
    {
        using var doc  = ParseDoc(raw);
        var root = doc.RootElement;

        return new DeveloperPrompt
        {
            Id            = GetStr(root, "id",            FallbackId(req.ComponentName)),
            ComponentName = GetStr(root, "componentName", req.ComponentName),
            PromptText    = GetStr(root, "promptText"),
            TargetLLM     = GetStr(root, "targetLLM",    req.TargetLLM ?? "Claude Sonnet"),
            Directives    = GetStr(root, "directives")
        };
    }

    // ── JSON extraction ───────────────────────────────────────────────────────

    /// <summary>
    /// Strips markdown fences and locates the outermost JSON object, then parses.
    /// Applies progressive fallbacks to handle common LLM formatting issues:
    ///   1. Normal parse of extracted JSON
    ///   2. Escape unescaped control chars inside string values (Gemini/GPT often
    ///      emit literal newlines in long content fields instead of \n)
    ///   3. Try the whole raw string in case ExtractJson clipped badly
    /// Throws <see cref="JsonException"/> only if all three attempts fail.
    /// </summary>
    private static JsonDocument ParseDoc(string raw)
    {
        var json = ExtractJson(raw.Trim());

        // Attempt 1 — clean parse
        try { return JsonDocument.Parse(json, DocOptions); }
        catch (JsonException) { }

        // Attempt 2 — fix unescaped control characters inside JSON string values.
        // LLMs frequently emit literal \n inside "content" for complex long documents.
        try { return JsonDocument.Parse(EscapeControlsInStrings(json), DocOptions); }
        catch (JsonException) { }

        // Attempt 3 — last resort: try the whole raw response
        return JsonDocument.Parse(raw, DocOptions);
    }

    /// <summary>
    /// Scans the JSON character by character, tracking whether we are inside a
    /// quoted string.  Any raw \n, \r, or \t found inside a string is replaced
    /// with its JSON escape sequence.  Already-escaped sequences (\\n etc.) are
    /// left untouched because the preceding backslash is consumed first.
    /// </summary>
    private static string EscapeControlsInStrings(string json)
    {
        var sb      = new System.Text.StringBuilder(json.Length + 64);
        bool inStr  = false;
        bool escape = false;

        foreach (char c in json)
        {
            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inStr = !inStr;
                sb.Append(c);
                continue;
            }

            if (inStr)
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n");  continue;
                    case '\r': sb.Append("\\r");  continue;
                    case '\t': sb.Append("\\t");  continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        // 1) Strip ```json … ``` or ``` … ``` code fences
        var fence = Regex.Match(
            text,
            @"```(?:json)?\s*\r?\n?([\s\S]*?)\r?\n?\s*```",
            RegexOptions.Singleline);

        if (fence.Success)
            return fence.Groups[1].Value.Trim();

        // 2) Locate outermost { … }
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');

        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        // 3) Return as-is and let JsonDocument surface the error
        return text;
    }

    // ── Field helpers ─────────────────────────────────────────────────────────

    private static string GetStr(JsonElement el, string prop, string fallback = "")
    {
        if (!el.TryGetProperty(prop, out var v))
            return fallback;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.GetRawText(),
            _                    => fallback
        };
    }

    private static int GetInt(JsonElement el, string prop, int fallback = 0)
    {
        if (!el.TryGetProperty(prop, out var v))
            return fallback;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
            return i;

        // LLMs sometimes return integers as strings
        if (v.ValueKind == JsonValueKind.String &&
            int.TryParse(v.GetString(), out var parsed))
            return parsed;

        return fallback;
    }

    private static double GetDbl(JsonElement el, string prop, double fallback = 0.0)
    {
        if (!el.TryGetProperty(prop, out var v))
            return fallback;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return d;

        // LLMs sometimes return numbers as strings (e.g. "0.9").
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return fallback;
    }

    private static List<string> GetStrList(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray()
                      .Where(e => e.ValueKind == JsonValueKind.String)
                      .Select(e => e.GetString() ?? string.Empty)
                      .Where(s => s.Length > 0)];
    }

    private static List<ArchDecision> GetArchDecisions(JsonElement root)
    {
        if (!root.TryGetProperty("archDecisions", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new ArchDecision(
            Decision:               GetStr(e, "decision"),
            ChosenApproach:         GetStr(e, "chosenApproach"),
            Rationale:              GetStr(e, "rationale"),
            AlternativesConsidered: [.. GetStrList(e, "alternativesConsidered")],
            Risks:                  [.. GetStrList(e, "risks")]
        )).Where(d => !string.IsNullOrWhiteSpace(d.Decision))];
    }

    private static List<QualityAttribute> GetQualityAttributes(JsonElement root)
    {
        if (!root.TryGetProperty("qualityAttributes", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new QualityAttribute(
            Attribute:   GetStr(e, "attribute"),
            Target:      GetStr(e, "target"),
            Measurement: GetStr(e, "measurement")
        )).Where(q => !string.IsNullOrWhiteSpace(q.Attribute))];
    }

    private static List<BuyVsBuildOption> GetBuyVsBuild(JsonElement root)
    {
        if (!root.TryGetProperty("buyVsBuild", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new BuyVsBuildOption(
            Component:           GetStr(e, "component"),
            BuyOption:           GetStr(e, "buyOption"),
            BuyRationale:        GetStr(e, "buyRationale"),
            BuildApproach:       GetStr(e, "buildApproach"),
            BuildRationale:      GetStr(e, "buildRationale"),
            Recommendation:      GetStr(e, "recommendation", "Hybrid"),
            RecommendationReason:GetStr(e, "recommendationReason")
        )).Where(b => !string.IsNullOrWhiteSpace(b.Component))];
    }

    private static List<AssessmentSection> GetAssessmentSections(JsonElement root)
    {
        if (!root.TryGetProperty("sections", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new AssessmentSection(
            Title: GetStr(e, "title"),
            Body:  GetStr(e, "body")
        )).Where(s => !string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Body))];
    }

    private static List<RecommendedDocument> GetRecommendedDocuments(JsonElement root)
    {
        if (!root.TryGetProperty("recommendedDocuments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new RecommendedDocument(
            ExpectedOutcome: GetStr(e, "expectedOutcome"),
            Title:           GetStr(e, "title"),
            TemplateType:    GetStr(e, "templateType", "executive-summary"),
            Rationale:       GetStr(e, "rationale")
        )).Where(d => !string.IsNullOrWhiteSpace(d.Title))];
    }

    private static FeasibilityAnalysis? GetFeasibility(JsonElement root)
    {
        if (!root.TryGetProperty("feasibility", out var f) ||
            f.ValueKind != JsonValueKind.Object)
            return null;

        List<FeasibilityOption> options = [];
        if (f.TryGetProperty("options", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            options = [.. arr.EnumerateArray().Select(e => new FeasibilityOption(
                Name:           GetStr(e, "name"),
                Verdict:        GetStr(e, "verdict", "Partial"),
                Score:          GetInt(e, "score", 5),
                EffortEstimate: GetStr(e, "effortEstimate"),
                Challenges:     [.. GetStrList(e, "challenges")],
                Roadblocks:     [.. GetStrList(e, "roadblocks")],
                Recommendation: GetStr(e, "recommendation")
            )).Where(o => !string.IsNullOrWhiteSpace(o.Name))];
        }

        return new FeasibilityAnalysis(
            UseCase:               GetStr(f, "useCase"),
            Summary:               GetStr(f, "summary"),
            PrimaryConcernVerdict: GetStr(f, "primaryConcernVerdict"),
            Options:               options
        );
    }

    private static List<TechRadarEntry> GetTechRadar(JsonElement root)
    {
        if (!root.TryGetProperty("techRadar", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new TechRadarEntry(
            Layer:        GetStr(e, "layer"),
            Technologies: [.. GetStrList(e, "technologies")]
        )).Where(t => !string.IsNullOrWhiteSpace(t.Layer))];
    }

    private static List<CompetitorInsight> GetCompetitors(JsonElement root)
    {
        if (!root.TryGetProperty("competitorInsights", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select(e => new CompetitorInsight
        {
            CompetitorName    = GetStr(e, "competitorName",   "Unknown Competitor"),
            FeatureGap        = GetStr(e, "featureGap"),
            ImpactScore       = GetStr(e, "impactScore",      "7.5/10"),
            StrategicPlaybook = GetStr(e, "strategicPlaybook")
        })];
    }

    private static List<PrioritizedItem> GetPrioritisedItems(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);

        return [.. arr.EnumerateArray().Select((e, idx) =>
        {
            var rawId = GetStr(e, "id");
            var id    = string.IsNullOrWhiteSpace(rawId) || !seen.Add(rawId)
                        ? $"llm-{idx:000}"
                        : rawId;

            return new PrioritizedItem
            {
                Id                  = id,
                Name                = GetStr(e, "name",             $"AI Initiative {idx + 1}"),
                Description         = GetStr(e, "description"),
                Urgency             = Clamp(GetInt(e, "urgency",    7)),
                Difficulty          = Clamp(GetInt(e, "difficulty", 5)),
                Value               = Clamp(GetInt(e, "value",      8)),
                Rationale           = GetStr(e, "rationale"),
                RealLifeValue       = GetStr(e, "realLifeValue"),
                IntegrationSteps    = GetStr(e, "integrationSteps"),
                FeasibilityScore    = Math.Clamp(GetInt(e, "feasibilityScore", 0), 0, 10),
                FeasibilityAnalysis = GetStr(e, "feasibilityAnalysis"),
                // 8 dimension scores — populated when DimensionWeights are used
                BusinessValue            = NullableScore(e, "businessValue"),
                MarketUrgency            = NullableScore(e, "marketUrgency"),
                Feasibility              = NullableScore(e, "feasibility"),
                CompetitiveGap           = NullableScore(e, "competitiveGap"),
                ImplementationDifficulty = NullableScore(e, "implementationDifficulty"),
                RegulatoryTailwind       = NullableScore(e, "regulatoryTailwind"),
                StrategicFit             = NullableScore(e, "strategicFit"),
                AIFitness                = NullableScore(e, "aiFitness"),
            };
        })];
    }

    private static int? NullableScore(JsonElement e, string prop)
    {
        var v = GetInt(e, prop, -1);
        return v < 1 ? null : (int?)Math.Clamp(v, 1, 10);
    }

    private static List<PainPoint> GetPainPoints(JsonElement root)
    {
        if (!root.TryGetProperty("painPoints", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return [.. arr.EnumerateArray().Select((e, idx) => new PainPoint
        {
            Id              = GetStr(e, "id",              $"pp-{idx:000}"),
            Title           = GetStr(e, "title",           "Unknown pain point"),
            Description     = GetStr(e, "description"),
            AffectedSegment = GetStr(e, "affectedSegment", "Enterprise teams"),
            Severity        = Math.Clamp(GetInt(e, "severity", 5), 1, 10),
            Frequency       = GetStr(e, "frequency",       "Common"),
            RelatedOpportunityIds = GetStrList(e, "relatedOpportunityIds").ToArray(),
            LiveSource      = e.TryGetProperty("liveSource", out var ls)
                              && ls.ValueKind == JsonValueKind.String ? ls.GetString() : null
        }).Where(p => !string.IsNullOrWhiteSpace(p.Title))];
    }

    public static MissionSuggestions ParseMissionSuggestions(string raw, string persona, string secondaryAudience)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var root = doc.RootElement;

            var tones = root.TryGetProperty("toneOptions", out var tonesEl) && tonesEl.ValueKind == JsonValueKind.Array
                ? tonesEl.EnumerateArray().Select(e => new Domain.Models.ToneOption
                  {
                      Label = GetStr(e, "label", "Option"),
                      FullPhrase = GetStr(e, "fullPhrase", GetStr(e, "label", "Professional tone"))
                  }).ToArray()
                : [];

            var goals = root.TryGetProperty("goalOptions", out var goalsEl) && goalsEl.ValueKind == JsonValueKind.Array
                ? goalsEl.EnumerateArray().Select(e => new Domain.Models.GoalOption
                  {
                      Label = GetStr(e, "label", "Option"),
                      Text  = GetStr(e, "text",  GetStr(e, "label", "Achieve the document objective."))
                  }).ToArray()
                : [];

            var criteriaOpts = root.TryGetProperty("criteriaOptions", out var critEl) && critEl.ValueKind == JsonValueKind.Array
                ? critEl.EnumerateArray().Select(e => new Domain.Models.CriteriaOption
                  {
                      Label    = GetStr(e, "label", "Criteria set"),
                      Criteria = e.TryGetProperty("criteria", out var cArr) && cArr.ValueKind == JsonValueKind.Array
                                 ? [.. cArr.EnumerateArray().Select(c => c.GetString() ?? "").Where(s => s.Length > 0)]
                                 : []
                  }).ToArray()
                : [];

            return new Domain.Models.MissionSuggestions
            {
                Persona           = persona,
                SecondaryAudience = secondaryAudience,
                ToneOptions       = tones,
                GoalOptions       = goals,
                CriteriaOptions   = criteriaOpts
            };
        }
        catch
        {
            return FallbackMissionSuggestions(persona, secondaryAudience);
        }
    }

    public static Domain.Models.MissionSuggestions FallbackFor(string persona, string secondaryAudience) =>
        FallbackMissionSuggestions(persona, secondaryAudience);

    private static Domain.Models.MissionSuggestions FallbackMissionSuggestions(string persona, string secondaryAudience) =>
        new()
        {
            Persona           = persona,
            SecondaryAudience = secondaryAudience,
            ToneOptions =
            [
                new() { Label = "Professional",  FullPhrase = "Professional, clear, and authoritative" },
                new() { Label = "Persuasive",     FullPhrase = "Persuasive, value-focused, and client-centric" },
                new() { Label = "Technical",      FullPhrase = "Technical, precise, and evidence-based" },
                new() { Label = "Strategic",      FullPhrase = "Strategic, concise, and decision-oriented" }
            ],
            GoalOptions =
            [
                new() { Label = "Standard",       Text = "The reader can understand the solution and make an informed decision." },
                new() { Label = "Decision-ready", Text = "The reader has all the information needed to approve and fund this initiative." },
                new() { Label = "Action-focused", Text = "The reader knows exactly what to do next and by when." },
                new() { Label = "Risk-aware",     Text = "The reader understands both the opportunity and the risks before committing." }
            ],
            CriteriaOptions =
            [
                new()
                {
                    Label    = "Standard criteria",
                    Criteria = [
                        "Does it clearly state the problem and solution?",
                        "Does it address likely objections?",
                        "Does it include a concrete next step?",
                        "Would the target reader find it credible?"
                    ]
                }
            ]
        };

    public static Domain.Models.UseCaseReadiness ParseUseCaseReadiness(string raw, AssessmentRequest req)
        => ParseReadiness(raw, () => FallbackUseCaseReadiness(req));

    /// <summary>Blueprint-readiness variant: same JSON shape, opportunity-oriented offline fallback.</summary>
    public static Domain.Models.UseCaseReadiness ParseOpportunityReadiness(string raw, GenerateBlueprintRequest req)
        => ParseReadiness(raw, () => FallbackBlueprintReadiness(req));

    /// <summary>Core readiness parser shared by the use-case and blueprint critics; on any failure returns onFail().</summary>
    public static Domain.Models.UseCaseReadiness ParseReadiness(string raw, Func<Domain.Models.UseCaseReadiness> onFail)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var root = doc.RootElement;

            var fields = root.TryGetProperty("fields", out var fEl) && fEl.ValueKind == JsonValueKind.Array
                ? fEl.EnumerateArray().Select(e => new Domain.Models.FieldReview(
                        GetStr(e, "field"),
                        GetStr(e, "status", "weak"),
                        GetStr(e, "comment")))
                    .Where(f => !string.IsNullOrWhiteSpace(f.Field))
                    .ToArray()
                : [];

            var suggestions = root.TryGetProperty("suggestions", out var sEl) && sEl.ValueKind == JsonValueKind.Array
                ? sEl.EnumerateArray().Select(e => new Domain.Models.ImprovementSuggestion(
                        GetStr(e, "field"),
                        GetStr(e, "suggestion"),
                        e.TryGetProperty("proposedText", out var pt) && pt.ValueKind == JsonValueKind.String
                            ? pt.GetString() : null))
                    .Where(s => !string.IsNullOrWhiteSpace(s.Suggestion))
                    .ToArray()
                : [];

            return new Domain.Models.UseCaseReadiness
            {
                ReadinessScore      = Math.Clamp(GetInt(root, "readinessScore", 0), 0, 100),
                Verdict             = GetStr(root, "verdict", "Review the brief for completeness before running the assessment."),
                Fields              = fields,
                ClarifyingQuestions = [.. GetStrList(root, "clarifyingQuestions")],
                Suggestions         = suggestions
            };
        }
        catch
        {
            return onFail();
        }
    }

    /// <summary>Offline / parse-failure fallback: scores the brief from which fields are present and
    /// substantive, flags empties as missing, and emits generic clarifying questions. Never crashes.</summary>
    public static Domain.Models.UseCaseReadiness FallbackUseCaseReadiness(AssessmentRequest req)
    {
        // Quick mode = single scenario field; brief mode = the six structured fields.
        var quick = string.IsNullOrWhiteSpace(req.UseCase) && string.IsNullOrWhiteSpace(req.ProblemStatement)
                    && string.IsNullOrWhiteSpace(req.Objective) && string.IsNullOrWhiteSpace(req.ScopeOfWork)
                    && string.IsNullOrWhiteSpace(req.ExpectedOutcome) && string.IsNullOrWhiteSpace(req.Context);

        var specs = quick
            ? new[] { ("useCaseScenario", req.UseCaseScenario) }
            : [
                ("useCase",          req.UseCase),
                ("context",          req.Context),
                ("problemStatement", req.ProblemStatement),
                ("objective",        req.Objective),
                ("scopeOfWork",      req.ScopeOfWork),
                ("expectedOutcome",  req.ExpectedOutcome),
              ];

        static string Status(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "missing" : v.Trim().Length < 40 ? "weak" : "strong";

        var fields = specs.Select(s => new Domain.Models.FieldReview(
            s.Item1,
            Status(s.Item2),
            Status(s.Item2) switch
            {
                "missing" => "Not provided — add this so the assessment can address it.",
                "weak"    => "Too brief — add specifics so the assessment isn't generic.",
                _         => "Looks substantive."
            })).ToArray();

        var strong = fields.Count(f => f.Status == "strong");
        var score  = fields.Length == 0 ? 0 : (int)Math.Round(100.0 * strong / fields.Length);

        var suggestions = fields
            .Where(f => f.Status != "strong")
            .Select(f => new Domain.Models.ImprovementSuggestion(
                f.Field,
                $"Add or expand the {f.Field} with concrete specifics.",
                $"[e.g. describe the {f.Field} clearly here]"))
            .ToArray();

        return new Domain.Models.UseCaseReadiness
        {
            ReadinessScore      = score,
            Verdict             = score >= 80
                ? "The brief looks reasonably complete."
                : "The brief is thin — fill the flagged fields for a stronger assessment.",
            Fields              = fields,
            ClarifyingQuestions =
            [
                "What is the single most important outcome this engagement must deliver?",
                "What does success look like, and how will it be measured?",
                "Are you weighing alternative options or platforms? If so, which?"
            ],
            Suggestions         = suggestions
        };
    }

    /// <summary>Offline / parse-failure fallback for the pre-blueprint critic: scores the opportunity inputs
    /// (description / integration steps / priority signal) and emits architecture-oriented clarifying questions.</summary>
    public static Domain.Models.UseCaseReadiness FallbackBlueprintReadiness(GenerateBlueprintRequest req)
    {
        var specs = new[]
        {
            ("solutionDescription", req.SolutionDescription),
            ("integrationSteps",    req.IntegrationSteps),
            ("prioritySignal",      req.PrioritySignal),
        };

        static string Status(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "missing" : v.Trim().Length < 40 ? "weak" : "strong";

        var fields = specs.Select(s => new Domain.Models.FieldReview(
            s.Item1,
            Status(s.Item2),
            Status(s.Item2) switch
            {
                "missing" => "Not provided — the blueprint will be generic without it.",
                "weak"    => "Too brief — add specifics so the architecture specialises.",
                _         => "Looks substantive."
            })).ToArray();

        var strong = fields.Count(f => f.Status == "strong");
        var score  = fields.Length == 0 ? 0 : (int)Math.Round(100.0 * strong / fields.Length);

        var suggestions = fields
            .Where(f => f.Status != "strong")
            .Select(f => new Domain.Models.ImprovementSuggestion(
                f.Field,
                $"Add or expand the {f.Field} with concrete specifics.",
                $"[e.g. describe the {f.Field} clearly here]"))
            .ToArray();

        return new Domain.Models.UseCaseReadiness
        {
            ReadinessScore      = score,
            Verdict             = score >= 80
                ? "The opportunity looks reasonably specified for a blueprint."
                : "Thin inputs — enrich the flagged fields for a stronger, more specialised blueprint.",
            Fields              = fields,
            ClarifyingQuestions =
            [
                "What hard constraints (compliance, data residency, existing platforms) must the architecture respect?",
                "What non-functional targets matter most (scale, latency, availability)?",
                "Which external systems or services must this integrate with?"
            ],
            Suggestions         = suggestions
        };
    }

    public static Domain.Models.DocumentReview ParseDocumentReview(string raw)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var root = doc.RootElement;

            var findings = root.TryGetProperty("findings", out var fEl) && fEl.ValueKind == JsonValueKind.Array
                ? fEl.EnumerateArray().Select(e => new Domain.Models.DocumentFinding(
                        GetStr(e, "axis", "relevance"),
                        GetStr(e, "severity", "low"),
                        GetStr(e, "detail"),
                        e.TryGetProperty("suggestedFix", out var sf) && sf.ValueKind == JsonValueKind.String
                            ? sf.GetString() : null))
                    .Where(f => !string.IsNullOrWhiteSpace(f.Detail))
                    .ToArray()
                : [];

            return new Domain.Models.DocumentReview
            {
                ReviewScore = Math.Clamp(GetInt(root, "reviewScore", 100), 0, 100),
                Verdict     = GetStr(root, "verdict", "No issues found."),
                Findings    = findings
            };
        }
        catch
        {
            return FallbackDocumentReview();
        }
    }

    /// <summary>Advise-only fallback — no findings, full score, so a review failure never blocks or misleads.</summary>
    public static Domain.Models.DocumentReview FallbackDocumentReview() => new()
    {
        ReviewScore = 100,
        Verdict     = "Automated review unavailable — no findings.",
        Findings    = []
    };

    public static DomainSuggestions ParseDomains(string raw)
    {
        try
        {
            using var doc = ParseDoc(raw);
            var root      = doc.RootElement;
            var categories = new List<DomainCategory>();

            if (root.TryGetProperty("domains", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var name = GetStr(el, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    categories.Add(new DomainCategory
                    {
                        Name       = name,
                        SubDomains = GetStrList(el, "subDomains")
                    });
                }
            }

            return new DomainSuggestions { Domains = categories };
        }
        catch
        {
            return new DomainSuggestions { Domains = [] };
        }
    }

    // ── Output safety ─────────────────────────────────────────────────────────

    private static readonly string[] UnsafeCodePatterns =
    [
        "Assembly.Load",
        "Assembly.LoadFrom",
        "Process.Start",
        "ProcessStartInfo",
        "Environment.GetEnvironmentVariable",
    ];

    private static string SanitizeCodeTemplate(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        foreach (var pattern in UnsafeCodePatterns)
            code = code.Replace(pattern, $"/* {pattern} — safety-filtered */",
                StringComparison.Ordinal);
        return code;
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static int Clamp(int v) => Math.Clamp(v, 1, 10);

    private static string FallbackId(string seed)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
