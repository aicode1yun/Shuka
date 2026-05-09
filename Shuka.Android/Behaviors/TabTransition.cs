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
///       TabTransition.Prepare(myTabIndex: N);
///       await TabTransition.SlideInAsync(BodyGrid, myTabIndex: N);
///   }
///
/// Pass the specific content view (e.g. BodyGrid, BodyScrollView) — not the page itself.
/// </summary>
public static class TabTransition
{
    private const int    DurationMs    = 480;
    private const double SlideDistance = 14;

    private static bool _goingRight    = true;
    private static bool _shouldAnimate = false;

    /// <summary>
    /// Call synchronously at the top of OnAppearing (no await).
    /// Captures the slide direction from AppShell before any awaits change it.
    /// Does NOT touch page or content opacity — avoids any tab bar flash.
    /// </summary>
    public static void Prepare(int myTabIndex)
    {
        int from = AppShell.LastTabIndex;
        int to   = myTabIndex;

        if (from == to)
        {
            _shouldAnimate = false;
            return;
        }

        _goingRight    = to > from;
        _shouldAnimate = true;
    }

    /// <summary>
    /// Animates only the content view (not the tab bar or header).
    /// Hides and offsets the content view after layout, then animates it in.
    /// </summary>
    public static async Task SlideInAsync(View contentView)
    {
        if (!_shouldAnimate) return;

        // Wait one frame for layout to complete so TranslationX sticks.
        await Task.Delay(16);

        // Hide and offset only the content — tab bar and header are untouched.
        contentView.Opacity      = 0;
        contentView.TranslationX = _goingRight ? SlideDistance : -SlideDistance;

        // One more frame so the renderer picks up the starting state.
        await Task.Delay(16);

        await Task.WhenAll(
            contentView.TranslateToAsync(0, 0, DurationMs, Easing.SinOut),
            contentView.FadeToAsync(1.0, DurationMs, Easing.SinOut)
        );
    }
}
