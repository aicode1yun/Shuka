using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Usage
if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Normal:  Shuka <index-url> [chapters] [output.epub] [cover-url]");
    Console.WriteLine("  Batch:   Shuka --batch <urls-file.txt>");
    Console.WriteLine("  CF fix:  Shuka --solve-cf <site-url>");
    Console.WriteLine();
    Console.WriteLine("  chapters = how many chapters to download (0 = all)");
    Console.WriteLine();
    Console.WriteLine("  Supported sites:");
    Console.WriteLine("    52shuku.net  — e.g. https://www.52shuku.net/bl/09_b/bkd7d.html");
    Console.WriteLine("    czbooks.net  — e.g. https://czbooks.net/n/clgajm");
    Console.WriteLine("    dmxs.org     — e.g. https://www.dmxs.org/GLBH/1840.html");
    Console.WriteLine("    69shuba.com  — e.g. https://www.69shuba.com/book/90488.htm");
    return;
}

// Playwright browser install passthrough (used by installer)
if (args.Length >= 2 && args[0] == "playwright" && args[1] == "install")
{
    var exitCode = Microsoft.Playwright.Program.Main(args.Skip(1).ToArray());
    Environment.Exit(exitCode);
    return;
}

// --solve-cf: open a visible browser so the user can solve the CF challenge once.
// The cookies are saved to the persistent profile and reused by all future headless runs.
if (args.Length >= 2 && args[0] == "--solve-cf")
{
    string targetUrl = args[1];
    string userDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shuka", "browser-profile");
    Directory.CreateDirectory(userDataDir);

    Console.WriteLine($"Opening browser for: {targetUrl}");
    Console.WriteLine("A browser window will open. Wait for the page to fully load.");
    Console.WriteLine("Once you see the actual page content (not a security check), press Enter here.");
    Console.WriteLine();

    var pw2 = await Playwright.CreateAsync();
    var ctx2 = await pw2.Chromium.LaunchPersistentContextAsync(userDataDir, new()
    {
        Headless = false,
        Args = new[] { "--disable-blink-features=AutomationControlled" },
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        ViewportSize = new() { Width = 1280, Height = 800 },
    });
    var pg2 = await ctx2.NewPageAsync();
    await pg2.GotoAsync(targetUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60000 });

    // Auto-detect when challenge clears (up to 60s), but also let user press Enter manually
    Console.Write("Waiting for page to load");
    var cts2 = new CancellationTokenSource();
    var autoWait = Task.Run(async () =>
    {
        var dl2 = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < dl2 && !cts2.Token.IsCancellationRequested)
        {
            try
            {
                string t2 = await pg2.TitleAsync();
                bool isChal = t2.Contains("Just a moment") || t2.Contains("请稍候") ||
                              t2.Contains("Checking") || t2.Contains("Security") ||
                              string.IsNullOrWhiteSpace(t2);
                if (!isChal)
                {
                    Console.WriteLine($"\nPage loaded: {t2}");
                    return;
                }
            }
            catch { /* page may be navigating */ }
            Console.Write(".");
            await Task.Delay(1000);
        }
    }, cts2.Token);

    // Also let user press Enter to confirm manually
    var manualWait = Task.Run(() => { Console.ReadLine(); });
    await Task.WhenAny(autoWait, manualWait);
    cts2.Cancel();

    // Extra wait to ensure cookies are fully written to disk
    await Task.Delay(2000);
    await ctx2.CloseAsync();
    pw2.Dispose();

    Console.WriteLine("CF cookies saved to browser profile.");
    Console.WriteLine("Downloads from this site should now work without retries.");
    return;
}

// HTTP client for sites
var sh = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
using var site = new HttpClient(sh) { Timeout = TimeSpan.FromSeconds(30) };
site.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
site.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,zh-CN;q=0.8");
site.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

// HTTP client for Google Translate and cover downloads (clean, no site headers)
var gh = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
using var gt = new HttpClient(gh) { Timeout = TimeSpan.FromSeconds(45) };
gt.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

// Fetch page as properly decoded string (auto-detects charset)
// Falls back to Playwright headless browser if Cloudflare blocks the request
async Task<string> Fetch(string url, int retries = 4)
{
    // Known Cloudflare-protected sites — skip HTTP client entirely, go straight to Playwright
    if (url.Contains("69shuba.com", StringComparison.OrdinalIgnoreCase))
        return await FetchWithPlaywrightVerified(url);

    int delay = 1000;
    Exception? last = null;
    for (int i = 0; i <= retries; i++)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var uri = new Uri(url);
            req.Headers.Add("Referer", $"{uri.Scheme}://{uri.Host}/");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var resp = await site.SendAsync(req, cts.Token);

            bool isCf = resp.Headers.Contains("cf-ray") || resp.Headers.Server.ToString().Contains("cloudflare");
            if (isCf && ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 503))
                return await FetchWithPlaywrightVerified(url);

            resp.EnsureSuccessStatusCode();
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
            string latin1 = Encoding.Latin1.GetString(bytes);

            // Detect Cloudflare managed challenge served as 200 (with or without CF headers)
            if (latin1.Contains("cf-chl-opt", StringComparison.OrdinalIgnoreCase))
                return await FetchWithPlaywrightVerified(url);

            // Check HTTP Content-Type header first, then HTML meta tag
            string charset = "utf-8";
            string? ctHeader = resp.Content.Headers.ContentType?.CharSet;
            if (!string.IsNullOrWhiteSpace(ctHeader))
            {
                charset = ctHeader.Trim().Trim('"');
            }
            else
            {
                string head = latin1[..Math.Min(latin1.Length, 4096)];
                var cm = Regex.Match(head, @"charset\s*=\s*[""']?\s*([\w-]+)", RegexOptions.IgnoreCase);
                if (cm.Success) charset = cm.Groups[1].Value.Trim();
            }

            charset = charset.ToLowerInvariant() switch
            {
                "gb2312" or "gb_2312" or "csgb2312" or "x-gbk" or "chinese" => "gbk",
                "big5"   or "csbig5"  or "x-x-big5"                         => "big5",
                _                                                             => charset
            };

            Encoding enc;
            try   { enc = Encoding.GetEncoding(charset); }
            catch { enc = Encoding.UTF8; }
            string result = enc.GetString(bytes);

            // Final check: if we got a CF challenge page despite no CF headers, retry via Playwright
            if (result.Contains("cf-chl-opt", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("请稍候", StringComparison.OrdinalIgnoreCase) && result.Contains("cloudflare", StringComparison.OrdinalIgnoreCase))
                return await FetchWithPlaywrightVerified(url);

            return result;
        }
        catch (Exception ex) { last = ex; await Task.Delay(delay); delay = Math.Min(delay * 2, 16000); }
    }
    throw new Exception($"Fetch failed: {url} — {last?.Message}");
}

