using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Cache;
using MeridianStudio.API.Infrastructure.LLM;

namespace MeridianStudio.API.Application.Services;

public sealed class BlueprintChatService(
    PayloadCache cache,
    IEnumerable<ILLMProvider> providers,
    ILogger<BlueprintChatService> logger) : IBlueprintChatService
{
    private readonly IReadOnlyList<ILLMProvider> _providers = [.. providers];

    public async IAsyncEnumerable<(string Event, string Data)> StreamChatAsync(
        string blueprintId,
        BlueprintChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!cache.TryGet<SystemBlueprint>($"bp-by-id:{blueprintId}", out var bp))
        {
            logger.LogWarning("[Chat] Blueprint {Id} not found in cache.", blueprintId);
            yield return ("error", "Blueprint not found. Please generate the blueprint first.");
            yield break;
        }

        var (sys, usr) = PromptBuilder.BuildBlueprintChat(bp, request);
        var rawBuilder  = new StringBuilder();

        // Try providers in order — same pattern as StreamBlueprintAsync
        IAsyncEnumerator<string>? active = null;
        string? firstChunk = null;
        var modelUsed = "Heuristic Engine (Offline)";

        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured) continue;

            var enumerator = provider.StreamAsync(sys, usr, ct).GetAsyncEnumerator(ct);
            bool hasFirst;

            try { hasFirst = await enumerator.MoveNextAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Chat] {P} failed before first chunk.", provider.Name);
                await enumerator.DisposeAsync();
                continue;
            }

            if (!hasFirst) { await enumerator.DisposeAsync(); continue; }

            active     = enumerator;
            firstChunk = enumerator.Current;
            modelUsed  = provider.Name;
            break;
        }

        if (active is not null && firstChunk is not null)
        {
            rawBuilder.Append(firstChunk);
            yield return ("chunk", firstChunk);

            await using (active)
            {
                while (await active.MoveNextAsync())
                {
                    rawBuilder.Append(active.Current);
                    yield return ("chunk", active.Current);
                }
            }
        }
        else
        {
            logger.LogInformation("[Chat] All providers exhausted — no streaming response.");
            yield return ("chunk", "I'm currently offline. Please ensure an API key is configured.");
        }

        // Post-process: extract <apply>...</apply> and emit as separate event
        var full       = rawBuilder.ToString();
        var applyMatch = Regex.Match(full, @"<apply>([\s\S]*?)</apply>", RegexOptions.Singleline);

        if (applyMatch.Success)
        {
            logger.LogInformation("[Chat] Apply patch detected for blueprint {Id} section {S}.",
                blueprintId[..Math.Min(8, blueprintId.Length)], request.SectionKey);

            // Re-serialise to compact single-line JSON — the LLM often emits indented JSON
            // inside <apply> blocks. Multi-line JSON breaks the SSE data: line format because
            // our Angular pump splits on \n and only reads the first data: line.
            var rawApply = applyMatch.Groups[1].Value.Trim();
            string compactApply;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawApply);
                compactApply  = System.Text.Json.JsonSerializer.Serialize(doc.RootElement);
            }
            catch
            {
                compactApply = rawApply; // fallback: use as-is
            }

            yield return ("apply", compactApply);
        }

        yield return ("done", "{}");
    }
}
