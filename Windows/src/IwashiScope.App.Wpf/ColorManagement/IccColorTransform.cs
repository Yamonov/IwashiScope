using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.ColorManagement;

/// <summary>Full ICC v2/v4 matrix/TRC and LUT transforms. No intermediate RGB gamut.</summary>
public sealed class IccColorTransform : IDisposable
{
    private readonly object _gate = new();
    private nint _profile, _lab, _srgb, _fromLab, _fromSrgb, _toLab, _fromBgra;
    public string Description { get; }
    public Guid Identity { get; } = Guid.NewGuid();
    public const string IntentDescription = "Relative Colorimetric / BPC off";

    public IccColorTransform(byte[]? profileBytes)
    {
        try
        {
            _profile = profileBytes is null ? Native.cmsCreate_sRGBProfile() :
                Native.cmsOpenProfileFromMem(profileBytes, checked((uint)profileBytes.Length));
            if (_profile == 0 || Native.cmsGetColorSpace(_profile) != 0x52474220 ||
                Native.cmsGetDeviceClass(_profile) != 0x6D6E7472)
                throw new InvalidDataException("A valid RGB display ICC profile is required.");
            var description = new StringBuilder(512);
            Native.cmsGetProfileInfo(_profile, 0, "en", "US", description, 1024);
            Description = description.ToString();
            _lab = Native.cmsCreateLab4Profile(0);
            _srgb = Native.cmsCreate_sRGBProfile();
            if (_lab == 0 || _srgb == 0) throw new InvalidDataException("Cannot create ICC source profiles.");
            _fromLab = Create(_lab, Native.LabDouble, _profile, Native.RgbDouble);
            _fromSrgb = Create(_srgb, Native.RgbDouble, _profile, Native.RgbDouble);
            _toLab = Create(_profile, Native.RgbDouble, _lab, Native.LabDouble);
        }
        catch { Dispose(); throw; }
    }

    private static nint Create(nint source, uint input, nint destination, uint output)
    {
        // Relative colorimetric, no black-point compensation or perceptual mapping.
        // NOOPTIMIZE avoids a coarse intermediate CLUT; NOCACHE avoids shared 1-pixel state.
        var transform = Native.cmsCreateTransform(source, input, destination, output, 1, 0x140);
        return transform != 0 ? transform : throw new InvalidDataException("ICC transform creation failed.");
    }

