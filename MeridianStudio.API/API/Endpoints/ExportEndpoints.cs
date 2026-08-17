using MeridianStudio.API.Infrastructure.Documents;

namespace MeridianStudio.API.API.Endpoints;

/// <summary>
/// Stateless markdown → file conversion. Lets any client (e.g. the Use Case / assessment view, which
/// isn't a persisted artifact) download PDF/DOCX/Markdown without first saving an artifact.
/// </summary>
public static class ExportEndpoints
{
    public sealed record ExportMarkdownRequest
    {
        public required string Title { get; init; }
        public required string Markdown { get; init; }
        /// <summary>markdown | pdf | docx</summary>
        public string Format { get; init; } = "pdf";
    }

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/export", Handle)
              .WithName("ExportMarkdown")
              .WithTags("Export")
              .WithSummary("Convert markdown to markdown | pdf | docx and download");
        return routes;
    }

    private static IResult Handle(ExportMarkdownRequest request, ILoggerFactory lf)
    {
        var log = lf.CreateLogger("Export");
        if (string.IsNullOrWhiteSpace(request.Markdown))
            return Results.BadRequest(new { error = "markdown is required." });

        var fmt = (request.Format ?? "pdf").Trim().ToLowerInvariant();
        if (fmt is not ("markdown" or "md" or "pdf" or "docx"))
            return Results.BadRequest(new { error = $"Unsupported format '{request.Format}'. Use markdown | pdf | docx." });

        // Clean trailing hard-break backslashes so downloads (incl. assessments) never carry the artifact.
        var markdown = MarkdownSanitizer.StripHardBreakBackslashes(request.Markdown);
        var slug = Slug(request.Title);
        try
        {
            return fmt switch
            {
                "pdf"  => Results.File(MarkdownConverter.ToPdf(markdown, request.Title), "application/pdf", $"{slug}.pdf"),
                "docx" => Results.File(MarkdownConverter.ToDocx(markdown, request.Title),
                              "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{slug}.docx"),
                _      => Results.File(System.Text.Encoding.UTF8.GetBytes(markdown), "text/markdown", $"{slug}.md"),
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Export] {Fmt} conversion failed.", fmt);
            return Results.Problem(title: "Export conversion failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string Slug(string s)
    {
        var chars = (s ?? "document").Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "document" : slug[..Math.Min(60, slug.Length)];
    }
}
