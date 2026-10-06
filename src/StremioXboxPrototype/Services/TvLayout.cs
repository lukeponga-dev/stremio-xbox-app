namespace StremioXboxPrototype.Services;

public static class TvLayout
{
    public static double GetShelfCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) return 240;
        // Fit smaller TV cards so both shelf titles can appear at 1080p, while
        // a quarter of the next card makes horizontal scrolling obvious.
        var wholeCards = Math.Max(1, (int)Math.Round(viewport / 200));
        return viewport / (wholeCards + 0.25);
    }
}
