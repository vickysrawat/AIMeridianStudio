using System.Text.Json;

namespace MeridianStudio.API.Infrastructure.ExampleBank;

/// <summary>
/// Records every mission selection the user makes before generating a document.
/// No quality gate — every selection is a training signal.
/// Future calls to MissionSuggestionService inject past selections as few-shot context
/// so suggestions surface popular choices first.
/// Storage: {bankRoot}/selections/{templateType}.json, max 20 entries (rolling).
/// </summary>
public sealed class SelectionBankService(IConfiguration config, ILogger<SelectionBankService> logger)
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _root = Path.Combine(
        config.GetValue<string>("ExampleBank:Root", "example-bank")!, "selections");

    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int MaxEntries = 20;

    public sealed record SelectionEntry
    {
        public required string Domain { get; init; }
        public required string SolutionType { get; init; }
        public required string SelectedTone { get; init; }
        public required string SelectedGoal { get; init; }
        public required string[] SelectedCriteria { get; init; }
        public required bool WasRefined { get; init; }
        public required string Timestamp { get; init; }
        /// <summary>Sub-domain within the domain (nullable so existing selection files still deserialize).</summary>
        public string? SubDomain { get; init; }
    }

    public async Task RecordAsync(
        string templateType,
        string domain,
        string solutionType,
        string selectedTone,
        string selectedGoal,
        string[] selectedCriteria,
        bool wasRefined,
        CancellationToken ct = default,
        string? subDomain = null)
    {
        var entry = new SelectionEntry
        {
            Domain           = domain,
            SubDomain        = subDomain,
            SolutionType     = solutionType,
            SelectedTone     = selectedTone,
            SelectedGoal     = selectedGoal,
            SelectedCriteria = selectedCriteria,
            WasRefined       = wasRefined,
            Timestamp        = DateTimeOffset.UtcNow.ToString("O")
        };

        await _lock.WaitAsync(ct);
        try
        {
            var path = FilePath(templateType);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var entries = await LoadAsync<SelectionEntry>(path);
            entries.Add(entry);

            if (entries.Count > MaxEntries)
                entries = entries[^MaxEntries..];

            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(entries, _json), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SelectionBank] Failed to record selection for {T}", templateType);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns a formatted string of recent selections for injection into LLM prompts.
    /// Limited to the 5 most recent entries for the same domain/solutionType context.
    /// </summary>
    public async Task<string> GetContextAsync(
        string templateType, string domain, string solutionType,
        CancellationToken ct = default, string? subDomain = null)
    {
        try
        {
            var path = FilePath(templateType);
            if (!File.Exists(path)) return string.Empty;

            var entries = await LoadAsync<SelectionEntry>(path);

            var domainFiltered = entries
                .Where(e => string.IsNullOrWhiteSpace(domain) ||
                            e.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase) ||
                            domain.Contains(e.Domain, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Prefer same-sub-domain selections when a sub-domain is supplied; fall back to
            // the domain-filtered set if none match so we still surface popular choices.
            var scoped = !string.IsNullOrWhiteSpace(subDomain)
                ? domainFiltered.Where(e => !string.IsNullOrWhiteSpace(e.SubDomain)
                                            && e.SubDomain.Contains(subDomain, StringComparison.OrdinalIgnoreCase)).ToList()
                : domainFiltered;
            if (scoped.Count == 0) scoped = domainFiltered;

            var relevant = scoped.TakeLast(5).ToList();
            if (relevant.Count == 0) return string.Empty;

            return string.Join("\n", relevant.Select(e =>
                $"- Domain: {e.Domain}{(string.IsNullOrWhiteSpace(e.SubDomain) ? "" : $" / {e.SubDomain}")}, " +
                $"SolutionType: {e.SolutionType} → " +
                $"Tone: \"{e.SelectedTone}\", Goal label: \"{e.SelectedGoal[..Math.Min(60, e.SelectedGoal.Length)]}...\"" +
                $"{(e.WasRefined ? " (refined by user)" : "")}"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SelectionBank] Failed to load context for {T}", templateType);
            return string.Empty;
        }
    }

    private string FilePath(string templateType) =>
        Path.Combine(_root, $"{templateType.ToLowerInvariant().Replace(" ", "-")}.json");

    private static async Task<List<T>> LoadAsync<T>(string path)
    {
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<T>>(json, _json) ?? [];
    }
}
