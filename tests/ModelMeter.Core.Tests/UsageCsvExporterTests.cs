using System.Globalization;
using ModelMeter.Core.Models;
using ModelMeter.Core.Storage;
using Xunit;

namespace ModelMeter.Core.Tests;

public sealed class UsageCsvExporterTests
{
    [Fact]
    public async Task WriteAsync_ExportsSortedEscapedPrivacySafeUsageMetadata()
    {
        var records = new[]
        {
            new UsageRecord
            {
                Id = "later", ProviderId = "opencode", ModelId = "model,large",
                Timestamp = DateTimeOffset.Parse("2026-11-22T12:00:00+02:00"),
                InputTokens = 10, OutputTokens = 20, CacheReadTokens = 30,
                CacheWriteTokens = 40, EstimatedApiCost = 1.25m,
                CostKind = CostDataKind.ApiEquivalentEstimate,
                SessionId = "session \"quoted\"", ProjectName = "project, one",
                Source = "local-db"
            },
            new UsageRecord
            {
                Id = "earlier", ProviderId = "claude-code", ModelId = "sonnet",
                Timestamp = DateTimeOffset.Parse("2026-11-22T08:00:00Z"),
                InputTokens = 1, OutputTokens = 2, Source = "jsonl"
            }
        };

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await UsageCsvExporter.WriteAsync(records, writer);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("timestamp_utc,provider_id,model_id", lines[0]);
        Assert.Contains(",claude-code,sonnet,", lines[1]);
        Assert.Contains("\"model,large\"", lines[2]);
        Assert.Contains("\"session \"\"quoted\"\"", lines[2]);
        Assert.Contains("\"project, one\"", lines[2]);
        Assert.EndsWith(",10,20,30,40,100,1.25,ApiEquivalentEstimate,local-db", lines[2]);
        Assert.DoesNotContain("prompt", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
