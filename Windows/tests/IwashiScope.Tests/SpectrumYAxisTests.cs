using System.Reflection;
using IwashiScope.App.Wpf.Export;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class SpectrumYAxisTests
{
    [Theory]
    [InlineData(MeasurementMode.Reflectance, SpectrumYAxisMode.Fixed, 100)]
    [InlineData(MeasurementMode.Ambient, SpectrumYAxisMode.Automatic, 200)]
    [InlineData(MeasurementMode.Emissive, SpectrumYAxisMode.Automatic, 200)]
    public void MeasurementModesHaveExpectedYAxisDefaults(
        MeasurementMode measurementMode,
        SpectrumYAxisMode expectedMode,
        double expectedFixedUpperBound)
    {
        var configuration = SpectrumYAxisConfiguration.ForMeasurementMode(measurementMode);

        Assert.Equal(expectedMode, configuration.Mode);
        Assert.Equal(expectedFixedUpperBound, configuration.FixedUpperBound);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(14, 10)]
    [InlineData(15, 20)]
    [InlineData(194, 190)]
    [InlineData(195, 200)]
    [InlineData(506, 500)]
    public void FixedUpperBoundIsClampedAndSnappedToTen(double input, double expected)
    {
        var normalized = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, input)
            .Normalize();

        Assert.Equal(expected, normalized.FixedUpperBound);
    }

    [Fact]
    public void NonFiniteFixedUpperBoundNormalizesToMinimum()
    {
        var normalized = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, double.NaN)
            .Normalize();

        Assert.Equal(SpectrumYAxisConfiguration.MinimumUpperBound, normalized.FixedUpperBound);
    }

    [Fact]
    public void AutomaticAndFixedUpperBoundsResolveIndependently()
    {
        var automatic = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Automatic, 200);
        var fixedAxis = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 100);

        Assert.Equal(60, automatic.ResolveUpperBound(50));
        Assert.Equal(1, automatic.ResolveUpperBound(0.5));
        Assert.Equal(100, fixedAxis.ResolveUpperBound(450));
    }

    [Fact]
    public void Fixed130UsesFiveExactIntegerIntervals()
    {
        var scale = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 130)
            .ResolveScale(450);

        Assert.Equal(130, scale.UpperBound);
        Assert.Equal([0, 26, 52, 78, 104, 130], scale.TickValues);
    }

    [Fact]
    public void AutomaticRequired54RoundsUpToSixIntegerIntervals()
    {
        var scale = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Automatic, 200)
            .ResolveScale(50);

        Assert.Equal(60, scale.UpperBound);
        Assert.Equal([0, 10, 20, 30, 40, 50, 60], scale.TickValues);
    }

    [Fact]
    public void AutomaticRequired123RoundsUpToFiveIntegerIntervals()
    {
        var scale = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Automatic, 200)
            .ResolveScale(123.0 / 1.08);

        Assert.Equal(125, scale.UpperBound);
        Assert.Equal([0, 25, 50, 75, 100, 125], scale.TickValues);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(7)]
    [InlineData(50)]
    [InlineData(113.88888888888889)]
    [InlineData(463)]
    public void AutomaticScaleAlwaysUsesIntegerTicks(double measuredMaximum)
    {
        var scale = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Automatic, 200)
            .ResolveScale(measuredMaximum);

        Assert.True(scale.UpperBound >= Math.Max(1, measuredMaximum * 1.08));
        Assert.All(scale.TickValues, tick => Assert.Equal(Math.Truncate(tick), tick));
        Assert.All(
            scale.TickValues.Zip(scale.TickValues.Skip(1)),
            pair => Assert.True(pair.First < pair.Second));
    }

    [Fact]
    public void ViewModelResetsYAxisConfigurationWhenMeasurementModeChanges()
    {
        var viewModel = new MainWindowViewModel();
        var modeProperty = typeof(MainWindowViewModel).GetProperty(
            nameof(MainWindowViewModel.Mode),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(modeProperty);

        Assert.Equal(
            SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Reflectance),
            viewModel.YAxisConfiguration);

        viewModel.IsSpectrumYAxisAutomatic = true;
        viewModel.SpectrumYAxisFixedUpperBound = 350;
        modeProperty.SetValue(viewModel, MeasurementMode.Ambient);
        Assert.Equal(
            SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Ambient),
            viewModel.YAxisConfiguration);

        viewModel.IsSpectrumYAxisFixed = true;
        viewModel.SpectrumYAxisFixedUpperBound = 470;
        modeProperty.SetValue(viewModel, MeasurementMode.Emissive);
        Assert.Equal(
            SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Emissive),
            viewModel.YAxisConfiguration);
    }

    [Fact]
    public void SpectrumPngChangesForAutomaticFixed100AndFixed200()
    {
        var measurement = TestMeasurementFactory.Create(MeasurementMode.Ambient);

        var automatic = RenderSpectrum(
            measurement,
            new SpectrumYAxisConfiguration(SpectrumYAxisMode.Automatic, 200));
        var fixed100 = RenderSpectrum(
            measurement,
            new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 100));
        var fixed200 = RenderSpectrum(
            measurement,
            new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 200));

        Assert.False(automatic.SequenceEqual(fixed100));
        Assert.False(automatic.SequenceEqual(fixed200));
        Assert.False(fixed100.SequenceEqual(fixed200));
    }

    [Fact]
    public async Task MeasurementExportPassesYAxisConfigurationToSpectrumPng()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"IwashiScope-y-axis-{Guid.NewGuid():N}");
        try
        {
            var entry = MeasurementHistoryEntry.Create(
                TestMeasurementFactory.Create(MeasurementMode.Ambient));
            var configuration = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 200);
            var paths = await new MeasurementExportService().ExportAsync(
                directory,
                [entry],
                SpectrumOnly(configuration));

            var path = Assert.Single(paths);
            Assert.Equal(RenderSpectrum(entry.Measurement, configuration), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DragExportPassesYAxisConfigurationToSpectrumPng()
    {
        var entry = MeasurementHistoryEntry.Create(
            TestMeasurementFactory.Create(MeasurementMode.Emissive));
        var configuration = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 100);
        using var cache = new DragExportCache();

        var paths = await cache.CreateAsync([entry], SpectrumOnly(configuration));

        var path = Assert.Single(paths);
        Assert.Equal(RenderSpectrum(entry.Measurement, configuration), await File.ReadAllBytesAsync(path));
    }

    private static MeasurementExportOptions SpectrumOnly(
        SpectrumYAxisConfiguration configuration) =>
        new()
        {
            SpectrumPng = true,
            CriPng = false,
            Tm30Png = false,
            Csv = false,
            Ase = false,
            UsePracticalSpectrumRange = false,
            SpectrumYAxisConfiguration = configuration,
        };

    private static byte[] RenderSpectrum(
        SpotMeasurement measurement,
        SpectrumYAxisConfiguration configuration) =>
        ChartPngRenderer.Spectrum(
            measurement,
            practicalRange: false,
            showD50: false,
            showD65: false,
            yAxisConfiguration: configuration);
}
