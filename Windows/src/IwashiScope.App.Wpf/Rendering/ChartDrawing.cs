using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.Rendering;

internal static class ChartDrawing
{
    private static readonly Typeface Typeface = new("Segoe UI");
    private static readonly Brush AxisBrush = Freeze(new SolidColorBrush(Color.FromRgb(91, 102, 112)));
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(225, 230, 234)));
    private static readonly Pen AxisPen = Freeze(new Pen(AxisBrush, 1));
    private static readonly Pen GridPen = Freeze(new Pen(GridBrush, 1));
    private static readonly Pen LabCenterPen = Freeze(
        new Pen(new SolidColorBrush(Color.FromArgb(128, 91, 102, 112)), 1));
    private static readonly ConcurrentDictionary<LabPlaneCacheKey, BitmapSource>
        LabPlaneCache = new();
    private static readonly Pen SpectrumPen =
        Freeze(new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        });
    private static readonly Pen HistorySpectrumPen =
        Freeze(new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.25)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        });
    private static readonly Brush HistoryBackgroundBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(249, 250, 250)));
    private static readonly Pen HistoryDividerPen =
        Freeze(new Pen(new SolidColorBrush(Color.FromRgb(222, 224, 226)), 1));
    private static readonly Pen HistoryCriRulePen =
        Freeze(new Pen(new SolidColorBrush(Color.FromArgb(89, 91, 102, 112)), 0.75)
        {
            DashStyle = new DashStyle([3, 3], 0),
        });
    private static readonly Pen D50Pen =
        Freeze(new Pen(new SolidColorBrush(Color.FromRgb(238, 150, 44)), 1.5)
        {
            DashStyle = DashStyles.Dash,
        });
    private static readonly Pen D65Pen =
        Freeze(new Pen(new SolidColorBrush(Color.FromRgb(64, 125, 225)), 1.5)
        {
            DashStyle = DashStyles.Dash,
        });
    private static readonly Brush[] CriBrushes =
    [
        Rgb(0.72, 0.48, 0.46), Rgb(0.78, 0.64, 0.27), Rgb(0.56, 0.70, 0.27),
        Rgb(0.25, 0.66, 0.48), Rgb(0.20, 0.66, 0.72), Rgb(0.24, 0.50, 0.83),
        Rgb(0.47, 0.37, 0.78), Rgb(0.72, 0.35, 0.68), Rgb(0.88, 0.20, 0.20),
        Rgb(0.89, 0.68, 0.16), Rgb(0.22, 0.65, 0.30), Rgb(0.18, 0.38, 0.80),
        Rgb(0.86, 0.53, 0.43), Rgb(0.64, 0.61, 0.22), Rgb(0.79, 0.43, 0.32),
    ];
    private static readonly (double Wavelength, Color Color)[] SpectrumStops =
    [
        (380.0, RgbColor(0.42, 0.18, 0.85)),
        (439.5, RgbColor(0.16, 0.30, 0.95)),
        (488.5, RgbColor(0.05, 0.72, 0.95)),
        (537.5, RgbColor(0.12, 0.82, 0.35)),
        (583.0, RgbColor(0.94, 0.88, 0.12)),
        (632.0, RgbColor(1.00, 0.47, 0.08)),
        (674.0, RgbColor(0.95, 0.08, 0.12)),
        (730.0, RgbColor(0.55, 0.00, 0.04)),
    ];
    private static readonly Brush[] HistoryCriBrushes =
        CriBrushes.Select(CreateHistoryCriGradient).ToArray();

    public static Rect PlotRect(Rect bounds) =>
        new(
            bounds.Left + Math.Max(54, bounds.Width * 0.07),
            bounds.Top + Math.Max(24, bounds.Height * 0.06),
            Math.Max(1, bounds.Width - Math.Max(80, bounds.Width * 0.1)),
            Math.Max(1, bounds.Height - Math.Max(66, bounds.Height * 0.15)));

    internal static Color LightingHistorySpectrumColor(double wavelength) =>
        SpectrumColor(wavelength);

    internal static Color LightingHistoryCriColor(int index)
    {
        if (index is < 1 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return ((SolidColorBrush)CriBrushes[index - 1]).Color;
    }

    public static void DrawSpectrum(
        DrawingContext drawing,
        Rect bounds,
        SpotMeasurement measurement,
        bool practicalRange,
        bool showD50,
        bool showD65,
        SpectrumYAxisConfiguration yAxisConfiguration,
        SpectralSample? hover = null,
        double pixelsPerDip = 1)
    {
        drawing.DrawRectangle(Brushes.White, null, bounds);
        var plot = PlotRect(bounds);

        var samples = measurement.DisplaySpectrum(practicalRange)
            .OrderBy(sample => sample.Wavelength)
            .ToArray();
        if (samples.Length == 0)
        {
            drawing.DrawRectangle(Brushes.White, AxisPen, plot);
            DrawCenteredText(drawing, "No spectrum", bounds, 16, AxisBrush, pixelsPerDip);
            return;
        }

        var minimumWavelength = samples[0].Wavelength;
        var maximumWavelength = samples[^1].Wavelength;
        drawing.DrawRectangle(
            SpectrumGradient(minimumWavelength, maximumWavelength, 0.17),
            null,
            plot); // square chart corners by design, including exported PNG
        var d50Overlay = showD50
            ? CieStandardIlluminants.ScaleToMeasurement(
                CieStandardIlluminant.D50,
                measurement.Spectrum)
            : [];
        var d65Overlay = showD65
            ? CieStandardIlluminants.ScaleToMeasurement(
                CieStandardIlluminant.D65,
                measurement.Spectrum)
            : [];
        var overlays = new[] { d50Overlay, d65Overlay };
        var maximumValue = samples.Max(sample => sample.Value);
        foreach (var overlay in overlays)
        {
            var inRange = overlay.Where(sample =>
                sample.Wavelength >= minimumWavelength &&
                sample.Wavelength <= maximumWavelength);
            if (inRange.Any())
            {
                maximumValue = Math.Max(maximumValue, inRange.Max(sample => sample.Value));
            }
        }
        var yAxisScale = yAxisConfiguration.ResolveScale(maximumValue);
        maximumValue = yAxisScale.UpperBound;

        var spectrumPoints = SeriesPoints(
            samples,
            plot,
            minimumWavelength,
            maximumWavelength,
            maximumValue);
        if (spectrumPoints.Length >= 2)
        {
            var areaPoints = new List<Point>(spectrumPoints.Length + 2)
            {
                new(spectrumPoints[0].X, plot.Bottom),
            };
            areaPoints.AddRange(spectrumPoints);
            areaPoints.Add(new Point(spectrumPoints[^1].X, plot.Bottom));
            drawing.DrawGeometry(
                SpectrumGradient(minimumWavelength, maximumWavelength, 1),
                null,
                ClosedGeometry(areaPoints));
        }

        foreach (var tickValue in yAxisScale.TickValues)
        {
            var y = MapY(tickValue, maximumValue, plot);
            drawing.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(
                drawing,
                tickValue.ToString("0", CultureInfo.InvariantCulture),
                new Point(bounds.Left + 4, y - 8),
                11,
                AxisBrush,
                pixelsPerDip);
        }

        var xTicks = new[]
        {
            minimumWavelength,
            minimumWavelength + (maximumWavelength - minimumWavelength) * 0.25,
            minimumWavelength + (maximumWavelength - minimumWavelength) * 0.5,
            minimumWavelength + (maximumWavelength - minimumWavelength) * 0.75,
            maximumWavelength,
        };
        foreach (var tick in xTicks)
        {
            var x = MapX(tick, minimumWavelength, maximumWavelength, plot);
            drawing.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = $"{tick:0} nm";
            var text = Formatted(label, 11, AxisBrush, pixelsPerDip);
            drawing.DrawText(text, new Point(x - text.Width / 2, plot.Bottom + 8));
        }

        if (d50Overlay.Count > 0)
        {
            DrawSeries(
                drawing,
                d50Overlay,
                D50Pen,
                plot,
                minimumWavelength,
                maximumWavelength,
                maximumValue);
        }
        if (d65Overlay.Count > 0)
        {
            DrawSeries(
                drawing,
                d65Overlay,
                D65Pen,
                plot,
                minimumWavelength,
                maximumWavelength,
                maximumValue);
        }
        var legendX = plot.Right - 116;
        var legendY = plot.Top + 10;
        if (d50Overlay.Count > 0)
        {
            drawing.DrawLine(D50Pen, new Point(legendX, legendY + 7), new Point(legendX + 28, legendY + 7));
            DrawText(drawing, "D50", new Point(legendX + 34, legendY), 11, AxisBrush, pixelsPerDip);
            legendY += 20;
        }
        if (d65Overlay.Count > 0)
        {
            drawing.DrawLine(D65Pen, new Point(legendX, legendY + 7), new Point(legendX + 28, legendY + 7));
            DrawText(drawing, "D65", new Point(legendX + 34, legendY), 11, AxisBrush, pixelsPerDip);
        }
        DrawPolyline(drawing, spectrumPoints, SpectrumPen);
        drawing.DrawRectangle(null, AxisPen, plot);

        if (hover is not null)
        {
            var x = MapX(hover.Wavelength, minimumWavelength, maximumWavelength, plot);
            var y = MapY(hover.Value, maximumValue, plot);
            drawing.DrawEllipse(Brushes.White, SpectrumPen, new Point(x, y), 4, 4);
            var label = $"{hover.Wavelength:0.#} nm\n測定値 {hover.Value:0.##}";
            var text = Formatted(label, 12, Brushes.White, pixelsPerDip);
            var tooltip = new Rect(
                Math.Clamp(x + 10, plot.Left, plot.Right - text.Width - 16),
                Math.Clamp(y - text.Height - 16, plot.Top, plot.Bottom - text.Height - 12),
                text.Width + 14,
                text.Height + 10);
            var tooltipBrush = new SolidColorBrush(Color.FromArgb(235, 28, 35, 42));
            var pointerEdgeX = tooltip.Left >= x ? tooltip.Left : tooltip.Right;
            var pointerY = Math.Clamp(y, tooltip.Top + 7, tooltip.Bottom - 7);
            var pointer = new StreamGeometry();
            using (var pointerContext = pointer.Open())
            {
                pointerContext.BeginFigure(new Point(x, y), true, true);
                pointerContext.LineTo(new Point(pointerEdgeX, pointerY - 6), true, false);
                pointerContext.LineTo(new Point(pointerEdgeX, pointerY + 6), true, false);
            }
            pointer.Freeze();
            drawing.DrawGeometry(tooltipBrush, null, pointer);
            drawing.DrawRectangle(tooltipBrush, null, tooltip);
            drawing.DrawText(text, new Point(tooltip.Left + 7, tooltip.Top + 5));
        }
    }

    public static void DrawCri(
        DrawingContext drawing,
        Rect bounds,
        CriResult? cri,
        double pixelsPerDip = 1)
    {
        drawing.DrawRectangle(Brushes.White, null, bounds);
        var plot = PlotRect(bounds);
        drawing.DrawRectangle(Brushes.White, AxisPen, plot);
        if (cri is null)
        {
            DrawCenteredText(drawing, "No CRI data", bounds, 16, AxisBrush, pixelsPerDip);
            return;
        }

        var values = Enumerable.Range(1, 15)
            .Select(index => cri.Individual.TryGetValue(index, out var value)
                ? (Index: index, Value: value)
                : (Index: index, Value: double.NaN))
            .ToArray();
        var finiteValues = values.Where(value => double.IsFinite(value.Value)).ToArray();
        var minimum = finiteValues.Length == 0
            ? 0
            : Math.Min(0, Math.Floor(finiteValues.Min(value => value.Value) / 10) * 10);
        var maximum = Math.Max(100, finiteValues.Length == 0
            ? 100
            : Math.Ceiling(finiteValues.Max(value => value.Value) / 10) * 10);
        var interval = (maximum - minimum) / 5;
        for (var tick = 0; tick <= 5; tick++)
        {
            var value = minimum + interval * tick;
            var y = plot.Bottom - plot.Height * (value - minimum) / (maximum - minimum);
            drawing.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(drawing, $"{value:0}", new Point(plot.Left - 38, y - 7), 11, AxisBrush, pixelsPerDip);
        }

        var slot = plot.Width / values.Length;
        var zeroY = plot.Bottom - plot.Height * (0 - minimum) / (maximum - minimum);
        var ruleY = plot.Bottom - plot.Height * (80 - minimum) / (maximum - minimum);
        drawing.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(90, 91, 102, 112)), 1)
            {
                DashStyle = DashStyles.Dash,
            },
            new Point(plot.Left, ruleY),
            new Point(plot.Right, ruleY));
        foreach (var value in values)
        {
            if (!double.IsFinite(value.Value))
            {
                continue;
            }
            var valueY = plot.Bottom -
                plot.Height * (Math.Clamp(value.Value, minimum, maximum) - minimum) /
                (maximum - minimum);
            var rect = new Rect(
                plot.Left + (value.Index - 1) * slot + slot * 0.16,
                Math.Min(valueY, zeroY),
                slot * 0.68,
                Math.Max(1, Math.Abs(zeroY - valueY)));
            var brush = CriBrushes[value.Index - 1];
            drawing.DrawRectangle(brush, null, rect);
            var axisText = Formatted($"R{value.Index}", 10, AxisBrush, pixelsPerDip);
            drawing.DrawText(
                axisText,
                new Point(rect.Left + (rect.Width - axisText.Width) / 2, plot.Bottom + 7));
            var barHeight = rect.Height;
            var labelInside = barHeight >= 22;
            var valueText = Formatted(
                $"{value.Value:0.#}",
                9,
                labelInside ? Brushes.White : AxisBrush,
                pixelsPerDip);
            var tipY = value.Value >= 0 ? rect.Top : rect.Bottom;
            var directionIntoBar = value.Value >= 0 ? 1 : -1;
            var labelCenterY = tipY +
                (labelInside ? directionIntoBar : -directionIntoBar) *
                (6 + valueText.Height / 2);
            var labelY = Math.Clamp(
                labelCenterY - valueText.Height / 2,
                plot.Top,
                plot.Bottom - valueText.Height);
            drawing.DrawText(
                valueText,
                new Point(rect.Left + (rect.Width - valueText.Width) / 2, labelY));
        }

        var rangeStart = plot.Left + slot * 0.16;
        var rangeEnd = plot.Left + slot * 7 + slot * 0.84;
        var raText = Formatted($"Ra {cri.Ra:0.0}", 12, SpectrumPen.Brush, pixelsPerDip);
        var raCenter = (rangeStart + rangeEnd) / 2;
        var raY = Math.Max(bounds.Top + 2, plot.Top - raText.Height - 3);
        var lineY = raY + raText.Height / 2;
        var gap = raText.Width / 2 + 7;
        drawing.DrawLine(SpectrumPen, new Point(rangeStart, lineY), new Point(raCenter - gap, lineY));
        drawing.DrawLine(SpectrumPen, new Point(raCenter + gap, lineY), new Point(rangeEnd, lineY));
        drawing.DrawLine(SpectrumPen, new Point(rangeStart, lineY), new Point(rangeStart, plot.Top));
        drawing.DrawLine(SpectrumPen, new Point(rangeEnd, lineY), new Point(rangeEnd, plot.Top));
        drawing.DrawText(raText, new Point(raCenter - raText.Width / 2, raY));
    }

    public static void DrawLightingHistoryThumbnail(
        DrawingContext drawing,
        Rect bounds,
        SpotMeasurement? measurement)
    {
        drawing.DrawRectangle(HistoryBackgroundBrush, null, bounds);
        if (measurement is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        const double spectrumHeight = 44;
        const double dividerHeight = 1;
        const double criHeight = 44;
        const double totalHeight = spectrumHeight + dividerHeight + criHeight;
        var verticalScale = bounds.Height / totalHeight;
        var spectrumBounds = new Rect(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            spectrumHeight * verticalScale);
        var dividerY = spectrumBounds.Bottom + dividerHeight * verticalScale / 2;
        var criBounds = new Rect(
            bounds.Left,
            spectrumBounds.Bottom + dividerHeight * verticalScale,
            bounds.Width,
            Math.Max(0, bounds.Bottom - spectrumBounds.Bottom - dividerHeight * verticalScale));

        DrawLightingHistorySpectrum(drawing, spectrumBounds, measurement);
        drawing.DrawLine(
            HistoryDividerPen,
            new Point(bounds.Left, dividerY),
            new Point(bounds.Right, dividerY));
        DrawLightingHistoryCri(drawing, criBounds, measurement.Cri);
    }

    public static void DrawLabAB(
        DrawingContext drawing,
        Rect bounds,
        SpotMeasurement? measurement,
        double pixelsPerDip = 1)
    {
        var lab = measurement?.Lab;
        var limit = lab is { } finiteLab && finiteLab.IsFinite
            ? LabABChartScale.ResolveLimit(finiteLab.Second, finiteLab.Third)
            : 100;
        var domainMinimum = -limit;
        var domainMaximum = limit;
        const double inset = 2;
        var plotSize = Math.Max(
            1,
            Math.Min(bounds.Width - inset * 2, bounds.Height - inset * 2));
        var plot = new Rect(
            bounds.Left + (bounds.Width - plotSize) / 2,
            bounds.Top + (bounds.Height - plotSize) / 2,
            plotSize,
            plotSize);
        var plotBackground = new SolidColorBrush(Color.FromArgb(9, 91, 102, 112));
        plotBackground.Freeze();
        drawing.DrawRectangle(plotBackground, null, plot);

        if (measurement?.Lab is { } backgroundLab && backgroundLab.IsFinite)
        {
            DrawLabPlaneBackground(
                drawing,
                plot,
                backgroundLab.First,
                measurement.LabWhitePoint,
                limit);
        }

        double X(double value) =>
            plot.Left + (value - domainMinimum) / (domainMaximum - domainMinimum) * plot.Width;
        double Y(double value) =>
            plot.Bottom - (value - domainMinimum) / (domainMaximum - domainMinimum) * plot.Height;

        var centerX = X(0);
        var centerY = Y(0);
        drawing.DrawLine(LabCenterPen, new Point(centerX, plot.Top), new Point(centerX, plot.Bottom));
        drawing.DrawLine(LabCenterPen, new Point(plot.Left, centerY), new Point(plot.Right, centerY));

        var positiveLimit = Formatted(
            limit.ToString("0", CultureInfo.InvariantCulture),
            9,
            AxisBrush,
            pixelsPerDip);
        drawing.DrawText(
            positiveLimit,
            new Point(plot.Right - positiveLimit.Width - 3, plot.Top + 2));
        var negativeLimit = Formatted(
            (-limit).ToString("0", CultureInfo.InvariantCulture),
            9,
            AxisBrush,
            pixelsPerDip);
        drawing.DrawText(
            negativeLimit,
            new Point(plot.Left + 3, plot.Bottom - negativeLimit.Height - 2));

        var positiveB = Formatted("+b", 9, Brushes.Black, pixelsPerDip);
        drawing.DrawText(
            positiveB,
            new Point(plot.Left + (plot.Width - positiveB.Width) / 2, plot.Top + 2));
        var negativeB = Formatted("-b", 9, Brushes.Black, pixelsPerDip);
        drawing.DrawText(
            negativeB,
            new Point(plot.Left + (plot.Width - negativeB.Width) / 2, plot.Bottom - negativeB.Height - 2));
        var negativeA = Formatted("-a", 9, Brushes.Black, pixelsPerDip);
        drawing.DrawText(
            negativeA,
            new Point(plot.Left + 2, plot.Top + (plot.Height - negativeA.Height) / 2));
        var positiveA = Formatted("+a", 9, Brushes.Black, pixelsPerDip);
        drawing.DrawText(
            positiveA,
            new Point(plot.Right - positiveA.Width - 2, plot.Top + (plot.Height - positiveA.Height) / 2));

        drawing.DrawRectangle(null, AxisPen, plot);

        if (lab is not { } plottedLab || !plottedLab.IsFinite)
        {
            return;
        }

        var point = new Point(
            X(Math.Clamp(plottedLab.Second, domainMinimum, domainMaximum)),
            Y(Math.Clamp(plottedLab.Third, domainMinimum, domainMaximum)));
        var outline = new Pen(Brushes.White, 2);
        outline.Freeze();
        drawing.DrawEllipse(Brushes.Black, outline, point, 5, 5);
    }

    private static void DrawLabPlaneBackground(
        DrawingContext drawing,
        Rect plot,
        double lightness,
        string? whitePoint,
        double limit)
    {
        drawing.PushClip(new RectangleGeometry(plot));
        drawing.PushOpacity(0.5);
        drawing.DrawImage(LabPlaneImage(lightness, whitePoint, limit), plot);
        drawing.Pop();
        drawing.Pop();
    }

    private static BitmapSource LabPlaneImage(
        double lightness,
        string? whitePoint,
        double limit)
    {
        var key = new LabPlaneCacheKey(
            (int)Math.Round(Math.Clamp(lightness, 0, 100) * 2),
            (int)limit,
            whitePoint?.Contains("D65", StringComparison.OrdinalIgnoreCase) == true);
        if (LabPlaneCache.Count > 32)
        {
            LabPlaneCache.Clear();
        }
        return LabPlaneCache.GetOrAdd(key, static cacheKey => CreateLabPlaneImage(cacheKey));
    }

    private static BitmapSource CreateLabPlaneImage(LabPlaneCacheKey key)
    {
        const int pixelSize = 96;
        const int bytesPerPixel = 4;
        var stride = pixelSize * bytesPerPixel;
        var pixels = new byte[pixelSize * stride];
        var lightness = key.LightnessHalves / 2.0;
        var limit = (double)key.Limit;
        var whitePoint = key.IsD65 ? "D65" : "D50";
        var denominator = pixelSize - 1.0;

        for (var row = 0; row < pixelSize; row++)
        {
            var b = limit - row / denominator * 2 * limit;
            for (var column = 0; column < pixelSize; column++)
            {
                var a = -limit + column / denominator * 2 * limit;
                var color = LabColorConverter.Convert(
                    new Vector3(lightness, a, b),
                    whitePoint).Srgb;
                var offset = row * stride + column * bytesPerPixel;
                pixels[offset] = color.BlueByte;
                pixels[offset + 1] = color.GreenByte;
                pixels[offset + 2] = color.RedByte;
                pixels[offset + 3] = 255;
            }
        }

        var image = BitmapSource.Create(
            pixelSize,
            pixelSize,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        image.Freeze();
        return image;
    }

    private readonly record struct LabPlaneCacheKey(
        int LightnessHalves,
        int Limit,
        bool IsD65);

    private static void DrawLightingHistorySpectrum(
        DrawingContext drawing,
        Rect bounds,
        SpotMeasurement measurement)
    {
        var samples = measurement.Spectrum
            .Where(sample =>
                double.IsFinite(sample.Wavelength) &&
                double.IsFinite(sample.Value))
            .OrderBy(sample => sample.Wavelength)
            .ToArray();
        var minimumWavelength =
            double.IsFinite(measurement.SpectrumStart) &&
            double.IsFinite(measurement.SpectrumEnd) &&
            measurement.SpectrumEnd > measurement.SpectrumStart
                ? measurement.SpectrumStart
                : samples.FirstOrDefault()?.Wavelength ?? 380;
        var maximumWavelength =
            double.IsFinite(measurement.SpectrumStart) &&
            double.IsFinite(measurement.SpectrumEnd) &&
            measurement.SpectrumEnd > measurement.SpectrumStart
                ? measurement.SpectrumEnd
                : samples.LastOrDefault()?.Wavelength ?? 730;
        drawing.DrawRectangle(
            SpectrumGradient(minimumWavelength, maximumWavelength, 0.10),
            null,
            bounds);
        if (samples.Length < 2)
        {
            return;
        }

        var plot = new Rect(
            bounds.Left + 4,
            bounds.Top + 4,
            Math.Max(1, bounds.Width - 8),
            Math.Max(1, bounds.Height - 8));
        var clip = new RectangleGeometry(plot, 5, 5);
        drawing.PushClip(clip);
        drawing.DrawRoundedRectangle(
            SpectrumGradient(minimumWavelength, maximumWavelength, 0.13),
            null,
            plot,
            5,
            5);

        var yUpperBound = Math.Max(1, samples.Max(sample => sample.Value) * 1.08);
        var points = samples.Select(sample => new Point(
            MapX(sample.Wavelength, minimumWavelength, maximumWavelength, plot),
            plot.Bottom - Math.Clamp(sample.Value / yUpperBound, 0, 1) * plot.Height))
            .ToArray();
        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(points[0].X, plot.Bottom), true, true);
            context.LineTo(points[0], true, false);
            context.PolyLineTo(points[1..], true, false);
            context.LineTo(new Point(points[^1].X, plot.Bottom), true, false);
        }
        area.Freeze();
        drawing.DrawGeometry(
            SpectrumGradient(minimumWavelength, maximumWavelength, 1),
            null,
            area);
        DrawPolyline(drawing, points, HistorySpectrumPen);
        drawing.Pop();
    }

    private static void DrawLightingHistoryCri(
        DrawingContext drawing,
        Rect bounds,
        CriResult? cri)
    {
        drawing.DrawRectangle(HistoryBackgroundBrush, null, bounds);
        if (cri is null)
        {
            return;
        }

        var scores = Enumerable.Range(1, 15)
            .Where(index =>
                cri.Individual.TryGetValue(index, out var value) &&
                double.IsFinite(value))
            .Select(index => (Index: index, Value: cri.Individual[index]))
            .ToArray();
        if (scores.Length == 0)
        {
            return;
        }

        var plot = new Rect(
            bounds.Left + 4,
            bounds.Top + 4,
            Math.Max(1, bounds.Width - 8),
            Math.Max(1, bounds.Height - 8));
        var minimum = Math.Min(0, scores.Min(score => score.Value));
        var maximum = Math.Max(100, scores.Max(score => score.Value));
        var range = Math.Max(maximum - minimum, double.Epsilon);
        double ScoreY(double value) =>
            plot.Bottom - (Math.Clamp(value, minimum, maximum) - minimum) / range * plot.Height;

        var zeroY = ScoreY(0);
        var slot = plot.Width / 15;
        foreach (var score in scores)
        {
            var valueY = ScoreY(score.Value);
            var rectangle = new Rect(
                plot.Left + (score.Index - 1) * slot + slot * 0.16,
                Math.Min(valueY, zeroY),
                Math.Max(1, slot * 0.68),
                Math.Max(1, Math.Abs(zeroY - valueY)));
            drawing.DrawRoundedRectangle(
                HistoryCriBrushes[score.Index - 1],
                null,
                rectangle,
                1.5,
                1.5);
        }

        var ruleY = ScoreY(80);
        drawing.DrawLine(
            HistoryCriRulePen,
            new Point(plot.Left, ruleY),
            new Point(plot.Right, ruleY));
    }

    public static void DrawTm30Vector(
        DrawingContext drawing,
        Rect bounds,
        Tm30Result? result,
        double pixelsPerDip = 1)
    {
        drawing.DrawRectangle(Brushes.White, null, bounds);
        var geometry = result is null
            ? null
            : Tm30ColorVectorGeometry.Create(result.HueBins);
        if (result is null || geometry is null)
        {
            DrawCenteredText(drawing, "No TM-30 data", bounds, 16, AxisBrush, pixelsPerDip);
            return;
        }
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var maximumTestRadius = geometry.TestContour.Max(point => point.Radius);
        var scaleLimit = Math.Max(1.2, Math.Ceiling(maximumTestRadius * 10) / 10);
        var plotRadius = Math.Max(10, Math.Min(bounds.Width, bounds.Height) * 0.40);

        for (var index = 0; index < 16; index++)
        {
            var startAngle = index * 2 * Math.PI / 16;
            var endAngle = (index + 1) * 2 * Math.PI / 16;
            var backdropRadius = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2;
            var wedge = new StreamGeometry();
            using (var context = wedge.Open())
            {
                context.BeginFigure(center, true, true);
                context.LineTo(RadialPoint(center, startAngle, backdropRadius), true, false);
                context.LineTo(RadialPoint(center, endAngle, backdropRadius), true, false);
            }
            wedge.Freeze();
            drawing.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(
                    58,
                    Hsv((startAngle + endAngle) / 2 * 180 / Math.PI, 0.62, 0.96).R,
                    Hsv((startAngle + endAngle) / 2 * 180 / Math.PI, 0.62, 0.96).G,
                    Hsv((startAngle + endAngle) / 2 * 180 / Math.PI, 0.62, 0.96).B)),
                null,
                wedge);
        }

        foreach (var normalizedRadius in new[] { 0.8, 0.9, 1.0, 1.1, 1.2 })
        {
            var radius = normalizedRadius * plotRadius / scaleLimit;
            drawing.DrawEllipse(
                null,
                new Pen(
                    new SolidColorBrush(Color.FromArgb(
                        normalizedRadius == 1 ? (byte)200 : (byte)130,
                        255,
                        255,
                        255)),
                    normalizedRadius == 1 ? 1.5 : 0.75),
                center,
                radius,
                radius);
        }

        for (var index = 0; index < 16; index++)
        {
            var angle = index * 2 * Math.PI / 16;
            drawing.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(96, 91, 102, 112)), 0.8)
                {
                    DashStyle = DashStyles.Dash,
                },
                RadialPoint(center, angle, plotRadius * 0.08),
                RadialPoint(center, angle, plotRadius * 1.22));
            var labelAngle = (index + 0.5) * 2 * Math.PI / 16;
            var label = Formatted((index + 1).ToString(), 10, AxisBrush, pixelsPerDip);
            var labelPoint = RadialPoint(center, labelAngle, plotRadius * 1.14);
            drawing.DrawText(
                label,
                new Point(labelPoint.X - label.Width / 2, labelPoint.Y - label.Height / 2));
        }

        Point PlotPoint(Tm30ColorVectorPoint point) =>
            new(
                center.X + point.X * plotRadius / scaleLimit,
                center.Y - point.Y * plotRadius / scaleLimit);

        foreach (var shift in geometry.Shifts)
        {
            DrawArrow(
                drawing,
                PlotPoint(shift.Reference),
                PlotPoint(shift.Test),
                new Pen(new SolidColorBrush(Color.FromArgb(145, 65, 120, 220)), 1));
        }

        var referencePoints = geometry.ReferenceContour.Select(PlotPoint).ToList();
        var testPoints = geometry.TestContour.Select(PlotPoint).ToList();
        Close(referencePoints);
        Close(testPoints);
        var testFill = ClosedGeometry(testPoints);
        drawing.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(30, 0, 117, 190)),
            null,
            testFill);
        DrawPolyline(drawing, referencePoints, new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1.25));
        DrawPolyline(
            drawing,
            testPoints,
            new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 190)), 2.5));

        DrawText(
            drawing,
            $"CCT {result.Cct:0} K\nDuv {result.Duv:+0.000000;-0.000000;0.000000}",
            new Point(bounds.Left + 12, bounds.Top + 8),
            11,
            AxisBrush,
            pixelsPerDip);
    }

    public static void DrawRfRg(
        DrawingContext drawing,
        Rect bounds,
        Tm30Result? result,
        double pixelsPerDip = 1)
    {
        drawing.DrawRectangle(Brushes.White, null, bounds);
        var plot = PlotRect(bounds);
        drawing.DrawRectangle(Brushes.White, AxisPen, plot);
        const double minRf = 50;
        const double maxRf = 100;
        const double minRg = 60;
        const double maxRg = 140;

        Point Point(double rf, double rg) =>
            new(
                plot.Left + (rf - minRf) / (maxRf - minRf) * plot.Width,
                plot.Bottom - (rg - minRg) / (maxRg - minRg) * plot.Height);

        var practical = ClosedGeometry(
        [
            Point(50, 50),
            Point(50, 150),
            Point(100, 100),
            Point(50, 50),
        ]);
        var planckian = ClosedGeometry(
        [
            Point(50, 62),
            Point(50, 138),
            Point(100, 100),
            Point(50, 62),
        ]);
        drawing.PushClip(new RectangleGeometry(plot));
        drawing.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(34, 91, 102, 112)),
            null,
            practical);
        drawing.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(54, 91, 102, 112)),
            null,
            planckian);
        drawing.Pop();

        for (var value = 50; value <= 100; value += 10)
        {
            var x = Point(value, minRg).X;
            drawing.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Formatted(value.ToString(CultureInfo.InvariantCulture), 9, AxisBrush, pixelsPerDip);
            drawing.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 7));
        }
        for (var value = 60; value <= 140; value += 20)
        {
            var y = Point(minRf, value).Y;
            drawing.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Formatted(value.ToString(CultureInfo.InvariantCulture), 9, AxisBrush, pixelsPerDip);
            drawing.DrawText(label, new Point(plot.Left - label.Width - 7, y - label.Height / 2));
        }
        var centerPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 91, 102, 112)), 0.8)
        {
            DashStyle = DashStyles.Dash,
        };
        drawing.DrawLine(centerPen, Point(50, 100), Point(100, 100));
        DrawText(drawing, "①", Point(57, 126), 11, AxisBrush, pixelsPerDip);
        DrawText(drawing, "②", Point(67, 128), 11, AxisBrush, pixelsPerDip);
        if (result is not null)
        {
            var measured = Point(
                Math.Clamp(result.FidelityIndex, minRf, maxRf),
                Math.Clamp(result.GamutIndex, minRg, maxRg));
            drawing.DrawEllipse(
                Brushes.Red,
                new Pen(Brushes.White, 2),
                measured,
                4,
                4);
        }
        DrawText(drawing, "Rf", new Point(plot.Right - 18, plot.Bottom + 21), 10, AxisBrush, pixelsPerDip);
        DrawText(drawing, "Rg", new Point(plot.Left - 38, plot.Top), 12, AxisBrush, pixelsPerDip);
    }

    public static void DrawTm30Samples(
        DrawingContext drawing,
        Rect bounds,
        Tm30Result? result,
        double pixelsPerDip = 1)
    {
        drawing.DrawRectangle(Brushes.White, null, bounds);
        var plot = PlotRect(bounds);
        drawing.DrawRectangle(Brushes.White, AxisPen, plot);
        if (result is null || result.EvaluationSamples.Count != 99)
        {
            DrawCenteredText(drawing, "No TM-30 sample data", bounds, 16, AxisBrush, pixelsPerDip);
            return;
        }
        var slot = plot.Width / 99;
        foreach (var sample in result.EvaluationSamples.OrderBy(sample => sample.Index))
        {
            var score = Tm30SampleFidelity.Score(sample.ReferenceJab, sample.TestJab) ?? 0;
            var height = plot.Height * score / 100;
            var brush = JabBrush(sample.ReferenceJab);
            drawing.DrawRectangle(
                brush,
                null,
                new Rect(
                    plot.Left + (sample.Index - 1) * slot + slot * 0.08,
                    plot.Bottom - height,
                    Math.Max(1, slot * 0.84),
                    height));
        }
        foreach (var index in new[] { 1, 20, 40, 60, 80, 99 })
        {
            var x = plot.Left + (index - 0.5) * slot;
            DrawText(drawing, index.ToString(), new Point(x - 8, plot.Bottom + 8), 10, AxisBrush, pixelsPerDip);
        }
    }

    private static void DrawSeries(
        DrawingContext drawing,
        IReadOnlyList<SpectralSample> samples,
        Pen pen,
        Rect plot,
        double minimumWavelength,
        double maximumWavelength,
        double maximumValue)
    {
        DrawPolyline(
            drawing,
            SeriesPoints(
                samples,
                plot,
                minimumWavelength,
                maximumWavelength,
                maximumValue),
            pen);
    }

    private static Point[] SeriesPoints(
        IReadOnlyList<SpectralSample> samples,
        Rect plot,
        double minimumWavelength,
        double maximumWavelength,
        double maximumValue) =>
        samples
            .Where(sample =>
                sample.Wavelength >= minimumWavelength &&
                sample.Wavelength <= maximumWavelength &&
                double.IsFinite(sample.Value))
            .Select(sample => new Point(
                MapX(sample.Wavelength, minimumWavelength, maximumWavelength, plot),
                MapY(sample.Value, maximumValue, plot)))
            .ToArray();

    private static void DrawPolyline(DrawingContext drawing, IReadOnlyList<Point> points, Pen pen)
    {
        if (points.Count < 2)
        {
            return;
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, pen, geometry);
    }

    private static StreamGeometry ClosedGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count > 0)
        {
            using var context = geometry.Open();
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void DrawArrow(DrawingContext drawing, Point start, Point end, Pen pen)
    {
        drawing.DrawLine(pen, start, end);
        var delta = end - start;
        var length = delta.Length;
        if (length < 4)
        {
            return;
        }
        delta.Normalize();
        var arrowLength = Math.Min(7, length * 0.55);
        var halfWidth = arrowLength * 0.45;
        var basis = end - delta * arrowLength;
        var perpendicular = new Vector(-delta.Y, delta.X);
        drawing.DrawLine(pen, basis + perpendicular * halfWidth, end);
        drawing.DrawLine(pen, end, basis - perpendicular * halfWidth);
    }

    private static Point RadialPoint(Point center, double angle, double radius) =>
        new(
            center.X + Math.Cos(angle) * radius,
            center.Y - Math.Sin(angle) * radius);

    private static void Close(List<Point> points)
    {
        if (points.Count > 0)
        {
            points.Add(points[0]);
        }
    }

    private static double MapX(double value, double minimum, double maximum, Rect plot) =>
        plot.Left + (value - minimum) / Math.Max(maximum - minimum, 1e-12) * plot.Width;

    private static double MapY(double value, double maximum, Rect plot) =>
        plot.Bottom - Math.Clamp(value / maximum, 0, 1) * plot.Height;

    private static LinearGradientBrush SpectrumGradient(
        double minimumWavelength,
        double maximumWavelength,
        double opacity)
    {
        var width = Math.Max(maximumWavelength - minimumWavelength, double.Epsilon);
        var wavelengths = new[] { minimumWavelength }
            .Concat(SpectrumStops
                .Select(stop => stop.Wavelength)
                .Where(wavelength =>
                    wavelength > minimumWavelength &&
                    wavelength < maximumWavelength))
            .Append(maximumWavelength);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            Opacity = opacity,
        };
        foreach (var wavelength in wavelengths)
        {
            brush.GradientStops.Add(new GradientStop(
                SpectrumColor(wavelength),
                (wavelength - minimumWavelength) / width));
        }
        brush.Freeze();
        return brush;
    }

    private static Color SpectrumColor(double wavelength)
    {
        if (wavelength <= SpectrumStops[0].Wavelength)
        {
            return SpectrumStops[0].Color;
        }
        if (wavelength >= SpectrumStops[^1].Wavelength)
        {
            return SpectrumStops[^1].Color;
        }
        for (var index = 0; index < SpectrumStops.Length - 1; index++)
        {
            var lower = SpectrumStops[index];
            var upper = SpectrumStops[index + 1];
            if (wavelength > upper.Wavelength)
            {
                continue;
            }
            var fraction = (wavelength - lower.Wavelength) /
                (upper.Wavelength - lower.Wavelength);
            return Color.FromRgb(
                (byte)Math.Round(lower.Color.R + (upper.Color.R - lower.Color.R) * fraction),
                (byte)Math.Round(lower.Color.G + (upper.Color.G - lower.Color.G) * fraction),
                (byte)Math.Round(lower.Color.B + (upper.Color.B - lower.Color.B) * fraction));
        }
        return SpectrumStops[^1].Color;
    }

    private static Brush JabBrush(Vector3 jab)
    {
        var chroma = Math.Sqrt(jab.Second * jab.Second + jab.Third * jab.Third);
        var hue = Math.Atan2(jab.Third, jab.Second) * 180 / Math.PI;
        if (hue < 0) hue += 360;
        return new SolidColorBrush(Hsv(
            hue,
            Math.Clamp(chroma / 45, 0, 0.85),
            Math.Clamp(0.22 + 0.78 * jab.First / 100, 0.18, 1)));
    }

    private static Color Hsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((red + m) * 255),
            (byte)Math.Round((green + m) * 255),
            (byte)Math.Round((blue + m) * 255));
    }

    private static Brush CreateHistoryCriGradient(Brush source)
    {
        var color = ((SolidColorBrush)source).Color;
        var brush = new LinearGradientBrush(
            Mix(color, Colors.White, 0.16),
            Mix(color, Colors.Black, 0.06),
            90);
        return Freeze(brush);
    }

    private static Color Mix(Color source, Color target, double fraction) =>
        Color.FromRgb(
            (byte)Math.Round(source.R + (target.R - source.R) * fraction),
            (byte)Math.Round(source.G + (target.G - source.G) * fraction),
            (byte)Math.Round(source.B + (target.B - source.B) * fraction));

    private static Brush Rgb(double red, double green, double blue) =>
        Freeze(new SolidColorBrush(RgbColor(red, green, blue)));

    private static Color RgbColor(double red, double green, double blue) =>
        Color.FromRgb(
            (byte)Math.Round(red * 255),
            (byte)Math.Round(green * 255),
            (byte)Math.Round(blue * 255));

    private static void DrawCenteredText(
        DrawingContext drawing,
        string value,
        Rect bounds,
        double size,
        Brush brush,
        double pixelsPerDip)
    {
        var text = Formatted(value, size, brush, pixelsPerDip);
        drawing.DrawText(
            text,
            new Point(
                bounds.Left + (bounds.Width - text.Width) / 2,
                bounds.Top + (bounds.Height - text.Height) / 2));
    }

    private static void DrawText(
        DrawingContext drawing,
        string value,
        Point origin,
        double size,
        Brush brush,
        double pixelsPerDip) =>
        drawing.DrawText(Formatted(value, size, brush, pixelsPerDip), origin);

    private static FormattedText Formatted(
        string value,
        double size,
        Brush brush,
        double pixelsPerDip) =>
        new(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface,
            size,
            brush,
            pixelsPerDip);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
