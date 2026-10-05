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
        card.SetFocused(false);
        var item = card.Item;
        card.TitleText.Text = item?.Name ?? "";
        card.FallbackTitle.Text = item?.Name ?? "";
        card.FallbackTitle.Visibility = Visibility.Visible;
        card.Artwork.Background = (Brush)Application.Current.Resources["PanelBrush"];
        if (Uri.TryCreate(item?.Poster, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
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
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(card, item?.Name ?? "Title");
    }

    public void SetDetails(MetaItem? details)
    {
        MetadataText.Text = details?.CardMetadata ?? "Details unavailable";
    }

    public void SetCardWidth(double width)
    {
        Width = width;
        PosterFrame.Width = width - 24;
        PosterFrame.Height = (width - 24) * 1.5;
        PosterRow.Height = new GridLength(PosterFrame.Height);
        Height = PosterFrame.Height + 122;
    }

    public void SetFocused(bool focused)
    {
        IsPosterFocused = focused;
        FocusOutline.Opacity = focused ? 1 : 0;
        FocusGlow.Opacity = focused ? 1 : 0;
        FocusDetails.Opacity = focused ? 1 : 0;
        var from = PosterScale.ScaleX;
        _animation?.Stop();
        var to = focused && PrototypeSettings.GetPosterAnimationsEnabled() ? 1.06 : 1;
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
