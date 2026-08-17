using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeridianStudio.API.Infrastructure.Diagnostics;
using MeridianStudio.API.Infrastructure.LLM;
using MeridianStudio.API.Infrastructure.Validation;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Post-generation Mermaid repair pass over diagram-bearing content. Tiered self-healing:
///   1. learned-fix cache (zero LLM),
///   2. deterministic sidecar repair (fast, on the response path),
///   3. one-time LLM repair (background, off by default, fast-model) → verified → cached,
///   4. unresolved → logged to a corpus, original left in place (client fallback still renders).
/// Entirely fail-soft: when the validator is disabled/unreachable, content is returned unchanged.
/// </summary>
public sealed partial class DocumentValidationService(
    IDiagramValidator validator,
    ILearnedMermaidFixStore learned,
    LLMOrchestrator orchestrator,
    IConfiguration config,
    ILogger<DocumentValidationService> logger)
{
    [GeneratedRegex(@"```mermaid[ \t]*\r?\n([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex MermaidBlock();

    /// <summary>Repairs every fenced ```mermaid block in <paramref name="content"/> and splices it back.</summary>
    public async Task<string> RepairContentAsync(string content, string operation, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(content) || !validator.Enabled) return content;
        var matches = MermaidBlock().Matches(content);
        if (matches.Count == 0) return content;

        var sb = new StringBuilder(content.Length);
        var last = 0;
        foreach (Match m in matches)
        {
            sb.Append(content, last, m.Index - last);
            var block = m.Groups[1].Value.TrimEnd('\r', '\n');
            var repaired = await RepairBlockAsync(block, operation, ct);
            sb.Append("```mermaid\n").Append(repaired);
            if (!repaired.EndsWith('\n')) sb.Append('\n');
            sb.Append("```");
            last = m.Index + m.Length;
        }
        sb.Append(content, last, content.Length - last);
        return sb.ToString();
    }

    private async Task<string> RepairBlockAsync(string block, string operation, CancellationToken ct)
    {
        // 1. Learned-fix cache — deterministic, no LLM.
        var cached = learned.TryGet(block);
        if (cached is not null)
        {
            logger.LogInformation("[Validator] Learned-fix cache hit for a diagram in {Op}.", operation);
            return cached;
        }

        // 2. Deterministic sidecar repair (fast; validates internally).
        var result = await validator.RepairAsync(block, ct);
        if (result is null) return block;             // fail-soft: sidecar down → unchanged
        if (result.Ok)
        {
            if (result.RulesApplied.Count > 0)
                logger.LogInformation("[Validator] Repaired a diagram in {Op} via [{Rules}].",
                    operation, string.Join(", ", result.RulesApplied));
            return result.Repaired;
        }

        // 3. One-time LLM repair — background, off by default, never blocks the response.
        if (config.GetValue("Validator:LlmRepairEnabled", false))
            _ = Task.Run(() => LlmRepairAndLearnAsync(block, result.Error, result.ErrorSignature), CancellationToken.None);
        else
            RecordUnresolved(block, result.Error, result.ErrorSignature);

        // Current response keeps the original block; client-side fallback still renders it.
        return block;
    }

    /// <summary>Background: ask the LLM once for a fix, verify it headlessly, and cache it if valid.</summary>
    private async Task LlmRepairAndLearnAsync(string block, string? error, string? errorSignature)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ct = cts.Token;

            var (candidate, _) = await orchestrator.ExecuteAsync(
                "diagram-repair", // routed to a fast model via Routing:Profiles:diagram-repair
                async (provider, pct) =>
                {
                    var (sys, usr) = BuildRepairPrompt(block, error);
                    var raw = await provider.CompleteAsync(sys, usr, pct);
                    return ExtractMermaid(raw);
                },
                () => block, // heuristic fallback = no change (avoids a bad offline "fix")
                ct);

            if (string.IsNullOrWhiteSpace(candidate) || candidate == block) { RecordUnresolved(block, error, errorSignature); return; }

            var check = await validator.ValidateAsync(candidate, ct);
            if (check?.Ok == true)
            {
                learned.Record(block, candidate, errorSignature);
                logger.LogInformation("[Validator] LLM-tier repair verified + cached (sig {Sig}).", errorSignature ?? "?");
            }
            else
            {
                RecordUnresolved(block, error, errorSignature);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Validator] Background LLM repair failed.");
            RecordUnresolved(block, error, errorSignature);
        }
    }

    private void RecordUnresolved(string block, string? error, string? errorSignature)
        => learned.RecordUnresolved(block, error, errorSignature);

    private static (string System, string User) BuildRepairPrompt(string block, string? error)
    {
        const string sys =
            "You fix Mermaid diagram SYNTAX only. Do not change the diagram's meaning, nodes, or edges — " +
            "only make it parse. Node ids must be single tokens; put human text in bracket labels id[Label]. " +
            "Respond with ONLY valid JSON of the exact shape {\"mermaid\":\"<corrected diagram, \\n for newlines>\"}.";
        var usr =
            $"The following Mermaid failed to parse{(string.IsNullOrWhiteSpace(error) ? "" : $" with error: {error}")}.\n\n" +
            $"DIAGRAM:\n{block}\n\nReturn the corrected diagram as JSON.";
        return (sys, usr);
    }

    private static string ExtractMermaid(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                if (doc.RootElement.TryGetProperty("mermaid", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                    return mEl.GetString()!.Trim();
            }
            catch (JsonException) { /* fall through */ }
        }
        // Strip a ```mermaid fence if the model wrapped it.
        var fence = Regex.Match(t, @"```(?:mermaid)?\s*\n([\s\S]*?)```", RegexOptions.IgnoreCase);
        return (fence.Success ? fence.Groups[1].Value : t).Trim();
    }
}
