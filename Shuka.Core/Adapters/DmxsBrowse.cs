using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Browse/discover support for dmxs.org (耽美小说).
///
/// Recent:  https://www.dmxs.org/news_last/                    (page 1)
///          https://www.dmxs.org/news_last/index_{page}.html   (page 2+)
/// Popular: https://www.dmxs.org/  (homepage hot section — popular requires login, use homepage)
/// Search:  https://www.dmxs.org/e/search/index.php?searchword={query}&amp;page={page}
///
/// Book URLs: https://www.dmxs.org/{category}/{id}.html
/// No Cloudflare protection.
/// </summary>
public class DmxsBrowse : IBrowsableAdapter
{
    public string SiteName         => "dmxs.org";
    public string Description      => "BL danmei · boys love novels";
    public string IconGlyph        => "\uE894"; // language (globe)
    public bool   RequiresCfBypass => false;

    public string GetRecentUrl(int page = 1) =>
        page == 1
            ? "https://www.dmxs.org/news_last/"
            : $"https://www.dmxs.org/news_last/index_{page}.html";

    public string GetPopularUrl(int page = 1) =>
        // Homepage shows hot section; no paginated popular without login
        "https://www.dmxs.org/";

    public string GetSearchUrl(string query, int page = 1) =>
        $"https://www.dmxs.org/e/search/index.php?searchword={Uri.EscapeDataString(query)}&page={page}";

    public ListingPage ParseListing(string html, string pageUrl)
    {
        var novels = new List<NovelEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // dmxs book URLs: /{category}/{numericId}.html
        // e.g. /book/23204.html  /cycs/23202.html  /gdjk/23200.html
        // The recent page wraps each entry in a block with title, author, size, date, description.
        // Pattern: href="/{category}/{id}.html" followed by title text

        // Try structured article/entry blocks first (recent page format)
        var blockPattern = new Regex(
            @"<a\s+href=[""']((?:https?://(?:www\.)?dmxs\.org)?/[a-zA-Z]+/(\d+)\.html)[""'][^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        foreach (Match m in blockPattern.Matches(html))
        {
            string href   = m.Groups[1].Value;
            string bookId = m.Groups[2].Value;
            string inner  = m.Groups[3].Value;

            // Skip navigation/category links (no numeric ID)
            if (string.IsNullOrEmpty(bookId)) continue;
            // Skip pagination links
            if (href.Contains("index_")) continue;

            string url = href.StartsWith("http")
                ? href
                : "https://www.dmxs.org" + href;

            if (!seen.Add(bookId)) continue;

            // Title: strip HTML tags from link inner text
            string title = Regex.Replace(inner, @"<[^>]+>", "").Trim();
            title = System.Net.WebUtility.HtmlDecode(title);

            // The recent page format is: "Title 作者：AuthorName [date]"
            // Extract author from title string if present
            string? author = null;
            var authInTitle = Regex.Match(title, @"\s+作者[：:]\s*([^\s\[【]+)");
            if (authInTitle.Success)
            {
                author = authInTitle.Groups[1].Value.Trim();
                title  = title[..authInTitle.Index].Trim();
            }

            // Strip trailing date/size noise like "[05-08]" or "2026-05-08"
            title = Regex.Replace(title, @"\s*[\[\(（【]\d{2,4}[-/]\d{2}[-/]?\d{0,2}[\]\)）】]?\s*$", "").Trim();

            if (title.Length < 2) continue;

            novels.Add(new NovelEntry(title, author, url, null, null, null));
        }

        // Fallback: scan for any /{category}/{id}.html links with adjacent text
        if (novels.Count == 0)
        {
            var linkPattern = new Regex(
                @"href=[""'](?:https?://(?:www\.)?dmxs\.org)?(/[a-zA-Z]+/(\d+)\.html)[""'][^>]*>\s*([^<]{2,80})\s*</a>",
                RegexOptions.IgnoreCase);
            foreach (Match m in linkPattern.Matches(html))
            {
                string bookId = m.Groups[2].Value;
                if (!seen.Add(bookId)) continue;
                string title = System.Net.WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
                if (title.Length < 2) continue;
                string url = "https://www.dmxs.org" + m.Groups[1].Value;
                novels.Add(new NovelEntry(title, null, url, null, null, null));
            }
        }

        // Pagination: recent pages use index_{page}.html
        bool hasNext = html.Contains("下一页") ||
                       Regex.IsMatch(html, @"index_\d+\.html", RegexOptions.IgnoreCase);
        int currentPage = 1;
        var pageM = Regex.Match(pageUrl, @"index_(\d+)\.html$");
        if (pageM.Success) int.TryParse(pageM.Groups[1].Value, out currentPage);

        return new ListingPage(novels, hasNext && novels.Count > 0, currentPage);
    }
}
