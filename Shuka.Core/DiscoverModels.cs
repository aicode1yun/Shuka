namespace Shuka.Core;

/// <summary>
/// A novel entry shown in the Discover browse listing.
/// </summary>
public record NovelEntry(
    string Title,
    string? Author,
    string Url,
    string? CoverUrl,
    string? Description,
    string? Tags
);

/// <summary>
/// A page of novel listings returned by a browse/search request.
/// </summary>
public record ListingPage(
    List<NovelEntry> Novels,
    bool HasNextPage,
    int CurrentPage
);

/// <summary>
/// Implement on adapters that support browsing/discovery.
/// </summary>
public interface IBrowsableAdapter
{
    /// <summary>Human-readable source name shown in the Discover tab.</summary>
    string SiteName { get; }

    /// <summary>Short description of the source content type.</summary>
    string Description { get; }

    /// <summary>Material Symbols codepoint to use as the source icon.</summary>
    string IconGlyph { get; }

    /// <summary>Whether this source requires Cloudflare bypass to browse.</summary>
    bool RequiresCfBypass { get; }

    /// <summary>URL for the "Recent" listing page (page 1).</summary>
    string GetRecentUrl(int page = 1);

    /// <summary>URL for the "Popular" listing page (page 1).</summary>
    string GetPopularUrl(int page = 1);

    /// <summary>URL for a search query.</summary>
    string GetSearchUrl(string query, int page = 1);

    /// <summary>Parse a listing/search HTML page into novel entries.</summary>
    ListingPage ParseListing(string html, string pageUrl);
}
