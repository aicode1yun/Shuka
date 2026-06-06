using System.Collections.Specialized;
using Shuka.Android.Behaviors;
using Shuka.Android.Platforms.Android;
using Shuka.Android.Services;

namespace Shuka.Android.Pages;

public partial class HistoryPage : ContentPage
{
    private const string PrefKeyViewMode = "history_view_mode";

    private readonly Dictionary<Guid, HistoryCard> _cards = new();
    private string _searchQuery = "";

    private enum SortField { Date, Title, Author }
    private SortField _sortField     = SortField.Date;
    private bool      _sortAscending = false; // date defaults to newest-first

    private bool _isOptionsSheetOpen;
    private HistoryEntry? _activeOptionsEntry;
    private bool _isCompactView;
    private double _lastWidth = -1;

    public HistoryPage()
    {
        InitializeComponent();
        HistoryService.Instance.Entries.CollectionChanged += OnCollectionChanged;

        _isCompactView = Preferences.Default.Get(PrefKeyViewMode, false);
        RefreshToggleViewPill();

        RebuildCards();

        RefreshSortPills();
        ApplyFilter();
        UpdateCountLabel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MainActivity.Instance?.SetTabBarVisible(true);
        TabTransition.Prepare(BodyGrid, myTabIndex: 2);
        await TabTransition.SlideInAsync(BodyGrid);
    }

    private async Task AnimateIn()
    {
        BodyGrid.Opacity      = 1;
        BodyGrid.TranslationY = 0;
        await Task.CompletedTask;
    }

    // ── Collection changes ────────────────────────────────────────────────────

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (e.NewItems != null)
                foreach (HistoryEntry entry in e.NewItems)
                    await AddCardWithAnimationAsync(entry);

            if (e.OldItems != null)
                foreach (HistoryEntry entry in e.OldItems)
                    await RemoveCardWithAnimationAsync(entry);

