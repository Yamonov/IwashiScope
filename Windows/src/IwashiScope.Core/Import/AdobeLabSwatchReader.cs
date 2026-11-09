using System.Buffers.Binary;
using System.Text;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Import;

public sealed record AdobeLabSwatch(string Name, Vector3 Lab, int? UnnamedRecordNumber = null);
public sealed record LabSwatchMatch(AdobeLabSwatch Swatch, double DeltaE2000, double DeltaE76);
public enum LabSwatchRanking { Standard, ColorToneFirst }

public sealed record LabSwatchLibrary(string Name, IReadOnlyList<AdobeLabSwatch> Swatches)
{
    public IReadOnlyList<LabSwatchMatch> NearestSwatches(Vector3 reference, CancellationToken cancellationToken = default) =>
        NearestSwatches(reference, LabSwatchRanking.Standard, cancellationToken);

    public IReadOnlyList<LabSwatchMatch> NearestSwatches(
        Vector3 reference, LabSwatchRanking ranking, CancellationToken cancellationToken)
    {
        const int limit = 6;
        if (!reference.IsFinite) return [];
        var lightnessFactor = ranking == LabSwatchRanking.ColorToneFirst ? 2 : 1;
        var nearest = new List<(LabSwatchMatch Match, double Score)>(limit + 1);
        foreach (var swatch in Swatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = CieColorDifference.DeltaE2000(reference, swatch.Lab, lightnessFactor);
            if (!double.IsFinite(score)) continue;
            var position = nearest.FindIndex(candidate => candidate.Score > score);
            if (position == -1) position = nearest.Count;
            if (position >= limit || nearest.Any(candidate => candidate.Match.Swatch == swatch)) continue;
            var deltaE = lightnessFactor == 1 ? score : CieColorDifference.DeltaE2000(reference, swatch.Lab);
            nearest.Insert(position, (new(swatch, deltaE, CieColorDifference.DeltaE76(reference, swatch.Lab)), score));
            if (nearest.Count > limit) nearest.RemoveAt(limit);
        }
        return nearest.Select(candidate => candidate.Match).ToArray();
    }
}

public static class AdobeLabSwatchReader
{
    private const ushort LabColorSpace = 7;
    public const int MaximumFileSize = 16 * 1024 * 1024;
    private static readonly UnicodeEncoding BigEndianUnicode = new(true, false, true);

    public static LabSwatchLibrary ReadFile(string path, CancellationToken cancellationToken = default)
    {
        var format = GetFormat(path);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;
        var headerSize = format == Format.Aco ? 4 : 8;
        if (length < headerSize || length > MaximumFileSize) throw InvalidData();
        Span<byte> header = stackalloc byte[headerSize];
        stream.ReadExactly(header);
        ValidateHeader(header, format);
        cancellationToken.ThrowIfCancellationRequested();

        var data = new byte[(int)length];
        header.CopyTo(data);
        stream.ReadExactly(data.AsSpan(headerSize));
        return Decode(data, path, cancellationToken);
    }

    public static LabSwatchLibrary Decode(
        ReadOnlySpan<byte> data, string fileName, CancellationToken cancellationToken = default)
    {
        var format = GetFormat(fileName);
        ValidateHeader(data, format);
        var title = Path.GetFileNameWithoutExtension(fileName);
        var reader = new Reader(data);
        return format == Format.Aco
            ? new(title, ReadAco(ref reader, cancellationToken))
            : ReadAcb(ref reader, title, cancellationToken);
    }

