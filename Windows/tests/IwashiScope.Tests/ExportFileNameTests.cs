using IwashiScope.App.Wpf.Export;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using System.IO;

namespace IwashiScope.Tests;

public sealed class ExportFileNameTests
{
    [Fact]
    public void NamesUseFullPresentationOrderAndPreserveUnicode()
    {
        var first = Entry(1, "先頭");
        var second = Entry(2, "選択外");
        var third = Entry(3, "色票");
        var ordered = new[] { third, second, first };
        var selected = new[] { third, first };

        var names = MeasurementExportFileNamer.BaseNames(selected, ordered);

        Assert.Equal("001-色票", names[third.Id]);
        Assert.Equal("003-先頭", names[first.Id]);
        Assert.Equal(
            "001-色票-Selected-Swatches.ase",
            MeasurementExportFileNamer.CombinedSwatchFileName(selected, ordered));
    }

    [Fact]
    public void ComponentsAreSafeForWindowsReservedAndDuplicateNames()
    {
        var first = Entry(1, "CON");
        var second = Entry(2, "CON");
        var third = Entry(3, "a::b / c");
        var ordered = new[] { first, second, third };

        var names = MeasurementExportFileNamer.BaseNames(ordered, ordered);

        Assert.Equal("001-_CON", names[first.Id]);
        Assert.Equal("002-_CON", names[second.Id]);
        Assert.Equal("003-a-b-c", names[third.Id]);
        Assert.Equal(3, names.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EmptyNameUsesSequenceOnlyAndSingleAseUsesSwatchSuffix()
    {
        var entry = Entry(1, " / : . - ");

        var names = MeasurementExportFileNamer.BaseNames([entry], [entry]);

        Assert.Equal("001", names[entry.Id]);
        Assert.Equal(
            "001-Swatch.ase",
            MeasurementExportFileNamer.CombinedSwatchFileName([entry], [entry]));
    }

    [Fact]
    public async Task ExportWritesOneCombinedAseUsingPresentationSequence()
    {
        var selectedFirst = Entry(1, "色票");
        var unselected = Entry(2, "選択外");
        var selectedLast = Entry(3, "CON");
        var ordered = new[] { selectedFirst, unselected, selectedLast };
        var selected = new[] { selectedFirst, selectedLast };
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IwashiScope-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = await new MeasurementExportService().ExportAsync(
                directory,
                selected,
                new MeasurementExportOptions
                {
                    SpectrumPng = false,
                    CriPng = false,
                    Tm30Png = false,
                    Csv = false,
                    Ase = true,
                },
                ordered);

            var path = Assert.Single(paths);
            Assert.Equal(
                "001-色票-Selected-Swatches.ase",
                Path.GetFileName(path));
            Assert.Equal("ASEF", System.Text.Encoding.ASCII.GetString(
                await File.ReadAllBytesAsync(path),
                0,
                4));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static MeasurementHistoryEntry Entry(int sequence, string? name) =>
        new(
            Guid.NewGuid(),
            name,
            TestMeasurementFactory.Create(MeasurementMode.Reflectance, sequence),
            null);
}
