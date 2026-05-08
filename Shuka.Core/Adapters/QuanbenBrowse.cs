using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Browse/discover support for quanben.io.
/// Recent:  https://www.quanben.io/sort/new/1.html
/// Popular: https://www.quanben.io/sort/hot/1.html
/// Search:  https://www.quanben.io/search/{query}/1.html
/// </summary>
public class QuanbenBrowse : IBrowsableAdapter
{
    public string SiteName        => "quanben.io";
    public bool   RequiresCfBypass => false;

    public string GetRecentUrl(int page = 1)  =>
        $"https://www.quanben.io/sort/new/{page}.html";
    public string GetPopularUrl(int page = 1) =>
        $"https://www.quanben.io/sort/hot/{page}.html";
    public string GetSearchUrl(string query, int page = 1) =>
        $"https://www.quanben.io/search/{Uri.EscapeDataString(query)}/{page}.html";

    public ListingPage ParseListing(string html, string pageUrl)
    {
        var novels = new List<NovelEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // quanben listing: <li> items with <a href="/n/{bookId}/"> links
        // Each item typically has: title, author, cover img, short description
        var itemPattern = new Regex(
            @"<li[^>]*>([\s\S]*?)</li>",
            RegexOptions.IgnoreCase);

        foreach (Match item in itemPattern.Matches(html))
        {
            string block = item.Groups[1].Value;

            // Must contain a /n/ link
            var urlM = Regex.Match(block,
                @"href=[""'](?:https?://(?:www\.)?quanben\.io)?(/n/([^""'/]+)/)[""']",
                RegexOptions.IgnoreCase);
            if (!urlM.Success) continue;

            string bookId = urlM.Groups[2].Value;
            if (!seen.Add(bookId)) continue;

            string url = $"https://www.quanben.io/n/{bookId}/list.html";

            // Title — prefer <span itemprop="name"> or <h3>/<h4>
            string title = "";
            var titleM = Regex.Match(block,
                @"<span[^>]+itemprop=""name""[^>]*>([^<]+)</span>",
                RegexOptions.IgnoreCase);
            if (titleM.Success) title = System.Net.WebUtility.HtmlDecode(titleM.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(title))
            {
                titleM = Regex.Match(block,
                    @"<h[34][^>]*>([^<]+)</h[34]>",
                    RegexOptions.IgnoreCase);
                if (titleM.Success) title = System.Net.WebUtility.HtmlDecode(titleM.Groups[1].Value.Trim());
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                // Fall back to link text
                var aM = Regex.Match(block,
                    @"href=[""'][^""']*" + Regex.Escape(bookId) + @"[^""']*[""'][^>]*>([^<]{2,60})</a>",
                    RegexOptions.IgnoreCase);
                if (aM.Success) title = System.Net.WebUtility.HtmlDecode(aM.Groups[1].Value.Trim());
            }
            if (string.IsNullOrWhiteSpace(title)) continue;

            // Cover
            string? cover = null;
            var imgM = Regex.Match(block,
                @"<img[^>]+src=[""'](https?://[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (imgM.Success) cover = imgM.Groups[1].Value;

            // Author
            string? author = null;
            var authM = Regex.Match(block,
                @"作者[：:]\s*([^\s<,，]{1,30})",
                RegexOptions.IgnoreCase);
            if (authM.Success) author = authM.Groups[1].Value.Trim();

            // Description
            string? desc = null;
            var descM = Regex.Match(block,
                @"<p[^>]*class=""[^""]*desc[^""]*""[^>]*>([^<]{10,})</p>",
                RegexOptions.IgnoreCase);
            if (descM.Success)
                desc = System.Net.WebUtility.HtmlDecode(descM.Groups[1].Value.Trim());

            novels.Add(new NovelEntry(title, author, url, cover, desc, null));
        }

        // Fallback: direct /n/ link scan
        if (novels.Count == 0)
        {
            var linkPattern = new Regex(
                @"href=[""'](?:https?://(?:www\.)?quanben\.io)?/n/([^""'/]+)/[""'][^>]*>([^<]{2,80})</a>",
                RegexOptions.IgnoreCase);
            foreach (Match m in linkPattern.Matches(html))
            {
                string bookId = m.Groups[1].Value;
                if (!seen.Add(bookId)) continue;
                string title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
                if (title.Length < 2) continue;
                novels.Add(new NovelEntry(title, null,
                    $"https://www.quanben.io/n/{bookId}/list.html", null, null, null));
            }
        }

        bool hasNext = html.Contains("下一页") ||
                       Regex.IsMatch(html, @"href=[""'][^""']*sort[^""']*/\d+\.html[""']",
                           RegexOptions.IgnoreCase);

        int currentPage = 1;
        var pageM = Regex.Match(pageUrl, @"/(\d+)\.html$");
        if (pageM.Success) int.TryParse(pageM.Groups[1].Value, out currentPage);

        return new ListingPage(novels, hasNext && novels.Count > 0, currentPage);
    }
}
