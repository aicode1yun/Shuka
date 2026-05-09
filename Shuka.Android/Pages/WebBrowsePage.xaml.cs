using Shuka.Android.Services;
using Shuka.Core.Adapters;

namespace Shuka.Android.Pages;

/// <summary>
/// Full-screen WebView browser for Discover sources.
/// Shows a floating Download FAB whenever the current URL matches a known
/// novel adapter (quanben.io, czbooks.net, dmxs.org, 69shuba.com, 52shuku.net).
/// </summary>
public partial class WebBrowsePage : ContentPage
{
    // Stricter per-site checks: is this URL a novel index page (not just the domain)?
    private static readonly Dictionary<string, Func<string, bool>> _novelPageChecks = new()
    {
        // quanben.io: /n/{bookId}/  or  /n/{bookId}/list.html
        ["quanben.io"]  = url => System.Text.RegularExpressions.Regex.IsMatch(
            url, @"quanben\.io/n/[^/?#]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase),

        // czbooks.net: /n/{bookId}  (not /new/, /hot/, /search, etc.)
        ["czbooks.net"] = url => System.Text.RegularExpressions.Regex.IsMatch(
            url, @"czbooks\.net/n/[^/?#]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase),

        // 69shuba.com: /book/{numericId}.htm
        ["69shuba.com"] = url => System.Text.RegularExpressions.Regex.IsMatch(
            url, @"69shuba\.com/book/\d+\.htm", System.Text.RegularExpressions.RegexOptions.IgnoreCase),

        // dmxs.org: /{category}/{numericId}.html  (not /news_last/, /tags, etc.)
        ["dmxs.org"]    = url => System.Text.RegularExpressions.Regex.IsMatch(
            url, @"dmxs\.org/[a-zA-Z]+/\d+\.html", System.Text.RegularExpressions.RegexOptions.IgnoreCase),

        // 52shuku.net: /{category}/{folder}/bk{id}.html
        ["52shuku.net"] = url => System.Text.RegularExpressions.Regex.IsMatch(
            url, @"52shuku\.net/[^/]+/[^/]+/bk[^/]+\.html", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
    };

    /// <summary>Returns true if the URL is a valid novel index page for its site.</summary>
    private static bool IsNovelPage(string url)
    {
        string? site = DetectSite(url);
        if (site == null) return false;
        return _novelPageChecks.TryGetValue(site, out var check) && check(url);
    }

    private string _currentUrl;
    private readonly string _homeUrl;
    private bool   _isLoading;
    private bool   _isTranslated;   // true when currently viewing via Google Translate proxy
    private string _originalUrl = string.Empty; // the pre-translate URL, so we can toggle back

    /// <summary>
    /// Set this before pushing WebBrowsePage. When the user taps Fetch,
    /// the URL is passed here and the WebView is popped so the caller
    /// can pre-fill its URL entry.
    /// </summary>
    public static Action<string>? OnUrlFetched { get; set; }

    public WebBrowsePage(string startUrl)
    {
        InitializeComponent();
        _currentUrl = startUrl;
        _homeUrl    = startUrl;
        Navigate(startUrl);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Hide the persistent tab bar — it doesn't belong on the WebView page
        MainActivity.Instance?.SetTabBarVisible(false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Restore the tab bar when leaving
        MainActivity.Instance?.SetTabBarVisible(true);
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
        {
            SiteWebView.GoBack();
        }
        else
        {
            await Shell.Current.Navigation.PopAsync();
        }
    }

    private void OnForwardTapped(object sender, TappedEventArgs e)
    {
        if (SiteWebView.CanGoForward)
        {
            SiteWebView.GoForward();
        }
    }

    private void OnReloadTapped(object sender, TappedEventArgs e)
        => SiteWebView.Reload();

    private void OnHomeSourceTapped(object sender, TappedEventArgs e)
    {
        // Reset translate state when going home
        if (_isTranslated)
        {
            _isTranslated = false;
            _originalUrl  = string.Empty;
            UpdateTranslateFabAppearance();
        }
        Navigate(_homeUrl);
    }

    private async void OnOpenInBrowserTapped(object sender, TappedEventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri(_currentUrl)); }
        catch { /* ignore */ }
    }

    // Sites that use Cloudflare — Google Translate proxy can't load them in WebView.
    // For these, we open the translated URL in the external browser instead.
    private static readonly HashSet<string> _cfSites = new(StringComparer.OrdinalIgnoreCase)
    {
        "69shuba.com", "czbooks.net"
    };

    private async void OnTranslateTapped(object sender, TappedEventArgs e)
    {
        await FabTranslate.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabTranslate.ScaleToAsync(1.0,  70, Easing.SpringOut);

        if (_isTranslated)
        {
            // Revert to the original URL
            string urlToRestore = _originalUrl;
            _isTranslated = false;
            _originalUrl  = string.Empty;
            UpdateTranslateFabAppearance();
            Navigate(urlToRestore);
        }
        else
        {
            // Check if current site is CF-protected BEFORE trying to translate
            string? site = DetectSite(_currentUrl);

            if (site != null && _cfSites.Contains(site))
            {
                // CF-protected sites can't be translated via web proxies
                // Offer alternatives
                string? choice = await DisplayActionSheetAsync(
                    $"{site} uses Cloudflare protection which blocks web translation services.",
                    "Cancel",
                    null,
                    "Copy URL",
                    "Open in Browser");

                if (choice == "Copy URL")
                {
                    await Clipboard.Default.SetTextAsync(_currentUrl);
                    await ShowQueuedToastAsync("URL copied to clipboard!");
                }
                else if (choice == "Open in Browser")
                {
                    try { await Launcher.Default.OpenAsync(new Uri(_currentUrl)); }
                    catch { /* ignore */ }
                }
                return;
            }

            // Non-CF site: translate in-app via Google Translate proxy
            string encoded      = Uri.EscapeDataString(_currentUrl);
            string translateUrl = $"https://translate.google.com/translate?sl=auto&tl=en&u={encoded}";
            
            _originalUrl  = _currentUrl;
            _isTranslated = true;
            UpdateTranslateFabAppearance();
            Navigate(translateUrl);
        }
    }

    /// <summary>
    /// Updates the Translate FAB to show active/inactive state.
    /// Active = currently translated (accent color + "Original" label).
    /// </summary>
    private void UpdateTranslateFabAppearance()
    {
        if (_isTranslated)
        {
            FabTranslate.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            FabTranslate.SetDynamicResource(Border.StrokeProperty, "AccentLight");
            FabTranslateIcon.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            FabTranslateLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            FabTranslateLabel.Text = "ORIGINAL";
        }
        else
        {
            FabTranslate.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
            FabTranslate.SetDynamicResource(Border.StrokeProperty, "Stroke");
            FabTranslateIcon.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            FabTranslateLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            FabTranslateLabel.Text = "TRANSLATE";
        }
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
        UpdateNavigationButtons();
    }

    private void OnNavigated(object sender, WebNavigatedEventArgs e)
    {
        _isLoading = false;
        LoadingBar.IsVisible = false;
        LoadingBar.Progress  = 0;

        _currentUrl      = e.Url;
        UrlBarLabel.Text = e.Url;

        UpdateDownloadFab(e.Url);
        UpdateNavigationButtons();
    }

    /// <summary>
    /// Updates the back/forward button states based on WebView navigation history.
    /// </summary>
    private void UpdateNavigationButtons()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Back button is always enabled (either goes back in WebView or pops the page)
            BackButton.Opacity = 1.0;
            
            // Forward button is only enabled if WebView can go forward
            ForwardButton.Opacity = SiteWebView.CanGoForward ? 1.0 : 0.4;
        });
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

