using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Browse/discover support for quanben.io (全本小说网).
///
/// The /sort/new/ and /sort/hot/ URLs no longer exist (404).
/// The site now uses category pages:
///   Browse (popular): https://www.quanben.io/c/xuanhuan.html          (page 1)
///                     https://www.quanben.io/c/xuanhuan_{page}.html   (page 2+)
///   Search:           https://www.quanben.io/search/{query}/{page}.html
///
/// Book URLs: https://www.quanben.io/n/{bookId}/list.html
/// No Cloudflare protection.
/// </summary>
public class QuanbenBrowse : IBrowsableAdapter
{
    public string SiteName         => "quanben.io";
    public string Description      => "Chinese full novels · fantasy & romance";
    public string IconGlyph        => "\uE894"; // language (globe)
    public bool   RequiresCfBypass => false;

    // Browse the 玄幻 (xuanhuan/fantasy) category as the default "popular" listing
    public string GetRecentUrl(int page = 1) =>
        page == 1
            ? "https://www.quanben.io/c/xuanhuan.html"
            : $"https://www.quanben.io/c/xuanhuan_{page}.html";

    public string GetPopularUrl(int page = 1) =>
        page == 1
            ? "https://www.quanben.io/c/dushi.html"
            : $"https://www.quanben.io/c/dushi_{page}.html";

    public string GetSearchUrl(string query, int page = 1) =>
        $"https://www.quanben.io/search/{Uri.EscapeDataString(query)}/{page}.html";

    public ListingPage ParseListing(string html, string pageUrl)
    {
        var novels = new List<NovelEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Category page structure:
        //   <img src="...cover...">
        //   <h3><a href="/n/{bookId}/">Title</a></h3>
        //   作者: AuthorName
        //   Description text...
        //
        // Each book is in a block that contains an /n/{bookId}/ link.

        var blockPattern = new Regex(
            @"(<img[^>]+>[\s\S]{0,600}?<h3[\s\S]*?</h3>[\s\S]{0,400}?(?:作者|author)[：:\s]*[^<\n]{1,40})",
            RegexOptions.IgnoreCase);

        // Simpler approach: find all /n/{bookId}/ links and grab surrounding context
        var linkPattern = new Regex(
            @"href=[""'](?:https?://(?:www\.)?quanben\.io)?/n/([^""'/]+)/[""']",
            RegexOptions.IgnoreCase);

        // Walk through all /n/ links in document order
        foreach (Match m in linkPattern.Matches(html))
        {
            string bookId = m.Groups[1].Value;
            if (!seen.Add(bookId)) continue;

            // Grab a window of text around this link to extract title/author/cover
            int start  = Math.Max(0, m.Index - 600);
            int length = Math.Min(html.Length - start, 1200);
            string window = html.Substring(start, length);

            // Title: prefer <h3> or <h4> near the link
            string title = "";
            var titleM = Regex.Match(window,
                @"<h[34][^>]*>\s*(?:<a[^>]*>)?\s*([^<]{2,80})\s*(?:</a>)?\s*</h[34]>",
                RegexOptions.IgnoreCase);
            if (titleM.Success) title = System.Net.WebUtility.HtmlDecode(titleM.Groups[1].Value.Trim());

            // Fallback: link text itself
            if (string.IsNullOrWhiteSpace(title))
            {
                var aM = Regex.Match(window,
                    @"href=[""'][^""']*/n/" + Regex.Escape(bookId) + @"/[""'][^>]*>\s*([^<]{2,80})\s*</a>",
                    RegexOptions.IgnoreCase);
                if (aM.Success) title = System.Net.WebUtility.HtmlDecode(aM.Groups[1].Value.Trim());
            }
            if (string.IsNullOrWhiteSpace(title)) continue;

            // Cover: img src near this block
            string? cover = null;
            var imgM = Regex.Match(window,
                @"<img[^>]+src=[""'](https?://[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (imgM.Success) cover = imgM.Groups[1].Value;

            // Author: 作者: or 作者：
            string? author = null;
            var authM = Regex.Match(window, @"作者[：:\s]*([^\s<,，\n]{1,30})");
            if (authM.Success) author = authM.Groups[1].Value.Trim();

            // Description: short text after author line
            string? desc = null;
            var descM = Regex.Match(window,
                @"<p[^>]*>\s*([^\s<][^<]{15,200})\s*</p>",
                RegexOptions.IgnoreCase);
            if (descM.Success)
                desc = System.Net.WebUtility.HtmlDecode(descM.Groups[1].Value.Trim());

            novels.Add(new NovelEntry(
                title, author,
                $"https://www.quanben.io/n/{bookId}/list.html",
                cover, desc, null));
        }

        // Pagination: category pages use /c/{cat}_{page}.html or search uses /{page}.html
        bool hasNext = html.Contains("下一页");
        int currentPage = 1;
        var pageM = Regex.Match(pageUrl, @"_(\d+)\.html$");
        if (!pageM.Success) pageM = Regex.Match(pageUrl, @"/(\d+)\.html$");
        if (pageM.Success) int.TryParse(pageM.Groups[1].Value, out currentPage);

        return new ListingPage(novels, hasNext && novels.Count > 0, currentPage);
    }
}
