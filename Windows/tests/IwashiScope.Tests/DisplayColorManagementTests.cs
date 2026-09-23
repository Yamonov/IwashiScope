using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.App.Wpf.ColorManagement;
using IwashiScope.App.Wpf.Controls;
using IwashiScope.App.Wpf.Export;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class DisplayColorManagementTests
{
    [Fact]
    public void NativeLibraryDoesNotEmbedPersonalBuildPaths()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "IwashiScope.DisplayColor.dll"));
        foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode })
        {
            var text = encoding.GetString(bytes);
            Assert.DoesNotContain("C:\\Users\\", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/", text, StringComparison.Ordinal);
        }
    }
    private static void Close(double expected, double actual, double epsilon = .0003) =>
        Assert.True(double.IsFinite(actual) && Math.Abs(actual - expected) < epsilon, $"Expected {expected:R}; actual {actual:R}");
    private static double[] A(Vector3 v) => [v.First, v.Second, v.Third];

    [Theory]
    [InlineData(2.1)] [InlineData(4.3)]
    public void NativeWideGamutRoundTripDoesNotClipThroughSrgb(double version)
    {
        using var wide = new IccColorTransform(IccTestProfiles.Wide(version));
        using var srgb = new IccColorTransform(null);
        var source = new[] { .12, .87, .18 };
        var lab = wide.ToD50Lab(source); var value = new Vector3(lab[0], lab[1], lab[2]);
        Assert.True(LabColorConverter.Convert(value).Srgb.IsOutOfGamut);
        var direct = A(wide.FromLab(value));
        for (var i = 0; i < 3; i++) Close(source[i], direct[i]);
        var clippedSrgb = A(srgb.FromLab(value)).Select(v => Math.Clamp(v, 0, 1)).ToArray();
        var lossy = wide.FromSrgb(clippedSrgb);
        Assert.True(lossy.Zip(direct, (a, b) => Math.Abs(a - b)).Max() > .03);
    }
    [Theory]
    [InlineData(1.8)] [InlineData(2.2)] [InlineData(2.6)]
    public void TrcIsTakenFromProfileRatherThanAssumedSrgb(double gamma)
    {
        using var transform = new IccColorTransform(IccTestProfiles.Wide(gamma: gamma));
        var gray = transform.FromLab(new(50, 0, 0));
        var expected = Math.Pow(Math.Pow((50 + 16d) / 116, 3), 1 / gamma);
        foreach (var component in A(gray)) Close(expected, component);
    }
    [Fact]
    public void LutProfileWithoutMatrixTagsIsUsed()
    {
        using var transform = new IccColorTransform(IccTestProfiles.LabLut());
        var rgb = transform.FromLab(new(25, -64, 32));
        Close(.25, rgb.First); Close(64d / 255, rgb.Second); Close(160d / 255, rgb.Third);
    }
    [Fact]
    public void InvalidProfilesAndNonFiniteInputAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new IccColorTransform(new byte[128]));
        using var transform = new IccColorTransform(null);
        Assert.Throws<ArgumentException>(() => transform.FromLab(new(double.NaN, 0, 0)));
        Assert.Throws<ArgumentException>(() => transform.FromSrgb([0, 1]));
        transform.Dispose();
        Assert.Throws<ObjectDisposedException>(() => transform.FromLab(new(50, 0, 0)));
    }
    [Fact]
    public void D65WhiteIsAdaptedOnceAndUnknownWhiteIsNotSilentlyAssumed()
    {
        var lab = IccColorTransform.ToD50Lab(new(100, 0, 0), "D65");
        Close(100, lab.First, .002); Close(0, lab.Second, .002); Close(0, lab.Third, .002);
        Assert.Throws<ArgumentException>(() => IccColorTransform.ToD50Lab(new(50, 0, 0), "Illuminant A"));
    }
    [Fact]
    public void DeviceCodesSurviveWpfAndHistoryUpdatesWithoutChangingLab()
    {
        Sta(() =>
        {
            using var first = new IccColorTransform(IccTestProfiles.Wide(gamma: 1.8));
            using var second = new IccColorTransform(IccTestProfiles.Wide(gamma: 2.6));
            var measurement = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
            var original = measurement.Lab;
            var item = new HistoryItemViewModel(new MeasurementHistoryEntry(Guid.NewGuid(), "ICC test", measurement),
                1, false, [], true, (_, _) => { });
            item.SetDisplayColors(new(first, "test", "synthetic.icc", 0));
            var expected = first.FromLab(original!, measurement.LabWhitePoint);
            var border = new Border { Width = 32, Height = 32, Background = item.SwatchBrush };
            border.Measure(new(32, 32)); border.Arrange(new(0, 0, 32, 32));
            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(16, 16, 1, 1), pixel, 4, 0);
            Close(Math.Clamp(expected.First, 0, 1), pixel[2] / 255d, .006);
            Close(Math.Clamp(expected.Second, 0, 1), pixel[1] / 255d, .006);
            Close(Math.Clamp(expected.Third, 0, 1), pixel[0] / 255d, .006);
            var old = ((SolidColorBrush)item.SwatchBrush).Color;
            item.SetDisplayColors(new(second, "test", "changed.icc", 0));
            Assert.NotEqual(old, ((SolidColorBrush)item.SwatchBrush).Color);
            Assert.Equal(original, measurement.Lab);
        });
    }
    [Fact]
    public void ReferenceImagesRetainTransparencyAndDoNotAlterExports()
    {
        Sta(() =>
        {
            using var transform = new IccColorTransform(IccTestProfiles.Wide());
            var context = new DisplayColorContext(transform, "test", "test.icc", 0);
            var pixels = new float[] { .1f, .2f, .3f, .5f, 0, 0, 0, 0 };
            var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Prgba128Float, null, pixels, 32);
            source.Freeze(); var result = context.ReferenceImage(source); var output = new float[8];
            result.CopyPixels(output, 32, 0);
            Close(.5, output[3], 1e-7); Close(0, output[7], 1e-7);
            Assert.All(output, v => Assert.True(float.IsFinite(v)));
            Assert.Same(result, context.ReferenceImage(source));
            var measurement = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
            var before = ChartPngRenderer.Spectrum(measurement, true, false, false, SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Reflectance));
            var element = new SpectrumChart { Measurement = measurement };
            DisplayColorContext.SetContext(element, context);
            element.Measure(new(400, 220)); element.Arrange(new(0, 0, 400, 220));
            var preview = new RenderTargetBitmap(400, 220, 96, 96, PixelFormats.Pbgra32); preview.Render(element);
            var after = ChartPngRenderer.Spectrum(measurement, true, false, false, SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Reflectance));
            Assert.Equal(before, after);
        });
    }
    [Fact]
    public void CurrentDisplayQueryIsReadOnlyAndProducesExplicitStatus()
    {
        var context = DisplayProfileController.ReadCurrent(0);
        try
        {
            Assert.NotEmpty(context.Status(true));
            var evidence = Environment.GetEnvironmentVariable("IWASHISCOPE_COLOR_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "display-query.json"), JsonSerializer.Serialize(new
                { context.Monitor, context.ProfilePath, context.ProfileName, context.AdvancedMode, context.HasProfile, context.Error }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { context.Transform?.Dispose(); }
    }
    [Fact]
    public void ExistingChartsInheritAReplacementProfileWithoutChangingMeasurement()
    {
        Sta(() =>
        {
            using var a = new IccColorTransform(IccTestProfiles.Wide(gamma: 1.8));
            using var b = new IccColorTransform(IccTestProfiles.Wide(gamma: 2.6));
            var measurement = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
            var chart = new LabABChart { Measurement = measurement };
            var parent = new Border { Child = chart, Background = Brushes.White };
            parent.Measure(new(160, 160)); parent.Arrange(new(0, 0, 160, 160));
            byte[] Render(DisplayColorContext context)
            {
                DisplayColorContext.SetContext(parent, context);
                Assert.Same(context, DisplayColorContext.GetContext(chart));
                // Offscreen RenderTargetBitmap does not run a dispatcher render pass.
                // Flush the AffectsRender arrange invalidation as the live window does.
                parent.UpdateLayout();
                var bitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(parent);
                var pixels = new byte[160 * 160 * 4]; bitmap.CopyPixels(pixels, 640, 0); return pixels;
            }
            Assert.NotEqual(Render(new(a, "A", "A.icc", 0)), Render(new(b, "B", "B.icc", 0)));
            Assert.Same(measurement, chart.Measurement);
        });
    }
    [Fact]
    public void MeasuredAndAppearancePatchesShareTheDisplayTransformButNotCalculationState()
    {
        Sta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "IwashiScope-icc-" + Guid.NewGuid().ToString("N"));
            var model = new MainWindowViewModel(new IwashiScope.Infrastructure.Windows.Storage.SettingsStore(Path.Combine(root, "settings.json")),
                new IwashiScope.Infrastructure.Windows.Storage.MeasurementHistoryPersistenceStore(Path.Combine(root, "history.json")));
            using var transform = new IccColorTransform(IccTestProfiles.Wide());
            var measurement = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveMeasurement))!.SetValue(model, measurement);
            model.SelectedCieIlluminantOption = new(CieReferenceIlluminant.D50, "D50");
            var before = model.ReflectanceColorComparisonResult;
            var numericRgb = model.SrgbHex;
            var context = new DisplayColorContext(transform, "test", "test.icc", 0);
            model.SetDisplayColors(context);
            Assert.Same(before, model.ReflectanceColorComparisonResult);
            Assert.Equal(numericRgb, model.SrgbHex);
            Assert.Equal(((SolidColorBrush)context.LabBrush(measurement.Lab!, measurement.LabWhitePoint)).Color,
                ((SolidColorBrush)model.SwatchBrush).Color);
            Assert.Equal(((SolidColorBrush)context.LabBrush(before!.SimulatedLab)).Color,
                ((SolidColorBrush)model.SimulatedReflectancePatchBrush).Color);
            model.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }
    [Fact]
    public void ManagedDrawingEvidenceAndTiming()
    {
        Sta(() =>
        {
            using var transform = new IccColorTransform(IccTestProfiles.Wide());
            var context = new DisplayColorContext(transform, "synthetic wide-gamut test", "synthetic.icc", 0);
            var measurement = TestMeasurementFactory.Create(MeasurementMode.Reflectance);
            var chart = new SpectrumChart { Measurement = measurement };
            DisplayColorContext.SetContext(chart, context);
            chart.Measure(new(800, 360)); chart.Arrange(new(0, 0, 800, 360));
            var watch = Stopwatch.StartNew();
            var bitmap = new RenderTargetBitmap(800, 360, 96, 96, PixelFormats.Pbgra32);
            for (var i = 0; i < 5; i++) { chart.InvalidateVisual(); chart.UpdateLayout(); bitmap.Render(chart); }
            watch.Stop();
            var evidence = Environment.GetEnvironmentVariable("IWASHISCOPE_COLOR_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "managed-drawing-timing.json"), JsonSerializer.Serialize(new { MeanMilliseconds = watch.Elapsed.TotalMilliseconds / 5, Width = 800, Height = 360 }));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(evidence, "managed-reference-chart.png")); encoder.Save(file);
            }
        });
    }
    [Fact]
    public void ByteReferenceTransformPreservesAlphaAndAgreesWithFloatIcc()
    {
        using var transform = new IccColorTransform(IccTestProfiles.Wide());
        var bytes = new byte[] { 96, 64, 32, 128, 0, 0, 0, 0, 220, 180, 70, 255 };
        var result = transform.FromPremultipliedSrgb(bytes);
        for (var i = 0; i < 3; i++)
        {
            var offset = i * 4; var alpha = bytes[offset + 3]; Assert.Equal(alpha, result[offset + 3]);
            if (alpha == 0) continue;
            var reference = transform.FromSrgb([bytes[offset + 2] / (double)alpha, bytes[offset + 1] / (double)alpha, bytes[offset] / (double)alpha]);
            for (var c = 0; c < 3; c++) Close(Math.Clamp(reference[c], 0, 1), result[offset + 2 - c] / (double)alpha, .012);
        }
    }
    private static void Sta(Action action)
    {
        Exception? error = null; var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
