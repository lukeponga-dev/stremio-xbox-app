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
        var gap = width < 1000 ? 24 : 48;
        // The rail stays narrow; the separate content gap provides the safe
        // left inset seen in the TV layout without pushing the icons inward.
        var sidebarWidth = 80;
        var contentWidth = Math.Max(1, width - sidebarWidth - horizontalInset - gap);
        return new TvViewportLayout(horizontalInset, verticalInset, gap, sidebarWidth,
            contentWidth < 860, width >= 1000);
    }

    public static double GetShelfCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) return 160;
        // The compact row fits roughly seven posters at the 1294px reference
        // viewport while retaining a usable minimum on Xbox-sized layouts.
        return Math.Clamp(viewport / 7.5, 160, 200);
    }

    public static double GetContinueWatchingCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) return 194;
        return Math.Clamp(viewport / 5.7, 194, 280);
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
