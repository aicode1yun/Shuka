using Shuka.Android.Pages;

namespace Shuka.Android;

public partial class AppShell : Shell
{
    // Tab order matches the TabBar declaration — used by pages to determine slide direction
    public static readonly string[] TabRoutes =
        ["MainPage", "DownloadsPage", "HistoryPage", "SettingsPage"];

    public static int LastTabIndex  { get; private set; } = 0;
    public static int ActiveTabIndex { get; private set; } = 0;

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(SourceBrowsePage), typeof(SourceBrowsePage));

        Navigating += OnShellNavigating;
    }

    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        string segment = e.Target?.Location?.OriginalString?.Split('/').LastOrDefault() ?? "";
        int newIndex = Array.IndexOf(TabRoutes, segment);
        if (newIndex < 0) return;

        LastTabIndex   = ActiveTabIndex;
        ActiveTabIndex = newIndex;
    }
}
