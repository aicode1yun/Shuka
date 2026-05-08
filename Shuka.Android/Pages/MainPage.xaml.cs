using Shuka.Android.Services;
using Shuka.Core;

namespace Shuka.Android.Pages;

public partial class MainPage : ContentPage
{
    private bool _discoverBuilt = false;

    public MainPage()
    {
        InitializeComponent();

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

        // Resolve the accent color once from the app resources
        Color accent      = (Color)(Application.Current!.Resources["AccentLight"]);
        Color textPrimary = (Color)(Application.Current!.Resources["TextPrimary"]);
        Color textMuted   = (Color)(Application.Current!.Resources["TextMuted"]);

        if (download)
        {
            TabDownloadLabel.TextColor = textPrimary;
            TabDownloadBar.Color       = accent;
            TabDiscoverLabel.TextColor = textMuted;
            TabDiscoverBar.Color       = Colors.Transparent;
        }
        else
        {
            TabDiscoverLabel.TextColor = textPrimary;
            TabDiscoverBar.Color       = accent;
            TabDownloadLabel.TextColor = textMuted;
            TabDownloadBar.Color       = Colors.Transparent;
        }
    }

    // ── Discover: pin persistence ─────────────────────────────────────────────

    // Pins stored as ordered list of SiteNames (oldest pin = index 0 = shown first)
    private const string PrefKeyPins = "discover_pinned_sources";

