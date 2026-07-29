/*
 The CIE 1995 CRI calculation in this file is adapted from ArgyllCMS 3.5.0
 xicc/xspect.c, function icx_CIE1995_CRI(), written by Graeme W. Gill,
 Copyright (C) 2000-2006, GPL-2.0-or-later.

 Adapted by IwashiScope to calculate R15 from 5 nm resampled spectra.
 IwashiScope modifications are AGPL-3.0-only. See THIRD_PARTY_NOTICES.md.
*/

using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public static class ColorRenderingIndexCalculator
{
    private sealed record Xyz(double X, double Y, double Z)
    {
        public Xyz Divide(double divisor) => new(X / divisor, Y / divisor, Z / divisor);
    }

    private sealed record Yuv(double Y, double U, double V);
    private sealed record Ycd(double Y, double C, double D);
    private sealed record Wuv(double W, double U, double V);

    private static readonly double[] Wavelengths = Enumerable
        .Range(0, ColorRenderingReferenceData.SampleCount)
        .Select(index =>
            ColorRenderingReferenceData.StartWavelength +
            index * ColorRenderingReferenceData.Interval)
        .ToArray();

    public static double? R15(IReadOnlyList<SpectralSample> spectrum, double cct)
    {
        var ordered = spectrum.OrderBy(sample => sample.Wavelength).ToArray();
        if (ordered.Length < 2 ||
            ordered[0].Wavelength > 400 ||
            ordered[^1].Wavelength < 700 ||
            !double.IsFinite(cct) ||
            cct is < 1 or > 25_000)
        {
            return null;
        }

        var referenceIlluminant = ReferenceIlluminant(cct);
        if (referenceIlluminant is null)
        {
            return null;
        }
        var testIlluminant = Wavelengths
            .Select(wavelength => InterpolatedValue(ordered, wavelength))
            .ToArray();

        var testWhiteRaw = Tristimulus(testIlluminant);
        var referenceWhiteRaw = Tristimulus(referenceIlluminant);
        if (testWhiteRaw is null ||
            referenceWhiteRaw is null ||
            testWhiteRaw.Y <= 1e-12 ||
            referenceWhiteRaw.Y <= 1e-12)
        {
            return null;
        }

        var testWhite = testWhiteRaw.Divide(testWhiteRaw.Y);
        var referenceWhite = referenceWhiteRaw.Divide(referenceWhiteRaw.Y);
        var testWhiteYuv = Cie1960Yuv(testWhite);
        var referenceWhiteYuv = Cie1960Yuv(referenceWhite);
        var testWhiteYcd = testWhiteYuv is null ? null : ToYcd(testWhiteYuv);
        var referenceWhiteYcd = referenceWhiteYuv is null ? null : ToYcd(referenceWhiteYuv);
        if (testWhiteYcd is null ||
            referenceWhiteYcd is null ||
            Math.Abs(testWhiteYcd.C) <= 1e-12 ||
            Math.Abs(testWhiteYcd.D) <= 1e-12)
        {
            return null;
        }

        var cAdaptation = referenceWhiteYcd.C / testWhiteYcd.C;
        var dAdaptation = referenceWhiteYcd.D / testWhiteYcd.D;
        var referenceColorRaw = Tristimulus(referenceIlluminant, ColorRenderingReferenceData.Tcs15);
        var testColorRaw = Tristimulus(testIlluminant, ColorRenderingReferenceData.Tcs15);
        if (referenceColorRaw is null || testColorRaw is null)
        {
            return null;
        }

        var referenceColor = referenceColorRaw.Divide(referenceWhiteRaw.Y);
        var testColor = testColorRaw.Divide(testWhiteRaw.Y);
        var referenceWuv = Cie1964Wuv(referenceColor, referenceWhite);
        var testColorYuv = Cie1960Yuv(testColor);
        var testColorYcd = testColorYuv is null ? null : ToYcd(testColorYuv);
        if (referenceWuv is null || testColorYuv is null || testColorYcd is null)
        {
            return null;
        }

        var denominator =
            16.518 +
            1.481 * testColorYcd.C * cAdaptation -
            testColorYcd.D * dAdaptation;
        if (Math.Abs(denominator) <= 1e-12)
        {
            return null;
        }

        var adaptedTestColor = new Yuv(
            testColorYuv.Y,
            (10.872 +
             0.404 * testColorYcd.C * cAdaptation -
             4 * testColorYcd.D * dAdaptation) / denominator,
            5.520 / denominator);
        var testWuv = Cie1964Wuv(adaptedTestColor, referenceWhite);
        if (testWuv is null)
        {
            return null;
        }

        var deltaW = referenceWuv.W - testWuv.W;
        var deltaU = referenceWuv.U - testWuv.U;
        var deltaV = referenceWuv.V - testWuv.V;
        var deltaE = Math.Sqrt(deltaW * deltaW + deltaU * deltaU + deltaV * deltaV);
        var value = 100 - 4.6 * deltaE;
        return double.IsFinite(value) ? value : null;
    }

    public static SpotMeasurement AddR15(SpotMeasurement measurement)
    {
        if (measurement.Cri is not { } cri ||
            cri.Individual.ContainsKey(15) ||
            measurement.Cct is not { } cct ||
            R15(measurement.Spectrum, cct) is not { } r15)
        {
            return measurement;
        }

        var individual = cri.Individual.ToDictionary(pair => pair.Key, pair => pair.Value);
        individual[15] = r15;
        return measurement with
        {
            Cri = cri with { Individual = individual },
        };
    }

    private static double[]? ReferenceIlluminant(double cct) =>
        cct < 5_000 ? PlanckianIlluminant(cct) : DaylightIlluminant(cct);

    private static double[] PlanckianIlluminant(double cct)
    {
        const double secondRadiationConstant = 1.4388e-2;
        const double normalizationWavelength = 560e-9;
        var normalization =
            Math.Pow(normalizationWavelength, -5) /
            (Math.Exp(secondRadiationConstant / (normalizationWavelength * cct)) - 1);
        return Wavelengths.Select(wavelength =>
        {
            var meters = wavelength * 1e-9;
            var spectralPower =
                Math.Pow(meters, -5) /
                (Math.Exp(secondRadiationConstant / (meters * cct)) - 1);
            return spectralPower / normalization;
        }).ToArray();
    }

    private static double[]? DaylightIlluminant(double cct)
    {
        if (cct is < 5_000 or > 25_000)
        {
            return null;
        }

        var x = cct < 7_000
            ? -4.6070e9 / Math.Pow(cct, 3) +
              2.9678e6 / Math.Pow(cct, 2) +
              0.09911e3 / cct +
              0.244063
            : -2.0064e9 / Math.Pow(cct, 3) +
              1.9018e6 / Math.Pow(cct, 2) +
              0.24748e3 / cct +
              0.237040;
        var y = -3 * x * x + 2.87 * x - 0.275;
        var denominator = 0.25539 * x - 0.73217 * y + 0.02387;
        if (Math.Abs(denominator) <= 1e-12)
        {
            return null;
        }
        var m1 = (-1.77861 * x + 5.90757 * y - 1.34674) / denominator;
        var m2 = (-31.44464 * x + 30.06400 * y + 0.03638) / denominator;
        return ColorRenderingReferenceData.DaylightS0.Indices()
            .Select(index =>
                ColorRenderingReferenceData.DaylightS0[index] +
                m1 * ColorRenderingReferenceData.DaylightS1[index] +
                m2 * ColorRenderingReferenceData.DaylightS2[index])
            .ToArray();
    }

    private static Xyz? Tristimulus(double[] illuminant, double[]? reflectance = null)
    {
        if (illuminant.Length != ColorRenderingReferenceData.SampleCount ||
            reflectance is not null &&
            reflectance.Length != ColorRenderingReferenceData.SampleCount)
        {
            return null;
        }

        var x = 0d;
        var y = 0d;
        var z = 0d;
        for (var index = 0; index < illuminant.Length; index++)
        {
            var power = illuminant[index] * (reflectance?[index] ?? 1);
            x += power * ColorRenderingReferenceData.XBar[index];
            y += power * ColorRenderingReferenceData.YBar[index];
            z += power * ColorRenderingReferenceData.ZBar[index];
        }
        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
            ? new Xyz(x, y, z)
            : null;
    }

    private static Yuv? Cie1960Yuv(Xyz xyz)
    {
        var denominator = xyz.X + 15 * xyz.Y + 3 * xyz.Z;
        return Math.Abs(denominator) <= 1e-12
            ? null
            : new Yuv(xyz.Y, 4 * xyz.X / denominator, 6 * xyz.Y / denominator);
    }

    private static Ycd? ToYcd(Yuv yuv) =>
        Math.Abs(yuv.V) <= 1e-12
            ? null
            : new Ycd(
                yuv.Y,
                (4 - yuv.U - 10 * yuv.V) / yuv.V,
                (1.708 * yuv.V - 1.481 * yuv.U + 0.404) / yuv.V);

    private static Wuv? Cie1964Wuv(Xyz xyz, Xyz white)
    {
        var yuv = Cie1960Yuv(xyz);
        return yuv is null ? null : Cie1964Wuv(yuv, white);
    }

    private static Wuv? Cie1964Wuv(Yuv yuv, Xyz white)
    {
        var whiteYuv = Cie1960Yuv(white);
        if (whiteYuv is null || whiteYuv.Y <= 1e-12 || yuv.Y < 0)
        {
            return null;
        }
        var w = 25 * Math.Pow(yuv.Y * 100 / whiteYuv.Y, 1.0 / 3.0) - 17;
        return new Wuv(
            w,
            13 * w * (yuv.U - whiteYuv.U),
            13 * w * (yuv.V - whiteYuv.V));
    }

    private static double InterpolatedValue(SpectralSample[] samples, double wavelength)
    {
        if (wavelength <= samples[0].Wavelength)
        {
            return samples[0].Value;
        }
        if (wavelength >= samples[^1].Wavelength)
        {
            return samples[^1].Value;
        }

        var lower = 0;
        var upper = samples.Length - 1;
        while (upper - lower > 1)
        {
            var midpoint = (lower + upper) / 2;
            if (samples[midpoint].Wavelength <= wavelength)
            {
                lower = midpoint;
            }
            else
            {
                upper = midpoint;
            }
        }

        var width = samples[upper].Wavelength - samples[lower].Wavelength;
        if (width <= 0)
        {
            return samples[lower].Value;
        }
        var fraction = (wavelength - samples[lower].Wavelength) / width;
        return samples[lower].Value +
               (samples[upper].Value - samples[lower].Value) * fraction;
    }

    private static IEnumerable<int> Indices(this Array array) =>
        Enumerable.Range(0, array.Length);
}
