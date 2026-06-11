using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Curatio.Desktop;

public sealed class LoadingSpinner : Control
{
    private static readonly IBrush[] DotBrushes = CreateDotBrushes();
    private readonly DispatcherTimer _timer;
    private int _frame;

    public LoadingSpinner()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Render, (_, _) =>
        {
            _frame = (_frame + 1) % 12;
            InvalidateVisual();
        });

        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var orbit = Math.Max(2, Math.Min(Bounds.Width, Bounds.Height) * 0.34);
        var radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) * 0.07);

        for (var index = 0; index < 12; index++)
        {
            var angle = (index - _frame) * Math.PI / 6;
            var point = new Point(
                center.X + Math.Sin(angle) * orbit,
                center.Y - Math.Cos(angle) * orbit);
            context.DrawEllipse(DotBrushes[index], null, point, radius, radius);
        }
    }

    private static IBrush[] CreateDotBrushes()
    {
        var brushes = new IBrush[12];
        for (var index = 0; index < brushes.Length; index++)
        {
            var opacity = 0.16 + (11 - index) * 0.07;
            brushes[index] = new SolidColorBrush(Color.FromArgb(
                (byte)(255 * opacity), 24, 24, 27));
        }

        return brushes;
    }
}
