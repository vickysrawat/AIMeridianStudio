using System.Text;

namespace MeridianStudio.API.Infrastructure.Documents;

/// <summary>
/// Merges LLM patch output into an existing Markdown document by heading. A patch section whose
/// heading matches an existing one REPLACES that section in place — preserving its position and
/// preventing duplicates; genuinely new sections are appended at the end. This fixes the patch
/// loop re-adding the same section at the bottom of the document when a criterion is fixed twice.
/// </summary>
public static class DocumentSectionMerger
{
    private sealed record Section(int Level, string NormTitle, string Text);

    public static string Merge(string existing, string newSections)
    {
        if (string.IsNullOrWhiteSpace(newSections)) return existing;
        if (string.IsNullOrWhiteSpace(existing)) return newSections.Trim();

        var lines    = new List<string>(existing.Replace("\r\n", "\n").Split('\n'));
        var toAppend = new List<string>();

        foreach (var sec in SplitSections(newSections))
        {
            var idx = sec.NormTitle.Length == 0 ? -1 : FindHeading(lines, sec.NormTitle);
            if (idx < 0)
            {
                toAppend.Add(sec.Text.Trim());
                continue;
            }

            // Replace the existing section in place: from its heading to the next heading at the
            // same or higher level (fewer/equal '#'), or end of document.
            var level = HeadingLevel(lines[idx]);
            var end   = idx + 1;
            while (end < lines.Count)
            {
                var lvl = HeadingLevel(lines[end]);
                if (lvl is > 0 && lvl <= level) break;
                end++;
            }

            lines.RemoveRange(idx, end - idx);
            lines.InsertRange(idx, sec.Text.Trim().Split('\n'));
        }

        var sb = new StringBuilder(string.Join("\n", lines).TrimEnd());
        foreach (var block in toAppend)
            sb.Append("\n\n").Append(block);

        return sb.ToString();
    }

    private static List<Section> SplitSections(string md)
    {
        var lines      = md.Replace("\r\n", "\n").Split('\n');
        var headingIdx = new List<int>();
        for (var i = 0; i < lines.Length; i++)
            if (HeadingLevel(lines[i]) > 0) headingIdx.Add(i);

        var result = new List<Section>();

        if (headingIdx.Count == 0)
        {
            var whole = md.Trim();
            if (whole.Length > 0) result.Add(new Section(0, string.Empty, whole));
            return result;
        }

        // Any preamble before the first heading is kept as an untitled (always-appended) block.
        if (headingIdx[0] > 0)
        {
            var pre = string.Join("\n", lines[..headingIdx[0]]).Trim();
            if (pre.Length > 0) result.Add(new Section(0, string.Empty, pre));
        }

        for (var k = 0; k < headingIdx.Count; k++)
        {
            var start = headingIdx[k];
            var end   = k + 1 < headingIdx.Count ? headingIdx[k + 1] : lines.Length;
            var text  = string.Join("\n", lines[start..end]).TrimEnd();
            result.Add(new Section(HeadingLevel(lines[start]), Normalize(HeadingText(lines[start])), text));
        }

        return result;
    }

    private static int FindHeading(List<string> lines, string normTitle)
    {
        for (var i = 0; i < lines.Count; i++)
            if (HeadingLevel(lines[i]) > 0 && Normalize(HeadingText(lines[i])) == normTitle)
                return i;
        return -1;
    }

    private static int HeadingLevel(string line)
    {
        var t = line.TrimStart();
        var n = 0;
        while (n < t.Length && t[n] == '#') n++;
        return n is >= 1 and <= 6 && n < t.Length && t[n] == ' ' ? n : 0;
    }

    private static string HeadingText(string line)
    {
        var t = line.TrimStart();
        return t.TrimStart('#').Trim();
    }

    private static string Normalize(string text)
    {
        var sb        = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); lastSpace = false; }
            else if (!lastSpace)          { sb.Append(' '); lastSpace = true; }
        }
        return sb.ToString().Trim();
    }
}
