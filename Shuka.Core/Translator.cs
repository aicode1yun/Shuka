using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace Shuka.Core;

public class Translator
{
    private readonly HttpClient _http;

    // 8 concurrent translate calls — balanced for mobile networks
    private static readonly SemaphoreSlim _globalSem = new(8);

    // 800 chars — fits comfortably in the mobile web page URL
    private const int ChunkSize = 800;

    public Translator(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Split text into chunks and translate them in parallel.</summary>
    public async Task<string> Translate(string text, Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // ── Build chunks ──────────────────────────────────────────────────────
        var chunks = new List<string>();
        var cur    = new StringBuilder(ChunkSize + 200);

        foreach (var line in text.Split('\n'))
        {
            if (cur.Length + line.Length + 1 > ChunkSize && cur.Length > 0)
            {
                chunks.Add(cur.ToString());
                cur.Clear();
            }
            if (cur.Length > 0) cur.Append('\n');
            cur.Append(line);
        }
        if (cur.Length > 0) chunks.Add(cur.ToString());

        // ── Translate all chunks in parallel ──────────────────────────────────
        var tasks = chunks.Select(async (chunk, i) =>
        {
            await _globalSem.WaitAsync(ct);
            try   { return (i, text: await TranslateChunk(chunk, log, ct)); }
            finally { _globalSem.Release(); }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return string.Join("\n", results.OrderBy(r => r.i).Select(r => r.text));
    }

    /// <summary>
    /// Translate one chunk. Tries multiple Google endpoints, then MyMemory.
    /// Logs every failure so the user can see what's happening.
    /// </summary>
    private async Task<string> TranslateChunk(string chunk, Action<string>? log,
        CancellationToken ct)
    {
        // ── Endpoint 1: Google Translate mobile web page ──────────────────────
        // Scrapes the result-container div. Not rate-limited like the JSON API.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string truncated = chunk.Length > 900 ? chunk[..900] : chunk;
                string? result = await CallGoogleMobileWeb(truncated, ct);
                if (result != null) return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (attempt < 3) await Task.Delay(300 * attempt, ct);
                else log?.Invoke($"[Google web attempt {attempt}/3 failed: {ex.Message}]");
            }
        }

        // ── Endpoint 2: Google gtx GET (JSON API) ─────────────────────────────
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string? result = await CallGoogleGtx(chunk, ct);
                if (result != null) return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log?.Invoke($"[Google gtx attempt {attempt}/3 failed: {ex.Message}]");
                if (attempt < 3)
                    await Task.Delay(400 * attempt, ct);
            }
        }

