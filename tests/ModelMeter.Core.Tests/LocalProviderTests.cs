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

}
