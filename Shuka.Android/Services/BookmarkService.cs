using System.Collections.ObjectModel;
using System.Text.Json;

namespace Shuka.Android.Services;

/// <summary>
/// Manages bookmarked novels for quick access.
/// Bookmarks are persisted to Preferences and organized by source site.
/// </summary>
public class BookmarkService
{
    public static readonly BookmarkService Instance = new();

    private const string PrefKeyBookmarks = "bookmarks_json";

    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();

    /// <summary>
    /// Event fired when bookmarks are added, removed, or modified.
    /// </summary>
    public event EventHandler? BookmarksChanged;

    private BookmarkService()
    {
        LoadBookmarks();
    }

    /// <summary>
    /// Adds a bookmark for a novel.
    /// </summary>
    public void AddBookmark(string url, string title, string author, string siteName, int chapterCount = 0, string? coverUrl = null)
    {
        string normalizedUrl = NormalizeUrlKey(url);

        // Check if already bookmarked
        var existing = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.SiteName, siteName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return; // Already bookmarked

        var bookmark = new BookmarkItem
        {
            Url = url,
            Title = title,
            Author = author,
            SiteName = siteName,
            ChapterCount = chapterCount,
            CoverUrl = coverUrl,
            BookmarkedAt = DateTime.Now,
            Tags = new List<string>()
        };

        MainThread.BeginInvokeOnMainThread(() => Bookmarks.Insert(0, bookmark));
        SaveBookmarks();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the chapter count for an existing bookmark.
    /// No-op when the URL is not bookmarked or <paramref name="chapterCount"/> is not positive.
    /// </summary>
    public void UpdateBookmarkChapterCount(string url, int chapterCount)
    {
        if (chapterCount <= 0) return;
        string normalized = NormalizeUrlKey(url);
        var bookmark = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalized, StringComparison.OrdinalIgnoreCase));
        if (bookmark == null) return;
        bookmark.ChapterCount = chapterCount;
        SaveBookmarks();
    }

    /// <summary>
    /// Updates an existing bookmark's tags.
    /// </summary>
    public void UpdateBookmarkTags(string url, List<string> tags)
    {
        string normalizedUrl = NormalizeUrlKey(url);
        var bookmark = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (bookmark != null)
        {
            bookmark.Tags = tags;
            SaveBookmarks();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Adds a tag to a bookmark.
    /// </summary>
    public void AddTag(string url, string tag)
    {
        string normalizedUrl = NormalizeUrlKey(url);
        var bookmark = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (bookmark != null && !bookmark.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            bookmark.Tags.Add(tag);
            SaveBookmarks();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Removes a tag from a bookmark.
    /// </summary>
    public void RemoveTag(string url, string tag)
    {
        string normalizedUrl = NormalizeUrlKey(url);
        var bookmark = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (bookmark != null)
        {
            bookmark.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            SaveBookmarks();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Removes a bookmark by URL.
    /// </summary>
    public void RemoveBookmark(string url)
    {
        string normalizedUrl = NormalizeUrlKey(url);
        var bookmark = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (bookmark != null)
        {
            MainThread.BeginInvokeOnMainThread(() => Bookmarks.Remove(bookmark));
            SaveBookmarks();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Checks if a URL is bookmarked.
    /// </summary>
    public bool IsBookmarked(string url, string? siteName = null)
    {
        string normalizedUrl = NormalizeUrlKey(url);
        return Bookmarks.Any(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(siteName) ||
             string.Equals(b.SiteName, siteName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets all bookmarks for a specific site.
    /// </summary>
    public List<BookmarkItem> GetBookmarksForSite(string siteName)
    {
        return Bookmarks
            .Where(b => string.Equals(b.SiteName, siteName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.BookmarkedAt)
            .ToList();
    }

    /// <summary>
    /// Gets the count of bookmarks for a specific site.
    /// </summary>
    public int GetBookmarkCountForSite(string siteName)
    {
        return Bookmarks.Count(b =>
            string.Equals(b.SiteName, siteName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears all bookmarks.
    /// </summary>
    public void ClearAll()
    {
        MainThread.BeginInvokeOnMainThread(() => Bookmarks.Clear());
        SaveBookmarks();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Restores a single bookmark from a backup.
    /// If <paramref name="replace"/> is true and a bookmark with the same URL already exists,
    /// the existing entry is removed before inserting the restored one.
    /// If <paramref name="replace"/> is false, the existing entry is left untouched (no-op).
    /// </summary>
    public void RestoreBookmark(BookmarkItem item, bool replace)
    {
        string normalizedUrl = NormalizeUrlKey(item.Url);

        var existing = Bookmarks.FirstOrDefault(b =>
            string.Equals(NormalizeUrlKey(b.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (!replace)
                return; // Keep existing — do nothing

            // Replace: remove old entry
            MainThread.BeginInvokeOnMainThread(() => Bookmarks.Remove(existing));
        }

        // Ensure restore date is set
        if (item.BookmarkedAt == default)
            item.BookmarkedAt = DateTime.Now;

        MainThread.BeginInvokeOnMainThread(() => Bookmarks.Insert(0, item));
        SaveBookmarks();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns a snapshot of all bookmarks suitable for JSON export.
    /// </summary>
    public List<BookmarkItem> GetExportSnapshot() => Bookmarks.ToList();

    private void LoadBookmarks()
    {
        try
        {
            string json = Preferences.Default.Get(PrefKeyBookmarks, "");
            if (string.IsNullOrWhiteSpace(json))
                return;

            var items = JsonSerializer.Deserialize<List<BookmarkItem>>(json);
            if (items != null)
            {
                foreach (var item in items)
                    Bookmarks.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BookmarkService] Error loading bookmarks: {ex.Message}");
        }
    }

    private void SaveBookmarks()
    {
        try
        {
            var json = JsonSerializer.Serialize(Bookmarks.ToList());
            Preferences.Default.Set(PrefKeyBookmarks, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BookmarkService] Error saving bookmarks: {ex.Message}");
        }
    }

    private static string NormalizeUrlKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/').ToLowerInvariant();

        string host = uri.Host.ToLowerInvariant();
        string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/').ToLowerInvariant();
        return $"{host}{path}";
    }
}

/// <summary>
/// Represents a bookmarked novel.
/// </summary>
public class BookmarkItem
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string SiteName { get; set; } = "";
    public DateTime BookmarkedAt { get; set; }
    public int ChapterCount { get; set; } = 0;
    public List<string> Tags { get; set; } = new();
    public string? CoverUrl { get; set; }
}