    // Example novel URLs per site — shown in the invalid-URL banner
    private static readonly Dictionary<string, string> _exampleUrls = new()
    {
        ["quanben.io"]  = "e.g. https://www.quanben.io/n/aoshidanshen/list.html",
        ["czbooks.net"] = "e.g. https://czbooks.net/n/cp11cgi",
        ["69shuba.com"] = "e.g. https://www.69shuba.com/book/48273.htm",
        ["dmxs.org"]    = "e.g. https://www.dmxs.org/book/23204.html",
        ["52shuku.net"] = "e.g. https://www.52shuku.net/xiandaidushi/08_b/bkdKE.html",
    };

    /// <summary>
    /// Returns the site key (domain) that the current URL belongs to,
    /// or null if it doesn't match any known source.
    /// </summary>
    private static string? DetectSite(string url)
    {
        foreach (var key in _exampleUrls.Keys)
            if (url.Contains(key, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    /// <summary>
    /// Show the Download and Fetch FABs on any page belonging to a known source.
    /// Hidden when in translated mode to avoid URL extraction issues.
    /// </summary>
    private void UpdateDownloadFab(string url)
    {
        // Hide Fetch/Download when in translated mode
        // User must press ORIGINAL first to use these features
        if (_isTranslated)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FabDownload.IsVisible = false;
                FabFetch.IsVisible    = false;
            });
            return;
        }

        // Check if we're on a known source site
        bool onKnownSite = DetectSite(url) != null;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            bool wasVisible = FabDownload.IsVisible;
            FabDownload.IsVisible = onKnownSite;
            FabFetch.IsVisible    = onKnownSite;

            if (onKnownSite && !wasVisible)
            {
                foreach (var fab in new View[] { FabDownload, FabFetch })
                {
                    fab.Opacity      = 0;
                    fab.TranslationY = 20;
                    _ = Task.WhenAll(
                        fab.FadeToAsync(1.0, 200, Easing.CubicOut),
                        fab.TranslateToAsync(0, 0, 200, Easing.CubicOut));
                }
            }
        });
    }

    private async void OnFetchFabTapped(object sender, TappedEventArgs e)
    {
        await FabFetch.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabFetch.ScaleToAsync(1.0,  70, Easing.SpringOut);

        string url  = _currentUrl;
        string site = DetectSite(url) ?? "";

        // Validate: must be a novel index page
        if (!IsNovelPage(url))
        {
            string hint = _exampleUrls.TryGetValue(site, out var ex)
                ? $"Navigate to a novel's index page.\n{ex}"
                : "Navigate to a specific novel's index page first.";
            await ShowInvalidUrlBannerAsync(hint);
            return;
        }

        // Fire the callback so the caller (MainPage) can pre-fill its URL entry
        OnUrlFetched?.Invoke(url);

        // Pop back to the Download tab
        await Shell.Current.Navigation.PopAsync();
    }

    private async void OnDownloadFabTapped(object sender, TappedEventArgs e)
    {
        await FabDownload.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabDownload.ScaleToAsync(1.0,  70, Easing.SpringOut);

        string url  = _currentUrl;
        string site = DetectSite(url) ?? "";

        // Validate: must be a novel index page, not just the site homepage/listing
        if (!IsNovelPage(url))
        {
            string hint = _exampleUrls.TryGetValue(site, out var ex)
                ? $"Navigate to a novel's index page.\n{ex}"
                : "Navigate to a specific novel's index page first.";
            await ShowInvalidUrlBannerAsync(hint);
            return;
        }

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

    private async Task ShowInvalidUrlBannerAsync(string hint)
    {
        InvalidUrlHintLabel.Text     = hint;
        InvalidUrlBanner.Opacity     = 0;
        InvalidUrlBanner.TranslationY = 30;
        InvalidUrlBanner.IsVisible   = true;

        await Task.WhenAll(
            InvalidUrlBanner.FadeToAsync(1.0, 250, Easing.CubicOut),
            InvalidUrlBanner.TranslateToAsync(0, 0, 250, Easing.CubicOut));

        await Task.Delay(4000);

        await Task.WhenAll(
            InvalidUrlBanner.FadeToAsync(0, 250, Easing.CubicIn),
            InvalidUrlBanner.TranslateToAsync(0, 30, 250, Easing.CubicIn));

        InvalidUrlBanner.IsVisible = false;
    }

    private async Task ShowQueuedToastAsync(string message = "Queued for download!")
    {
        QueuedToastLabel.Text = message;
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
