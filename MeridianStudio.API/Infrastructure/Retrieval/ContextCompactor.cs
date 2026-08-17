using System.Text;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.Retrieval;

/// <summary>
/// EXTRACTIVE compaction: shortens text by keeping its leading whole sentences up to a token
/// budget — never paraphrases, so no fact is altered or invented. Used for the budget
/// allocator's lowest-priority narrative tier (and analogous to the chat-history compaction in
/// PromptBuilder). Never applied to authoritative content (schemas, endpoints, figures) — those
/// degrade by dropping whole chunks instead.
/// </summary>
public static class ContextCompactor
{
    public static string ToTokens(string text, int maxTokens, ITokenCounter tokens)
    {
        if (string.IsNullOrWhiteSpace(text) || maxTokens <= 0) return string.Empty;
        if (tokens.Count(text) <= maxTokens) return text;

        var sb = new StringBuilder();
        foreach (var sentence in SplitSentences(text))
        {
            if (tokens.Count(sb.ToString() + sentence) > maxTokens) break;
            sb.Append(sentence);
        }

        var kept = sb.ToString().TrimEnd();

        // First sentence alone already exceeds the budget — fall back to a proxy char trim.
        if (kept.Length == 0) kept = tokens.TrimToTokens(text, maxTokens).TrimEnd();

        return kept.Length == 0 ? string.Empty : kept + " […]";
    }

    // Splits keeping delimiters so reassembly is loss-free; breaks on ., !, ?, and newlines.
    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isBreak = c is '.' or '!' or '?' or '\n';
            var atBoundary = isBreak && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]) || c == '\n');
            if (atBoundary)
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }
}
