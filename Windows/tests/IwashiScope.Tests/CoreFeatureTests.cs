using System.Buffers.Binary;
using System.Text;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Export;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Core.Workspace;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.Tests;

public sealed class CoreFeatureTests
{
    [Fact]
    public void MeasurementModeArgumentsMatchMacContract()
    {
        Assert.Equal(
            ["-J", "-v", "-s", "-H", "-c", "2"],
            MeasurementMode.Reflectance.SpotreadArguments(2));
        Assert.Equal(
            ["-J", "-v", "-s", "-H", "-a", "-c", "1"],
            MeasurementMode.Ambient.SpotreadArguments());
        Assert.Equal(
            ["-J", "-v", "-s", "-H", "-e", "-T", "-c", "1"],
            MeasurementMode.Emissive.SpotreadArguments());
    }

    [Fact]
    public void HistorySupportsRangeSelectionRenameReorderAndDelete()
    {
        var history = new MeasurementHistory();
        var first = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 1), "A");
        var second = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 2), "B");
        var third = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 3), "C");

        history.SelectExclusive(first.Id);
        history.SelectRange(second.Id);
        Assert.Equal(2, history.SelectedIds.Count);
        history.Rename(second.Id, "Renamed");
        history.MoveSelectionBefore(MeasurementMode.Reflectance, third.Id);
        Assert.Equal(["A", "Renamed", "C"], history.Ordered(MeasurementMode.Reflectance).Select(x => x.Name));
        history.Toggle(first.Id);
        var removed = history.DeleteSelected();
        Assert.Single(removed);
        Assert.Equal("Renamed", removed[0].Name);
    }

    [Fact]
    public void SessionStateMachineCoversMeasurementAndSingleRecovery()
    {
        var state = new MeasurementSessionStateMachine(MeasurementMode.Ambient);
        state.Start(MeasurementMode.Ambient);
        state.Apply(new HelloAcceptedEvent(3, "IwashiScope spot reader", 1, "3.5.0"));
        state.Apply(new MeasurementPromptEvent());
        Assert.Equal(MeasurementSessionPhase.Ready, state.Phase);
        state.Apply(new MeasurementStartedEvent());
        Assert.Equal(MeasurementSessionPhase.Measuring, state.Phase);
        state.Apply(new MeasurementCompletedEvent(TestMeasurementFactory.Create(MeasurementMode.Ambient)));
        Assert.Equal(MeasurementSessionPhase.Workspace, state.Phase);
        Assert.True(state.TryBeginAutomaticRecovery());
        Assert.Equal(MeasurementSessionPhase.Recovering, state.Phase);
        Assert.False(state.TryBeginAutomaticRecovery());
        Assert.Equal(MeasurementSessionPhase.Failed, state.Phase);
    }

    [Fact]
    public void Tm30SampleFidelityMatchesReferenceFormula()
    {
        var reference = new Vector3(50, 20, -10);
        Assert.Equal(100, Tm30SampleFidelity.Score(reference, reference));
        var test = new Vector3(51, 20, -10);
        var expected = Math.Clamp(
            10 * Math.Log(1 + Math.Exp(Math.Max(100 - 7.54, 0) / 10)),
            0,
            100);
        Assert.Equal(expected, Tm30SampleFidelity.Score(reference, test)!.Value, 10);
    }

    [Fact]
    public void Tm30VectorGeometryNormalizesAndInterpolatesSixteenHueBins()
    {
        var tm30 = TestMeasurementFactory.Create(MeasurementMode.Ambient).Tm30!;

        var geometry = Tm30ColorVectorGeometry.Create(tm30.HueBins);

        Assert.NotNull(geometry);
        Assert.Equal(64, geometry.ReferenceContour.Count);
        Assert.Equal(64, geometry.TestContour.Count);
        Assert.Equal(16, geometry.Shifts.Count);
        Assert.All(
            geometry.ReferenceContour,
            point => Assert.Equal(1, point.Radius, 10));
        Assert.All(
            geometry.TestContour,
            point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            });
    }

    [Fact]
    public void LabD50WhiteMapsNearSrgbWhite()
    {
        var color = LabColorConverter.D50LabToSrgb(new Vector3(100, 0, 0));
        Assert.InRange(color.Red, 0.995, 1.005);
        Assert.InRange(color.Green, 0.995, 1.005);
        Assert.InRange(color.Blue, 0.995, 1.005);
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(5, -5, 5)]
    [InlineData(5.01, 0, 10)]
    [InlineData(-10, 3, 10)]
    [InlineData(10.01, 0, 25)]
    [InlineData(25, -20, 25)]
    [InlineData(25.01, 0, 50)]
    [InlineData(-50, 40, 50)]
    [InlineData(50.01, 0, 100)]
    [InlineData(120, -110, 100)]
    public void LabChartScaleUsesFiveCleanRanges(double a, double b, double expected)
    {
        Assert.Equal(expected, LabABChartScale.ResolveLimit(a, b));
    }

    [Fact]
    public void R15MatchesItsReferenceIlluminantAndRequiresCoverage()
    {
        const double cct = 4500;
        const double c2 = 1.4388e-2;
        const double normalizationWavelength = 560e-9;
        var normalization =
            Math.Pow(normalizationWavelength, -5) /
            (Math.Exp(c2 / (normalizationWavelength * cct)) - 1);
        var spectrum = Enumerable.Range(0, 95)
            .Select(index =>
            {
                var wavelength = 360d + index * 5;
                var meters = wavelength * 1e-9;
                var value = Math.Pow(meters, -5) /
                            (Math.Exp(c2 / (meters * cct)) - 1) /
                            normalization;
                return new SpectralSample(index, wavelength, value);
            })
            .ToArray();

        Assert.Equal(100, ColorRenderingIndexCalculator.R15(spectrum, cct)!.Value, 8);
        Assert.Null(
            ColorRenderingIndexCalculator.R15(
                spectrum.Where(sample => sample.Wavelength > 400).ToArray(),
                cct));
        Assert.Null(ColorRenderingIndexCalculator.R15(spectrum, 25_001));
    }

    [Fact]
    public void SrgbAdobeRgbAndDisplayP3ConversionsAndGamutWarningsAreIndependent()
    {
        var srgbOnly = LabColorConverter.Convert(new Vector3(20, -35, 20), "D50");
        Assert.True(srgbOnly.Srgb.IsOutOfGamut);
        Assert.False(srgbOnly.AdobeRgb.IsOutOfGamut);
        Assert.False(srgbOnly.DisplayP3.IsOutOfGamut);

        var adobeOnlyComparedWithP3 =
            LabColorConverter.Convert(new Vector3(20, -35, 30), "D50");
        Assert.True(adobeOnlyComparedWithP3.AdobeRgb.IsOutOfGamut);
        Assert.False(adobeOnlyComparedWithP3.DisplayP3.IsOutOfGamut);

        var p3OnlyComparedWithAdobe =
            LabColorConverter.Convert(new Vector3(20, -45, 25), "D50");
        Assert.False(p3OnlyComparedWithAdobe.AdobeRgb.IsOutOfGamut);
        Assert.True(p3OnlyComparedWithAdobe.DisplayP3.IsOutOfGamut);

        Assert.NotEqual(
            adobeOnlyComparedWithP3.Srgb.RgbDescription,
            adobeOnlyComparedWithP3.DisplayP3.RgbDescription);
        Assert.StartsWith("#", adobeOnlyComparedWithP3.Srgb.Hex);
    }

    [Fact]
    public void CsvUsesCrLfAndInvariantNumbers()
    {
        var measurement = TestMeasurementFactory.Create(MeasurementMode.Ambient);
        var csv = Encoding.UTF8.GetString(MeasurementCsvEncoder.Lighting(measurement));
        Assert.Contains("\r\n", csv);
        Assert.DoesNotContain("\n", csv.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.Contains("TM-30-15 evaluation sample,99", csv);
        Assert.Contains("CRI,R15", csv);
    }

    [Fact]
    public void AseIsBigEndianLabSpotColor()
    {
        var data = AdobeSwatchExchangeEncoder.Encode(
            [new AdobeLabSwatch("測定", new Vector3(50, 10, -20))]);
        Assert.Equal("ASEF", Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2)));
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12, 2)));
        Assert.Contains(Encoding.ASCII.GetBytes("LAB "), data);
    }

    [Fact]
    public void WorkspaceRoundTripsAndRejectsDuplicateIds()
    {
        var history = new MeasurementHistory();
        var identity = new SpotreadInstrumentIdentity("ColorMunki", "CM-123");
        var entry = history.Add(
            TestMeasurementFactory.Create(MeasurementMode.Reflectance),
            "Sample",
            identity);
        var ambient = history.Add(
            TestMeasurementFactory.Create(MeasurementMode.Ambient),
            "Ambient",
            identity);
        history.SelectExclusive(entry.Id);
        var document = WorkspaceDocument.Create(
            history,
            MeasurementMode.Reflectance,
            MeasurementSidebarTab.SpotreadLog);

        var json = WorkspaceSerializer.Serialize(document);
        Assert.Contains("\"selectedSidebarTab\": \"spotreadLog\"", json);
        Assert.Contains("\"suggestedEV100\": 10.5", json);
        Assert.Contains("\"status\": \"valid\"", json);
        var decoded = WorkspaceSerializer.Deserialize(json);
        var decodedReflectance = decoded.Workspace.History.Modes.Single(
            state => state.Mode == MeasurementMode.Reflectance);
        Assert.Equal(entry.Id, decodedReflectance.ActiveEntryId);
        Assert.Equal("Sample", decoded.Workspace.History.Entries[0].Name);
        Assert.Equal("CM-123", decoded.Workspace.History.Entries[0].InstrumentIdentity?.SerialNumber);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, decoded.Workspace.SelectedSidebarTab);

        var restored = new MeasurementHistory();
        decoded.RestoreInto(restored);
        Assert.Equal(entry.Id, restored.ActiveIdFor(MeasurementMode.Reflectance));
        Assert.Equal(ambient.Id, restored.ActiveIdFor(MeasurementMode.Ambient));

        var invalid = document with
        {
            Workspace = document.Workspace with
            {
                History = document.Workspace.History with { Entries = [entry, entry] },
            },
        };
        Assert.Throws<InvalidDataException>(() => WorkspaceSerializer.Serialize(invalid));
        Assert.Throws<InvalidDataException>(
            () => WorkspaceSerializer.Deserialize(json.Replace(
                "\"formatVersion\": 1",
                "\"formatVersion\": 99",
                StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("a:b/c", "a_b_c")]
    [InlineData("name. ", "name")]
    [InlineData("", "Measurement")]
    public void WindowsFileNamesAreSanitized(string input, string expected)
    {
        Assert.Equal(expected, WindowsFileNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void ViewingConditionEvaluatorsUseMacThresholds()
    {
        var measurement = TestMeasurementFactory.Create(MeasurementMode.Ambient);
        var jspst = PrintingViewingConditionEvaluator.Evaluate(measurement);
        Assert.Equal(IlluminanceClassification.PrintComparison, jspst.IlluminanceClassification);
        var iso = Iso3664NumericEvaluator.Evaluate(measurement);
        Assert.Equal(Iso3664IlluminanceCondition.P3, iso.IlluminanceCondition);
        Assert.Equal(EvaluationStatus.Meets, iso.SummaryStatus);
    }
}
