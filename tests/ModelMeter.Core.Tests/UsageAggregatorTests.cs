using ModelMeter.Core.Aggregation;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Tests;

public sealed class UsageAggregatorTests
{
    [Fact]
    public void Summarize_AddsBucketsAndCountsDistinctSessionsAndProviders()
    {
        var records = new[]
        {
            new UsageRecord { Id = "1", ProviderId = "claude", ModelId = "sonnet", SessionId = "s1", InputTokens = 100, OutputTokens = 50, CacheReadTokens = 20, EstimatedApiCost = 0.01m },
            new UsageRecord { Id = "2", ProviderId = "claude", ModelId = "sonnet", SessionId = "s1", InputTokens = 30, OutputTokens = 10, CacheWriteTokens = 5, EstimatedApiCost = 0.02m },
            new UsageRecord { Id = "3", ProviderId = "codex", ModelId = "gpt", SessionId = "s2", InputTokens = 200, OutputTokens = 80 }
        };

        var summary = UsageAggregator.Summarize(records);

        Assert.Equal(330, summary.InputTokens);
        Assert.Equal(140, summary.OutputTokens);
        Assert.Equal(20, summary.CacheReadTokens);
        Assert.Equal(5, summary.CacheWriteTokens);
        Assert.Equal(495, summary.TotalTokens);
        Assert.Equal(2, summary.SessionCount);
        Assert.Equal(2, summary.ActiveProviderCount);
        Assert.Equal(0.03m, summary.ApiEquivalentCost);
    }

    [Fact]
    public void Summarize_EmptySequenceReturnsZeroes()
    {
        var summary = UsageAggregator.Summarize(Array.Empty<UsageRecord>());
        Assert.Equal(0, summary.TotalTokens);
        Assert.Equal(0, summary.SessionCount);
    }
}
