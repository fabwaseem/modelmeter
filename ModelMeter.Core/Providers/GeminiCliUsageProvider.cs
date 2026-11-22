using System.Globalization;
using System.Text.Json;
using ModelMeter.Core.Interfaces;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Providers;

/// <summary>
/// Imports Gemini CLI API response token counts from an explicitly configured local
/// OpenTelemetry log file. It never reads or stores prompt/response attributes.
/// </summary>
public sealed class GeminiCliUsageProvider : IUsageProvider
{
    private readonly string _telemetryFile;

    public GeminiCliUsageProvider(string? telemetryFile = null)
    {
        _telemetryFile = telemetryFile ?? ResolveTelemetryFile();
    }

    public string ProviderId => "gemini-cli";
    public string DisplayName => "Gemini CLI";

    public Task<ProviderSnapshot> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detected = File.Exists(_telemetryFile);
        return Task.FromResult(new ProviderSnapshot
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            State = detected ? ProviderState.Detected : ProviderState.NotDetected,
            LastUpdatedAt = detected ? File.GetLastWriteTimeUtc(_telemetryFile) : null,
            StatusMessage = detected
                ? "Opt-in local Gemini telemetry found"
                : "Local telemetry not found; enable Gemini CLI telemetry to import usage"
        });
    }

    public async Task<IReadOnlyList<UsageRecord>> GetUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var records = new List<UsageRecord>();
        if (!File.Exists(_telemetryFile)) return records;

        await using var stream = new FileStream(_telemetryFile, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length is 0 or > 2 * 1024 * 1024) continue;

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (document)
            {
                var root = document.RootElement;
                if (!TryReadApiResponse(root, out var timestamp, out var attributes)) continue;
                if (timestamp < from || timestamp > to) continue;

                var model = ReadAttribute(attributes, "model") ??
                            ReadAttribute(attributes, "model_name") ?? "unknown";
                var promptId = ReadAttribute(attributes, "prompt_id");
                var sessionId = ReadCommonAttribute(root, "session.id") ??
                                ReadCommonAttribute(root, "sessionId");
                var input = ReadAttributeLong(attributes, "input_token_count");
                var output = ReadAttributeLong(attributes, "output_token_count");
                var cached = ReadAttributeLong(attributes, "cached_content_token_count");
                var thought = ReadAttributeLong(attributes, "thoughts_token_count");

                if (input == 0 && output == 0 && cached == 0 && thought == 0) continue;

                // Prompt IDs are telemetry correlation IDs, not prompt contents.
                var eventId = promptId ?? $"{timestamp:O}:{model}:{input}:{output}:{cached}:{thought}";
                var id = $"{sessionId ?? "session"}:{eventId}";
                if (!seen.Add(id)) continue;

                records.Add(new UsageRecord
                {
                    Id = id,
                    ProviderId = ProviderId,
                    ModelId = model,
                    SessionId = sessionId,
                    Timestamp = timestamp,
                    InputTokens = Math.Max(0, input - cached),
                    OutputTokens = output + thought,
                    CacheReadTokens = cached,
                    CostKind = CostDataKind.Unknown,
                    Source = "gemini-cli-local-telemetry"
                });
            }
        }

        return records;
    }

    private static bool TryReadApiResponse(JsonElement root, out DateTimeOffset timestamp,
        out JsonElement attributes)
    {
        timestamp = default;
        attributes = default;

        // Gemini's local OpenTelemetry file exporter writes one structured log record per line.
        // Support both direct log records and the standard OTLP resourceLogs envelope.
        if (ReadAnyString(root, "eventName") == "gemini_cli.api_response" ||
            ReadAnyString(root, "name") == "gemini_cli.api_response" ||
            ReadAnyString(root, "body") == "gemini_cli.api_response")
        {
            attributes = FindAttributes(root);
            timestamp = ReadTimestamp(root);
            return timestamp != default && attributes.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }

        if (root.TryGetProperty("resourceLogs", out var resourceLogs) &&
            resourceLogs.ValueKind == JsonValueKind.Array)
        {
            foreach (var resource in resourceLogs.EnumerateArray())
            {
                if (!resource.TryGetProperty("scopeLogs", out var scopes) ||
                    scopes.ValueKind != JsonValueKind.Array) continue;
                foreach (var scope in scopes.EnumerateArray())
                {
                    if (!scope.TryGetProperty("logRecords", out var logs) ||
                        logs.ValueKind != JsonValueKind.Array) continue;
                    foreach (var log in logs.EnumerateArray())
                    {
                        var body = ReadAnyString(log, "body");
                        if (body != "gemini_cli.api_response") continue;
                        attributes = FindAttributes(log);
                        timestamp = ReadTimestamp(log);
                        return timestamp != default && attributes.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
                    }
                }
            }
        }

        return false;
    }

    private static JsonElement FindAttributes(JsonElement element) =>
        element.TryGetProperty("attributes", out var attributes) ? attributes : default;

    private static string? ReadAttribute(JsonElement attributes, string name)
    {
        if (attributes.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return attributes.TryGetProperty(name, out var value) ? ReadScalar(value) : null;

        if (attributes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in attributes.EnumerateArray())
            {
                if (ReadString(item, "key") != name || !item.TryGetProperty("value", out var value))
                    continue;
                return ReadScalar(value);
            }
        }
        return null;
    }

    private static long ReadAttributeLong(JsonElement attributes, string name)
    {
        var value = ReadAttribute(attributes, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed) : 0;
    }

    private static string? ReadCommonAttribute(JsonElement root, string name)
    {
        var attributes = FindAttributes(root);
        return ReadAttribute(attributes, name);
    }

    private static string? ReadScalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            return value.ToString();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "stringValue", "intValue", "doubleValue", "boolValue" })
                if (value.TryGetProperty(key, out var scalar)) return scalar.ToString();
        }
        return null;
    }

    private static string? ReadOtlpScalar(JsonElement value) => ReadScalar(value);

    private static string? ReadAnyString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return ReadScalar(value);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTimeOffset ReadTimestamp(JsonElement element)
    {
        foreach (var key in new[] { "timestamp", "time", "timeUnixNano" })
        {
            if (!element.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;
            if ((value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) ||
                (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)))
            {
                try
                {
                    return key == "timeUnixNano"
                        ? DateTimeOffset.FromUnixTimeMilliseconds(number / 1_000_000)
                        : DateTimeOffset.FromUnixTimeSeconds(number);
                }
                catch (ArgumentOutOfRangeException) { return default; }
            }
        }
        return default;
    }

    private static string ResolveTelemetryFile()
    {
        var configured = Environment.GetEnvironmentVariable("GEMINI_TELEMETRY_OUTFILE");
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
            return configured;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var settingsPath = Path.Combine(home, ".gemini", "settings.json");
        try
        {
            if (File.Exists(settingsPath))
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (settings.RootElement.TryGetProperty("telemetry", out var telemetry) &&
                    telemetry.TryGetProperty("outfile", out var outfile) &&
                    outfile.ValueKind == JsonValueKind.String)
                {
                    var configuredPath = outfile.GetString();
                    if (!string.IsNullOrWhiteSpace(configuredPath) &&
                        Path.IsPathFullyQualified(configuredPath)) return configuredPath;
                }
            }
        }
        catch (JsonException) { }

        return Path.Combine(home, ".gemini", "telemetry.log");
    }
}
