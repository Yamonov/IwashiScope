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
        var bounds = new Rect(RenderSize);
        drawingContext.DrawRectangle(Brushes.White, null, bounds);
        if (Measurement is null)
        {
            return;
        }

        var dividerY = bounds.Top + bounds.Height * 0.5;
        drawingContext.DrawLine(
            new Pen(new SolidColorBrush(Color.FromRgb(220, 227, 233)), 1),
            new Point(bounds.Left, dividerY),
            new Point(bounds.Right, dividerY));

        var samples = Measurement.Spectrum
            .Where(sample => double.IsFinite(sample.Value))
            .OrderBy(sample => sample.Wavelength)
            .ToArray();
        if (samples.Length > 1)
        {
            var maximum = Math.Max(samples.Max(sample => sample.Value), 1e-12);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var points = samples.Select((sample, index) => new Point(
                    bounds.Left + index * bounds.Width / Math.Max(1, samples.Length - 1),
                    dividerY - 3 - sample.Value / maximum * Math.Max(1, dividerY - bounds.Top - 6)))
                    .ToArray();
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points[1..], true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(
                null,
                new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 190)), 1.4),
                geometry);
        }

        if (Measurement.Cri is { } cri)
        {
            var values = Enumerable.Range(1, 15)
                .Select(index => cri.Individual.GetValueOrDefault(index))
                .ToArray();
            var slot = bounds.Width / values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                var value = Math.Clamp(values[index], 0, 100);
                var height = value / 100 * Math.Max(1, bounds.Bottom - dividerY - 5);
                drawingContext.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(80, 135, 186)),
                    null,
                    new Rect(
                        bounds.Left + index * slot + 1,
                        bounds.Bottom - height - 2,
                        Math.Max(1, slot - 2),
                        height));
            }
        }
    }
}
