namespace Shuka.Android.Behaviors;

/// <summary>
/// Plays a directional slide-in animation on the page content when switching tabs.
/// The tab bar and header are NOT animated — only the content view slides.
///
/// Usage in OnAppearing:
///
///   protected override async void OnAppearing()
///   {
///       base.OnAppearing();
///       TabTransition.Prepare(BodyGrid, myTabIndex: N);
///       await TabTransition.SlideInAsync(BodyGrid);
///   }
///
/// Pass the specific content view (e.g. BodyGrid, BodyScrollView) — not the page itself.
/// </summary>
public static class TabTransition
{
    private static bool _goingRight    = true;
    private static bool _shouldAnimate = false;

    /// <summary>
    /// Call synchronously at the top of OnAppearing (no await).
    /// Captures the slide direction and applies initial layout properties synchronously
    /// on the UI thread to prevent any momentary split-second flashing.
    /// </summary>
    public static void Prepare(View contentView, int myTabIndex)
    {
        int from = AppShell.LastTabIndex;
        int to   = myTabIndex;

        if (from == to)
        {
            _shouldAnimate = false;
            contentView.Opacity = 1.0;
            contentView.TranslationX = 0;
            contentView.Scale = 1.0;
            return;
        }

        _goingRight    = to > from;
        _shouldAnimate = true;

        // Apply starting states synchronously to prevent layout flashing
        contentView.Opacity      = 0;
        contentView.TranslationX = _goingRight ? 80 : -80; // Sleek and spacious lateral shift
        contentView.Scale        = 0.96;                   // Dynamic subtle scale down
    }

    /// <summary>
    /// Animates only the content view using a snappier, fluid deceleration.
    /// </summary>
    public static async Task SlideInAsync(View contentView)
    {
        if (!_shouldAnimate) return;

        // Wait one brief frame to allow the renderer to process the initial state
        await Task.Delay(16);

        // Premium fluid deceleration transition in 240ms
        await Task.WhenAll(
            contentView.TranslateToAsync(0, 0, 240, Easing.CubicOut),
            contentView.FadeToAsync(1.0, 240, Easing.CubicOut),
            contentView.ScaleToAsync(1.0, 240, Easing.CubicOut)
        );
    }
}
