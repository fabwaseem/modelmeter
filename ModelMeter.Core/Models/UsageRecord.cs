namespace ModelMeter.Core.Models;

/// <summary>Normalized usage event. Deliberately excludes prompt and response content.</summary>
public sealed record UsageRecord
{
    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public string? SessionId { get; init; }
    public string? ProjectName { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public decimal? EstimatedApiCost { get; init; }
    public CostDataKind CostKind { get; init; } = CostDataKind.ApiEquivalentEstimate;
    public string Source { get; init; } = "unknown";
}

public enum CostDataKind { Unknown, ApiEquivalentEstimate, ActualApiBilling }
