using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class CieCam16Tests
{
    // Same independent Colour 0.4.7 CIECAM16 values used by the macOS suite.
    public static IEnumerable<object[]> References()
    {
        yield return ["published-example", new Vector3(19.01,20,21.78), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 318.31, 20, AdaptationDegreeOverride: null), new double[] { 41.73120790512664,195.37170899282242,0.10335573870906986,0.10743677233590453,2.3450150729795514,217.067959767393 }];
        yield return ["dark-response-extension", new Vector3(0.0019,0.002,0.0021), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 20, 20, AdaptationDegreeOverride: null), new double[] { 0.005737722462536499,1.294752188498241,0.003288912727132853,0.002714682597932648,4.578953177103232,132.15239464062964 }];
        yield return ["red", new Vector3(41.24,21.26,1.93), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 20, 20, AdaptationDegreeOverride: null), new double[] { 46.16068525962413,116.13229504300234,113.00319604569215,93.27332017211664,89.61943750297647,27.411761971313314 }];
        yield return ["blue", new Vector3(18.05,7.22,95.05), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 20, 20, AdaptationDegreeOverride: null), new double[] { 25.176524693189016,85.76592493600417,86.58011588552816,71.46359706732989,91.28197927529834,282.7862752800284 }];
        yield return ["d50-white", new Vector3(96.42,100,82.49), new AppearanceViewingConditions(new Vector3(96.42,100,82.49), 20, 20, AdaptationDegreeOverride: null), new double[] { 100,170.95847129299287,2.0444104032684867,1.6874650698389428,9.935096345586933,120.43330310713041 }];
        yield return ["warm-white", new Vector3(109.85,100,35.585), new AppearanceViewingConditions(new Vector3(109.85,100,35.585), 20, 20, AdaptationDegreeOverride: null), new double[] { 100,171.18118259239156,5.969903759897662,4.92758403548609,16.966372921764076,62.407530784231625 }];
        yield return ["upper-response-extension", new Vector3(250,200,300), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 20, 20, AdaptationDegreeOverride: null), new double[] { 152.6786763875107,211.20591570143674,75.01261571179164,61.91573306833575,54.143642627248255,337.44469815962407 }];
        yield return ["no-adaptation", new Vector3(20,15,4), new AppearanceViewingConditions(new Vector3(109.85,100,35.585), 20, 20, AdaptationDegreeOverride: 0), new double[] { 36.039544136063455,103.2450224513288,41.80939287462644,34.50965126880149,57.81436018448623,45.78833007908955 }];
        yield return ["half-adaptation", new Vector3(20,15,4), new AppearanceViewingConditions(new Vector3(109.85,100,35.585), 20, 20, AdaptationDegreeOverride: 0.5), new double[] { 36.00270233395984,102.94669304660869,30.54366745022796,25.210873435507356,49.48661475277476,34.67568180283519 }];
        yield return ["full-adaptation", new Vector3(20,15,4), new AppearanceViewingConditions(new Vector3(109.85,100,35.585), 20, 20, AdaptationDegreeOverride: 1), new double[] { 35.96975419747751,102.56343573032642,22.07235073661561,18.218612475129948,42.14648580059901,21.27876376081173 }];
        yield return ["dim-field", new Vector3(12,15,22), new AppearanceViewingConditions(new Vector3(95.05,100,108.88), 1, 10, AdaptationDegreeOverride: null), new double[] { 37.061376574000924,64.04229825722787,29.13235393410123,18.74757232015992,54.10520717873839,211.94749506829615 }];
    }

    [Theory]
    [MemberData(nameof(References))]
    public void MatchesIndependentReferenceAndInverse(
        string name, Vector3 xyz, AppearanceViewingConditions conditions, double[] expected)
    {
        var model = new CieCam16(conditions);
        var result = model.Forward(xyz);
        Assert.NotNull(result.Hue);
        double[] actual = [result.Lightness, result.Brightness, result.Chroma,
            result.Colorfulness, result.Saturation, result.Hue.Value];
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-8, $"{name}: component {i}, {actual[i]} vs {expected[i]}");
        AssertVector(xyz, model.Inverse(result));
    }

    [Fact]
    public void BlackHasNoHueAndRoundTrips()
    {
        var model = new CieCam16(new(new Vector3(96.42, 100, 82.49)));
        var black = new Vector3(0, 0, 0);
        Assert.Equal(AppearanceCorrelates.Black, model.Forward(black));
        Assert.Equal(black, model.Inverse(AppearanceCorrelates.Black));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(270d)]
    [InlineData(359.999)]
    public void InverseIsStableAtHueAxes(double hue)
    {
        var model = new CieCam16(new(new Vector3(96.42, 100, 82.49)));
        var result = model.Forward(model.Inverse(100, 10, hue));
        Assert.InRange(Math.Abs(result.Brightness - 100), 0, 1e-8);
        Assert.InRange(Math.Abs(result.Colorfulness - 10), 0, 1e-8);
        var difference = Math.Abs(result.Hue!.Value - hue);
        Assert.InRange(Math.Min(difference, Math.Abs(difference - 360)), 0, 1e-8);
    }

    [Fact]
    public void InvalidConditionsAndInputsAreExplicitErrors()
    {
        AssertError(ColorAppearanceError.InvalidViewingConditions,
            () => new CieCam16(new(new Vector3(0.9642, 1, 0.8249))));
        AssertError(ColorAppearanceError.InvalidViewingConditions,
            () => new CieCam16(new(new Vector3(96.42, 100, 82.49), AdaptingLuminance: 0)));
        AssertError(ColorAppearanceError.InvalidViewingConditions,
            () => new CieCam16(new(new Vector3(96.42, 100, 82.49), AdaptationDegreeOverride: 1.1)));
        var model = new CieCam16(new(new Vector3(96.42, 100, 82.49)));
        AssertError(ColorAppearanceError.InvalidStimulus, () => model.Forward(new(double.NaN, 20, 10)));
        AssertError(ColorAppearanceError.InvalidStimulus, () => model.Inverse(100, 10, null));
        AssertError(ColorAppearanceError.OutsideModelDomain, () => model.Inverse(0, 10, 30));
    }

    [Fact]
    public void ReproductionPreservesBrightnessColorfulnessAndSaturation()
    {
        var source = new CieCam16(new(new Vector3(109.85, 100, 35.585)));
        var destination = new CieCam16(new(new Vector3(96.42, 100, 82.49)));
        var predicted = source.Forward(new(40, 25, 5));
        var reproduced = destination.Forward(destination.Inverse(predicted));
        Assert.InRange(Math.Abs(reproduced.Brightness - predicted.Brightness), 0, 1e-8);
        Assert.InRange(Math.Abs(reproduced.Colorfulness - predicted.Colorfulness), 0, 1e-8);
        Assert.InRange(Math.Abs(reproduced.Saturation - predicted.Saturation), 0, 1e-8);
        Assert.InRange(Math.Abs(reproduced.Hue!.Value - predicted.Hue!.Value), 0, 1e-8);
    }

    private static void AssertError(ColorAppearanceError error, Action action) =>
        Assert.Equal(error, Assert.Throws<ColorAppearanceException>(action).Error);

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Math.Abs(actual.First - expected.First), 0, 1e-8);
        Assert.InRange(Math.Abs(actual.Second - expected.Second), 0, 1e-8);
        Assert.InRange(Math.Abs(actual.Third - expected.Third), 0, 1e-8);
    }
}
