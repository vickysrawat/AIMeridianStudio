using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WP = DocumentFormat.OpenXml.Wordprocessing;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;

namespace MeridianStudio.API.Infrastructure.Documents;

/// <summary>
/// Converts a GitHub-flavored Markdown document to PDF (QuestPDF) or DOCX (OpenXML) by walking the
/// Markdig AST and mapping block types to native elements. Inline content is flattened to plain
/// text (block structure — headings, paragraphs, lists, code, tables — is preserved); this is a
/// robust v1 export, not a full CommonMark renderer.
/// </summary>
public static class MarkdownConverter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── PDF (QuestPDF) ──────────────────────────────────────────────────────────

    public static byte[] ToPdf(string markdown, string title)
    {
        var doc = Markdig.Markdown.Parse(markdown ?? "", Pipeline);

        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11).LineHeight(1.35f));
                page.Footer().AlignCenter().Text(t => t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium));

                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    foreach (var block in doc)
                        RenderPdfBlock(col, block);
                });
            });
        }).GeneratePdf();
    }

    private static void RenderPdfBlock(ColumnDescriptor col, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                var size = h.Level switch { 1 => 20f, 2 => 16f, 3 => 13f, _ => 11.5f };
                col.Item().PaddingTop(h.Level <= 2 ? 8 : 4)
                   .Text(Flatten(h.Inline)).FontSize(size).Bold();
                break;

            case ParagraphBlock p:
                col.Item().Text(Flatten(p.Inline));
                break;

            case ListBlock list:
                var idx = 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var marker = list.IsOrdered ? $"{idx++}. " : "•  ";
                    var text = string.Join(" ", item.OfType<LeafBlock>().Select(lb => Flatten(lb.Inline)));
                    col.Item().PaddingLeft(12).Text($"{marker}{text}");
                }
                break;

            case QuoteBlock q:
                var quote = string.Join("\n", q.OfType<LeafBlock>().Select(lb => Flatten(lb.Inline)));
                col.Item().PaddingLeft(10).BorderLeft(2).BorderColor(Colors.Grey.Lighten1)
                   .PaddingLeft(8).Text(quote).Italic().FontColor(Colors.Grey.Darken1);
                break;

            case Markdig.Syntax.CodeBlock code:
                col.Item().Background(Colors.Grey.Lighten4).Padding(6)
                   .Text(GetCodeText(code)).FontFamily(QuestPDF.Helpers.Fonts.Consolas).FontSize(9.5f);
                break;

            case MdTable table:
                RenderPdfTable(col, table);
                break;

            case ThematicBreakBlock:
                col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                break;
        }
    }

    private static void RenderPdfTable(ColumnDescriptor col, MdTable table)
    {
        var rows = table.OfType<MdTableRow>().ToList();
        if (rows.Count == 0) return;
        var colCount = rows.Max(r => r.Count);

        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { for (var i = 0; i < colCount; i++) c.RelativeColumn(); });
            foreach (var row in rows)
            {
                var cells = row.OfType<MdTableCell>().ToList();
                for (var i = 0; i < colCount; i++)
                {
                    var text = i < cells.Count
                        ? string.Join(" ", cells[i].OfType<LeafBlock>().Select(lb => Flatten(lb.Inline)))
                        : "";
                    var cell = t.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4);
                    if (row.IsHeader) cell.Text(text).Bold();
                    else cell.Text(text);
                }
            }
        });
    }

    // ── DOCX (OpenXML) ──────────────────────────────────────────────────────────

    public static byte[] ToDocx(string markdown, string title)
    {
        var md = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();
            main.Document = new WP.Document();
            var body = main.Document.AppendChild(new Body());

            foreach (var block in md)
                RenderDocxBlock(body, block);
        }
        return ms.ToArray();
    }

    private static void RenderDocxBlock(Body body, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.AppendChild(HeadingParagraph(Flatten(h.Inline), h.Level));
                break;

            case ParagraphBlock p:
                body.AppendChild(TextParagraph(Flatten(p.Inline)));
                break;

            case ListBlock list:
                var idx = 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var marker = list.IsOrdered ? $"{idx++}. " : "•  ";
                    var text = string.Join(" ", item.OfType<LeafBlock>().Select(lb => Flatten(lb.Inline)));
                    body.AppendChild(TextParagraph(marker + text, indent: 360));
                }
                break;

            case QuoteBlock q:
                foreach (var lb in q.OfType<LeafBlock>())
                    body.AppendChild(TextParagraph(Flatten(lb.Inline), indent: 360, italic: true));
                break;

            case Markdig.Syntax.CodeBlock code:
                foreach (var line in GetCodeText(code).Split('\n'))
                    body.AppendChild(MonospaceParagraph(line));
                break;

            case MdTable table:
                body.AppendChild(BuildDocxTable(table));
                break;

            case ThematicBreakBlock:
                body.AppendChild(TextParagraph("———"));
                break;
        }
    }

    private static WP.Paragraph HeadingParagraph(string text, int level)
    {
        var sizeHalfPt = level switch { 1 => "40", 2 => "32", 3 => "26", _ => "23" }; // half-points
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new Bold(), new FontSize { Val = sizeHalfPt });
        return new WP.Paragraph(run) { ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { Before = "160", After = "60" }) };
    }

    private static WP.Paragraph TextParagraph(string text, int indent = 0, bool italic = false)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (italic) run.RunProperties = new RunProperties(new Italic());
        var p = new WP.Paragraph(run);
        if (indent > 0)
            p.ParagraphProperties = new ParagraphProperties(new Indentation { Left = indent.ToString() });
        return p;
    }

    private static WP.Paragraph MonospaceParagraph(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" }, new FontSize { Val = "18" });
        return new WP.Paragraph(run);
    }

    private static WP.Table BuildDocxTable(MdTable table)
    {
        var t = new WP.Table();
        t.AppendChild(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        foreach (var row in table.OfType<MdTableRow>())
        {
            var tr = new WP.TableRow();
            foreach (var cell in row.OfType<MdTableCell>())
            {
                var text = string.Join(" ", cell.OfType<LeafBlock>().Select(lb => Flatten(lb.Inline)));
                var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                if (row.IsHeader) run.RunProperties = new RunProperties(new Bold());
                tr.AppendChild(new WP.TableCell(new WP.Paragraph(run)));
            }
            t.AppendChild(tr);
        }
        return t;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Flattens inline content to plain text (literals + code + line breaks).</summary>
    private static string Flatten(ContainerInline? inline)
    {
        if (inline is null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var child in inline)
            AppendInline(sb, child);
        return sb.ToString().Trim();
    }

    private static void AppendInline(System.Text.StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
            case CodeInline code: sb.Append(code.Content); break;
            case LineBreakInline: sb.Append(' '); break;
            case LinkInline link:
                foreach (var c in link) AppendInline(sb, c);
                if (!string.IsNullOrEmpty(link.Url)) sb.Append($" ({link.Url})");
                break;
            case ContainerInline container:
                foreach (var c in container) AppendInline(sb, c);
                break;
        }
    }

    private static string GetCodeText(Markdig.Syntax.CodeBlock code)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in code.Lines.Lines)
        {
            var slice = line.Slice;
            if (slice.Text is not null) sb.AppendLine(slice.Text.Substring(slice.Start, slice.Length));
        }
        return sb.ToString().TrimEnd();
    }
}
