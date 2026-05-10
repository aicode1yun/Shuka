using System.IO;
using Android.Graphics;
using Android.OS;
using Android.Webkit;
using Java.Interop;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Shuka.Android.Services;
using AWebView = Android.Webkit.WebView;

namespace Shuka.Android.Platforms.Android;

/// <summary>
/// WebViewHandler that blocks ads natively (ShouldInterceptRequest, wraps MAUI’s
/// WebViewClient) and exposes ShouldBlock to injected JavaScript for fetch/XHR/DOM.
/// Parser-loaded scripts like 52shuku’s ad_top.js must be blocked at the network layer.
/// </summary>
public class AdBlockingWebViewHandler : WebViewHandler
{
    /// <summary>
    /// MAUI calls <see cref="WebViewHandler.MapWebViewClient"/> during connect and whenever
    /// that mapper runs; it replaces <see cref="AWebView.WebViewClient"/> with a bare
    /// <see cref="MauiWebViewClient"/>, which removed our interceptor. We override that
    /// mapper entry so the outer client is always <see cref="ShukaAdWebViewClient"/>.
    /// </summary>
    private static new readonly PropertyMapper<Microsoft.Maui.Controls.WebView, AdBlockingWebViewHandler> Mapper =
        new(WebViewHandler.Mapper)
        {
            [nameof(WebViewClient)] = MapShukaWebViewClient,
        };

    private static void MapShukaWebViewClient(IWebViewHandler handler, Microsoft.Maui.IWebView webView)
    {
        if (handler is not WebViewHandler wh || wh.PlatformView is not AWebView wv)
            return;

        wv.SetWebViewClient(new ShukaAdWebViewClient(new MauiWebViewClient(wh)));
        System.Diagnostics.Debug.WriteLine("[AdBlockingWebViewHandler] MapWebViewClient → ShukaAdWebViewClient(MauiWebViewClient)");
    }

    // Intercept the NavigateTo command so we can ensure our JS interface stays attached.
    private static new readonly CommandMapper<Microsoft.Maui.Controls.WebView, AdBlockingWebViewHandler> CommandMapper =
        new(WebViewHandler.CommandMapper)
        {
            ["NavigateTo"] = (handler, view, args) =>
            {
                // Let MAUI handle the navigation
                WebViewHandler.CommandMapper["NavigateTo"]?.Invoke(handler, view, args);
                handler.EnsureAdBlockingWebViewClient();
                handler.EnsureJsInterface();
            }
        };

    public AdBlockingWebViewHandler() : base(Mapper, CommandMapper) { }

    protected override AWebView CreatePlatformView()
    {
        var view = base.CreatePlatformView();
        System.Diagnostics.Debug.WriteLine("[AdBlockingWebViewHandler] CreatePlatformView");
        return view;
    }

    protected override void ConnectHandler(AWebView platformView)
    {
        base.ConnectHandler(platformView);
        EnsureAdBlockingWebViewClient(platformView);
        EnsureJsInterface();
    }

    /// <summary>
    /// Wrap MAUI’s WebViewClient so resource loads can be filtered without breaking navigation.
    /// </summary>
    internal void EnsureAdBlockingWebViewClient(AWebView? platformView = null)
    {
        var view = platformView ?? PlatformView;
        if (view == null) return;

        try
        {
#pragma warning disable CA1416
            var current = view.WebViewClient;
            if (current is ShukaAdWebViewClient)
                return;

            view.SetWebViewClient(new ShukaAdWebViewClient(current ?? new WebViewClient()));
            System.Diagnostics.Debug.WriteLine("[AdBlockingWebViewHandler] ✓ ShouldInterceptRequest wrapper installed");
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AdBlockingWebViewHandler] ❌ EnsureAdBlockingWebViewClient: {ex.Message}");
        }
    }

    internal void EnsureJsInterface()
    {
        var platformView = PlatformView;
        if (platformView == null) return;

        try
        {
#pragma warning disable CA1416
            // Check if already attached by looking at a tag
            if (platformView.Tag?.ToString() == "__shuka_jsinterface")
            {
                System.Diagnostics.Debug.WriteLine("[AdBlockingWebViewHandler] JS interface already attached");
                return;
            }

            // Enable JavaScript (should already be on but make sure)
            platformView.Settings.JavaScriptEnabled = true;

            // Add the JS interface that exposes ShouldBlock to JavaScript
            platformView.AddJavascriptInterface(
                new AdBlockJsInterface(), "ShukaAdBlock");

            // Tag the view so we know the interface is attached
            platformView.Tag = new Java.Lang.String("__shuka_jsinterface");

            System.Diagnostics.Debug.WriteLine(
                "[AdBlockingWebViewHandler] ✓ ShukaAdBlock JS interface installed");
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AdBlockingWebViewHandler] ❌ Failed to install JS interface: {ex.Message}");
        }
    }

    protected override void DisconnectHandler(AWebView platformView)
    {
        System.Diagnostics.Debug.WriteLine("[AdBlockingWebViewHandler] DisconnectHandler");

        try
        {
#pragma warning disable CA1416
            platformView.RemoveJavascriptInterface("ShukaAdBlock");
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AdBlockingWebViewHandler] Error removing JS interface: {ex.Message}");
        }

        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// Delegates all callbacks to MAUI’s inner client but intercepts resource loads for ad URLs.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("android")]
