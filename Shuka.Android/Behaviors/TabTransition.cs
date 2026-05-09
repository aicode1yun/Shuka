namespace Shuka.Android.Behaviors;

/// <summary>
/// Plays a directional slide-in animation on a page when switching tabs.
/// The tab bar (last child of the root Grid) is pinned — only content slides.
/// </summary>
public static class TabTransition
{
    private const int    DurationMs    = 280;
    private const double SlideDistance = 60;

    public static async Task SlideInAsync(ContentPage page, int myTabIndex)
    {
        int from = AppShell.LastTabIndex;
        int to   = myTabIndex;

        if (from == to) return;

        bool goingRight = to > from;

        // Slide the whole page
        page.TranslationX = goingRight ? SlideDistance : -SlideDistance;
        page.Opacity      = 0;

        // Pin the tab bar (last child of root Grid) so it doesn't slide
        View? tabBar = GetTabBar(page);
        if (tabBar != null)
        {
            tabBar.TranslationX = -(page.TranslationX); // counter-offset
        }

        await Task.Delay(16);

        var tasks = new List<Task>
        {
            page.TranslateToAsync(0, 0, DurationMs, Easing.CubicOut),
            page.FadeToAsync(1.0, DurationMs - 30, Easing.Linear),
        };

        // Also animate tab bar back to 0 in sync
        if (tabBar != null)
            tasks.Add(tabBar.TranslateToAsync(0, 0, DurationMs, Easing.CubicOut));

        await Task.WhenAll(tasks);
    }

    private static View? GetTabBar(ContentPage page)
    {
        if (page.Content is Grid root && root.Children.Count > 0)
            return root.Children[^1] as View; // last child = CustomTabBar
        return null;
    }
}
