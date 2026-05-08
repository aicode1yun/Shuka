using Shuka.Core;
using Shuka.Android.Platform;

namespace Shuka.Android.Pages;

public partial class SourceBrowsePage : ContentPage
{
    private readonly IBrowsableAdapter _source;
    private readonly DiscoverService   _service;

    private enum BrowseMode { Recent, Popular, Search }
    private BrowseMode _mode    = BrowseMode.Recent;
    private int        _page    = 1;
    private bool       _loading = false;
    private bool       _hasMore = true;
    private string     _query   = "";

    public SourceBrowsePage(IBrowsableAdapter source, string? initialQuery = null)
    {
        InitializeComponent();
        _source  = source;
        _service = new DiscoverService(new WebViewCloudflareBypass());

        TitleLabel.Text = source.SiteName;
        SearchEntry.TextChanged += (_, e) =>
            SearchClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            _query = initialQuery;
            _mode  = BrowseMode.Search;
            SearchEntry.Text = initialQuery;
            SearchClearBtn.IsVisible = true;
        }

        RefreshPills();
        _ = LoadPageAsync(reset: true);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async void OnBackTapped(object sender, TappedEventArgs e)
        => await Shell.Current.Navigation.PopAsync();

    // ── Filter pills ──────────────────────────────────────────────────────────

    private async void OnRecentTapped(object sender, TappedEventArgs e)
    {
        if (_mode == BrowseMode.Recent) return;
        _mode = BrowseMode.Recent;
        _query = "";
        SearchEntry.Text = "";
        RefreshPills();
        await LoadPageAsync(reset: true);
    }

    private async void OnPopularTapped(object sender, TappedEventArgs e)
    {
        if (_mode == BrowseMode.Popular) return;
        _mode = BrowseMode.Popular;
        _query = "";
        SearchEntry.Text = "";
        RefreshPills();
        await LoadPageAsync(reset: true);
    }

    private void RefreshPills()
    {
        SetPillActive(PillRecent,  _mode == BrowseMode.Recent);
        SetPillActive(PillPopular, _mode == BrowseMode.Popular);
    }

