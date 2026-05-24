using Shuka.Android.Services;

namespace Shuka.Android.Pages;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {UpdateService.InstalledVersion}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Ensure tab bar is hidden when this page appears
        // Use BeginInvokeOnMainThread to ensure it runs after navigation completes
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MainActivity.Instance?.SetTabBarVisible(false);
        });
        
        // Force layout refresh to prevent blank page issue
        this.ForceLayout();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Only restore tab bar if we're going back to a page that needs it
        // Check if we're still in the navigation stack
        if (Navigation?.NavigationStack?.Contains(this) == false)
        {
            var previousPage = Navigation?.NavigationStack?.LastOrDefault();
            if (previousPage == null || 
                (previousPage is not AboutPage &&
                 previousPage is not SourceBrowsePage &&
                 previousPage is not WebBrowsePage &&
                 previousPage is not ShukaQuestPage))
            {
                MainActivity.Instance?.SetTabBarVisible(true);
            }
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        // Additional safety: ensure content is visible when navigated to
        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.IsVisible = true;
            this.Opacity = 1.0;
            MainActivity.Instance?.SetTabBarVisible(false);
        });
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
        => await Navigation.PopAsync();

    private async void OnGitHubTapped(object sender, TappedEventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri("https://github.com/seizue/Shuka")); }
        catch { await DisplayAlertAsync("Error", "Could not open browser.", "OK"); }
    }

    private async void OnBugTapped(object sender, TappedEventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri("https://github.com/seizue/Shuka/issues/new")); }
        catch { await DisplayAlertAsync("Error", "Could not open browser.", "OK"); }
    }
}