    private static List<AdobeLabSwatch> ReadAco(ref Reader reader, CancellationToken cancellationToken)
    {
        const int recordSize = 10;
        var version = reader.UInt16();
        var count = reader.UInt16();
        if (version == 1)
        {
            var afterUnnamedRecords = reader;
            afterUnnamedRecords.Take(count * recordSize);
            if (afterUnnamedRecords.Remaining > 0)
            {
                reader = afterUnnamedRecords;
                version = reader.UInt16();
                if (version != 2) throw InvalidData();
                count = reader.UInt16();
            }
        }
        if (count > reader.Remaining / (version == 1 ? recordSize : recordSize + 4)) throw InvalidData();

        var swatches = new List<AdobeLabSwatch>();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var space = reader.UInt16();
            var color = reader.Take(recordSize - 2);
            var name = version == 2 ? reader.StringBytes() : default;
            if (space != LabColorSpace) continue;

            var l = BinaryPrimitives.ReadUInt16BigEndian(color);
            var a = BinaryPrimitives.ReadInt16BigEndian(color[2..]);
            var b = BinaryPrimitives.ReadInt16BigEndian(color[4..]);
            if (l > 10_000 || a is < -12_800 or > 12_700 || b is < -12_800 or > 12_700) continue;
            var displayName = DisplayString(name);
            swatches.Add(new(displayName, new(l / 100.0, a / 100.0, b / 100.0),
                displayName.Length == 0 ? index + 1 : null));
        }
        if (reader.Remaining != 0) throw InvalidData();
        return swatches;
    }

    private static LabSwatchLibrary ReadAcb(
        ref Reader reader, string fileTitle, CancellationToken cancellationToken)
    {
        reader.Take(8);
        var titleBytes = reader.StringBytes();
        var prefixBytes = reader.StringBytes();
        var suffixBytes = reader.StringBytes();
        reader.StringBytes(); // Description
        var count = reader.UInt16();
        reader.Take(4); // Page size and key color index
        if (reader.UInt16() != LabColorSpace) return new(fileTitle, []);
        if (count > reader.Remaining / 13) throw InvalidData();

        var title = DisplayString(titleBytes);
        var prefix = DisplayString(prefixBytes);
        var suffix = DisplayString(suffixBytes);
        var swatches = new List<AdobeLabSwatch>();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = DisplayString(reader.StringBytes());
            reader.Take(6); // Color code
            var color = reader.Take(3);
            if (name.Length == 0) continue; // Empty page slot
            swatches.Add(new(prefix + name + suffix, new(color[0] * 100.0 / 255, color[1] - 128, color[2] - 128)));
        }
        var footer = reader.Take(reader.Remaining);
        if (!footer.IsEmpty && !footer.SequenceEqual("spflspot"u8) && !footer.SequenceEqual("spflproc"u8))
            throw InvalidData();
        return new(title.Length == 0 ? fileTitle : title, swatches);
    }

    private static string DisplayString(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = BigEndianUnicode.GetString(bytes).Trim('\0');
        }
        catch (DecoderFallbackException exception)
        {
            throw InvalidData(exception);
        }
        var separator = text.IndexOf('=');
        if (text.StartsWith("$$$/", StringComparison.Ordinal) && separator >= 0)
            text = text[(separator + 1)..];
        return text.Replace("^R", "®").Replace("^C", "©");
    }

    private static Format GetFormat(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".aco" => Format.Aco,
        ".acb" => Format.Acb,
        _ => throw InvalidData(),
    };

    private static void ValidateHeader(ReadOnlySpan<byte> data, Format format)
    {
        if (format == Format.Aco)
        {
            if (data.Length < 4 || BinaryPrimitives.ReadUInt16BigEndian(data) is not (1 or 2))
                throw InvalidData();
        }
        else if (data.Length < 8 || !data[..4].SequenceEqual("8BCB"u8) ||
                 BinaryPrimitives.ReadUInt16BigEndian(data[4..]) != 1)
        {
            throw InvalidData();
        }
    }

    private static InvalidDataException InvalidData(Exception? inner = null) =>
        new("Invalid, incomplete or oversized Adobe swatch file.", inner);

    private enum Format { Aco, Acb }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _remaining = data;
        public int Remaining => _remaining.Length;

        public ReadOnlySpan<byte> Take(int count)
        {
            if ((uint)count > (uint)Remaining) throw InvalidData();
            var bytes = _remaining[..count];
            _remaining = _remaining[count..];
            return bytes;
        }

        public ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

        public ReadOnlySpan<byte> StringBytes()
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(Take(4));
            if (length > Remaining / 2) throw InvalidData();
            return Take((int)length * 2);
        }
    }
}
