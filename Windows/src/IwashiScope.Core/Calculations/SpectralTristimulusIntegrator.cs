using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public static class SpectralTristimulusIntegrator
{
    public sealed record IntegrationResult(Vector3 ObjectXyz, Vector3 WhiteXyz, IReadOnlyList<double> Wavelengths);
    private static readonly double[] DefaultWavelengths = Enumerable.Range(0, 71).Select(i => 380d + i * 5).ToArray();

    public static IntegrationResult? Integrate(
        IReadOnlyList<SpectralSample> reflectance,
        IReadOnlyList<SpectralSample> illuminant,
        IReadOnlyList<double>? wavelengths = null)
    {
        var orderedReflectance = reflectance
            .Where(s => double.IsFinite(s.Wavelength) && double.IsFinite(s.Value))
            .OrderBy(s => s.Wavelength).ToArray();
        var orderedIlluminant = illuminant
            .Where(s => double.IsFinite(s.Wavelength) && double.IsFinite(s.Value) && s.Value >= 0)
            .OrderBy(s => s.Wavelength).ToArray();
        if (orderedReflectance.Length < 2 || orderedIlluminant.Length < 2 ||
            orderedReflectance.Zip(orderedReflectance.Skip(1)).Any(p => p.First.Wavelength >= p.Second.Wavelength) ||
            orderedIlluminant.Zip(orderedIlluminant.Skip(1)).Any(p => p.First.Wavelength >= p.Second.Wavelength))
            return null;

        double whiteX = 0, whiteY = 0, whiteZ = 0, objectX = 0, objectY = 0, objectZ = 0;
        var used = new List<double>();
        foreach (var wavelength in wavelengths ?? DefaultWavelengths)
        {
            var source = ReflectanceIlluminantSpectrumCalculator.Interpolate(orderedIlluminant, wavelength);
            var reflected = ReflectanceIlluminantSpectrumCalculator.Interpolate(orderedReflectance, wavelength);
            var observer = ObserverValues(wavelength);
            if (source is null || reflected is null || observer is null) continue;
            var scaledReflectance = Math.Max(0, reflected.Value) / 100;
            whiteX += source.Value * observer.Value.X;
            whiteY += source.Value * observer.Value.Y;
            whiteZ += source.Value * observer.Value.Z;
            objectX += source.Value * scaledReflectance * observer.Value.X;
            objectY += source.Value * scaledReflectance * observer.Value.Y;
            objectZ += source.Value * scaledReflectance * observer.Value.Z;
            used.Add(wavelength);
        }
        if (used.Count < 2 || !double.IsFinite(whiteY) || whiteY <= 1e-12) return null;
        var normalization = 100 / whiteY;
        var white = new Vector3(whiteX * normalization, 100, whiteZ * normalization);
        var value = new Vector3(objectX * normalization, objectY * normalization, objectZ * normalization);
        return white.IsFinite && value.IsFinite ? new IntegrationResult(value, white, used.ToArray()) : null;
    }

    private static (double X, double Y, double Z)? ObserverValues(double wavelength)
    {
        var indexValue = (wavelength - ColorRenderingReferenceData.StartWavelength) / ColorRenderingReferenceData.Interval;
        if (!double.IsFinite(indexValue)) return null;
        var index = (int)Math.Round(indexValue);
        if (Math.Abs(indexValue - index) >= 1e-9 || index < 0 ||
            index >= ColorRenderingReferenceData.XBar.Length ||
            index >= ColorRenderingReferenceData.YBar.Length ||
            index >= ColorRenderingReferenceData.ZBar.Length) return null;
        return (ColorRenderingReferenceData.XBar[index], ColorRenderingReferenceData.YBar[index], ColorRenderingReferenceData.ZBar[index]);
    }
}
