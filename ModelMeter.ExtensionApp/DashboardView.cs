using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModelMeter.ExtensionApp;

internal sealed class DashboardView : Grid
{
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly MainPlugin.WidgetSettings _settings;
    private string _active = "Overview";

    public DashboardView(MainPlugin.WidgetSettings settings)
    {
        _settings = settings;
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
        demo.Children.Add(new TextBlock { Text = "DEMO DATA", FontSize = 9, CharacterSpacing = 90, Foreground = B("#FFA3E635") });
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
        p.Children.Add(Heading("Overview", "Your AI activity, all in one place."));
        var cards = new Grid { ColumnSpacing = 10 };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.Children.Add(Metric("TOTAL TOKENS", "1.84M", "+18.6% vs yesterday", true));
        var cost = Metric("API-EQUIVALENT COST", _settings.ShowCost ? "$12.48" : "Hidden", "Estimate · not subscription spend");
        Grid.SetColumn(cost, 1); cards.Children.Add(cost); p.Children.Add(cards);
        p.Children.Add(Section("USAGE TREND", "LAST 7 DAYS"));
        p.Children.Add(Chart());
        p.Children.Add(Section("PROVIDER ACTIVITY", "TODAY"));
        p.Children.Add(Provider("Claude Code", "812K tokens", .82, "#FFD97757"));
        p.Children.Add(Provider("OpenAI Codex", "526K tokens", .56, "#FF74AA8A"));
        p.Children.Add(Provider("Gemini CLI", "338K tokens", .36, "#FF7DA8EA"));
        p.Children.Add(Provider("OpenCode", "164K tokens", .18, "#FFC18BEA"));
        p.Children.Add(new TextBlock { Text = "Illustrative demo figures. API-equivalent cost is not an actual bill.", FontSize = 10, Foreground = B("#FF777D87"), TextWrapping = TextWrapping.Wrap });
        return Scroll(p);
    }

    private UIElement Providers()
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Heading("Providers", "Provider adapters will connect local usage sources."));
        p.Children.Add(ProviderCard("Claude Code", "Planned · Local session history", "812K", "#FFD97757"));
        p.Children.Add(ProviderCard("OpenAI Codex", "Planned · Local session history", "526K", "#FF74AA8A"));
        p.Children.Add(ProviderCard("Gemini CLI", "Planned · Telemetry opt-in", "338K", "#FF7DA8EA"));
        p.Children.Add(ProviderCard("OpenCode", "Planned · Local session database", "164K", "#FFC18BEA"));
        return Scroll(p);
    }

    private UIElement Analytics()
    {
        var p = new StackPanel { Spacing = 14 };
        p.Children.Add(Heading("Analytics", "Explore token volume and usage mix."));
        p.Children.Add(Chart());
        p.Children.Add(Section("TOKEN COMPOSITION", "TODAY"));
        p.Children.Add(Provider("Input tokens", "612K · 33%", .33, "#FF7DA8EA"));
        p.Children.Add(Provider("Output tokens", "418K · 23%", .23, "#FFA3E635"));
        p.Children.Add(Provider("Cache read", "766K · 42%", .42, "#FFC18BEA"));
        p.Children.Add(Provider("Cache write", "44K · 2%", .05, "#FFD97757"));
        return Scroll(p);
    }

    private UIElement Models()
    {
        var p = new StackPanel { Spacing = 11 };
        p.Children.Add(Heading("Models", "Usage grouped by model."));
        p.Children.Add(ProviderCard("Claude Sonnet", "Claude Code · Input, output and cache", "684K", "#FFD97757"));
        p.Children.Add(ProviderCard("GPT-5", "OpenAI Codex · Input and output", "526K", "#FF74AA8A"));
        p.Children.Add(ProviderCard("Gemini Pro", "Gemini CLI · Input and output", "338K", "#FF7DA8EA"));
        p.Children.Add(ProviderCard("Claude Opus", "Claude Code · Input, output and cache", "128K", "#FFD97757"));
        p.Children.Add(ProviderCard("DeepSeek", "OpenCode · Input and output", "164K", "#FFC18BEA"));
        return Scroll(p);
    }

    private UIElement Sessions()
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(Heading("Sessions", "Recent activity, without conversation contents."));
        p.Children.Add(Session("modelmeter", "Claude Sonnet · Just now · 42 min", "184K"));
        p.Children.Add(Session("firevoice", "GPT-5 · 12 min ago · 1h 08m", "142K"));
        p.Children.Add(Session("reelspan", "Gemini Pro · 38 min ago · 26 min", "96K"));
        p.Children.Add(Session("hiway-platform", "Claude Sonnet · 1h ago · 54 min", "88K"));
        return Scroll(p);
    }

    private UIElement Quotas()
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Heading("Quotas", "Only provider-reported limits will appear here."));
        p.Children.Add(Quota("Claude Code", "5-hour window", .62, "Demo: 62% used · reset in 2h 14m"));
        p.Children.Add(Quota("Claude Code", "Weekly window", .38, "Demo: 38% used · reset Monday"));
        p.Children.Add(new TextBlock { Text = "These are placeholders, not live quota data. Production quota cards will require a reliable supported provider source.", FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = B("#FF969BA5") });
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

    private static UIElement Chart()
    {
        var root = new Grid { Height = 145, Margin = new Thickness(4, 8, 4, 0) };
        for (var i = 0; i < 7; i++) root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var values = new[] { .42, .61, .48, .74, .66, .94, .78 };
        for (var i = 0; i < values.Length; i++)
        {
            var bar = new Border { Width = 22, Height = 110 * values[i], VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(5,5,2,2), Background = i == 5 ? B("#FFA3E635") : B("#FF48533A") };
            Grid.SetColumn(bar, i); root.Children.Add(bar);
        }
        var labels = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var _ in Enumerable.Range(0, 7)) labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var days = new[] { "M", "T", "W", "T", "F", "S", "S" };
        for (var i = 0; i < days.Length; i++) { var t = new TextBlock { Text = days[i], FontSize = 9, Foreground = B("#FF777D87"), HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(t, i); labels.Children.Add(t); }
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
        return new Border { Background = B("#FF191B1F"), BorderBrush = B("#FF30343A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = g };
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

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static SolidColorBrush B(string hex) => new(Color.FromArgb(Convert.ToByte(hex[1..3],16), Convert.ToByte(hex[3..5],16), Convert.ToByte(hex[5..7],16), Convert.ToByte(hex[7..9],16)));
}
