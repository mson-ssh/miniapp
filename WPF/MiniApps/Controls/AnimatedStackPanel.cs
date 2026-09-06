using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MiniApps.Controls;

public sealed class AnimatedStackPanel : Panel
{
    private readonly Dictionary<UIElement, Rect> previousBounds = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var currentChildren = InternalChildren.Cast<UIElement>().ToHashSet();
        foreach (var removed in previousBounds.Keys.Where(child => !currentChildren.Contains(child)).ToArray()) previousBounds.Remove(removed);

        var y = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var rect = new Rect(0, y, finalSize.Width, child.DesiredSize.Height);
            child.Arrange(rect);
            if (previousBounds.TryGetValue(child, out var previous) && Math.Abs(previous.Y - rect.Y) > 0.5)
            {
                var transform = child.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    child.RenderTransform = transform;
                }
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(previous.Y - rect.Y, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }, HandoffBehavior.SnapshotAndReplace);
            }
            previousBounds[child] = rect;
            y += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
