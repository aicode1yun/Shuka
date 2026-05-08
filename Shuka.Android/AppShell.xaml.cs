using Shuka.Android.Pages;

namespace Shuka.Android;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(SourceBrowsePage), typeof(SourceBrowsePage));
    }
}
