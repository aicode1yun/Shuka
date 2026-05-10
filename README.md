# Shuka

A tool that downloads Chinese web novels, translates them to English via Google Translate, and saves them as `.epub` files ready for any e-reader. Available on **Windows** and **Android**.

![Github Downloads](https://img.shields.io/github/downloads/seizue/Shuka/total?style=flat&logo=github)

## Screenshot

<img width="1366" height="736" alt="Shuka (2)" src="https://github.com/user-attachments/assets/c07f2852-306a-4c6d-aec6-7336507d673e" />


### Supported Sites

| Site | Example URL |
|------|------------|
| [69shuba.com](https://www.69shuba.com/) | `https://www.69shuba.com/book/90417.htm` |
| [52shuku.net](https://www.52shuku.net) | `https://www.52shuku.net/bl/09_b/bkd7d.html` |
| [czbooks.net](https://czbooks.net) | `https://czbooks.net/n/clgajm` |
| [dmxs.org](https://www.dmxs.org) | `https://www.dmxs.org/gdjk/22982.html` |
| [quanben.io](https://www.quanben.io) | `https://www.quanben.io/n/aoshidanshen/list.html` |

> **czbooks.net** and **69shuba.com** is protected by Cloudflare. Shuka handles this automatically using a headless browser on Windows and a hidden WebView on Android — no extra setup needed.

## Features

### General
- Downloads and translates Chinese novels to English
- Saves output as a properly formatted `.epub` (cover, title page, chapters)
- Auto-detects cover image from the novel's index page
- Generates a styled SVG cover if no image is found
- Parallel fetch + translate pipeline for faster downloads
- Checkpoint system — resumes interrupted downloads from where they left off
- Extensible adapter system for adding new sites

### Windows 
- Interactive TUI with Spectre.Console — spinner, live progress bar, and styled output
- Batch download mode — queue multiple novels and download sequentially
- Built-in Cloudflare bypass using a headless Chromium browser (Playwright)
- `--solve-cf` command to manually refresh Cloudflare cookies for protected sites
- CLI mode — pass URL and options directly to skip the TUI

### Android 
- Discover tab — browse supported sources in a built-in WebView
- **Fetch** button in the WebView — grabs the current novel URL and pre-fills the Download tab, ready for custom cover or chapter range
- **Download** button in the WebView — queues the novel directly without leaving the browser
- **Bookmark** button in the WebView — saves novels for quick access later
- Bookmarks page — view all saved novels organized by source, with search, filtering, and tagging
- Multi-select mode — select multiple bookmarks to batch download or delete
- Built-in ad blocker — blocks ads and trackers in the WebView browser (can be toggled in Settings)
- Source filter and pin system — pin favourite sources to the top of the Discover list
- Draft persistence — the URL field is saved when switching apps and restored on return
- Queue multiple novels at once — each download runs independently in the background
- Downloads continue even when the app is closed or the screen is off
- Auto-retries up to 5 times on error with increasing delay between attempts
- On failure, Retry and Dismiss buttons appear on the download card
- Prevents duplicate downloads — queuing the same URL twice is blocked
- Open or share the finished `.epub` directly from the Downloads tab
- Custom save location with full storage permission handling (Android 11+)
- Built-in themes: Obsidian, Rosewood, Slate, Amoled, Frost, Parchment, Blossom

## Installation

### Windows
Download and run `Shuka-Windows-vX.X.X.exe` from the [Releases](https://github.com/seizue/Shuka/releases) page. No admin rights required.

The installer places everything in `%LocalAppData%\Shuka`, creates a Start Menu shortcut, and installs the Chromium browser needed for Cloudflare bypass.

### Android
Download `Shuka-Android-vX.X.X.apk` from the [Releases](https://github.com/seizue/Shuka/releases) page and install it. Enable **Install from unknown sources** if prompted.

Default save location is `Downloads/Shuka` on internal storage. You can change this in **Settings → Download Location**.

> On Android 11 and above, Shuka will ask for **All Files Access** when setting a custom save folder.

## Usage

### Windows

Launch **Shuka** from the Start Menu or desktop shortcut. The interactive TUI opens automatically:

```
  ╔══════════════════════════════════╗
  ║  Shuka  Chinese To English EPUB  ║
  ╚══════════════════════════════════╝

  What would you like to do?
  > Download single novel
    Batch download (multiple novels)
    Fix Cloudflare (--solve-cf)
    View supported sites
    Exit
```

**Single download** — paste the novel URL, optionally provide a cover image URL, and choose how many chapters to download (0 = all). Progress is shown live with a spinner and progress bar. The `.epub` is saved to your Downloads folder.

**Batch download** — add novels one by one, then start. A summary table is shown before downloading. All novels download sequentially, one `.epub` each.

**Fix Cloudflare** — opens a visible browser window so you can solve the Cloudflare challenge for czbooks.net or 69shuba.com. Run this once before downloading from those sites. Cookies are saved automatically.

**View supported sites** — lists all supported sites with example URLs directly in the terminal.

### Command line

You can also pass arguments directly to skip the TUI:

```bash
# Single novel (all chapters)
Shuka.exe <url>

# Single novel (first 3 chapters, useful for testing)
Shuka.exe <url> 3

# Single novel with a custom cover
Shuka.exe <url> 0 <cover-url>

# Batch from a text file (one URL per line, # for comments)
Shuka.exe --batch urls.txt

# Solve Cloudflare for a site (run once before downloading)
Shuka.exe --solve-cf https://www.69shuba.com

# Examples with supported sites
Shuka.exe https://www.69shuba.com/book/90417.htm
Shuka.exe https://czbooks.net/n/clgajm 50
Shuka.exe https://www.dmxs.org/gdjk/22982.html
Shuka.exe https://www.52shuku.net/bl/09_b/bkd7d.html
Shuka.exe https://www.quanben.io/n/aoshidanshen/list.html 3
```

Output is saved to `%USERPROFILE%\Downloads` by default.


## Building from source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (Windows CLI) and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Android / MAUI).

```bash
dotnet build -c Release
```

**Windows installer** — publish first then compile with [Inno Setup](https://jrsoftware.org/isinfo.php):

```bash
dotnet publish Shuka.Windows/Shuka.Windows.csproj -c Release -r win-x64 --self-contained true -o Shuka.Windows/bin/publish
Shuka.Windows/bin/publish/Shuka.exe playwright install chromium
ISCC.exe Shuka.Windows/installer.iss
```

**Android APK:**

```bash
dotnet publish Shuka.Android/Shuka.Android.csproj -f net10.0-android -c Release
```

## Adding a new site

Implement `ISiteAdapter` in `Shuka.Core` and register it in `BookService`:

```csharp
class MySiteAdapter : ISiteAdapter
{
    public string SiteName => "mysite.com";
    public bool Matches(string url) => url.Contains("mysite.com");
    public string NormalizeUrl(string url) => /* strip chapter suffix etc */;
    public IndexInfo ParseIndex(string html, string indexUrl) => /* parse title, author, chapter list */;
    public List<string> ExtractChapterText(string html) => /* extract paragraphs */;
}
```

## License

See [LICENSE](LICENSE).
