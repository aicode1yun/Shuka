using Shuka.Core.Adapters;

namespace Shuka.Core;

/// <summary>
/// Provides browsable novel sources for the Discover tab.
/// </summary>
public class DiscoverService
{
    private readonly HttpFetcher _fetcher;

    public static readonly IReadOnlyList<IBrowsableAdapter> Sources =
    [
        new QuanbenBrowse(),
        new CzBooksBrowse(),   // opens in WebView — CF blocks HTTP but browser works fine
        new ShubaBrowse(),
        new DmxsBrowse(),
        new ShukuBrowse(),
    ];

    public DiscoverService(ICloudflareBypass? cfBypass = null)
    {
        _fetcher = new HttpFetcher(cfBypass);
    }

    public async Task<ListingPage> GetRecentAsync(
        IBrowsableAdapter source, int page = 1,
        Action<string>? log = null, CancellationToken ct = default)
    {
        string url  = source.GetRecentUrl(page);
        string html = await _fetcher.Fetch(url, log: log, ct: ct);
        return source.ParseListing(html, url);
    }

    public async Task<ListingPage> GetPopularAsync(
        IBrowsableAdapter source, int page = 1,
        Action<string>? log = null, CancellationToken ct = default)
    {
        string url  = source.GetPopularUrl(page);
        string html = await _fetcher.Fetch(url, log: log, ct: ct);
        return source.ParseListing(html, url);
    }

    public async Task<ListingPage> SearchAsync(
        IBrowsableAdapter source, string query, int page = 1,
        Action<string>? log = null, CancellationToken ct = default)
    {
        string url  = source.GetSearchUrl(query, page);
        string html = await _fetcher.Fetch(url, log: log, ct: ct);
        return source.ParseListing(html, url);
    }

    /// <summary>
    /// Searches all sources in parallel and returns results grouped by source.
    /// Sources that fail are silently skipped.
    /// </summary>
    public async Task<List<(IBrowsableAdapter Source, ListingPage Results)>> SearchAllAsync(
        string query, Action<string>? log = null, CancellationToken ct = default)
    {
        var tasks = Sources.Select(async source =>
        {
            try
            {
                var page = await SearchAsync(source, query, 1, log, ct);
                return (source, page, error: false);
            }
            catch
            {
                return (source, new ListingPage(new List<NovelEntry>(), false, 1), error: true);
            }
        });

        var results = await Task.WhenAll(tasks);

        return results
            .Where(r => !r.error && r.Item2.Novels.Count > 0)
            .Select(r => (r.source, r.Item2))
            .ToList();
    }
}
