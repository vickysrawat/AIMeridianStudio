using MeridianStudio.API.Infrastructure.LLM.Embedding;
using MeridianStudio.API.Infrastructure.Retrieval;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.Diagnostics;

public sealed record SelfCheckResult(string Name, bool Passed, string Detail);

public sealed record SelfCheckReport(bool AllPassed, int Passed, int Total, IReadOnlyList<SelfCheckResult> Checks);

/// <summary>
/// Deterministic, offline invariant checks over the retrieval/budget/embedding machinery added in
/// Phases 0–4. Runs without API keys (uses whichever embedding provider is configured) and without
/// the app under generation load — a fast regression tripwire for the structural guarantees the
/// pipeline relies on. This is NOT the LLM golden-set evaluation (that needs live keys and graded
/// briefs); it guards the deterministic foundations those evaluations sit on.
/// </summary>
public sealed class SelfCheckService(
    ITokenCounter tokens,
    IEmbeddingProvider embedder,
    IDomainClassifier classifier)
{
    public async Task<SelfCheckReport> RunAsync(CancellationToken ct = default)
    {
        var checks = new List<SelfCheckResult>();
        void Check(string name, bool cond, string detail) => checks.Add(new SelfCheckResult(name, cond, detail));

        // 1. Token counter basics.
        Check("tokenizer.empty-is-zero", tokens.Count("") == 0, "Count(\"\") == 0");
        var shortCount = tokens.Count("hello");
        var longCount  = tokens.Count("hello world, this is a noticeably longer string with more tokens");
        Check("tokenizer.monotonic", longCount > shortCount, $"{longCount} > {shortCount}");

        // 2. Chunker keeps a fenced block (with internal blank lines) whole.
        const string md = "Intro paragraph.\n\n```mermaid\ngraph TD\n\nA-->B\n```\n\nOutro paragraph.";
        var chunks     = MarkdownChunker.Chunk(md, tokens);
        var fenceWhole = chunks.Any(c => c.Text.Contains("graph TD") && c.Text.Contains("A-->B"));
        Check("chunker.fence-intact", fenceWhole, $"{chunks.Count} chunks; fence kept whole={fenceWhole}");

        // 3. Budget allocator respects its bound.
        const int budget = 100;
        var bigBody   = string.Join("\n\n", Enumerable.Range(0, 60).Select(i => $"Para {i} lorem ipsum dolor sit amet."));
        var sections  = new List<BudgetSection> { new("--- A ---", bigBody, 1, false, 0) };
        var assembled = PromptContextBudget.Assemble("HEADER\n", sections, budget, tokens);
        var asmTokens = tokens.Count(assembled);
        Check("budget.within-bound", asmTokens <= budget + 20, $"{asmTokens} <= {budget}+20");

        // 4. Compactor stays within budget and non-empty.
        var longText  = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i}"));
        var compacted = ContextCompactor.ToTokens(longText, 30, tokens);
        Check("compactor.within-bound", compacted.Length > 0 && tokens.Count(compacted) <= 40,
            $"len={compacted.Length}, tokens={tokens.Count(compacted)} <= 40");

        // 5. Cosine identity / space-mismatch safety.
        Check("cosine.identity", Math.Abs(VectorMath.Cosine([1f, 2f, 3f], [1f, 2f, 3f]) - 1.0) < 1e-6, "cos(x,x)=1");
        Check("cosine.mismatch-zero", VectorMath.Cosine([1f, 2f], [1f, 2f, 3f]) == 0, "different lengths → 0");

        // 6. Embedding self-similarity beats cross-topic similarity.
        var a    = await embedder.EmbedAsync("financial banking payments fraud detection", ct);
        var b    = await embedder.EmbedAsync("financial banking payments fraud detection", ct);
        var cVec = await embedder.EmbedAsync("pediatric clinical diagnosis hospital imaging", ct);
        var same = VectorMath.Cosine(a, b);
        var diff = VectorMath.Cosine(a, cVec);
        Check("embedding.self-similar", same >= diff, $"same={same:F3} >= diff={diff:F3} (space={embedder.SpaceId})");

        // 7. Domain classifier maps an obvious input to the right vertical.
        var cls = await classifier.ClassifyAsync("clinical patient diagnosis hospital ehr fhir", ct);
        Check("classifier.healthcare", cls.Domain == "Healthcare AI", $"got '{cls.Domain}' ({cls.Confidence:P0} via {cls.Method})");

        var passed = checks.Count(c => c.Passed);
        return new SelfCheckReport(passed == checks.Count, passed, checks.Count, checks);
    }
}