internal sealed class ShukaAdWebViewClient(WebViewClient inner) : WebViewClient
{
    private readonly WebViewClient _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    [System.Runtime.Versioning.SupportedOSPlatform("android24.0")]
    public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
    {
        TryNoteSubFrameSiteUrl(request);
        return _inner.ShouldOverrideUrlLoading(view, request);
    }

    public override void OnPageStarted(AWebView? view, string? url, Bitmap? favicon) =>
        _inner.OnPageStarted(view, url, favicon);

    public override void OnPageFinished(AWebView? view, string? url) =>
        _inner.OnPageFinished(view, url);

    [System.Runtime.Versioning.SupportedOSPlatform("android23.0")]
    public override void OnReceivedError(AWebView? view, IWebResourceRequest? request, WebResourceError? error) =>
        _inner.OnReceivedError(view, request, error);

    [System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
    public override bool OnRenderProcessGone(AWebView? view, RenderProcessGoneDetail? detail) =>
        _inner.OnRenderProcessGone(view, detail);

    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
    {
#pragma warning disable CA1416
        var url = request?.Url?.ToString();
        if (!string.IsNullOrEmpty(url) &&
            AdBlockerService.Instance.IsEnabled &&
            AdBlockerService.Instance.ShouldBlock(url))
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlockNative] ✓ BLOCKED: {url}");
            return new WebResourceResponse(
                "text/plain",
                "utf-8",
                new MemoryStream(Array.Empty<byte>()));
        }

        return _inner.ShouldInterceptRequest(view, request);
#pragma warning restore CA1416
    }

    /// <summary>
    /// Subframe navigations (e.g. taps inside Google Translate’s content iframe). Prefetch rarely uses this callback;
    /// <see cref="ShouldInterceptRequest"/> is not used for tracking to avoid prerender/prefetch HTML stealing the URL.
    /// Translate often reports <c>HasGesture=false</c> here, so we do not gate on gesture.
    /// </summary>
    private static void TryNoteSubFrameSiteUrl(IWebResourceRequest? request)
    {
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.N || request == null || request.IsForMainFrame)
                return;

            TranslateEmbeddedFrameTracker.NoteUrlIfEmbeddedSite(request.Url?.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShukaAdWebView] TryNoteSubFrameSiteUrl: {ex.Message}");
        }
    }
}

/// <summary>
/// Java-side bridge that lets injected JavaScript call back to C# to decide
/// whether a given URL should be blocked. Exposed as window.ShukaAdBlock.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("android")]
public class AdBlockJsInterface : Java.Lang.Object
{
    /// <summary>
    /// Called from JavaScript: ShukaAdBlock.shouldBlock("https://ad.example.com/banner.js")
    /// Returns "true" or "false" as a string.
    /// </summary>
    [JavascriptInterface]
    [Export("shouldBlock")]
    public string ShouldBlock(string url)
    {
        try
        {
            bool blocked = AdBlockerService.Instance.ShouldBlock(url);
            if (blocked)
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlockJS] ✓ BLOCKED: {url}");
            }
            return blocked ? "true" : "false";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlockJS] Error: {ex.Message}");
            return "false";
        }
    }

    /// <summary>
    /// Called from JavaScript: ShukaAdBlock.isEnabled()
    /// Returns "true" or "false".
    /// </summary>
    [JavascriptInterface]
    [Export("isEnabled")]
    public string IsEnabled()
    {
        return AdBlockerService.Instance.IsEnabled ? "true" : "false";
    }

    /// <summary>
    /// Called from JavaScript to log messages back to the Android debug log.
    /// </summary>
    [JavascriptInterface]
    [Export("log")]
    public void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[AdBlockJS] {message}");
    }
}
