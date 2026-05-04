using System.Text;
using Shuka;
using Shuka.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// ── Usage ─────────────────────────────────────────────────────────────────────
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

// ── Playwright browser install passthrough (used by installer) ────────────────
if (args.Length >= 2 && args[0] == "playwright" && args[1] == "install")
{
    Environment.Exit(Microsoft.Playwright.Program.Main(args.Skip(1).ToArray()));
    return;
}

// ── --solve-cf: manual CF challenge solver ────────────────────────────────────
if (args.Length >= 2 && args[0] == "--solve-cf")
{
    await PlaywrightFetcher.SolveCfInteractiveAsync(args[1]);
    return;
}

// ── HTTP clients ──────────────────────────────────────────────────────────────
var siteHandler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
using var siteClient = new HttpClient(siteHandler) { Timeout = TimeSpan.FromSeconds(30) };
siteClient.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
siteClient.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,zh-CN;q=0.8");
siteClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

var httpHandler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
using var httpClient = new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(45) };
httpClient.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

await using var fetcher    = new PlaywrightFetcher(siteClient);
var             translator = new Translator(httpClient);
var             downloader = new Downloader(fetcher, translator, httpClient);

// ── --batch mode ──────────────────────────────────────────────────────────────
if (args[0].Equals("--batch", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2) { Console.WriteLine("Error: --batch requires a file path."); return; }
    if (!File.Exists(args[1])) { Console.WriteLine($"Error: file not found: {args[1]}"); return; }

    var urls = File.ReadAllLines(args[1])
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .ToList();

    if (urls.Count == 0) { Console.WriteLine("No URLs found in batch file."); return; }
    Console.WriteLine($"Batch mode: {urls.Count} book(s) found.\n");

    Console.WriteLine("=== Phase 1: Gathering book info ===");
    var books = new List<BookInfo>();
    foreach (var url in urls)
    {
        try   { books.Add(await downloader.GatherBookInfoAsync(url)); }
        catch (Exception ex) { Console.WriteLine($"  [skip] {url} — {ex.Message}"); }
    }

    Console.WriteLine("\n=== Books to download ===");
    for (int i = 0; i < books.Count; i++)
    {
        var b = books[i];
        Console.WriteLine($"  [{i + 1}] {b.Title} by {b.Author} — {b.Total} chapters" +
                          $" — cover: {(b.CoverUrl != null ? "found" : "none")}");
    }
    Console.WriteLine();

    Console.WriteLine("=== Phase 2: Downloading & building EPUBs ===");
    for (int i = 0; i < books.Count; i++)
    {
        Console.WriteLine($"\n[{i + 1}/{books.Count}]");
        try   { await downloader.ProcessBookAsync(books[i]); }
        catch (Exception ex) { Console.WriteLine($"  [error] {books[i].Title}: {ex.Message}"); }
    }
    Console.WriteLine("\nBatch complete.");
    return;
}

// ── Single book mode ──────────────────────────────────────────────────────────
{
    string  indexUrl     = args[0];
    int     chapterLimit = args.Length > 1 && int.TryParse(args[1], out int pl) ? pl : 0;
    string? outFile      = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : null;
    string? coverUrl     = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;

    Console.WriteLine("=== Phase 1: Gathering book info ===");
    var book = await downloader.GatherBookInfoAsync(indexUrl, chapterLimit, coverUrl);

    Console.WriteLine($"  Title:    {book.Title}");
    Console.WriteLine($"  Author:   {book.Author}");
    Console.WriteLine($"  Chapters: {book.Total} (of {book.ChapterUrls.Count} found)");
    Console.WriteLine($"  Cover:    {book.CoverUrl ?? "none (will generate)"}");
    Console.WriteLine();

    if (book.Total == 0) { Console.WriteLine("No chapters found."); return; }

    Console.WriteLine("=== Phase 2: Downloading & building EPUB ===");
    await downloader.ProcessBookAsync(book, outFile);
}
