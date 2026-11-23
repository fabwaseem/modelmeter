using ModelMeter.Core.Models;
using ModelMeter.Core.Aggregation;
using ModelMeter.Core.Storage;
using Xunit;

namespace ModelMeter.Core.Tests;

public sealed class UsageDatabaseTests
{
    [Fact]
    public async Task Upsert_IsIdempotentAndRoundTripsNormalizedUsage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "usage.db");
        var timestamp = DateTimeOffset.Parse("2026-11-22T10:15:00Z");
        var record = new UsageRecord
        {
            Id = "claude-session:message-1",
            ProviderId = "claude-code",
            ModelId = "claude-sonnet",
            SessionId = "session-1",
            ProjectName = "project-a",
            Timestamp = timestamp,
            InputTokens = 120,
            OutputTokens = 30,
            CacheReadTokens = 50,
            CacheWriteTokens = 40,
            CostKind = CostDataKind.Unknown,
            Source = "claude-code-local-jsonl"
        };

        try
        {
            await using var database = new UsageDatabase(path);
            await database.InitializeAsync();
            Assert.Equal(1, await database.UpsertAsync(new[] { record }));
            Assert.Equal(0, await database.UpsertAsync(new[] { record }));

            // Provider readers can correct previously imported metadata. Re-imports must update
            // changed rows without duplicating unchanged rows.
            var corrected = record with { InputTokens = 140, ModelId = "claude-sonnet-5" };
            Assert.Equal(1, await database.UpsertAsync(new[] { corrected }));
            Assert.Equal(0, await database.UpsertAsync(new[] { corrected }));

            var result = await database.GetUsageAsync(timestamp.AddHours(-1), timestamp.AddHours(1));
            var stored = Assert.Single(result);
            Assert.Equal(record.Id, stored.Id);
            Assert.Equal(record.ProviderId, stored.ProviderId);
            Assert.Equal(corrected.ModelId, stored.ModelId);
            Assert.Equal(record.SessionId, stored.SessionId);
            Assert.Equal(record.ProjectName, stored.ProjectName);
            Assert.Equal(record.Timestamp, stored.Timestamp);
            Assert.Equal(260, stored.InputTokens + stored.OutputTokens + stored.CacheReadTokens + stored.CacheWriteTokens);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