// Playwright-based fetch for Cloudflare-protected sites
// Reuses a single browser instance across calls for performance
IPlaywright? _playwright = null;
IBrowserContext? _context = null;
SemaphoreSlim _playwrightSem = new(1, 1);

// Wrapper: calls FetchWithPlaywright and verifies the result isn't still a challenge page
async Task<string> FetchWithPlaywrightVerified(string url, int maxAttempts = 3)
{
    await _playwrightSem.WaitAsync();
    try
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string html = await FetchWithPlaywright(url);

            // Check if we still got a challenge page — by title poll or body content
            bool isChallenge = html.Contains("cf-chl-opt", StringComparison.OrdinalIgnoreCase) ||
                               (html.Contains("请稍候", StringComparison.OrdinalIgnoreCase) &&
                                html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)) ||
                               (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) &&
                                html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase));

            if (!isChallenge) return html;

            if (attempt < maxAttempts)
            {
                Console.Write($" [CF retry {attempt}/{maxAttempts}, waiting {attempt * 4}s]");
                await Task.Delay(attempt * 4000);
            }
        }
        // Last attempt — return whatever we get
        return await FetchWithPlaywright(url);
    }
    finally
    {
        _playwrightSem.Release();
    }
}

async Task<string> FetchWithPlaywright(string url)
{
    if (_playwright == null)
    {
        // Auto-install Chromium if missing or version has changed since last install.
        // The marker file stores the Playwright assembly version; if it differs from
        // the current package version the browser binaries are stale and need updating.
        string playwrightVersion = typeof(Microsoft.Playwright.Playwright)
            .Assembly.GetName().Version?.ToString() ?? "unknown";
        string markerPath = Path.Combine(AppContext.BaseDirectory, ".playwright-version");
        string installedVersion = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : "";

        if (installedVersion != playwrightVersion)
        {
            Console.WriteLine(installedVersion == ""
                ? "\n  [cloudflare] Installing browser (first run)..."
                : $"\n  [cloudflare] Updating browser ({installedVersion} → {playwrightVersion})...");
            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0)
                throw new Exception($"Playwright browser install failed (exit code {exitCode}). Run: Shuka.exe playwright install chromium");
            File.WriteAllText(markerPath, playwrightVersion);
        }

        Console.WriteLine("\n  [cloudflare] Starting headless browser...");
        _playwright = await Playwright.CreateAsync();

        // Use a persistent user data directory so CF cookies/tokens survive between runs.
        // After the first successful challenge solve, subsequent runs skip it entirely.
        string userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Shuka", "browser-profile");
        Directory.CreateDirectory(userDataDir);

        // LaunchPersistentContext combines browser launch + context creation.
        // The persistent profile stores CF clearance cookies across sessions.
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(userDataDir, new()
        {
            Headless = true,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-dev-shm-usage",
            },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.7",
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
            },
            ViewportSize = new() { Width = 1280, Height = 800 },
        });
        await _context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, 'plugins',  { get: () => [1, 2, 3] });
            Object.defineProperty(navigator, 'languages',{ get: () => ['zh-CN','zh','en'] });
            window.chrome = { runtime: {} };
        ");
    }

    var page = await _context!.NewPageAsync();
    try
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load, Timeout = 45000 });

        // Poll title until CF challenge clears — up to 40s
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            string t = await page.TitleAsync();
            bool isChallenge = t.Contains("Just a moment") ||
                               t.Contains("Checking your browser") ||
                               t.Contains("Please Wait") ||
                               t.Contains("Security Check") ||
                               t.Contains("请稍候") ||
                               t.Contains("验证") ||
                               string.IsNullOrWhiteSpace(t);
            if (!isChallenge) break;
            Console.Write(".");
            await Task.Delay(1000);
        }

        await Task.Delay(1500);
        return await page.ContentAsync();
    }
    finally
    {
        await page.CloseAsync();
    }
}

// Translate a single chunk — tries Google mobile web page, then gtx GET/POST, then MyMemory
async Task<string> TranslateChunk(string chunk)
{
    // Phase 1: Google Translate mobile web page — scrapes the result-container div.
    // This endpoint is NOT rate-limited like the gtx API and works even when the API is blocked.
    // Limit: ~1000 chars per request (URL length). Chunks are split in Translate() already.
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            // The mobile page accepts up to ~1000 chars in the query string reliably
            string truncated = chunk.Length > 900 ? chunk[..900] : chunk;
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://translate.google.com/m?sl=zh-CN&tl=en&q={Uri.EscapeDataString(truncated)}");
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            using var cts0 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await gt.SendAsync(req, cts0.Token);
            if (resp.IsSuccessStatusCode)
            {
                string html = await resp.Content.ReadAsStringAsync();
                // Result is in: <div class="result-container">TRANSLATED TEXT</div>
                var m = Regex.Match(html, @"class=""result-container""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string r = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                    if (!string.IsNullOrEmpty(r)) return r;
                }
            }
        }
        catch (Exception ex)
        {
            if (attempt < 3) await Task.Delay(300 * attempt);
            else Console.Write($"\n  [Google web retry {attempt}/3: {ex.Message}]");
        }
    }

    // Phase 2: Google gtx GET (JSON API — may be rate-limited but worth trying)
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://translate.googleapis.com/translate_a/single" +
                $"?client=gtx&sl=zh-CN&tl=en&dt=t&q={Uri.EscapeDataString(chunk)}");
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "application/json, text/plain, */*");
            req.Headers.Add("Referer", "https://translate.google.com/");
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await gt.SendAsync(req, cts1.Token);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                if (!json.TrimStart().StartsWith('<'))
                {
                    string? r = ParseGoogleJson(json);
                    if (r != null) return r;
                }
            }
        }
        catch { if (attempt < 2) await Task.Delay(400); }
    }

    // Phase 3: Google gtx POST
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://translate.googleapis.com/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t");
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Referer", "https://translate.google.com/");
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", chunk) });
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await gt.SendAsync(req, cts2.Token);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                if (!json.TrimStart().StartsWith('<'))
                {
                    string? r = ParseGoogleJson(json);
                    if (r != null) return r;
                }
            }
        }
        catch { if (attempt < 2) await Task.Delay(400); }
    }

    // Phase 4: MyMemory fallback
    Console.Write("\n  [trying MyMemory...]");
    try
    {
        string? mm = await CallMyMemory(chunk);
        if (mm != null) { Console.Write(" [ok]"); return mm; }
    }
    catch (Exception ex) { Console.Write($" [failed: {ex.Message}]"); }

    Console.WriteLine("\n  [all translators failed — keeping original]");
    return chunk;
}

