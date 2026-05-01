namespace Codomon.Desktop.Services;

/// <summary>
/// Utility helpers for estimating the token cost of text before sending it to an LLM,
/// and for splitting a list of summaries into token-bounded batches.
/// </summary>
/// <remarks>
/// Token counting uses the widely-accepted heuristic of 1 token ≈ 4 characters of
/// English/code text.  This is an approximation; the exact count varies by tokenizer.
/// </remarks>
public static class LlmHelper
{
    /// <summary>Characters per token used by the estimation heuristic.</summary>
    public const int CharsPerToken = 4;

    // ── Estimation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Estimates the number of tokens in <paramref name="text"/> using the
    /// 4-chars-per-token heuristic.
    /// </summary>
    public static int EstimateTokenCount(string text)
        => (int)Math.Ceiling(text.Length / (double)CharsPerToken);

    /// <summary>
    /// Reads the content of each <paramref name="summaries"/> file from disk and returns
    /// the estimated total token count for all of them combined.
    /// Files that do not exist on disk are silently skipped.
    /// </summary>
    public static async Task<int> EstimateTokenCountAsync(
        IEnumerable<SummaryEntry> summaries,
        CancellationToken cancellationToken = default)
    {
        int total = 0;
        foreach (var entry in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(entry.SummaryFilePath)) continue;
            var content = await File.ReadAllTextAsync(entry.SummaryFilePath, cancellationToken);
            total += EstimateTokenCount(content);
        }
        return total;
    }

    // ── Batching ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Partitions <paramref name="summaries"/> into batches such that the estimated
    /// token count of each batch does not exceed <paramref name="tokenThreshold"/>.
    /// If a single summary already exceeds the threshold it is placed in its own batch.
    /// </summary>
    /// <param name="summaries">Ordered list of summaries to partition.</param>
    /// <param name="tokenThreshold">Maximum token budget per batch.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// A list of batches; each batch is a non-empty sublist of
    /// <paramref name="summaries"/>.
    /// </returns>
    public static async Task<List<List<SummaryEntry>>> SplitIntoBatchesAsync(
        IReadOnlyList<SummaryEntry> summaries,
        int tokenThreshold,
        CancellationToken cancellationToken = default)
    {
        var batches = new List<List<SummaryEntry>>();
        var current = new List<SummaryEntry>();
        int currentTokens = 0;

        foreach (var entry in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int entryTokens = 0;
            if (File.Exists(entry.SummaryFilePath))
            {
                var content = await File.ReadAllTextAsync(entry.SummaryFilePath, cancellationToken);
                entryTokens = EstimateTokenCount(content);
            }

            // If adding this entry would exceed the threshold and we already have
            // items in the current batch, seal the current batch and start a new one.
            if (current.Count > 0 && currentTokens + entryTokens > tokenThreshold)
            {
                batches.Add(current);
                current = new List<SummaryEntry>();
                currentTokens = 0;
            }

            current.Add(entry);
            currentTokens += entryTokens;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }
}
