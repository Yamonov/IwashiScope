using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IwashiScope.App.Wpf.Rendering;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.Controls;

public sealed class SpectrumChart : FrameworkElement
{
    public SpectrumChart()
    {
        ClipToBounds = true;
    }

    public static readonly DependencyProperty MeasurementProperty =
        DependencyProperty.Register(
            nameof(Measurement),
            typeof(SpotMeasurement),
            typeof(SpectrumChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsePracticalRangeProperty =
        DependencyProperty.Register(
            nameof(UsePracticalRange),
            typeof(bool),
            typeof(SpectrumChart),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowD50Property =
        DependencyProperty.Register(
            nameof(ShowD50),
            typeof(bool),
            typeof(SpectrumChart),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowD65Property =
        DependencyProperty.Register(
            nameof(ShowD65),
            typeof(bool),
            typeof(SpectrumChart),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisConfigurationProperty =
        DependencyProperty.Register(
            nameof(YAxisConfiguration),
            typeof(SpectrumYAxisConfiguration),
            typeof(SpectrumChart),
            new FrameworkPropertyMetadata(
                SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Reflectance),
                FrameworkPropertyMetadataOptions.AffectsRender));

    private SpectralSample? _hover;

    public SpotMeasurement? Measurement
    {
        get => (SpotMeasurement?)GetValue(MeasurementProperty);
        set => SetValue(MeasurementProperty, value);
    }

    public bool UsePracticalRange
    {
        get => (bool)GetValue(UsePracticalRangeProperty);
        set => SetValue(UsePracticalRangeProperty, value);
    }

    public bool ShowD50
    {
        get => (bool)GetValue(ShowD50Property);
        set => SetValue(ShowD50Property, value);
    }

    public bool ShowD65
    {
        get => (bool)GetValue(ShowD65Property);
        set => SetValue(ShowD65Property, value);
    }

    public SpectrumYAxisConfiguration YAxisConfiguration
    {
        get => (SpectrumYAxisConfiguration)GetValue(YAxisConfigurationProperty);
        set => SetValue(YAxisConfigurationProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(RenderSize);
        if (Measurement is null)
        {
            drawingContext.DrawRectangle(Brushes.White, null, bounds);
            return;
        }
        ChartDrawing.DrawSpectrum(
            drawingContext,
            bounds,
            Measurement,
            UsePracticalRange,
            ShowD50,
            ShowD65,
            YAxisConfiguration,
            _hover,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Measurement is null)
        {
            return;
        }
        var samples = Measurement.DisplaySpectrum(UsePracticalRange);
        if (samples.Count == 0)
        {
            return;
        }
        var plot = ChartDrawing.PlotRect(new Rect(RenderSize));
        var x = Math.Clamp(e.GetPosition(this).X, plot.Left, plot.Right);
        var fraction = (x - plot.Left) / Math.Max(plot.Width, 1);
        var minimum = samples.Min(sample => sample.Wavelength);
        var maximum = samples.Max(sample => sample.Wavelength);
        var wavelength = minimum + fraction * (maximum - minimum);
        _hover = samples.MinBy(sample => Math.Abs(sample.Wavelength - wavelength));
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        InvalidateVisual();
    }
}

public abstract class MeasurementChartElement : FrameworkElement
{
    protected MeasurementChartElement()
    {
        ClipToBounds = true;
    }

    public static readonly DependencyProperty MeasurementProperty =
        DependencyProperty.Register(
            nameof(Measurement),
            typeof(SpotMeasurement),
            typeof(MeasurementChartElement),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SpotMeasurement? Measurement
    {
        get => (SpotMeasurement?)GetValue(MeasurementProperty);
        set => SetValue(MeasurementProperty, value);
    }
}

public sealed class CriChart : MeasurementChartElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChartDrawing.DrawCri(
            drawingContext,
            new Rect(RenderSize),
            Measurement?.Cri,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}

public sealed class Tm30VectorChart : MeasurementChartElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChartDrawing.DrawTm30Vector(
            drawingContext,
            new Rect(RenderSize),
            Measurement?.Tm30,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}

public sealed class RfRgChart : MeasurementChartElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChartDrawing.DrawRfRg(
            drawingContext,
            new Rect(RenderSize),
            Measurement?.Tm30,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}

public sealed class Tm30SampleChart : MeasurementChartElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChartDrawing.DrawTm30Samples(
            drawingContext,
            new Rect(RenderSize),
            Measurement?.Tm30,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}

public sealed class LightingHistoryChart : MeasurementChartElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChartDrawing.DrawLightingHistoryThumbnail(
            drawingContext,
            new Rect(RenderSize),
            Measurement);
    }
}
