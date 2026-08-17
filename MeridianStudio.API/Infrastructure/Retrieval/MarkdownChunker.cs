using System.Text;
using MeridianStudio.API.Infrastructure.Tokenization;

namespace MeridianStudio.API.Infrastructure.Retrieval;

/// <summary>One structural unit of text plus its token cost.</summary>
public sealed record TextChunk(string Text, int TokenCount, string? SourceLabel = null);

/// <summary>
/// Splits text into structure-bounded chunks for budgeted assembly. Chunk boundaries are blank
/// lines, EXCEPT inside a fenced ``` code block — so Mermaid diagrams, SQL DDL, and code stay
/// whole. Markdown tables (contiguous "|" rows with no blank line between them) also stay whole.
/// The result is paragraph/block granularity: trimming drops whole blocks instead of cutting a
/// table row, a CREATE TABLE statement, or a diagram in half.
/// </summary>
public static class MarkdownChunker
{
    public static IReadOnlyList<TextChunk> Chunk(string text, ITokenCounter tokens, string? sourceLabel = null)
    {
        var chunks = new List<TextChunk>();
        if (string.IsNullOrEmpty(text)) return chunks;

        var lines   = text.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var inFence = false;

        void Flush()
        {
            var t = current.ToString().TrimEnd();
            if (t.Length > 0) chunks.Add(new TextChunk(t, tokens.Count(t), sourceLabel));
            current.Clear();
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                current.AppendLine(line);
                continue;
            }

            if (!inFence && string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            current.AppendLine(line);
        }

        Flush();
        return chunks;
    }
}
