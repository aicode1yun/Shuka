using Android.Content;
using Android.OS;
using AndroidX.Core.Content;

namespace Shuka.Android.Platforms.Android;

/// <summary>
/// Opens or shares EPUB files using native Android intents with proper
/// URI permission granting. Handles both SAF <c>content://</c> URIs and
/// regular file paths via FileProvider.
/// </summary>
public static class EpubOpener
{
    private const string Authority = "com.seizue.shuka.fileprovider";

    /// <summary>
    /// Returns <c>true</c> if the given EPUB path is accessible — either a
    /// SAF <c>content://</c> URI (assumed accessible) or a file that exists on disk.
    /// </summary>
    public static bool IsAccessible(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase)) return true;
        return File.Exists(path);
    }

    /// <summary>
    /// Open an EPUB file in an external reader app using <c>ACTION_VIEW</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The EPUB file doesn't exist on disk.</exception>
    /// <exception cref="InvalidOperationException">No EPUB reader app is installed.</exception>
    public static void Open(string epubPath)
    {
        var intent = BuildViewIntent(epubPath);
        Launch(intent, "No EPUB reader app is installed. Install one from the Play Store and try again.");
    }

    /// <summary>
    /// Share an EPUB file via the system share sheet using <c>ACTION_SEND</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The EPUB file doesn't exist on disk.</exception>
    /// <exception cref="InvalidOperationException">No app found to share the EPUB file.</exception>
    public static void Share(string epubPath, string title)
    {
        var sendIntent = BuildSendIntent(epubPath, title);
        var chooser = Intent.CreateChooser(sendIntent, "Share EPUB")!;
        Launch(chooser, "No app found to share the EPUB file.");
    }

    // ── Intent builders ─────────────────────────────────────────────────────

    private static Intent BuildViewIntent(string epubPath)
    {
        var ctx = global::Android.App.Application.Context;

        if (epubPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = global::Android.Net.Uri.Parse(epubPath)!;
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/epub+zip");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            return intent;
        }

        if (File.Exists(epubPath))
        {
            var file = new Java.IO.File(epubPath);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, Authority, file);
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/epub+zip");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            return intent;
        }

        throw new FileNotFoundException("EPUB file not found.", epubPath);
    }

    private static Intent BuildSendIntent(string epubPath, string title)
    {
        var ctx = global::Android.App.Application.Context;

        if (epubPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = global::Android.Net.Uri.Parse(epubPath)!;
            var intent = new Intent(Intent.ActionSend);
            intent.SetType("application/epub+zip");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.PutExtra(Intent.ExtraSubject, title);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            return intent;
        }

        if (File.Exists(epubPath))
        {
            var file = new Java.IO.File(epubPath);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, Authority, file);
            var intent = new Intent(Intent.ActionSend);
            intent.SetType("application/epub+zip");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.PutExtra(Intent.ExtraSubject, title);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            return intent;
        }

        throw new FileNotFoundException("EPUB file not found.", epubPath);
    }

    // ── Launch helper ───────────────────────────────────────────────────────

    private static void Launch(Intent intent, string noAppMessage)
    {
        var ctx = global::Android.App.Application.Context;

        // NOTE: Do NOT check ResolveActivity() here — on Android 11+
        // (API 30) package-visibility restrictions cause it to return null
        // for implicit intents even when capable apps are installed.
        // StartActivity() itself is unaffected and resolves correctly.

        try
        {
            // Prefer the current Activity for proper back-stack behaviour
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
            {
                activity.StartActivity(intent);
            }
            else
            {
                intent.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(intent);
            }
        }
        catch (global::Android.Content.ActivityNotFoundException)
        {
            throw new InvalidOperationException(noAppMessage);
        }
    }
}
