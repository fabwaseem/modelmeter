using System.Text.Json;
using ModelMeter.Core.Interfaces;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Providers;

/// <summary>
/// Reads token usage metadata from Claude Code's local project JSONL files.
/// Transcript content is never copied into a ModelMeter record.
/// </summary>
public sealed class ClaudeCodeUsageProvider : IUsageProvider
{
    private readonly string _projectsRoot;

    public ClaudeCodeUsageProvider(string? projectsRoot = null)
    {
        _projectsRoot = projectsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    public string ProviderId => "claude-code";
    public string DisplayName => "Claude Code";

    public Task<ProviderSnapshot> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detected = Directory.Exists(_projectsRoot);
        return Task.FromResult(new ProviderSnapshot
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            State = detected ? ProviderState.Detected : ProviderState.NotDetected,
            LastUpdatedAt = detected ? DateTimeOffset.UtcNow : null,
            StatusMessage = detected ? "Local session history found" : "Claude Code session folder not found"
        });
    }

    public async Task<IReadOnlyList<UsageRecord>> GetUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var records = new List<UsageRecord>();
        if (!Directory.Exists(_projectsRoot)) return records;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_projectsRoot, "*.jsonl",
                     new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
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
                    if (!HasString(root, "type", "assistant")) continue;
                    if (!root.TryGetProperty("message", out var message) ||
                        message.ValueKind != JsonValueKind.Object ||
                        !message.TryGetProperty("usage", out var usage) ||
                        usage.ValueKind != JsonValueKind.Object) continue;

                    var timestamp = ReadTimestamp(root, "timestamp");
                    if (timestamp is null || timestamp < from || timestamp > to) continue;

                    var sessionId = ReadString(root, "sessionId") ?? Path.GetFileNameWithoutExtension(file);
                    var messageId = ReadString(message, "id") ?? ReadString(root, "uuid");
                    if (string.IsNullOrWhiteSpace(messageId)) continue;
                    var id = $"{sessionId}:{messageId}";
                    if (!seen.Add(id)) continue;

                    var model = ReadString(message, "model") ?? "unknown";
                    var project = ReadString(root, "cwd");
                    records.Add(new UsageRecord
                    {
                        Id = id,
                        ProviderId = ProviderId,
                        ModelId = model,
                        SessionId = sessionId,
                        ProjectName = ProjectLabel(project),
                        Timestamp = timestamp.Value,
                        InputTokens = ReadLong(usage, "input_tokens"),
                        OutputTokens = ReadLong(usage, "output_tokens"),
                        CacheWriteTokens = ReadLong(usage, "cache_creation_input_tokens"),
                        CacheReadTokens = ReadLong(usage, "cache_read_input_tokens"),
                        CostKind = CostDataKind.Unknown,
                        Source = "claude-code-local-jsonl"
                    });
                }
            }
        }

        return records;
    }

    private static bool HasString(JsonElement element, string property, string expected) =>
        ReadString(element, property)?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? ReadString(JsonElement element, string property) =>
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
