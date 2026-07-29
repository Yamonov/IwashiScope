/*
 SPDX-FileCopyrightText: 2026 Yamonov
 SPDX-License-Identifier: GPL-3.0-only

 Adapted from the CIE D50 and D65 official datasets distributed in
 ThirdParty/CIE by the macOS v0.9.4 source. Original data: CC BY-SA 4.0.
 Values are the unchanged 380...730 nm, 5 nm samples. See
 THIRD_PARTY_NOTICES.md and the macOS source's ThirdParty/CIE/README.md.
*/
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public enum CieStandardIlluminant
{
    D50,
    D65,
}

public static class CieStandardIlluminants
{
    public const double StartWavelength = 380;
    public const double EndWavelength = 730;
    public const double Interval = 5;
    public const double NormalizationWavelength = 560;

    private static readonly double[] D50Values =
    [
        24.4875, 27.179, 29.8706, 39.5894, 49.3081, 52.9104, 56.5128, 58.2733,
        60.0338, 58.9256, 57.8175, 66.3212, 74.8249, 81.036, 87.2472, 88.9297,
        90.6122, 90.9902, 91.3681, 93.2383, 95.1085, 93.5356, 91.9627, 93.8432,
        95.7237, 96.1685, 96.6133, 96.8712, 97.129, 99.614, 102.099, 101.427,
        100.755, 101.536, 102.317, 101.158, 100, 98.8675, 97.735, 98.3265,
        98.918, 96.2084, 93.4988, 95.5933, 97.6878, 98.4784, 99.2691, 99.1553,
        99.0415, 97.3816, 95.7218, 97.2895, 98.8572, 97.2622, 95.6672, 96.9285,
        98.1898, 100.597, 103.003, 101.068, 99.133, 93.257, 87.3809, 89.4922,
        91.6035, 92.246, 92.8886, 84.8715, 76.8544, 81.6828, 86.5112,
    ];

    private static readonly double[] D65Values =
    [
        49.9755, 52.3118, 54.6482, 68.7015, 82.7549, 87.1204, 91.486, 92.4589,
        93.4318, 90.057, 86.6823, 95.7736, 104.865, 110.936, 117.008, 117.41,
        117.812, 116.336, 114.861, 115.392, 115.923, 112.367, 108.811, 109.082,
        109.354, 108.578, 107.802, 106.296, 104.79, 106.239, 107.689, 106.047,
        104.405, 104.225, 104.046, 102.023, 100, 98.1671, 96.3342, 96.0611,
        95.788, 92.2368, 88.6856, 89.3459, 90.0062, 89.8026, 89.5991, 88.6489,
        87.6987, 85.4936, 83.2886, 83.4939, 83.6992, 81.863, 80.0268, 80.1207,
        80.2146, 81.2462, 82.2778, 80.281, 78.2842, 74.0027, 69.7213, 70.6652,
        71.6091, 72.979, 74.349, 67.9765, 61.604, 65.7448, 69.8856,
    ];

    public static IReadOnlyList<SpectralSample> Samples(CieStandardIlluminant illuminant)
    {
        var values = illuminant == CieStandardIlluminant.D50 ? D50Values : D65Values;
        return values.Select((value, index) =>
            new SpectralSample(index, StartWavelength + index * Interval, value)).ToArray();
    }

    public static IReadOnlyList<SpectralSample> ScaleToMeasurement(
        CieStandardIlluminant illuminant,
        IReadOnlyList<SpectralSample> measured,
        double wavelength = NormalizationWavelength)
    {
        var reference = Samples(illuminant);
        var measuredValue = Interpolate(measured, wavelength);
        var referenceValue = Interpolate(reference, wavelength);
        if (measuredValue is not { } measurement ||
            referenceValue is not { } referenceMeasurement ||
            measurement <= 0 ||
            referenceMeasurement <= 0)
        {
            return [];
        }

        var scale = measurement / referenceMeasurement;
        return reference.Select(sample => sample with { Value = sample.Value * scale }).ToArray();
    }

    public static double? Interpolate(IReadOnlyList<SpectralSample> samples, double wavelength)
    {
        var ordered = samples.OrderBy(sample => sample.Wavelength).ToArray();
        if (ordered.Length == 0 ||
            wavelength < ordered[0].Wavelength ||
            wavelength > ordered[^1].Wavelength)
        {
            return null;
        }
        var exact = ordered.FirstOrDefault(sample => sample.Wavelength == wavelength);
        if (exact is not null)
        {
            return exact.Value;
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Wavelength < wavelength)
            {
                continue;
            }
            var lower = ordered[index - 1];
            var upper = ordered[index];
            var fraction = (wavelength - lower.Wavelength) /
                (upper.Wavelength - lower.Wavelength);
            return lower.Value + (upper.Value - lower.Value) * fraction;
        }
        return null;
    }
}