    private void SetPillActive(Border pill, bool active)
    {
        if (active)
        {
            pill.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            pill.SetDynamicResource(Border.StrokeProperty, "AccentLight");
            if (pill.Content is Label lbl)
                lbl.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        }
        else
        {
            pill.SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
            pill.SetDynamicResource(Border.StrokeProperty, "Stroke");
            if (pill.Content is Label lbl)
                lbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async void OnSearchCompleted(object sender, EventArgs e)
    {
        string q = SearchEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(q)) return;
        _query = q;
        _mode  = BrowseMode.Search;
        RefreshPills();
        await LoadPageAsync(reset: true);
    }

    private async void OnSearchClearTapped(object sender, TappedEventArgs e)
    {
        SearchEntry.Text = "";
        _query = "";
        SearchClearBtn.IsVisible = false;
        _mode = BrowseMode.Recent;
        RefreshPills();
        await LoadPageAsync(reset: true);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private async Task LoadPageAsync(bool reset = false)
    {
        if (_loading) return;
        if (!reset && !_hasMore) return;

        _loading = true;

        if (reset)
        {
            _page = 1;
            _hasMore = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                NovelList.Children.Clear();
                LoadingState.IsVisible = true;
                EmptyState.IsVisible   = false;
                ListScroll.IsVisible   = false;
            });
        }

        try
        {
            ListingPage result = _mode switch
            {
                BrowseMode.Popular => await _service.GetPopularAsync(_source, _page),
                BrowseMode.Search  => await _service.SearchAsync(_source, _query, _page),
                _                  => await _service.GetRecentAsync(_source, _page),
            };

            _hasMore = result.HasNextPage;
            _page++;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingState.IsVisible = false;

                if (result.Novels.Count == 0 && reset)
                {
                    EmptyState.IsVisible = true;
                    ListScroll.IsVisible = false;
                    return;
                }

                ListScroll.IsVisible = true;
                EmptyState.IsVisible = false;

                foreach (var novel in result.Novels)
                    NovelList.Children.Add(BuildNovelCard(novel));

                // Load more button if there are more pages
                if (_hasMore)
                {
                    var loadMoreBtn = BuildLoadMoreButton();
                    NovelList.Children.Add(loadMoreBtn);
                }
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingState.IsVisible = false;
                EmptyState.IsVisible   = NovelList.Children.Count == 0;
                ListScroll.IsVisible   = NovelList.Children.Count > 0;
                if (NovelList.Children.Count == 0)
                {
                    var errLabel = new Label
                    {
                        Text              = $"Failed to load: {ex.Message}",
                        FontSize          = 12,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin            = new Thickness(16),
                    };
                    errLabel.SetDynamicResource(Label.TextColorProperty, "Danger");
                    NovelList.Children.Add(errLabel);
                    ListScroll.IsVisible = true;
                    EmptyState.IsVisible = false;
                }
            });
        }
        finally
        {
            _loading = false;
        }
    }

    // ── Novel card ────────────────────────────────────────────────────────────

    private View BuildNovelCard(NovelEntry novel)
    {
        // Cover
        View coverView;
        if (!string.IsNullOrWhiteSpace(novel.CoverUrl) &&
            Uri.TryCreate(novel.CoverUrl, UriKind.Absolute, out var coverUri))
        {
            var img = new Image
            {
                Source            = ImageSource.FromUri(coverUri),
                Aspect            = Aspect.AspectFill,
                WidthRequest      = 64,
                HeightRequest     = 92,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest    = 64,
                HeightRequest   = 92,
                Content         = img,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }
        else
        {
            var ph = new Label
            {
                Text              = "\uEA78",
                FontFamily        = "MaterialSymbols",
                FontSize          = 28,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            ph.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                WidthRequest    = 64,
                HeightRequest   = 92,
                Content         = ph,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }

        var titleLbl = new Label
        {
            Text          = novel.Title,
            FontSize      = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines      = 2,
        };
        titleLbl.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var authorLbl = new Label
        {
            Text      = novel.Author ?? "",
            FontSize  = 12,
            IsVisible = !string.IsNullOrWhiteSpace(novel.Author),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines  = 1,
        };
        authorLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var descLbl = new Label
        {
            Text      = novel.Description ?? "",
            FontSize  = 11,
            IsVisible = !string.IsNullOrWhiteSpace(novel.Description),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines  = 2,
        };
        descLbl.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // Download button
        var dlIcon = new Label
        {
            Text            = "\uF090",
            FontFamily      = "MaterialSymbols",
            FontSize        = 14,
            VerticalOptions = LayoutOptions.Center,
            Margin          = new Thickness(0, 0, 4, 0),
        };
        dlIcon.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");
        var dlText = new Label
        {
            Text            = "Download",
            FontSize        = 11,
            FontAttributes  = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        dlText.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");

        var dlBtn = new Border
        {
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HeightRequest   = 32,
            Padding         = new Thickness(10, 0),
            HorizontalOptions = LayoutOptions.Start,
            Content         = new HorizontalStackLayout
            {
                Spacing           = 0,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Children          = { dlIcon, dlText }
            },
        };
        dlBtn.SetDynamicResource(Border.BackgroundColorProperty, "Accent");
        dlBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await dlBtn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await dlBtn.ScaleToAsync(1.0,  70, Easing.SpringOut);
                OnDownloadTapped(novel);
            })
        });

        var textStack = new VerticalStackLayout
        {
            Spacing         = 4,
            VerticalOptions = LayoutOptions.Center,
            Children        = { titleLbl, authorLbl, descLbl, dlBtn }
        };

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 12,
            Padding       = new Thickness(14),
        };
        contentGrid.Add(coverView,  0, 0);
        contentGrid.Add(textStack,  1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding         = new Thickness(0),
            Content         = contentGrid,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");

        return card;
    }

    private View BuildLoadMoreButton()
    {
        var lbl = new Label
        {
            Text              = "Load more",
            FontSize          = 13,
            FontAttributes    = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "AccentLight");

        var btn = new Border
        {
            StrokeThickness   = 1,
            StrokeShape       = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            HeightRequest     = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Content           = lbl,
        };
        btn.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        btn.SetDynamicResource(Border.StrokeProperty, "Stroke");

        btn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                // Remove this button, load next page
                NovelList.Children.Remove(btn);
                await LoadPageAsync(reset: false);
            })
        });

        return btn;
    }

    private void OnDownloadTapped(NovelEntry novel)
    {
        // Pre-fill the Home tab URL entry and switch to it
        Services.DownloadManager.Instance.Enqueue(novel.Url, 0,
            string.IsNullOrWhiteSpace(novel.CoverUrl) ? null : novel.CoverUrl);

        // Navigate to Downloads tab
        if (Shell.Current != null)
            _ = Shell.Current.GoToAsync("//DownloadsPage");
    }
}
