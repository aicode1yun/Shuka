using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidUri = Android.Net.Uri;

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
    }

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
