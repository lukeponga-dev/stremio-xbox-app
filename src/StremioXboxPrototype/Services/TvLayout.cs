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
        var horizontalInset = Math.Clamp(width * 0.035, 18, 56);
        var verticalInset = Math.Clamp(height * 0.045, 12, 38);
        var gap = width < 900 ? 16 : width < 1400 ? 24 : 32;
        // The application uses a full-width content canvas; preserve a safe
        // overscan margin while allowing the header to reflow before it clips.
        const double sidebarWidth = 0;
        var contentWidth = Math.Max(1, width - horizontalInset * 2 - gap);
        return new TvViewportLayout(horizontalInset, verticalInset, gap, sidebarWidth,
            contentWidth < 1180, width >= 1180);
    }

    public static int GetHomeGridColumnCount(double viewport)
    {
        if (!double.IsFinite(viewport) || viewport <= 0) viewport = 960;
        return viewport >= 1100 ? 4 : 3;
    }

    public static double GetHomeGridCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) viewport = 960;
        var columns = GetHomeGridColumnCount(viewport);
        const double gap = 28;
        return Math.Clamp((viewport - gap * (columns - 1)) / columns, 180, 360);
    }

    public static AddonGridLayout GetAddonGridLayout(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) availableWidth = 360;
        var gridWidth = Math.Min(availableWidth, 690);
        var columns = Math.Max(1, Math.Min(3, (int)Math.Floor(gridWidth / 180)));
        return new AddonGridLayout(gridWidth, columns, Math.Min(220, Math.Max(1, gridWidth / columns - 4)),
            availableWidth < 430, availableWidth < 540);
    }
}

public readonly record struct TvViewportLayout(double HorizontalInset, double VerticalInset,
    double ContentGap, double SidebarWidth, bool StackHeader, bool ShowDetailsPoster);

public readonly record struct AddonGridLayout(double GridWidth, int Columns, double TileWidth,
    bool StackTopActions, bool StackImportActions);
