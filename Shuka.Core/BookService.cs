using System.Text.RegularExpressions;
using Shuka.Core.Adapters;

namespace Shuka.Core;

/// <summary>
/// Orchestrates gathering book info, downloading chapters, translating, and building the EPUB.
/// Platform-agnostic — used by both the Windows CLI and the Android app.
/// </summary>
public class BookService
{
    private readonly HttpFetcher _fetcher;
    private readonly HttpClient  _gtClient;
    private readonly Translator  _translator;

    private static readonly ISiteAdapter[] Adapters =
        [new ShukuAdapter(), new CzBooksAdapter(), new DmxsAdapter(), new ShubaAdapter(), new QuanbenAdapter()];

    public BookService(ICloudflareBypass? cfBypass = null)
    {
        _fetcher = new HttpFetcher(cfBypass);

        var gh = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        };
        _gtClient = new HttpClient(gh) { Timeout = TimeSpan.FromSeconds(60) };
        _gtClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 10; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");
        _gtClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");

        _translator = new Translator(_gtClient);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<BookInfo> GatherBookInfo(string indexUrl, int chapterLimit = 0,
        string? forceCoverUrl = null, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var adapter = DetectAdapter(indexUrl);
        indexUrl = adapter.NormalizeUrl(indexUrl);
        log?.Invoke($"Gathering [{adapter.SiteName}]: {indexUrl}");

        string html = await _fetcher.Fetch(indexUrl, log: log, ct: ct);
        var info = adapter.ParseIndex(html, indexUrl);
        int total = chapterLimit > 0 ? Math.Min(chapterLimit, info.ChapterUrls.Count) : info.ChapterUrls.Count;
        string? coverUrl = forceCoverUrl ?? info.CoverUrl ?? TryExtractCover(html, indexUrl);

        return new BookInfo(indexUrl, info.Title, info.Author, info.ChapterUrls, total, chapterLimit, coverUrl, adapter);
    }

    public async Task<string> ProcessBook(BookInfo book, string outputPath,
        IProgress<ProgressEventArgs>? progress = null, Action<string>? log = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        log?.Invoke("Translating title/author...");

        // Run title/author translation and cover download in parallel
        var titleTask  = _translator.Translate(book.Title,  log, ct);
        var authorTask = _translator.Translate(book.Author, log, ct);
        var coverTask  = DownloadCover(book.CoverUrl, log);

        await Task.WhenAll(titleTask, authorTask, coverTask);

        book.TitleEn  = titleTask.Result;
        book.AuthorEn = authorTask.Result;
        var (coverBytes, coverMime) = coverTask.Result;

        log?.Invoke($"Title (EN): {book.TitleEn}  Author (EN): {book.AuthorEn}");

        ct.ThrowIfCancellationRequested();
        var chapters = await DownloadChapters(book, progress, log, ct);

        ct.ThrowIfCancellationRequested();
        log?.Invoke("Building EPUB...");
        if (File.Exists(outputPath)) File.Delete(outputPath);
        EpubBuilder.Build(outputPath, book.Title, book.TitleEn!, book.Author, book.AuthorEn!,
            chapters, coverBytes, coverMime);

        return outputPath;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sequential fetch + translate pipeline.
    /// Fetches and translates one chapter at a time — simple, reliable, no deadlocks.
    /// Progress is reported after each chapter completes.
    /// </summary>
    private async Task<List<(int Idx, string Title, string Text)>> DownloadChapters(
        BookInfo book, IProgress<ProgressEventArgs>? progress, Action<string>? log,
        CancellationToken ct = default)
    {
        var chapterList = book.ChapterUrls.Take(book.Total).ToList();
        int total = chapterList.Count;
        var results = new List<(int Idx, string Title, string Text)>(total);

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var ch = chapterList[i];

            // Fetch with per-chapter retry
            string html = "";
            for (int fetchAttempt = 1; fetchAttempt <= 5; fetchAttempt++)
            {
                try
                {
                    html = await _fetcher.Fetch(ch.Url, log: log, ct: ct);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (fetchAttempt == 5)
                    {
                        log?.Invoke($"[fetch failed ch{i + 1} after 5 attempts] {ex.Message}");
                        html = "";
                    }
                    else
                    {
                        int delaySec = Math.Min(fetchAttempt * 2, 10);
                        log?.Invoke($"[fetch retry {fetchAttempt}/5 ch{i + 1}] {ex.Message} — waiting {delaySec}s");
                        await Task.Delay(delaySec * 1000, ct);
                    }
                }
            }

            // Translate with per-chapter retry — more attempts + longer backoff
            // for large novels where Google rate-limits more aggressively
            var paras = book.Adapter.ExtractChapterText(html);
            string text = "";
            if (paras.Count > 0)
            {
                for (int transAttempt = 1; transAttempt <= 6; transAttempt++)
                {
                    try
                    {
                        text = await _translator.Translate(string.Join("\n", paras), log, ct);
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        if (transAttempt == 6)
                        {
                            log?.Invoke($"[translate failed ch{i + 1} after 6 attempts] {ex.Message} — keeping original");
                            text = string.Join("\n", paras);
                        }
                        else
                        {
                            // Exponential backoff: 2s, 4s, 8s, 16s, 30s max
                            int delaySec = Math.Min((int)Math.Pow(2, transAttempt), 30);
                            log?.Invoke($"[translate retry {transAttempt}/6 ch{i + 1}] waiting {delaySec}s...");
                            await Task.Delay(delaySec * 1000, ct);
                        }
                    }
                }
            }

            results.Add((i + 1, ch.Title, text));

            progress?.Report(new ProgressEventArgs
            {
                Current = i + 1,
                Total   = total,
                Message = $"Translated chapter {i + 1} of {total}..."
            });
        }

        return results;
    }

    private async Task<(byte[]? bytes, string mime)> DownloadCover(string? coverUrl, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return (null, "image/jpeg");
        log?.Invoke("Downloading cover...");
        try
        {
            byte[] bytes = await _gtClient.GetByteArrayAsync(coverUrl);
            string ext = Path.GetExtension(new Uri(coverUrl).AbsolutePath).ToLowerInvariant();
            string mime = ext switch { ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg" };
            if (bytes.Length >= 4)
            {
                if (bytes[0] == 0x89 && bytes[1] == 0x50) mime = "image/png";
                else if (bytes[0] == 0xFF && bytes[1] == 0xD8) mime = "image/jpeg";
                else if (bytes[0] == 0x47 && bytes[1] == 0x49) mime = "image/gif";
            }
            log?.Invoke($"Cover OK ({bytes.Length / 1024}KB, {mime})");
            return (bytes, mime);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Cover failed: {ex.Message} (using generated cover)");
            return (null, "image/jpeg");
        }
    }

    private static ISiteAdapter DetectAdapter(string url) =>
        Adapters.FirstOrDefault(a => a.Matches(url))
        ?? throw new Exception($"No supported adapter for URL: {url}\nSupported: 52shuku.net, czbooks.net, dmxs.org, 69shuba.com, quanben.io");

    private static string? TryExtractCover(string html, string baseUrl)
    {
        var og = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!og.Success)
            og = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
        if (og.Success) return og.Groups[1].Value.Trim();

        var img = Regex.Match(html, @"<img[^>]+src=[""']([^""']+cover[^""']*)[""']", RegexOptions.IgnoreCase);
        if (img.Success)
        {
            string src = img.Groups[1].Value.Trim();
            return src.StartsWith("http") ? src : new Uri(new Uri(baseUrl), src).ToString();
        }
        return null;
    }
}