static string? ParseGoogleJson(string json)
{
    using var jdoc = JsonDocument.Parse(json);
    var root = jdoc.RootElement;
    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
    var first = root[0];
    if (first.ValueKind != JsonValueKind.Array) return null;
    var sb = new StringBuilder();
    foreach (var seg in first.EnumerateArray())
        if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0
            && seg[0].ValueKind == JsonValueKind.String)
            sb.Append(seg[0].GetString());
    string r = sb.ToString().Trim();
    return string.IsNullOrEmpty(r) ? null : r;
}

async Task<string?> CallMyMemory(string chunk)
{
    // MyMemory caps at 500 chars — split and reassemble
    if (chunk.Length > 450)
    {
        var parts = new List<string>();
        for (int i = 0; i < chunk.Length; i += 450)
            parts.Add(chunk.Substring(i, Math.Min(450, chunk.Length - i)));
        var results = new List<string>();
        foreach (var part in parts)
        {
            string? r = await CallMyMemory(part);
            if (r == null) return null;
            results.Add(r);
            if (parts.Count > 1) await Task.Delay(100);
        }
        return string.Join("", results);
    }

    using var req = new HttpRequestMessage(HttpMethod.Get,
        $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(chunk)}&langpair=zh-CN|en-US");
    req.Headers.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var resp = await gt.SendAsync(req, cts.Token);
    if (!resp.IsSuccessStatusCode) return null;
    string json = await resp.Content.ReadAsStringAsync();
    using var jdoc = JsonDocument.Parse(json);
    if (jdoc.RootElement.GetProperty("responseStatus").GetInt32() != 200) return null;
    string? result = jdoc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
    return !string.IsNullOrWhiteSpace(result) && result != chunk ? result.Trim() : null;
}

// Split text into chunks and translate in parallel
async Task<string> Translate(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return text;
    var lines  = text.Split('\n');
    var chunks = new List<string>();
    var cur    = new StringBuilder();
    foreach (var line in lines)
    {
        if (cur.Length + line.Length + 1 > 800 && cur.Length > 0)
        { chunks.Add(cur.ToString()); cur.Clear(); }
        if (cur.Length > 0) cur.Append('\n');
        cur.Append(line);
    }
    if (cur.Length > 0) chunks.Add(cur.ToString());

    // Translate all chunks in parallel (max 3 concurrent to avoid rate-limiting)
    var sem = new SemaphoreSlim(3);
    var tasks = chunks.Select(async (chunk, i) =>
    {
        await sem.WaitAsync();
        try   { return (i, text: await TranslateChunk(chunk)); }
        finally { sem.Release(); }
    }).ToList();

    var results = await Task.WhenAll(tasks);
    return string.Join("\n", results.OrderBy(r => r.i).Select(r => r.text));
}

// Try to extract a cover image URL from og:image meta tag
static string? TryExtractCover(string html, string baseUrl)
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

// Download cover image bytes, detect mime type from magic bytes
async Task<(byte[]? bytes, string mime)> DownloadCover(string? coverUrl)
{
    if (string.IsNullOrWhiteSpace(coverUrl)) return (null, "image/jpeg");
    Console.Write("  Downloading cover...");
    try
    {
        byte[] bytes = await gt.GetByteArrayAsync(coverUrl);
        string ext = Path.GetExtension(new Uri(coverUrl).AbsolutePath).ToLowerInvariant();
        string mime = ext switch { ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg" };
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0x89 && bytes[1] == 0x50) mime = "image/png";
            else if (bytes[0] == 0xFF && bytes[1] == 0xD8) mime = "image/jpeg";
            else if (bytes[0] == 0x47 && bytes[1] == 0x49) mime = "image/gif";
        }
        Console.WriteLine($" OK ({bytes.Length / 1024}KB, {mime})");
        return (bytes, mime);
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Failed: {ex.Message} (using generated cover)");
        return (null, "image/jpeg");
    }
}

// Download and translate all chapters for a book (fetch-ahead pipeline)
async Task<List<(int Idx, string Title, string Text)>> DownloadChapters(BookInfo book)
{
    // Use concurrency 1 for CF-protected sites — Playwright fetches must be serialized
    // so the CF cookie is established before the next fetch starts.
    // (The _playwrightSem inside FetchWithPlaywrightVerified already enforces this,
    //  but limiting here avoids queuing up many simultaneous waits.)
    var fetchSem = new SemaphoreSlim(1);
    var t0 = DateTime.Now;

    // Kick off all fetches immediately (gated by semaphore)
    var fetchTasks = book.ChapterUrls.Take(book.Total).Select(async (ch, i) =>
    {
        await fetchSem.WaitAsync();
        try   { return (i, title: ch.Title, html: await Fetch(ch.Url)); }
        finally { fetchSem.Release(); }
    }).ToArray();

    var chapters = new List<(int Idx, string Title, string Text)>(book.Total);

    for (int i = 0; i < book.Total; i++)
    {
        double elapsed = (DateTime.Now - t0).TotalSeconds;
        string eta = i > 0 ? $"~{TimeSpan.FromSeconds(elapsed / i * (book.Total - i)):mm\\:ss} left" : "";
        Console.Write($"\r  [{i+1}/{book.Total}] Translating... {eta}      ");

        var (_, chTitle, html) = await fetchTasks[i];
        var paras = book.Adapter.ExtractChapterText(html);

        string english = await Translate(string.Join("\n", paras));
        chapters.Add((i + 1, chTitle, english));
    }

    return chapters;
}

// Build the output .epub file path (defaults to Downloads folder)
static string BuildOutputPath(BookInfo book, string? outFile)
{
    if (outFile != null) return outFile;
    string safeName = Regex.Replace(book.TitleEn ?? book.Title, @"[\\/:*?""<>|]", "_").Trim('_');
    if (string.IsNullOrWhiteSpace(safeName))
        safeName = Regex.Match(book.IndexUrl, @"/([^/]+?)/?$").Groups[1].Value;
    string fileName = safeName[..Math.Min(safeName.Length, 60)] +
                      (book.ChapterLimit > 0 ? $"_ch1-{book.Total}" : "") + ".epub";
    string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    return Path.Combine(downloads, fileName);
}

