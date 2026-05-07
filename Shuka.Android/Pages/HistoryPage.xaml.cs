using System.Collections.Specialized;
using Shuka.Android.Services;

namespace Shuka.Android.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly Dictionary<Guid, HistoryCard> _cards = new();
    private string _searchQuery = "";

    private enum SortField { Date, Title, Author }
    private SortField _sortField     = SortField.Date;
    private bool      _sortAscending = false; // date defaults to newest-first

    public HistoryPage()
    {
        InitializeComponent();
        HistoryService.Instance.Entries.CollectionChanged += OnCollectionChanged;

        foreach (var entry in HistoryService.Instance.Entries)
            AddCard(entry);

        RefreshSortPills();
        ApplyFilter();
        UpdateCountLabel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AnimateIn();
    }

    private async Task AnimateIn()
    {
        BodyGrid.Opacity = 0;
        BodyGrid.TranslationY = 18;
        await Task.WhenAll(
            BodyGrid.FadeToAsync(1.0, 220, Easing.CubicOut),
            BodyGrid.TranslateToAsync(0, 0, 220, Easing.CubicOut));
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
        CardList.Add(card); // order managed by RebuildCardOrder
    }

    private async Task AddCardWithAnimationAsync(HistoryEntry entry)
    {
        if (_cards.ContainsKey(entry.Id)) return;
        var card = BuildCard(entry);
        card.Opacity = 0;
        card.TranslationY = -20;
        card.Scale = 0.95;
        _cards[entry.Id] = card;
        CardList.Add(card);
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
        CardList.Remove(card);
        _cards.Remove(entry.Id);
    }

    private HistoryCard BuildCard(HistoryEntry entry)
    {
        var card = new HistoryCard(entry);
        card.OpenRequested        += OnOpenRequested;
        card.ShareRequested       += OnShareRequested;
        card.DeleteRequested      += OnDeleteRequested;
        card.RedownloadRequested  += OnRedownloadRequested;
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

        // Rebuild card order in CardList to match sorted order
        CardList.Clear();
        foreach (var entry in filtered)
        {
            if (_cards.TryGetValue(entry.Id, out var card))
                CardList.Add(card);
        }

        int visibleCount = filtered.Count();

        EmptyState.IsVisible     = false;
        ListScroll.IsVisible     = visibleCount > 0;
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
            "Clear Library",
            "Remove all novels from your library? EPUB files on disk are not deleted.",
            "Clear", "Cancel");

        if (confirm)
        {
            SearchEntry.Text = "";
            await HistoryService.Instance.ClearAllAsync();
        }
    }

    private async void OnOpenRequested(HistoryEntry entry)
    {
        if (entry.EpubPath == null || !File.Exists(entry.EpubPath))
        {
            await DisplayAlertAsync("File Not Found",
                "The EPUB file could not be found. It may have been moved or deleted.", "OK");
            return;
        }
        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Open EPUB",
                File  = new ReadOnlyFile(entry.EpubPath, "application/epub+zip")
            });
        }
        catch
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Open EPUB",
                File  = new ShareFile(entry.EpubPath, "application/epub+zip")
            });
        }
    }

    private async void OnShareRequested(HistoryEntry entry)
    {
        if (entry.EpubPath == null || !File.Exists(entry.EpubPath)) return;
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Share EPUB",
            File  = new ShareFile(entry.EpubPath, "application/epub+zip")
        });
    }

    private async void OnDeleteRequested(HistoryEntry entry)
    {
        bool confirm = await DisplayAlertAsync(
            "Remove from Library",
            $"Remove \"{entry.Title}\" from your library? The EPUB file on disk is not deleted.",
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
}
