using Microsoft.ML.Tokenizers;

namespace MeridianStudio.API.Infrastructure.Tokenization;

/// <summary>
/// <see cref="ITokenCounter"/> backed by the cl100k_base BPE tokenizer
/// (<see cref="TiktokenTokenizer"/>). If the tokenizer cannot be constructed at
/// runtime (missing encoding data, etc.) it degrades to a deterministic
/// chars-per-token heuristic so token accounting never breaks the request path.
/// Register as a singleton — the tokenizer is immutable and thread-safe.
/// </summary>
public sealed class TokenCounter : ITokenCounter
{
    // ~4 characters per token is the standard rule-of-thumb for English prose used
    // when the BPE backend is unavailable. Intentionally a slight over-estimate.
    private const double CharsPerToken = 4.0;

    private readonly Tokenizer? _tokenizer;

    public TokenCounter(ILogger<TokenCounter> logger)
    {
        try
        {
            _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[TokenCounter] BPE tokenizer unavailable — falling back to the chars/token heuristic.");
            _tokenizer = null;
        }
    }

    public int Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        if (_tokenizer is not null)
        {
            try { return _tokenizer.CountTokens(text); }
            catch { /* fall through to heuristic */ }
        }

        return Heuristic(text);
    }

    public string TrimToTokens(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text) || maxTokens <= 0) return string.Empty;
        if (Count(text) <= maxTokens) return text;

        // Proportional first cut, then shrink until within budget. Backend-agnostic so it
        // behaves identically whether the BPE tokenizer or the heuristic is active.
        var approxChars = Math.Min(text.Length, (int)(maxTokens * CharsPerToken));
        var slice = text[..approxChars];

        while (slice.Length > 0 && Count(slice) > maxTokens)
        {
            var shrink = Math.Max(1, slice.Length / 16);
            slice = slice[..(slice.Length - shrink)];
        }

        return slice;
    }

    private static int Heuristic(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);
}
