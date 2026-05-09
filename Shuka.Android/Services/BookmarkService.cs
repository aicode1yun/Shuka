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
    public void AddBookmark(string url, string title, string author, string siteName, int chapterCount = 0)
    {
        // Check if already bookmarked
        var existing = Bookmarks.FirstOrDefault(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
            return; // Already bookmarked

        var bookmark = new BookmarkItem
        {
            Url = url,
            Title = title,
            Author = author,
            SiteName = siteName,
            ChapterCount = chapterCount,
            BookmarkedAt = DateTime.Now,
            Tags = new List<string>()
        };

        MainThread.BeginInvokeOnMainThread(() => Bookmarks.Insert(0, bookmark));
        SaveBookmarks();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates an existing bookmark's tags.
    /// </summary>
    public void UpdateBookmarkTags(string url, List<string> tags)
    {
        var bookmark = Bookmarks.FirstOrDefault(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        
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
        var bookmark = Bookmarks.FirstOrDefault(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        
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
        var bookmark = Bookmarks.FirstOrDefault(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        
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
        var bookmark = Bookmarks.FirstOrDefault(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        
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
    public bool IsBookmarked(string url)
    {
        return Bookmarks.Any(b => 
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
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
}
