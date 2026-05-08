using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Browse/discover support for 69shuba.com.
/// Recent:  https://www.69shuba.com/book/new/1/
/// Popular: https://www.69shuba.com/book/hot/1/
/// Search:  https://www.69shuba.com/search.htm?searchkey={query}&amp;page={page}
/// Requires Cloudflare bypass.
/// </summary>
public class ShubaBrowse : IBrowsableAdapter
{
    public string SiteName        => "69shuba.com";
    public bool   RequiresCfBypass => true;

    public string GetRecentUrl(int page = 1)  =>
        $"https://www.69shuba.com/book/new/{page}/";
    public string GetPopularUrl(int page = 1) =>
        $"https://www.69shuba.com/book/hot/{page}/";
    public string GetSearchUrl(string query, int page = 1) =>
        $"https://www.69shuba.com/search.htm?searchkey={Uri.EscapeDataString(query)}&page={page}";

    public ListingPage ParseListing(string html, string pageUrl)
    {
        var novels = new List<NovelEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 69shuba listing: book cards with /book/{id}/ links
        var bookPattern = new Regex(
            @"href=[""'](?:https?://(?:www\.)?69shuba\.com)?/book/(\d+)/[""'][^>]*>([^<]{2,80})</a>",
            RegexOptions.IgnoreCase);

        // Try to find structured blocks first
        var blockPattern = new Regex(
            @"<(?:div|li)[^>]+class=""[^""]*(?:book-item|novel-item|item)[^""]*""[^>]*>([\s\S]*?)</(?:div|li)>",
            RegexOptions.IgnoreCase);

        foreach (Match block in blockPattern.Matches(html))
        {
            string content = block.Groups[1].Value;

            var urlM = Regex.Match(content,
                @"href=[""'](?:https?://(?:www\.)?69shuba\.com)?/book/(\d+)/[""']",
                RegexOptions.IgnoreCase);
            if (!urlM.Success) continue;

            string bookId = urlM.Groups[1].Value;
            if (!seen.Add(bookId)) continue;

            string url = $"https://www.69shuba.com/book/{bookId}/";

            string title = "";
            var titleM = Regex.Match(content,
                @"<(?:h[1-6]|span)[^>]*class=""[^""]*(?:title|name)[^""]*""[^>]*>([^<]+)",
                RegexOptions.IgnoreCase);
            if (titleM.Success) title = System.Net.WebUtility.HtmlDecode(titleM.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(title))
            {
                var aM = Regex.Match(content,
                    @"href=[""'][^""']*/book/" + Regex.Escape(bookId) + @"/[""'][^>]*>([^<]{2,60})</a>",
                    RegexOptions.IgnoreCase);
                if (aM.Success) title = System.Net.WebUtility.HtmlDecode(aM.Groups[1].Value.Trim());
            }
            if (string.IsNullOrWhiteSpace(title)) continue;

            string? cover = null;
            var imgM = Regex.Match(content,
                @"<img[^>]+src=[""'](https?://[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (imgM.Success) cover = imgM.Groups[1].Value;

            // Fallback cover from CDN
            if (cover == null && int.TryParse(bookId, out int bid))
                cover = $"https://cdn.cdnshu.com/files/article/image/{bid / 1000}/{bookId}/{bookId}s.jpg";

            string? author = null;
            var authM = Regex.Match(content, @"作者[：:]\s*([^\s<,，]{1,30})");
            if (authM.Success) author = authM.Groups[1].Value.Trim();

            novels.Add(new NovelEntry(title, author, url, cover, null, null));
        }

        // Fallback: direct link scan
        if (novels.Count == 0)
        {
            foreach (Match m in bookPattern.Matches(html))
            {
                string bookId = m.Groups[1].Value;
                if (!seen.Add(bookId)) continue;
                string title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
                if (title.Length < 2) continue;
                string? cover = int.TryParse(bookId, out int bid)
                    ? $"https://cdn.cdnshu.com/files/article/image/{bid / 1000}/{bookId}/{bookId}s.jpg"
                    : null;
                novels.Add(new NovelEntry(title, null,
                    $"https://www.69shuba.com/book/{bookId}/", cover, null, null));
            }
        }

        bool hasNext = html.Contains("下一页");
        int currentPage = 1;
        var pageM = Regex.Match(pageUrl, @"/(\d+)/$");
        if (pageM.Success) int.TryParse(pageM.Groups[1].Value, out currentPage);

        return new ListingPage(novels, hasNext && novels.Count > 0, currentPage);
    }
}