// Translate metadata, download cover, download chapters, build EPUB
async Task ProcessBook(BookInfo book, string? outFile = null)
{
    Console.WriteLine($"\n--- {book.Title} ({book.Total} chapters) [{book.Adapter.SiteName}] ---");

    Console.Write("  Translating title/author...");
    book.TitleEn  = await Translate(book.Title);
    book.AuthorEn = await Translate(book.Author);
    Console.WriteLine(" done");
    Console.WriteLine($"  Title (EN):  {book.TitleEn}");
    Console.WriteLine($"  Author (EN): {book.AuthorEn}");

    var (coverBytes, coverMime) = await DownloadCover(book.CoverUrl);
    var chapters = await DownloadChapters(book);

    Console.WriteLine("\n  Building EPUB...");
    string path = BuildOutputPath(book, outFile);
    if (File.Exists(path)) File.Delete(path);
    BuildEpub(path, book.Title, book.TitleEn, book.Author, book.AuthorEn, chapters, coverBytes, coverMime);
    Console.WriteLine($"  Saved: {Path.GetFullPath(path)}");
}

// Detect which adapter handles this URL
static ISiteAdapter DetectAdapter(string url)
{
    ISiteAdapter[] adapters = [new ShukuAdapter(), new CzBooksAdapter(), new DmxsAdapter(), new ShubaAdapter()];
    return adapters.FirstOrDefault(a => a.Matches(url))
        ?? throw new Exception($"No supported adapter for URL: {url}\nSupported sites: 52shuku.net, czbooks.net, dmxs.org, 69shuba.com");
}

// Gather book info using the appropriate site adapter
async Task<BookInfo> GatherBookInfo(string indexUrl, int chapterLimit = 0, string? forceCoverUrl = null)
{
    var adapter = DetectAdapter(indexUrl);
    indexUrl = adapter.NormalizeUrl(indexUrl);
    Console.WriteLine($"  Gathering [{adapter.SiteName}]: {indexUrl}");
    string html = await Fetch(indexUrl);
    var info = adapter.ParseIndex(html, indexUrl);
    int total = chapterLimit > 0 ? Math.Min(chapterLimit, info.ChapterUrls.Count) : info.ChapterUrls.Count;
    string? coverUrl = forceCoverUrl ?? info.CoverUrl ?? TryExtractCover(html, indexUrl);
    return new BookInfo(indexUrl, info.Title, info.Author, info.ChapterUrls, total, chapterLimit, coverUrl, adapter);
}

// Entry point
bool isBatch = args[0].Equals("--batch", StringComparison.OrdinalIgnoreCase);

if (isBatch)
{
    if (args.Length < 2) { Console.WriteLine("Error: --batch requires a file path."); return; }
    string batchFile = args[1];
    if (!File.Exists(batchFile)) { Console.WriteLine($"Error: file not found: {batchFile}"); return; }

    var urls = File.ReadAllLines(batchFile)
        .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

    if (urls.Count == 0) { Console.WriteLine("No URLs found in batch file."); return; }
    Console.WriteLine($"Batch mode: {urls.Count} book(s) found.\n");

    Console.WriteLine("=== Phase 1: Gathering book info ===");
    var books = new List<BookInfo>();
    foreach (var url in urls)
    {
        try { books.Add(await GatherBookInfo(url)); }
        catch (Exception ex) { Console.WriteLine($"  [skip] {url} — {ex.Message}"); }
    }

    Console.WriteLine("\n=== Books to download ===");
    for (int i = 0; i < books.Count; i++)
    {
        var b = books[i];
        Console.WriteLine($"  [{i+1}] {b.Title} by {b.Author} — {b.Total} chapters — cover: {(b.CoverUrl != null ? "found" : "none")}");
    }
    Console.WriteLine();

    Console.WriteLine("=== Phase 2: Downloading & building EPUBs ===");
    for (int i = 0; i < books.Count; i++)
    {
        Console.WriteLine($"\n[{i+1}/{books.Count}]");
        try { await ProcessBook(books[i]); }
        catch (Exception ex) { Console.WriteLine($"  [error] {books[i].Title}: {ex.Message}"); }
    }
    Console.WriteLine("\nBatch complete.");
}
else
{
    string indexUrl    = args[0];
    int    chapterLimit = args.Length > 1 && int.TryParse(args[1], out int pl) ? pl : 0;
    string? outFile    = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : null;
    string? coverUrl   = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;

    Console.WriteLine("=== Phase 1: Gathering book info ===");
    var book = await GatherBookInfo(indexUrl, chapterLimit, coverUrl);

    Console.WriteLine($"  Title:    {book.Title}");
    Console.WriteLine($"  Author:   {book.Author}");
    Console.WriteLine($"  Chapters: {book.Total} (of {book.ChapterUrls.Count} found)");
    Console.WriteLine($"  Cover:    {(book.CoverUrl != null ? book.CoverUrl : "none (will generate)")}");
    Console.WriteLine();

    if (book.Total == 0) { Console.WriteLine("No chapters found."); return; }

    Console.WriteLine("=== Phase 2: Downloading & building EPUB ===");
    await ProcessBook(book, outFile);
}

// Cleanup Playwright browser if it was used
if (_context is not null) await _context.CloseAsync();
_playwright?.Dispose();


