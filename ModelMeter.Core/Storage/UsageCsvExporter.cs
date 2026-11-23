using System.Globalization;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Storage;

/// <summary>
/// Writes privacy-safe normalized usage metadata as RFC 4180-compatible CSV.
/// Transcript content and source file paths are intentionally not exported.
/// </summary>
public static class UsageCsvExporter
{
    private static readonly string[] Headers =
    [
        "timestamp_utc", "provider_id", "model_id", "session_id", "project_name",
        "input_tokens", "output_tokens", "cache_read_tokens", "cache_write_tokens",
        "total_tokens", "estimated_api_cost", "cost_kind", "source"
    ];

    public static async Task WriteAsync(
        IEnumerable<UsageRecord> records,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(writer);

        await writer.WriteLineAsync(string.Join(",", Headers));
        foreach (var record in records.OrderBy(x => x.Timestamp).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var total = checked(record.InputTokens + record.OutputTokens +
                                record.CacheReadTokens + record.CacheWriteTokens);
            var fields = new[]
            {
                record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                record.ProviderId,
                record.ModelId,
                record.SessionId ?? string.Empty,
                record.ProjectName ?? string.Empty,
                record.InputTokens.ToString(CultureInfo.InvariantCulture),
                record.OutputTokens.ToString(CultureInfo.InvariantCulture),
                record.CacheReadTokens.ToString(CultureInfo.InvariantCulture),
                record.CacheWriteTokens.ToString(CultureInfo.InvariantCulture),
                total.ToString(CultureInfo.InvariantCulture),
                record.EstimatedApiCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                record.CostKind.ToString(),
                record.Source
            };

            await writer.WriteLineAsync(string.Join(",", fields.Select(Escape)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
