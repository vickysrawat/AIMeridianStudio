using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.Documents;

/// <summary>
/// Parses generated Markdown into the structured section model (stable ids per heading). Splits
/// only at heading boundaries — never inside a fenced ``` block — so code/tables/Mermaid stay whole.
/// Any preamble before the first heading becomes a Level-0 (heading-less) section. After parsing,
/// the structure is the source of truth and <see cref="DocumentRenderer"/> re-emits it; a fix
/// replaces a single section's body by id, so untouched sections keep their parsed text verbatim.
/// </summary>
public static class DocumentIndex
{
    public static List<DocumentSection> Parse(string markdown)
    {
        var sections = new List<DocumentSection>();
        if (string.IsNullOrWhiteSpace(markdown)) return sections;

        var lines   = markdown.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var n       = 0;

        int?   curLevel  = null;        // null = currently in the preamble (Level 0)
        string curHead   = string.Empty;
        var    curBody   = new List<string>();

        void Flush()
        {
            // Preamble with no content is dropped; otherwise emit.
            if (curLevel is null && curBody.All(string.IsNullOrWhiteSpace)) { curBody.Clear(); return; }
            var body = string.Join("\n", curBody).Trim();
            sections.Add(new DocumentSection
            {
                Id      = $"s{++n}",
                Heading = curHead,
                Level   = curLevel ?? 0,
                Body    = body
            });
            curBody.Clear();
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;

            var level = inFence ? 0 : HeadingLevel(line);
            if (level > 0)
            {
                Flush();
                curLevel = level;
                curHead  = HeadingText(line);
            }
            else
            {
                curBody.Add(line);
            }
        }
        Flush();

        return sections;
    }

    private static int HeadingLevel(string line)
    {
        var t = line.TrimStart();
        var n = 0;
        while (n < t.Length && t[n] == '#') n++;
        return n is >= 1 and <= 6 && n < t.Length && t[n] == ' ' ? n : 0;
    }

    private static string HeadingText(string line) => line.TrimStart().TrimStart('#').Trim();
}
