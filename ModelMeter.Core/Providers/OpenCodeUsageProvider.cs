using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelMeter.Core.Interfaces;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Providers;

/// <summary>
/// Reads assistant usage metadata from OpenCode's local SQLite store.
/// Opens the source database read-only and never reads message parts or transcript text.
/// </summary>
public sealed class OpenCodeUsageProvider : IUsageProvider
{
    private readonly string _databasePath;

    public OpenCodeUsageProvider(string? databasePath = null)
    {
        _databasePath = databasePath ?? ResolveDatabasePath();
    }

    public string ProviderId => "opencode";
    public string DisplayName => "OpenCode";

    public Task<ProviderSnapshot> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detected = File.Exists(_databasePath);
        return Task.FromResult(new ProviderSnapshot
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            State = detected ? ProviderState.Detected : ProviderState.NotDetected,
            LastUpdatedAt = detected ? File.GetLastWriteTimeUtc(_databasePath) : null,
            StatusMessage = detected
                ? "Local OpenCode database found"
                : "OpenCode database not found"
        });
    }

    public async Task<IReadOnlyList<UsageRecord>> GetUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var records = new List<UsageRecord>();
        if (!File.Exists(_databasePath)) return records;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(connection, "message", cancellationToken))
            return records;

        var columns = await GetColumnsAsync(connection, "message", cancellationToken);
        if (!columns.Contains("id") || !columns.Contains("session_id") ||
            !columns.Contains("time_created") || !columns.Contains("data"))
            return records;

        var hasSession = await TableExistsAsync(connection, "session", cancellationToken);
        var sessionColumns = hasSession
            ? await GetColumnsAsync(connection, "session", cancellationToken)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasSessionId = sessionColumns.Contains("id");
        var directoryExpression = hasSessionId && sessionColumns.Contains("directory") ? "s.directory" : "NULL";
        var join = hasSessionId ? "LEFT JOIN session s ON s.id = m.session_id" : "";
        var sql = $"""
            SELECT m.id, m.session_id, m.time_created, m.data, {directoryExpression}
            FROM message m
            {join}
            ORDER BY m.time_created
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var messageId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var sessionId = reader.IsDBNull(1) ? null : reader.GetString(1);
            var fallbackMilliseconds = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            var json = reader.IsDBNull(3) ? null : reader.GetString(3);
            var directory = reader.IsDBNull(4) ? null : reader.GetString(4);
            if (string.IsNullOrWhiteSpace(json)) continue;

            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException) { continue; }

            using (document)
            {
                var root = document.RootElement;
                if (ReadString(root, "role") != "assistant") continue;
                if (!root.TryGetProperty("tokens", out var tokens) ||
                    tokens.ValueKind != JsonValueKind.Object) continue;

                var timestamp = ReadMessageTimestamp(root, fallbackMilliseconds);
                if (timestamp < from || timestamp > to) continue;

                var model = ReadString(root, "modelID") ??
                            ReadNestedString(root, "model", "modelID") ?? "unknown";
                var input = ReadLong(tokens, "input");
                var output = ReadLong(tokens, "output");
                var reasoning = ReadLong(tokens, "reasoning");
                var cacheRead = ReadNestedLong(tokens, "cache", "read");
                var cacheWrite = ReadNestedLong(tokens, "cache", "write");
                if (input == 0 && output == 0 && reasoning == 0 && cacheRead == 0 && cacheWrite == 0)
                    continue;

                var id = $"{sessionId ?? "session"}:{messageId ?? timestamp.ToString("O", CultureInfo.InvariantCulture)}";
                if (!seen.Add(id)) continue;

                records.Add(new UsageRecord
                {
                    Id = id,
                    ProviderId = ProviderId,
                    ModelId = model,
                    SessionId = sessionId,
                    ProjectName = ProjectLabel(directory),
                    Timestamp = timestamp,
                    InputTokens = input,
                    OutputTokens = output + reasoning,
                    CacheReadTokens = cacheRead,
                    CacheWriteTokens = cacheWrite,
                    EstimatedApiCost = ReadDecimal(root, "cost"),
                    CostKind = root.TryGetProperty("cost", out _) ? CostDataKind.ApiEquivalentEstimate : CostDataKind.Unknown,
                    Source = "opencode-local-sqlite"
                });
            }
        }

        return records;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static DateTimeOffset ReadMessageTimestamp(JsonElement root, long fallbackMilliseconds)
    {
        if (root.TryGetProperty("time", out var time) &&
            time.ValueKind == JsonValueKind.Object &&
            time.TryGetProperty("created", out var created) &&
            created.TryGetInt64(out var milliseconds))
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException) { }
        }

        try { return DateTimeOffset.FromUnixTimeMilliseconds(fallbackMilliseconds); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MinValue; }
    }

    private static string? ReadNestedString(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? ReadString(nested, property) : null;

    private static long ReadNestedLong(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadLong(nested, property) : 0;

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result)
            ? Math.Max(0, result) : 0;

    private static decimal? ReadDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDecimal(out var result)
            ? Math.Max(0, result) : null;

    private static string? ProjectLabel(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var label = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }


    private static string ResolveDatabasePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("OPENCODE_DB");
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;

        var dataDirectory = Environment.GetEnvironmentVariable("OPENCODE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            return Path.Combine(dataDirectory, "opencode.db");

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            return Path.Combine(xdgDataHome, "opencode", "opencode.db");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".local", "share", "opencode", "opencode.db"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "opencode.db"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "opencode.db")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