            ApplyFilter();
            UpdateCountLabel();
        });
    }

    private void AddCard(HistoryEntry entry)
    {
        if (_cards.ContainsKey(entry.Id)) return;
        var card = BuildCard(entry);
        _cards[entry.Id] = card;

        ApplyFilter();
    }

    private async Task AddCardWithAnimationAsync(HistoryEntry entry)
    {
        if (_cards.ContainsKey(entry.Id)) return;
        var card = BuildCard(entry);
        card.Opacity = 0;
        card.TranslationY = -20;
        card.Scale = 0.95;
        _cards[entry.Id] = card;

        ApplyFilter();

        await Task.WhenAll(
            card.FadeToAsync(1.0, 350, Easing.CubicOut),
            card.TranslateToAsync(0, 0, 350, Easing.CubicOut),
            card.ScaleToAsync(1.0, 350, Easing.CubicOut));
    }

    private async Task RemoveCardWithAnimationAsync(HistoryEntry entry)
    {
        if (!_cards.TryGetValue(entry.Id, out var card)) return;
        await Task.WhenAll(
            card.FadeToAsync(0, 250, Easing.CubicIn),
            card.ScaleToAsync(0.9, 250, Easing.CubicIn));
        _cards.Remove(entry.Id);
        ApplyFilter();
    }

    private HistoryCard BuildCard(HistoryEntry entry)
    {
        var card = new HistoryCard(entry, _isCompactView);
        card.OpenRequested    += OnOpenRequested;
        card.OptionsRequested += OnOptionsRequested;
        return card;
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    private void OnSortDateTapped(object sender, TappedEventArgs e)
    {
        if (_sortField == SortField.Date)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField     = SortField.Date;
            _sortAscending = false; // newest first by default
        }
        RefreshSortPills();
        ApplyFilter();
    }

    private void OnSortTitleTapped(object sender, TappedEventArgs e)
    {
        if (_sortField == SortField.Title)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField     = SortField.Title;
            _sortAscending = true; // A→Z by default
        }
        RefreshSortPills();
        ApplyFilter();
    }

    private void OnSortAuthorTapped(object sender, TappedEventArgs e)
    {
        if (_sortField == SortField.Author)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField     = SortField.Author;
            _sortAscending = true; // A→Z by default
        }
        RefreshSortPills();
        ApplyFilter();
    }

    private void RefreshSortPills()
    {
        SetPill(SortDatePill,   SortDateIcon,   SortDateLabel,   SortDateArrow,   SortField.Date);
        SetPill(SortTitlePill,  SortTitleIcon,  SortTitleLabel,  SortTitleArrow,  SortField.Title);
        SetPill(SortAuthorPill, SortAuthorIcon, SortAuthorLabel, SortAuthorArrow, SortField.Author);
    }

    private void SetPill(Border pill, Label icon, Label label, Label arrow, SortField field)
    {
        bool active = _sortField == field;

        if (active)
        {
            pill.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            pill.SetDynamicResource(Border.StrokeProperty, "AccentLight");
            icon.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            label.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            arrow.IsVisible = true;
            arrow.Text = _sortAscending ? "\uE5C7" : "\uE5C5"; // arrow_drop_up / arrow_drop_down
            arrow.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        }
        else
        {
            pill.SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
            pill.SetDynamicResource(Border.StrokeProperty, "Stroke");
            icon.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            label.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            arrow.IsVisible = false;
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = e.NewTextValue?.Trim() ?? "";
        ClearSearchBtn.IsVisible = !string.IsNullOrEmpty(_searchQuery);
        ApplyFilter();
    }

    private void OnClearSearchTapped(object sender, TappedEventArgs e)
    {
        SearchEntry.Text = "";
        _searchQuery = "";
        ClearSearchBtn.IsVisible = false;
        ApplyFilter();
    }

    // ── Filter + Sort ─────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        bool hasEntries  = HistoryService.Instance.Entries.Count > 0;
        bool isSearching = !string.IsNullOrEmpty(_searchQuery);

        if (!hasEntries)
        {
            EmptyState.IsVisible     = true;
            NoResultsState.IsVisible = false;
            ListScroll.IsVisible     = false;
            GridScroll.IsVisible     = false;
            return;
        }

        // Get sorted + filtered entries
        var sorted = GetSortedEntries();
        var filtered = isSearching
            ? sorted.Where(e =>
                e.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                e.Author.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
              .ToList()
            : sorted;

        double width = _lastWidth;
        if (width <= 0)
        {
            width = DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;
        }

        double cardWidth = 80;
        double cardHeight = 120;
        if (width > 48)
        {
            cardWidth = (width - 48) / 4;
            cardHeight = cardWidth * 1.5;
        }

        // Rebuild card order in CardList and CardGrid to match sorted order
        CardList.Clear();
        CardGrid.Children.Clear();
        CardGrid.RowDefinitions.Clear();

        int index = 0;
        foreach (var entry in filtered)
        {
            if (_cards.TryGetValue(entry.Id, out var card))
            {
                if (_isCompactView)
                {
                    card.WidthRequest = cardWidth;
                    card.HeightRequest = cardHeight;

                    int row = index / 4;
                    int col = index % 4;

                    if (col == 0)
                    {
                        CardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    }

                    Grid.SetColumn(card, col);
                    Grid.SetRow(card, row);
                    CardGrid.Children.Add(card);
                }
                else
                {
                    card.WidthRequest = -1;
                    card.HeightRequest = -1;
                    CardList.Add(card);
                }
                index++;
            }
        }

        int visibleCount = filtered.Count();

        EmptyState.IsVisible     = false;
        ListScroll.IsVisible     = visibleCount > 0 && !_isCompactView;
        GridScroll.IsVisible     = visibleCount > 0 && _isCompactView;
        NoResultsState.IsVisible = visibleCount == 0;

        if (visibleCount == 0 && isSearching)
            NoResultsLabel.Text = $"No results for \"{_searchQuery}\"";
    }

    private IEnumerable<HistoryEntry> GetSortedEntries()
    {
        var entries = HistoryService.Instance.Entries.AsEnumerable();
        return (_sortField, _sortAscending) switch
        {
            (SortField.Date,   false) => entries.OrderByDescending(e => e.CompletedAt),
            (SortField.Date,   true)  => entries.OrderBy(e => e.CompletedAt),
            (SortField.Title,  true)  => entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
            (SortField.Title,  false) => entries.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
            (SortField.Author, true)  => entries.OrderBy(e => e.Author, StringComparer.OrdinalIgnoreCase),
            (SortField.Author, false) => entries.OrderByDescending(e => e.Author, StringComparer.OrdinalIgnoreCase),
            _                         => entries.OrderByDescending(e => e.CompletedAt),
        };
    }

    private void RefreshEmptyState() => ApplyFilter();

    private void UpdateCountLabel()
    {
        int total = HistoryService.Instance.Entries.Count;
        CountLabel.Text = total == 0
            ? "Your downloaded novels"
            : total == 1 ? "1 novel" : $"{total} novels";
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async void OnClearAllTapped(object sender, TappedEventArgs e)
    {
        var btn = (Border)sender;
        await btn.ScaleToAsync(0.95, 80, Easing.CubicOut);
        await btn.ScaleToAsync(1.0, 80, Easing.SpringOut);

        if (HistoryService.Instance.Entries.Count == 0) return;

        bool confirm = await DisplayAlertAsync(
            "Clear History",
            "Remove all novels from your history? EPUB files on disk are not deleted.",
            "Clear", "Cancel");

        if (confirm)
        {
            SearchEntry.Text = "";
            await HistoryService.Instance.ClearAllAsync();
        }
    }

    private async void OnOpenRequested(HistoryEntry entry)
    {
        try
        {
            if (entry == null)
            {
                await DisplayAlertAsync("Error", "Invalid history entry.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.EpubPath))
            {
                await DisplayAlertAsync("File Not Found",
                    "No EPUB file path available for this novel.", "OK");
                return;
            }

            if (!EpubOpener.IsAccessible(entry.EpubPath))
            {
                await DisplayAlertAsync("File Not Found",
                    "The EPUB file could not be found. It may have been moved or deleted.", "OK");
                return;
            }

            try
            {
                EpubOpener.Open(entry.EpubPath);
            }
            catch (InvalidOperationException)
            {
                // No EPUB reader installed — fall back to share sheet
                try
                {
                    EpubOpener.Share(entry.EpubPath, entry.Title);
                }
                catch
                {
                    await DisplayAlertAsync("No EPUB Reader",
                        "No EPUB reader app is installed. Install one from the Play Store and try again.",
                        "OK");
                }
            }
            catch (FileNotFoundException)
            {
                await DisplayAlertAsync("File Not Found",
                    "The EPUB file could not be found. It may have been moved or deleted.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HistoryPage] OnOpenRequested error: {ex.Message}");

            // Last resort: try native share
            try
            {
                if (EpubOpener.IsAccessible(entry?.EpubPath) && entry?.EpubPath is string path)
                    EpubOpener.Share(path, entry.Title);
            }
            catch
            {
                await DisplayAlertAsync("Error",
                    "Could not open or share the EPUB file.", "OK");
            }
        }
    }

    private async void OnShareRequested(HistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.EpubPath)) return;
        if (!EpubOpener.IsAccessible(entry.EpubPath)) return;

        try
        {
            EpubOpener.Share(entry.EpubPath, entry.Title);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HistoryPage] Share failed: {ex.Message}");
            await DisplayAlertAsync("Share Failed",
                "Could not share the EPUB file.", "OK");
        }
    }

    private async void OnDeleteRequested(HistoryEntry entry)
    {
        bool confirm = await DisplayAlertAsync(
            "Remove from History",
            $"Remove \"{entry.Title}\" from your history? The EPUB file on disk is not deleted.",
            "Remove", "Cancel");

        if (confirm)
            await HistoryService.Instance.RemoveAsync(entry);
    }

    private async void OnRedownloadRequested(HistoryEntry entry)
    {
        bool confirm = await DisplayAlertAsync(
            "Re-download",
            $"Re-download \"{entry.Title}\"?\n\nThis will queue a new download using the original URL.",
            "Download", "Cancel");

        if (!confirm) return;

        // Check for an existing active download for this URL
        var existing = DownloadManager.Instance.FindExisting(entry.Url);
        if (existing != null && existing.IsRunning)
        {
            await DisplayAlertAsync("Already Downloading",
                "This novel is already in the download queue.", "OK");
            return;
        }

        // Enqueue via DownloadManager — same as tapping Download on the Home tab
        DownloadManager.Instance.Enqueue(entry.Url, entry.ChapterCount,
            string.IsNullOrWhiteSpace(entry.CoverUrl) ? null : entry.CoverUrl);

        // Navigate to Downloads tab so the user can watch progress
        if (Shell.Current != null)
            await Shell.Current.GoToAsync("//DownloadsPage");
    }

    // ── Options sheet ─────────────────────────────────────────────────────────

    private async void OnOptionsRequested(HistoryEntry entry)
    {
        await ShowOptionsSheetAsync(entry);
    }

    private async Task ShowOptionsSheetAsync(HistoryEntry entry)
    {
        if (_isOptionsSheetOpen || entry == null)
            return;

        _isOptionsSheetOpen = true;
        _activeOptionsEntry = entry;
        OptionsSheetSubtitle.Text = entry.Title;

        bool fileExists = EpubOpener.IsAccessible(entry.EpubPath);
        OptionsSheetShareBtn.IsVisible = fileExists;
        OptionsSheetRedownloadBtn.IsVisible = !fileExists;

        OptionsSheetOverlay.IsVisible = true;
        OptionsSheetOverlay.Opacity = 0;
        OptionsSheet.Opacity = 0;
        OptionsSheet.TranslationY = 28;

        UpdateSheetBottomMargins();

        await Task.WhenAll(
            OptionsSheetOverlay.FadeToAsync(1, 160, Easing.CubicOut),
            OptionsSheet.FadeToAsync(1, 180, Easing.CubicOut),
            OptionsSheet.TranslateToAsync(0, 0, 180, Easing.CubicOut));
    }

    private async Task HideOptionsSheetAsync()
    {
        if (!_isOptionsSheetOpen)
            return;

        _isOptionsSheetOpen = false;
        await Task.WhenAll(
            OptionsSheet.FadeToAsync(0, 140, Easing.CubicIn),
            OptionsSheet.TranslateToAsync(0, 24, 140, Easing.CubicIn),
            OptionsSheetOverlay.FadeToAsync(0, 140, Easing.CubicIn));
        OptionsSheetOverlay.IsVisible = false;
        _activeOptionsEntry = null;
    }

    private async void OnOptionsSheetOverlayTapped(object sender, TappedEventArgs e)
    {
        await HideOptionsSheetAsync();
    }

    private void OnOptionsSheetTapped(object sender, TappedEventArgs e)
    {
        // Swallow tap so overlay handler does not close it.
    }

    private async void OnOptionsSheetCloseTapped(object sender, TappedEventArgs e)
    {
        await HideOptionsSheetAsync();
    }

    private async void OnOptionsSheetShareTapped(object sender, TappedEventArgs e)
    {
        if (_activeOptionsEntry == null) return;
        var entry = _activeOptionsEntry;
        await HideOptionsSheetAsync();
        OnShareRequested(entry);
    }

    private async void OnOptionsSheetRedownloadTapped(object sender, TappedEventArgs e)
    {
        if (_activeOptionsEntry == null) return;
        var entry = _activeOptionsEntry;
        await HideOptionsSheetAsync();
        OnRedownloadRequested(entry);
    }

    private async void OnOptionsSheetRemoveTapped(object sender, TappedEventArgs e)
    {
        if (_activeOptionsEntry == null) return;
        var entry = _activeOptionsEntry;
        await HideOptionsSheetAsync();
        OnDeleteRequested(entry);
    }

    private void UpdateSheetBottomMargins()
    {
        double bottomInset = 16;
#if ANDROID
        if (MainActivity.Instance is { } activity)
            bottomInset = Math.Max(bottomInset, activity.GetOverlayBottomInsetDip(14));
#endif

        OptionsSheet.Margin = new Thickness(12, 0, 12, bottomInset);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateSheetBottomMargins();

        if (width > 0 && Math.Abs(_lastWidth - width) > 0.1)
        {
            _lastWidth = width;
            ApplyFilter();
        }
    }

    private void RebuildCards()
    {
        _cards.Clear();
        foreach (var entry in HistoryService.Instance.Entries)
        {
            var card = BuildCard(entry);
            _cards[entry.Id] = card;
        }
    }

    private void RefreshToggleViewPill()
    {
        if (_isCompactView)
        {
            ToggleViewPill.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            ToggleViewPill.SetDynamicResource(Border.StrokeProperty, "AccentLight");
            ToggleViewIcon.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            ToggleViewIcon.Text = "\uE9B0"; // grid_view
            ToggleViewLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            ToggleViewLabel.Text = "Grid";
        }
        else
        {
            ToggleViewPill.SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
            ToggleViewPill.SetDynamicResource(Border.StrokeProperty, "Stroke");
            ToggleViewIcon.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            ToggleViewIcon.Text = "\uE8EF"; // view_list
            ToggleViewLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");
            ToggleViewLabel.Text = "List";
        }
    }

    private async void OnToggleViewTapped(object sender, TappedEventArgs e)
    {
        var btn = (Border)sender;
        await btn.ScaleToAsync(0.95, 70, Easing.CubicOut);
        await btn.ScaleToAsync(1.0, 70, Easing.SpringOut);

        _isCompactView = !_isCompactView;
        Preferences.Default.Set(PrefKeyViewMode, _isCompactView);

        RefreshToggleViewPill();
        RebuildCards();
        ApplyFilter();
    }
}