// Build EPUB zip from translated chapters
static void BuildEpub(string path, string titleZh, string titleEn, string authorZh, string authorEn,
    List<(int Idx, string ChTitle, string Text)> chapters, byte[]? coverBytes, string coverMime)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

    var mte = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
    using (var w = new StreamWriter(mte.Open())) w.Write("application/epub+zip");

    W(zip, "META-INF/container.xml",
        "<?xml version=\"1.0\"?>" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
        "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>" +
        "</rootfiles></container>");

    string uid = $"shuka-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    var items = new List<(string Id, string Fname, string Label)>();

    string coverItemId   = "cover-image";
    string coverExt      = coverMime == "image/png" ? "png" : coverMime == "image/gif" ? "gif" : "jpg";
    string coverImgFname = $"cover.{coverExt}";
    string coverMimeAttr = coverMime;

    if (coverBytes != null)
    {
        var ce = zip.CreateEntry($"OEBPS/{coverImgFname}", CompressionLevel.NoCompression);
        using var cs = ce.Open();
        cs.Write(coverBytes, 0, coverBytes.Length);
    }
    else
    {
        coverImgFname = "cover.svg";
        coverMimeAttr = "image/svg+xml";
        W(zip, "OEBPS/cover.svg", GenerateCoverSvg(titleEn, titleZh, authorEn, authorZh));
    }

    W(zip, "OEBPS/cover.xhtml",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Cover</title>" +
        "<style>body{margin:0;padding:0;text-align:center;background:#000;}" +
        "img{max-width:100%;max-height:100vh;}</style>" +
        "</head><body>" +
        $"<img src=\"{coverImgFname}\" alt=\"Cover\"/>" +
        "</body></html>");
    items.Add(("cover", "cover.xhtml", "Cover"));

    W(zip, "OEBPS/titlepage.xhtml",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Title Page</title>" +
        "<style>" +
        "body{font-family:Georgia,serif;text-align:center;margin:3em 2em;}" +
        ".title-en{font-size:1.8em;font-weight:bold;margin-bottom:0.3em;}" +
        ".title-zh{font-size:1.3em;color:#555;margin-bottom:1.5em;}" +
        ".author-label{font-size:0.9em;color:#888;text-transform:uppercase;letter-spacing:0.1em;}" +
        ".author-en{font-size:1.2em;font-weight:bold;margin-top:0.2em;}" +
        ".author-zh{font-size:1em;color:#555;}" +
        ".divider{margin:2em auto;width:60px;border-top:2px solid #ccc;}" +
        ".source{font-size:0.8em;color:#aaa;margin-top:3em;}" +
        "</style></head><body>" +
        $"<div class=\"title-en\">{X(titleEn)}</div>" +
        $"<div class=\"title-zh\">{X(titleZh)}</div>" +
        "<div class=\"divider\"></div>" +
        "<div class=\"author-label\">Author</div>" +
        $"<div class=\"author-en\">{X(authorEn)}</div>" +
        $"<div class=\"author-zh\">{X(authorZh)}</div>" +
        "<div class=\"source\">Translated to English by Shuka</div>" +
        "</body></html>");
    items.Add(("titlepage", "titlepage.xhtml", "Title Page"));

    foreach (var (idx, chTitle, text) in chapters)
    {
        string id = $"ch{idx}", fname = $"ch{idx}.xhtml";
        items.Add((id, fname, chTitle));
        var paras = text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => $"<p>{X(l.Trim())}</p>");
        W(zip, $"OEBPS/{fname}",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>" +
            $"<title>{X(chTitle)}</title>" +
            "<style>body{font-family:Georgia,serif;line-height:1.8;margin:2em}p{margin:0.5em 0}</style>" +
            $"</head><body><h2>{X(chTitle)}</h2>" +
            string.Join("", paras) + "</body></html>");
    }

    string coverItem = $"<item id=\"{coverItemId}\" href=\"{coverImgFname}\" media-type=\"{coverMimeAttr}\" properties=\"cover-image\"/>";
    string mf = coverItem + string.Join("", items.Select(c => $"<item id=\"{c.Id}\" href=\"{c.Fname}\" media-type=\"application/xhtml+xml\"/>"));
    string sp = string.Join("", items.Select(c => $"<itemref idref=\"{c.Id}\"/>"));
    string np = string.Join("", items.Select((c, i) =>
        $"<navPoint id=\"n{i+1}\" playOrder=\"{i+1}\"><navLabel><text>{X(c.Label)}</text></navLabel><content src=\"{c.Fname}\"/></navPoint>"));

    W(zip, "OEBPS/content.opf",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<package xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"uid\" version=\"2.0\">" +
        "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
        $"<dc:title>{X(titleEn)}</dc:title><dc:creator>{X(authorEn)}</dc:creator>" +
        $"<dc:language>en</dc:language><dc:identifier id=\"uid\">{uid}</dc:identifier>" +
        "<meta name=\"cover\" content=\"cover-image\"/>" +
        $"</metadata><manifest><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>{mf}</manifest>" +
        $"<spine toc=\"ncx\">{sp}</spine></package>");

    W(zip, "OEBPS/toc.ncx",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">" +
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">" +
        $"<head><meta name=\"dtb:uid\" content=\"{uid}\"/></head>" +
        $"<docTitle><text>{X(titleEn)}</text></docTitle><navMap>{np}</navMap></ncx>");
}

// Generate a styled SVG cover when no image URL is provided
static string GenerateCoverSvg(string titleEn, string titleZh, string authorEn, string authorZh)
{
    var palettes = new[]
    {
        ("#1a1a2e", "#e94560", "#ffffff"),
        ("#0f3460", "#e94560", "#ffffff"),
        ("#16213e", "#0f3460", "#e2b96f"),
        ("#2d1b69", "#11998e", "#ffffff"),
        ("#1a1a1a", "#c0392b", "#f5f5f5"),
        ("#0d2137", "#f7971e", "#ffffff"),
    };
    int pick = Math.Abs(titleEn.GetHashCode()) % palettes.Length;
    var (bg, accent, fg) = palettes[pick];

    var titleLines = WrapText(titleEn, 22);
    var titleSvg = string.Join("", titleLines.Select((l, i) =>
        $"<tspan x=\"300\" dy=\"{(i == 0 ? 0 : 52)}\">{X(l)}</tspan>"));

    double titleBlockH = titleLines.Count * 52;
    double titleY = 280 - titleBlockH / 2;
    string zhShort = titleZh.Length > 20 ? titleZh[..20] + "…" : titleZh;

    return
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"600\" height=\"900\" viewBox=\"0 0 600 900\">" +
        $"<rect width=\"600\" height=\"900\" fill=\"{bg}\"/>" +
        $"<rect x=\"0\" y=\"0\" width=\"600\" height=\"8\" fill=\"{accent}\"/>" +
        $"<rect x=\"60\" y=\"60\" width=\"480\" height=\"2\" fill=\"{accent}\" opacity=\"0.4\"/>" +
        $"<rect x=\"60\" y=\"840\" width=\"480\" height=\"2\" fill=\"{accent}\" opacity=\"0.4\"/>" +
        $"<circle cx=\"300\" cy=\"450\" r=\"260\" fill=\"none\" stroke=\"{accent}\" stroke-width=\"1\" opacity=\"0.15\"/>" +
        $"<circle cx=\"300\" cy=\"450\" r=\"200\" fill=\"none\" stroke=\"{accent}\" stroke-width=\"1\" opacity=\"0.1\"/>" +
        $"<rect x=\"60\" y=\"{titleY - 30}\" width=\"480\" height=\"{titleBlockH + 60}\" fill=\"{accent}\" opacity=\"0.08\" rx=\"4\"/>" +
        $"<rect x=\"60\" y=\"{titleY - 30}\" width=\"4\" height=\"{titleBlockH + 60}\" fill=\"{accent}\" rx=\"2\"/>" +
        $"<text x=\"300\" y=\"{titleY}\" font-family=\"Georgia, serif\" font-size=\"44\" font-weight=\"bold\" " +
        $"fill=\"{fg}\" text-anchor=\"middle\" dominant-baseline=\"hanging\">{titleSvg}</text>" +
        $"<text x=\"300\" y=\"{titleY + titleBlockH + 24}\" font-family=\"serif\" font-size=\"22\" " +
        $"fill=\"{fg}\" text-anchor=\"middle\" opacity=\"0.6\">{X(zhShort)}</text>" +
        $"<line x1=\"220\" y1=\"580\" x2=\"380\" y2=\"580\" stroke=\"{accent}\" stroke-width=\"1.5\"/>" +
        $"<text x=\"300\" y=\"610\" font-family=\"Georgia, serif\" font-size=\"24\" font-weight=\"bold\" " +
        $"fill=\"{fg}\" text-anchor=\"middle\">{X(authorEn)}</text>" +
        $"<text x=\"300\" y=\"645\" font-family=\"serif\" font-size=\"18\" " +
        $"fill=\"{fg}\" text-anchor=\"middle\" opacity=\"0.6\">{X(authorZh)}</text>" +
        $"<text x=\"300\" y=\"870\" font-family=\"Georgia, serif\" font-size=\"13\" " +
        $"fill=\"{fg}\" text-anchor=\"middle\" opacity=\"0.35\">Translated to English by Shuka</text>" +
        "</svg>";
}

