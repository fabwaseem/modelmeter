using Microsoft.Data.Sqlite;
using System.Text;
using ModelMeter.Core.Providers;
using Xunit;

namespace ModelMeter.Core.Tests;

public sealed class LocalProviderTests
{
    [Fact]
    public async Task ClaudeCode_ImportsOnlyAssistantUsageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "project-a");
        Directory.CreateDirectory(project);
        try
        {
            var jsonl = """
                {"type":"user","sessionId":"s1","timestamp":"2026-11-22T10:00:00Z","message":{"content":"private prompt"}}
                {"type":"assistant","sessionId":"s1","uuid":"u1","timestamp":"2026-11-22T10:01:00Z","cwd":"C:\\work\\project-a","message":{"id":"m1","model":"claude-sonnet","usage":{"input_tokens":120,"output_tokens":30,"cache_creation_input_tokens":40,"cache_read_input_tokens":50},"content":[{"type":"text","text":"private response"}]}}
                """;
            await File.WriteAllTextAsync(Path.Combine(project, "session.jsonl"), jsonl, Encoding.UTF8);

            var provider = new ClaudeCodeUsageProvider(root);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            var record = Assert.Single(records);
            Assert.Equal("claude-sonnet", record.ModelId);
            Assert.Equal(120, record.InputTokens);
            Assert.Equal(30, record.OutputTokens);
            Assert.Equal(40, record.CacheWriteTokens);
            Assert.Equal(50, record.CacheReadTokens);
            Assert.Equal("project-a", record.ProjectName);
            Assert.DoesNotContain(records, x => x.Source.Contains("private", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Codex_UsesPerTurnTokenUsageAndIgnoresTranscriptEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions", "2026", "11", "22");
        Directory.CreateDirectory(sessions);
        try
        {
            var jsonl = """
                {"type":"session_meta","payload":{"id":"codex-session","cwd":"C:\\work\\project-b","originator":"codex-tui"}}
                {"type":"turn_context","payload":{"model":"gpt-5-codex"}}
                {"type":"response_item","payload":{"type":"message","content":"must not be stored"}}
                {"timestamp":"2026-11-22T10:02:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":500,"cached_input_tokens":200,"output_tokens":90},"last_token_usage":{"input_tokens":100,"cached_input_tokens":40,"output_tokens":20}}}}
                """;
            await File.WriteAllTextAsync(Path.Combine(sessions, "rollout-test.jsonl"), jsonl, Encoding.UTF8);

            var provider = new CodexUsageProvider(root);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            var record = Assert.Single(records);
            Assert.Equal("gpt-5-codex", record.ModelId);
            Assert.Equal("codex-session", record.SessionId);
            Assert.Equal(60, record.InputTokens);
            Assert.Equal(40, record.CacheReadTokens);
            Assert.Equal(20, record.OutputTokens);
            Assert.Equal("project-b", record.ProjectName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Codex_CalculatesDeltasFromCumulativeSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        try
        {
            var jsonl = """
                {"type":"session_meta","payload":{"id":"s-delta"}}
                {"timestamp":"2026-11-22T10:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":10}}}}
                {"timestamp":"2026-11-22T10:02:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":180,"cached_input_tokens":50,"output_tokens":35}}}}
                """;
            await File.WriteAllTextAsync(Path.Combine(sessions, "rollout-delta.jsonl"), jsonl, Encoding.UTF8);

            var provider = new CodexUsageProvider(root);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            Assert.Equal(2, records.Count);
            Assert.Equal(50, records[1].InputTokens);
            Assert.Equal(30, records[1].CacheReadTokens);
            Assert.Equal(25, records[1].OutputTokens);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Codex_MaintainsCumulativeBaselineAfterPerTurnUsage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        try
        {
            var jsonl = """
                {"type":"session_meta","payload":{"id":"s-mixed"}}
                {"timestamp":"2026-11-22T10:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":500,"cached_input_tokens":200,"output_tokens":90},"last_token_usage":{"input_tokens":100,"cached_input_tokens":40,"output_tokens":20}}}}
                {"timestamp":"2026-11-22T10:02:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":650,"cached_input_tokens":260,"output_tokens":120}}}}
                """;
            await File.WriteAllTextAsync(Path.Combine(sessions, "rollout-mixed.jsonl"), jsonl, Encoding.UTF8);

            var provider = new CodexUsageProvider(root);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            Assert.Equal(2, records.Count);
            Assert.Equal(90, records[1].InputTokens);
            Assert.Equal(60, records[1].CacheReadTokens);
            Assert.Equal(30, records[1].OutputTokens);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Codex_UsesPreWindowCumulativeSnapshotsAsBaselineWithoutImportingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        try
        {
            var jsonl = """
                {"type":"session_meta","payload":{"id":"s-window"}}
                {"timestamp":"2026-11-21T23:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":300,"output_tokens":100}}}}
                {"timestamp":"2026-11-22T10:02:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1150,"cached_input_tokens":350,"output_tokens":140}}}}
                """;
            await File.WriteAllTextAsync(Path.Combine(sessions, "rollout-window.jsonl"), jsonl, Encoding.UTF8);

            var provider = new CodexUsageProvider(root);
            var records = await provider.GetUsageAsync(
                DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
                CancellationToken.None);

            var record = Assert.Single(records);
            Assert.Equal(100, record.InputTokens);
            Assert.Equal(50, record.CacheReadTokens);
            Assert.Equal(40, record.OutputTokens);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GeminiCli_ImportsOnlyApiResponseTokenMetadataFromLocalTelemetry()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var telemetry = Path.Combine(root, "telemetry.log");
            var jsonl = """
                {"timestamp":"2026-11-22T11:00:00Z","eventName":"gemini_cli.user_prompt","attributes":{"prompt":"private prompt"}}
                {"timestamp":"2026-11-22T11:01:00Z","body":{"stringValue":"gemini_cli.api_response"},"attributes":{"model":"gemini-2.5-pro","prompt_id":"p-1","input_token_count":120,"output_token_count":30,"cached_content_token_count":20,"thoughts_token_count":5,"response_text":"private response"}}
                {"resourceLogs":[{"scopeLogs":[{"logRecords":[{"timeUnixNano":"1795345320000000000","body":{"stringValue":"gemini_cli.api_response"},"attributes":[{"key":"model","value":{"stringValue":"gemini-2.5-flash"}},{"key":"prompt_id","value":{"stringValue":"p-2"}},{"key":"input_token_count","value":{"intValue":"90"}},{"key":"output_token_count","value":{"intValue":"15"}},{"key":"cached_content_token_count","value":{"intValue":"10"}}]}]}]}]}
                """;
            await File.WriteAllTextAsync(telemetry, jsonl, Encoding.UTF8);

            var provider = new GeminiCliUsageProvider(telemetry);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            Assert.Equal(2, records.Count);
            Assert.Equal("gemini-2.5-pro", records[0].ModelId);
            Assert.Equal(100, records[0].InputTokens);
            Assert.Equal(20, records[0].CacheReadTokens);
            Assert.Equal(35, records[0].OutputTokens);
            Assert.Equal("gemini-2.5-flash", records[1].ModelId);
            Assert.Equal(80, records[1].InputTokens);
            Assert.Equal(10, records[1].CacheReadTokens);
            Assert.Equal(15, records[1].OutputTokens);
            Assert.All(records, record => Assert.DoesNotContain("private", record.Source));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OpenCode_ImportsAssistantUsageFromReadOnlyDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "opencode.db");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE session (id TEXT PRIMARY KEY, directory TEXT);
                    CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT, time_created INTEGER, data TEXT);
                    INSERT INTO session VALUES ('ses-1', 'C:\\work\\project-c');
                    INSERT INTO message VALUES ('msg-user', 'ses-1', 1795345200000,
                      '{"role":"user","content":"private prompt"}');
                    INSERT INTO message VALUES ('msg-assistant', 'ses-1', 1795345260000,
                      '{"role":"assistant","modelID":"claude-sonnet-4","providerID":"anthropic","cost":0.012,"time":{"created":1795345260000},"tokens":{"input":100,"output":30,"reasoning":5,"cache":{"read":20,"write":10}}}');
                    INSERT INTO message VALUES ('msg-old', 'ses-1', 1795258800000,
                      '{"role":"assistant","modelID":"claude-opus-4","time":{"created":1795258800000},"tokens":{"input":900,"output":400,"reasoning":0,"cache":{"read":0,"write":0}}}');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var provider = new OpenCodeUsageProvider(databasePath);
            var records = await provider.GetUsageAsync(DateTimeOffset.Parse("2026-11-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-11-23T00:00:00Z"), CancellationToken.None);

            var record = Assert.Single(records);
            Assert.Equal("claude-sonnet-4", record.ModelId);
            Assert.Equal("ses-1", record.SessionId);
            Assert.Equal("project-c", record.ProjectName);
            Assert.Equal(100, record.InputTokens);
            Assert.Equal(35, record.OutputTokens);
            Assert.Equal(20, record.CacheReadTokens);
            Assert.Equal(10, record.CacheWriteTokens);
            Assert.Equal(0.012m, record.EstimatedApiCost);
            Assert.Equal(ModelMeter.Core.Models.CostDataKind.ApiEquivalentEstimate, record.CostKind);
            Assert.DoesNotContain(records, x => x.Source.Contains("private", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, true); }
    }

}
