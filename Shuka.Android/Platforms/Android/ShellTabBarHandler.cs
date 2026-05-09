using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Graphics.Drawables.Shapes;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Google.Android.Material.BottomNavigation;

namespace Shuka.Android.Platforms.Android;

/// <summary>
/// Custom Shell renderer that styles the BottomNavigationView with a pill
/// indicator on the active tab — matching the GitHub-style nav shown in the design.
/// Registered via ConfigureMauiHandlers in MauiProgram.
/// </summary>
public class ShukaShellRenderer : ShellRenderer
{
    public ShukaShellRenderer(global::Android.Content.Context context) : base(context) { }

    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(
        ShellItem shellItem)
        => new PillBottomNavTracker();
}

/// <summary>
/// Applies a rounded-rectangle (pill) background to the active BottomNavigationView item.
/// </summary>
internal class PillBottomNavTracker : IShellBottomNavViewAppearanceTracker
{
    private static readonly List<(PillBottomNavTracker Tracker, BottomNavigationView View)> _instances = new();

    private BottomNavigationView? _view;

    public void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
    {
        _view = bottomView;
        _instances.RemoveAll(x => x.Tracker == this);
        _instances.Add((this, bottomView));
        ApplyPillStyle(bottomView);
    }

    public void ResetAppearance(BottomNavigationView bottomView)
        => ApplyPillStyle(bottomView);

    public void Dispose()
    {
        _instances.RemoveAll(x => x.Tracker == this);
    }

    public static void RefreshAll()
    {
        foreach (var (_, view) in _instances.ToList())
            ApplyPillStyle(view);
    }

    private static void ApplyPillStyle(BottomNavigationView bottomView)
    {
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app?.Resources == null) return;

            Color accentBg   = app.Resources.TryGetValue("AccentContainer",  out var ab) ? (Color)ab : Color.FromArgb("#2A1E2E");
            Color accent     = app.Resources.TryGetValue("AccentLight",       out var a)  ? (Color)a  : Color.FromArgb("#8B5E5F");
            Color unselected = app.Resources.TryGetValue("NavBarUnselected",  out var u)  ? (Color)u  : Color.FromArgb("#4A5270");
            Color navBg      = app.Resources.TryGetValue("NavBar",            out var nb) ? (Color)nb : Color.FromArgb("#1A1D27");

            var androidAccentBg   = ToAndroid(accentBg);
            var androidAccent     = ToAndroid(accent);
            var androidUnselected = ToAndroid(unselected);
            var androidNavBg      = ToAndroid(navBg);

            bottomView.SetBackgroundColor(androidNavBg);
            bottomView.ItemActiveIndicatorEnabled = true;
            bottomView.ItemActiveIndicatorColor   = ColorStateList.ValueOf(androidAccentBg);

            var states   = new int[][] { [global::Android.Resource.Attribute.StateChecked], [] };
            var colors   = new int[] { androidAccent, androidUnselected };
            var tintList = new ColorStateList(states, colors);

            bottomView.ItemIconTintList  = tintList;
            bottomView.ItemTextColor     = tintList;
            bottomView.ItemRippleColor   = ColorStateList.ValueOf(global::Android.Graphics.Color.Transparent);
            bottomView.LabelVisibilityMode = LabelVisibilityMode.LabelVisibilityLabeled;
        }
        catch { }
    }

    private static GradientDrawable CreatePillDrawable(global::Android.Graphics.Color color)
    {
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Rectangle);
        d.SetCornerRadius(64f);
        d.SetColor(color);
        return d;
    }

    private static global::Android.Graphics.Color ToAndroid(Color c) =>
        global::Android.Graphics.Color.Argb(
            (int)(c.Alpha * 255),
            (int)(c.Red   * 255),
            (int)(c.Green * 255),
            (int)(c.Blue  * 255));
}
