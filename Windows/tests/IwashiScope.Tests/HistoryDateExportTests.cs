using System.Buffers.Binary;
using System.Text;
using IwashiScope.App.Wpf.Export;
using IwashiScope.Core.Export;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class HistoryDateExportTests
{
    [Fact]
    public void DateSwatchesHaveARealAseGroupWithOrderedLabSpotColors()
    {
        var first = MeasurementHistoryEntry.Create(TestMeasurementFactory.Create(MeasurementMode.Reflectance), "先頭");
        var second = MeasurementHistoryEntry.Create(first.Measurement, "次");
        var other = MeasurementHistoryEntry.Create(first.Measurement, "別日");
        var group = new MeasurementHistoryDateGroup(new DateOnly(2026, 9, 2), [second, first]);
        var data = MeasurementExportService.DateSwatches(group, [other, second, first]);
        Assert.Equal("2026-09-02", group.ExportName);
        Assert.Equal("ASEF", Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)));
        Assert.Equal(0xC001, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12)));
        Assert.Equal(24u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(14)));
        Assert.Equal("2026-09-02\0", Encoding.BigEndianUnicode.GetString(data, 20, 22));
        var plain = AdobeSwatchExchangeEncoder.Encode([
            new("002-次", second.Measurement.Lab!), new("003-先頭", first.Measurement.Lab!),
        ]);
        Assert.Equal(plain.AsSpan(12).ToArray(), data.AsSpan(42, plain.Length - 12).ToArray());
        Assert.Equal(new byte[] { 0xC0, 0x02, 0, 0, 0, 0 }, data[^6..]);
    }

    [Theory]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public async Task DateFolderMatchesDragOutputIncludingRangeAndYAxis(MeasurementMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "IwashiScope-date-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = MeasurementHistoryEntry.Create(TestMeasurementFactory.Create(mode), "光源");
            var other = MeasurementHistoryEntry.Create(entry.Measurement, "別日");
            var group = new MeasurementHistoryDateGroup(new DateOnly(2026, 9, 1), [entry]);
            var order = new[] { other, entry };
            var yAxis = new SpectrumYAxisConfiguration(SpectrumYAxisMode.Fixed, 130);
            var service = new MeasurementExportService();
            var expected = await service.ExportAsync(Path.Combine(root, "drag"), group.Entries,
                MeasurementExportOptions.ForDrag(mode, true, yAxis), order);
            var actual = await service.ExportLightingDateAsync(Path.Combine(root, "date"), group, order, mode, true, yAxis);
            Assert.Equal(3, actual.Count);
            Assert.Equal(expected.Select(Path.GetFileName), actual.Select(Path.GetFileName));
            Assert.All(actual, path => Assert.Equal("2026-09-01", Path.GetFileName(Path.GetDirectoryName(path))));
            Assert.DoesNotContain(actual, path => path.EndsWith(".ase") || path.EndsWith(".csv"));
            foreach (var pair in expected.Zip(actual))
                Assert.Equal(await File.ReadAllBytesAsync(pair.First), await File.ReadAllBytesAsync(pair.Second));

            var existing = actual.ToDictionary(path => path, File.ReadAllBytes);
            var repeated = await service.ExportLightingDateAsync(Path.Combine(root, "date"), group, order, mode, true, yAxis);
            Assert.Empty(repeated.Intersect(actual));
            foreach (var pair in existing) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MixedModeAndEmptyDateGroupsAreNotExportable()
    {
        var reflectance = MeasurementHistoryEntry.Create(TestMeasurementFactory.Create(MeasurementMode.Reflectance));
        var ambient = MeasurementHistoryEntry.Create(TestMeasurementFactory.Create(MeasurementMode.Ambient));
        var mixed = new MeasurementHistoryDateGroup(new DateOnly(2026, 9, 1), [reflectance, ambient]);
        foreach (var mode in Enum.GetValues<MeasurementMode>())
        {
            Assert.False(MeasurementExportService.CanExportDate(mixed, mode));
            Assert.False(MeasurementExportService.CanExportDate(mixed with { Entries = [] }, mode));
        }
    }

    [Fact]
    public void AseRejectsValuesThatOverflowItsFloatRepresentation()
    {
        Assert.Throws<InvalidDataException>(() => AdobeSwatchExchangeEncoder.Encode([
            new("Invalid", new Vector3(double.MaxValue, 0, 0)),
        ], "2026-09-02"));
    }
}
