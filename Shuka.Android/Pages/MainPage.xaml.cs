using Shuka.Android.Behaviors;
using Shuka.Android.Services;
using Shuka.Core;
using System.Text.RegularExpressions;

namespace Shuka.Android.Pages;

public partial class MainPage : ContentPage
{
    public static MainPage? Instance { get; private set; }

    private readonly DiscoverService _discoverService;
    private bool _discoverBuilt = false;
    private CancellationTokenSource? _discoverBannerCts;

    public MainPage()
    {
        InitializeComponent();
        Instance = this;
        _discoverService = new DiscoverService(new Platform.WebViewCloudflareBypass());

        UrlEntry.TextChanged += (_, e) =>
        {
            UrlClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
            // Hide preview card when URL is cleared
            if (string.IsNullOrEmpty(e.NewTextValue))
                PreviewInfoCard.IsVisible = false;
        };
        CoverEntry.TextChanged += (_, e) => CoverClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        GlobalSearchEntry.TextChanged += (_, e) =>
            GlobalSearchClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

        // Subscribe to bookmark changes to update the badge counts
        BookmarkService.Instance.BookmarksChanged += OnBookmarksChanged;

        SetActiveTab(download: true);
    }

    private void OnBookmarksChanged(object? sender, EventArgs e)
    {
        // Rebuild discover sources if they've been built
        if (_discoverBuilt)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildDiscoverSources();
            });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Restore draft inputs that were saved before the app went to background
        string savedUrl = Preferences.Default.Get("draft_url", "");
        string savedCover = Preferences.Default.Get("draft_cover", "");
        string savedChapters = Preferences.Default.Get("draft_chapters", "0");

        if (!string.IsNullOrEmpty(savedUrl) && string.IsNullOrEmpty(UrlEntry.Text))
            UrlEntry.Text = savedUrl;
        if (!string.IsNullOrEmpty(savedCover) && string.IsNullOrEmpty(CoverEntry.Text))
            CoverEntry.Text = savedCover;
        if (ChaptersEntry.Text == "0" || string.IsNullOrEmpty(ChaptersEntry.Text))
            ChaptersEntry.Text = savedChapters;

        // Re-apply tab colors in case the theme changed while on another tab
        SetActiveTab(DownloadPanel.IsVisible);
        UpdateDiscoverBottomInset();

        TabTransition.Prepare(myTabIndex: 0);
        await TabTransition.SlideInAsync(BodyGrid);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateDiscoverBottomInset();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Persist draft inputs so they survive app backgrounding / process death
        Preferences.Default.Set("draft_url", UrlEntry.Text ?? "");
        Preferences.Default.Set("draft_cover", CoverEntry.Text ?? "");
        Preferences.Default.Set("draft_chapters", ChaptersEntry.Text ?? "0");
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
        if (!download)
            UpdateDiscoverBottomInset();

        // Resolve the accent color once from the app resources
        Color accent = (Color)(Application.Current!.Resources["AccentLight"]);
        Color textPrimary = (Color)(Application.Current!.Resources["TextPrimary"]);
        Color textMuted = (Color)(Application.Current!.Resources["TextMuted"]);

        if (download)
        {
            TabDownloadLabel.TextColor = textPrimary;
            TabDownloadBar.Color = accent;
            TabDiscoverLabel.TextColor = textMuted;
            TabDiscoverBar.Color = Colors.Transparent;
        }
        else
        {
            TabDiscoverLabel.TextColor = textPrimary;
            TabDiscoverBar.Color = accent;
            TabDownloadLabel.TextColor = textMuted;
            TabDownloadBar.Color = Colors.Transparent;
        }
    }

    private void UpdateDiscoverBottomInset()
    {
        double bottomInset = 40;
#if ANDROID
        if (MainActivity.Instance is { } activity)
            bottomInset = Math.Max(bottomInset, activity.GetOverlayBottomInsetDip(8));
#endif

        var sourcePad = DiscoverSourceList.Padding;
        DiscoverSourceList.Padding = new Thickness(sourcePad.Left, sourcePad.Top, sourcePad.Right, bottomInset);

        var resultPad = SearchResultsList.Padding;
        SearchResultsList.Padding = new Thickness(resultPad.Left, resultPad.Top, resultPad.Right, bottomInset);
    }

    // ── Fetch callback from WebBrowsePage ────────────────────────────────────

    /// <summary>
    /// Called by WebBrowsePage when the user taps Fetch.
    /// Switches to the Download tab and pre-fills the URL entry.
    /// </summary>
    public void FillUrlFromWebView(string url)
    {
        SetActiveTab(download: true);
        UrlEntry.Text = url;
        // Scroll to top so the URL field is visible
        _ = DownloadPanel.ScrollToAsync(0, 0, false);
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

    private void BuildDiscoverSources() => RebuildSourceList();

    private void RebuildSourceList()
    {
        var pins = LoadPins();
        var sources = DiscoverService.Sources;

        // Sort: pinned first (oldest pin = lowest index = first), then alphabetical
        var sorted = sources
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
        bool shownAllHeader = false;

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
            Text = source.IconGlyph,
            FontFamily = "MaterialSymbols",
            FontSize = 22,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        iconLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var iconBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            WidthRequest = 48,
            HeightRequest = 48,
            VerticalOptions = LayoutOptions.Center,
            Content = iconLabel,
        };
        iconBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");

        // ── Text stack ───────────────────────────────────────────────────────
        var titleLabel = new Label
        {
            Text = source.SiteName,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var descLabel = new Label
        {
            Text = source.Description,
            FontSize = 11,
        };
        descLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // CF bypass badge — only shown when the source needs it
        var cfBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(6, 2),
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = source.RequiresCfBypass,
        };
        cfBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
        var cfLabel = new Label
        {
            Text = "CF bypass",
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
        };
        cfLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        cfBadge.Content = cfLabel;

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, descLabel, cfBadge },
        };

        // ── Pin button ───────────────────────────────────────────────────────
        var pinIcon = new Label
        {
            Text = pinned ? "\uE9C9" : "\uE9C7", // active pin / default pushpin-style
            FontFamily = "MaterialSymbols",
            FontSize = 20,
            Rotation = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        pinIcon.SetDynamicResource(Label.TextColorProperty,
            pinned ? "AccentLight" : "TextMuted");

        var pinBtn = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 40,
            HeightRequest = 40,
            VerticalOptions = LayoutOptions.Center,
            Content = pinIcon,
        };
        pinBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                TogglePin(source.SiteName);
                RebuildSourceList();
            })
        });

        // ── Bookmark button ──────────────────────────────────────────────────
        int bookmarkCount = BookmarkService.Instance.GetBookmarkCountForSite(source.SiteName);

        var bookmarkIcon = new Label
        {
            Text = bookmarkCount > 0 ? "\uE866" : "\uE867", // bookmark filled / outlined
            FontFamily = "MaterialSymbols",
            FontSize = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        bookmarkIcon.SetDynamicResource(Label.TextColorProperty,
            bookmarkCount > 0 ? "AccentLight" : "TextMuted");

        // Badge showing bookmark count (only if > 0)
        var bookmarkBadgeContainer = new Grid
        {
            WidthRequest = 40,
            HeightRequest = 40,
            VerticalOptions = LayoutOptions.Center,
        };
        bookmarkBadgeContainer.Add(bookmarkIcon);

        if (bookmarkCount > 0)
        {
            // Small circular badge with count
            var badgeCircle = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                WidthRequest = 16,
                HeightRequest = 16,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 0, 0, 0),
            };
            badgeCircle.SetDynamicResource(Border.BackgroundColorProperty, "AccentLight");

            var badgeLabel = new Label
            {
                Text = bookmarkCount > 99 ? "99+" : bookmarkCount.ToString(),
                FontSize = 8,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            badgeCircle.Content = badgeLabel;

            bookmarkBadgeContainer.Add(badgeCircle);
        }

        var bookmarkBtn = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Content = bookmarkBadgeContainer,
        };
        bookmarkBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await bookmarkBtn.ScaleToAsync(0.85, 70, Easing.CubicOut);
                await bookmarkBtn.ScaleToAsync(1.0, 70, Easing.SpringOut);

                // Navigate to bookmarks page filtered by this source
                await Shell.Current.Navigation.PushAsync(
                    new BookmarksPage(source.SiteName));
            })
        });

        // ── Chevron ──────────────────────────────────────────────────────────
        var chevron = new Label
        {
            Text = "\uE5CC",
            FontFamily = "MaterialSymbols",
            FontSize = 20,
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
                new ColumnDefinition { Width = GridLength.Auto },   // bookmark
                new ColumnDefinition { Width = GridLength.Auto },   // pin
                new ColumnDefinition { Width = GridLength.Auto },   // chevron
            },
            ColumnSpacing = 12,
            Padding = new Thickness(14, 14),
        };
        row.Add(iconBadge, 0, 0);
        row.Add(textStack, 1, 0);
        row.Add(bookmarkBtn, 2, 0);
        row.Add(pinBtn, 3, 0);
        row.Add(chevron, 4, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(0),
            Content = row,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");

        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                try
                {
                    await card.ScaleToAsync(0.97, 80, Easing.CubicOut);
                    await card.ScaleToAsync(1.0, 80, Easing.SpringOut);

                    // Get the URL and validate it
                    string url = source.GetRecentUrl(1);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        await DisplayAlertAsync("Error",
                            $"Could not get browse URL for {source.SiteName}", "OK");
                        return;
                    }

                    // Register the fetch callback before opening the WebView
                    WebBrowsePage.OnUrlFetched = FillUrlFromWebView;

                    // Create a new instance each time to avoid NameScope conflicts
                    var webPage = new WebBrowsePage(url);

                    // Ensure the page is not cached by Shell
                    Shell.SetPresentationMode(webPage, PresentationMode.NotAnimated);

                    await Shell.Current.Navigation.PushAsync(webPage, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainPage] Source card tap error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[MainPage] Stack trace: {ex.StackTrace}");

                    await DisplayAlertAsync("Navigation Error",
                        $"Could not open {source.SiteName}:\n{ex.Message}", "OK");
                }
            })
        });

        return card;
    }

    // ── Discover: global search ────────────────────────────────────────────────

    private async void OnGlobalSearchCompleted(object sender, EventArgs e)
    {
        string query = GlobalSearchEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
            return;

        await RunGlobalSearchAsync(query);
    }

    private async void OnGlobalSearchClearTapped(object sender, TappedEventArgs e)
    {
        GlobalSearchEntry.Text = "";
        GlobalSearchClearBtn.IsVisible = false;
        ShowSourceList();
        await Task.CompletedTask;
    }

    private async Task RunGlobalSearchAsync(string query)
    {
        DiscoverSourceScrollView.IsVisible = false;
        SearchResultsView.IsVisible = false;
        SearchLoadingState.IsVisible = true;
        SearchResultsList.Children.Clear();

        try
        {
            var sourceResults = await _discoverService.SearchAllWithStatusAsync(query);

            SearchLoadingState.IsVisible = false;

            var successful = sourceResults
                .Where(r => r.IsSuccess && r.Results.Novels.Count > 0)
                .ToList();
            var failed = sourceResults
                .Where(r => !r.IsSuccess)
                .ToList();

            if (successful.Count == 0 && failed.Count == 0)
            {
                SearchResultsLabel.Text = $"No results for \"{query}\"";
                SearchResultsView.IsVisible = true;
                return;
            }

            int total = successful.Sum(r => r.Results.Novels.Count);
            SearchResultsLabel.Text = $"{total} result{(total == 1 ? "" : "s")} for \"{query}\"";

            foreach (var result in successful)
            {
                var source = result.Source;
                var page = result.Results;
                SearchResultsList.Children.Add(BuildSearchSourceHeader(source, page.Novels.Count));

                var shown = page.Novels.Take(5).ToList();
                SearchResultsList.Children.Add(BuildSearchResultsGrid(source, shown));

                if (page.Novels.Count > 5 || page.HasNextPage)
                    SearchResultsList.Children.Add(BuildSeeAllButton(source, query));
            }

            foreach (var fail in failed)
            {
                SearchResultsList.Children.Add(BuildSearchSourceHeader(fail.Source, 0));
                SearchResultsList.Children.Add(BuildSourceUnavailableRow(fail.Source, fail.ErrorMessage, query));
            }

            SearchResultsView.IsVisible = true;
        }
        catch (Exception ex)
        {
            SearchLoadingState.IsVisible = false;
            SearchResultsLabel.Text = $"Search failed: {ex.Message}";
            SearchResultsView.IsVisible = true;
        }
    }

    private void ShowSourceList()
    {
        SearchLoadingState.IsVisible = false;
        SearchResultsView.IsVisible = false;
        DiscoverSourceScrollView.IsVisible = true;
    }

    private View BuildSearchSourceHeader(IBrowsableAdapter source, int count)
    {
        var label = new Label
        {
            Text = $"{source.SiteName}  ·  {count} result{(count == 1 ? "" : "s")}",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(4, 8, 0, 4),
        };
        label.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        return label;
    }

    private View BuildSourceUnavailableRow(IBrowsableAdapter source, string? errorMessage, string query)
    {
        bool likelyCloudflare =
            source.RequiresCfBypass ||
            (!string.IsNullOrWhiteSpace(errorMessage) &&
             (errorMessage.Contains("403", StringComparison.OrdinalIgnoreCase) ||
              errorMessage.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ||
              errorMessage.Contains("forbidden", StringComparison.OrdinalIgnoreCase)));

        string primary = likelyCloudflare
            ? $"Source temporarily blocked ({source.SiteName})"
            : $"Source unavailable right now ({source.SiteName})";
        string secondary = likelyCloudflare
            ? "Cloudflare/site protection blocked this request. You can retry."
            : "The source failed to respond. Tap to retry.";

        var msg = new Label
        {
            Text = primary,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold
        };
        msg.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var detail = new Label
        {
            Text = string.IsNullOrWhiteSpace(errorMessage) ? secondary : $"{secondary}\n{errorMessage}",
            FontSize = 10,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        detail.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var retryLabel = new Label
        {
            Text = "Retry",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        retryLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        Border box = new Border();

        var retryBtn = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HeightRequest = 28,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Start,
            Content = retryLabel
        };
        retryBtn.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        retryBtn.SetDynamicResource(Border.StrokeProperty, "AccentLight");
        retryBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await retryBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await retryBtn.ScaleToAsync(1.0, 70, Easing.SpringOut);
                await RetrySingleSourceAsync(source, query, box);
            })
        });

        box = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { msg, detail, retryBtn }
            }
        };
        box.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        box.SetDynamicResource(Border.StrokeProperty, "Stroke");
        return box;
    }

    private async Task RetrySingleSourceAsync(IBrowsableAdapter source, string query, View currentRow)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        int rowIndex = SearchResultsList.Children.IndexOf(currentRow);
        if (rowIndex < 0)
            return;

        var result = await _discoverService.SearchSourceWithStatusAsync(source, query);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (rowIndex >= SearchResultsList.Children.Count)
                return;

            SearchResultsList.Children.RemoveAt(rowIndex);
            if (result.IsSuccess && result.Results.Novels.Count > 0)
            {
                var shown = result.Results.Novels.Take(5).ToList();
                SearchResultsList.Children.Insert(rowIndex, BuildSearchResultsGrid(source, shown));

                if (result.Results.Novels.Count > 5 || result.Results.HasNextPage)
                    SearchResultsList.Children.Insert(rowIndex + 1, BuildSeeAllButton(source, query));
            }
            else
            {
                SearchResultsList.Children.Insert(rowIndex,
                    BuildSourceUnavailableRow(source, result.ErrorMessage, query));
            }
        });
    }

    private Grid BuildSearchResultsGrid(IBrowsableAdapter source, List<NovelEntry> novels)
    {
        var twoColGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 8,
            RowSpacing = 8,
        };

        for (int i = 0; i < novels.Count; i++)
        {
            int row = i / 2;
            int col = i % 2;
            if (twoColGrid.RowDefinitions.Count <= row)
                twoColGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            twoColGrid.Add(BuildSearchResultCard(source, novels[i]), col, row);
        }

        return twoColGrid;
    }

    private View BuildSearchResultCard(IBrowsableAdapter source, NovelEntry novel)
    {
        const double coverWidth = 44;
        const double coverHeight = 62;
        bool suppressCardTap = false;

        View coverView;
        if (!string.IsNullOrWhiteSpace(novel.CoverUrl) &&
            Uri.TryCreate(novel.CoverUrl, UriKind.Absolute, out var coverUri))
        {
            var img = new Image
            {
                Source = ImageSource.FromUri(coverUri),
                Aspect = Aspect.AspectFill,
                WidthRequest = coverWidth,
                HeightRequest = coverHeight
            };
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 },
                WidthRequest = coverWidth,
                HeightRequest = coverHeight,
                Content = img,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }
        else
        {
            var lilyImg = new Image
            {
                Source = ImageSource.FromFile("lily.png"),
                Aspect = Aspect.AspectFit,
                WidthRequest = 26,
                HeightRequest = 26,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0.45,
            };
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 },
                WidthRequest = coverWidth,
                HeightRequest = coverHeight,
                Content = lilyImg,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
        }

        var titleLbl = new Label
        {
            Text = novel.Title,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2
        };
        titleLbl.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        string authorText = string.IsNullOrWhiteSpace(novel.Author) ? "Author: Unknown" : $"Author: {novel.Author}";
        var authorLbl = new Label
        {
            Text = authorText,
            FontSize = 10,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        authorLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        string chapterText = $"Chapter: {GetChapterSummary(novel)}";
        var chapterLbl = new Label
        {
            Text = chapterText,
            FontSize = 10,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        chapterLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var dlIcon = new Label
        {
            Text = "\uF090",
            FontFamily = "MaterialSymbols",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        dlIcon.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");

        var dlBtn = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            WidthRequest = 24,
            HeightRequest = 24,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
            Content = dlIcon,
        };
        dlBtn.SetDynamicResource(Border.BackgroundColorProperty, "Accent");
        dlBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                suppressCardTap = true;
                await dlBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await dlBtn.ScaleToAsync(1.0, 70, Easing.SpringOut);
                DownloadManager.Instance.Enqueue(novel.Url, 0,
                    string.IsNullOrWhiteSpace(novel.CoverUrl) ? null : novel.CoverUrl);
                if (Shell.Current != null)
                    await Shell.Current.GoToAsync("//DownloadsPage");
                await Task.Delay(80);
                suppressCardTap = false;
            })
        });

        bool isBookmarked = BookmarkService.Instance.IsBookmarked(novel.Url, source.SiteName);

        if (isBookmarked)
        {
            var savedBadge = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
                Padding = new Thickness(3, 1),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 2, 0),
                Content = new Label
                {
                    Text = "\uE866",
                    FontFamily = "MaterialSymbols",
                    FontSize = 9,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            savedBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            ((Label)savedBadge.Content).SetDynamicResource(Label.TextColorProperty, "AccentLight");

            var coverGrid = new Grid();
            coverGrid.Add(coverView);
            coverGrid.Add(savedBadge);
            coverView = coverGrid;
        }

        var bmIcon = new Label
        {
            Text = isBookmarked ? "\uE866" : "\uE867",
            FontFamily = "MaterialSymbols",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        bmIcon.SetDynamicResource(Label.TextColorProperty, isBookmarked ? "AccentLight" : "TextSecondary");

        var bmBtn = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            WidthRequest = 24,
            HeightRequest = 24,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
            Content = bmIcon,
        };
        bmBtn.SetDynamicResource(Border.BackgroundColorProperty, isBookmarked ? "AccentContainer" : "BgCard");
        bmBtn.SetDynamicResource(Border.StrokeProperty, isBookmarked ? "AccentLight" : "Stroke");
        bmBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                suppressCardTap = true;
                await bmBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await bmBtn.ScaleToAsync(1.0, 70, Easing.SpringOut);

                if (BookmarkService.Instance.IsBookmarked(novel.Url, source.SiteName))
                {
                    BookmarkService.Instance.RemoveBookmark(novel.Url);
                    bmIcon.Text = "\uE867";
                    bmIcon.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
                    bmBtn.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
                    bmBtn.SetDynamicResource(Border.StrokeProperty, "Stroke");
                    await ShowDiscoverBookmarkBannerAsync($"Removed: {novel.Title}");
                }
                else
                {
                    int knownCount = TryExtractChapterCount(novel);
                    BookmarkService.Instance.AddBookmark(
                        novel.Url,
                        novel.Title,
                        novel.Author ?? "Unknown",
                        source.SiteName,
                        knownCount,
                        novel.CoverUrl);
                    // If the listing didn't include a chapter count, fetch it in the background
                    if (knownCount == 0)
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                int n = await _discoverService.GetChapterCountAsync(novel.Url);
                                if (n > 0)
                                    BookmarkService.Instance.UpdateBookmarkChapterCount(novel.Url, n);
                            }
                            catch { }
                        });
                    bmIcon.Text = "\uE866";
                    bmIcon.SetDynamicResource(Label.TextColorProperty, "AccentLight");
                    bmBtn.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
                    bmBtn.SetDynamicResource(Border.StrokeProperty, "AccentLight");
                    await ShowDiscoverBookmarkBannerAsync($"Saved: {novel.Title}");
                }

                await Task.Delay(80);
                suppressCardTap = false;
            })
        });

        var actionRow = new HorizontalStackLayout
        {
            Spacing = 4,
            Children = { dlBtn, bmBtn }
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLbl, authorLbl, chapterLbl, actionRow }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 8,
            Padding = new Thickness(10),
        };
        grid.Add(coverView, 0, 0);
        grid.Add(textStack, 1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(0),
            Content = grid,
            HorizontalOptions = LayoutOptions.Fill,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                if (suppressCardTap)
                    return;
                await card.ScaleToAsync(0.97, 80, Easing.CubicOut);
                await card.ScaleToAsync(1.0, 80, Easing.SpringOut);
                await Shell.Current.Navigation.PushAsync(new WebBrowsePage(novel.Url));
            })
        });
        return card;
    }

    private async Task ShowDiscoverBookmarkBannerAsync(string message)
    {
        _discoverBannerCts?.Cancel();
        _discoverBannerCts = new CancellationTokenSource();
        var token = _discoverBannerCts.Token;

        DiscoverBookmarkBannerLabel.Text = message;
        DiscoverBookmarkBanner.IsVisible = true;
        DiscoverBookmarkBanner.Opacity = 0;
        DiscoverBookmarkBanner.TranslationY = 10;
        await Task.WhenAll(
            DiscoverBookmarkBanner.FadeToAsync(1, 160, Easing.CubicOut),
            DiscoverBookmarkBanner.TranslateToAsync(0, 0, 160, Easing.CubicOut));

        try
        {
            await Task.Delay(1800, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await Task.WhenAll(
            DiscoverBookmarkBanner.FadeToAsync(0, 150, Easing.CubicIn),
            DiscoverBookmarkBanner.TranslateToAsync(0, 8, 150, Easing.CubicIn));
        DiscoverBookmarkBanner.IsVisible = false;
    }

    private static string GetChapterSummary(NovelEntry novel)
    {
        if (novel.ChapterCount is > 0)
            return $"{novel.ChapterCount.Value}";

        if (!string.IsNullOrWhiteSpace(novel.ChapterText))
            return novel.ChapterText!;

        foreach (var value in new[] { novel.Tags, novel.Description })
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var chapterCn = Regex.Match(value, @"第\s*([0-9零一二三四五六七八九十百千万两]+)\s*章");
            if (chapterCn.Success)
                return chapterCn.Value.Replace(" ", "");

            var chapterEn = Regex.Match(value, @"\b(?:chapter|ch)\.?\s*([0-9]{1,6})\b", RegexOptions.IgnoreCase);
            if (chapterEn.Success)
                return $"Chapter {chapterEn.Groups[1].Value}";
        }

        return "N/A";
    }

    private static int TryExtractChapterCount(NovelEntry novel)
    {
        foreach (var value in new[] { novel.Tags, novel.Description })
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var chapterCount = Regex.Match(value, @"\b([1-9][0-9]{0,4})\s*chapters?\b", RegexOptions.IgnoreCase);
            if (chapterCount.Success && int.TryParse(chapterCount.Groups[1].Value, out int parsed))
                return parsed;
        }

        return 0;
    }

    private View BuildSeeAllButton(IBrowsableAdapter source, string query)
    {
        var lbl = new Label
        {
            Text = $"See all results from {source.SiteName} →",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var btn = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            HeightRequest = 34,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(10, 0),
            Content = new Grid
            {
                Children = { lbl }
            },
        };
        btn.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        btn.SetDynamicResource(Border.StrokeProperty, "Stroke");

        btn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                var browsePage = new SourceBrowsePage(source, initialQuery: query);
                await Shell.Current.Navigation.PushAsync(browsePage);
            })
        });

        return btn;
    }

    // ── Download handlers (unchanged) ─────────────────────────────────────────

    private async void OnUrlPreviewTapped(object sender, TappedEventArgs e)
    {
        await AnimateButtonPress(UrlPreviewBtn);

        string url = UrlEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            await DisplayAlertAsync("Missing URL", "Please enter a novel URL first.", "OK");
            return;
        }

        // Show loading state
        PreviewInfoCard.IsVisible = true;
        PreviewTitle.Text = "Loading...";
        PreviewAuthor.Text = "";
        PreviewChapters.Text = "";

        try
        {
            var service = new BookService(new Platform.WebViewCloudflareBypass());

            // Fetch just the index page to get title, author, and chapter count
            var book = await Task.Run(async () =>
            {
                return await service.GatherBookInfo(url, 0, null,
                    msg => { /* ignore log messages */ },
                    CancellationToken.None, 0);
            });

            // Display the preview info
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PreviewTitle.Text = book.TitleEn ?? book.Title;
                PreviewAuthor.Text = $"by {book.AuthorEn ?? book.Author}";
                PreviewChapters.Text = $"{book.Total} Chapters Available";

                // Animate the card appearance
                PreviewInfoCard.Opacity = 0;
                PreviewInfoCard.TranslationY = -10;
                _ = Task.WhenAll(
                    PreviewInfoCard.FadeToAsync(1.0, 250, Easing.CubicOut),
                    PreviewInfoCard.TranslateToAsync(0, 0, 250, Easing.CubicOut));
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                PreviewInfoCard.IsVisible = false;
                await DisplayAlertAsync("Preview Failed",
                    $"Could not fetch novel information:\n{ex.Message}", "OK");
            });
        }
    }

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
                chapters = to - from + 1;
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
        // Clear the saved draft — the user has submitted it
        Preferences.Default.Remove("draft_url");
        Preferences.Default.Remove("draft_cover");
        Preferences.Default.Set("draft_chapters", "0");
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
                        try
                        {
                            await Launcher.Default.OpenAsync(new OpenFileRequest
                            {
                                Title = "Open EPUB",
                                File = new ReadOnlyFile(existing.EpubPath, "application/epub+zip")
                            });
                        }
                        catch
                        {
                            await Share.Default.RequestAsync(new ShareFileRequest
                            {
                                Title = "Open EPUB",
                                File = new ShareFile(existing.EpubPath, "application/epub+zip")
                            });
                        }
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
