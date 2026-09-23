using System.Runtime.CompilerServices;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.ColorManagement;

/// <summary>Display-only context. Never read this from an exporter or Core calculation.</summary>
public sealed class DisplayColorContext
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.RegisterAttached(
        "Context", typeof(DisplayColorContext), typeof(DisplayColorContext),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static DisplayColorContext? GetContext(DependencyObject element) => (DisplayColorContext?)element.GetValue(ContextProperty);
    public static void SetContext(DependencyObject element, DisplayColorContext? value) => element.SetValue(ContextProperty, value);
    private readonly ConditionalWeakTable<BitmapSource, BitmapSource> _referenceImages = new();
    internal IccColorTransform? Transform { get; }
    public string Monitor { get; }
    public string ProfilePath { get; }
    public string ProfileName => !string.IsNullOrWhiteSpace(Transform?.Description) ? Transform.Description :
        ProfilePath.Length > 0 ? Path.GetFileName(ProfilePath) : "sRGB";
    public int AdvancedMode { get; }
    public string? Error { get; }
    public bool HasProfile => Transform != null && ProfilePath.Length != 0;
    public bool IsGenericSrgb => Path.GetFileName(ProfilePath).Equals("sRGB Color Space Profile.icm", StringComparison.OrdinalIgnoreCase);

    public DisplayColorContext(IccColorTransform? transform, string monitor, string profilePath, int advancedMode, string? error = null)
    { Transform = transform; Monitor = monitor; ProfilePath = profilePath; AdvancedMode = advancedMode; Error = error; }

    public string Status(bool japanese) => japanese
        ? $"表示色管理：{(HasProfile ? IsGenericSrgb ? "汎用sRGB ICC" : "ICC適用" : "sRGB参考表示")} · {ProfileName}"
        : $"Display color: {(HasProfile ? IsGenericSrgb ? "generic sRGB ICC" : "ICC applied" : "sRGB reference")} · {ProfileName}";
    public string Details(bool japanese)
    {
        var mode = AdvancedMode switch { 0 => "SDR", 1 => "ACM / WCG", 2 => "HDR", 3 => "Advanced Color", _ => "Unknown" };
        var details = $"{Monitor} · {mode}\n{ProfilePath}\n{IccColorTransform.IntentDescription}";
        return details + (japanese
            ? "\nD50観察光と、実際のモニター状態を測定したICCを前提とします。モニターの白色点・白輝度を実物の観察条件に合わせてください。校正状態や実物との一致は自動判定していません。"
            : "\nAssumes D50 viewing light and an ICC profile measured for the current monitor state. Match display white and luminance to the viewing condition. Calibration and physical matching have not been automatically verified.")
            + (IsGenericSrgb ? (japanese ? "\n汎用sRGBプロファイルです。実測したモニターICCが適用されていることを確認してください。"
                : "\nThis is a generic sRGB profile. Check that the measured monitor ICC is assigned.") : "")
            + (AdvancedMode > 0 ? (japanese
                ? "\nWindowsがこのアプリに公開する有効ICCを使用します。ICC互換が無効なACM/HDRではsRGB表示に制限されることがあります。SDRでの比較を推奨します。"
                : "\nUses the effective ICC exposed by Windows. ACM/HDR without legacy ICC compatibility may restrict this app to sRGB. SDR is recommended for comparison.") : "")
            + (Error == null ? "" : $"\n{Error}");
    }
    public Brush LabBrush(Vector3 lab, string? whitePoint = "D50")
    {
        if (Transform == null)
        {
            var fallback = LabColorConverter.Convert(lab, whitePoint).Srgb;
            return Frozen(new SolidColorBrush(Color.FromRgb(fallback.RedByte, fallback.GreenByte, fallback.BlueByte)));
        }
        return DeviceBrush(Transform.FromLab(lab, whitePoint));
    }
    public Brush LinearSrgbBrush(Vector3 linear)
    {
        if (Transform == null) return Frozen(new SolidColorBrush(Color.FromScRgb(1, (float)linear.First, (float)linear.Second, (float)linear.Third)));
        var result = Transform.FromSrgb([Encode(linear.First), Encode(linear.Second), Encode(linear.Third)]);
        return DeviceBrush(new(result[0], result[1], result[2]));
    }
    public static Brush DeviceBrush(Vector3 encodedDevice) => Frozen(new SolidColorBrush(Color.FromScRgb(1,
        (float)Decode(Math.Clamp(encodedDevice.First, 0, 1)),
        (float)Decode(Math.Clamp(encodedDevice.Second, 0, 1)),
        (float)Decode(Math.Clamp(encodedDevice.Third, 0, 1)))));

    // WPF transports numeric device codes via its usual transfer function.
    // Decode here cancels that final encoding; it is not another ICC conversion.
    public BitmapSource LabImage(int width, int height, double[] d50Lab)
    {
        if (Transform == null) throw new InvalidOperationException("ICC context is unavailable.");
        return DeviceImage(width, height, Transform.FromD50Lab(d50Lab));
    }
    public static BitmapSource DeviceImage(int width, int height, double[] encodedDevice)
    {
        if (encodedDevice.Length != width * height * 3) throw new ArgumentException("Image dimensions do not match.");
        var pixels = new float[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            for (var channel = 0; channel < 3; channel++) pixels[i * 4 + channel] = (float)Decode(Math.Clamp(encodedDevice[i * 3 + channel], 0, 1));
            pixels[i * 4 + 3] = 1;
        }
        return FloatImage(width, height, pixels);
    }
    public BitmapSource ReferenceImage(BitmapSource source) => Transform == null ? source : _referenceImages.GetValue(source, ConvertReferenceImage);
    private BitmapSource ConvertReferenceImage(BitmapSource source)
    {
        if (source.Format == PixelFormats.Pbgra32)
        {
            var bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(bytes, source.PixelWidth * 4, 0);
            var convertedBytes = Transform!.FromPremultipliedSrgb(bytes);
            var convertedImage = BitmapSource.Create(source.PixelWidth, source.PixelHeight,
                source.DpiX, source.DpiY, PixelFormats.Pbgra32, null, convertedBytes, source.PixelWidth * 4);
            convertedImage.Freeze(); return convertedImage;
        }
        var image = source.Format == PixelFormats.Prgba128Float ? source : new FormatConvertedBitmap(source, PixelFormats.Prgba128Float, null, 0);
        var count = image.PixelWidth * image.PixelHeight;
        var pixels = new float[count * 4]; image.CopyPixels(pixels, image.PixelWidth * 16, 0);
        var rgb = new double[count * 3];
        for (var i = 0; i < count; i++)
        {
            var alpha = pixels[i * 4 + 3];
            for (var c = 0; c < 3; c++) rgb[i * 3 + c] = alpha > 0 ? Encode(pixels[i * 4 + c] / alpha) : 0;
        }
        var converted = Transform!.FromSrgb(rgb);
        for (var i = 0; i < count; i++)
            for (var c = 0; c < 3; c++) pixels[i * 4 + c] = (float)Decode(Math.Clamp(converted[i * 3 + c], 0, 1)) * pixels[i * 4 + 3];
        return FloatImage(image.PixelWidth, image.PixelHeight, pixels, image.DpiX, image.DpiY);
    }
    private static BitmapSource FloatImage(int width, int height, float[] pixels, double dpiX = 96, double dpiY = 96)
    {
        var bitmap = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Prgba128Float, null, pixels, width * 16);
        RenderOptions.SetBitmapScalingMode(bitmap, BitmapScalingMode.Linear); bitmap.Freeze(); return bitmap;
    }
    internal static double Encode(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    internal static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
}
