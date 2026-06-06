using System.Net;
using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Site adapter for yamibo.com (百合会) — a curated yuri/GL novel community.
///
/// Novel index URL format : https://www.yamibo.com/novel/{id}
/// Chapter URL format     : https://www.yamibo.com/novel/view-chapter?id={chapterId}
///
/// Cover image pattern    : https://www.yamibo.com/covern/000/{padded_id}.jpg
/// No Cloudflare protection.
/// </summary>
public class YamiboAdapter : ISiteAdapter
{
    public string SiteName => "yamibo.com";
    public bool RequiresCfBypass => true;

    public bool Matches(string url) =>
        url.Contains("yamibo.com", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        // Ensure HTTPS
        if (url.StartsWith("http://")) url = "https://" + url[7..];
        if (!url.StartsWith("http"))   url = "https://" + url;
        return url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        // ── Title ────────────────────────────────────────────────────────────────
        // <h3 class="col-md-12" ...>Title</h3>
        string title = Regex.Match(html,
            @"<h3[^>]*class=""[^""]*col-md-12[^""]*""[^>]*>\s*([^<]+?)\s*</h3>",
            RegexOptions.IgnoreCase).Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            // Fallback: page <title> tag  → "Title - 百合会"
            title = Regex.Match(html,
                @"<title>\s*([^<]+?)\s*-\s*百合会\s*</title>",
                RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        }

        // ── Author ───────────────────────────────────────────────────────────────
        // Each chapter page shows: 作者：Name   (not on index, but sidebar "作者" panel)
        // On the index page, author appears in the sidebar author panel <h3>作者</h3>
        // and below that a link.  We look for it two ways:
        string author = "Unknown";

        // 1) Sidebar author block with img-circle avatar and h5 username
        var authorPanel = Regex.Match(html,
            @"href=""/user/space\?id=\d+""[^>]*>\s*<img[^>]+class=""[^""]*img-circle[^""]*""[^>]*>\s*</a>\s*</p>\s*<h5[^>]*>\s*([^<]+?)\s*</h5>",
            RegexOptions.IgnoreCase);
        if (authorPanel.Success)
            author = WebUtility.HtmlDecode(authorPanel.Groups[1].Value.Trim());

        // 2) Older sidebar style: <h3 class="panel-title pull-left">作者</h3> ... <a href="/user/space?id=...">Name</a>
        if (author == "Unknown")
        {
            var oldPanel = Regex.Match(html,
                @"<h3[^>]*>\s*作者\s*</h3>[\s\S]{0,500}?<a[^>]+href=""/user/space\?id=\d+""[^>]*>\s*([^<]+?)\s*</a>",
                RegexOptions.IgnoreCase);
            if (oldPanel.Success)
                author = WebUtility.HtmlDecode(oldPanel.Groups[1].Value.Trim());
        }

        // 3) Fallback: breadcrumb <a href="/user/space?id=...">Author</a> next to chapter author line
        if (author == "Unknown")
        {
            var am = Regex.Match(html,
                @"作者[：:]\s*<a[^>]+href=""/user/space\?id=\d+""[^>]*>\s*([^<]+?)\s*</a>",
                RegexOptions.IgnoreCase);
            if (am.Success)
                author = WebUtility.HtmlDecode(am.Groups[1].Value.Trim());
        }

        // ── Chapter list ─────────────────────────────────────────────────────────
        // <div data-key="{chapterId}"><div class="col-md-4 col-xs-6">
        //   <a class="margin-r-5" href="/novel/view-chapter?id={chapterId}">Chapter Title</a>
        // </div></div>
        var chapterUrls = Regex.Matches(html,
            @"<a[^>]+href=""(/novel/view-chapter\?id=(\d+))""[^>]*>\s*([^<]+?)\s*</a>",
            RegexOptions.IgnoreCase)
            .Select(m => new ChapterRef(
                "https://www.yamibo.com" + m.Groups[1].Value,
                WebUtility.HtmlDecode(m.Groups[3].Value.Trim())))
            .Where(c => !string.IsNullOrWhiteSpace(c.Title))
            .GroupBy(c => c.Url)   // deduplicate
            .Select(g => g.First())
            .ToList();

        // ── Cover ────────────────────────────────────────────────────────────────
        // <img class="img-responsive" src="/covern/000/267/137.jpg?=..." alt="">
        string? cover = null;
        var coverM = Regex.Match(html,
            @"<img[^>]+class=""img-responsive""[^>]+src=""(/covern/[^""]+)""",
            RegexOptions.IgnoreCase);
        if (coverM.Success)
            cover = "https://www.yamibo.com" + coverM.Groups[1].Value.Split('?')[0];

        return new IndexInfo(title, author, chapterUrls, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        // Chapter body is in:
        //   <div id="txt" class="panel-warning chapter panel panel-default">
        //     <div class="panel-body">
        //       <p>...paragraph...</p>  (may contain <br />, HTML entities)
        //     </div>
        //   </div>
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);

        // Extract just the chapter body div
        string fragment = TryExtractChapterBody(html) ?? html;

        var result = new List<string>();

        // Split on <p> tags
        foreach (Match m in Regex.Matches(fragment,
            @"<p(?:\s[^>]*)?>([^<]*(?:<(?!/p>)[^<]*)*)</p>",
            RegexOptions.IgnoreCase))
        {
            string inner = m.Groups[1].Value;
            // Strip any remaining inline tags (e.g. <br />)
            inner = Regex.Replace(inner, @"<[^>]+>", " ");
            inner = WebUtility.HtmlDecode(inner);
            inner = inner.Replace("\u3000", " ").Trim();

            if (inner.Length == 0) continue;
            // Only keep lines that contain at least one CJK character
            if (Regex.IsMatch(inner, @"[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff\u3000-\u303f\uff00-\uffef]"))
                result.Add(inner);
        }

        return result;
    }

    private static string? TryExtractChapterBody(string html)
    {
        // The chapter text lives in: <div id="txt" class="panel-warning chapter panel panel-default">
        //   <div class="panel-heading">...</div>
        //   <div id="w0-collapse1" class="in panel-collapse collapse">
        //     <div class="panel-body"> TEXT HERE </div>
        //   </div>
        // </div>
        var bodyMatch = Regex.Match(html,
            @"<div[^>]+\bid=""txt""[^>]*>[\s\S]*?<div[^>]+class=""panel-body""[^>]*>([\s\S]*?)</div>\s*</div>\s*</div>\s*</div>",
            RegexOptions.IgnoreCase);
        return bodyMatch.Success ? bodyMatch.Groups[1].Value : null;
    }
}
