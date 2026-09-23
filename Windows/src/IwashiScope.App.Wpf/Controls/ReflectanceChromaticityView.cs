using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;
using IwashiScope.App.Wpf.Localization;

using IwashiScope.App.Wpf.ColorManagement;

namespace IwashiScope.App.Wpf.Controls;

public sealed class ReflectanceChromaticityView : GroupBox
{
    public static readonly DependencyProperty UiLanguageProperty = DependencyProperty.Register(nameof(UiLanguage), typeof(string), typeof(ReflectanceChromaticityView),
        new PropertyMetadata("ja", (d, e) => ((ReflectanceChromaticityView)d)._localization.SetLanguage((string)e.NewValue)));
    public string UiLanguage { get => (string)GetValue(UiLanguageProperty); set => SetValue(UiLanguageProperty, value); }
    private readonly LocalizationCatalog _localization = new();
    private readonly List<Action> _localizedLabels = [];
    public static readonly DependencyProperty MeasurementProperty = DependencyProperty.Register(nameof(Measurement), typeof(SpotMeasurement),
        typeof(ReflectanceChromaticityView), new PropertyMetadata(null, (d, _) => ((ReflectanceChromaticityView)d).Refresh()));
    public SpotMeasurement? Measurement { get => (SpotMeasurement?)GetValue(MeasurementProperty); set => SetValue(MeasurementProperty, value); }
    private readonly ChromaticityDiagram _diagram = new();
    private readonly TextBlock _coordinates = new(), _detail = new() { TextWrapping = TextWrapping.Wrap }, _markerDetail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<RadioButton> _radios = [];
    private readonly List<System.Windows.Shapes.Line> _legendLines = [];
    private readonly ChromaticityReferenceButton _brightness = new() { Content = "明るさ（参考）", FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _limit = new() { Text = "白を超える相対輝度は、画面の表示上限に合わせています。", FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
    private Window? _host;
    private ConfusionColorMode _mode;
    public ReflectanceChromaticityView()
    {
        Header = "xy色度図"; Margin = new Thickness(0, 12, 0, 0); Padding = new Thickness(6);
        AutomationProperties.SetAutomationId(this, "reflectance-cie2006-chromaticity-group");
        var stack = new StackPanel(); Content = stack;
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        row.Children.Add(new TextBlock { Text = "色覚タイプ", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        var group = "Chromaticity-" + Guid.NewGuid().ToString("N");
        foreach (var mode in Enum.GetValues<ConfusionColorMode>())
        {
            var button = new RadioButton { Content = mode.ToString(), GroupName = group, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), IsChecked = mode == ConfusionColorMode.C };
            AutomationProperties.SetAutomationId(button, "chromaticity-color-" + mode);
            button.Checked += (_, _) => { _mode = mode; _brightness.ResetReference(); Refresh(); };
            _radios.Add(button); row.Children.Add(button);
        }
        row.Children.Add(new TextBlock { Text = "の混同色を表示", FontSize = 11 }); stack.Children.Add(row);
        var referenceRow = new WrapPanel();
        referenceRow.Children.Add(new TextBlock { Text = "CIE2015 / CIE2006 LMS · 2°", FontSize = 11, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetAutomationId(_brightness, "chromaticity-brightness-reference-button");
        _brightness.ToolTip = "押している間、タイプ別の相対輝度を均一なグレーで表示します。離すとカラーに戻ります。";
        _brightness.ReferencePressedChanged += (_, _) => UpdateReferencePresentation();
        referenceRow.Children.Add(_brightness); stack.Children.Add(referenceRow);
        _coordinates.FontSize = 11; _coordinates.Margin = new Thickness(0, 6, 0, 3); stack.Children.Add(_coordinates);
        stack.Children.Add(_diagram);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        legend.Children.Add(new TextBlock { Text = "混同線", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var (label, color, dash) in new[] { ("L (P)", Colors.Red, DashStyles.Solid), ("M (D)", Colors.Green, new DashStyle(new[] { 4d, 8d / 3 }, 0)), ("S (T)", Colors.Blue, new DashStyle(new[] { 2d / 3, 2d }, 0)) })
        {
            var line = new System.Windows.Shapes.Line { X2 = 22, Y1 = 6, Y2 = 6, Stroke = new SolidColorBrush(color), StrokeThickness = 1.5, StrokeDashArray = dash.Dashes, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 24 };
            legend.Children.Add(line); legend.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(2, 0, 8, 0) });
            _legendLines.Add(line);
        }
        stack.Children.Add(legend);
        _detail.FontSize = _markerDetail.FontSize = 10; _detail.Foreground = _markerDetail.Foreground = Brushes.Gray;
        stack.Children.Add(_detail); stack.Children.Add(_markerDetail);
        stack.Children.Add(new TextBlock { Text = "タイプ別の相対輝度を一定にして表示（色域外は彩度を調整）。", ToolTip = "PはM、DはL、TはL/Mの輝度成分をD50白に対して規格化した参考モデルです。主観的な明るさや、実際のモニター上の錐体応答一致を保証するものではありません。", TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(_limit);
        Loaded += (_, _) => { _host = Window.GetWindow(this); if (_host != null) _host.Deactivated += HostDeactivated; };
        Unloaded += (_, _) => { _brightness.ResetReference(); if (_host != null) _host.Deactivated -= HostDeactivated; _host = null; };
        CollectLabels(stack);
        _localizedLabels.Add(() => Header = T("xy色度図"));
        _localization.LanguageChanged += () => { foreach (var update in _localizedLabels) update(); Refresh(); };
        Refresh();
    }
    private string T(string text) => _localization.Text(text);
    private void CollectLabels(DependencyObject parent)
    {
        if (parent is TextBlock text && !string.IsNullOrEmpty(text.Text)) { var source = text.Text; _localizedLabels.Add(() => text.Text = T(source)); }
        if (parent is Button button && button.Content is string title) _localizedLabels.Add(() => button.Content = T(title));
        if (parent is FrameworkElement element && element.ToolTip is string hint) _localizedLabels.Add(() => element.ToolTip = T(hint));
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) CollectLabels(child);
    }
    private void HostDeactivated(object? sender, EventArgs e) => _brightness.ResetReference();
    private void UpdateReferencePresentation()
    {
        _diagram.ShowsBrightnessReference = _brightness.IsReferencePressed;
        for (var i = 0; i < _legendLines.Count; i++) _legendLines[i].Stroke = _brightness.IsReferencePressed ? Brushes.Gray : new SolidColorBrush(new[] { Colors.Red, Colors.Green, Colors.Blue }[i]);
        _markerDetail.Text = T(_brightness.IsReferencePressed ? "参考グレー：帯と測定点を同じ相対輝度で表示しています。" : Measurement?.Lab?.IsFinite == true ? "円の色は測定Labの表示色です。座標はRGBから逆算していません。" : "測定Labがないため、測定点は輪郭のみで表示します。");
        _diagram.InvalidateVisual();
    }
    private void Refresh()
    {
        _brightness.ResetReference();
        var result = Cie2006Chromaticity.Measure(Measurement, out var error);
        var usable = result != null;
        foreach (var radio in _radios) radio.IsEnabled = usable;
        if (!usable) { _mode = ConfusionColorMode.C; _radios[0].IsChecked = true; }
        _coordinates.Text = T("測定結果（D50）") + (result == null ? "   x_F —  y_F —" : string.Create(CultureInfo.InvariantCulture, $"   x_F {result.Point.X:F4}  y_F {result.Point.Y:F4}"));
        _detail.Text = result != null ? (UiLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? $"Recalculated under D50 over the measured range {result.Range.Start:0.#}–{result.Range.End:0.#} nm." : $"D50・{result.Range.Start:0.#}–{result.Range.End:0.#} nmの測定範囲から再計算。") : T(error switch
        {
            ChromaticityError.ZeroSignal => "反射光がゼロのため、色度を定義できません。",
            ChromaticityError.InvalidSpectrum => "色度を計算できないスペクトルです。",
            _ => "色度の計算に必要な反射スペクトルがありません。",
        });
        var reference = result == null ? null : ChromaticityBrightnessReference.Create(result.Lms, result.WhiteLms, _mode);
        _brightness.IsEnabled = reference != null;
        _limit.Visibility = reference?.IsDisplayLimited == true ? Visibility.Visible : Visibility.Collapsed;
        _diagram.Measurement = Measurement; _diagram.Result = result; _diagram.ColorMode = _mode; _diagram.InvalidateVisual();
        UpdateReferencePresentation();
    }
}

public sealed class ChromaticityDiagram : FrameworkElement
{
    public SpotMeasurement? Measurement { get; set; }
    public PhysiologicalMeasurement? Result { get; set; }
    public ConfusionColorMode ColorMode { get; set; }
    public bool ShowsBrightnessReference { get; set; }
    public ChromaticityDiagram() => AutomationProperties.SetAutomationId(this, "cie2006-chromaticity-diagram");
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 300;
        return new(width, width * 0.9 / 0.8);
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        ChromaticityDrawing.Draw(drawingContext, new Rect(RenderSize), Measurement, Result, ColorMode, ShowsBrightnessReference, DisplayColorContext.GetContext(this));
    }
}

public static class ChromaticityDrawing
{
    private static readonly Lazy<BitmapSource> Background = new(CreateBackground);
    private sealed record PreparedSection(ChromaticitySegment Segment, BitmapSource Image, Brush First, Brush Last);
    private sealed record PreparedBand(Vector3 Gray, IReadOnlyList<PreparedSection> Sections);
    private static readonly Dictionary<(Vector3 Lms, Vector3 White, ConfusionColorMode Mode), PreparedBand> BandCache = [];
    private static PreparedBand? Prepare(PhysiologicalMeasurement? result, ConfusionColorMode mode)
    {
        if (result == null || mode == ConfusionColorMode.C) return null;
        var key = (result.Lms, result.WhiteLms, mode);
        lock (BandCache)
        {
            if (BandCache.TryGetValue(key, out var cached)) return cached;
            if (ChromaticityConfusionBand.Make(result.Lms, mode) is not { } band ||
                ChromaticityBrightnessReference.Create(result.Lms, result.WhiteLms, mode) is not { } reference) return null;
            var mapper = new ChromaticityBandDisplayMapper(reference);
            var sections = new List<PreparedSection>();
            foreach (var section in band.Sections)
            {
                if (mapper.Raster(section) is not { } pixels) return null;
                var bitmap = FloatBitmap(4096, 1, pixels);
                sections.Add(new(section.Segment, bitmap, LinearBrush(new(pixels[0], pixels[1], pixels[2])), LinearBrush(new(pixels[^4], pixels[^3], pixels[^2]))));
            }
            if (BandCache.Count >= 8) BandCache.Remove(BandCache.Keys.First());
            var value = new PreparedBand(mapper.GrayLinearRgb, sections); BandCache.Add(key, value); return value;
        }
    }
    private static Brush LinearBrush(Vector3 rgb)
    {
        var brush = new SolidColorBrush(Color.FromScRgb(1, (float)rgb.First, (float)rgb.Second, (float)rgb.Third)); brush.Freeze(); return brush;
    }
    private static BitmapSource FloatBitmap(int width, int height, float[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Prgba128Float, null, pixels, width * 16);
        RenderOptions.SetBitmapScalingMode(bitmap, BitmapScalingMode.Linear); bitmap.Freeze(); return bitmap;
    }
    public static Rect Plot(Rect bounds)
    {
        const double inset = ChromaticityConfusionBand.LineWidth / 2 + 1;
        var scale = Math.Max(0, Math.Min((bounds.Width - 2 * inset) / 0.8, (bounds.Height - 2 * inset) / 0.9));
        return new(bounds.X + (bounds.Width - 0.8 * scale) / 2, bounds.Y + (bounds.Height - 0.9 * scale) / 2, 0.8 * scale, 0.9 * scale);
    }
    public static void Draw(DrawingContext drawing, Rect bounds, SpotMeasurement? measurement, PhysiologicalMeasurement? result, ConfusionColorMode mode, bool showsBrightnessReference = false, DisplayColorContext? displayColors = null)
    {
        var plot = Plot(bounds); var scale = plot.Width / 0.8;
        if (scale <= 0) return;
        Point Position(PhysiologicalPoint p) => new(plot.Left + p.X * scale, plot.Bottom - p.Y * scale);
        var points = ChromaticityDisplayLocus.Points.Select(Position).ToArray();
        var locus = new StreamGeometry();
        using (var context = locus.Open()) { context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        locus.Freeze();
        var prepared = Prepare(result, mode);
        var gray = showsBrightnessReference && prepared?.Sections.Count > 0 ? displayColors?.LinearSrgbBrush(prepared.Gray) ?? LinearBrush(prepared.Gray) : null;
        if (gray != null) drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(230, 230, 230)), null, bounds);
        else
        {
            drawing.DrawGeometry(new SolidColorBrush(Color.FromArgb(23, 128, 128, 128)), null, locus);
            drawing.PushClip(locus); drawing.PushOpacity(ChromaticityIllustrationBackground.Opacity); drawing.DrawImage(displayColors?.ReferenceImage(Background.Value) ?? Background.Value, plot); drawing.Pop(); drawing.Pop();
        }
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(51, 128, 128, 128)), 0.5);
        for (var i = 0; i <= 8; i++) drawing.DrawLine(grid, Position(new(i / 10d, 0)), Position(new(i / 10d, 0.9)));
        for (var i = 0; i <= 9; i++) drawing.DrawLine(grid, Position(new(0, i / 10d)), Position(new(0.8, i / 10d)));
        drawing.PushClip(locus);
        drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)), 2.4) { LineJoin = PenLineJoin.Round }, locus);
        drawing.Pop();
        if (result is not { Point: var point }) return;
        drawing.PushClip(locus);
        foreach (var cone in Enum.GetValues<PhysiologicalCone>())
        {
            if (Cie2006Chromaticity.ConfusionLine(point, cone) is not { } line) continue;
            var color = gray != null ? Colors.Gray : cone switch { PhysiologicalCone.Long => Colors.Red, PhysiologicalCone.Medium => Colors.Green, _ => Colors.Blue };
            var dashes = cone switch { PhysiologicalCone.Long => Array.Empty<double>(), PhysiologicalCone.Medium => new[] { 6d, 4d }, _ => new[] { 1d, 3d } };
            foreach (var (width, brush) in new[] { (3.5, new SolidColorBrush(Color.FromArgb(153, 255, 255, 255))), (1.5, new SolidColorBrush(color) { Opacity = 0.85 }) })
                drawing.DrawLine(new Pen(brush, width) { DashStyle = new DashStyle(dashes.Select(d => d / width), 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round }, Position(line.Start), Position(line.End));
        }
        drawing.Pop();
        if (prepared != null)
        {
            foreach (var section in prepared.Sections)
            {
                var start = Position(section.Segment.Start); var end = Position(section.Segment.End);
                if (gray != null) drawing.DrawLine(new Pen(gray, 20) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end);
                else
                {
                    var dx = end.X - start.X; var dy = end.Y - start.Y; var length = Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 0) continue;
                    drawing.PushTransform(new MatrixTransform(dx / length, dy / length, -dy / length, dx / length, start.X, start.Y));
                    drawing.DrawImage(displayColors?.ReferenceImage(section.Image) ?? section.Image, new Rect(0, -10, length, 20));
                    drawing.Pop();
                    // No locus clip on the opaque round caps.
                    drawing.DrawEllipse(ReferenceBrush(section.First), null, start, 10, 10); drawing.DrawEllipse(ReferenceBrush(section.Last), null, end, 10, 10);
                }
            }
        }
        if (point.X < 0 || point.X > 0.8 || point.Y < 0 || point.Y > 0.9) return;
        var location = Position(point);
        drawing.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 1), location, 8, 8);
        Brush? marker = gray;
        if (gray == null && measurement?.Lab is { IsFinite: true } lab)
        {
            var rgb = LabColorConverter.Convert(lab, measurement.LabWhitePoint).Srgb;
            marker = displayColors?.LabBrush(lab, measurement.LabWhitePoint) ?? new SolidColorBrush(Color.FromRgb(rgb.RedByte, rgb.GreenByte, rgb.BlueByte));
        }
        drawing.DrawEllipse(marker, new Pen(Brushes.White, 2), location, 7, 7);
        Brush ReferenceBrush(Brush source)
        {
            if (displayColors == null || source is not SolidColorBrush solid) return source;
            return displayColors.LinearSrgbBrush(new(solid.Color.ScR, solid.Color.ScG, solid.Color.ScB));
        }
    }
    public static double Decode(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    private static BitmapSource CreateBackground()
    {
        return FloatBitmap(1024, 1152, new ChromaticityIllustrationBackground().Raster()!);
    }
}
