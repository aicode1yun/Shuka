using Spectre.Console;
using Shuka.Core;

namespace Shuka;

/// <summary>
/// Interactive TUI for Shuka — launched when no arguments are passed
/// or when --ui is specified. Wraps the same Downloader pipeline used
/// by the CLI, with a nicer Spectre.Console interface.
/// </summary>
internal static class Tui
{
    public static async Task RunAsync(Downloader downloader)
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[grey]What would you like to do?[/]")
                    .HighlightStyle(new Style(Color.IndianRed1))
                    .AddChoices(
                        "Download single novel",
                        "Batch download (multiple novels)",
                        "Fix Cloudflare (--solve-cf)",
                        "View supported sites",
                        "Exit"));

            switch (choice)
            {
                case "Download single novel":
                    await RunSingleAsync(downloader);
                    break;
                case "Batch download (multiple novels)":
                    await RunBatchAsync(downloader);
                    break;
                case "Fix Cloudflare (--solve-cf)":
                    await RunSolveCfAsync();
                    break;
                case "View supported sites":
                    RunViewSites();
                    break;
                case "Exit":
                    AnsiConsole.MarkupLine("\n[grey]Goodbye![/]");
                    return;
            }

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to menu...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    // ── Single download ───────────────────────────────────────────────────────

    private static async Task RunSingleAsync(Downloader downloader)
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold yellow]  Single Novel[/]\n");

        string url = AnsiConsole.Ask<string>("[cyan]Novel URL:[/]").Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        string coverInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[grey]Cover URL[/] [dim](leave blank to auto-detect)[/]:")
                .AllowEmpty());
        string? cover = string.IsNullOrWhiteSpace(coverInput) ? null : coverInput.Trim();

        int chapters = AnsiConsole.Prompt(
            new TextPrompt<int>("[grey]Chapters[/] [dim](0 = all)[/]:")
                .DefaultValue(0)
                .ValidationErrorMessage("[red]Enter a number[/]"));

        AnsiConsole.WriteLine();

        await RunDownloadAsync(downloader, url, chapters, cover);
    }

    // ── Batch download ────────────────────────────────────────────────────────

    private static async Task RunBatchAsync(Downloader downloader)
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold yellow]  Batch Download[/]\n");
        AnsiConsole.MarkupLine("[grey]Add novels one by one, then start downloading.[/]\n");

        var queue = new List<(string Url, string? Cover)>();

        while (true)
        {
            AnsiConsole.MarkupLine($"[dim]--- Novel #{queue.Count + 1} ---[/]");

            string url = AnsiConsole.Ask<string>("[cyan]Novel URL[/] [dim](blank to stop adding)[/]:").Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                if (queue.Count == 0) return;
                break;
            }

            string coverInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[grey]Cover URL[/] [dim](leave blank to auto-detect)[/]:")
                    .AllowEmpty());
            string? cover = string.IsNullOrWhiteSpace(coverInput) ? null : coverInput.Trim();

            queue.Add((url, cover));
            AnsiConsole.MarkupLine($"[green]✓ Novel #{queue.Count} added.[/]\n");

            var next = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[grey]{queue.Count} novel(s) queued. What next?[/]")
                    .HighlightStyle(new Style(Color.IndianRed1))
                    .AddChoices("Add another novel", $"Start downloading ({queue.Count} queued)", "Cancel"));

            if (next.StartsWith("Start")) break;
            if (next == "Cancel") return;
        }

        if (queue.Count == 0) return;

        AnsiConsole.WriteLine();

        // Show queue summary
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]#[/]").Centered())
            .AddColumn("[grey]URL[/]")
            .AddColumn("[grey]Cover[/]");

        for (int i = 0; i < queue.Count; i++)
            table.AddRow(
                $"[dim]{i + 1}[/]",
                $"[cyan]{Markup.Escape(queue[i].Url)}[/]",
                queue[i].Cover != null ? "[green]custom[/]" : "[dim]auto[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        for (int i = 0; i < queue.Count; i++)
        {
            AnsiConsole.MarkupLine($"[bold]\n[{i + 1}/{queue.Count}] Downloading...[/]");
            await RunDownloadAsync(downloader, queue[i].Url, 0, queue[i].Cover);
        }

        AnsiConsole.MarkupLine("\n[green]Batch complete! Check your Downloads folder.[/]");
    }

    // ── Solve CF ──────────────────────────────────────────────────────────────

    private static async Task RunSolveCfAsync()
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold yellow]  Fix Cloudflare[/]\n");
        AnsiConsole.MarkupLine("[grey]Opens a visible browser window so you can solve the Cloudflare challenge.[/]");
        AnsiConsole.MarkupLine("[grey]After the page loads, come back here and press Enter.[/]\n");

        string url = AnsiConsole.Ask<string>("[cyan]Site URL[/] [dim](e.g. https://www.69shuba.com)[/]:").Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        await PlaywrightFetcher.SolveCfInteractiveAsync(url);
        AnsiConsole.MarkupLine("\n[green]✓ Cloudflare cookies saved. Downloads should now work.[/]");
    }

    // ── Download pipeline with live progress ──────────────────────────────────

    private static async Task RunDownloadAsync(
        Downloader downloader, string url, int chapters, string? cover)
    {
        BookInfo? book = null;

        // Phase 1: gather book info
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("indianred1"))
            .StartAsync("Gathering book info...", async ctx =>
            {
                try
                {
                    book = await downloader.GatherBookInfoAsync(url, chapters, cover);
                    ctx.Status($"[green]Found:[/] {Markup.Escape(book.TitleEn ?? book.Title)}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                }
            });

        if (book == null) return;

        // Show book info panel
        var panel = new Panel(
            $"[bold]{Markup.Escape(book.TitleEn ?? book.Title)}[/]\n" +
            $"[grey]{Markup.Escape(book.AuthorEn ?? book.Author)}[/]\n" +
            $"[dim]{book.Total} chapters · {book.Adapter.SiteName}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.IndianRed1)
            .Padding(1, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        if (book.Total == 0)
        {
            AnsiConsole.MarkupLine("[red]No chapters found.[/]");
            return;
        }

        // Phase 2: download + translate with progress bar
        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn().FinishedStyle(Style.Parse("green")),
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.Dots) { Style = Style.Parse("indianred1") })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(
                    $"[cyan]{Markup.Escape(book.TitleEn ?? book.Title)}[/]",
                    maxValue: book.Total);

                try
                {
                    await downloader.ProcessBookAsync(book, null,
                        onProgress: (current, total, msg) =>
                        {
                            task.Value       = current;
                            task.Description = $"[cyan]{Markup.Escape(book.TitleEn ?? book.Title)}[/] [dim]{Markup.Escape(msg)}[/]";
                        });

                    task.Value       = book.Total;
                    task.Description = $"[green]✓ {Markup.Escape(book.TitleEn ?? book.Title)}[/]";
                }
                catch (Exception ex)
                {
                    task.Description = $"[red]✗ {Markup.Escape(ex.Message)}[/]";
                }
            });
    }

    // ── View supported sites ──────────────────────────────────────────────────

    private static void RunViewSites()
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold yellow]  Supported Sites[/]\n");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Site[/]"))
            .AddColumn(new TableColumn("[grey]Example URL[/]"))
            .AddColumn(new TableColumn("[grey]Notes[/]").Centered());

        table.AddRow(
            "[cyan]52shuku.net[/]",
            "[dim]https://www.52shuku.net/bl/09_b/bkd7d.html[/]",
            "");
        table.AddRow(
            "[cyan]czbooks.net[/]",
            "[dim]https://czbooks.net/n/clgajm[/]",
            "[yellow]CF bypass[/]");
        table.AddRow(
            "[cyan]dmxs.org[/]",
            "[dim]https://www.dmxs.org/GLBH/1840.html[/]",
            "");
        table.AddRow(
            "[cyan]69shuba.com[/]",
            "[dim]https://www.69shuba.com/book/90488.htm[/]",
            "[yellow]CF bypass[/]");
        table.AddRow(
            "[cyan]quanben.io[/]",
            "[dim]https://www.quanben.io/n/aoshidanshen/list.html[/]",
            "");

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Sites marked [/][yellow]CF bypass[/][grey] require running [/][indianred1]Fix Cloudflare[/][grey] once before downloading.[/]");
    }

    private static void RenderHeader()
    {
        // Plain styled header — always fits regardless of terminal width
        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Markup("[bold indianred1]  ╔══════════════════════════════════════╗[/]\n" +
                       "[bold indianred1]  ║[/]  [bold white]Shuka[/]  [grey]Chinese To English EPUB[/]  [bold indianred1]║[/]\n" +
                       "[bold indianred1]  ╚══════════════════════════════════════╝[/]\n"));
        AnsiConsole.WriteLine();
    }
}
