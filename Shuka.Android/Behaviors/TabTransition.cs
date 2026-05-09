namespace Shuka.Android.Behaviors;

/// <summary>
/// Plays a directional slide-in animation on a page when it becomes the active tab.
/// Call from OnAppearing — sets TranslationX before the frame renders so there's no flash.
/// </summary>
public static class TabTransition
{
    private const int    DurationMs    = 320;
    private const double SlideDistance = 80; // small offset — feels like a nudge, not a full slide

    public static async Task SlideInAsync(ContentPage page, int myTabIndex)
    {
        int from = AppShell.LastTabIndex;
        int to   = myTabIndex;

        // No animation on same-tab tap or first load
        if (from == to) return;

        bool goingRight = to > from;

        // Set off-screen position and invisible before the frame paints
        page.TranslationX = goingRight ? SlideDistance : -SlideDistance;
        page.Opacity      = 0;

        // Wait two frames so the renderer commits the starting position
        await Task.Delay(16);

        await Task.WhenAll(
            page.TranslateToAsync(0, 0, DurationMs, Easing.CubicOut),
            page.FadeToAsync(1.0, DurationMs - 40, Easing.Linear));
    }
}
