using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.Documents;

/// <summary>
/// Renders a <see cref="StructuredDocument"/> to Markdown deterministically: sections in order
/// (heading at its level + body), then a "## Sources" list when the document has sources. The
/// structure is the source of truth — this is the only place Markdown is produced, so a fix that
/// replaces one section node changes only that section's rendered text.
/// </summary>
public static class DocumentRenderer
{
    public static string Render(StructuredDocument doc)
    {
        var sb = new StringBuilder();

        foreach (var s in doc.Sections)
        {
            // Level 0 = heading-less preamble (rendered as body only).
            if (s.Level >= 1 && !string.IsNullOrWhiteSpace(s.Heading))
            {
                var hashes = new string('#', Math.Clamp(s.Level, 1, 6));
                sb.Append(hashes).Append(' ').Append(s.Heading.Trim()).Append("\n\n");
            }
            var body = s.Body.Trim();
            if (body.Length > 0) sb.Append(body).Append("\n\n");
        }

        sb.Append(RenderSources(doc.Sources));
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Builds the "## Sources" block (empty when there are no sources). Each line carries
    /// the clickable URL + grounded-as-of date — the human-verification handle.</summary>
    public static string RenderSources(IReadOnlyList<SourceRef> sources)
    {
        if (sources.Count == 0) return string.Empty;

        var sb = new StringBuilder("## Sources\n\n");
        foreach (var src in sources)
        {
            sb.Append("- ").Append(src.Id).Append(" — ").Append(src.Title.Trim());
            if (!string.IsNullOrWhiteSpace(src.FetchedAt))
                sb.Append(" (grounded ").Append(src.FetchedAt![..Math.Min(10, src.FetchedAt!.Length)]).Append(')');
            if (!string.IsNullOrWhiteSpace(src.Url))
                sb.Append(" — ").Append(src.Url);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
