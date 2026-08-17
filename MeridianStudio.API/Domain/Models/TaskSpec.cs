namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Code execution specification tracking progress and generated output for a discrete task.
/// </summary>
public sealed record TaskSpec
{
    public required string Id { get; init; }
    public required string TaskName { get; init; }
    public required string Status { get; init; }
    public required int ProgressScore { get; init; }
    public required string SystemicValue { get; init; }
    public required string EstimatedEffort { get; init; }
    public required string GeneratedCodeTemplate { get; init; }
    public required List<string> OutputLogs { get; init; }
    public string ModelUsed { get; init; } = string.Empty;

    public static TaskSpec Create(
        string id,
        string taskName,
        string status,
        int progressScore,
        string systemicValue,
        string estimatedEffort,
        string generatedCodeTemplate,
        List<string>? outputLogs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName, nameof(taskName));
        ArgumentException.ThrowIfNullOrWhiteSpace(status, nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(systemicValue, nameof(systemicValue));
        ArgumentException.ThrowIfNullOrWhiteSpace(estimatedEffort, nameof(estimatedEffort));
        ArgumentOutOfRangeException.ThrowIfNegative(progressScore, nameof(progressScore));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progressScore, 100, nameof(progressScore));

        return new TaskSpec
        {
            Id = id,
            TaskName = taskName,
            Status = status,
            ProgressScore = progressScore,
            SystemicValue = systemicValue,
            EstimatedEffort = estimatedEffort,
            GeneratedCodeTemplate = generatedCodeTemplate,
            OutputLogs = outputLogs ?? []
        };
    }
}
