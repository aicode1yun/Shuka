using System.Collections.Specialized;
using Shuka.Android.Services;

namespace Shuka.Android.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly Dictionary<Guid, HistoryCard> _cards = new();

    public HistoryPage()
    {
        InitializeComponent();
        HistoryService.Instance.Entries.CollectionChanged += OnCollectionChanged;

        foreach (var entry in HistoryService.Instance.Entries)
            AddCard(entry);

        RefreshEmptyState();
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

            RefreshEmptyState();
        });
    }

    private void AddCard(HistoryEntry entry)
    {
        if (_cards.ContainsKey(entry.Id)) return;
        var card = BuildCard(entry);
        _cards[entry.Id] = card;
        CardList.Insert(0, card);
    }

    private async Task AddCardWithAnimationAsync(HistoryEntry entry)
    {
        if (_cards.ContainsKey(entry.Id)) return;
        var card = BuildCard(entry);
        card.Opacity = 0;
        card.TranslationY = -20;
        card.Scale = 0.95;
        _cards[entry.Id] = card;
        CardList.Insert(0, card);
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
        card.OpenRequested   += OnOpenRequested;
        card.ShareRequested  += OnShareRequested;
        card.DeleteRequested += OnDeleteRequested;
        return card;
    }

    private void RefreshEmptyState()
    {
        bool hasItems = HistoryService.Instance.Entries.Count > 0;
        EmptyState.IsVisible = !hasItems;
        ListScroll.IsVisible = hasItems;
    }

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
            await HistoryService.Instance.ClearAllAsync();
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
}
