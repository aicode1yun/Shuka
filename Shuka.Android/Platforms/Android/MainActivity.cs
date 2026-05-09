using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidUri = Android.Net.Uri;
using Microsoft.Maui.Platform;
using AndroidX.Core.View;

namespace Shuka.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public static MainActivity? Instance { get; private set; }

    // Folder picker support
    public const int FolderPickerRequestCode = 9001;
    private TaskCompletionSource<AndroidUri?>? _folderPickerTcs;

    /// <summary>
    /// Opens the system folder picker and returns the selected tree URI, or null if cancelled.
    /// </summary>
    public Task<AndroidUri?> PickFolderAsync()
    {
        _folderPickerTcs = new TaskCompletionSource<AndroidUri?>();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        StartActivityForResult(intent, FolderPickerRequestCode);
        return _folderPickerTcs.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == FolderPickerRequestCode)
        {
            if (resultCode == Result.Ok && data?.Data is AndroidUri uri)
            {
                // Persist permission across reboots
                var flags = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
                ContentResolver?.TakePersistableUriPermission(uri, flags);
                _folderPickerTcs?.TrySetResult(uri);
            }
            else
            {
                _folderPickerTcs?.TrySetResult(null);
            }
            _folderPickerTcs = null;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;

        // Request POST_NOTIFICATIONS permission on Android 13+
#pragma warning disable CA1416
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                != global::Android.Content.PM.Permission.Granted)
            {
                RequestPermissions(
                    [global::Android.Manifest.Permission.PostNotifications], 1002);
            }
        }
#pragma warning restore CA1416

        var bgColor  = (Microsoft.Maui.Graphics.Color)Microsoft.Maui.Controls.Application.Current!.Resources["BgPage"];
        var navColor = (Microsoft.Maui.Graphics.Color)Microsoft.Maui.Controls.Application.Current!.Resources["NavBar"];
        bool lightIcons = App.CurrentTheme != AppTheme.Frost
                       && App.CurrentTheme != AppTheme.Parchment
                       && App.CurrentTheme != AppTheme.Blossom;

        var androidBg  = global::Android.Graphics.Color.Argb(
            (int)(bgColor.Alpha * 255), (int)(bgColor.Red * 255),
            (int)(bgColor.Green * 255), (int)(bgColor.Blue * 255));
        var androidNav = global::Android.Graphics.Color.Argb(
            (int)(navColor.Alpha * 255), (int)(navColor.Red * 255),
            (int)(navColor.Green * 255), (int)(navColor.Blue * 255));

        ApplyStatusBarColor(androidBg,  lightIcons);
        ApplyNavBarColor(androidNav, lightIcons);

        // Add the persistent tab bar as a native overlay on the DecorView.
        // This places it completely outside the MAUI/Shell/fragment hierarchy
        // so it is never affected by page transition animations.
        AddPersistentTabBar();
    }

    /// <summary>
    /// Inflates a single CustomTabBar into the DecorView's content frame so it
    /// sits above all pages and is never part of any fragment transaction.
    /// Respects the system navigation bar inset so it isn't covered by gesture/button nav.
    /// </summary>
    private void AddPersistentTabBar()
    {
        try
        {
            var decorContent = FindViewById<FrameLayout>(global::Android.Resource.Id.Content);
            if (decorContent == null) return;

            var mauiContext = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext
                           ?? (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()
                                  ?.Handler?.MauiContext);
            if (mauiContext == null) return;

            var tabBar = new Controls.CustomTabBar();
            var nativeTabBar = tabBar.ToPlatform(mauiContext);

            float density   = Resources!.DisplayMetrics!.Density;
            int   tabBarPx  = (int)(72 * density);            var lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                tabBarPx,
                GravityFlags.Bottom);

            decorContent.AddView(nativeTabBar, lp);

            // Apply window insets so the tab bar sits above the system nav bar,
            // not behind it. This handles both gesture nav and 3-button nav.
            ViewCompat.SetOnApplyWindowInsetsListener(nativeTabBar, new WindowInsetsCallback(lp, nativeTabBar, tabBarPx));
        }
        catch { /* never crash on tab bar setup */ }
    }

    private sealed class WindowInsetsCallback : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
    {
        private readonly FrameLayout.LayoutParams _lp;
        private readonly global::Android.Views.View _view;
        private readonly int _tabBarPx;

        public WindowInsetsCallback(FrameLayout.LayoutParams lp, global::Android.Views.View view, int tabBarPx)
        {
            _lp       = lp;
            _view     = view;
            _tabBarPx = tabBarPx;
        }

        public AndroidX.Core.View.WindowInsetsCompat? OnApplyWindowInsets(
            global::Android.Views.View? v,
            AndroidX.Core.View.WindowInsetsCompat? insets)
        {
            if (insets == null) return insets;

            var navInsets    = insets!.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
            int navBarHeight = navInsets?.Bottom ?? 0;

            // Sit the tab bar just above the system navigation bar
            _lp.BottomMargin       = navBarHeight;
            _lp.Height             = _tabBarPx;
            _view.LayoutParameters = _lp;

            return insets;
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            StyleBottomNavigationView();
    }

    /// <summary>
    /// Finds the native BottomNavigationView in the view hierarchy and applies
    /// the pill-style active indicator matching the current Shuka theme.
    /// </summary>
    public void StyleBottomNavigationView()
    {
        try
        {
            var bottomNav = FindBottomNavigationView(Window?.DecorView);
            if (bottomNav == null) return;

            var app = Microsoft.Maui.Controls.Application.Current;
            if (app?.Resources == null) return;

            Microsoft.Maui.Graphics.Color accentBg   = app.Resources.TryGetValue("AccentContainer",  out var ab) ? (Microsoft.Maui.Graphics.Color)ab : Microsoft.Maui.Graphics.Color.FromArgb("#2A1E2E");
            Microsoft.Maui.Graphics.Color accent      = app.Resources.TryGetValue("AccentLight",       out var a)  ? (Microsoft.Maui.Graphics.Color)a  : Microsoft.Maui.Graphics.Color.FromArgb("#8B5E5F");
            Microsoft.Maui.Graphics.Color unselected  = app.Resources.TryGetValue("NavBarUnselected",  out var u)  ? (Microsoft.Maui.Graphics.Color)u  : Microsoft.Maui.Graphics.Color.FromArgb("#4A5270");
            Microsoft.Maui.Graphics.Color navBg       = app.Resources.TryGetValue("NavBar",            out var nb) ? (Microsoft.Maui.Graphics.Color)nb : Microsoft.Maui.Graphics.Color.FromArgb("#1A1D27");

            var androidAccentBg   = ToAndroidColor(accentBg);
            var androidAccent     = ToAndroidColor(accent);
            var androidUnselected = ToAndroidColor(unselected);
            var androidNavBg      = ToAndroidColor(navBg);

            bottomNav.SetBackgroundColor(androidNavBg);

            // Pill indicator color
            bottomNav.ItemActiveIndicatorEnabled = true;
            bottomNav.ItemActiveIndicatorColor   = global::Android.Content.Res.ColorStateList.ValueOf(androidAccentBg);

            // Icon + label tint
            var states   = new int[][] { [global::Android.Resource.Attribute.StateChecked], [] };
            var colors   = new int[] { androidAccent, androidUnselected };
            var tintList = new global::Android.Content.Res.ColorStateList(states, colors);
            bottomNav.ItemIconTintList = tintList;
            bottomNav.ItemTextColor    = tintList;

            // Remove ripple
            bottomNav.ItemRippleColor = global::Android.Content.Res.ColorStateList.ValueOf(global::Android.Graphics.Color.Transparent);

            // Always show labels
            bottomNav.LabelVisibilityMode = Google.Android.Material.BottomNavigation.LabelVisibilityMode.LabelVisibilityLabeled;
        }
        catch { /* never crash on styling */ }
    }

    private static Google.Android.Material.BottomNavigation.BottomNavigationView? FindBottomNavigationView(global::Android.Views.View? root)
    {
        if (root is Google.Android.Material.BottomNavigation.BottomNavigationView bnv)
            return bnv;
        if (root is global::Android.Views.ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var found = FindBottomNavigationView(vg.GetChildAt(i));
                if (found != null) return found;
            }
        }
        return null;
    }

    private static global::Android.Graphics.Color ToAndroidColor(Microsoft.Maui.Graphics.Color c) =>
        global::Android.Graphics.Color.Argb(
            (int)(c.Alpha * 255), (int)(c.Red * 255),
            (int)(c.Green * 255), (int)(c.Blue * 255));

    /// <summary>
    /// Updates the status bar and navigation bar background and icon tint to match the current theme.
    /// </summary>
