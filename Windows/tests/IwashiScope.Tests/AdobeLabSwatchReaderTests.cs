using System.Buffers.Binary;
using System.Text;
using IwashiScope.Core.Import;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class AdobeLabSwatchReaderTests
{
    [Fact]
    public void AcoUsesNamedSectionAndSkipsNonLabAndOutOfRangeRecords()
    {
        var rgb = Words(0, 65535, 0, 0, 0);
        var invalid = Words(7, 10001, 0, 0, 0);
        var invalidA = Words(7, 5000, unchecked((ushort)-12801), 0, 0);
        var invalidB = Words(7, 5000, 0, 12701, 0);
        var valid = Words(7, 10000, unchecked((ushort)-12800), 12700, 0);
        var data = Concat(Words(1, 5), rgb, invalid, invalidA, invalidB, valid,
            Words(2, 5), rgb, Name("Red"), invalid, Name("Invalid"),
            invalidA, Name("Invalid a*"), invalidB, Name("Invalid b*"),
            valid, Name("$$$/Sample=色見本\0"));
        var library = AdobeLabSwatchReader.Decode(data, "Mixed.ACO");
        Assert.Equal("Mixed", library.Name);
        Assert.Equal(new AdobeLabSwatch("色見本", new(100, -128, 127)), Assert.Single(library.Swatches));
    }

    [Fact]
    public void AcoPreservesUnnamedRecordPosition()
    {
        var data = Words(1, 2, 0, 65535, 0, 0, 0, 7, 5000, 0, 0, 0);
        Assert.Equal(new AdobeLabSwatch("", new(50, 0, 0), 2),
            Assert.Single(AdobeLabSwatchReader.Decode(data, "Unnamed.aco").Swatches));
    }

    [Fact]
    public void AcbUsesMetadataAndSkipsPadding()
    {
        var data = Concat("8BCB"u8.ToArray(), Words(1, 3012), Name("$$$/Title=Example^R"),
            Name("Example "), Name(" C"), Name(""), Words(2, 5, 1, 7),
            Name(""), new byte[9], Name("101"), "101   "u8.ToArray(), [128, 119, 145],
            "spflspot"u8.ToArray());
        var library = AdobeLabSwatchReader.Decode(data, "Book.acb");
        Assert.Equal("Example®", library.Name);
        Assert.Equal(new AdobeLabSwatch("Example 101 C", new(128 * 100.0 / 255, -9, 17)),
            Assert.Single(library.Swatches));
    }

    [Fact]
    public void RejectsTruncatedAndOversizedFiles()
    {
        var data = Concat(Words(2, 1, 7, 5000, 0, 0, 0), [255, 255, 255, 255]);
        Assert.Throws<InvalidDataException>(() => AdobeLabSwatchReader.Decode(data, "Truncated.aco"));
        using var fixture = new TemporarySwatchFile();
        using (var file = File.Create(fixture.Path))
        {
            file.Write(Words(1, 1, 7, 5000, 0, 0, 0));
            file.SetLength(AdobeLabSwatchReader.MaximumFileSize + 1L);
        }
        Assert.Throws<InvalidDataException>(() => AdobeLabSwatchReader.ReadFile(fixture.Path));
    }

    [Fact]
    public void ReturnsSixUniqueMatchesWithBothDifferences()
    {
        var colors = new[] { 100, 54, 51, 51, 53, 50, 52, 55 }
            .Select(value => new AdobeLabSwatch(value.ToString(), new(value, 0, 0))).ToArray();
        var library = new LabSwatchLibrary("Test", colors);
        var matches = library.NearestSwatches(new(50, 0, 0));
        Assert.Equal(new[] { 50d, 51, 52, 53, 54, 55 }, matches.Select(match => match.Swatch.Lab.First));
        Assert.Equal(new[] { 0d, 1, 2, 3, 4, 5 }, matches.Select(match => match.DeltaE76));
        Assert.Equal(0, matches[0].DeltaE2000);
        Assert.Throws<OperationCanceledException>(() =>
            library.NearestSwatches(new(50, 0, 0), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void ColorToneFirstChangesOrderWithoutChangingDisplayedDifferences()
    {
        var library = new LabSwatchLibrary("Test",
        [
            new("Lightness difference", new(54, 0, 0)),
            new("Chroma difference", new(50, 2, 0)),
        ]);
        var reference = new Vector3(50, 0, 0);
        var standard = library.NearestSwatches(reference);
        var colorToneFirst = library.NearestSwatches(reference, LabSwatchRanking.ColorToneFirst, CancellationToken.None);

        Assert.Equal("Chroma difference", standard[0].Swatch.Name);
        Assert.Equal(standard.Reverse(), colorToneFirst);
    }

    private static byte[] Words(params ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), words[i]);
        return bytes;
    }

    private static byte[] Name(string text) =>
        Concat(Words(0, (ushort)text.Length), Encoding.BigEndianUnicode.GetBytes(text));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private sealed class TemporarySwatchFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"IwashiScope-{Guid.NewGuid():N}.aco");
        public void Dispose() => File.Delete(Path);
    }
}
