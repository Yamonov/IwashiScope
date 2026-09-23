using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.App.Wpf.Controls;
using IwashiScope.App.Wpf.Export;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class Cie2006ParityTests
{
    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mac-build66-golden.json")));
    private static double[] Values(JsonElement element) => element.EnumerateArray().Select(v => v.GetDouble()).ToArray();
    private static Vector3 Vector(JsonElement element) { var v = Values(element); return new(v[0], v[1], v[2]); }
    private static void Close(double expected, double actual, double tolerance = 1e-9) => Assert.True(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}; actual {actual:R}; delta {actual - expected:R}");
    private static void Close(JsonElement expected, Vector3 value) { var v = Values(expected); Close(v[0], value.First); Close(v[1], value.Second); Close(v[2], value.Third); }
    private static void Close(JsonElement expected, PhysiologicalPoint value) { var v = Values(expected); Close(v[0], value.X); Close(v[1], value.Y); }
    private static SpotMeasurement Measurement(JsonElement fixture)
    {
        var spectrum = fixture.GetProperty("spectrum").EnumerateArray().Select((s, i) => new SpectralSample(i, s[0].GetDouble(), s[1].GetDouble())).ToArray();
        return new() { Mode = MeasurementMode.Reflectance, SpectrumStart = spectrum[0].Wavelength, SpectrumEnd = spectrum[^1].Wavelength,
            DeclaredStepCount = spectrum.Length, Spectrum = spectrum, Lab = new(50, 0, 0), LabWhitePoint = "D50", PracticalSpectrumRange = new(420, 700) };
    }

    [Fact]
    public void AllApprovedMacSpectraSeamsAndBandsMatch()
    {
        using var document = Golden(); var root = document.RootElement;
        Assert.Equal(5, root.GetProperty("fixtures").GetArrayLength());
        Assert.Equal(root.GetProperty("paletteTriangleCount").GetInt32(), ChromaticityBackgroundPalette.Shared.Triangles.Count);
        Close(root.GetProperty("paletteWhiteLMS"), ChromaticityBrightnessReference.PaletteWhiteLms);
        foreach (var fixture in root.GetProperty("fixtures").EnumerateArray())
        {
            var measurement = Measurement(fixture); var original = measurement.Spectrum.ToArray();
            var result = Cie2006Chromaticity.Measure(measurement, out var error);
            Assert.Null(error); Assert.NotNull(result);
            Close(fixture.GetProperty("lms"), result.Lms); Close(fixture.GetProperty("whiteLMS"), result.WhiteLms);
            Close(fixture.GetProperty("xyzF"), result.XyzF); Close(fixture.GetProperty("xyF"), result.Point);
            Close(fixture.GetProperty("range")[0].GetDouble(), result.Range.Start); Close(fixture.GetProperty("range")[1].GetDouble(), result.Range.End);
            CheckBands(fixture, result.Lms, result.WhiteLms);
            Assert.Null(ChromaticityConfusionBand.Make(result.Lms, ConfusionColorMode.C));
            Assert.Equal(original, measurement.Spectrum);
            Assert.Equal(result, Cie2006Chromaticity.Measure(measurement with { PracticalSpectrumRange = new(500, 600) }, out _));
        }
        foreach (var fixture in root.GetProperty("seamFixtures").EnumerateArray())
            CheckBands(fixture, Vector(fixture.GetProperty("lms")), Vector(fixture.GetProperty("whiteLMS")));
    }

    private static void CheckBands(JsonElement fixture, Vector3 lms, Vector3 white)
    {
        foreach (var expected in fixture.GetProperty("bands").EnumerateArray())
        {
            var mode = Enum.Parse<ConfusionColorMode>(expected.GetProperty("mode").GetString()!);
            var band = ChromaticityConfusionBand.Make(lms, mode); Assert.NotNull(band);
            var reference = ChromaticityBrightnessReference.Create(lms, white, mode); Assert.NotNull(reference);
            Close(expected.GetProperty("segment").GetProperty("start"), band.Segment.Start);
            Close(expected.GetProperty("segment").GetProperty("end"), band.Segment.End);
            Close(expected.GetProperty("requestedQ").GetDouble(), reference.RequestedLuminance);
            Close(expected.GetProperty("displayQ").GetDouble(), reference.RelativeLuminance);
            foreach (var profile in expected.GetProperty("profiles").EnumerateArray())
            {
                var space = profile.GetProperty("profile").GetString() == "sRGB" ? ChromaticityRenderingSpace.Srgb : ChromaticityRenderingSpace.DisplayP3;
                var transformTolerance = space == ChromaticityRenderingSpace.Srgb ? 1e-9 : 2e-5;
                var mapper = new ChromaticityBandDisplayMapper(reference, space, MacColorTransformReference.For(profile.GetProperty("profile").GetString()!));
                // Mac ColorSync uses native floating transforms; Windows uses an unbounded double matrix.
                CloseDisplay(profile.GetProperty("grayLinearRGB"), mapper.GrayLinearRgb, transformTolerance);
                var sections = profile.GetProperty("sections");
                Assert.Equal(sections.GetArrayLength(), band.Sections.Count);
                for (var i = 0; i < band.Sections.Count; i++)
                {
                    var section = band.Sections[i]; var expectedSection = sections[i];
                    Close(expectedSection.GetProperty("range")[0].GetDouble(), section.Start);
                    Close(expectedSection.GetProperty("range")[1].GetDouble(), section.End);
                    foreach (var sample in expectedSection.GetProperty("samples").EnumerateArray())
                    {
                        var stop = section.SampleAt(sample.GetProperty("location").GetDouble()); Assert.NotNull(stop);
                        Close(sample.GetProperty("xyF"), stop.Point); Close(sample.GetProperty("rawLMS"), stop.Lms);
                        Close(sample.GetProperty("rawLinearSRGB"), stop.LinearRgb);
                        var mapped = mapper.Sample(stop); Assert.NotNull(mapped);
                        CloseDisplay(sample.GetProperty("displayLinearRGB"), mapped.LinearRgb, transformTolerance);
                        CloseDisplay(sample.GetProperty("displayModelLMS"), mapped.ModelLms, space == ChromaticityRenderingSpace.Srgb ? 1e-9 : 0.002);
                        Close(sample.GetProperty("chromaScale").GetDouble(), mapped.ChromaScale, transformTolerance);
                        Close(reference.RelativeLuminance, ChromaticityBrightnessReference.Response(mapped.ModelLms, ChromaticityBrightnessReference.PaletteWhiteLms, mode)!.Value);
                    }
                    var pixels = mapper.Raster(section); Assert.NotNull(pixels); Assert.Equal(4096 * 4, pixels.Length);
                    Assert.All(pixels, v => Assert.InRange(v, 0, 1));
                    for (var p = 3; p < pixels.Length; p += 4) Assert.Equal(1f, pixels[p]);
                }
            }
        }
    }
    private static void CloseDisplay(JsonElement expected, Vector3 actual, double tolerance)
    {
        var values = Values(expected); Close(values[0], actual.First, tolerance); Close(values[1], actual.Second, tolerance); Close(values[2], actual.Third, tolerance);
    }

    [Fact]
    public void All441DisplayPointsAndPolesMatchMac()
    {
        using var document = Golden(); var expected = document.RootElement.GetProperty("displayLocus");
        Assert.Equal(441, ChromaticityDisplayLocus.Samples.Count);
        for (var i = 0; i < 441; i++)
        {
            var actual = ChromaticityDisplayLocus.Samples[i]; var reference = expected[i];
            Close(reference.GetProperty("wavelength").GetDouble(), actual.Wavelength);
            Close(reference.GetProperty("lms"), actual.Lms); Close(reference.GetProperty("xyzF"), actual.XyzF);
            Close(reference.GetProperty("xyz1931"), actual.Xyz1931); Close(reference.GetProperty("xyF"), actual.Point);
            if (actual.Wavelength > 615) Assert.Equal(0, actual.Lms.Third);
            if (i % 5 == 0) Assert.Equal(Cie2006Chromaticity.SpectralLms(actual.Wavelength), actual.Lms);
        }
        foreach (var (name, cone) in new[] { ("L", PhysiologicalCone.Long), ("M", PhysiologicalCone.Medium), ("S", PhysiologicalCone.Short) })
            Close(document.RootElement.GetProperty("copunctalPoints").GetProperty(name), Cie2006Chromaticity.CopunctalPoint(cone));
    }

    [Fact]
    public void ReferencesNormalizeBeforeCropAndNeverInventShortTail()
    {
        var full = Cie2006LmsReference.Curves(360, 900);
        Assert.Equal(new[] { 89, 89, 46 }, full.Select(c => c.Samples.Count));
        Assert.All(full, c => Close(1, c.Samples.Max(s => s.Value)));
        var cropped = Cie2006LmsReference.Curves(421, 702);
        foreach (var curve in cropped)
        {
            Assert.Equal(421, curve.Samples[0].Wavelength);
            Assert.Equal(curve.Cone == PhysiologicalCone.Short ? 615 : 702, curve.Samples[^1].Wavelength);
            foreach (var sample in curve.Samples.Where(s => s.Wavelength % 5 == 0))
                Close(full.Single(c => c.Cone == curve.Cone).Samples.Single(s => s.Wavelength == sample.Wavelength).Value, sample.Value);
        }
        Assert.Empty(Cie2006LmsReference.Curves(double.NaN, 700));
        Assert.DoesNotContain(Cie2006LmsReference.Curves(616, 800), c => c.Cone == PhysiologicalCone.Short);
    }

    [Fact]
    public void InvalidEmptyDuplicateAndZeroSignalsHaveNoPoint()
    {
        using var document = Golden(); var m = Measurement(document.RootElement.GetProperty("fixtures")[0]);
        Assert.Null(Cie2006Chromaticity.Measure(m with { Spectrum = [] }, out var empty)); Assert.Equal(ChromaticityError.InsufficientSpectrum, empty);
        Assert.Null(Cie2006Chromaticity.Measure(m with { Spectrum = [new(0, 400, 1), new(1, 400, 2)] }, out var duplicate)); Assert.Equal(ChromaticityError.InvalidSpectrum, duplicate);
        Assert.Null(Cie2006Chromaticity.Measure(m with { Spectrum = m.Spectrum.Select(s => s with { Value = 0 }).ToArray() }, out var zero)); Assert.Equal(ChromaticityError.ZeroSignal, zero);
        Assert.Null(Cie2006Chromaticity.Measure(m with { Spectrum = [new(0, 400, double.NaN), new(1, 500, 2)] }, out var invalid)); Assert.Equal(ChromaticityError.InvalidSpectrum, invalid);
        Assert.Null(Cie2006Chromaticity.Measure(m with { Mode = MeasurementMode.Ambient }, out _));
        Assert.Null(ChromaticityConfusionBand.Make(null, ConfusionColorMode.P));
        Assert.Null(ChromaticityConfusionBand.Make(new(double.NaN, 1, 1), ConfusionColorMode.P));
    }

    [Fact]
    public void PchipPreservesKnotsMonotonicityAndHasNoExtrapolation()
    {
        var interpolator = new DisplaySpectralInterpolator(new[] { 0d, 0.1, 1, 0.8, 0.8 }, 390, 5);
        for (var i = 0; i <= 20; i++) Assert.InRange(interpolator.Value(390 + i)!.Value, 0, 1);
        Assert.Equal(0.1, interpolator.Value(395)); Assert.Equal(1, interpolator.Value(400));
        Assert.Null(interpolator.Value(389)); Assert.Null(interpolator.Value(411));
    }

    [Fact]
    public void NormalExportIncludesLmsWhileDragAndReflectanceMainRemainUnchanged()
    {
        var lighting = TestMeasurementFactory.Create(MeasurementMode.Ambient);
        var plain = ChartPngRenderer.Spectrum(lighting, false, false, false);
        Assert.NotEqual(plain, ChartPngRenderer.Spectrum(lighting, false, false, false, showLms: true));
        var reflectance = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
        Assert.Equal(ChartPngRenderer.Spectrum(reflectance, false, false, false), ChartPngRenderer.Spectrum(reflectance, false, false, false, showLms: true));
        foreach (var mode in Enum.GetValues<MeasurementMode>())
        {
            var options = MeasurementExportOptions.ForDrag(mode, true, SpectrumYAxisConfiguration.ForMeasurementMode(mode));
            Assert.False(options.ShowD50); Assert.False(options.ShowD65); Assert.False(options.ShowLms);
        }
    }

    [Fact]
    public void BackgroundIsTransparentOutsideAndGeometryHasEqualScale()
    {
        var pixels = ChromaticityBackgroundPalette.Shared.Rgba(32, 36)!;
        Assert.Equal(0, pixels[3]); Assert.Contains((byte)255, pixels);
        var plot = ChromaticityDrawing.Plot(new Rect(0, 0, 300, 340)); Close(plot.Width / 0.8, plot.Height / 0.9);
        Assert.True(plot.Left >= 11); Assert.Equal(20, ChromaticityConfusionBand.LineWidth);
    }

    [Fact]
    public void RenderWindowsEvidenceUsingOnlyIsolatedFixtures()
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("IWASHISCOPE_PARITY_EVIDENCE");
        if (string.IsNullOrEmpty(evidenceRoot)) return;
        Directory.CreateDirectory(evidenceRoot);
        using var document = Golden(); var fixture = document.RootElement.GetProperty("fixtures")[2]; var measurement = Measurement(fixture);
        var result = Cie2006Chromaticity.Measure(measurement, out _);
        foreach (var mode in Enum.GetValues<ConfusionColorMode>())
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 720));
                drawing.PushTransform(new ScaleTransform(2, 2));
                ChromaticityDrawing.Draw(drawing, new Rect(0, 0, 320, 360), measurement, result, mode); drawing.Pop();
            }
            var bitmap = new RenderTargetBitmap(640, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var pixels = new byte[640 * 720 * 4]; bitmap.CopyPixels(pixels, 640 * 4, 0);
            Assert.True(Enumerable.Range(0, 640 * 720).Any(i => pixels[i * 4 + 3] == 255 && pixels[i * 4] != pixels[i * 4 + 2]), "Expected a visible colored diagram, not a blank offscreen surface.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(evidenceRoot, $"windows-chromaticity-{mode}.png")); encoder.Save(output);
        }
        File.WriteAllBytes(Path.Combine(evidenceRoot, "windows-spectrum-lms.png"), ChartPngRenderer.Spectrum(TestMeasurementFactory.Create(MeasurementMode.Ambient), false, true, true, showLms: true));
    }

    [Fact]
    public void ChromaticityControlsDefaultToCAndDisableForInvalidMeasurement()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var document = Golden(); var measurement = Measurement(document.RootElement.GetProperty("fixtures")[2]);
                var view = new ReflectanceChromaticityView { Measurement = measurement, Width = 380, Background = Brushes.White };
                var body = Assert.IsType<StackPanel>(view.Content);
                var radios = Assert.IsType<WrapPanel>(body.Children[0]).Children.OfType<RadioButton>().ToArray();
                Assert.Equal(4, radios.Length); Assert.True(radios[0].IsChecked); Assert.All(radios, b => Assert.True(b.IsEnabled));
                var evidenceRoot = Environment.GetEnvironmentVariable("IWASHISCOPE_PARITY_EVIDENCE");
                if (!string.IsNullOrEmpty(evidenceRoot))
                {
                    view.Measure(new Size(380, double.PositiveInfinity)); view.Arrange(new Rect(new Point(), view.DesiredSize)); view.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(760, (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(evidenceRoot, "windows-chromaticity-controls.png")); encoder.Save(output);
                }
                radios[1].IsChecked = true;
                view.Measurement = measurement with { Lab = new(50, double.NaN, 0) };
                Assert.All(radios, b => Assert.True(b.IsEnabled)); // The raw LMS model does not depend on Lab.
                view.Measurement = measurement with { Lab = null };
                Assert.All(radios, b => Assert.True(b.IsEnabled));
                view.Measurement = measurement with { Spectrum = [] };
                Assert.True(radios[0].IsChecked); Assert.All(radios, b => Assert.False(b.IsEnabled));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("Isolated WPF control validation failed.", failure);
    }
}
