using System.Globalization;
using Microsoft.Data.Sqlite;
using ModelMeter.Core.Models;

namespace ModelMeter.Core.Storage;

/// <summary>
/// Local SQLite store for normalized usage metadata. No transcript or source content schema exists.
/// </summary>
public sealed class UsageDatabase : IAsyncDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;

    public UsageDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        _databasePath = databasePath;
    }

    public static string GetDefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModelMeter", "usage.db");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null) return;
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        await _connection.OpenAsync(cancellationToken);

        await using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS usage_records (
                id TEXT PRIMARY KEY NOT NULL,
                provider_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                session_id TEXT NULL,
                project_name TEXT NULL,
                timestamp_utc TEXT NOT NULL,
                input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                cache_read_tokens INTEGER NOT NULL,
                cache_write_tokens INTEGER NOT NULL,
                estimated_api_cost TEXT NULL,
                cost_kind INTEGER NOT NULL,
                source TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_usage_timestamp ON usage_records(timestamp_utc);
            CREATE INDEX IF NOT EXISTS idx_usage_provider_timestamp ON usage_records(provider_id, timestamp_utc);
            CREATE INDEX IF NOT EXISTS idx_usage_session ON usage_records(session_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> UpsertAsync(IEnumerable<UsageRecord> records, CancellationToken cancellationToken = default)
    {
        var connection = GetConnection();
        var inserted = 0;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO usage_records
            (id, provider_id, model_id, session_id, project_name, timestamp_utc,
             input_tokens, output_tokens, cache_read_tokens, cache_write_tokens,
             estimated_api_cost, cost_kind, source)
            VALUES ($id, $provider, $model, $session, $project, $timestamp,
                    $input, $output, $cacheRead, $cacheWrite, $cost, $costKind, $source)
            ON CONFLICT(id) DO UPDATE SET
                provider_id = excluded.provider_id,
                model_id = excluded.model_id,
                session_id = excluded.session_id,
                project_name = excluded.project_name,
                timestamp_utc = excluded.timestamp_utc,
                input_tokens = excluded.input_tokens,
                output_tokens = excluded.output_tokens,
                cache_read_tokens = excluded.cache_read_tokens,
                cache_write_tokens = excluded.cache_write_tokens,
                estimated_api_cost = excluded.estimated_api_cost,
                cost_kind = excluded.cost_kind,
                source = excluded.source
            WHERE provider_id IS NOT excluded.provider_id
               OR model_id IS NOT excluded.model_id
               OR session_id IS NOT excluded.session_id
               OR project_name IS NOT excluded.project_name
               OR timestamp_utc IS NOT excluded.timestamp_utc
               OR input_tokens IS NOT excluded.input_tokens
               OR output_tokens IS NOT excluded.output_tokens
               OR cache_read_tokens IS NOT excluded.cache_read_tokens
               OR cache_write_tokens IS NOT excluded.cache_write_tokens
               OR estimated_api_cost IS NOT excluded.estimated_api_cost
               OR cost_kind IS NOT excluded.cost_kind
               OR source IS NOT excluded.source;
            """;

        var parameters = new[]
        {
            command.Parameters.Add("$id", SqliteType.Text),
            command.Parameters.Add("$provider", SqliteType.Text),
            command.Parameters.Add("$model", SqliteType.Text),
            command.Parameters.Add("$session", SqliteType.Text),
            command.Parameters.Add("$project", SqliteType.Text),
            command.Parameters.Add("$timestamp", SqliteType.Text),
            command.Parameters.Add("$input", SqliteType.Integer),
            command.Parameters.Add("$output", SqliteType.Integer),
            command.Parameters.Add("$cacheRead", SqliteType.Integer),
            command.Parameters.Add("$cacheWrite", SqliteType.Integer),
            command.Parameters.Add("$cost", SqliteType.Text),
            command.Parameters.Add("$costKind", SqliteType.Integer),
            command.Parameters.Add("$source", SqliteType.Text)
        };

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parameters[0].Value = record.Id;
            parameters[1].Value = record.ProviderId;
            parameters[2].Value = record.ModelId;
            parameters[3].Value = (object?)record.SessionId ?? DBNull.Value;
            parameters[4].Value = (object?)record.ProjectName ?? DBNull.Value;
            parameters[5].Value = record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            parameters[6].Value = record.InputTokens;
            parameters[7].Value = record.OutputTokens;
            parameters[8].Value = record.CacheReadTokens;
            parameters[9].Value = record.CacheWriteTokens;
            parameters[10].Value = record.EstimatedApiCost?.ToString(CultureInfo.InvariantCulture) is { } cost ? cost : DBNull.Value;
            parameters[11].Value = (int)record.CostKind;
            parameters[12].Value = record.Source;
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<IReadOnlyList<UsageRecord>> GetUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var connection = GetConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, provider_id, model_id, session_id, project_name, timestamp_utc,
                   input_tokens, output_tokens, cache_read_tokens, cache_write_tokens,
                   estimated_api_cost, cost_kind, source
            FROM usage_records
            WHERE timestamp_utc >= $from AND timestamp_utc <= $to
            ORDER BY timestamp_utc;
            """;
        command.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var records = new List<UsageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal? cost = null;
            if (!reader.IsDBNull(10) && decimal.TryParse(reader.GetString(10), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                cost = parsed;

            records.Add(new UsageRecord
            {
                Id = reader.GetString(0),
                ProviderId = reader.GetString(1),
                ModelId = reader.GetString(2),
                SessionId = reader.IsDBNull(3) ? null : reader.GetString(3),
                ProjectName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Timestamp = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                InputTokens = reader.GetInt64(6),
                OutputTokens = reader.GetInt64(7),
                CacheReadTokens = reader.GetInt64(8),
                CacheWriteTokens = reader.GetInt64(9),
                EstimatedApiCost = cost,
                CostKind = (CostDataKind)reader.GetInt32(11),
                Source = reader.GetString(12)
            });
        }

        return records;
    }

    private SqliteConnection GetConnection() =>
        _connection ?? throw new InvalidOperationException("InitializeAsync must be called before using the database.");

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
