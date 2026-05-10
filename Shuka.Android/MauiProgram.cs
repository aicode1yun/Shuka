using Shuka.Android.Platforms.Android;

namespace Shuka.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("MaterialSymbols.ttf", "MaterialSymbols");
            })
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<Entry, ThemedEntryHandler>();
#if ANDROID
                handlers.AddHandler<Shell, ShukaShellRenderer>();
                // Re-enable AdBlockingWebViewHandler with improved implementation
                handlers.AddHandler<Microsoft.Maui.Controls.WebView, AdBlockingWebViewHandler>();
#endif
            });

        return builder.Build();
    }
}
