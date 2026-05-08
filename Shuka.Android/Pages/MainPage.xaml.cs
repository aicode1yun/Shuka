using Shuka.Android.Services;
using Shuka.Core;
using Shuka.Android.Platform;

namespace Shuka.Android.Pages;

public partial class MainPage : ContentPage
{
    private readonly DiscoverService _discoverService;
    private bool _discoverBuilt = false;

    public MainPage()
    {
        InitializeComponent();

        _discoverService = new DiscoverService(new WebViewCloudflareBypass());

        UrlEntry.TextChanged   += (_, e) => UrlClearBtn.IsVisible   = !string.IsNullOrEmpty(e.NewTextValue);
        CoverEntry.TextChanged += (_, e) => CoverClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        GlobalSearchEntry.TextChanged += (_, e) =>
            GlobalSearchClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

        SetActiveTab(download: true);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Only animate the currently visible panel
        var panel = DownloadPanel.IsVisible ? (View)DownloadPanel : DiscoverPanel;
        panel.Opacity = 0;
        panel.TranslationY = 18;
        await Task.WhenAll(
            panel.FadeToAsync(1.0, 220, Easing.CubicOut),
            panel.TranslateToAsync(0, 0, 220, Easing.CubicOut));
    }

    // ── Top tab switching ─────────────────────────────────────────────────────

    private void OnTabDownloadTapped(object sender, TappedEventArgs e) => SetActiveTab(download: true);
    private void OnTabDiscoverTapped(object sender, TappedEventArgs e)
    {
        SetActiveTab(download: false);
        if (!_discoverBuilt)
        {
            BuildDiscoverSources();
            _discoverBuilt = true;
        }
    }

    private void SetActiveTab(bool download)
    {
        DownloadPanel.IsVisible = download;
        DiscoverPanel.IsVisible = !download;

        if (download)
        {
            TabDownloadPill.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
            TabDownloadLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            TabDiscoverPill.BackgroundColor = Colors.Transparent;
            TabDiscoverLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        }
        else
        {
            TabDiscoverPill.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
            TabDiscoverLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            TabDownloadPill.BackgroundColor = Colors.Transparent;
            TabDownloadLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        }
    }

    // ── Discover: source cards ────────────────────────────────────────────────

    private void BuildDiscoverSources()
    {
        DiscoverSourceList.Children.Clear();
        foreach (var source in DiscoverService.Sources)
            DiscoverSourceList.Children.Add(BuildSourceCard(source));
    }

    private View BuildSourceCard(IBrowsableAdapter source)
    {
        var cfBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding         = new Thickness(8, 3),
            IsVisible       = source.RequiresCfBypass,
        };
        cfBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
        var cfLabel = new Label { Text = "CF bypass", FontSize = 10, FontAttributes = FontAttributes.Bold };
        cfLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        cfBadge.Content = cfLabel;

