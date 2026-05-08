using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Browse/discover support for czbooks.net.
///
/// czbooks.net is behind Cloudflare and blocks all HTTP fetches (403).
/// It works fine in a real browser/WebView. The URLs below are used by
/// WebBrowsePage to open the site — ParseListing is a best-effort fallback
/// but will typically return empty (the WebView handles browsing directly).
///
/// Recent:  https://czbooks.net/new/1
/// Popular: https://czbooks.net/hot/1
/// Search:  https://czbooks.net/search?q={query}
/// </summary>
public class CzBooksBrowse : IBrowsableAdapter
{
    public string SiteName         => "czbooks.net";
    public string Description      => "Chinese novels · Cloudflare protected";
    public string IconGlyph        => "\uE894"; // language (globe)
    public bool   RequiresCfBypass => true;

    public string GetRecentUrl(int page = 1)  => "https://czbooks.net/";
    public string GetPopularUrl(int page = 1) => "https://czbooks.net/hot/1";
    public string GetSearchUrl(string query, int page = 1) =>
        $"https://czbooks.net/search?q={Uri.EscapeDataString(query)}&page={page}";

    public ListingPage ParseListing(string html, string pageUrl)
    {
        var novels = new List<NovelEntry>();

        // czbooks listing cards: <div class="novel-item"> or <li class="book-item">
        // Try multiple patterns since the site may vary
        var cardPattern = new Regex(
            @"<(?:div|li)[^>]+class=""[^""]*(?:novel-item|book-item|item)[^""]*""[^>]*>([\s\S]*?)</(?:div|li)>",
            RegexOptions.IgnoreCase);

        foreach (Match card in cardPattern.Matches(html))
        {
            string block = card.Groups[1].Value;

            // Extract URL
            var urlM = Regex.Match(block,
                @"href=[""'](https?://czbooks\.net/n/[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!urlM.Success)
                urlM = Regex.Match(block,
                    @"href=[""'](/n/[^""']+)[""']",
                    RegexOptions.IgnoreCase);
            if (!urlM.Success) continue;

            string url = urlM.Groups[1].Value.StartsWith("http")
                ? urlM.Groups[1].Value
                : "https://czbooks.net" + urlM.Groups[1].Value;

            // Extract title
            string title = "";
            var titleM = Regex.Match(block,
                @"<(?:h[1-6]|span|div)[^>]*class=""[^""]*title[^""]*""[^>]*>([^<]+)",
                RegexOptions.IgnoreCase);
            if (titleM.Success) title = System.Net.WebUtility.HtmlDecode(titleM.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(title))
            {
                var aM = Regex.Match(block, @"<a[^>]*>([^<]{2,60})</a>", RegexOptions.IgnoreCase);
                if (aM.Success) title = System.Net.WebUtility.HtmlDecode(aM.Groups[1].Value.Trim());
            }
            if (string.IsNullOrWhiteSpace(title)) continue;

            // Extract cover
            string? cover = null;
            var imgM = Regex.Match(block,
                @"<img[^>]+src=[""'](https?://[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (imgM.Success) cover = imgM.Groups[1].Value;

            // Extract author
            string? author = null;
            var authM = Regex.Match(block,
                @"作者[：:]\s*([^\s<,，]{1,30})",
                RegexOptions.IgnoreCase);
            if (authM.Success) author = authM.Groups[1].Value.Trim();

            novels.Add(new NovelEntry(title, author, url, cover, null, null));
        }

        // Fallback: parse any /n/{id} links if card parsing found nothing
        if (novels.Count == 0)
        {
            var linkPattern = new Regex(
                @"href=[""'](?:https?://czbooks\.net)?(/n/([^""'/]+))[""'][^>]*>([^<]{2,80})</a>",
                RegexOptions.IgnoreCase);
            var seen = new HashSet<string>();
            foreach (Match m in linkPattern.Matches(html))
            {
                string bookId = m.Groups[2].Value;
                if (!seen.Add(bookId)) continue;
                string url = "https://czbooks.net" + m.Groups[1].Value;
                string title = System.Net.WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
                if (title.Length < 2) continue;
                novels.Add(new NovelEntry(title, null, url, null, null, null));
            }
        }

        bool hasNext = html.Contains("下一页") || html.Contains("next") ||
                       Regex.IsMatch(html, @"page=\d+", RegexOptions.IgnoreCase);

        int currentPage = 1;
        var pageM = Regex.Match(pageUrl, @"/(\d+)$");
        if (pageM.Success) int.TryParse(pageM.Groups[1].Value, out currentPage);

        return new ListingPage(novels, hasNext && novels.Count > 0, currentPage);
    }
}