    private List<string> LoadPins()
    {
        string raw = Preferences.Default.Get(PrefKeyPins, "");
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split('|').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private void SavePins(List<string> pins)
        => Preferences.Default.Set(PrefKeyPins, string.Join("|", pins));

    private bool IsPinned(string siteName)
        => LoadPins().Contains(siteName);

    private void TogglePin(string siteName)
    {
        var pins = LoadPins();
        if (pins.Contains(siteName))
            pins.Remove(siteName);
        else
            pins.Add(siteName); // append = newest pin last, oldest first
        SavePins(pins);
    }

    // ── Discover: source cards ────────────────────────────────────────────────

    private void BuildDiscoverSources() => RebuildSourceList(filter: "");

    private void RebuildSourceList(string filter)
    {
        var pins    = LoadPins();
        var sources = DiscoverService.Sources;

        // Apply filter
        IEnumerable<IBrowsableAdapter> filtered = string.IsNullOrWhiteSpace(filter)
            ? sources
            : sources.Where(s =>
                s.SiteName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

        // Sort: pinned first (oldest pin = lowest index = first), then alphabetical
        var sorted = filtered
            .OrderBy(s =>
            {
                int idx = pins.IndexOf(s.SiteName);
                return idx >= 0 ? idx : int.MaxValue; // pinned items by pin age
            })
            .ThenBy(s => s.SiteName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DiscoverSourceList.Children.Clear();

        if (sorted.Count == 0)
        {
            var empty = new Label
            {
                Text = "No sources match your filter.",
                FontSize = 13,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 24),
            };
            empty.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            DiscoverSourceList.Children.Add(empty);
            return;
        }

        // Section header for pinned
        bool shownPinnedHeader = false;
        bool shownAllHeader    = false;

        foreach (var source in sorted)
        {
            bool pinned = pins.Contains(source.SiteName);

            if (pinned && !shownPinnedHeader)
            {
                DiscoverSourceList.Children.Add(MakeSectionHeader("PINNED"));
                shownPinnedHeader = true;
            }
            else if (!pinned && !shownAllHeader && shownPinnedHeader)
            {
                DiscoverSourceList.Children.Add(MakeSectionHeader("ALL SOURCES"));
                shownAllHeader = true;
            }
            else if (!pinned && !shownAllHeader && !shownPinnedHeader)
            {
                DiscoverSourceList.Children.Add(MakeSectionHeader("ALL SOURCES"));
                shownAllHeader = true;
            }

            DiscoverSourceList.Children.Add(BuildSourceCard(source, pinned));
        }
    }

    private Label MakeSectionHeader(string text)
    {
        var lbl = new Label
        {
            Text = text,
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(4, 8, 0, 2),
            CharacterSpacing = 1.2,
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        return lbl;
    }

    private View BuildSourceCard(IBrowsableAdapter source, bool pinned)
    {
        // ── Left icon badge ──────────────────────────────────────────────────
        var iconLabel = new Label
        {
            Text            = source.IconGlyph,
            FontFamily      = "MaterialSymbols",
            FontSize        = 22,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
        };
        iconLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var iconBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            WidthRequest    = 48,
            HeightRequest   = 48,
            VerticalOptions = LayoutOptions.Center,
            Content         = iconLabel,
        };
        iconBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");

        // ── Text stack ───────────────────────────────────────────────────────
        var titleLabel = new Label
        {
            Text           = source.SiteName,
            FontSize       = 15,
            FontAttributes = FontAttributes.Bold,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var descLabel = new Label
        {
            Text     = source.Description,
            FontSize = 11,
        };
        descLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // CF bypass badge — only shown when the source needs it
        var cfBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding         = new Thickness(6, 2),
            HorizontalOptions = LayoutOptions.Start,
            IsVisible       = source.RequiresCfBypass,
        };
        cfBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
        var cfLabel = new Label
        {
            Text           = "CF bypass",
            FontSize       = 9,
            FontAttributes = FontAttributes.Bold,
        };
        cfLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        cfBadge.Content = cfLabel;

        var textStack = new VerticalStackLayout
        {
            Spacing         = 3,
            VerticalOptions = LayoutOptions.Center,
            Children        = { titleLabel, descLabel, cfBadge },
        };

        // ── Pin button ───────────────────────────────────────────────────────
        var pinIcon = new Label
        {
            Text       = pinned ? "\uE9C9" : "\uE9C8", // push_pin filled / outlined
            FontFamily = "MaterialSymbols",
            FontSize   = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
        };
        pinIcon.SetDynamicResource(Label.TextColorProperty,
            pinned ? "AccentLight" : "TextMuted");

        var pinBtn = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            WidthRequest    = 40,
            HeightRequest   = 40,
            VerticalOptions = LayoutOptions.Center,
            Content         = pinIcon,
        };
        pinBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                TogglePin(source.SiteName);
                string filterText = GlobalSearchEntry.Text?.Trim() ?? "";
                RebuildSourceList(filterText);
            })
        });

        // ── Chevron ──────────────────────────────────────────────────────────
        var chevron = new Label
        {
            Text            = "\uE5CC",
            FontFamily      = "MaterialSymbols",
            FontSize        = 20,
            VerticalOptions = LayoutOptions.Center,
        };
        chevron.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // ── Row layout ───────────────────────────────────────────────────────
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },   // icon
                new ColumnDefinition { Width = GridLength.Star },   // text
                new ColumnDefinition { Width = GridLength.Auto },   // pin
                new ColumnDefinition { Width = GridLength.Auto },   // chevron
            },
            ColumnSpacing = 12,
            Padding       = new Thickness(14, 14),
        };
        row.Add(iconBadge, 0, 0);
        row.Add(textStack, 1, 0);
        row.Add(pinBtn,    2, 0);
        row.Add(chevron,   3, 0);

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
                await Shell.Current.Navigation.PushAsync(
                    new WebBrowsePage(source.GetRecentUrl(1)));
            })
        });

        return card;
    }

    // ── Discover: source filter ───────────────────────────────────────────────

    private void OnSourceFilterChanged(object sender, TextChangedEventArgs e)
    {
        string filter = e.NewTextValue?.Trim() ?? "";
        GlobalSearchClearBtn.IsVisible = !string.IsNullOrEmpty(filter);
        RebuildSourceList(filter);
    }

    private void OnGlobalSearchClearTapped(object sender, TappedEventArgs e)
    {
        GlobalSearchEntry.Text         = "";
        GlobalSearchClearBtn.IsVisible = false;
        RebuildSourceList(filter: "");
    }

    // ── Discover: removed global search (replaced by source filter) ───────────
    // The following stubs keep the build clean if any XAML event refs remain.
    private void OnGlobalSearchCompleted(object sender, EventArgs e) { }

    private View BuildSearchResultCard(NovelEntry novel) => new Label(); // unused
    private View BuildSeeAllButton(IBrowsableAdapter source, string query) => new Label(); // unused

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
