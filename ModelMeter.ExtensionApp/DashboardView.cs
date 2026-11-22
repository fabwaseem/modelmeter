using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModelMeter.Core.Aggregation;
using ModelMeter.Core.Models;

namespace ModelMeter.ExtensionApp;

internal sealed class DashboardView : Grid
{
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly MainPlugin.WidgetSettings _settings;
    private readonly IReadOnlyList<UsageRecord> _records;
    private readonly IReadOnlyList<ProviderSnapshot> _snapshots;
    private string _active = "Overview";

    public DashboardView(MainPlugin.WidgetSettings settings, IReadOnlyList<UsageRecord> records, IReadOnlyList<ProviderSnapshot> snapshots)
    {
        _settings = settings;
        _records = records;
        _snapshots = snapshots;
        Background = B("#FF101113");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(24, 20, 24, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Spacing = 3 };
        brand.Children.Add(new TextBlock { Text = "MODELMETER", FontSize = 12, CharacterSpacing = 150, FontWeight = FontWeights.Bold, Foreground = B("#FFF3F4F6") });
        brand.Children.Add(new TextBlock { Text = "YOUR AI USAGE. ONE DASHBOARD.", FontSize = 9, CharacterSpacing = 90, Foreground = B("#FF969BA5") });
        header.Children.Add(brand);
        var demo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        demo.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = B("#FFA3E635") });
        demo.Children.Add(new TextBlock { Text = "LOCAL USAGE", FontSize = 9, CharacterSpacing = 90, Foreground = B("#FFA3E635") });
        Grid.SetColumn(demo, 1); header.Children.Add(demo);
        Children.Add(header);

        var body = new Grid { Margin = new Thickness(24, 8, 24, 24), ColumnSpacing = 22 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Spacing = 5 };
        foreach (var name in new[] { "Overview", "Providers", "Analytics", "Models", "Sessions", "Quotas" })
        {
            var button = new Button { Content = name, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 10, 12, 10), Background = name == _active ? B("#FF25282D") : B("#00101113"), Foreground = name == _active ? B("#FFA3E635") : B("#FF969BA5"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
            button.Click += (_, _) => Navigate(name);
            _tabs[name] = button; nav.Children.Add(button);
        }
        body.Children.Add(nav);
        Grid.SetColumn(_page, 1); body.Children.Add(_page);
        Grid.SetRow(body, 1); Children.Add(body);
        Navigate("Overview");
    }

    private void Navigate(string name)
    {
        _active = name;
        foreach (var pair in _tabs)
        {
            pair.Value.Background = pair.Key == name ? B("#FF25282D") : B("#00101113");
            pair.Value.Foreground = pair.Key == name ? B("#FFA3E635") : B("#FF969BA5");
        }
        _page.Content = name switch
        {
            "Overview" => Overview(),
            "Providers" => Providers(),
            "Analytics" => Analytics(),
            "Models" => Models(),
            "Sessions" => Sessions(),
            "Quotas" => Quotas(),
            _ => Overview()
        };
    }

    private UIElement Overview()
    {
        var p = new StackPanel { Spacing = 15 };
        var summary = UsageAggregator.Summarize(_records);
        p.Children.Add(Heading("Overview", "Your local AI coding usage, all in one place."));
        var cards = new Grid { ColumnSpacing = 10 };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.Children.Add(Metric("TOTAL TOKENS", FormatTokens(summary.TotalTokens), $"{summary.SessionCount:N0} sessions · last 30 days", true));
        var hasRecordedEstimate = _records.Any(x => x.EstimatedApiCost.HasValue && x.CostKind == CostDataKind.ApiEquivalentEstimate);\n        var costValue = hasRecordedEstimate ? "$" + summary.ApiEquivalentCost.ToString("0.0000") : "Not priced";\n        var costNote = hasRecordedEstimate ? "Provider-reported estimate · not an invoice" : "No provider cost estimate available";\n        var cost = Metric("API-EQUIVALENT COST", _settings.ShowCost ? costValue : "Hidden", _settings.ShowCost ? costNote : "Cost display disabled");
        Grid.SetColumn(cost, 1); cards.Children.Add(cost); p.Children.Add(cards);
        p.Children.Add(Section("USAGE TREND", "LAST 7 DAYS"));
        p.Children.Add(Chart(_records));
        p.Children.Add(Section("PROVIDER ACTIVITY", "LAST 30 DAYS"));
        var grouped = _records.GroupBy(x => x.ProviderId).OrderByDescending(g => g.Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens)).ToArray();
        if (grouped.Length == 0)
            p.Children.Add(EmptyState("No local usage events found", "Use Claude Code or Codex, then reopen ModelMeter to import supported local usage records."));
        else
        {
            var max = grouped.Max(g => g.Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens));
            foreach (var group in grouped)
            {
                var total = group.Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens);
                p.Children.Add(Provider(ProviderName(group.Key), FormatTokens(total), max == 0 ? 0 : (double)total / max, ColorForProvider(group.Key)));
            }
        }
        p.Children.Add(new TextBlock { Text = "Imported from local session metadata. Prompts, responses and tool output are not displayed or retained.", FontSize = 10, Foreground = B("#FF777D87"), TextWrapping = TextWrapping.Wrap });
        return Scroll(p);
    }

    private UIElement Providers()
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Heading("Providers", "Local usage sources detected on this device."));
        foreach (var snapshot in _snapshots)
        {
            var total = _records.Where(x => x.ProviderId == snapshot.ProviderId).Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens);
            p.Children.Add(ProviderCard(snapshot.DisplayName, $"{snapshot.State} · {snapshot.StatusMessage}", total > 0 ? FormatTokens(total) : "—", ColorForProvider(snapshot.ProviderId)));
        }
        if (_snapshots.Count == 0) p.Children.Add(EmptyState("No providers checked", "ModelMeter has not detected any supported local provider directories."));
        p.Children.Add(new TextBlock { Text = "Gemini CLI: enable local telemetry in ~/.gemini/settings.json with telemetry.enabled=true, telemetry.target=local, and an absolute telemetry.outfile path. ModelMeter imports API-response token counts only. OpenCode reads assistant usage metadata from its local SQLite database.", FontSize = 10, Foreground = B("#FF969BA5"), TextWrapping = TextWrapping.Wrap });
        return Scroll(p);
    }

    private UIElement Analytics()
    {
        var p = new StackPanel { Spacing = 14 };
        p.Children.Add(Heading("Analytics", "Token breakdown from supported local session records."));
        p.Children.Add(Chart(_records));
        var summary = UsageAggregator.Summarize(_records);
        p.Children.Add(Section("TOKEN COMPOSITION", "LAST 30 DAYS"));
        var buckets = new[] { ("Input tokens", summary.InputTokens, "#FF7DA8EA"), ("Output tokens", summary.OutputTokens, "#FFA3E635"), ("Cache read", summary.CacheReadTokens, "#FFC18BEA"), ("Cache write", summary.CacheWriteTokens, "#FFD97757") };
        var max = Math.Max(1, buckets.Max(x => x.Item2));
        foreach (var bucket in buckets) p.Children.Add(Provider(bucket.Item1, FormatTokens(bucket.Item2), (double)bucket.Item2 / max, bucket.Item3));
        return Scroll(p);
    }

    private UIElement Models()
    {
        var p = new StackPanel { Spacing = 11 };
        p.Children.Add(Heading("Models", "Token usage grouped by model."));
        var models = _records.GroupBy(x => new { x.ProviderId, x.ModelId })
            .Select(g => new { g.Key.ProviderId, g.Key.ModelId, Tokens = g.Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens) })
            .OrderByDescending(x => x.Tokens).Take(20).ToArray();
        foreach (var model in models) p.Children.Add(ProviderCard(model.ModelId, ProviderName(model.ProviderId), FormatTokens(model.Tokens), ColorForProvider(model.ProviderId)));
        if (models.Length == 0) p.Children.Add(EmptyState("No model usage yet", "Model breakdowns will appear when supported usage events are found."));
        return Scroll(p);
    }

    private UIElement Sessions()
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(Heading("Sessions", "Recent sessions, without conversation contents."));
        var sessions = _records.Where(x => !string.IsNullOrWhiteSpace(x.SessionId)).GroupBy(x => x.SessionId!)
            .Select(g => new { Latest = g.Max(x => x.Timestamp), Project = g.Select(x => x.ProjectName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Local project", Model = g.OrderByDescending(x => x.Timestamp).First().ModelId, Tokens = g.Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens) })
            .OrderByDescending(x => x.Latest).Take(50).ToArray();
        foreach (var session in sessions) p.Children.Add(Session(session.Project, $"{session.Model} · {session.Latest.ToLocalTime():MMM d, h:mm tt}", FormatTokens(session.Tokens)));
        if (sessions.Length == 0) p.Children.Add(EmptyState("No sessions imported", "Sessions will appear here after ModelMeter finds token usage in local provider history."));
        return Scroll(p);
    }

    private UIElement Quotas()
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Heading("Quotas", "Subscription limits are separate from API usage."));
        p.Children.Add(EmptyState("No verified quota data", "This build reads local token usage only. It does not guess subscription limits, remaining quota or reset times."));
        return Scroll(p);
    }

    private static UIElement Heading(string title, string subtitle)
    {
        var s = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
        s.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = B("#FFF3F4F6") });
        s.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = B("#FF969BA5") });
        return s;
    }

    private static UIElement Section(string title, string trailing)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = title, FontSize = 9, CharacterSpacing = 110, FontWeight = FontWeights.SemiBold, Foreground = B("#FF969BA5") });
        var t = new TextBlock { Text = trailing, FontSize = 9, Foreground = B("#FF777D87") }; Grid.SetColumn(t, 1); g.Children.Add(t);
        return g;
    }

    private static Border Metric(string label, string value, string note, bool accent = false)
    {
        var s = new StackPanel { Spacing = 7 };
        s.Children.Add(new TextBlock { Text = label, FontSize = 9, CharacterSpacing = 80, Foreground = B("#FF969BA5") });
        s.Children.Add(new TextBlock { Text = value, FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = accent ? B("#FFA3E635") : B("#FFF3F4F6") });
        s.Children.Add(new TextBlock { Text = note, FontSize = 10, Foreground = B("#FF969BA5"), TextWrapping = TextWrapping.Wrap });
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Child = s };
    }

    private static UIElement Chart(IReadOnlyList<UsageRecord> records)
    {
        var root = new Grid { Height = 145, Margin = new Thickness(4, 8, 4, 0) };
        for (var i = 0; i < 7; i++) root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var today = DateTimeOffset.Now.Date;
        var daily = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = today.AddDays(offset - 6);
            var total = records.Where(x => x.Timestamp.ToLocalTime().Date == date).Sum(x => x.InputTokens + x.OutputTokens + x.CacheReadTokens + x.CacheWriteTokens);
            return (Date: date, Total: total);
        }).ToArray();
        var max = Math.Max(1, daily.Max(x => x.Total));
        for (var i = 0; i < daily.Length; i++)
        {
            var height = Math.Max(2, 110d * daily[i].Total / max);
            var bar = new Border { Width = 22, Height = height, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(5,5,2,2), Background = i == 6 ? B("#FFA3E635") : B("#FF48533A") };
            Grid.SetColumn(bar, i); root.Children.Add(bar);
        }
        var labels = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var _ in Enumerable.Range(0, 7)) labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < daily.Length; i++)
        {
            var t = new TextBlock { Text = daily[i].Date.ToString("ddd")[..1], FontSize = 9, Foreground = B("#FF777D87"), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(t, i); labels.Children.Add(t);
        }
        var s = new StackPanel(); s.Children.Add(root); s.Children.Add(labels);
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = s };
    }

    private static UIElement Provider(string name, string value, double ratio, string color)
    {
        var g = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var s = new StackPanel { Spacing = 5 };
        var labels = new Grid(); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        labels.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = B("#FFE5E7EB") });
        var amount = new TextBlock { Text = value, FontSize = 10, Foreground = B("#FF969BA5") }; Grid.SetColumn(amount, 1); labels.Children.Add(amount);
        s.Children.Add(labels);
        var track = new Grid { Height = 4, Background = B("#FF30343A") };
        track.Children.Add(new Border { Width = 360 * ratio, HorizontalAlignment = HorizontalAlignment.Left, Background = B(color), CornerRadius = new CornerRadius(4) });
        s.Children.Add(track); g.Children.Add(s);
        return g;
    }

    private static UIElement ProviderCard(string name, string status, string tokens, string color)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var s = new StackPanel { Spacing = 5 };
        s.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("#FFF3F4F6") });
        s.Children.Add(new TextBlock { Text = status, FontSize = 10, Foreground = B("#FF969BA5") });
        g.Children.Add(s);
        var n = new TextBlock { Text = tokens, FontSize = 14, Foreground = B(color), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(n, 1); g.Children.Add(n);
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(13), Child = g };
    }

    private static UIElement Session(string project, string details, string tokens)
    {
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(new TextBlock { Text = project, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = B("#FFF3F4F6") });
        s.Children.Add(new TextBlock { Text = details, FontSize = 10, Foreground = B("#FF969BA5") });
        g.Children.Add(s);
        var n = new TextBlock { Text = tokens, FontSize = 13, Foreground = B("#FFA3E635"), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(n, 1); g.Children.Add(n);
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(13), Child = g };
    }

    private static UIElement Quota(string provider, string window, double used, string note)
    {
        var s = new StackPanel { Spacing = 9 };
        s.Children.Add(new TextBlock { Text = provider + " · " + window, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = B("#FFF3F4F6") });
        var track = new Grid { Height = 6, Background = B("#FF30343A") };
        track.Children.Add(new Border { Width = 400 * used, HorizontalAlignment = HorizontalAlignment.Left, Background = B("#FFA3E635"), CornerRadius = new CornerRadius(4) });
        s.Children.Add(track);
        s.Children.Add(new TextBlock { Text = note, FontSize = 10, Foreground = B("#FF969BA5") });
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = s };
    }

    private static UIElement EmptyState(string title, string description)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("#FFF3F4F6") });
        panel.Children.Add(new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = B("#FF969BA5") });
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = panel };
    }

    private static string ProviderName(string id) => id switch { "claude-code" => "Claude Code", "openai-codex" => "OpenAI Codex", "gemini-cli" => "Gemini CLI", "opencode" => "OpenCode", _ => id };
    private static string ColorForProvider(string id) => id switch { "claude-code" => "#FFD97757", "openai-codex" => "#FF74AA8A", "gemini-cli" => "#FF7DA8EA", "opencode" => "#FFB4A0F5", _ => "#FFA3E635" };
    private static string FormatTokens(long value) => value >= 1_000_000 ? (value / 1_000_000d).ToString("0.##") + "M" : value >= 1_000 ? (value / 1_000d).ToString("0.#") + "K" : value.ToString("N0");

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static SolidColorBrush B(string hex) => new(Color.FromArgb(Convert.ToByte(hex[1..3],16), Convert.ToByte(hex[3..5],16), Convert.ToByte(hex[5..7],16), Convert.ToByte(hex[7..9],16)));
}
