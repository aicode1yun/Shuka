using Shuka.Core;
using Shuka.Android.Platform;

namespace Shuka.Android.Pages;

public partial class DiscoverPage : ContentPage
{
    private readonly DiscoverService _service;

    public DiscoverPage()
    {
        InitializeComponent();
        _service = new DiscoverService(new WebViewCloudflareBypass());

        GlobalSearchEntry.TextChanged += (_, e) =>
            GlobalSearchClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

        BuildSourceCards();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BodyGrid.Opacity = 0;
        BodyGrid.TranslationY = 18;
        await Task.WhenAll(
            BodyGrid.FadeToAsync(1.0, 220, Easing.CubicOut),
            BodyGrid.TranslateToAsync(0, 0, 220, Easing.CubicOut));
    }

    // ── Source cards ──────────────────────────────────────────────────────────

    private void BuildSourceCards()
    {
        SourceList.Children.Clear();
        foreach (var source in DiscoverService.Sources)
            SourceList.Children.Add(BuildSourceCard(source));
    }

    private View BuildSourceCard(IBrowsableAdapter source)
    {
        var cfBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(8, 3),
            IsVisible = source.RequiresCfBypass,
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
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
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
        row.Add(chevron, 1, 0);

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
                await card.ScaleToAsync(0.97, 80, Easing.CubicOut);
                await card.ScaleToAsync(1.0, 80, Easing.SpringOut);
                await Shell.Current.Navigation.PushAsync(new SourceBrowsePage(source));
            })
        });

        return card;
    }

    // ── Global search ─────────────────────────────────────────────────────────

    private async void OnGlobalSearchCompleted(object sender, EventArgs e)
    {
        string query = GlobalSearchEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;
        await RunGlobalSearchAsync(query);
    }

    private async void OnGlobalSearchClearTapped(object sender, TappedEventArgs e)
    {
        GlobalSearchEntry.Text = "";
        GlobalSearchClearBtn.IsVisible = false;
        ShowSourceList();
    }

    private async Task RunGlobalSearchAsync(string query)
    {
        // Show loading, hide source list and previous results
        SourceScrollView.IsVisible = false;
        SearchResultsView.IsVisible = false;
        SearchLoadingState.IsVisible = true;
        SearchResultsList.Children.Clear();

        try
        {
            var results = await _service.SearchAllWithStatusAsync(query);

            SearchLoadingState.IsVisible = false;

            int total = results.Sum(r => r.Results.Novels.Count);
            SearchResultsLabel.Text = total == 0
                ? $"No results for \"{query}\""
                : $"{total} result{(total == 1 ? "" : "s")} for \"{query}\"";

            foreach (var result in results)
            {
                // Always show the source header — even when count is 0
                SearchResultsList.Children.Add(
                    BuildSearchSourceHeader(result.Source, result.Results.Novels.Count));

                // Only attach novel cards when there are actual results
                if (result.IsSuccess && result.Results.Novels.Count > 0)
                {
                    foreach (var novel in result.Results.Novels.Take(5))
                        SearchResultsList.Children.Add(BuildSearchResultCard(novel, result.Source));

                    if (result.Results.Novels.Count > 5 || result.Results.HasNextPage)
                        SearchResultsList.Children.Add(BuildSeeAllButton(result.Source, query));
                }
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
        SourceScrollView.IsVisible = true;
    }

    // ── Search result UI ──────────────────────────────────────────────────────

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

    private View BuildSearchResultCard(NovelEntry novel, IBrowsableAdapter? source = null)
    {
        View coverView;
        if (!string.IsNullOrWhiteSpace(novel.CoverUrl) &&
            Uri.TryCreate(novel.CoverUrl, UriKind.Absolute, out var coverUri))
        {
            var img = new Image
            {
                Source = ImageSource.FromUri(coverUri),
                Aspect = Aspect.AspectFill,
                WidthRequest = 52,
                HeightRequest = 74
            };
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest = 52,
                HeightRequest = 74,
                Content = img,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }
        else
        {
            var ph = new Label
            {
                Text = "\uEA78",
                FontFamily = "MaterialSymbols",
                FontSize = 24,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            ph.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest = 52,
                HeightRequest = 74,
                Content = ph,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }

        var titleLbl = new Label
        {
            Text = novel.Title,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2
        };
        titleLbl.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var authorLbl = new Label
        {
            Text = novel.Author ?? "",
            FontSize = 11,
            IsVisible = !string.IsNullOrWhiteSpace(novel.Author),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        authorLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // Download button
        var dlIcon = new Label
        {
            Text = "\uF090",
            FontFamily = "MaterialSymbols",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        dlIcon.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");
        var dlText = new Label
        {
            Text = "Download",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        dlText.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");

        var dlBtn = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            HorizontalOptions = LayoutOptions.Start,
            Content = new HorizontalStackLayout
            {
                Spacing = 0,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { dlIcon, dlText }
            },
        };
        dlBtn.SetDynamicResource(Border.BackgroundColorProperty, "Accent");
        dlBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await dlBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await dlBtn.ScaleToAsync(1.0, 70, Easing.SpringOut);
                Services.DownloadManager.Instance.Enqueue(novel.Url, 0,
                    string.IsNullOrWhiteSpace(novel.CoverUrl) ? null : novel.CoverUrl);
                if (Shell.Current != null)
                    await Shell.Current.GoToAsync("//DownloadsPage");
            })
        });

        // Chapter count — shown immediately if the listing provided it,
        // otherwise fetched in the background (skipped for CF-bypass sources).
        var chapterLabel = new Label
        {
            FontSize = 10,
            IsVisible = false,
        };
        chapterLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        if (novel.ChapterCount.HasValue && novel.ChapterCount > 0)
        {
            chapterLabel.Text = $"{novel.ChapterCount} chapters";
            chapterLabel.IsVisible = true;
        }
        else if (source?.RequiresCfBypass != true)
        {
            // Lazy-fetch chapter count in the background
            chapterLabel.Text = "...";
            chapterLabel.IsVisible = true;
            _ = FetchAndUpdateChapterLabelAsync(novel.Url, chapterLabel);
        }

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLbl, authorLbl, chapterLabel, dlBtn }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 10,
            Padding = new Thickness(12),
        };
        grid.Add(coverView, 0, 0);
        grid.Add(textStack, 1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(0),
            Content = grid,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");
        return card;
    }

    /// <summary>
    /// Fetches the chapter count for <paramref name="url"/> in the background
    /// and updates <paramref name="label"/> on the main thread when done.
    /// </summary>
    private async Task FetchAndUpdateChapterLabelAsync(string url, Label label)
    {
        try
        {
            int count = await _service.GetChapterCountAsync(url);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (count > 0)
                {
                    label.Text = $"{count} chapters";
                    label.IsVisible = true;
                }
                else
                {
                    label.IsVisible = false;
                }
            });
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(() => label.IsVisible = false);
        }
    }

    private View BuildSeeAllButton(IBrowsableAdapter source, string query)
    {
        var lbl = new Label
        {
            Text = $"See all results from {source.SiteName} →",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var btn = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Fill,
            Content = lbl,
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
}
