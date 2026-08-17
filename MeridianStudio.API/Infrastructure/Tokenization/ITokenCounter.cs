namespace MeridianStudio.API.Infrastructure.Tokenization;

/// <summary>
/// Counts and trims text by token budget. Used by cost telemetry (Phase 0) and the
/// prompt-context budget allocator (Phase 3). Counts are a close upper-bound proxy —
/// the backing tokenizer is not byte-identical to each provider's own tokenizer, so
/// callers should keep a generous reserve rather than treating counts as exact.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Token count for <paramref name="text"/>; 0 for null/empty.</summary>
    int Count(string? text);

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it fits within
    /// <paramref name="maxTokens"/>, otherwise a prefix trimmed to approximately that
    /// many tokens. Never throws; trims on the character axis as a proxy.
    /// </summary>
    string TrimToTokens(string text, int maxTokens);
}
