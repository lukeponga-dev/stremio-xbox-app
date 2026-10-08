using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace StremioXboxPrototype.Controls;

public sealed partial class PosterCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MetaItem), typeof(PosterCard), new PropertyMetadata(null, ItemChanged));
    private Storyboard? _animation;
    public bool IsPosterFocused { get; private set; }

    public MetaItem? Item
    {
        get => (MetaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public PosterCard()
    {
        InitializeComponent();
        Unloaded += (_, _) => SetFocused(false);
    }

    private static void ItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (PosterCard)sender;
        var item = card.Item;
        card.TitleText.Text = item?.Name ?? "";
        card.TypeText.Text = item?.Type == "series" ? "Series · Open for streams" : "Movie · Open for streams";
        card.FallbackTitle.Text = item?.Name ?? "";
        card.FallbackTitle.Visibility = Visibility.Visible;
        card.Artwork.Background = (Brush)Application.Current.Resources["PanelBrush"];
        if (Uri.TryCreate(item?.Poster, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            // Bound decoding so larger TV cards do not retain full source posters.
            var image = new BitmapImage(uri) { DecodePixelWidth = 432 };
            var brush = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            image.ImageFailed += (_, _) =>
            {
                if (ReferenceEquals(card.Artwork.Background, brush)) card.FallbackTitle.Visibility = Visibility.Visible;
            };
            card.Artwork.Background = brush;
            card.FallbackTitle.Visibility = Visibility.Collapsed;
        }
        card.SetDetails(item);
        card.WatchProgressBar.Visibility = item?.WatchProgress is null ? Visibility.Collapsed : Visibility.Visible;
        card.WatchProgressBar.Value = item?.WatchProgress?.Percent ?? 0;
        card.SetFocused(false);
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(card, item?.Name ?? "Title");
    }

    public void SetDetails(MetaItem? details)
    {
        MetadataText.Text = Item?.WatchProgress?.RemainingText ?? details?.CardMetadata ?? "Details unavailable";
        TypeText.Text = Item?.WatchProgress is not null ? "Continue watching" :
            Item?.Type == "series" ? "Series · Open for streams" : "Movie · Open for streams";
    }

    public void SetCardWidth(double width)
    {
        Width = width;
        PosterFrame.Width = width - 24;
        // Posters use a consistent 2:3 ratio at every responsive shelf width.
        PosterFrame.Height = (width - 24) * 1.5;
        PosterRow.Height = new GridLength(PosterFrame.Height);
        UpdateCardHeight();
    }

    private void UpdateCardHeight()
    {
        // Reserve two title lines; focused titles can grow to reveal their full
        // name without clipping or moving into the following shelf.
        TitleText.Measure(new Windows.Foundation.Size(Math.Max(1, Width - 24), double.PositiveInfinity));
        Height = PosterFrame.Height + Math.Max(62, TitleText.DesiredSize.Height) + 82;
    }

    public void SetFocused(bool focused)
    {
        // Border and metadata appear even when motion is disabled, so the remote
        // focus position is never communicated by animation alone.
        IsPosterFocused = focused;
        FocusOutline.Opacity = focused ? 1 : 0;
        FocusGlow.Opacity = focused ? 1 : 0;
        FocusDetails.Visibility = focused || Item?.WatchProgress is not null ? Visibility.Visible : Visibility.Collapsed;
        TitleText.MaxLines = focused ? 0 : 2;
        TitleText.TextTrimming = focused ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        UpdateCardHeight();
        var from = PosterScale.ScaleX;
        _animation?.Stop();
        // Keep the zoom inside the card gutter; the purple outline supplies the
        // main focus cue and remains visible when animation is disabled.
        var to = focused && PrototypeSettings.GetPosterAnimationsEnabled() ? 1.04 : 1;
        if (!PrototypeSettings.GetPosterAnimationsEnabled())
        {
            PosterScale.ScaleX = PosterScale.ScaleY = to;
            return;
        }
        _animation = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, PosterScale);
            Storyboard.SetTargetProperty(animation, property);
            _animation.Children.Add(animation);
        }
        _animation.Begin();
    }
}
