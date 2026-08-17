namespace MeridianStudio.API.Infrastructure.Validation;

public sealed record DiagramValidationResult(bool Ok, string? Error = null, string? ErrorSignature = null);

public sealed record DiagramRepairResult(
    bool Ok,
    string Repaired,
    IReadOnlyList<string> RulesApplied,
    string? Error = null,
    string? ErrorSignature = null);

public sealed record DocumentDiagramResult(int Index, bool Ok, string? Error);

public sealed record DocumentValidationResult(
    bool Ok,
    IReadOnlyList<DocumentDiagramResult> Diagrams,
    IReadOnlyList<string> Issues);

/// <summary>
/// Client for the browserless validator sidecar. Every method is <b>fail-soft</b>: when the sidecar
/// is disabled (<c>Validator:Enabled=false</c>), unreachable, or times out, it returns <c>null</c> so
/// callers leave the content unchanged — the feature can never break generation.
/// </summary>
public interface IDiagramValidator
{
    bool Enabled { get; }

    Task<DiagramValidationResult?> ValidateAsync(string source, CancellationToken ct = default);

    Task<DiagramRepairResult?> RepairAsync(string source, CancellationToken ct = default);

    Task<DocumentValidationResult?> ValidateDocumentAsync(string markdown, CancellationToken ct = default);
}
