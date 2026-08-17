using MeridianStudio.API.Application.Contracts;

namespace MeridianStudio.API.Infrastructure.Guard;

/// <summary>
/// Centralised input validation and sanitization for all API endpoints.
/// Used by endpoint handlers for early rejection (400) and by PromptBuilder
/// for defense-in-depth sanitization before user content is embedded in prompts.
/// </summary>
public static class InputGuard
{
    // ── Max length constants ──────────────────────────────────────────────────
    public const int MaxKeywordsLength    = 500;
    public const int MaxFeedbackLength    = 1000;
    public const int MaxNameLength        = 200;
    public const int MaxContextLength     = 1000;
    public const int MaxIdLength          = 100;
    public const int MaxDomainLength      = 1000;
    public const int MaxTitleLength       = 200;
    public const int MaxSystemicLength    = 500;

    private static readonly HashSet<string> AllowedTemplateTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "executive-summary", "market-analysis", "technical-specification", "proposal",
            "governance-adr", "developer-handbook", "detailed-design"
        };

    // Known prompt-injection / jailbreak phrases — checked case-insensitively
    private static readonly string[] InjectionPatterns =
    [
        "ignore previous instructions",
        "ignore all previous",
        "disregard your",
        "forget your instructions",
        "reveal your system prompt",
        "you are now",
        "act as if",
        "pretend you are",
        "jailbreak",
        "dan mode",
        "developer mode",
        "override instructions",
        "new instructions:"
    ];

    // ── Per-request validators ────────────────────────────────────────────────

    public static Dictionary<string, string[]>? ValidateResearch(ResearchRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(req.Keywords),     req.Keywords,     MaxKeywordsLength, required: true);
        Check(errors, nameof(req.UserFeedback),  req.UserFeedback, MaxFeedbackLength);
        if (req.Page < 1)
            errors[nameof(req.Page)] = ["Page must be ≥ 1."];
        return errors.Count > 0 ? errors : null;
    }

    public static Dictionary<string, string[]>? ValidateBlueprint(GenerateBlueprintRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(req.SolutionId),   req.SolutionId,   MaxIdLength,     required: true, injection: false);
        Check(errors, nameof(req.SolutionName),  req.SolutionName, MaxNameLength,   required: true);
        Check(errors, nameof(req.Domain),        req.Domain,       MaxDomainLength);
        return errors.Count > 0 ? errors : null;
    }

    public static Dictionary<string, string[]>? ValidateTask(ExecuteTaskRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(req.TaskName),     req.TaskName,      MaxNameLength,    required: true);
        Check(errors, nameof(req.Context),       req.Context,       MaxContextLength);
        Check(errors, nameof(req.SystemicValue), req.SystemicValue, MaxSystemicLength);
        return errors.Count > 0 ? errors : null;
    }

    public static Dictionary<string, string[]>? ValidateAssessment(AssessmentRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        // Require at least the free-form scenario or one structured field.
        var hasAny = !string.IsNullOrWhiteSpace(req.UseCaseScenario)
                  || !string.IsNullOrWhiteSpace(req.UseCase)
                  || !string.IsNullOrWhiteSpace(req.ProblemStatement)
                  || !string.IsNullOrWhiteSpace(req.Objective)
                  || !string.IsNullOrWhiteSpace(req.ExpectedOutcome);
        if (!hasAny)
            errors[nameof(req.UseCaseScenario)] = ["Provide a scenario or at least one brief field (use case, problem, objective, or expected outcome)."];

        // Bound length only; injection scanning would false-positive on prose.
        // UseCaseScenario and UseCase accept full RFP/requirement documents — use generous limits.
        Check(errors, nameof(req.UseCaseScenario),  req.UseCaseScenario,  20_000, injection: false);
        Check(errors, nameof(req.UseCase),          req.UseCase,           8_000, injection: false);
        Check(errors, nameof(req.Context),          req.Context,           8_000, injection: false);
        Check(errors, nameof(req.ProblemStatement), req.ProblemStatement,  8_000, injection: false);
        Check(errors, nameof(req.Objective),        req.Objective,         8_000, injection: false);
        Check(errors, nameof(req.ScopeOfWork),      req.ScopeOfWork,      12_000, injection: false);
        Check(errors, nameof(req.ExpectedOutcome),  req.ExpectedOutcome,   8_000, injection: false);
        Check(errors, nameof(req.Domain),           req.Domain,           MaxDomainLength);
        return errors.Count > 0 ? errors : null;
    }

    public static Dictionary<string, string[]>? ValidateDocument(GenerateDocumentRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        // Exactly one grounding source is required: a blueprint or an assessment.
        if (string.IsNullOrWhiteSpace(req.BlueprintId) && string.IsNullOrWhiteSpace(req.AssessmentId))
            errors[nameof(req.BlueprintId)] = ["Either BlueprintId or AssessmentId must be provided."];
        Check(errors, nameof(req.BlueprintId),  req.BlueprintId,  MaxIdLength, injection: false);
        Check(errors, nameof(req.AssessmentId), req.AssessmentId, MaxIdLength, injection: false);
        Check(errors, nameof(req.Title),         req.Title,       MaxTitleLength, required: true);
        Check(errors, nameof(req.Domain),        req.Domain,      MaxDomainLength);

        if (string.IsNullOrWhiteSpace(req.TemplateType))
            errors[nameof(req.TemplateType)] = ["TemplateType must not be empty."];
        else if (!AllowedTemplateTypes.Contains(req.TemplateType))
            errors[nameof(req.TemplateType)] =
            [
                $"TemplateType '{req.TemplateType}' is not valid. " +
                $"Allowed values: {string.Join(", ", AllowedTemplateTypes)}."
            ];

        return errors.Count > 0 ? errors : null;
    }

    public static Dictionary<string, string[]>? ValidatePrompt(GenerateComponentPromptRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(req.ComponentName), req.ComponentName, MaxNameLength,   required: true);
        Check(errors, nameof(req.Context),        req.Context,       MaxContextLength);
        return errors.Count > 0 ? errors : null;
    }

    private static readonly HashSet<string> AllowedLanguages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "csharp", "typescript", "python", "java", "go"
        };

    public static Dictionary<string, string[]>? ValidateProject(GenerateProjectRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(req.SolutionName), req.SolutionName, MaxNameLength, required: true);
        Check(errors, nameof(req.Description),  req.Description,  MaxContextLength);
        if (!string.IsNullOrWhiteSpace(req.Language) && !AllowedLanguages.Contains(req.Language))
            errors[nameof(req.Language)] =
            [
                $"Language '{req.Language}' is not supported. " +
                $"Allowed: {string.Join(", ", AllowedLanguages)}."
            ];
        return errors.Count > 0 ? errors : null;
    }

    // ── Sanitization ──────────────────────────────────────────────────────────

    /// <summary>
    /// Trims whitespace and truncates to <paramref name="maxLength"/>.
    /// Returns null when input is null.
    /// </summary>
    public static string? Sanitize(string? value, int maxLength)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static void Check(
        Dictionary<string, string[]> errors,
        string field,
        string? value,
        int maxLength,
        bool required  = false,
        bool injection = true)
    {
        var fieldErrors = new List<string>();

        if (required && string.IsNullOrWhiteSpace(value))
        {
            fieldErrors.Add($"{field} must not be empty.");
        }
        else
        {
            if (value?.Length > maxLength)
                fieldErrors.Add($"{field} exceeds the maximum length of {maxLength} characters.");
            if (injection && value is not null && HasInjection(value))
                fieldErrors.Add($"{field} contains disallowed content.");
        }

        if (fieldErrors.Count > 0)
            errors[field] = [.. fieldErrors];
    }

    private static bool HasInjection(string input)
    {
        var lower = input.ToLowerInvariant();
        return InjectionPatterns.Any(p => lower.Contains(p));
    }
}
