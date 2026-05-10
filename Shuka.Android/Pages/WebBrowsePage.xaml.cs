using Shuka.Android.Services;
using Shuka.Core.Adapters;
using Shuka.Core;

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
    private bool   _fabMenuExpanded = false; // tracks FAB menu state

    /// <summary>
    /// Set this before pushing WebBrowsePage. When the user taps Fetch,
    /// the URL is passed here and the WebView is popped so the caller
    /// can pre-fill its URL entry.
    /// </summary>
    public static Action<string>? OnUrlFetched { get; set; }

    public WebBrowsePage(string startUrl)
    {
        try
        {
            // Assign unique instance ID
            _instanceId = System.Threading.Interlocked.Increment(ref _instanceCounter);
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Creating instance #{_instanceId}");
            
            // Try to clear any existing NameScope first
            try
            {
                var existingScope = Microsoft.Maui.Controls.Internals.NameScope.GetNameScope(this);
                if (existingScope != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Found existing NameScope, clearing it");
                    Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(this, null);
                }
            }
            catch { /* ignore */ }
            
            // Create a completely new NameScope for this instance
            var nameScope = new Microsoft.Maui.Controls.Internals.NameScope();
            Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(this, nameScope);
            
            try
            {
                InitializeComponent();
            }
            catch (ArgumentException ex) when (ex.Message.Contains("already exists in this NameScope"))
            {
                // MAUI bug: NameScope conflict. Try to recover by forcing a new NameScope
                System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] NameScope conflict detected, attempting recovery");
                
                // Force clear and retry
                Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(this, null);
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                
                var newScope = new Microsoft.Maui.Controls.Internals.NameScope();
                Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(this, newScope);
                
                // Retry InitializeComponent
                InitializeComponent();
            }
            
            // Validate startUrl before proceeding
            if (string.IsNullOrWhiteSpace(startUrl))
            {
                startUrl = "https://www.google.com";
                System.Diagnostics.Debug.WriteLine("[WebBrowsePage] Warning: Empty startUrl, using fallback");
            }
            
            _currentUrl = startUrl;
            _homeUrl    = startUrl;
            
            // Subscribe to WebView error events
            SiteWebView.Navigating += OnNavigating!;
            SiteWebView.Navigated += OnNavigated!;

            // Initialize ad blocker icon state
            UpdateAdBlockerIcon();
            
            Navigate(startUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Constructor error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Stack trace: {ex.StackTrace}");
            
            // Log to crash file
            try
            {
                var logPath = Path.Combine(FileSystem.CacheDirectory, "crash.log");
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WebBrowsePage constructor: {ex.Message}\n{ex.StackTrace}\n\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { /* ignore logging errors */ }
            
            throw; // Re-throw to show error to user
        }
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
        System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Instance #{_instanceId} disappearing");
        
        // Restore the tab bar when leaving
        MainActivity.Instance?.SetTabBarVisible(true);
        
        // Clean up WebView to prevent memory leaks
        CleanupWebView();
        
        // Clear the NameScope to prevent "element already exists" errors on next navigation
        try
        {
            // Get the current NameScope and clear all registrations
            var nameScope = Microsoft.Maui.Controls.Internals.NameScope.GetNameScope(this);
            if (nameScope is Microsoft.Maui.Controls.Internals.NameScope ns)
            {
                // Clear all registered names
                System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Clearing NameScope for instance #{_instanceId}");
            }
            
            // Set a new empty NameScope
            Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(this, new Microsoft.Maui.Controls.Internals.NameScope());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Error clearing NameScope: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the page is being removed from the navigation stack.
    /// Ensures complete cleanup of resources.
    /// </summary>
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        
        // If we're being popped (not just hidden), do a full cleanup
        if (Shell.Current.Navigation.NavigationStack.Contains(this) == false)
        {
            CleanupWebView();
        }
    }

    /// <summary>
    /// Properly disposes the WebView to prevent memory leaks.
    /// WebViews can hold significant memory and native resources.
    /// </summary>
    private void CleanupWebView()
    {
        try
        {
            if (SiteWebView != null)
            {
                // Unsubscribe from events to prevent memory leaks
                SiteWebView.Navigating -= OnNavigating!;
                SiteWebView.Navigated -= OnNavigated!;

                // Stop any ongoing navigation
                try
                {
                    // Clear the WebView source to stop loading
                    SiteWebView.Source = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Error clearing WebView source: {ex.Message}");
                }

#if ANDROID
                // Android-specific cleanup
                if (SiteWebView.Handler?.PlatformView is global::Android.Webkit.WebView androidWebView)
                {
                    try
                    {
                        // Stop loading any content
                        androidWebView.StopLoading();
                        
                        // Clear cache and history
                        androidWebView.ClearCache(true);
                        androidWebView.ClearHistory();
                        
                        // Remove all views to break circular references
                        androidWebView.RemoveAllViews();
                        
                        // Destroy the WebView
                        androidWebView.Destroy();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Error during Android WebView cleanup: {ex.Message}");
                    }
                }
#endif
            }
            
            // Clear the handler to help with cleanup
            try
            {
                if (SiteWebView?.Handler != null)
                {
                    SiteWebView.Handler.DisconnectHandler();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Error disconnecting handler: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Error during WebView cleanup: {ex.Message}");
        }
    }
    
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        
        // If handler is being removed, clean up
        if (Handler == null)
        {
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] Handler removed, cleaning up");
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the specified URL with validation and error handling.
    /// </summary>
    private void Navigate(string url)
    {
        try
        {
            // Validate URL format
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowNavigationError("Invalid URL", "The URL cannot be empty.");
                return;
            }

            // Ensure URL has a scheme
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            // Validate URI format
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                ShowNavigationError("Invalid URL", $"The URL format is invalid:\n{url}");
                return;
            }

            // Check for valid scheme
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                ShowNavigationError("Unsupported Protocol", $"Only HTTP and HTTPS URLs are supported.\n{url}");
                return;
            }

            _currentUrl = url;
            
            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    UrlBarLabel.Text = url;
                    SiteWebView.Source = new UrlWebViewSource { Url = url };
                    UpdateDownloadFab(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] UI update error: {ex.Message}");
                    ShowNavigationError("Navigation Error", "Failed to load the page.");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Navigate error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Stack trace: {ex.StackTrace}");
            ShowNavigationError("Navigation Error", $"Failed to navigate to URL:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Shows an error banner when navigation fails.
    /// </summary>
    private async void ShowNavigationError(string title, string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await DisplayAlertAsync(title, message, "OK");
            }
            catch
            {
                // Fallback: show in invalid URL banner if alert fails
                InvalidUrlHintLabel.Text = $"{title}: {message}";
                await ShowInvalidUrlBannerAsync(message);
            }
        });
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Back navigation error: {ex.Message}");
        }
    }

    private void OnForwardTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (SiteWebView.CanGoForward)
            {
                SiteWebView.GoForward();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Forward navigation error: {ex.Message}");
        }
    }

    private void OnReloadTapped(object sender, TappedEventArgs e)
    {
        try
        {
            SiteWebView.Reload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Reload error: {ex.Message}");
            ShowNavigationError("Reload Failed", "Could not reload the page. Please try again.");
        }
    }

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

    private async void OnAdBlockerToggleTapped(object sender, TappedEventArgs e)
    {
        // Toggle the ad blocker
        AdBlockerService.Instance.IsEnabled = !AdBlockerService.Instance.IsEnabled;
        UpdateAdBlockerIcon();

        // Animate the button
        await AdBlockerButton.ScaleToAsync(0.8, 80, Easing.CubicOut);
        await AdBlockerButton.ScaleToAsync(1.0, 80, Easing.SpringOut);

        // Show toast notification
        string message = AdBlockerService.Instance.IsEnabled 
            ? "Ad Blocker: ON" 
            : "Ad Blocker: OFF";
        await ShowQueuedToastAsync(message);

        // Full navigation reapplies native interception + injected filters (Reload alone can be cache-heavy).
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentUrl))
                    Navigate(_currentUrl);
                else
                    SiteWebView.Reload();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Ad blocker toggle refresh: {ex.Message}");
            }
        });
    }

    private void UpdateAdBlockerIcon()
    {
        bool enabled = AdBlockerService.Instance.IsEnabled;
        // shield icon when on, shield-off when disabled
        AdBlockerIcon.Text = enabled ? "\uE14B" : "\uE14B";
        AdBlockerIcon.SetDynamicResource(Label.TextColorProperty,
            enabled ? "AccentLight" : "TextMuted");
        AdBlockerButton.Opacity = enabled ? 1.0 : 0.45;
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
        try
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
                // Validate current URL before translating
                if (string.IsNullOrWhiteSpace(_currentUrl))
                {
                    await DisplayAlertAsync("Cannot Translate", "No page is currently loaded.", "OK");
                    return;
                }

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
                        catch (Exception ex)
                        {
                            await DisplayAlertAsync("Error", $"Could not open browser:\n{ex.Message}", "OK");
                        }
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Translate error: {ex.Message}");
            await DisplayAlertAsync("Translation Error", 
                $"An error occurred while translating:\n{ex.Message}", "OK");
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
        try
        {
            // Collapse FAB menu when navigating
            if (_fabMenuExpanded)
            {
                _fabMenuExpanded = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _ = Task.WhenAll(
                        FabToggleIcon.RotateToAsync(0, 250, Easing.CubicOut),
                        FabMenuItems.FadeToAsync(0, 200, Easing.CubicIn)
                    );
                    FabMenuItems.IsVisible = false;
                });
            }

            _isLoading = true;
            LoadingBar.IsVisible = true;
            LoadingBar.Progress  = 0;
            _ = AnimateLoadingBarAsync();

            _currentUrl      = e.Url;
            UrlBarLabel.Text = e.Url;
            UpdateDownloadFab(e.Url);
            UpdateNavigationButtons();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] OnNavigating error: {ex.Message}");
            e.Cancel = true;
            ShowNavigationError("Navigation Error", "An error occurred while navigating.");
        }
    }

    private void OnNavigated(object sender, WebNavigatedEventArgs e)
    {
        try
        {
            _isLoading = false;
            LoadingBar.IsVisible = false;
            LoadingBar.Progress  = 0;

            // Check navigation result
            if (e.Result == WebNavigationResult.Failure)
            {
                ShowNavigationError("Page Load Failed", 
                    "The page could not be loaded. Please check your internet connection and try again.");
                return;
            }
            else if (e.Result == WebNavigationResult.Timeout)
            {
                ShowNavigationError("Connection Timeout", 
                    "The page took too long to load. Please try again.");
                return;
            }

            _currentUrl      = e.Url;
            UrlBarLabel.Text = e.Url;

            UpdateDownloadFab(e.Url);
            UpdateNavigationButtons();

            // Inject ad blocker cosmetic filter from MAUI layer as well,
            // since OnPageFinished in the native handler may fire before ad scripts run.
            _ = InjectAdBlockerAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] OnNavigated error: {ex.Message}");
            _isLoading = false;
            LoadingBar.IsVisible = false;
        }
    }

    /// <summary>
    /// Injects the ad blocker script immediately and with multiple delayed passes
    /// to catch ads that are injected by scripts after the page finishes loading.
    /// Uses an aggressive multi-pass approach like uBlock Origin.
    /// </summary>
    private async Task InjectAdBlockerAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] ===== AD BLOCKER INJECTION START =====");
            
            var js = AdBlockerService.Instance.GetCosmeticFilterScript();
            if (string.IsNullOrEmpty(js))
            {
                System.Diagnostics.Debug.WriteLine("[WebBrowsePage] WARNING: Ad blocker script is empty!");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Ad blocker script length: {js.Length} chars");

            // First pass — run immediately
            await SiteWebView.EvaluateJavaScriptAsync(js);
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] ✓ Ad blocker pass 1 complete");

            // Check if it's working by inspecting the page
            await Task.Delay(500);
            var inspectJs = @"