        var titleLabel = new Label { Text = source.SiteName, FontSize = 16, FontAttributes = FontAttributes.Bold };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var subtitleLabel = new Label { Text = "Chinese novels", FontSize = 12 };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var chevron = new Label { Text = "\uE5CC", FontFamily = "MaterialSymbols", FontSize = 22, VerticalOptions = LayoutOptions.Center };
        chevron.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var textStack = new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitleLabel, cfBadge }
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Padding = new Thickness(18, 16),
        };
        row.Add(textStack, 0, 0);
        row.Add(chevron,   1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding         = new Thickness(0),
            Content         = row,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");

        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await card.ScaleToAsync(0.97, 80, Easing.CubicOut);
                await card.ScaleToAsync(1.0,  80, Easing.SpringOut);
                await Shell.Current.Navigation.PushAsync(new SourceBrowsePage(source));
            })
        });

        return card;
    }

    // ── Discover: global search ───────────────────────────────────────────────

    private async void OnGlobalSearchCompleted(object sender, EventArgs e)
    {
        string query = GlobalSearchEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;
        await RunGlobalSearchAsync(query);
    }

    private void OnGlobalSearchClearTapped(object sender, TappedEventArgs e)
    {
        GlobalSearchEntry.Text = "";
        GlobalSearchClearBtn.IsVisible = false;
        DiscoverSourceScrollView.IsVisible    = true;
        DiscoverSearchResultsView.IsVisible   = false;
        DiscoverSearchLoadingState.IsVisible  = false;
    }

    private async Task RunGlobalSearchAsync(string query)
    {
        DiscoverSourceScrollView.IsVisible    = false;
        DiscoverSearchResultsView.IsVisible   = false;
        DiscoverSearchLoadingState.IsVisible  = true;
        DiscoverSearchResultsList.Children.Clear();

        try
        {
            var results = await _discoverService.SearchAllAsync(query);
            DiscoverSearchLoadingState.IsVisible = false;

            if (results.Count == 0)
            {
                DiscoverSearchResultsLabel.Text = $"No results for \"{query}\"";
                DiscoverSearchResultsView.IsVisible = true;
                return;
            }

            int total = results.Sum(r => r.Results.Novels.Count);
            DiscoverSearchResultsLabel.Text = $"{total} result{(total == 1 ? "" : "s")} for \"{query}\"";

            foreach (var (source, page) in results)
            {
                var header = new Label
                {
                    Text = $"{source.SiteName}  ·  {page.Novels.Count} result{(page.Novels.Count == 1 ? "" : "s")}",
                    FontSize = 12, FontAttributes = FontAttributes.Bold,
                    Margin = new Thickness(4, 8, 0, 4),
                };
                header.SetDynamicResource(Label.TextColorProperty, "AccentLight");
                DiscoverSearchResultsList.Children.Add(header);

                foreach (var novel in page.Novels.Take(5))
                    DiscoverSearchResultsList.Children.Add(BuildSearchResultCard(novel));

                if (page.Novels.Count > 5 || page.HasNextPage)
                    DiscoverSearchResultsList.Children.Add(BuildSeeAllButton(source, query));
            }

            DiscoverSearchResultsView.IsVisible = true;
        }
        catch (Exception ex)
        {
            DiscoverSearchLoadingState.IsVisible = false;
            DiscoverSearchResultsLabel.Text = $"Search failed: {ex.Message}";
            DiscoverSearchResultsView.IsVisible = true;
        }
    }

    private View BuildSearchResultCard(NovelEntry novel)
    {
        View coverView;
        if (!string.IsNullOrWhiteSpace(novel.CoverUrl) &&
            Uri.TryCreate(novel.CoverUrl, UriKind.Absolute, out var coverUri))
        {
            var img = new Image { Source = ImageSource.FromUri(coverUri),
                Aspect = Aspect.AspectFill, WidthRequest = 52, HeightRequest = 74 };
            coverView = new Border { StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest = 52, HeightRequest = 74, Content = img };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }
        else
        {
            var ph = new Label { Text = "\uEA78", FontFamily = "MaterialSymbols", FontSize = 24,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            ph.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            coverView = new Border { StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest = 52, HeightRequest = 74, Content = ph };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }

        var titleLbl = new Label { Text = novel.Title, FontSize = 13, FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
        titleLbl.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var authorLbl = new Label { Text = novel.Author ?? "", FontSize = 11,
            IsVisible = !string.IsNullOrWhiteSpace(novel.Author),
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        authorLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var dlIcon = new Label { Text = "\uF090", FontFamily = "MaterialSymbols", FontSize = 12,
            VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 4, 0) };
        dlIcon.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");
        var dlText = new Label { Text = "Download", FontSize = 10, FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center };
        dlText.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");

        var dlBtn = new Border { StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HeightRequest = 28, Padding = new Thickness(8, 0), HorizontalOptions = LayoutOptions.Start,
            Content = new HorizontalStackLayout { Spacing = 0,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Children = { dlIcon, dlText } } };
        dlBtn.SetDynamicResource(Border.BackgroundColorProperty, "Accent");
        dlBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await dlBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await dlBtn.ScaleToAsync(1.0,  70, Easing.SpringOut);
                DownloadManager.Instance.Enqueue(novel.Url, 0,
                    string.IsNullOrWhiteSpace(novel.CoverUrl) ? null : novel.CoverUrl);
                if (Shell.Current != null)
                    await Shell.Current.GoToAsync("//DownloadsPage");
            })
        });

        var textStack = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center,
            Children = { titleLbl, authorLbl, dlBtn } };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 10, Padding = new Thickness(12),
        };
        grid.Add(coverView, 0, 0);
        grid.Add(textStack, 1, 0);

        var card = new Border { StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(0), Content = grid };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");
        return card;
    }

    private View BuildSeeAllButton(IBrowsableAdapter source, string query)
    {
        var lbl = new Label { Text = $"See all results from {source.SiteName} →",
            FontSize = 12, FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center };
        lbl.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var btn = new Border { StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            HeightRequest = 40, HorizontalOptions = LayoutOptions.Fill, Content = lbl };
        btn.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        btn.SetDynamicResource(Border.StrokeProperty, "Stroke");

        btn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
                await Shell.Current.Navigation.PushAsync(new SourceBrowsePage(source, initialQuery: query)))
        });
        return btn;
    }

    // ── Download handlers (unchanged) ─────────────────────────────────────────

    private async void OnDownloadClicked(object sender, TappedEventArgs e)
    {
        await AnimateButtonPress(DownloadBtn);

        string url = UrlEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            await DisplayAlertAsync("Missing URL", "Please enter a novel URL.", "OK");
            return;
        }

        int chapters = 0, chapterFrom = 0;
        string chapText = ChaptersEntry.Text?.Trim() ?? "0";
        if (chapText.Contains('-'))
        {
            var parts = chapText.Split('-');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), out int from) &&
                int.TryParse(parts[1].Trim(), out int to) &&
                from >= 1 && to >= from)
            {
                chapterFrom = from;
                chapters    = to - from + 1;
            }
            else
            {
                await DisplayAlertAsync("Invalid Range", "Use format: 100-200 (from chapter 100 to 200)", "OK");
                return;
            }
        }
        else
        {
            chapters = int.TryParse(chapText, out int n) ? n : 0;
        }

        string? coverUrl = string.IsNullOrWhiteSpace(CoverEntry.Text) ? null : CoverEntry.Text.Trim();

        UrlEntry.IsEnabled = CoverEntry.IsEnabled = ChaptersEntry.IsEnabled = false;
        await Task.Delay(50);
        UrlEntry.IsEnabled = CoverEntry.IsEnabled = ChaptersEntry.IsEnabled = true;

        var existing = DownloadManager.Instance.FindExisting(url);
        if (existing != null)
        {
            bool shouldQueue = await HandleDuplicate(existing);
            if (!shouldQueue) return;
        }

        DownloadManager.Instance.Enqueue(url, chapters, coverUrl, chapterFrom);
        await AnimateClearInputs();
        await ShowQueuedBanner();
    }

    private async Task AnimateButtonPress(Border button)
    {
        await button.ScaleToAsync(0.95, 80, Easing.CubicOut);
        await button.ScaleToAsync(1.0, 80, Easing.SpringOut);
    }

    private async Task AnimateClearInputs()
    {
        var entries = new[] { UrlEntry, CoverEntry, ChaptersEntry };
        await Task.WhenAll(entries.Select(e => e.FadeToAsync(0.5, 150)));
        UrlEntry.Text = ""; CoverEntry.Text = ""; ChaptersEntry.Text = "0";
        await Task.WhenAll(entries.Select(e => e.FadeToAsync(1.0, 150)));
    }

    private async Task ShowQueuedBanner()
    {
        QueuedBanner.Opacity = 0; QueuedBanner.TranslationY = -20; QueuedBanner.IsVisible = true;
        await Task.WhenAll(QueuedBanner.FadeToAsync(1.0, 300, Easing.CubicOut),
                           QueuedBanner.TranslateToAsync(0, 0, 300, Easing.CubicOut));
        await Task.Delay(3000);
        await Task.WhenAll(QueuedBanner.FadeToAsync(0, 300, Easing.CubicIn),
                           QueuedBanner.TranslateToAsync(0, -20, 300, Easing.CubicIn));
        QueuedBanner.IsVisible = false;
    }

    private async Task<bool> HandleDuplicate(DownloadItem existing)
    {
        string title = string.IsNullOrWhiteSpace(existing.Title) || existing.Title == "Loading..."
            ? "this novel" : $"\"{existing.Title}\"";

        switch (existing.Status)
        {
            case DownloadStatus.Running:
            case DownloadStatus.Queued:
            {
                string? choice = await DisplayActionSheetAsync($"Already downloading {title}", "Cancel", null,
                    "Go to Downloads tab", "Download again anyway");
                if (choice == "Go to Downloads tab") { await Shell.Current.GoToAsync("//DownloadsPage"); return false; }
                return choice == "Download again anyway";
            }
            case DownloadStatus.Done:
            {
                string? choice = await DisplayActionSheetAsync($"{title} was already downloaded", "Cancel", null,
                    "Download again (re-translate)", "Open existing EPUB", "Go to Downloads tab");
                if (choice == "Download again (re-translate)") return true;
                if (choice == "Open existing EPUB" && existing.EpubPath != null && File.Exists(existing.EpubPath))
                {
                    try { await Launcher.Default.OpenAsync(new OpenFileRequest { Title = "Open EPUB",
                        File = new ReadOnlyFile(existing.EpubPath, "application/epub+zip") }); }
                    catch { await Share.Default.RequestAsync(new ShareFileRequest { Title = "Open EPUB",
                        File = new ShareFile(existing.EpubPath, "application/epub+zip") }); }
                    return false;
                }
                if (choice == "Go to Downloads tab") { await Shell.Current.GoToAsync("//DownloadsPage"); return false; }
                return false;
            }
            case DownloadStatus.Failed:
            case DownloadStatus.Cancelled:
            {
                string statusWord = existing.Status == DownloadStatus.Failed ? "failed" : "cancelled";
                string? choice = await DisplayActionSheetAsync($"A previous download of {title} {statusWord}",
                    "Cancel", null, "Download again", "Go to Downloads tab");
                if (choice == "Download again") { DownloadManager.Instance.Dismiss(existing); return true; }
                if (choice == "Go to Downloads tab") { await Shell.Current.GoToAsync("//DownloadsPage"); return false; }
                return false;
            }
            default: return true;
        }
    }

    private async void OnUrlPasteTapped(object sender, TappedEventArgs e)
    {
        string? text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) UrlEntry.Text = text.Trim();
    }
    private void OnUrlClearTapped(object sender, TappedEventArgs e) => UrlEntry.Text = "";
    private async void OnCoverPasteTapped(object sender, TappedEventArgs e)
    {
        string? text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) CoverEntry.Text = text.Trim();
    }
    private void OnCoverClearTapped(object sender, TappedEventArgs e) => CoverEntry.Text = "";
}
