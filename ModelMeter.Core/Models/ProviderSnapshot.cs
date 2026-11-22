namespace ModelMeter.Core.Models;

public sealed record ProviderSnapshot
{
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public ProviderState State { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public string? StatusMessage { get; init; }
    public bool HasSubscriptionQuota { get; init; }
    public bool HasApiBillingData { get; init; }
}

public enum ProviderState { NotDetected, Detected, Connected, NeedsSetup, UnsupportedVersion, Stale, Error, Disabled }
