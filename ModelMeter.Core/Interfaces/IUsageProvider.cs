using ModelMeter.Core.Models;

namespace ModelMeter.Core.Interfaces;

/// <summary>Provider adapters normalize usage without exposing transcript data.</summary>
public interface IUsageProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderSnapshot> DetectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UsageRecord>> GetUsageAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
