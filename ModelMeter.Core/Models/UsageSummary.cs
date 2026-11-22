namespace ModelMeter.Core.Models;

public sealed record UsageSummary
{
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
    public decimal ApiEquivalentCost { get; init; }
    public int SessionCount { get; init; }
    public int ActiveProviderCount { get; init; }
}
