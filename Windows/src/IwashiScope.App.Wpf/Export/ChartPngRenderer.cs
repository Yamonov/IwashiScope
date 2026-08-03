using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.App.Wpf.Rendering;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.Export;

public static class ChartPngRenderer
{
    public const int PixelWidth = 3000;
    public const int PixelHeight = 1500;
    public const double RasterScale = 3;
    public const double LogicalWidth = PixelWidth / RasterScale;
    public const double LogicalHeight = PixelHeight / RasterScale;
    public const double ContentInset = 20;

    private static readonly Rect ContentBounds = new(
        ContentInset,
        ContentInset,
        LogicalWidth - ContentInset * 2,
        LogicalHeight - ContentInset * 2);

    public static byte[] Spectrum(
        SpotMeasurement measurement,
        bool practicalRange,
        bool showD50,
        bool showD65,
        SpectrumYAxisConfiguration? yAxisConfiguration = null) =>
        Render(drawing =>
            ChartDrawing.DrawSpectrum(
                drawing,
                ContentBounds,
                measurement,
                practicalRange,
                showD50,
                showD65,
                yAxisConfiguration ?? SpectrumYAxisConfiguration.ForMeasurementMode(measurement.Mode),
                pixelsPerDip: 1));

    public static byte[] Cri(SpotMeasurement measurement) =>
        Render(drawing =>
            ChartDrawing.DrawCri(
                drawing,
                ContentBounds,
                measurement.Cri));

    public static byte[] Tm30(SpotMeasurement measurement) =>
        Render(drawing =>
        {
            var halfWidth = ContentBounds.Width / 2;
            ChartDrawing.DrawTm30Vector(
                drawing,
                new Rect(
                    ContentBounds.Left,
                    ContentBounds.Top,
                    halfWidth,
                    ContentBounds.Height),
                measurement.Tm30);
            ChartDrawing.DrawTm30Samples(
                drawing,
                new Rect(
                    ContentBounds.Left + halfWidth,
                    ContentBounds.Top,
                    halfWidth,
                    ContentBounds.Height),
                measurement.Tm30);
        });

    private static byte[] Render(Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(RasterScale, RasterScale));
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, LogicalWidth, LogicalHeight));
            draw(context);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            PixelWidth,
            PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        opaque.Freeze();

        var metadata = new BitmapMetadata("png");
        try
        {
            metadata.SetQuery("/sRGB/RenderingIntent", (byte)0);
        }
        catch (NotSupportedException)
        {
            // Bgr24 output is still opaque even if this encoder omits the optional sRGB chunk.
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaque, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