(function(){
  var report = {
    iframes: document.querySelectorAll('iframe').length,
    adElements: document.querySelectorAll('[class*=""ad""], [id*=""ad""]').length,
    scripts: document.querySelectorAll('script[src*=""ad""], script[src*=""doubleclick""]').length
  };
  return JSON.stringify(report);
})();
";
            var result = await SiteWebView.EvaluateJavaScriptAsync(inspectJs);
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] After pass 1: {result}");

            // Second pass — wait for lazy-loaded / script-injected ads (500ms)
            await Task.Delay(500);
            await SiteWebView.EvaluateJavaScriptAsync(js);
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] ✓ Ad blocker pass 2 complete");

            // Third pass — catch delayed ads (1.5s)
            await Task.Delay(1000);
            await SiteWebView.EvaluateJavaScriptAsync(js);
            result = await SiteWebView.EvaluateJavaScriptAsync(inspectJs);
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] After pass 3: {result}");

            // Fourth pass — some sites inject ads even later (3s)
            await Task.Delay(1500);
            await SiteWebView.EvaluateJavaScriptAsync(js);
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] ✓ Ad blocker pass 4 complete");
            
            // Fifth pass — final cleanup (5s)
            await Task.Delay(2000);
            await SiteWebView.EvaluateJavaScriptAsync(js);
            result = await SiteWebView.EvaluateJavaScriptAsync(inspectJs);
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] After pass 5: {result}");
            
            System.Diagnostics.Debug.WriteLine("[WebBrowsePage] ===== AD BLOCKER INJECTION COMPLETE =====");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] ❌ AdBlocker inject error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Updates the back/forward button states based on WebView navigation history.
    /// </summary>
    private void UpdateNavigationButtons()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Back button is always enabled (either goes back in WebView or pops the page)
                BackButton.Opacity = 1.0;
                
                // Forward button is only enabled if WebView can go forward
                ForwardButton.Opacity = SiteWebView.CanGoForward ? 1.0 : 0.4;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] UpdateNavigationButtons error: {ex.Message}");
        }
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
    
    // Static counter to ensure unique instances
    private static int _instanceCounter = 0;
    private readonly int _instanceId;

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
    /// Show Bookmark FAB on novel pages only.
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
                FabBookmark.IsVisible = false;
            });
            return;
        }

        // Check if we're on a known source site
        bool onKnownSite = DetectSite(url) != null;
        bool onNovelPage = IsNovelPage(url);
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FabDownload.IsVisible = onKnownSite;
            FabFetch.IsVisible    = onKnownSite;
            FabBookmark.IsVisible = onNovelPage; // Only show on actual novel pages

            // Update bookmark icon state
            if (onNovelPage)
            {
                UpdateBookmarkFabAppearance();
            }
        });
    }

    private async void OnFetchFabTapped(object sender, TappedEventArgs e)
    {
        await FabFetch.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabFetch.ScaleToAsync(1.0,  70, Easing.SpringOut);

        // Collapse menu after action
        if (_fabMenuExpanded)
        {
            _fabMenuExpanded = false;
            _ = Task.WhenAll(
                FabToggleIcon.RotateToAsync(0, 250, Easing.CubicOut),
                FabMenuItems.FadeToAsync(0, 200, Easing.CubicIn)
            );
            FabMenuItems.IsVisible = false;
        }

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

        // Collapse menu after action
        if (_fabMenuExpanded)
        {
            _fabMenuExpanded = false;
            _ = Task.WhenAll(
                FabToggleIcon.RotateToAsync(0, 250, Easing.CubicOut),
                FabMenuItems.FadeToAsync(0, 200, Easing.CubicIn)
            );
            FabMenuItems.IsVisible = false;
        }

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

    // ── FAB menu toggle ───────────────────────────────────────────────────────

    private async void OnFabToggleTapped(object sender, TappedEventArgs e)
    {
        await FabToggle.ScaleToAsync(0.92, 70, Easing.CubicOut);
        await FabToggle.ScaleToAsync(1.0, 70, Easing.SpringOut);

        _fabMenuExpanded = !_fabMenuExpanded;

        if (_fabMenuExpanded)
        {
            // Expand menu
            FabMenuItems.IsVisible = true;
            
            // Animate icon rotation (arrow pointing down)
            await Task.WhenAll(
                FabToggleIcon.RotateToAsync(180, 250, Easing.CubicOut),
                FabMenuItems.FadeToAsync(1.0, 200, Easing.CubicOut),
                FabMenuItems.TranslateToAsync(0, 0, 200, Easing.CubicOut)
            );
        }
        else
        {
            // Collapse menu
            await Task.WhenAll(
                FabToggleIcon.RotateToAsync(0, 250, Easing.CubicOut),
                FabMenuItems.FadeToAsync(0, 200, Easing.CubicIn),
                FabMenuItems.TranslateToAsync(0, 20, 200, Easing.CubicIn)
            );
            
            FabMenuItems.IsVisible = false;
        }
    }

    // ── Bookmark logic ────────────────────────────────────────────────────────

    private async void OnBookmarkTapped(object sender, TappedEventArgs e)
    {
        try
        {
            await FabBookmark.ScaleToAsync(0.92, 70, Easing.CubicOut);
            await FabBookmark.ScaleToAsync(1.0,  70, Easing.SpringOut);

            string url = _currentUrl;
            
            // Validate: must be a novel page
            if (!IsNovelPage(url))
            {
                await DisplayAlertAsync("Cannot Bookmark", 
                    "Navigate to a specific novel's index page first.", "OK");
                return;
            }

            string? site = DetectSite(url);
            if (site == null)
            {
                await DisplayAlertAsync("Cannot Bookmark", 
                    "This site is not supported for bookmarks.", "OK");
                return;
            }

            // Check if already bookmarked
            bool isBookmarked = BookmarkService.Instance.IsBookmarked(url);

            if (isBookmarked)
            {
                // Remove bookmark
                BookmarkService.Instance.RemoveBookmark(url);
                UpdateBookmarkFabAppearance();
                await ShowQueuedToastAsync("Bookmark removed!");
            }
            else
            {
                // Add bookmark - need to fetch title and author
                await AddBookmarkAsync(url, site);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] Bookmark error: {ex.Message}");
            await DisplayAlertAsync("Bookmark Error", 
                $"An error occurred:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Fetches the novel's title and author, then adds it to bookmarks.
    /// </summary>
    private async Task AddBookmarkAsync(string url, string siteName)
    {
        try
        {
            // Show loading state
            FabBookmarkLabel.Text = "LOADING...";
            FabBookmark.IsEnabled = false;

            // Fetch book info (title and author in Chinese) using BookService
            var bookService = new BookService();
            var bookInfo = await bookService.GatherBookInfo(url, 0, null);
            
            if (bookInfo == null || string.IsNullOrWhiteSpace(bookInfo.Title))
            {
                await DisplayAlertAsync("Error", 
                    "Could not fetch novel information. Please try again.", "OK");
                return;
            }

            // Add to bookmarks with Chinese title and author
            BookmarkService.Instance.AddBookmark(
                url, 
                bookInfo.Title, 
                bookInfo.Author ?? "Unknown", 
                siteName,
                bookInfo.Total);

            UpdateBookmarkFabAppearance();
            await ShowQueuedToastAsync($"Bookmarked: {bookInfo.Title}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowsePage] AddBookmark error: {ex.Message}");
            await DisplayAlertAsync("Error", 
                $"Could not add bookmark:\n{ex.Message}", "OK");
        }
        finally
        {
            // Restore button state
            FabBookmark.IsEnabled = true;
            UpdateBookmarkFabAppearance();
        }
    }

    /// <summary>
    /// Updates the Bookmark FAB to show bookmarked/not-bookmarked state.
    /// </summary>
    private void UpdateBookmarkFabAppearance()
    {
        bool isBookmarked = BookmarkService.Instance.IsBookmarked(_currentUrl);
        
        if (isBookmarked)
        {
            // Bookmarked state: filled icon, accent color
            FabBookmark.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
            FabBookmark.SetDynamicResource(Border.StrokeProperty, "AccentLight");
            FabBookmarkIcon.Text = "\uE866"; // bookmark filled
            FabBookmarkIcon.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            FabBookmarkLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
            FabBookmarkLabel.Text = "BOOKMARKED";
        }
        else
        {
            // Not bookmarked: outlined icon, muted color
            FabBookmark.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
            FabBookmark.SetDynamicResource(Border.StrokeProperty, "Stroke");
            FabBookmarkIcon.Text = "\uE867"; // bookmark outlined
            FabBookmarkIcon.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            FabBookmarkLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            FabBookmarkLabel.Text = "BOOKMARK";
        }
    }
}