#pragma warning disable CA1416, CA1422
    public void ApplyStatusBarColor(global::Android.Graphics.Color bgColor, bool lightIcons)
    {
        if (Window is null) return;

        // ── Status bar ────────────────────────────────────────────────────────
        Window.SetStatusBarColor(bgColor);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            int appearance = 0;
            if (!lightIcons) appearance |= (int)WindowInsetsControllerAppearance.LightStatusBars;
            Window.InsetsController?.SetSystemBarsAppearance(
                appearance,
                (int)WindowInsetsControllerAppearance.LightStatusBars);
        }
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var flags = Window.DecorView.SystemUiFlags;
            Window.DecorView.SystemUiFlags = lightIcons
                ? flags & ~SystemUiFlags.LightStatusBar
                : flags | SystemUiFlags.LightStatusBar;
        }
    }

    /// <summary>
    /// Updates the bottom navigation bar (gesture bar / button bar) color and icon tint.
    /// Effective on API 26+ for icon tint, API 21+ for color.
    /// </summary>
    public void ApplyNavBarColor(global::Android.Graphics.Color navColor, bool lightIcons)
    {
        if (Window is null) return;

        Window.SetNavigationBarColor(navColor);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            // API 30+: WindowInsetsController
            int appearance = 0;
            if (!lightIcons) appearance |= (int)WindowInsetsControllerAppearance.LightNavigationBars;
            Window.InsetsController?.SetSystemBarsAppearance(
                appearance,
                (int)WindowInsetsControllerAppearance.LightNavigationBars);
        }
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            // API 26–29: SystemUiFlags.LightNavigationBar
            var flags = Window.DecorView.SystemUiFlags;
            Window.DecorView.SystemUiFlags = lightIcons
                ? flags & ~SystemUiFlags.LightNavigationBar
                : flags | SystemUiFlags.LightNavigationBar;
        }
        // API 21–25: color set above, no icon tint control
    }
#pragma warning restore CA1416, CA1422
}
