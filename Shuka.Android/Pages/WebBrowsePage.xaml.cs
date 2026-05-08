using Shuka.Android.Services;
using Shuka.Core;
using Shuka.Core.Adapters;

namespace Shuka.Android.Pages;

/// <summary>
/// Full-screen WebView browser for Discover sources.
/// Shows a floating Download FAB whenever the current URL matches a known
/// novel adapter (quanben.io, czbooks.net, dmxs.org, 69shuba.com, 52shuku.net).
/// </summary>
public partial class WebBrowsePage : ContentPage
{
    // All adapters that can download a novel — used to detect if the current
    // page is a downloadable novel index.
    private static readonly ISiteAdapter[] _adapters =
    [
        new QuanbenAdapter(),
        new CzBooksAdapter(),
        new DmxsAdapter(),
        new ShubaAdapter(),
        new ShukuAdapter(),
    ];

    private string _currentUrl;
    private readonly string _homeUrl;
    private bool   _isLoading;

    public WebBrowsePage(string startUrl)
    {
        InitializeComponent();
        _currentUrl = startUrl;
        _homeUrl    = startUrl;
        Navigate(startUrl);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void Navigate(string url)
    {
        _currentUrl = url;
        UrlBarLabel.Text = url;
        SiteWebView.Source = new UrlWebViewSource { Url = url };
        UpdateDownloadFab(url);
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        if (SiteWebView.CanGoBack)
            SiteWebView.GoBack();
        else
            await Shell.Current.Navigation.PopAsync();
    }

    private void OnReloadTapped(object sender, TappedEventArgs e)
        => SiteWebView.Reload();

    private void OnHomeSourceTapped(object sender, TappedEventArgs e)
        => Navigate(_homeUrl);

    private async void OnOpenInBrowserTapped(object sender, TappedEventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri(_currentUrl)); }
        catch { /* ignore */ }
    }

    // ── WebView events ────────────────────────────────────────────────────────

    private void OnNavigating(object sender, WebNavigatingEventArgs e)
    {
        _isLoading = true;
        LoadingBar.IsVisible = true;
        LoadingBar.Progress  = 0;
        _ = AnimateLoadingBarAsync();

        _currentUrl      = e.Url;
        UrlBarLabel.Text = e.Url;
        UpdateDownloadFab(e.Url);
    }

    private void OnNavigated(object sender, WebNavigatedEventArgs e)
    {
        _isLoading = false;
        LoadingBar.IsVisible = false;
        LoadingBar.Progress  = 0;

        _currentUrl      = e.Url;
        UrlBarLabel.Text = e.Url;
        UpdateDownloadFab(e.Url);
    }

    private async Task AnimateLoadingBarAsync()
    {
        // Animate to 85% quickly, then stall until navigation completes
        await LoadingBar.ProgressTo(0.85, 1200, Easing.CubicOut);
        while (_isLoading)
            await Task.Delay(200);
        await LoadingBar.ProgressTo(1.0, 200, Easing.Linear);
    }

    // ── FAB logic ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Show the Download FAB only when the current URL is a novel page
    /// that one of our adapters can handle.
    /// </summary>
    private void UpdateDownloadFab(string url)
    {
        bool supported = _adapters.Any(a => a.Matches(url));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (FabDownload.IsVisible == supported) return;
            FabDownload.IsVisible = supported;
            if (supported)
            {
                FabDownload.Opacity      = 0;
                FabDownload.TranslationY = 20;
                _ = Task.WhenAll(
                    FabDownload.FadeToAsync(1.0, 200, Easing.CubicOut),
                    FabDownload.TranslateToAsync(0, 0, 200, Easing.CubicOut));
            }
        });
    }

    private async void OnDownloadFabTapped(object sender, TappedEventArgs e)
    {
        await FabDownload.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabDownload.ScaleToAsync(1.0,  70, Easing.SpringOut);

        string url = _currentUrl;

        // Check for duplicate
        var existing = DownloadManager.Instance.FindExisting(url);
        if (existing != null)
        {
            string title = string.IsNullOrWhiteSpace(existing.Title) || existing.Title == "Loading..."
                ? "this novel" : $"\"{existing.Title}\"";

            bool alreadyActive = existing.Status is DownloadStatus.Running or DownloadStatus.Queued;
            string message = alreadyActive
                ? $"Already downloading {title}."
                : $"{title} was already downloaded.";

            string? choice = await DisplayActionSheetAsync(message, "Stay here", null,
                "Download again", "Go to Downloads");

            if (choice == "Go to Downloads")
            {
                await Shell.Current.GoToAsync("//DownloadsPage");
                return;
            }
            if (choice != "Download again") return;

            if (existing.IsFinished)
                DownloadManager.Instance.Dismiss(existing);
        }

        DownloadManager.Instance.Enqueue(url, 0, null);
        await ShowQueuedToastAsync();
    }

    private async Task ShowQueuedToastAsync()
    {
        QueuedToastLabel.Text = "Queued for download!";
        QueuedToast.Opacity      = 0;
        QueuedToast.TranslationY = 20;
        QueuedToast.IsVisible    = true;

        await Task.WhenAll(
            QueuedToast.FadeToAsync(1.0, 250, Easing.CubicOut),
            QueuedToast.TranslateToAsync(0, 0, 250, Easing.CubicOut));

        await Task.Delay(2500);

        await Task.WhenAll(
            QueuedToast.FadeToAsync(0, 250, Easing.CubicIn),
            QueuedToast.TranslateToAsync(0, 20, 250, Easing.CubicIn));

        QueuedToast.IsVisible = false;
    }
}
