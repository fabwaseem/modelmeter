using System.Text.Json;
using ModelMeter.Core.Interfaces;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Providers;

/// <summary>
/// Reads token_count events from Codex CLI rollout JSONL files.
/// Only normalized usage metadata is returned; transcript and tool payloads are discarded.
/// </summary>
public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly string _codexHome;

    public CodexUsageProvider(string? codexHome = null)
    {
        _codexHome = codexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(_codexHome))
            _codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    public string ProviderId => "openai-codex";
    public string DisplayName => "OpenAI Codex";

    public Task<ProviderSnapshot> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detected = Directory.Exists(Path.Combine(_codexHome, "sessions")) ||
                       Directory.Exists(Path.Combine(_codexHome, "archived_sessions"));
        return Task.FromResult(new ProviderSnapshot
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            State = detected ? ProviderState.Detected : ProviderState.NotDetected,
            LastUpdatedAt = detected ? DateTimeOffset.UtcNow : null,
            StatusMessage = detected ? "Local Codex session history found" : "Codex session folder not found"
        });
    }

    public async Task<IReadOnlyList<UsageRecord>> GetUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var records = new List<UsageRecord>();
        var roots = new[] { Path.Combine(_codexHome, "sessions"), Path.Combine(_codexHome, "archived_sessions") };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rootPath in roots)
        {
            if (!Directory.Exists(rootPath)) continue;
            foreach (var file in Directory.EnumerateFiles(rootPath, "rollout-*.jsonl",
                         new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ParseRolloutAsync(file, from, to, records, seen, cancellationToken);
            }
        }

        return records;
    }

    private async Task ParseRolloutAsync(string file, DateTimeOffset from, DateTimeOffset to,
        List<UsageRecord> records, HashSet<string> seen, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);

        string? sessionId = null;
        string? projectName = null;
        string model = "unknown";
        long previousInput = 0, previousCached = 0, previousOutput = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length is 0 or > 4 * 1024 * 1024) continue;
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (document)
            {
                var root = document.RootElement;
                var type = ReadString(root, "type");
                if (type == "session_meta" && root.TryGetProperty("payload", out var meta))
                {
                    sessionId = ReadString(meta, "id") ?? ReadString(meta, "session_id")
                        ?? Path.GetFileNameWithoutExtension(file);
                    projectName = ProjectLabel(ReadString(meta, "cwd"));
                    continue;
                }

                if (type == "turn_context" && root.TryGetProperty("payload", out var turn))
                {
                    model = ReadString(turn, "model") ?? model;
                    continue;
                }

                if (type != "event_msg" || !root.TryGetProperty("payload", out var payload) ||
                    ReadString(payload, "type") != "token_count" ||
                    !payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
                    continue;

                var timestamp = ReadTimestamp(root, "timestamp");
                if (timestamp is null || timestamp < from || timestamp > to) continue;

                var last = info.TryGetProperty("last_token_usage", out var lastUsage) &&
                           lastUsage.ValueKind == JsonValueKind.Object;
                var usage = last ? lastUsage : info.TryGetProperty("total_token_usage", out var totalUsage)
                    ? totalUsage : default;
                if (usage.ValueKind != JsonValueKind.Object) continue;

                var cumulativeInput = ReadLong(usage, "input_tokens");
                var cumulativeCached = ReadLong(usage, "cached_input_tokens");
                var cumulativeOutput = ReadLong(usage, "output_tokens");

                long input, cached, output;
                if (last)
                {
                    input = Math.Max(0, cumulativeInput - cumulativeCached);
                    cached = cumulativeCached;
                    output = cumulativeOutput;
                }
                else
                {
                    input = Math.Max(0, cumulativeInput - previousInput -
                        Math.Max(0, cumulativeCached - previousCached));
                    cached = Math.Max(0, cumulativeCached - previousCached);
                    output = Math.Max(0, cumulativeOutput - previousOutput);
                }

                if (!last)
                {
                    previousInput = Math.Max(previousInput, cumulativeInput);
                    previousCached = Math.Max(previousCached, cumulativeCached);
                    previousOutput = Math.Max(previousOutput, cumulativeOutput);
                }
                if (input == 0 && cached == 0 && output == 0) continue;

                sessionId ??= Path.GetFileNameWithoutExtension(file);
                var id = $"{sessionId}:{timestamp.Value:O}:{input}:{cached}:{output}";
                if (!seen.Add(id)) continue;

                records.Add(new UsageRecord
                {
                    Id = id,
                    ProviderId = ProviderId,
                    ModelId = model,
                    SessionId = sessionId,
                    ProjectName = projectName,
                    Timestamp = timestamp.Value,
                    InputTokens = input,
                    OutputTokens = output,
                    CacheReadTokens = cached,
                    CostKind = CostDataKind.Unknown,
                    Source = "codex-rollout-token-count"
                });
            }
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) && result > 0
            ? result : 0;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string property) =>
        DateTimeOffset.TryParse(ReadString(element, property), out var result) ? result : null;

    private static string? ProjectLabel(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        var label = Path.GetFileName(Path.TrimEndingDirectorySeparator(cwd));
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }
}