static List<string> WrapText(string text, int maxChars)
{
    var words = text.Split(' ');
    var lines = new List<string>();
    var cur   = new StringBuilder();
    foreach (var word in words)
    {
        if (cur.Length + word.Length + 1 > maxChars && cur.Length > 0)
        { lines.Add(cur.ToString()); cur.Clear(); }
        if (cur.Length > 0) cur.Append(' ');
        cur.Append(word);
    }
    if (cur.Length > 0) lines.Add(cur.ToString());
    return lines.Count > 0 ? lines : new List<string> { text };
}

static void W(ZipArchive zip, string name, string content)
{
    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
    using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
    w.Write(content);
}

static string X(string s) => s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");

// Site adapter interface — implement this to add a new site
interface ISiteAdapter
{
    string SiteName { get; }
    bool Matches(string url);
    string NormalizeUrl(string url);
    IndexInfo ParseIndex(string html, string indexUrl);
    List<string> ExtractChapterText(string html);
}

// Parsed index result from an adapter
record IndexInfo(string Title, string Author, List<ChapterRef> ChapterUrls, string? CoverUrl);

// A chapter reference (URL + optional display title)
record ChapterRef(string Url, string Title);

// BookInfo — holds all metadata and chapter list for one novel
record BookInfo(string IndexUrl, string Title, string Author,
    List<ChapterRef> ChapterUrls, int Total, int ChapterLimit,
    string? CoverUrl, ISiteAdapter Adapter)
{
    public string? TitleEn  { get; set; }
    public string? AuthorEn { get; set; }
}

// 52shuku.net adapter
class ShukuAdapter : ISiteAdapter
{
    public string SiteName => "52shuku.net";

