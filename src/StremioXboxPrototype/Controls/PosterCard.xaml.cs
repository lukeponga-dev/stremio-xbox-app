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
    private bool _isFeatured;
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
        card.Artwork.Background = (Brush)Application.Current.Resources[
            item?.WatchProgress is not null ? "PosterFallbackBrush" : "PanelBrush"];
        if (Uri.TryCreate(item?.Poster, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            // Bound decoding so larger TV cards do not retain full source posters.
            var image = new BitmapImage(uri) { DecodePixelWidth = 540 };
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
        card.ResumingBadge.Visibility = item?.WatchProgress is null ? Visibility.Collapsed : Visibility.Visible;
        card.WatchProgressBar.Value = item?.WatchProgress?.Percent ?? 0;
        card.SetFocused(false);
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(card, item?.Name ?? "Title");
    }

    public void SetDetails(MetaItem? details)
    {
        MetadataText.Text = Item?.WatchProgress is { } progress
            ? $"{progress.Percent:0}% watched · {progress.RemainingText}"
            : details?.CardMetadata ?? "Details unavailable";
        TypeText.Text = Item?.WatchProgress is not null ? "Continue watching  →" :
            Item?.Type == "series" ? "Series · Open for streams" : "Movie · Open for streams";
    }

    public void SetCardWidth(double width, bool featured = false)
    {
        _isFeatured = featured;
        Width = width;
        PosterFrame.Width = width - 28;
        PosterFrame.Height = PosterFrame.Width * 1.42;
        TitleText.FontSize = featured ? 21 : 20;
        MetadataText.FontSize = featured ? 16 : 14;
        TypeText.FontSize = featured ? 15 : 13;
        UpdateCardHeight();
    }

    private void UpdateCardHeight()
    {
        // Reserve two title lines; focused titles can grow to reveal their full
        // name without clipping or moving into the following shelf.
        TitleText.Measure(new Windows.Foundation.Size(Math.Max(1, Width - 20), double.PositiveInfinity));
        Height = PosterFrame.Height + Math.Max(36, TitleText.DesiredSize.Height) + (_isFeatured ? 54 : 44);
    }

    public void SetFocused(bool focused)
    {
        // Keep a strong, non-animated focus cue available for TV accessibility
        // settings while animating the card's scale, glow and metadata together.
        IsPosterFocused = focused;
        var animationsEnabled = PrototypeSettings.GetPosterAnimationsEnabled();
        var targetScale = focused && animationsEnabled ? 1.10 : 1;
        var targetOpacity = focused ? 1 : 0;
        FocusDetails.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;
        FocusDetails.Opacity = focused && animationsEnabled ? 0 : targetOpacity;
        TitleText.MaxLines = focused ? 0 : 2;
        TitleText.TextTrimming = focused ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        Artwork.Opacity = focused ? 1 : 0.92;
        ProgressGlow.Opacity = focused && Item?.WatchProgress is not null ? 1 : 0;
        Canvas.SetZIndex(PosterFrame, focused ? 10 : 0);
        UpdateCardHeight();

        _animation?.Stop();
        if (!animationsEnabled)
        {
            PosterScale.ScaleX = PosterScale.ScaleY = targetScale;
            FocusAura.Opacity = FocusGlow.Opacity = FocusOutline.Opacity = targetOpacity;
            FocusDetails.Opacity = targetOpacity;
            return;
        }

        _animation = new Storyboard();
        AddAnimation(PosterScale, "ScaleX", PosterScale.ScaleX, targetScale);
        AddAnimation(PosterScale, "ScaleY", PosterScale.ScaleY, targetScale);
        AddAnimation(FocusAura, "Opacity", FocusAura.Opacity, targetOpacity);
        AddAnimation(FocusGlow, "Opacity", FocusGlow.Opacity, targetOpacity);
        AddAnimation(FocusOutline, "Opacity", FocusOutline.Opacity, targetOpacity);
        AddAnimation(Artwork, "Opacity", Artwork.Opacity, focused ? 1 : 0.92);
        AddAnimation(ProgressGlow, "Opacity", ProgressGlow.Opacity,
            focused && Item?.WatchProgress is not null ? 1 : 0);
        if (focused) AddAnimation(FocusDetails, "Opacity", 0, 1);
        else FocusDetails.Opacity = 0;
        _animation.Begin();
    }

    private void AddAnimation(DependencyObject target, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        _animation!.Children.Add(animation);
    }
}
