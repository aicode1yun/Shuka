<div align="center">
<img src="https://github.com/user-attachments/assets/92bec4c5-db17-4f5a-bbfa-c8bc658acb1f"
     width="140"
     height="140"
     alt="appicon" />

# Shuka
A cross-platform web novel downloader and machine translation (MTL) tool that converts Chinese web novels into English `.epub` for any e-reader. Available as a **PowerShell CLI for Windows** and an **Android app built with .NET MAUI.**

<p align="center">   
     
[![GitHub Downloads](https://img.shields.io/github/downloads/seizue/Shuka/total?logo=github)](https://github.com/seizue/Shuka/releases)
[![GitHub Release](https://img.shields.io/github/v/release/seizue/Shuka)](https://github.com/seizue/Shuka/releases)
[![GitHub License](https://img.shields.io/github/license/seizue/Shuka)](https://github.com/seizue/Shuka/blob/main/LICENSE)

</div>


## Screenshot
<details>
 <summary>🔽 <strong>[ OPEN SCREENSHOT ]</strong></summary>
  <br>
  <img width="1366" alt="Shuka Screenshot"
       src="https://github.com/user-attachments/assets/c07f2852-306a-4c6d-aec6-7336507d673e" />
</details>


### Supported Sites

| Site | Example URL |
|------|------------|
| [69shuba.com](https://www.69shuba.com/) | `https://www.69shuba.com/book/90417.htm` |
| [52shuku.net](https://www.52shuku.net) | `https://www.52shuku.net/bl/09_b/bkd7d.html` |
| [czbooks.net](https://czbooks.net) | `https://czbooks.net/n/clgajm` |
| [dmxs.org](https://www.dmxs.org) | `https://www.dmxs.org/gdjk/22982.html` |
| [quanben.io](https://www.quanben.io) | `https://www.quanben.io/n/aoshidanshen/list.html` |

> **czbooks.net** and **69shuba.com** is protected by Cloudflare. Shuka handles this automatically using a headless browser on Windows and a hidden WebView on Android — no extra setup needed.


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