    public bool Matches(string url) =>
        url.Contains("52shuku.net", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        // Strip chapter suffix: bkd7d_2.html -> bkd7d.html
        url = Regex.Replace(url, @"_\d+\.html$", ".html");
        // Ensure https
        if (url.StartsWith("http://")) url = "https://" + url[7..];
        if (!url.StartsWith("http")) url = "https://" + url;
        return url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        string title = Regex.Match(html, @"<h1[^>]*>([\s\S]*?)</h1>", RegexOptions.IgnoreCase).Groups[1].Value;
        title = Regex.Replace(title, @"<[^>]+>", "").Trim();
        title = Regex.Replace(title, @"\s*\(\d+\)\s*$", "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Regex.Match(indexUrl, @"/([^/]+)\.html$").Groups[1].Value;

        string author = "Unknown";
        var am = Regex.Match(html, @"作者[：:]\s*([^\s【\n】<&]+)");
        if (am.Success) author = am.Groups[1].Value.Trim();

        string baseUrl = Regex.Replace(indexUrl, @"\.html$", "");
        var chapterUrls = Regex.Matches(html, @"href=[""']?([^""'\s>]+)[""']?", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Select(h => h.StartsWith("http") ? h : new Uri(new Uri(indexUrl), h).ToString())
            .Where(u => u.StartsWith(baseUrl + "_") && u.EndsWith(".html"))
            .Distinct()
            .OrderBy(u => { var m = Regex.Match(u, @"_(\d+)\.html$"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; })
            .Select((u, i) => new ChapterRef(u, $"Page {i + 1}"))
            .ToList();

        string? cover = null;
        var og = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!og.Success) og = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
        if (og.Success) cover = og.Groups[1].Value.Trim();

        return new IndexInfo(title, author, chapterUrls, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);
        var result = new List<string>();
        foreach (Match m in Regex.Matches(html, @"<p(?:\s[^>]*)?>([^<]*(?:<(?!/p>)[^<]*)*)</p>", RegexOptions.IgnoreCase))
        {
            string inner = m.Groups[1].Value;
            inner = Regex.Replace(inner, @"<[^>]+>", "");
            inner = inner.Replace("&nbsp;", " ").Replace("&amp;", "&")
                         .Replace("&lt;", "<").Replace("&gt;", ">")
                         .Replace("&quot;", "\"").Replace("&#39;", "'")
                         .Replace("\u3000", " ").Trim();
            if (inner.Length > 0 && Regex.IsMatch(inner, @"[\u4e00-\u9fff\u3400-\u4dbf]"))
                result.Add(inner);
        }
        return result;
    }
}

// czbooks.net adapter
class CzBooksAdapter : ISiteAdapter
{
    public string SiteName => "czbooks.net";

    public bool Matches(string url) =>
        url.Contains("czbooks.net", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        // Normalize to index: czbooks.net/n/{id} — strip any chapter code
        if (!url.StartsWith("http")) url = "https://" + url;
        // If URL is a chapter page like /n/clgajm/cdg03m2, strip the chapter part
        var m = Regex.Match(url, @"(https?://czbooks\.net/n/[^/]+)(?:/.*)?$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        // Title: from <title> tag or 《》 brackets
        string title = Regex.Match(html, @"《([^》]+)》").Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Regex.Match(html, @"<title[^>]*>([^<|]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Regex.Match(indexUrl, @"/n/([^/]+)").Groups[1].Value;

        // Author: look for 作者 followed by a link or plain text
        string author = "Unknown";
        var am = Regex.Match(html, @"作者[：:\s]*<[^>]+>([^<]+)<", RegexOptions.IgnoreCase);
        if (!am.Success) am = Regex.Match(html, @"作者[：:\s]+([^\s<\n,，]+)");
        if (am.Success) author = am.Groups[1].Value.Trim();

        // Chapter links: href="/n/{id}/{code}" — rendered HTML has these as anchor tags
        string bookId = Regex.Match(indexUrl, @"/n/([^/]+)").Groups[1].Value;
        var chapterUrls = Regex.Matches(html,
                @"href=[""'](https?://czbooks\.net/n/" + Regex.Escape(bookId) + @"/([^""'?]+))[""']",
                RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => (url: m.Groups[1].Value, code: m.Groups[2].Value))
            .DistinctBy(x => x.code)
            .ToList();

        // Also try relative hrefs
        if (chapterUrls.Count == 0)
        {
            chapterUrls = Regex.Matches(html,
                    @"href=[""'](/n/" + Regex.Escape(bookId) + @"/([^""'?]+))[""']",
                    RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => (url: "https://czbooks.net" + m.Groups[1].Value, code: m.Groups[2].Value))
                .DistinctBy(x => x.code)
                .ToList();
        }

        // Extract chapter numbers from chapterNumber= query param for ordering
        var chapters = Regex.Matches(html,
                @"href=[""'][^""']*(/n/" + Regex.Escape(bookId) + @"/([^""'?]+))\?chapterNumber=(\d+)[""']",
                RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => new {
                Url   = "https://czbooks.net" + m.Groups[1].Value,
                Code  = m.Groups[2].Value,
                Num   = int.Parse(m.Groups[3].Value)
            })
            .DistinctBy(x => x.Code)
            .OrderBy(x => x.Num)
            .Select(x => new ChapterRef(x.Url, $"Chapter {x.Num}"))
            .ToList();

        // Cover from og:image
        string? cover = null;
        var og = Regex.Match(html, @"<img[^>]+src=[""'](https?://img\.czbooks\.net/[^""']+)[""']", RegexOptions.IgnoreCase);
        if (og.Success) cover = og.Groups[1].Value;
        if (cover == null)
        {
            var ogm = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!ogm.Success) ogm = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
            if (ogm.Success) cover = ogm.Groups[1].Value.Trim();
        }

        return new IndexInfo(title, author, chapters, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        // czbooks renders content as plain text with 　 (fullwidth space) paragraph indentation
        // Strip scripts/styles/nav first
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<nav[\s\S]*?</nav>",       "", RegexOptions.IgnoreCase);

        // Find the main content block — czbooks wraps chapter text in a div with class containing "content" or "chapter"
        var contentMatch = Regex.Match(html,
            @"<div[^>]+class=[""'][^""']*(?:content|chapter-content|article)[^""']*[""'][^>]*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);

        string content = contentMatch.Success ? contentMatch.Groups[1].Value : html;

        // Strip remaining tags
        content = Regex.Replace(content, @"<[^>]+>", "\n");
        // Decode entities
        content = content.Replace("&nbsp;", " ").Replace("&amp;", "&")
                         .Replace("&lt;", "<").Replace("&gt;", ">")
                         .Replace("&quot;", "\"").Replace("&#39;", "'");

        // Split on newlines or fullwidth-space-prefixed lines
        var result = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            string trimmed = line.Trim().TrimStart('\u3000').Trim();
            if (trimmed.Length > 0 && Regex.IsMatch(trimmed, @"[\u4e00-\u9fff\u3400-\u4dbf]"))
                result.Add(trimmed);
        }
        return result;
    }
}

// dmxs.org adapter (Chinese novel site)
// Index URL: https://www.dmxs.org/{category}/{bookId}.html
// Chapter URL: https://www.dmxs.org/view/{classId}-{bookId}-{chapterNum}.html
class DmxsAdapter : ISiteAdapter{
    public string SiteName => "dmxs.org";

    public bool Matches(string url) =>
        url.Contains("dmxs.org", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        if (!url.StartsWith("http")) url = "https://" + url;
        // Strip query/fragment; keep only the index page path
        var m = Regex.Match(url,
            @"(https?://(?:www\.)?dmxs\.org/[^/?#]+/\d+\.html)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        // Title from <h1>
        string title = Regex.Match(html, @"<h1[^>]*>\s*([^<]+?)\s*</h1>",
            RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Regex.Match(html, @"<title[^>]*>([^<|_–-]+)",
                RegexOptions.IgnoreCase).Groups[1].Value.Trim();

        // Author: 作者：<a>name</a> or 作者：name
        string author = "Unknown";
        var am = Regex.Match(html, @"作者[：:]\s*<a[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase);
        if (!am.Success) am = Regex.Match(html, @"作者[：:]\s*([^\s<\n,，]+)");
        if (am.Success) author = am.Groups[1].Value.Trim();

        // Chapter links: /view/{classId}-{bookId}-{num}.html
        var chapters = Regex.Matches(html,
                @"href=[""'](?:https?://(?:www\.)?dmxs\.org)?/view/(\d+)-(\d+)-(\d+)\.html[""'][^>]*>([^<]*)</a>",
                RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => new {
                ClassId = m.Groups[1].Value,
                BookId  = m.Groups[2].Value,
                Num     = int.Parse(m.Groups[3].Value),
                Title   = System.Net.WebUtility.HtmlDecode(m.Groups[4].Value.Trim()),
                Url     = $"https://www.dmxs.org/view/{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}.html"
            })
            .DistinctBy(x => x.Num)
            .OrderBy(x => x.Num)
            .Select(x => new ChapterRef(x.Url,
                string.IsNullOrWhiteSpace(x.Title) ? $"Chapter {x.Num}" : x.Title))
            .ToList();

        // Cover from og:image (dmxs rarely has one, but check anyway)
        string? cover = null;
        var ogM = Regex.Match(html,
            @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!ogM.Success)
            ogM = Regex.Match(html,
                @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']",
                RegexOptions.IgnoreCase);
        if (ogM.Success) cover = ogM.Groups[1].Value.Trim();

        return new IndexInfo(title, author, chapters, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<nav[\s\S]*?</nav>",       "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<header[\s\S]*?</header>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<footer[\s\S]*?</footer>", "", RegexOptions.IgnoreCase);

        // dmxs wraps chapter text in <div id="content">
        string? content = null;
        foreach (var pattern in new[]
        {
            @"<div[^>]+id=[""']content[""'][^>]*>([\s\S]*?)</div>",
            @"<div[^>]+class=[""'][^""']*\bcontent\b[^""']*[""'][^>]*>([\s\S]*?)</div>",
            @"<article[^>]*>([\s\S]*?)</article>",
        })
        {
            var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.Length > 100)
            { content = m.Groups[1].Value; break; }
        }
        content ??= html;

        content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<p[^>]*>",  "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<[^>]+>",   "");
        content = System.Net.WebUtility.HtmlDecode(content);

        var result = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            string trimmed = line.Trim().TrimStart('\u3000').Trim();
            if (trimmed.Length > 0 &&
                Regex.IsMatch(trimmed, @"[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff]"))
                result.Add(trimmed);
        }
        return result;
    }
}

// 69shuba.com adapter (Simplified Chinese novel site, GBK encoded)
// Index URL: https://www.69shuba.com/book/{bookId}.htm  or  /book/{bookId}/
// Chapter URL: https://www.69shuba.com/txt/{bookId}/{chapterId}
class ShubaAdapter : ISiteAdapter
{
    public string SiteName => "69shuba.com";

    public bool Matches(string url) =>
        url.Contains("69shuba.com", StringComparison.OrdinalIgnoreCase);

    public string NormalizeUrl(string url)
    {
        if (!url.StartsWith("http")) url = "https://" + url;
        // Chapter URL → index
        var cm = Regex.Match(url, @"https?://(?:www\.)?69shuba\.com/txt/(\d+)/\d+", RegexOptions.IgnoreCase);
        if (cm.Success) return $"https://www.69shuba.com/book/{cm.Groups[1].Value}/";
        // .htm info page → full chapter list page
        var im = Regex.Match(url, @"https?://(?:www\.)?69shuba\.com/book/(\d+)\.htm", RegexOptions.IgnoreCase);
        if (im.Success) return $"https://www.69shuba.com/book/{im.Groups[1].Value}/";
        // Already list page — strip query/fragment
        var lm = Regex.Match(url, @"(https?://(?:www\.)?69shuba\.com/book/\d+/)", RegexOptions.IgnoreCase);
        return lm.Success ? lm.Groups[1].Value : url;
    }

    public IndexInfo ParseIndex(string html, string indexUrl)
    {
        string bookId = Regex.Match(indexUrl, @"/book/(\d+)/").Groups[1].Value;

        string cleanHtml = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        cleanHtml = Regex.Replace(cleanHtml, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        string title = "";
        var h1m = Regex.Match(cleanHtml, @"<h1[^>]*class=""[^""]*bookname[^""]*""[^>]*>\s*([^<]+)", RegexOptions.IgnoreCase);
        if (h1m.Success) title = h1m.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            h1m = Regex.Match(cleanHtml, @"<h1[^>]*>\s*([^<]+)", RegexOptions.IgnoreCase);
            if (h1m.Success) title = h1m.Groups[1].Value.Trim();
        }
        if (string.IsNullOrWhiteSpace(title))
            title = Regex.Match(cleanHtml, @"<title[^>]*>([^<|_–\-]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();

        string author = "Unknown";
        var am = Regex.Match(cleanHtml, @"作者[：:]\s*<a[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase);
        if (!am.Success) am = Regex.Match(cleanHtml, @"作者[：:]\s*([^\s<\n,，]+)");
        if (am.Success) author = am.Groups[1].Value.Trim();

        // Strip SEO junk appended to titles
        title = Regex.Replace(title, @"\s*[,，]\s*.*$", "").Trim();
        title = Regex.Replace(title, @"\s*[-_|–]\s*.*$", "").Trim();
        title = Regex.Replace(title, @"\s*(最新章节|无弹窗|全文阅读|免费阅读).*$", "").Trim();

        var chapters = Regex.Matches(html,
                @"href=[""'](?:https?://(?:www\.)?69shuba\.com)?/txt/" + Regex.Escape(bookId) + @"/(\d+)[""'][^>]*>([^<]*)</a>",
                RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => new { Id = m.Groups[1].Value, Title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim()) })
            .DistinctBy(x => x.Id)
            .Select((x, i) => new ChapterRef(
                $"https://www.69shuba.com/txt/{bookId}/{x.Id}",
                string.IsNullOrWhiteSpace(x.Title) ? $"Chapter {i + 1}" : x.Title))
            .ToList();

        string? cover = null;
        var imgM = Regex.Match(html,
            @"<img[^>]+src=[""'](https?://[^""']*/" + Regex.Escape(bookId) + @"[^""']*\.(jpg|png|webp))[""']",
            RegexOptions.IgnoreCase);
        if (imgM.Success) cover = imgM.Groups[1].Value.Trim();
        if (cover == null)
        {
            var ogM = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!ogM.Success) ogM = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
            if (ogM.Success) cover = ogM.Groups[1].Value.Trim();
        }
        // Fallback: construct CDN cover URL directly — prefix = floor(bookId / 1000)
        if (cover == null && int.TryParse(bookId, out int bid))
            cover = $"https://cdn.cdnshu.com/files/article/image/{bid / 1000}/{bookId}/{bookId}s.jpg";

        return new IndexInfo(title, author, chapters, cover);
    }

    public List<string> ExtractChapterText(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<nav[\s\S]*?</nav>",       "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<header[\s\S]*?</header>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<footer[\s\S]*?</footer>", "", RegexOptions.IgnoreCase);

        string? content = null;
        foreach (var pattern in new[]
        {
            @"<div[^>]+class=""[^""]*txtnav[^""]*""[^>]*>([\s\S]+?)<div[^>]+class=""[^""]*txtright",
            @"<div[^>]+class=""[^""]*txtnav[^""]*""[^>]*>([\s\S]+)",
            @"<div[^>]+id=""content""[^>]*>([\s\S]+?)</div>\s*</div>",
            @"<div[^>]+id=""content""[^>]*>([\s\S]+)",
            @"<div[^>]+class=""[^""]*\bcontent\b[^""]*""[^>]*>([\s\S]+)",
            @"<article[^>]*>([\s\S]+?)</article>",
        })
        {
            var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.Length > 200) { content = m.Groups[1].Value; break; }
        }
        content ??= html;

        content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<p[^>]*>",  "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<[^>]+>",   "");
        content = System.Net.WebUtility.HtmlDecode(content);

        var result = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            string trimmed = line.Trim().TrimStart('\u3000').Trim();
            if (trimmed.Length > 0 &&
                Regex.IsMatch(trimmed, @"[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff]") &&
                !trimmed.Contains("69shuba") && !trimmed.Contains("www.") &&
                !Regex.IsMatch(trimmed, @"https?://"))
                result.Add(trimmed);
        }
        return result;
    }
}

