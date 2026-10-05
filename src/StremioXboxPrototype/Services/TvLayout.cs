namespace StremioXboxPrototype.Services;

public static class TvLayout
{
    public static double GetShelfCardWidth(double viewport)
    {
        if (viewport <= 0 || !double.IsFinite(viewport)) return 240;
        // A fifth of the next card advertises scrolling at the start of a shelf.
        var wholeCards = Math.Max(1, (int)Math.Round(viewport / 240 - 0.2));
        return viewport / (wholeCards + 0.2);
    }
}
