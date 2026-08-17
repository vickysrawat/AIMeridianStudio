using System.Text.RegularExpressions;

namespace MeridianStudio.API.Infrastructure.Documents;

/// <summary>
/// Small, safe cleanups applied to generated Markdown at the source so every surface (in-app render,
/// Copy Markdown, .txt/.pdf/.docx export) is uniformly clean.
/// </summary>
public static partial class MarkdownSanitizer
{
    [GeneratedRegex(@"\\+[ \t]*$")]
    private static partial Regex TrailingBackslashes();

    /// <summary>
    /// Removes trailing hard-break backslashes the LLM emits at line ends (CommonMark's "\ at EOL = line
    /// break"), which show as literal "\" in simple renderers. Fence-aware: backslashes inside fenced
    /// ```code``` / ```mermaid blocks are preserved. Idempotent; a no-op when there are no backslashes.
    /// </summary>
    public static string StripHardBreakBackslashes(string? content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains('\\')) return content ?? string.Empty;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            lines[i] = TrailingBackslashes().Replace(lines[i], "");
        }
        return string.Join("\n", lines);
    }
}
