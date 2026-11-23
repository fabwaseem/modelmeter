using ModelMeter.Core.Models;

namespace ModelMeter.Core.Aggregation;

public static class UsageAggregator
{
    public static UsageSummary Summarize(IEnumerable<UsageRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var items = records.ToArray();
        return new UsageSummary
        {
            InputTokens = items.Sum(x => x.InputTokens),
            OutputTokens = items.Sum(x => x.OutputTokens),
            CacheReadTokens = items.Sum(x => x.CacheReadTokens),
            CacheWriteTokens = items.Sum(x => x.CacheWriteTokens),
            ApiEquivalentCost = items.Sum(x => x.EstimatedApiCost ?? 0m),
            SessionCount = items.Where(x => !string.IsNullOrWhiteSpace(x.SessionId))
                .Select(x => (x.ProviderId, x.SessionId)).Distinct().Count(),
            ActiveProviderCount = items.Select(x => x.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        };
    }
}
