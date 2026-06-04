using System.Net;
using System.Text.RegularExpressions;

namespace Shuka.Core.Adapters;

/// <summary>
/// Adapter for situu.cc (思兔阅读) — a popular Chinese novel reader site.
/// </summary>
public class SituuAdapter : ISiteAdapter
{
    public string SiteName => "situu.cc";

    public bool Matches(string url) =>
        url.Contains("situu.cc", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        if (url.StartsWith("http://")) url = "https://" + url[7..];
        if (!url.StartsWith("http")) url = "https://" + url;

        // Strip chapter suffix: e.g. https://www.situu.cc/85_85861/22864833.html -> https://www.situu.cc/85_85861/
        url = Regex.Replace(url, @"\d+\.html$", "");
        if (!url.EndsWith("/")) url += "/";
        return url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        // Extract book ID (e.g. 85_85861)
        var idM = Regex.Match(indexUrl, @"/(\d+_\d+)(?:/|$)");
        string bookId = idM.Success ? idM.Groups[1].Value : "";

        // Extract Title
        string title = "";
        var titleM = Regex.Match(html, @"<div class=""book-describe"">[\s\S]*?<h1>([\s\S]*?)</h1>", RegexOptions.IgnoreCase);
        if (titleM.Success) title = titleM.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            var ogT = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!ogT.Success) ogT = Regex.Match(html, @"<meta[^>]+content=""([^""]+)""[^>]+property=""og:title""", RegexOptions.IgnoreCase);
            if (ogT.Success) title = ogT.Groups[1].Value.Trim();
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Regex.Match(html, @"<title[^>]*>([^<|_–\-]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        }
        title = Regex.Replace(title, @"\s*(最新章节|无弹窗|全文阅读|免费阅读).*$", "").Trim();

        // Extract Author
        string author = "Unknown";
        var am = Regex.Match(html, @"作者[：:]\s*<a[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase);
        if (!am.Success) am = Regex.Match(html, @"作者[：:]\s*([^\s【\n】<&]+)", RegexOptions.IgnoreCase);
        if (am.Success) author = am.Groups[1].Value.Trim();

        // Extract Chapters
        string listHtml = html;
        var listM = Regex.Match(html, @"<div[^>]+class=""book-list[^""]*""[^>]*>([\s\S]+?)</div>", RegexOptions.IgnoreCase);
        if (listM.Success)
        {
            listHtml = listM.Groups[1].Value;
        }

        var chapterMatches = Regex.Matches(listHtml,
            @"href=[""']([^""']*/" + Regex.Escape(bookId) + @"/\d+\.html)[""'][^>]*>([^<]*)</a>",
            RegexOptions.IgnoreCase);

        var chapters = chapterMatches
            .Cast<Match>()
            .Select(m => new
            {
                Url = m.Groups[1].Value.Trim(),
                Title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim())
            })
            .Select(x => new ChapterRef(
                x.Url.StartsWith("http") ? x.Url : new Uri(new Uri(indexUrl), x.Url).ToString(),
                string.IsNullOrWhiteSpace(x.Title) ? "Chapter" : x.Title
            ))
            .DistinctBy(x => x.Url)
            .ToList();

        // Extract Cover
        string? cover = null;
        var imgM = Regex.Match(html, @"<div class=""book-img"">[\s\S]*?<img[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
        if (imgM.Success) cover = imgM.Groups[1].Value.Trim();
        if (cover == null)
        {
            var og = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!og.Success) og = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
            if (og.Success) cover = og.Groups[1].Value.Trim();
        }
        if (cover != null && !cover.StartsWith("http"))
        {
            cover = new Uri(new Uri(indexUrl), cover).ToString();
        }
        if (cover == null && !string.IsNullOrEmpty(bookId))
        {
            var parts = bookId.Split('_');
            if (parts.Length == 2)
            {
                cover = $"https://www.situu.cc/files/article/image/{parts[0]}/{parts[1]}/{parts[1]}s.jpg";
            }
        }

        return new IndexInfo(title, author, chapters, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);

        // Body text is inside <div id="nr1"> or fallback to <div id="content">
        string? fragment = null;
        var m = Regex.Match(html, @"<div[^>]+id=""nr1""[^>]*>([\s\S]+?)</div>", RegexOptions.IgnoreCase);
        if (m.Success) fragment = m.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(fragment))
        {
            m = Regex.Match(html, @"<div[^>]+id=""content""[^>]*>([\s\S]+?)</div>", RegexOptions.IgnoreCase);
            if (m.Success) fragment = m.Groups[1].Value;
        }
        if (string.IsNullOrWhiteSpace(fragment)) fragment = html;

        // Convert breaks/paragraphs to newlines
        fragment = Regex.Replace(fragment, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, @"<p[^>]*>", "\n", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, @"<[^>]+>", "");

        string text = System.Net.WebUtility.HtmlDecode(fragment);

        var result = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            string trimmed = line.Trim().TrimStart('\u3000').Trim();
            if (trimmed.Length == 0) continue;

            // Situu specific noise lines
            if (trimmed.Contains("chuxianovel.com") ||
                trimmed.Contains("situu") ||
                trimmed.Contains("初夏小说") ||
                trimmed.Contains("支持书架") ||
                trimmed.Contains("www.") ||
                trimmed.Contains("http") ||
                trimmed.Contains("目录") ||
                trimmed.Contains("上一章") ||
                trimmed.Contains("下一章"))
            {
                continue;
            }

            // Keep lines containing CJK characters
            if (Regex.IsMatch(trimmed, @"[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff]"))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
