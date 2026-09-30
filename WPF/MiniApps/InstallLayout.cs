using System.Windows;

namespace MiniApps;

// Compact Install Software cards, shared by both targets.
public static class InstallLayout
{
    public static Visibility DescriptionVisibility => Visibility.Collapsed;
    public static Thickness ReadyPadding => new(9, 6, 9, 6);
    public static Thickness ProgressPadding => new(9, 7, 9, 7);
    public static Thickness StatusMargin => new(0, 2, 0, 3);
    public static Thickness NameMargin => new(0, 0, 0, 4);
    public static Thickness ProgressStatusMargin => new(0, 0, 0, 4);
    public static double ProgressMinHeight => 48;
    public static double InstallButtonSize => 28;
}
