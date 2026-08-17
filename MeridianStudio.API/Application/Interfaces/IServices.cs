using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Interfaces;

public interface IResearchService
{
    Task<ResearchResponse> ResearchAsync(ResearchRequest request, CancellationToken ct = default);
}

public interface IBlueprintService
{
    Task<SystemBlueprint> GenerateBlueprintAsync(GenerateBlueprintRequest request, CancellationToken ct = default);

    /// <summary>
    /// Yields SSE-ready (event, data) pairs.
    /// "chunk" events carry raw LLM token text.
    /// "complete" event carries the final serialised SystemBlueprint JSON.
    /// </summary>
    IAsyncEnumerable<(string Event, string Data)> StreamBlueprintAsync(
        GenerateBlueprintRequest request, CancellationToken ct = default);

    /// <summary>
    /// Apply client-side overrides (e.g. edited arch decisions) to a cached blueprint.
    /// Returns null if the blueprint is not found in cache.
    /// </summary>
    Task<SystemBlueprint?> PatchBlueprintAsync(string blueprintId, PatchBlueprintRequest patch, CancellationToken ct = default);

    /// <summary>
    /// Regenerates the system topology using current blueprint state (archDecisions, techRadar,
    /// projectNotes). Streams LLM chunks then a complete event with the updated blueprint.
    /// </summary>
    IAsyncEnumerable<(string Event, string Data)> RegenerateTopologyAsync(
        string blueprintId, CancellationToken ct = default);
}

public interface IAssessmentService
{
    /// <summary>
    /// Streams a use-case Assessment via SSE. "chunk" events carry raw LLM text;
    /// the final "complete" event carries the serialised Assessment JSON.
    /// </summary>
    IAsyncEnumerable<(string Event, string Data)> StreamAssessmentAsync(
        AssessmentRequest request, CancellationToken ct = default);

    /// <summary>Apply client/chat overrides to a cached assessment. Null if not found.</summary>
    Task<Assessment?> PatchAssessmentAsync(
        string assessmentId, PatchAssessmentRequest patch, CancellationToken ct = default);

    /// <summary>Streams a section-scoped refinement conversation about a cached assessment.</summary>
    IAsyncEnumerable<(string Event, string Data)> StreamChatAsync(
        string assessmentId, BlueprintChatRequest request, CancellationToken ct = default);
}

public interface ITaskExecutionService
{
    Task<TaskSpec> ExecuteTaskAsync(ExecuteTaskRequest request, CancellationToken ct = default);
}

public interface IDocumentService
{
    Task<CorporateDocument> GenerateDocumentAsync(GenerateDocumentRequest request, CancellationToken ct = default);

    /// <summary>Repairs one section of a structured document to satisfy a single criterion (by-id fix).</summary>
    Task<CorporateDocument> FixSectionAsync(StructuredDocument doc, string criterionId, CancellationToken ct = default);
}

public interface IPromptService
{
    Task<DeveloperPrompt> GeneratePromptAsync(GenerateComponentPromptRequest request, CancellationToken ct = default);
}

public interface IBlueprintChatService
{
    IAsyncEnumerable<(string Event, string Data)> StreamChatAsync(
        string blueprintId, BlueprintChatRequest request, CancellationToken ct = default);
}

public interface IMissionSuggestionService
{
    Task<MissionSuggestions> GetSuggestionsAsync(MissionSuggestionsRequest request, CancellationToken ct = default);
}

public interface IUseCaseAnalysisService
{
    /// <summary>Critiques a use-case brief and returns a readiness review (score, per-field status,
    /// clarifying questions, and one-click-applicable improvement suggestions). Advisory only.</summary>
    Task<UseCaseReadiness> AnalyzeAsync(AssessmentRequest request, CancellationToken ct = default);
}
