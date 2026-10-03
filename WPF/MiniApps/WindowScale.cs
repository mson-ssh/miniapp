namespace MiniApps;

// The window is laid out for 1000x720 and shown at a scale fitted to the screen. A Full HD work
// area (1920x1040 DIPs: 1080p at 100%, taskbar excluded) gets FullHdScale; other screens scale in
// proportion, within MinScale..MaxScale, and the window never takes more than ScreenFraction of the
// work area. The content is scaled with the window, so the layout stays the same on every screen.
public static class WindowScale
{
    public const double DesignWidth = 1000, DesignHeight = 720;
    public const double DesignMinWidth = 860, DesignMinHeight = 600;
    public const double FullHdWidth = 1920, FullHdHeight = 1040;
    public const double FullHdScale = 0.85;
    public const double MinScale = 0.75, MaxScale = 1.5;
    public const double ScreenFraction = 0.95;

    // Set once from the primary screen before the window is built; drop-down popups, which do not
    // inherit the window's transform, read it too.
    public static double Current { get; set; } = FullHdScale;

    public static double For(double workWidth, double workHeight)
    {
        var proportional = FullHdScale * Math.Min(workWidth / FullHdWidth, workHeight / FullHdHeight);
        var scale = Math.Max(MinScale, Math.Min(MaxScale, proportional));
        // A small screen keeps the whole window visible even below MinScale.
        var fits = Math.Min(workWidth * ScreenFraction / DesignWidth, workHeight * ScreenFraction / DesignHeight);
        return Math.Round(Math.Min(scale, fits), 3);
    }
}
