namespace StremioXboxPrototype.Services;

public static class TvLayout
{
    public static TvViewportLayout GetViewportLayout(double width, double height)
    {
        // XAML supplies effective pixels after display scaling. Never assume that
        // a 1080p/4K TV gives the application that many layout pixels.
        if (!double.IsFinite(width) || width <= 0) width = 960;
        if (!double.IsFinite(height) || height <= 0) height = 540;
        // Keep safe spacing proportional even on large viewports. Backgrounds
        // remain full bleed; this inset protects controls from TV overscan.
        var horizontalInset = Math.Max(width * 0.05, 16);
        var verticalInset = Math.Max(height * 0.05, 12);
        var gap = width < 1000 ? 16 : 24;
        var sidebarWidth = 148 + 8 + horizontalInset;
        var contentWidth = Math.Max(1, width - sidebarWidth - horizontalInset - gap);
        return new TvViewportLayout(horizontalInset, verticalInset, gap, sidebarWidth,
            contentWidth < 760, width >= 1000);
    }

    public static double GetShelfCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) return 240;
        // Prioritize legible posters and titles at sofa distance. A partial next
        // card makes horizontal scrolling apparent without shrinking the shelf.
        var wholeCards = Math.Max(1, (int)Math.Floor(viewport / 280));
        return viewport / (wholeCards + 0.25);
    }
}

public readonly record struct TvViewportLayout(double HorizontalInset, double VerticalInset,
    double ContentGap, double SidebarWidth, bool StackHeader, bool ShowDetailsPoster);
