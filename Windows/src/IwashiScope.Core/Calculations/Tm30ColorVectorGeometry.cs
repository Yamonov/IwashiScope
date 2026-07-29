using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public sealed record Tm30ColorVectorPoint(double X, double Y)
{
    public double Radius => Math.Sqrt(X * X + Y * Y);
}

public sealed record Tm30ColorVectorShift(
    int Index,
    Tm30ColorVectorPoint Reference,
    Tm30ColorVectorPoint Test);

public sealed record Tm30ColorVectorGeometry(
    IReadOnlyList<Tm30ColorVectorPoint> ReferenceContour,
    IReadOnlyList<Tm30ColorVectorPoint> TestContour,
    IReadOnlyList<Tm30ColorVectorShift> Shifts)
{
    public static Tm30ColorVectorGeometry? Create(
        IReadOnlyList<Tm30HueBin> source,
        int interpolationCount = 4)
    {
        var bins = source.OrderBy(bin => bin.Index).ToArray();
        if (interpolationCount <= 0 ||
            bins.Length != 16 ||
            !bins.Select(bin => bin.Index).SequenceEqual(Enumerable.Range(1, 16)))
        {
            return null;
        }

        var polarBins = bins.Select(bin => (
            Reference: Polar(bin.ReferenceJab.Second, bin.ReferenceJab.Third),
            Test: Polar(bin.TestJab.Second, bin.TestJab.Third))).ToArray();
        if (polarBins.Any(bin =>
                !double.IsFinite(bin.Reference.Angle) ||
                !double.IsFinite(bin.Reference.Radius) ||
                !double.IsFinite(bin.Test.Angle) ||
                !double.IsFinite(bin.Test.Radius) ||
                bin.Reference.Radius <= 1e-12))
        {
            return null;
        }

        var referenceContour = new List<Tm30ColorVectorPoint>(16 * interpolationCount);
        var testContour = new List<Tm30ColorVectorPoint>(16 * interpolationCount);
        var shifts = new List<Tm30ColorVectorShift>(16);
        for (var index = 0; index < polarBins.Length; index++)
        {
            var current = polarBins[index];
            var next = polarBins[(index + 1) % polarBins.Length];
            var referenceAngles = UnwrappedPair(
                current.Reference.Angle,
                next.Reference.Angle);
            var testAngles = UnwrappedPair(current.Test.Angle, next.Test.Angle);

            for (var sample = 0; sample < interpolationCount; sample++)
            {
                var fraction = (double)sample / interpolationCount;
                var referenceAngle = Interpolate(
                    referenceAngles.First,
                    referenceAngles.Second,
                    fraction);
                var referenceRadius = Interpolate(
                    current.Reference.Radius,
                    next.Reference.Radius,
                    fraction);
                var testAngle = Interpolate(
                    testAngles.First,
                    testAngles.Second,
                    fraction);
                var testRadius = Interpolate(
                    current.Test.Radius,
                    next.Test.Radius,
                    fraction);
                if (!double.IsFinite(referenceRadius) || referenceRadius <= 1e-12)
                {
                    return null;
                }

                var normalizedReference = Cartesian(referenceAngle, 1);
                var normalizedTest = Cartesian(testAngle, testRadius / referenceRadius);
                referenceContour.Add(normalizedReference);
                testContour.Add(normalizedTest);
                if (sample == 0)
                {
                    shifts.Add(new Tm30ColorVectorShift(
                        bins[index].Index,
                        normalizedReference,
                        normalizedTest));
                }
            }
        }

        if (testContour.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            return null;
        }
        return new Tm30ColorVectorGeometry(referenceContour, testContour, shifts);
    }

    private static (double Angle, double Radius) Polar(double first, double second) =>
        (Math.Atan2(second, first), Math.Sqrt(first * first + second * second));

    private static (double First, double Second) UnwrappedPair(double first, double second)
    {
        if (second - first > Math.PI)
        {
            first += 2 * Math.PI;
        }
        else if (second - first < -Math.PI)
        {
            first -= 2 * Math.PI;
        }
        return (first, second);
    }

    private static double Interpolate(double first, double second, double fraction) =>
        (1 - fraction) * first + fraction * second;

    private static Tm30ColorVectorPoint Cartesian(double angle, double radius) =>
        new(radius * Math.Cos(angle), radius * Math.Sin(angle));
}
