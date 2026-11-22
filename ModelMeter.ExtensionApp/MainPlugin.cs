using System.Text.Json;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidBar.SDK;
using ModelMeter.Core.Aggregation;
using ModelMeter.Core.Interfaces;
using ModelMeter.Core.Models;
using ModelMeter.Core.Providers;
using ModelMeter.Core.Storage;

namespace ModelMeter.ExtensionApp;

public sealed class MainPlugin : WidgetPluginBase, IConfigurableWidgetPlugin, IWidgetFlyoutLifecycle
{
    private WidgetSettings _settings = new();
    private IReadOnlyList<UsageRecord> _usageRecords = Array.Empty<UsageRecord>();
    private IReadOnlyList<ProviderSnapshot> _providerSnapshots = Array.Empty<ProviderSnapshot>();
    public override string Id => "com.fabwaseem.modelmeter";
    public override string Name => "ModelMeter";
    public override int PreviewLogicalWidth => 190;
    public override int FlyoutWidth => 760;
    public override int FlyoutHeight => 700;

    internal sealed class WidgetSettings
    {
        public bool ShowTokens { get; set; } = true;
        public bool ShowProviderCount { get; set; } = true;
        public bool ShowCost { get; set; } = true;
        public static WidgetSettings Parse(string? json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<WidgetSettings>(json) ?? new(); }
            catch { return new(); }
        }
    }

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = WidgetSettings.Parse(context.SettingsJson);
        await base.InitializeAsync(context);

        var providers = new IUsageProvider[]
        {
            new ClaudeCodeUsageProvider(),
            new CodexUsageProvider(),
            new GeminiCliUsageProvider()
        };
        var from = DateTimeOffset.Now.AddDays(-30);
        var to = DateTimeOffset.Now;
        var records = new List<UsageRecord>();
        var snapshots = new List<ProviderSnapshot>();

        foreach (var provider in providers)
        {
            ProviderSnapshot? snapshot = null;
            try
            {
                snapshot = await provider.DetectAsync(CancellationToken.None);
                snapshots.Add(snapshot);
                if (snapshot.State == ProviderState.NotDetected) continue;
                records.AddRange(await provider.GetUsageAsync(from, to, CancellationToken.None));
                snapshots[^1] = snapshot with
                {
                    State = ProviderState.Connected,
                    LastUpdatedAt = DateTimeOffset.Now,
                    StatusMessage = $"Imported {records.Count(x => x.ProviderId == provider.ProviderId):N0} usage events"
                };
            }
            catch (Exception ex)
            {
                var error = new ProviderSnapshot
                {
                    ProviderId = provider.ProviderId,
                    DisplayName = provider.DisplayName,
                    State = ProviderState.Error,
                    LastUpdatedAt = DateTimeOffset.Now,
                    StatusMessage = ex.GetType().Name
                };
                if (snapshot is null) snapshots.Add(error);
                else snapshots[^1] = error;
            }
        }

        try
        {
            await using var database = new UsageDatabase(UsageDatabase.GetDefaultDatabasePath());
            await database.InitializeAsync();
            await database.UpsertAsync(records);
            _usageRecords = await database.GetUsageAsync(from, to);
        }
        catch
        {
            // Keep the live scan usable if local persistence is unavailable.
            _usageRecords = records;
        }

        _providerSnapshots = snapshots;
    }

    public override UIElement? CreatePreviewContent()
    {
        var root = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Brush("#FFA3E635"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,9,0) });
        var copy = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = "LAST 30 DAYS", FontSize = 9, CharacterSpacing = 100, Foreground = Brush("#FF969BA5") });
        copy.Children.Add(new TextBlock { Text = _settings.ShowTokens ? $"{FormatTokens(UsageAggregator.Summarize(_usageRecords).TotalTokens)} tokens" : "ModelMeter", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFF3F4F6") });
        Grid.SetColumn(copy, 1); root.Children.Add(copy);
        if (_settings.ShowProviderCount)
        {
            var count = new TextBlock { Text = $"{_providerSnapshots.Count(x => x.State == ProviderState.Connected)} connected", FontSize = 10, Foreground = Brush("#FFA3E635"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,0,0) };
            Grid.SetColumn(count, 2); root.Children.Add(count);
        }
        return root;
    }

    public override UIElement? CreateFlyoutContent() => new DashboardView(_settings, _usageRecords, _providerSnapshots);

    public UIElement? CreateSettingsContent(IWidgetSettingsContext context)
    {
        var draft = WidgetSettings.Parse(context.SettingsJson);
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "ModelMeter settings", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Choose what appears in your taskbar glance.", FontSize = 12, Opacity = .7 });
        AddToggle(panel, "Show total tokens", draft.ShowTokens, v => draft.ShowTokens = v);
        AddToggle(panel, "Show active provider count", draft.ShowProviderCount, v => draft.ShowProviderCount = v);
        AddToggle(panel, "Show API-equivalent cost in dashboard", draft.ShowCost, v => draft.ShowCost = v);
        void Save()
        {
            context.SaveSettings(JsonSerializer.Serialize(draft));
            context.RequestPreviewRefresh();
        }
        void AddToggle(StackPanel host, string title, bool initial, Action<bool> update)
        {
            var toggle = new ToggleSwitch { Header = title, IsOn = initial };
            toggle.Toggled += (_, _) => { update(toggle.IsOn); Save(); };
            host.Children.Add(toggle);
        }
        panel.Children.Add(new TextBlock { Text = "Privacy first: usage metadata is read locally. Prompt and response text, tool output and source code are never stored by ModelMeter.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = .65 });
        return panel;
    }

    public void OnFlyoutShown() { }
    public void OnFlyoutHidden() { }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string FormatTokens(long value) => value >= 1_000_000
        ? (value / 1_000_000d).ToString("0.##") + "M"
        : value >= 1_000 ? (value / 1_000d).ToString("0.#") + "K" : value.ToString("N0");

    private static SolidColorBrush Brush(string hex) => new(Color.FromArgb(
        Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16),
        Convert.ToByte(hex[5..7], 16), Convert.ToByte(hex[7..9], 16)));
}
