using Shuka.Core;

namespace Shuka.Android.Pages;

public partial class DiscoverPage : ContentPage
{
    public DiscoverPage()
    {
        InitializeComponent();
        BuildSourceCards();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BodyScrollView.Opacity = 0;
        BodyScrollView.TranslationY = 18;
        await Task.WhenAll(
            BodyScrollView.FadeToAsync(1.0, 220, Easing.CubicOut),
            BodyScrollView.TranslateToAsync(0, 0, 220, Easing.CubicOut));
    }

    private void BuildSourceCards()
    {
        SourceList.Children.Clear();

        foreach (var source in DiscoverService.Sources)
        {
            var card = BuildSourceCard(source);
            SourceList.Children.Add(card);
        }
    }

    private View BuildSourceCard(IBrowsableAdapter source)
    {
        // CF badge
        var cfBadge = new Border
        {
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding         = new Thickness(8, 3),
            IsVisible       = source.RequiresCfBypass,
        };
        cfBadge.SetDynamicResource(Border.BackgroundColorProperty, "AccentContainer");
        var cfLabel = new Label
        {
            Text           = "CF bypass",
            FontSize       = 10,
            FontAttributes = FontAttributes.Bold,
        };
        cfLabel.SetDynamicResource(Label.TextColorProperty, "AccentLight");
        cfBadge.Content = cfLabel;

        var titleLabel = new Label
        {
            Text           = source.SiteName,
            FontSize       = 16,
            FontAttributes = FontAttributes.Bold,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var subtitleLabel = new Label
        {
            Text     = "Chinese novels",
            FontSize = 12,
        };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var chevron = new Label
        {
            Text            = "\uE5CC",
            FontFamily      = "MaterialSymbols",
            FontSize        = 22,
            VerticalOptions = LayoutOptions.Center,
        };
        chevron.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        var textStack = new VerticalStackLayout
        {
            Spacing         = 4,
            VerticalOptions = LayoutOptions.Center,
            Children        = { titleLabel, subtitleLabel, cfBadge }
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Padding = new Thickness(18, 16),
        };
        row.Add(textStack, 0, 0);
        row.Add(chevron,   1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding         = new Thickness(0),
            Content         = row,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");

        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await card.ScaleToAsync(0.97, 80, Easing.CubicOut);
                await card.ScaleToAsync(1.0,  80, Easing.SpringOut);
                await Navigation.PushAsync(new SourceBrowsePage(source));
            })
        });

        return card;
    }
}
