using System.Collections.ObjectModel;
using System.Text.Json;

namespace Shuka.Android.Services;

/// <summary>
/// Persists completed downloads as history entries.
/// Covers are downloaded and cached locally so they display offline.
/// </summary>
public class HistoryService
{
    public static readonly HistoryService Instance = new();

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    private static string HistoryFile =>
        Path.Combine(FileSystem.AppDataDirectory, "history.json");

    private static string CoversDir =>
        Path.Combine(FileSystem.AppDataDirectory, "covers");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private HistoryService()
    {
        _ = LoadAsync();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a download completes. Saves the entry and caches the cover.
    /// </summary>
    public async Task AddAsync(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Done) return;

        // Don't add duplicates
        if (Entries.Any(e => e.Url == item.Url && e.EpubPath == item.EpubPath))
            return;

        var entry = new HistoryEntry
        {
            Id           = item.Id,
            Title        = item.Title,
            Author       = item.Author,
            Url          = item.Url,
            EpubPath     = item.EpubPath,
            CoverUrl     = string.IsNullOrWhiteSpace(item.CoverUrl) ? null : item.CoverUrl,
            ChapterCount = item.Chapters,
            CompletedAt  = DateTime.Now,
        };

        // Cache cover image locally
        if (!string.IsNullOrWhiteSpace(entry.CoverUrl))
            entry.CoverLocalPath = await CacheCoverAsync(entry.Id, entry.CoverUrl);

        MainThread.BeginInvokeOnMainThread(() => Entries.Insert(0, entry));
        await SaveAsync();
    }

    /// <summary>Remove a single entry and delete its cached cover.</summary>
    public async Task RemoveAsync(HistoryEntry entry)
    {
        MainThread.BeginInvokeOnMainThread(() => Entries.Remove(entry));

        // Delete cached cover
        if (!string.IsNullOrWhiteSpace(entry.CoverLocalPath) &&
            File.Exists(entry.CoverLocalPath))
        {
            try { File.Delete(entry.CoverLocalPath); } catch { }
        }

        await SaveAsync();
    }

    /// <summary>Clear all history entries and cached covers.</summary>
    public async Task ClearAllAsync()
    {
        var entries = Entries.ToList();
        MainThread.BeginInvokeOnMainThread(() => Entries.Clear());

        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.CoverLocalPath) &&
                File.Exists(e.CoverLocalPath))
            {
                try { File.Delete(e.CoverLocalPath); } catch { }
            }
        }

        await SaveAsync();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        try
        {
            Directory.CreateDirectory(CoversDir);
            if (!File.Exists(HistoryFile)) return;
            string json = await File.ReadAllTextAsync(HistoryFile);
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (list == null) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var e in list)
                    Entries.Add(e);
            });
        }
        catch { /* corrupt file — start fresh */ }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var list = Entries.ToList();
            string json = JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(HistoryFile, json);
        }
        catch { }
        finally { _saveLock.Release(); }
    }

    // ── Cover caching ─────────────────────────────────────────────────────────

    private static async Task<string?> CacheCoverAsync(Guid id, string url)
    {
        try
        {
            string ext  = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
            string path = Path.Combine(CoversDir, $"{id:N}{ext}");

            if (File.Exists(path)) return path;

            byte[] bytes = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
