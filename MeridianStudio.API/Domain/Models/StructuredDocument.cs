namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// A document represented as structure (the source of truth); Markdown is a deterministic
/// render of this via <c>DocumentRenderer</c>. Sections have stable ids so a fix replaces a
/// node by id (no duplicates / stable placement); criteria carry frozen per-criterion status
/// and native section mapping; sources are the provenance pool referenced by section citations.
/// </summary>
public sealed record StructuredDocument
{
    public required string DocumentId { get; init; }
    public required string Title { get; init; }
    public required string TemplateType { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string SubDomain { get; init; } = string.Empty;
    public string? BlueprintId { get; init; }
    public string? AssessmentId { get; init; }
    /// <summary>The goal the document was generated against — used to re-judge a criterion on fix.</summary>
    public string Goal { get; init; } = string.Empty;
    public string? BlueprintContext { get; init; }

    public List<DocumentSection> Sections { get; init; } = [];
    public List<CriterionState> Criteria { get; init; } = [];
    public List<SourceRef> Sources { get; init; } = [];
}

/// <summary>One section of the document. <see cref="Body"/> is Markdown (the section's content,
/// excluding its heading). Identified by a stable <see cref="Id"/> so fixes target it directly.</summary>
public sealed record DocumentSection
{
    public required string Id { get; init; }
    public required string Heading { get; init; }
    /// <summary>Markdown heading level (1=#, 2=##, …). Defaults to 2.</summary>
    public int Level { get; init; } = 2;
    public string Body { get; init; } = string.Empty;
    /// <summary>Ids of the criteria this section addresses (native criterion→section mapping).</summary>
    public string[] CriterionIds { get; init; } = [];
    /// <summary>Ids of the sources (S#) this section's claims cite.</summary>
    public string[] CitationIds { get; init; } = [];
}

/// <summary>Frozen criterion in the stack: its text, current pass/fail status, the judge's
/// failure reason, and the section(s) that address it (set as fixes map them).</summary>
public sealed record CriterionState
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public bool Passed { get; init; }
    public string? FailureReason { get; init; }
    public string[] TargetSectionIds { get; init; } = [];
}

/// <summary>A provenance source in the document's pool. <see cref="Url"/> + <see cref="FetchedAt"/>
/// are the human-verification handle, surfaced beside every citation.</summary>
public sealed record SourceRef
{
    public required string Id { get; init; }          // "S1", "S2", …
    public required string Title { get; init; }
    public string? Url { get; init; }
    /// <summary>blueprint | assessment | research</summary>
    public string Origin { get; init; } = "research";
    /// <summary>ISO-8601 date the source was grounded/fetched ("grounded as of …").</summary>
    public string? FetchedAt { get; init; }
    /// <summary>The supporting excerpt the verifier checks claims against (Phase B).</summary>
    public string? Excerpt { get; init; }
}
