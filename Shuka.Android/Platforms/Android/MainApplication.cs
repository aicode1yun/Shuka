using Android.App;
using Android.Runtime;

namespace Shuka.Android;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        // If the app was killed while a download was running, the foreground
        // service may still be showing a stale notification. Stop it on startup
        // since the DownloadManager is a fresh singleton with no active downloads.
        Platforms.Android.DownloadForegroundService.Stop();
    }
}
