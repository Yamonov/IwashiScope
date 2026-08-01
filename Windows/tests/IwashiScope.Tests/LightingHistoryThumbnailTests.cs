using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.App.Wpf.Controls;
using IwashiScope.App.Wpf.Rendering;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class LightingHistoryThumbnailTests
{
    [Fact]
    public void ThumbnailPalettesMatchTheMacWavelengthAndCriColors()
    {
        Assert.Equal(Rgb(0.42, 0.18, 0.85), ChartDrawing.LightingHistorySpectrumColor(380));
        Assert.Equal(Rgb(0.12, 0.82, 0.35), ChartDrawing.LightingHistorySpectrumColor(537.5));
        Assert.Equal(Rgb(0.55, 0.00, 0.04), ChartDrawing.LightingHistorySpectrumColor(730));

        var expectedCriColors = new[]
        {
            Rgb(0.72, 0.48, 0.46), Rgb(0.78, 0.64, 0.27), Rgb(0.56, 0.70, 0.27),
            Rgb(0.25, 0.66, 0.48), Rgb(0.20, 0.66, 0.72), Rgb(0.24, 0.50, 0.83),
            Rgb(0.47, 0.37, 0.78), Rgb(0.72, 0.35, 0.68), Rgb(0.88, 0.20, 0.20),
            Rgb(0.89, 0.68, 0.16), Rgb(0.22, 0.65, 0.30), Rgb(0.18, 0.38, 0.80),
            Rgb(0.86, 0.53, 0.43), Rgb(0.64, 0.61, 0.22), Rgb(0.79, 0.43, 0.32),
        };
        Assert.Equal(
            expectedCriColors,
            Enumerable.Range(1, 15)
                .Select(ChartDrawing.LightingHistoryCriColor)
                .ToArray());
    }

    [Theory]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void LightingThumbnailRendersWavelengthAndCriColorFamilies(
        MeasurementMode mode)
    {
        var pixels = Render(TestMeasurementFactory.Create(mode));

        var spectrumPixels = Region(pixels, 0, 44).ToArray();
        Assert.Contains(spectrumPixels, IsBlueDominant);
        Assert.Contains(spectrumPixels, IsGreenDominant);
        Assert.Contains(spectrumPixels, IsRedDominant);
        Assert.True(
            spectrumPixels.Count(IsGrayLinePixel) >= 5,
            "Expected the macOS-style gray spectrum line over the wavelength fill.");

        var criPixels = Region(pixels, 45, 89).ToArray();
        Assert.Contains(criPixels, IsBlueDominant);
        Assert.Contains(criPixels, IsGreenDominant);
        Assert.Contains(criPixels, IsRedDominant);
    }

    private static Pixel[,] Render(SpotMeasurement measurement)
    {
        Pixel[,]? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = RenderOnStaThread(measurement);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA thumbnail rendering failed.", failure);
        }
        return result ?? throw new InvalidOperationException("STA thumbnail rendering returned no pixels.");
    }

    private static Pixel[,] RenderOnStaThread(SpotMeasurement measurement)
    {
        const int width = 110;
        const int height = 89;
        var chart = new LightingHistoryChart
        {
            Measurement = measurement,
            Width = width,
            Height = height,
        };
        chart.Measure(new Size(width, height));
        chart.Arrange(new Rect(0, 0, width, height));
        chart.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var stride = width * 4;
        var bytes = new byte[stride * height];
        bitmap.CopyPixels(bytes, stride, 0);
        var result = new Pixel[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                result[y, x] = new Pixel(
                    bytes[offset + 2],
                    bytes[offset + 1],
                    bytes[offset],
                    bytes[offset + 3]);
            }
        }
        return result;
    }

    private static IEnumerable<Pixel> Region(Pixel[,] pixels, int top, int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = 0; x < pixels.GetLength(1); x++)
            {
                yield return pixels[y, x];
            }
        }
    }

    private static bool IsBlueDominant(Pixel pixel) =>
        pixel.Blue > pixel.Red + 25 && pixel.Blue > pixel.Green + 25;

    private static bool IsGreenDominant(Pixel pixel) =>
        pixel.Green > pixel.Red + 20 && pixel.Green > pixel.Blue + 10;

    private static bool IsRedDominant(Pixel pixel) =>
        pixel.Red > pixel.Green + 30 && pixel.Red > pixel.Blue + 30;

    private static bool IsGrayLinePixel(Pixel pixel) =>
        Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) -
        Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)) <= 4 &&
        pixel.Red is >= 105 and <= 165;

    private static Color Rgb(double red, double green, double blue) =>
        Color.FromRgb(
            (byte)Math.Round(red * 255),
            (byte)Math.Round(green * 255),
            (byte)Math.Round(blue * 255));

    private readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha);
}
