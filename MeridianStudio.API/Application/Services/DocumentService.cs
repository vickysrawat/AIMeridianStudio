using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.Documents;
using MeridianStudio.API.Infrastructure.ExampleBank;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.LocalEngine;
using MeridianStudio.API.Infrastructure.Tokenization;
using MeridianStudio.API.Infrastructure.WebSearch;

namespace MeridianStudio.API.Application.Services;

public sealed class DocumentService(
    PayloadCache cache,
    LLMOrchestrator orchestrator,
    LocalCompilationEngine engine,
    DocumentGoalJudgeService judgeService,
    DocumentBankService documentBank,
    ITokenCounter tokens,
    IDomainClassifier domainClassifier,
    WebResearchEnricher enricher,
    DocumentValidationService validation,
    IConfiguration config,
    ILogger<DocumentService> logger) : IDocumentService
{
    private const int MaxIterations = 5;

    /// <summary>
    /// Token budget for the blueprint contract on a given pass, sized to the serving provider.
    /// Patch passes get half — only the failed-criteria-relevant sections are needed.
    /// </summary>
    private int BlueprintBudgetTokens(int providerMaxInputTokens, bool firstPass)
    {
        var outputReserve = config.GetValue("Budget:OutputReserveTokens", 8000);
        var fixedReserve  = config.GetValue("Budget:FixedPromptReserveTokens", 4000);
        var maxBlueprint  = config.GetValue("Budget:MaxBlueprintTokens", 12000);

        var available = providerMaxInputTokens - outputReserve - fixedReserve;
        var budget    = Math.Clamp(available, 1500, maxBlueprint);
        return firstPass ? budget : Math.Max(1500, budget / 2);
    }

    /// <summary>Lowercased, punctuation-stripped, whitespace-collapsed form of a criterion,
    /// used to de-duplicate criteria by meaning rather than a fragile prefix match.</summary>
    private static string NormaliseCriterion(string criterion)
    {
        var sb        = new System.Text.StringBuilder(criterion.Length);
        var lastSpace = true;   // avoids a leading space
        foreach (var ch in criterion)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    public async Task<CorporateDocument> GenerateDocumentAsync(
        GenerateDocumentRequest request, CancellationToken ct = default)
    {
        // When mission fields are absent (legacy callers or heuristic path),
        // fall through to the original single-pass generation.
        if (string.IsNullOrWhiteSpace(request.SelectedGoal))
            return await GenerateLegacyAsync(request, ct);

        return await GenerateGoalDirectedAsync(request, ct);
    }

    // ── By-id section fix (structured, deterministic) ───────────────────────────

    /// <summary>
    /// Repairs ONE section to satisfy a single criterion: regenerates that section, replaces it
    /// by id (no duplicates / stable placement), and re-judges ONLY the affected criteria (the
    /// fixed one + any sharing the changed section), freezing the rest. Stateless — the client
    /// supplies the structured document.
    /// </summary>
    public async Task<CorporateDocument> FixSectionAsync(
        StructuredDocument doc, string criterionId, CancellationToken ct = default)
    {
        var criterion = doc.Criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new ArgumentException($"Criterion '{criterionId}' not found.", nameof(criterionId));

        var persona   = PersonaRegistry.Get(doc.TemplateType);
        var blueprint = ResolveGroundingBlueprintForDoc(doc);
        var target    = doc.Sections.FirstOrDefault(s => criterion.TargetSectionIds.Contains(s.Id))
                        ?? BestSectionFor(criterion.Text, doc.Sections);
        var outline   = string.Join("\n", doc.Sections.Select(s => $"- [{s.Id}] {s.Heading}"));

        var (fix, modelUsed) = await orchestrator.ExecuteAsync(
            "fix-criterion",
            async (provider, pCt) =>
            {
                var budget = BlueprintBudgetTokens(provider.MaxInputTokens, firstPass: false);
                var (sys, usr) = PromptBuilder.BuildSectionFix(
                    doc, criterion, target, outline, persona.Persona, persona.SecondaryAudience,
                    blueprint, tokens, budget);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseSectionFix(raw, target?.Heading ?? criterion.Text);
            },
            () => (Heading: target?.Heading ?? criterion.Text, Body: target?.Body ?? string.Empty),
            ct);

        // Upsert by id — replace the target's body in place, or append a uniquely-headed section.
        var sections = doc.Sections.ToList();
        string targetId;
        if (target is not null)
        {
            var idx = sections.FindIndex(s => s.Id == target.Id);
            sections[idx] = target with
            {
                Heading = string.IsNullOrWhiteSpace(fix.Heading) ? target.Heading : fix.Heading,
                Body    = fix.Body
            };
            targetId = target.Id;
        }
        else
        {
            targetId = $"s{sections.Count + 1}";
            sections.Add(new DocumentSection
            {
                Id = targetId, Heading = fix.Heading, Level = 2, Body = fix.Body, CriterionIds = [criterion.Id]
            });
        }

        var changed    = sections.First(s => s.Id == targetId);
        var judgeInput  = $"DOCUMENT OUTLINE:\n{outline}\n\nSECTION UNDER REVIEW:\n## {changed.Heading}\n{changed.Body}";

        // Re-judge ONLY the criterion being fixed. Re-judging every criterion that merely shares the
        // changed section (BestSectionFor maps many criteria onto one section) — and judging them
        // against just that one section — is what flipped unrelated criteria to "failed" when the user
        // fixed a single criterion. Untouched criteria keep their prior pass/fail verdict verbatim.
        var affected = new HashSet<string> { criterionId };

        var newCriteria = new List<CriterionState>(doc.Criteria.Count);
        foreach (var c in doc.Criteria)
        {
            var cc = c.Id == criterionId && !c.TargetSectionIds.Contains(targetId)
                ? c with { TargetSectionIds = [targetId] }
                : c;

            if (affected.Contains(cc.Id))
            {
                var eval   = await judgeService.EvaluateAsync(judgeInput, modelUsed, doc.TemplateType, doc.Goal, [cc.Text], ct);
                var passed = eval.PassedCriteria.Contains(cc.Text, StringComparer.OrdinalIgnoreCase);
                cc = cc with
                {
                    Passed        = passed,
                    FailureReason = passed ? null : (eval.FailureReasons.TryGetValue(cc.Text, out var r) ? r : cc.FailureReason)
                };
            }
            newCriteria.Add(cc);
        }

        var updated = doc with { Sections = sections, Criteria = newCriteria };
        var content = DocumentRenderer.Render(updated);
        var achieved = newCriteria.All(c => c.Passed);

        return new CorporateDocument
        {
            Id                 = doc.DocumentId,
            BlueprintId        = doc.BlueprintId ?? doc.AssessmentId ?? string.Empty,
            Title              = doc.Title,
            Content            = content,
            TemplateType       = doc.TemplateType,
            CreatedAt          = DateTimeOffset.UtcNow.ToString("O"),
            ModelUsed          = modelUsed,
            DocumentId         = doc.DocumentId,
            Structured         = updated,
            EffectiveGoal      = doc.Goal,
            EffectiveCriteria  = [.. doc.Criteria.Select(c => c.Text)],
            PassedCriteria     = [.. newCriteria.Where(c => c.Passed).Select(c => c.Text)],
            FailedCriteria     = [.. newCriteria.Where(c => !c.Passed).Select(c => c.Text)],
            FailureReasons     = newCriteria.Where(c => !c.Passed && c.FailureReason is not null)
                                            .ToDictionary(c => c.Text, c => c.FailureReason!),
            GoalAchieved       = achieved,
            GoalAchievementPct = newCriteria.Count == 0 ? 0 : (int)Math.Round(100.0 * newCriteria.Count(c => c.Passed) / newCriteria.Count),
            IterationsUsed     = 1,
            FactChecked        = achieved && !modelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal)
        };
    }

    // ── Goal-directed generation ──────────────────────────────────────────────

    private async Task<CorporateDocument> GenerateGoalDirectedAsync(
        GenerateDocumentRequest request, CancellationToken ct)
    {
        var persona = PersonaRegistry.Get(request.TemplateType);

        // Each entry is a pass/fail gate in the goal-directed refinement loop.
        // If a criterion fails, the judge tells the LLM exactly what's missing so the
        // next iteration can add it — turning vague guidance into self-correcting requirements.
        var templateCriteria = request.TemplateType.ToLowerInvariant().Replace("_", "-") switch
        {
            "executive-summary" =>
            [
                "Does it quantify the current problem with measurable metrics?",
                "Does it clearly describe the proposed AI solution?",
                "Does it include a risk table with mitigations?",
                "Does it include financial highlights or ROI projections?",
                "Does it end with a concrete call to action or next steps?"
            ],
            "market-analysis" =>
            [
                "Does it include TAM, SAM, and SOM with dollar figures?",
                "Does it include a CAGR or growth projection?",
                "Does it include a competitor matrix with feature gaps and strategic playbooks?",
                "Does it define an ideal customer profile (firmographics, pain points, decision makers)?",
                "Does it identify adoption barriers or demand signals?"
            ],
            "technical-specification" or "technical-spec" =>
            [
                "Does it provide the architectural view?",
                "Is there a Mermaid architecture diagram (a fenced ```mermaid graph code block) showing the major components, services, data stores, and queues with labelled connections?",
                "Does the document contain a Mermaid sequence diagram (a fenced ```mermaid sequenceDiagram code block) tracing the end-to-end request/response flow through the layers?",
                "Does it include a technology stack table (package, version, purpose)?",
                "Does it define data architecture and storage patterns?",
                "Does it include security controls and compliance requirements?",
                "Does it specify non-functional requirements (latency targets, availability SLAs)?",
                "Does it describe the CI/CD pipeline and deployment approach?"
            ],
            "proposal" =>
            [
                "Does it clearly state the problem or opportunity being addressed?",
                "Does it describe the proposed solution with specific deliverables?",
                "Does it include a phased delivery plan with timeline and costs?",
                "Does it include a 3-year ROI or financial projection?",
                "Does it include a risk register with mitigations?",
                "Does it include acceptance criteria for each deliverable?"
            ],
            "governance-adr" =>
            [
                "Does it include a Status field (Proposed / Accepted / Deprecated)?",
                "Does it provide context covering the business driver and constraints?",
                "Does it state the decision and what it means specifically?",
                "Does it list the positive and negative consequences?",
                "Does it include at least 2 alternatives with rejection rationale?",
                "Does it identify the top security concerns with architectural mitigations?",
                "Does it include production failure modes with mitigation strategies?"
            ],
            "developer-handbook" =>
            [
                "Does it include epics with user stories in As-a / I-want / So-that format?",
                "Does it include an architecture overview with a data-flow diagram?",
                "Does it include a component reference table with key responsibilities?",
                "Does it list third-party dependencies with test-double substitution guidance?",
                "Does it document design patterns applied and why each was chosen?",
                "Does it include a prioritised to-do checklist with a week-by-week breakdown?"
            ],
            "detailed-design" =>
            [
                "Does it provide the architectural view?",
                "Is there a Mermaid architecture diagram (a fenced ```mermaid graph code block) showing every service, data store, queue, and external integration with labelled arrows?",
                "Does the document contain a Mermaid sequence diagram (a fenced ```mermaid sequenceDiagram code block) tracing the complete happy-path request flow through every layer?",
                "Does it include a solution directory tree with key files named?",
                "Does it include a technology stack table (package, version, purpose, licence)?",
                "Does it include database schema with CREATE TABLE DDL and indexes?",
                "Does it include core domain model records with required properties?",
                "Does it include key service implementations with method signatures?",
                "Does it include REST API contracts with request/response JSON examples?",
                "Are all event producers and consumers defined with their JSON schemas and communication protocols?",
                "Does it include an error handling strategy table?",
                "Does it include a sprint plan with a Definition of Done for each milestone?"
            ],
            _ => Array.Empty<string>()
        };

        // Merge template criteria into the user's, de-duplicating on a NORMALISED full-text
        // match. (The previous 15-char-prefix test collapsed any two criteria that shared an
        // opening like "Does it include …" — silently dropping, e.g., the security-controls
        // criterion. Normalised exact matching keeps genuinely distinct criteria.)
        var baseCriteria      = request.SelectedCriteria ?? [];
        var mandatoryCriteria = new List<string>(baseCriteria);
        var seenCriteria      = new HashSet<string>(baseCriteria.Select(NormaliseCriterion));
        foreach (var c in templateCriteria)
            if (seenCriteria.Add(NormaliseCriterion(c)))
                mandatoryCriteria.Add(c);
        var selectedCriteria = mandatoryCriteria.ToArray();

        var selectedGoal     = request.SelectedGoal!;
        var solutionType     = request.SolutionType ?? string.Empty;

        // When the caller didn't supply a domain, classify one (embedding similarity → keyword
        // fallback) so few-shot retrieval and grounding are still domain-scoped. Additive only —
        // a supplied domain is always respected.
        var effectiveDomain = request.Domain ?? string.Empty;
        if (string.IsNullOrWhiteSpace(effectiveDomain))
        {
            var classification = await domainClassifier.ClassifyAsync(
                $"{request.Title} {request.SubDomain} {request.BlueprintContext}", ct);
            effectiveDomain = classification.Domain;
            logger.LogInformation("[Document] Domain absent — classified as '{D}' ({C:P0} via {M}).",
                classification.Domain, classification.Confidence, classification.Method);
        }

        // Retrieve the grounding blueprint from cache — or synthesise one from the source
        // assessment (use-case workflow) — so document prompts embed structured data verbatim
        // rather than re-deriving it from the truncated fallback context.
        var blueprint = ResolveGroundingBlueprint(request);
        if (blueprint is null)
            logger.LogDebug("[Document] No cached blueprint/assessment for {Id} — falling back to BlueprintContext.", request.SourceId);
        // Fingerprint the grounding blueprint once — reused for the cache key AND stamped on the document
        // so freshness can later detect a blueprint revision. Empty when grounding is absent (→ unknown freshness).
        var groundedFp = blueprint is null ? string.Empty : Infrastructure.LLM.BlueprintFingerprint.Compute(blueprint);

        // Load few-shot examples from the document bank — hard-filtered by domain, then
        // semantically ranked by sub-domain + goal + criteria so examples are sub-domain specific.
        var examplesContext = await documentBank.GetExamplesContextAsync(
            request.TemplateType,
            effectiveDomain,
            request.SubDomain ?? string.Empty,
            selectedGoal,
            selectedCriteria,
            ct);

        // When the caller supplies an existing document, skip the full first-pass
        // generation and jump straight to the patch loop — targeting only the gaps
        // the caller already knows about.
        bool   isPatch         = !string.IsNullOrWhiteSpace(request.ExistingContent);
        string previousContent = request.ExistingContent ?? string.Empty;
        // gaps = only the criteria that need fixing (used by BuildDocumentPatch prompt).
        // When the caller provides KnownFailureReasons, use its keys — they are the
        // exact failing criteria. Fall back to SelectedCriteria only if no reasons supplied.
        string[] gaps = isPatch
            ? (request.KnownFailureReasons is { Count: > 0 }
                ? [.. request.KnownFailureReasons.Keys]
                : request.SelectedCriteria ?? [])
            : [];
        var gapReasons         = isPatch
                                     ? (request.KnownFailureReasons ?? new Dictionary<string, string>())
                                     : new Dictionary<string, string>();
        int startIteration     = isPatch ? 2 : 1;

        if (isPatch)
            logger.LogInformation(
                "[Document] Patch mode — skipping full generation. Targeting {N} gap(s): {G}",
                gaps.Length, string.Join(", ", gaps));

        // Content-addressed cache for completed goal-directed documents — an identical
        // re-request returns instantly instead of re-running the multi-pass loop (~up to
        // 10 LLM calls). Patch requests (ExistingContent) are explicit enhancements and are
        // neither served from nor written to the cache.
        // Fingerprint the RESOLVED blueprint's grounding-relevant content so a refinement made during
        // blueprint review (e.g. a changed tech radar that leaves CoreScenario untouched) busts this
        // cache. Keying on BlueprintId + BlueprintContext alone returned the stale document verbatim
        // because neither changes when only the tech stack / schema / decisions are patched.
        string? docCacheKey = isPatch ? null : cache.ComputeKey(new
        {
            request.BlueprintId, request.AssessmentId, request.Title, request.TemplateType,
            request.Domain, request.SubDomain, request.SolutionType, request.SelectedTone,
            Goal     = selectedGoal,
            Criteria = string.Join("|", selectedCriteria),
            request.BlueprintContext,
            BlueprintFingerprint = string.IsNullOrEmpty(groundedFp) ? null : groundedFp
        });
        if (docCacheKey is not null)
        {
            if (request.IsRerun)
            {
                cache.Evict(docCacheKey);
                logger.LogInformation("[Cache] Goal-directed document evicted for rerun — key: {K}", docCacheKey[..8]);
            }
            else if (cache.TryGet<CorporateDocument>(docCacheKey, out var cachedDoc))
            {
                logger.LogInformation("[Cache] Goal-directed document hit — key: {K}", docCacheKey[..8]);
                return cachedDoc;
            }
        }

        // ── Live grounding pre-step (fact-heavy templates, opt-in) ────────────────
        // Seeds request.ResearchSources so the model cites real, current vendor/market facts as
        // [S#]; the VendorCapabilityRule + NoAssumptionRule force [REQUIRED:] for anything unsourced.
        // Fact-heavy templates ground even when the client omits GroundInLiveResearch, when
        // Grounding:ForceForFactHeavy is enabled (default true) — A2. The honesty rules still
        // force [REQUIRED:] for anything unsourced, so this only adds citable facts.
        var forceGrounding = config.GetValue("Grounding:ForceForFactHeavy", true);
        if ((request.GroundInLiveResearch || forceGrounding) && !isPatch
            && ShouldGroundWithLiveSearch(request.TemplateType) && enricher.IsLiveSearchAvailable)
        {
            try
            {
                var vendors = ExtractCandidateVendors(request, effectiveDomain);
                var (live, factsBrief) = await enricher.EnrichDocumentAsync(
                    effectiveDomain, request.SubDomain ?? string.Empty, solutionType,
                    request.TemplateType, request.Title, vendors, ct);
                if (live.HasData)
                {
                    var grounded = live.Results.Select(r => new ResearchSourceDto(
                            r.Title, r.Url,
                            r.PublishedAt is { } d ? $"{r.Source}, {d:yyyy-MM}" : r.Source,
                            r.Excerpt))
                        .ToArray();
                    // The Gemini grounding path also yields a synthesised, cross-vendor facts-brief
                    // (cited to the same [S#] sources). Inject it as grounded context so the model
                    // has the actual statements to cite, not just source titles/URLs.
                    request = request with
                    {
                        ResearchSources = [.. (request.ResearchSources ?? []), .. grounded],
                        GroundedFacts   = string.IsNullOrWhiteSpace(factsBrief) ? request.GroundedFacts : factsBrief
                    };
                    logger.LogInformation("[Document] Live grounding: {N} source(s) injected{B}.",
                        grounded.Length, string.IsNullOrWhiteSpace(factsBrief) ? "" : " + facts-brief");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Document] Live grounding failed — continuing without sources.");
            }
        }

        CorporateDocument document = null!;
        DocumentGoalJudgeService.GoalEvaluation eval = null!;
        int iterations;

        // Build the competitor grounding strings once — reused on every pass.
        var (competitorSec, competitorConstraint) = BuildCompetitorSections(request);

        for (iterations = startIteration; iterations <= MaxIterations; iterations++)
        {
            bool isFirstPass = iterations == 1;

            logger.LogInformation(
                "[Document] {Mode} pass {I}/{Max} — template: {T}",
                isFirstPass ? "Full" : "Patch",
                iterations, MaxIterations, request.TemplateType);

            var (result, modelUsed) = await orchestrator.ExecuteAsync(
                "generate-document",
                async (provider, pCt) =>
                {
                    if (isFirstPass)
                    {
                        var (sys, usr) = PromptBuilder.BuildDocument(
                            request,
                            persona.Persona,
                            persona.SecondaryAudience,
                            solutionType,
                            examplesContext,
                            competitorSec,
                            competitorConstraint,
                            blueprint,
                            tokens,
                            BlueprintBudgetTokens(provider.MaxInputTokens, firstPass: true),
                            $"{selectedGoal} {string.Join(" ", selectedCriteria)}");

                        var raw = await provider.CompleteAsync(sys, usr, pCt);
                        return LLMResponseParser.ParseDocument(raw, request);
                    }
                    else
                    {
                        // Patch pass — LLM returns only new sections, server merges.
                        var (sys, usr) = PromptBuilder.BuildDocumentPatch(
                            request,
                            persona.Persona,
                            persona.SecondaryAudience,
                            solutionType,
                            previousContent,
                            gaps,
                            gapReasons,
                            competitorSec,
                            competitorConstraint,
                            blueprint,
                            tokens,
                            BlueprintBudgetTokens(provider.MaxInputTokens, firstPass: false),
                            string.Join(" ", gaps));

                        var raw = await provider.CompleteAsync(sys, usr, pCt);
                        var newSections = LLMResponseParser.ParseDocumentPatch(raw);

                        // Merge by heading: a patch section whose heading already exists REPLACES
                        // it in place (preserving position, preventing duplicates); genuinely new
                        // sections are appended. Re-fixing the same criterion no longer duplicates.
                        var mergedContent = DocumentSectionMerger.Merge(previousContent, newSections);

                        return new CorporateDocument
                        {
                            Id           = $"{request.SourceId[..Math.Min(6, request.SourceId.Length)]}{request.Title[..Math.Min(6, request.Title.Length)]}".ToLowerInvariant(),
                            BlueprintId  = request.BlueprintId ?? request.AssessmentId ?? string.Empty,
                            Title        = request.Title,
                            Content      = mergedContent,
                            TemplateType = request.TemplateType,
                            CreatedAt    = DateTimeOffset.UtcNow.ToString("O")
                        };
                    }
                },
                () => engine.CompileDocument(
                    request.SourceId,
                    request.Title,
                    request.TemplateType,
                    request.Domain),
                ct);

            document = result with { ModelUsed = modelUsed };
            previousContent = document.Content;

            // Always judge the FULL merged document — including on patch passes.
            // Previously, patch passes judged only a structure summary + the newest sections
            // (BuildPatchJudgeInput). That reduced every earlier-fixed section to a ~150-char
            // stub, so criteria a prior fix had satisfied would be marked failed again on the
            // next fix — the scorecard "flapped" and the failed count never went down across
            // sequential single-criterion fixes. Judging the full content keeps every prior
            // fix visible to the judge, so passed criteria stay passed.
            var judgeInput = document.Content;

            // Evaluate against user's criteria
            eval = await judgeService.EvaluateAsync(
                judgeInput,
                modelUsed,
                request.TemplateType,
                selectedGoal,
                selectedCriteria,
                ct);

            logger.LogInformation("[Document] Pass {I} score: {Pct}%, achieved: {A}",
                iterations, eval.GoalAchievementPct, eval.GoalAchieved);

            if (eval.GoalAchieved) break;

            // Feed the judge's specific gap reasons to the next patch call
            gaps       = eval.FailedCriteria;
            gapReasons = eval.FailureReasons;
        }

        // Record to document bank only on goal achievement
        if (eval.GoalAchieved && !document.ModelUsed.Contains(LLMOrchestrator.HeuristicModelName))
        {
            _ = documentBank.RecordAsync(
                request.TemplateType,
                effectiveDomain,
                request.SubDomain ?? string.Empty,
                solutionType,
                document.Title,
                document.Content,
                selectedGoal,
                selectedCriteria,
                eval.GoalAchievementPct,
                iterations,
                request.WasRefined,
                ct);
        }

        // ── Structured-native fields ────────────────────────────────────────────
        // Parse the generated Markdown into sections (stable ids), freeze the criteria stack
        // with per-criterion status + a best-effort section mapping, and assign a stable id.
        // The structure becomes the source of truth a by-id Fix targets.
        var documentId = DocumentId(request, selectedGoal);
        var sections   = DocumentIndex.Parse(document.Content);
        var criteria   = BuildCriteriaStack(selectedCriteria, eval, sections);
        var structured = new StructuredDocument
        {
            DocumentId       = documentId,
            Title            = request.Title,
            TemplateType     = request.TemplateType,
            Domain           = effectiveDomain,
            SubDomain        = request.SubDomain ?? string.Empty,
            BlueprintId      = request.BlueprintId,
            AssessmentId     = request.AssessmentId,
            Goal             = selectedGoal,
            BlueprintContext = request.BlueprintContext,
            Sections         = sections,
            Criteria         = criteria,
            Sources          = [.. (request.ResearchSources ?? []).Select((s, i) => new SourceRef
            {
                Id        = $"S{i + 1}",
                Title     = s.Title,
                Url       = s.Url,
                Origin    = "research",
                FetchedAt = DateTimeOffset.UtcNow.ToString("O"),
                Excerpt   = s.Excerpt
            })]
        };

        // Append a "## Sources" block to the rendered content so the [S#] markers resolve to
        // clickable links (the human-verification handle), unless the model already listed them.
        var contentWithSources = structured.Sources.Count > 0
                                 && !document.Content.Contains("## Sources", StringComparison.OrdinalIgnoreCase)
            ? document.Content.TrimEnd() + "\n\n" + DocumentRenderer.RenderSources(structured.Sources)
            : document.Content;

        // Browserless Mermaid repair pass over the rendered content (no-op if disabled / no diagrams).
        contentWithSources = await validation.RepairContentAsync(contentWithSources, "generate-document", ct);
        // Strip trailing hard-break backslashes at the source so every surface (render/copy/txt/pdf/docx) is clean.
        contentWithSources = Infrastructure.Documents.MarkdownSanitizer.StripHardBreakBackslashes(contentWithSources);

        var finalDocument = document with
        {
            Content            = contentWithSources,
            GoalAchievementPct = eval.GoalAchievementPct,
            GoalAchieved       = eval.GoalAchieved,
            IterationsUsed     = Math.Min(iterations, MaxIterations),
            PassedCriteria     = eval.PassedCriteria,
            FailedCriteria     = eval.FailedCriteria,
            FailureReasons     = eval.FailureReasons,
            CriterionScores    = eval.CriterionScores,
            EffectiveGoal      = selectedGoal,
            EffectiveCriteria  = selectedCriteria,
            WasRefined         = request.WasRefined,
            GroundedBlueprintFingerprint = groundedFp,
            // Fact-checked only when a live model passed the goal/faithfulness judge —
            // heuristic output is auto-passed without evaluation, so it is never marked checked.
            FactChecked        = eval.GoalAchieved
                                 && !document.ModelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal),
            DocumentId         = documentId,
            Structured         = structured
        };

        // Cache only fully-achieved, live-model documents — never partial results or
        // heuristic-engine output (mirrors the legacy path and the document bank policy).
        if (docCacheKey is not null && eval.GoalAchieved
            && !finalDocument.ModelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal))
        {
            var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Document:TtlHours", 24.0));
            cache.Set(docCacheKey, finalDocument, ttl);
        }

        return finalDocument;
    }

    // ── Structured-native helpers (stable id, frozen stack, section mapping) ────

    /// <summary>Stable document id from the grounding source + title + template (for by-id fixes).</summary>
    private static string DocumentId(GenerateDocumentRequest request, string selectedGoal)
    {
        var basis = $"{request.SourceId}|{request.Title}|{request.TemplateType}";
        var hash  = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis));
        return "doc-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>Freezes the criteria stack: per-criterion id, pass/fail from the judge, failure
    /// reason, and a best-effort target section (lexical relevance) so a Fix knows what to edit.</summary>
    private static List<CriterionState> BuildCriteriaStack(
        string[] selectedCriteria, DocumentGoalJudgeService.GoalEvaluation eval, List<DocumentSection> sections)
    {
        var passed = new HashSet<string>(eval.PassedCriteria, StringComparer.OrdinalIgnoreCase);
        var list   = new List<CriterionState>(selectedCriteria.Length);
        for (var i = 0; i < selectedCriteria.Length; i++)
        {
            var text   = selectedCriteria[i];
            var target = BestSectionFor(text, sections);
            list.Add(new CriterionState
            {
                Id               = $"c{i + 1}",
                Text             = text,
                Passed           = passed.Contains(text),
                FailureReason    = eval.FailureReasons.TryGetValue(text, out var r) ? r : null,
                TargetSectionIds = target is null ? [] : [target.Id]
            });
        }
        return list;
    }

    /// <summary>Best-effort criterion→section mapping by lexical relevance (reuses the Phase-3 scorer).</summary>
    private static DocumentSection? BestSectionFor(string criterionText, List<DocumentSection> sections)
    {
        DocumentSection? best = null;
        double bestScore = 0;
        foreach (var s in sections)
        {
            var score = Infrastructure.Retrieval.PromptContextBudget.Relevance(criterionText, $"{s.Heading}\n{s.Body}");
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // ── Live grounding gate + candidate vendors ─────────────────────────────────

    private static bool ShouldGroundWithLiveSearch(string templateType) =>
        templateType.ToLowerInvariant().Replace("_", "-")
            is "market-analysis" or "executive-summary" or "proposal"
            or "technical-specification" or "detailed-design";

    private static readonly string[] VendorLexicon =
    [
        "Azure", "AWS", "Amazon Web Services", "GCP", "Google Cloud", "OCI", "Oracle", "IBM",
        "Databricks", "Snowflake", "OpenAI", "Anthropic", "Cohere", "Mistral", "Hugging Face",
        "Salesforce", "SAP", "ServiceNow", "Workday", "Stripe", "Twilio", "MongoDB", "Elastic", "Datadog"
    ];

    /// <summary>Candidate vendors to ground: research competitor names ∪ a lexicon scan over the
    /// title/context/goal. Capped at 5 to bound search cost.</summary>
    private static IReadOnlyList<string> ExtractCandidateVendors(GenerateDocumentRequest request, string domain)
    {
        var set = new List<string>();
        void Add(string v) { if (!set.Contains(v, StringComparer.OrdinalIgnoreCase)) set.Add(v); }

        foreach (var c in request.CompetitorInsights ?? [])
            if (!string.IsNullOrWhiteSpace(c.CompetitorName)) Add(c.CompetitorName.Trim());

        var hay = $"{request.Title} {request.BlueprintContext} {request.SelectedGoal}";
        foreach (var v in VendorLexicon)
            if (hay.Contains(v, StringComparison.OrdinalIgnoreCase)) Add(v);

        return [.. set.Take(5)];
    }

    // ── Grounding source resolution (blueprint OR assessment) ──────────────────

    /// <summary>
    /// Returns the SystemBlueprint used to ground document generation: the cached blueprint
    /// when BlueprintId is set, otherwise a blueprint synthesised from the source assessment.
    /// Null when neither is cached (caller falls back to BlueprintContext prose).
    /// </summary>
    /// <summary>Grounding resolver for the by-id fix path (works from a StructuredDocument's ids).</summary>
    private SystemBlueprint? ResolveGroundingBlueprintForDoc(StructuredDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.BlueprintId)
            && cache.TryGet<SystemBlueprint>($"bp-by-id:{doc.BlueprintId}", out var bp))
            return bp;
        if (!string.IsNullOrWhiteSpace(doc.AssessmentId)
            && cache.TryGet<Assessment>($"assess-by-id:{doc.AssessmentId}", out var a))
            return SynthesiseFromAssessment(a);
        return null;
    }

    private SystemBlueprint? ResolveGroundingBlueprint(GenerateDocumentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BlueprintId)
            && cache.TryGet<SystemBlueprint>($"bp-by-id:{request.BlueprintId}", out var bp))
            return bp;

        if (!string.IsNullOrWhiteSpace(request.AssessmentId)
            && cache.TryGet<Assessment>($"assess-by-id:{request.AssessmentId}", out var assessment))
            return SynthesiseFromAssessment(assessment);

        return null;
    }

    /// <summary>
    /// Adapts an Assessment into a SystemBlueprint purely for document grounding: the
    /// assessment's narrative lands in CoreScenario; the application-development fields are
    /// marked not-applicable so documents render from the assessment, not an invented design.
    /// </summary>
    private static SystemBlueprint SynthesiseFromAssessment(Assessment a)
        => AssessmentGrounding.Synthesise(a);

    // ── Competitor section builder ────────────────────────────────────────────

    /// <summary>
    /// Extracts the competitor grounding strings from the request so they can be
    /// reused identically across every iteration without being rebuilt each time.
    /// Returns (competitorSection, competitorConstraint) — both empty strings when
    /// the request is not a market-analysis or has no competitor insights.
    /// </summary>
    private static (string Section, string Constraint) BuildCompetitorSections(
        GenerateDocumentRequest request)
    {
        if (!request.TemplateType.Equals("market-analysis", StringComparison.OrdinalIgnoreCase))
            return (string.Empty, string.Empty);

        // Market-analysis with no research-supplied competitor list (e.g. assessment-sourced docs):
        // still emit a constraint so the mandatory competitor matrix is grounded in the live RESEARCH
        // SOURCES and cite-or-placeholdered — never invented.
        if (request.CompetitorInsights is not { Length: > 0 })
            return (string.Empty,
                "\nCRITICAL — COMPETITOR GROUNDING:\n" +
                "  • Build the competitor matrix ONLY from competitors named in the RESEARCH SOURCES below.\n" +
                "  • For every capability or feature-gap cell, cite the supporting [S#].\n" +
                "  • If a cell cannot be sourced, write [REQUIRED: <competitor> <capability> — verify via the vendor's docs or an analyst report] — never invent a rating.\n" +
                "  • Do NOT generalise (e.g. 'major cloud providers') — name each competitor explicitly.\n");

        var lines = request.CompetitorInsights.Select(c =>
            $"  • {c.CompetitorName}\n" +
            $"    Feature gap vs our solution: {c.FeatureGap}\n" +
            $"    Strategic impact: {c.ImpactScore}\n" +
            $"    Recommended playbook: {c.StrategicPlaybook}");

        var section =
            "\n\nCOMPETITOR INTELLIGENCE (sourced from live market research — treat as authoritative):\n" +
            string.Join("\n\n", lines) + "\n";

        var constraint =
            "\nCRITICAL — COMPETITOR GROUNDING:\n" +
            "  • Use ONLY the competitors listed in COMPETITOR INTELLIGENCE above.\n" +
            "  • Do NOT invent, assume, or add any other competitor names.\n" +
            "  • Do NOT generalise (e.g. 'major cloud providers') — name each competitor explicitly.\n" +
            "  • Quote the specific feature gaps and playbooks provided — do not rephrase as generics.\n";

        return (section, constraint);
    }

    // ── Legacy single-pass (no mission fields) ────────────────────────────────

    private async Task<CorporateDocument> GenerateLegacyAsync(
        GenerateDocumentRequest request, CancellationToken ct)
    {
        var cacheKey = cache.ComputeKey(
            new { request.BlueprintId, request.AssessmentId, request.Title, request.TemplateType, request.Domain, request.BlueprintContext });

        if (request.IsRerun)
        {
            cache.Evict(cacheKey);
            logger.LogInformation("[Cache] Document evicted for rerun — key: {K}", cacheKey[..8]);
        }
        else if (cache.TryGet<CorporateDocument>(cacheKey, out var hit))
        {
            logger.LogInformation("[Cache] Document hit — key: {K}", cacheKey[..8]);
            return hit;
        }

        var (result, modelUsed) = await orchestrator.ExecuteAsync(
            "generate-document",
            async (provider, pCt) =>
            {
                var (sys, usr) = PromptBuilder.BuildDocument(request);
                var raw = await provider.CompleteAsync(sys, usr, pCt);
                return LLMResponseParser.ParseDocument(raw, request);
            },
            () => engine.CompileDocument(
                request.SourceId,
                request.Title,
                request.TemplateType,
                request.Domain),
            ct);

        var stamped = result with { ModelUsed = modelUsed };

        if (!modelUsed.Contains(LLMOrchestrator.HeuristicModelName, StringComparison.Ordinal))
        {
            var ttl = TimeSpan.FromHours(config.GetValue<double>("Cache:Document:TtlHours", 24.0));
            cache.Set(cacheKey, stamped, ttl);
        }

        return stamped;
    }
}
