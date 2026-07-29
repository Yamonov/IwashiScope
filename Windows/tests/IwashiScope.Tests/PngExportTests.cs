using System.IO;
using System.Buffers.Binary;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.App.Wpf.Export;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class PngExportTests
{
    [Fact]
    public void SpectrumPngIs3000PixelsWideOpaqueAndDecodable()
    {
        var measurement = TestMeasurementFactory.Create(MeasurementMode.Ambient);
        var data = ChartPngRenderer.Spectrum(
            measurement,
            practicalRange: true,
            showD50: true,
            showD65: true);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], data[..4]);
        using var stream = new MemoryStream(data);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = Assert.Single(decoder.Frames);
        Assert.Equal(3000, frame.PixelWidth);
        Assert.Equal(1500, frame.PixelHeight);
        Assert.Equal(PixelFormats.Bgr24, frame.Format);
        Assert.Equal(24, frame.Format.BitsPerPixel);
        Assert.Contains("sRGB", ChunkTypes(data));
        Assert.Equal(3, ChartPngRenderer.RasterScale);
        Assert.Equal(1000, ChartPngRenderer.LogicalWidth);
        Assert.Equal(500, ChartPngRenderer.LogicalHeight);
    }

    [Fact]
    public void ReferenceCurvesDisappearWhenMeasurementDoesNotContain560Nm()
    {
        var original = TestMeasurementFactory.Create(MeasurementMode.Ambient);
        var truncated = original with
        {
            SpectrumStart = 600,
            SpectrumEnd = 700,
            Spectrum = original.Spectrum
                .Where(sample => sample.Wavelength is >= 600 and <= 700)
                .ToArray(),
        };
        var withoutReferences = ChartPngRenderer.Spectrum(
            truncated,
            practicalRange: false,
            showD50: false,
            showD65: false);
        var requestedReferences = ChartPngRenderer.Spectrum(
            truncated,
            practicalRange: false,
            showD50: true,
            showD65: true);
        Assert.Equal(withoutReferences, requestedReferences);
    }

    [Fact]
    public void SpectrumUsesOpaqueAreaSemiTransparentBackgroundAndGrayLine()
    {
        var original = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
        var measurement = original with
        {
            SpectrumStart = 380,
            SpectrumEnd = 730,
            PracticalSpectrumRange = new WavelengthRange(380, 730),
            DeclaredStepCount = 3,
            Spectrum =
            [
                new SpectralSample(0, 380, 0.5),
                new SpectralSample(1, 555, 0.5),
                new SpectralSample(2, 730, 0.5),
            ],
        };
        var data = ChartPngRenderer.Spectrum(
            measurement,
            practicalRange: false,
            showD50: false,
            showD65: false);

        using var stream = new MemoryStream(data);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = Assert.Single(decoder.Frames);
        var stride = frame.PixelWidth * 3;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        var upper = PixelAt(pixels, stride, x: 1644, y: 180);
        var lower = PixelAt(pixels, stride, x: 1644, y: 800);
        var upperDistanceFromWhite = 765 - upper.Red - upper.Green - upper.Blue;
        var lowerDistanceFromWhite = 765 - lower.Red - lower.Green - lower.Blue;

        Assert.InRange(upperDistanceFromWhite, 20, 180);
        Assert.True(
            lowerDistanceFromWhite > upperDistanceFromWhite + 150,
            $"Expected the opaque area below the line to be more saturated; " +
            $"upper={upper}, lower={lower}.");

        var grayLinePixel = Enumerable.Range(225, 10)
            .Select(y => PixelAt(pixels, stride, x: 1644, y))
            .FirstOrDefault(pixel =>
                Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) -
                Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)) <= 4 &&
                pixel.Red is >= 110 and <= 150);
        Assert.NotEqual(default, grayLinePixel);
    }

    [Fact]
    public void CriExportRasterScaleMakesAxisLabelsReadable()
    {
        var data = ChartPngRenderer.Cri(
            TestMeasurementFactory.Create(MeasurementMode.Ambient));
        using var stream = new MemoryStream(data);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = Assert.Single(decoder.Frames);
        var stride = frame.PixelWidth * 3;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        var labelInkRows = Enumerable.Range(1330, 60)
            .Count(y => Enumerable.Range(1510, 96)
                .Any(x =>
                {
                    var pixel = PixelAt(pixels, stride, x, y);
                    return pixel.Red < 180 &&
                           pixel.Green < 180 &&
                           pixel.Blue < 180;
                }));

        Assert.True(
            labelInkRows >= 18,
            $"Expected the 3×-rasterized R8 axis label to occupy at least " +
            $"18 pixel rows, but found {labelInkRows}.");
    }

    private static (byte Red, byte Green, byte Blue) PixelAt(
        byte[] pixels,
        int stride,
        int x,
        int y)
    {
        var offset = y * stride + x * 3;
        return (
            Red: pixels[offset + 2],
            Green: pixels[offset + 1],
            Blue: pixels[offset]);
    }

    private static IReadOnlyList<string> ChunkTypes(byte[] data)
    {
        var result = new List<string>();
        var offset = 8;
        while (offset + 12 <= data.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                data.AsSpan(offset, 4)));
            var type = Encoding.ASCII.GetString(data, offset + 4, 4);
            result.Add(type);
            offset += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }
        return result;
    }
}
