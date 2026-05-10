namespace Shuka.Android.Platforms.Android;

/// <summary>
/// Remembers the last real site URL loaded inside a subframe. Google Translate often keeps the
/// top WebView URL and <c>iframe[src]</c> on the first page while the inner frame navigates.
/// </summary>
internal static class TranslateEmbeddedFrameTracker
{
    private static readonly object Gate = new();
    private static string? _last;

    public static void Clear()
    {
        lock (Gate) { _last = null; }
    }

    public static void NoteUrlIfEmbeddedSite(string? url)
    {
        if (!CouldBeOriginalSitePage(url))
            return;

        lock (Gate) { _last = url; }
    }

    public static bool TryGetLatest(out string? url)
    {
        lock (Gate)
        {
            url = _last;
            return !string.IsNullOrWhiteSpace(url);
        }
    }

    private static bool CouldBeOriginalSitePage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = (u.IdnHost ?? u.Host).ToLowerInvariant();
        if (IsGoogleOrAdInfrastructure(host))
            return false;

        if (LooksLikeStaticResource(u.AbsolutePath))
            return false;

        return true;
    }

    private static bool IsGoogleOrAdInfrastructure(string host)
    {
        if (host is "translate.google.com" or "translate.googleusercontent.com")
            return true;
        if (host.EndsWith(".translate.google.com", StringComparison.Ordinal))
            return true;
        if (host is "www.google.com" or "google.com")
            return true;
        if (host.EndsWith(".gstatic.com", StringComparison.Ordinal))
            return true;
        if (host.Contains("googlesyndication", StringComparison.Ordinal))
            return true;
        if (host.Contains("doubleclick", StringComparison.Ordinal))
            return true;
        if (host.Contains("google-analytics", StringComparison.Ordinal))
            return true;
        if (host.Contains("googleadservices", StringComparison.Ordinal))
            return true;
        if (host.Contains("googleapis.com", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool LooksLikeStaticResource(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var p = path.ToLowerInvariant();
        ReadOnlySpan<string> exts =
        [
            ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico",
            ".woff", ".woff2", ".ttf", ".map", ".json", ".xml", ".mp4", ".webm",
            ".m4a", ".mp3", ".wasm",
        ];

        foreach (var e in exts)
        {
            if (p.EndsWith(e, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
