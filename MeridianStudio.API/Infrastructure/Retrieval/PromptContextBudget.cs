using System.Text;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.Retrieval;

/// <summary>
/// A prioritised, relevance-ranked region of context competing for a shared token budget.
/// Lower <see cref="Priority"/> fills first; within equal priority, higher <see cref="Relevance"/>
/// wins. <see cref="Authoritative"/> content (schemas, endpoints, figures) is never compacted —
/// it degrades by dropping whole chunks; non-authoritative narrative may be extractively compacted.
/// </summary>
public sealed record BudgetSection(
    string Label,
    string Body,
    int Priority,
    bool Authoritative,
    double Relevance);

/// <summary>
/// Assembles context to fit a token budget. Fills sections in priority/relevance order; within a
/// section, includes whole structural chunks until the budget is reached, then either extractively
/// compacts the next chunk (narrative) or stops (authoritative). Lower-priority sections naturally
/// shrink first because higher-priority ones consume the budget before them.
/// </summary>
public static class PromptContextBudget
{
    private const int MinUsefulTokens = 50;

    public static string Assemble(
        string headerBlock,
        IReadOnlyList<BudgetSection> sections,
        int budgetTokens,
        ITokenCounter tokens)
    {
        var sb        = new StringBuilder(headerBlock);
        var remaining = budgetTokens - tokens.Count(headerBlock);

        foreach (var sec in sections.OrderBy(s => s.Priority).ThenByDescending(s => s.Relevance))
        {
            if (remaining <= MinUsefulTokens) break;

            var labelTokens = tokens.Count(sec.Label) + 2;
            if (labelTokens >= remaining) continue;

            var included = new StringBuilder();
            var used     = 0;

            foreach (var chunk in MarkdownChunker.Chunk(sec.Body, tokens))
            {
                if (used + chunk.TokenCount <= remaining - labelTokens)
                {
                    included.AppendLine(chunk.Text);
                    used += chunk.TokenCount;
                    continue;
                }

                // Doesn't fit: narrative may be extractively compacted into the leftover;
                // authoritative content is left whole (drop the remaining chunks).
                if (!sec.Authoritative)
                {
                    var leftover = remaining - labelTokens - used;
                    if (leftover > MinUsefulTokens / 2)
                    {
                        var compacted = ContextCompactor.ToTokens(chunk.Text, leftover, tokens);
                        if (compacted.Length > 0)
                        {
                            included.AppendLine(compacted);
                            used += tokens.Count(compacted);
                        }
                    }
                }
                break;
            }

            if (used == 0) continue;

            sb.Append('\n').Append(sec.Label).Append('\n')
              .Append(included.ToString().TrimEnd()).Append('\n');
            remaining -= labelTokens + used;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Cheap lexical relevance of <paramref name="text"/> to <paramref name="query"/> — the
    /// fraction of distinct query terms present in the text (0..1). Used to order sections within
    /// a priority without per-call embedding cost. Returns 0 when the query is empty (order then
    /// falls back to priority alone).
    /// </summary>
    public static double Relevance(string? query, string text)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;

        var queryTerms = Terms(query);
        if (queryTerms.Count == 0) return 0;

        var textTerms = Terms(text);
        var hits      = queryTerms.Count(textTerms.Contains);
        return (double)hits / queryTerms.Count;
    }

    private static HashSet<string> Terms(string s)
    {
        var set     = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                if (current.Length > 2) set.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 2) set.Add(current.ToString());
        return set;
    }
}
