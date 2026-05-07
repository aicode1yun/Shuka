using Shuka.Android.Services;

namespace Shuka.Android.Pages;

/// <summary>
/// A card showing a completed novel download with cover thumbnail,
/// title, author, chapter count, and action buttons.
/// </summary>
public class HistoryCard : ContentView
{
    public event Action<HistoryEntry>? OpenRequested;
    public event Action<HistoryEntry>? ShareRequested;
    public event Action<HistoryEntry>? DeleteRequested;

    private readonly HistoryEntry _entry;

    public HistoryCard(HistoryEntry entry)
    {
        _entry = entry;

        // ── Cover image ───────────────────────────────────────────────────────
        View coverView;
        if (!string.IsNullOrWhiteSpace(entry.CoverLocalPath) &&
            File.Exists(entry.CoverLocalPath))
        {
            var img = new Image
            {
                Source            = ImageSource.FromFile(entry.CoverLocalPath),
                Aspect            = Aspect.AspectFill,
                WidthRequest      = 72,
                HeightRequest     = 104,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                WidthRequest    = 72,
                HeightRequest   = 104,
                Content         = img,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }
        else
        {
            // Placeholder when no cover available
            var placeholder = new Label
            {
                Text              = "\uEA78", // auto_stories icon
                FontFamily        = "MaterialSymbols",
                FontSize          = 32,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            placeholder.SetDynamicResource(Label.TextColorProperty, "TextMuted");

            coverView = new Border
            {
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                WidthRequest    = 72,
                HeightRequest   = 104,
                Content         = placeholder,
            };
            ((Border)coverView).SetDynamicResource(Border.BackgroundColorProperty, "BgInput");
        }

        // ── Title ─────────────────────────────────────────────────────────────
        var titleLabel = new Label
        {
            Text          = entry.Title,
            FontSize      = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines      = 2,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        // ── Author ────────────────────────────────────────────────────────────
        var authorLabel = new Label
        {
            Text          = string.IsNullOrWhiteSpace(entry.Author) ? "" : entry.Author,
            FontSize      = 12,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines      = 1,
            IsVisible     = !string.IsNullOrWhiteSpace(entry.Author),
        };
        authorLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // ── Meta (chapters + date) ────────────────────────────────────────────
        var metaLabel = new Label
        {
            Text     = $"{entry.ChapterCount} chapters  ·  {entry.CompletedAt:MMM d, yyyy}",
            FontSize = 11,
        };
        metaLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        // ── File exists indicator ─────────────────────────────────────────────
        bool fileExists = !string.IsNullOrWhiteSpace(entry.EpubPath) &&
                          File.Exists(entry.EpubPath);
        var fileLabel = new Label
        {
            Text      = fileExists ? "\uE876  File available" : "\uE5CD  File missing",
            FontSize  = 11,
        };
        fileLabel.SetDynamicResource(Label.TextColorProperty,
            fileExists ? "Success" : "TextMuted");

        var textStack = new VerticalStackLayout
        {
            Spacing         = 4,
            VerticalOptions = LayoutOptions.Center,
            Children        = { titleLabel, authorLabel, metaLabel, fileLabel }
        };

        // ── Action buttons ────────────────────────────────────────────────────
        var openBtn  = MakeActionBtn("\uE2C7", "Open",   "Accent",    () => OpenRequested?.Invoke(_entry));
        var shareBtn = MakeActionBtn("\uE6B8", "Share",  "BgInput",   () => ShareRequested?.Invoke(_entry), outlined: true);
        var delBtn   = MakeActionBtn("\uE872", "Remove", "BgInput",   () => DeleteRequested?.Invoke(_entry), outlined: true, danger: true);

        var btnRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(8) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(8) },
                new ColumnDefinition { Width = GridLength.Star },
            },
            Margin = new Thickness(0, 8, 0, 0),
        };
        btnRow.Add(openBtn,  0, 0);
        btnRow.Add(shareBtn, 2, 0);
        btnRow.Add(delBtn,   4, 0);

        var rightStack = new VerticalStackLayout
        {
            Spacing         = 0,
            VerticalOptions = LayoutOptions.Fill,
            Children        = { textStack, btnRow }
        };

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 14,
            Padding       = new Thickness(16),
        };
        contentGrid.Add(coverView,   0, 0);
        contentGrid.Add(rightStack,  1, 0);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding         = new Thickness(0),
            Content         = contentGrid,
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "BgCard");
        card.SetDynamicResource(Border.StrokeProperty, "Stroke");

        Content = card;
    }

    private static Border MakeActionBtn(string icon, string label, string bgKey,
        Action onTap, bool outlined = false, bool danger = false)
    {
        var iconLabel = new Label
        {
            Text            = icon,
            FontFamily      = "MaterialSymbols",
            FontSize        = 14,
            VerticalOptions = LayoutOptions.Center,
            Margin          = new Thickness(0, 0, 4, 0),
        };

        var textLabel = new Label
        {
            Text            = label,
            FontSize        = 11,
            FontAttributes  = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };

        if (danger)
        {
            iconLabel.SetDynamicResource(Label.TextColorProperty, "Danger");
            textLabel.SetDynamicResource(Label.TextColorProperty, "Danger");
        }
        else if (outlined)
        {
            iconLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            textLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        }
        else
        {
            iconLabel.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");
            textLabel.SetDynamicResource(Label.TextColorProperty, "TextOnAccent");
        }

        var inner = new HorizontalStackLayout
        {
            Spacing           = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
            Children          = { iconLabel, textLabel },
        };

        var btn = new Border
        {
            StrokeThickness = outlined ? 1 : 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HeightRequest   = 34,
            Padding         = new Thickness(8, 0),
            Content         = inner,
        };
        btn.SetDynamicResource(Border.BackgroundColorProperty, bgKey);
        if (outlined) btn.SetDynamicResource(Border.StrokeProperty, "Stroke");

        btn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await btn.ScaleToAsync(0.93, 70, Easing.CubicOut);
                await btn.ScaleToAsync(1.0,  70, Easing.SpringOut);
                onTap();
            })
        });

        return btn;
    }
}