        // ── Endpoint 3: Google translate POST ────────────────────────────────
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string? result = await CallGooglePost(chunk, ct);
                if (result != null) return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log?.Invoke($"[Google POST attempt {attempt}/3 failed: {ex.Message}]");
                if (attempt < 3)
                    await Task.Delay(400 * attempt, ct);
            }
        }

        // ── Endpoint 4: MyMemory (reliable free fallback, 500-char limit) ─────
        log?.Invoke("[Google failed, trying MyMemory...]");
        try
        {
            string? mm = await CallMyMemory(chunk, ct);
            if (mm != null)
            {
                log?.Invoke("[MyMemory ok]");
                return mm;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log?.Invoke($"[MyMemory failed: {ex.Message}]");
        }

        // ── Last resort: return original Chinese ──────────────────────────────
        log?.Invoke("[all translation endpoints failed — keeping original]");
        return chunk;
    }

    /// <summary>Google Translate mobile web page — scrapes result-container div.</summary>
    private async Task<string?> CallGoogleMobileWeb(string chunk, CancellationToken ct)
    {
        string url = $"https://translate.google.com/m?sl=zh-CN&tl=en&q={Uri.EscapeDataString(chunk)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 10; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");
        req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var resp = await _http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}");

        string html = await resp.Content.ReadAsStringAsync(cts.Token);
        var m = System.Text.RegularExpressions.Regex.Match(html,
            @"class=""result-container""[^>]*>([\s\S]*?)</div>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        string result = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>Google Translate unofficial GET endpoint (gtx client).</summary>
    private async Task<string?> CallGoogleGtx(string chunk, CancellationToken ct)
    {
        string url = "https://translate.googleapis.com/translate_a/single" +
            $"?client=gtx&sl=zh-CN&tl=en&dt=t&q={Uri.EscapeDataString(chunk)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 10; Mobile) AppleWebKit/537.36 Chrome/124.0 Mobile Safari/537.36");
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        req.Headers.Add("Referer", "https://translate.google.com/");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var resp = await _http.SendAsync(req, cts.Token);

        // If we get a non-success or HTML error page, treat as failure
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}");

        string json = await resp.Content.ReadAsStringAsync(cts.Token);

        // Detect HTML error page (Google sometimes returns HTML instead of JSON)
        if (json.TrimStart().StartsWith('<'))
            throw new Exception("Got HTML instead of JSON (rate limited or blocked)");

        return ParseGoogleJson(json, chunk);
    }

    /// <summary>
    /// Google Translate via POST to the web API — works better on mobile networks
    /// where the GET endpoint is sometimes blocked.
    /// </summary>
    private async Task<string?> CallGooglePost(string chunk, CancellationToken ct)
    {
        // Use the same endpoint but with POST and form-encoded body
        const string url = "https://translate.googleapis.com/translate_a/single" +
            "?client=gtx&sl=zh-CN&tl=en&dt=t";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 10; Mobile) AppleWebKit/537.36 Chrome/124.0 Mobile Safari/537.36");
        req.Headers.Add("Referer", "https://translate.google.com/");
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", chunk)
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var resp = await _http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}");

        string json = await resp.Content.ReadAsStringAsync(cts.Token);
        if (json.TrimStart().StartsWith('<'))
            throw new Exception("Got HTML instead of JSON");

        return ParseGoogleJson(json, chunk);
    }

    private static string? ParseGoogleJson(string json, string original)
    {
        using var jdoc = JsonDocument.Parse(json);
        var root = jdoc.RootElement;

        // Response format: [[["translated","original",null,null,10],...],...]
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var firstArray = root[0];
        if (firstArray.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder(original.Length * 2);
        foreach (var seg in firstArray.EnumerateArray())
        {
            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0
                && seg[0].ValueKind == JsonValueKind.String)
                sb.Append(seg[0].GetString());
        }

        string result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private async Task<string?> CallMyMemory(string chunk, CancellationToken ct)
    {
        // MyMemory caps at 500 chars — split and reassemble
        if (chunk.Length <= 500)
            return await CallMyMemoryChunk(chunk, ct);

        var parts   = new List<string>();
        var results = new List<string>();

        for (int i = 0; i < chunk.Length; i += 450)
            parts.Add(chunk.Substring(i, Math.Min(450, chunk.Length - i)));

        foreach (var part in parts)
        {
            string? r = await CallMyMemoryChunk(part, ct);
            if (r == null) return null;
            results.Add(r);
            // Small delay between MyMemory requests to avoid rate limiting
            if (parts.Count > 1)
                await Task.Delay(100, ct);
        }

        return string.Join("", results);
    }

    private async Task<string?> CallMyMemoryChunk(string chunk, CancellationToken ct)
    {
        string url = "https://api.mymemory.translated.net/get" +
            $"?q={Uri.EscapeDataString(chunk)}&langpair=zh-CN|en-US";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 10; Mobile) AppleWebKit/537.36 Chrome/124.0 Mobile Safari/537.36");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var resp = await _http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}");

        string json = await resp.Content.ReadAsStringAsync(cts.Token);
        using var jdoc = JsonDocument.Parse(json);

        // Check response status
        int responseStatus = jdoc.RootElement
            .GetProperty("responseStatus")
            .GetInt32();

        if (responseStatus != 200)
            throw new Exception($"MyMemory status {responseStatus}");

        string? result = jdoc.RootElement
            .GetProperty("responseData")
            .GetProperty("translatedText")
            .GetString();

        return !string.IsNullOrWhiteSpace(result) && result != chunk
            ? result.Trim()
            : null;
    }
}