    public Vector3 FromLab(Vector3 lab, string? whitePoint = "D50")
    {
        var d50 = ToD50Lab(lab, whitePoint);
        var values = Transform(_fromLab, [d50.First, d50.Second, d50.Third]);
        return new(values[0], values[1], values[2]);
    }
    public double[] FromD50Lab(double[] labs) => Transform(_fromLab, labs);
    public double[] FromSrgb(double[] encodedRgb) => Transform(_fromSrgb, encodedRgb);
    public double[] ToD50Lab(double[] deviceRgb) => Transform(_toLab, deviceRgb);
    internal byte[] FromPremultipliedSrgb(byte[] pixels)
    {
        if (pixels.Length % 4 != 0) throw new ArgumentException("BGRA pixels are required.");
        // Keep alpha outside the ICC operation. In particular, do not let the
        // optimizer interpret WPF premultiplied channel values as straight RGB.
        var straight = (byte[])pixels.Clone();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha == 255) continue;
            for (var c = 0; c < 3; c++) straight[i + c] = alpha == 0 ? (byte)0 :
                (byte)Math.Min(255, (pixels[i + c] * 255 + alpha / 2) / alpha);
        }
        var output = new byte[pixels.Length];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_profile == 0, this);
            // Chart raster colors are already 8-bit sRGB. Preserve alpha and avoid
            // expensive float/transfer-function round trips for every hover frame.
            // Measurement patches never use this reference-image path.
            if (_fromBgra == 0)
            {
                _fromBgra = Native.cmsCreateTransform(_srgb, Native.Bgra,
                    _profile, Native.Bgra, 1, 0x04000440);
                if (_fromBgra == 0) throw new InvalidDataException("Reference ICC transform creation failed.");
            }
            Native.cmsDoTransformBytes(_fromBgra, straight, output, checked((uint)(pixels.Length / 4)));
        }
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3]; output[i + 3] = alpha;
            if (alpha == 255) continue;
            for (var c = 0; c < 3; c++) output[i + c] = (byte)((output[i + c] * alpha + 127) / 255);
        }
        return output;
    }

    private double[] Transform(nint transform, double[] input)
    {
        if (input.Length % 3 != 0 || input.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Finite RGB/Lab triples are required.", nameof(input));
        var output = new double[input.Length];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_profile == 0, this);
            Native.cmsDoTransform(transform, input, output, checked((uint)(input.Length / 3)));
        }
        if (output.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("The ICC transform produced non-finite output.");
        return output;
    }

    internal static Vector3 ToD50Lab(Vector3 lab, string? whitePoint)
    {
        if (!lab.IsFinite) throw new ArgumentException("Lab must be finite.", nameof(lab));
        if (string.IsNullOrWhiteSpace(whitePoint) || whitePoint.Contains("D50", StringComparison.OrdinalIgnoreCase)) return lab;
        if (!whitePoint.Contains("D65", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only D50 and D65 Lab are supported.", nameof(whitePoint));
        var fy = (lab.First + 16) / 116;
        var x = 0.95047 * Inverse(fy + lab.Second / 500);
        var y = Inverse(fy);
        var z = 1.08883 * Inverse(fy - lab.Third / 200);
        // Bradford D65 -> ICC D50; no sRGB encoding or clipping.
        var dx = 1.0478112 * x + 0.0228866 * y - 0.0501270 * z;
        var dy = 0.0295424 * x + 0.9904844 * y - 0.0170491 * z;
        var dz = -0.0092345 * x + 0.0150436 * y + 0.7521316 * z;
        var fx = Forward(dx / 0.96422); fy = Forward(dy); var fz = Forward(dz / 0.82521);
        return new(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        static double Inverse(double v) => v > 6d / 29 ? v * v * v : 3 * Math.Pow(6d / 29, 2) * (v - 4d / 29);
        static double Forward(double v) => v > Math.Pow(6d / 29, 3) ? Math.Cbrt(v) : v / (3 * Math.Pow(6d / 29, 2)) + 4d / 29;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var transform in new[] { _fromLab, _fromSrgb, _toLab, _fromBgra })
                if (transform != 0) Native.cmsDeleteTransform(transform);
            foreach (var profile in new[] { _profile, _lab, _srgb })
                if (profile != 0) Native.cmsCloseProfile(profile);
            _profile = _lab = _srgb = _fromLab = _fromSrgb = _toLab = _fromBgra = 0;
        }
    }

    internal static class Native
    {
        internal const string Dll = "IwashiScope.DisplayColor.dll";
        internal const uint LabDouble = (1u << 22) | (10u << 16) | (3u << 3);
        internal const uint RgbDouble = (1u << 22) | (4u << 16) | (3u << 3);
        internal const uint Bgra = (4u << 16) | (1u << 7) | (3u << 3) | 1u | (1u << 10) | (1u << 14);
        [DllImport(Dll)] internal static extern nint cmsOpenProfileFromMem(byte[] memory, uint size);
        [DllImport(Dll)] internal static extern nint cmsCreate_sRGBProfile();
        [DllImport(Dll)] internal static extern nint cmsCreateLab4Profile(nint white);
        [DllImport(Dll)] internal static extern uint cmsGetColorSpace(nint profile);
        [DllImport(Dll)] internal static extern uint cmsGetDeviceClass(nint profile);
        [DllImport(Dll, CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern uint cmsGetProfileInfo(nint profile, uint info,
            [MarshalAs(UnmanagedType.LPStr)] string language, [MarshalAs(UnmanagedType.LPStr)] string country, StringBuilder result, uint length);
        [DllImport(Dll)] internal static extern nint cmsCreateTransform(nint source, uint input, nint destination, uint output, uint intent, uint flags);
        [DllImport(Dll)] internal static extern void cmsDoTransform(nint transform, double[] input, [Out] double[] output, uint count);
        [DllImport(Dll, EntryPoint = "cmsDoTransform")] internal static extern void cmsDoTransformBytes(nint transform, byte[] input, [Out] byte[] output, uint count);
        [DllImport(Dll)] internal static extern void cmsDeleteTransform(nint transform);
        [DllImport(Dll)] internal static extern int cmsCloseProfile(nint profile);
    }
}
