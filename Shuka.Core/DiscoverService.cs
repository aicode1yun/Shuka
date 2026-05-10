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
        string url = source.GetRecentUrl(page);
        string html = await _fetcher.Fetch(url, log: log, ct: ct);
        return source.ParseListing(html, url);
    }

    public async Task<ListingPage> GetPopularAsync(
        IBrowsableAdapter source, int page = 1,
        Action<string>? log = null, CancellationToken ct = default)
    {
        string url = source.GetPopularUrl(page);
        string html = await _fetcher.Fetch(url, log: log, ct: ct);
        return source.ParseListing(html, url);
    }

    public async Task<ListingPage> SearchAsync(
        IBrowsableAdapter source, string query, int page = 1,
        Action<string>? log = null, CancellationToken ct = default)
    {
        string url = source.GetSearchUrl(query, page);
        var postInfo = source.GetSearchPostBody(query, page);

        string html = postInfo.HasValue
            ? await _fetcher.FetchPost(url, postInfo.Value.postBody, postInfo.Value.charset, log: log, ct: ct)
            : await _fetcher.Fetch(url, log: log, ct: ct);

        return source.ParseListing(html, url);
    }

    /// <summary>
    /// Searches all sources in parallel and returns results grouped by source.
    /// Sources that fail are silently skipped.
    /// </summary>
    public async Task<List<(IBrowsableAdapter Source, ListingPage Results)>> SearchAllAsync(
        string query, Action<string>? log = null, CancellationToken ct = default)
    {
        var results = await SearchAllWithStatusAsync(query, log, ct);
        return results
            .Where(r => r.IsSuccess && r.Results.Novels.Count > 0)
            .Select(r => (r.Source, r.Results))
            .ToList();
    }

    /// <summary>
    /// Searches all sources in parallel and includes per-source failure status.
    /// </summary>
    public async Task<List<SourceSearchResult>> SearchAllWithStatusAsync(
        string query, Action<string>? log = null, CancellationToken ct = default)
    {
        var tasks = Sources.Select(async source =>
        {
            try
            {
                var page = await SearchAsync(source, query, 1, log, ct);
                return new SourceSearchResult(source, page, true, null);
            }
            catch (Exception ex)
            {
                return new SourceSearchResult(
                    source,
                    new ListingPage(new List<NovelEntry>(), false, 1),
                    false,
                    ex.Message);
            }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>
    /// Searches a single source and returns success/failure status.
    /// </summary>
    public async Task<SourceSearchResult> SearchSourceWithStatusAsync(
        IBrowsableAdapter source, string query, Action<string>? log = null, CancellationToken ct = default)
    {
        try
        {
            var page = await SearchAsync(source, query, 1, log, ct);
            return new SourceSearchResult(source, page, true, null);
        }
        catch (Exception ex)
        {
            return new SourceSearchResult(
                source,
                new ListingPage(new List<NovelEntry>(), false, 1),
                false,
                ex.Message);
        }
    }

    /// <summary>
    /// Fetches the book index page for <paramref name="indexUrl"/> and returns
    /// the total number of chapters parsed from it.
    /// Returns 0 on any error or when no adapter matches the URL.
    /// </summary>
    public async Task<int> GetChapterCountAsync(string indexUrl, CancellationToken ct = default)
    {
        try
        {
            var adapter = BookService.Adapters.FirstOrDefault(a => a.Matches(indexUrl));
            if (adapter == null) return 0;
            string normalized = adapter.NormalizeUrl(indexUrl);
            string html = await _fetcher.Fetch(normalized, ct: ct);
            var info = adapter.ParseIndex(html, normalized);
            return info.ChapterUrls.Count;
        }
        catch { return 0; }
    }
}
