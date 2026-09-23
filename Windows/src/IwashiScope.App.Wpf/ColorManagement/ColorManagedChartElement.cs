using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IwashiScope.App.Wpf.ColorManagement;

/// <summary>Reference-chart colors are authored as sRGB, unlike direct-Lab patches.</summary>
public abstract class ColorManagedChartElement : FrameworkElement
{
    protected virtual bool ManagesReferenceColors => true;
    protected abstract void DrawContent(DrawingContext drawing);
    protected sealed override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var context = DisplayColorContext.GetContext(this);
        if (!ManagesReferenceColors || context?.Transform == null || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        { DrawContent(drawing); return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(RenderSize.Width * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(RenderSize.Height * dpi.DpiScaleY));
        // Bound allocation during unusual layout states; regular chart sizes are much smaller.
        if ((long)width * height > 16_000_000) { DrawContent(drawing); return; }
        var visual = new DrawingVisual();
        using (var raw = visual.RenderOpen()) DrawContent(raw);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        drawing.DrawImage(context.ReferenceImage(bitmap), new Rect(RenderSize));
    }
}
