using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Export;

public static class MeasurementCsvEncoder
{
    public static byte[] Spectrum(SpotMeasurement measurement)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "wavelength_nm", "value" },
        };
        rows.AddRange(measurement.Spectrum.Select(sample =>
            (IReadOnlyList<string>)new[] { Number(sample.Wavelength), Number(sample.Value) }));
        return Encoding.UTF8.GetBytes(Csv(rows));
    }

    public static byte[] Lighting(SpotMeasurement measurement)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "section", "item", "wavelength_nm", "value",
                "reference_J", "reference_a", "reference_b",
                "test_J", "test_a", "test_b",
            },
            new[]
            {
                "metadata", "captured_at", "",
                measurement.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "", "", "", "", "", "",
            },
            new[]
            {
                "metadata", "mode", "", measurement.Mode.ProtocolName(),
                "", "", "", "", "", "",
            },
        };

        rows.AddRange(measurement.Spectrum.Select(sample =>
            (IReadOnlyList<string>)new[]
            {
                "spectrum", sample.Id.ToString(CultureInfo.InvariantCulture),
                Number(sample.Wavelength), Number(sample.Value),
                "", "", "", "", "", "",
            }));

        if (measurement.Cri is { } cri)
        {
            rows.Add(MetricRow("CRI", "Ra", cri.Ra));
            foreach (var pair in cri.Individual.OrderBy(pair => pair.Key))
            {
                rows.Add(MetricRow("CRI", $"R{pair.Key}", pair.Value));
            }
            if (!cri.Individual.ContainsKey(9) && cri.R9 is { } r9)
            {
                rows.Add(MetricRow("CRI", "R9", r9));
            }
            rows.Add(["CRI", "caution", "", cri.Caution ? "true" : "false", "", "", "", "", "", ""]);
        }

        if (measurement.Tlci is { } tlci)
        {
            rows.Add(MetricRow("TLCI-2012", "Qa", tlci.Qa));
            rows.Add([
                "TLCI-2012", "caution", "", tlci.Caution ? "true" : "false",
                "", "", "", "", "", "",
            ]);
        }

        if (measurement.Tm30 is { } tm30)
        {
            rows.Add(MetricRow("TM-30-15", "Rf", tm30.FidelityIndex));
            rows.Add(MetricRow("TM-30-15", "Rg", tm30.GamutIndex));
            rows.Add(MetricRow("TM-30-15", "CCT", tm30.Cct));
            rows.Add(MetricRow("TM-30-15", "Duv", tm30.Duv));
            rows.Add([
                "TM-30-15", "status", "",
                tm30.Status == Tm30Status.Valid ? "valid" : "caution",
                "", "", "", "", "", "",
            ]);
            rows.AddRange(tm30.HueBins
                .OrderBy(bin => bin.Index)
                .Select(bin => ColorPairRow(
                    "TM-30-15 hue bin",
                    bin.Index.ToString(CultureInfo.InvariantCulture),
                    bin.ReferenceJab,
                    bin.TestJab)));
            rows.AddRange(tm30.EvaluationSamples
                .OrderBy(sample => sample.Index)
                .Select(sample => ColorPairRow(
                    "TM-30-15 evaluation sample",
                    sample.Index.ToString(CultureInfo.InvariantCulture),
                    sample.ReferenceJab,
                    sample.TestJab)));
        }

        return Encoding.UTF8.GetBytes(Csv(rows));
    }

    private static IReadOnlyList<string> MetricRow(string section, string item, double value) =>
        [section, item, "", Number(value), "", "", "", "", "", ""];

    private static IReadOnlyList<string> ColorPairRow(
        string section,
        string item,
        Vector3 reference,
        Vector3 test) =>
        [
            section, item, "", "",
            Number(reference.First), Number(reference.Second), Number(reference.Third),
            Number(test.First), Number(test.Second), Number(test.Third),
        ];

    private static string Number(double value) => value.ToString("G12", CultureInfo.InvariantCulture);

    private static string Csv(IEnumerable<IReadOnlyList<string>> rows) =>
        string.Join("\r\n", rows.Select(row => string.Join(",", row.Select(Escape)))) + "\r\n";

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}

public sealed record AdobeLabSwatch(string Name, Vector3 Lab);

public static class AdobeSwatchExchangeEncoder
{
    private const ushort ColorEntryBlockType = 0x0001;
    private const ushort SpotColorType = 0x0001;

    public static byte[] Encode(IReadOnlyList<AdobeLabSwatch> swatches)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "ASEF");
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt32(stream, checked((uint)swatches.Count));

        foreach (var swatch in swatches)
        {
            var payload = ColorEntryPayload(swatch);
            WriteUInt16(stream, ColorEntryBlockType);
            WriteUInt32(stream, checked((uint)payload.Length));
            stream.Write(payload);
        }

        return stream.ToArray();
    }

    private static byte[] ColorEntryPayload(AdobeLabSwatch swatch)
    {
        if (!swatch.Lab.IsFinite)
        {
            throw new InvalidDataException($"Swatch '{swatch.Name}' contains a non-finite Lab value.");
        }

        using var stream = new MemoryStream();
        WriteAseName(stream, swatch.Name);
        WriteAscii(stream, "LAB ");
        WriteSingle(stream, checked((float)(swatch.Lab.First / 100)));
        WriteSingle(stream, checked((float)swatch.Lab.Second));
        WriteSingle(stream, checked((float)swatch.Lab.Third));
        WriteUInt16(stream, SpotColorType);
        return stream.ToArray();
    }

    private static void WriteAseName(Stream stream, string name)
    {
        var units = name.AsSpan();
        var utf16Length = Encoding.BigEndianUnicode.GetByteCount(name) / 2;
        if (utf16Length >= ushort.MaxValue)
        {
            throw new InvalidDataException($"Swatch name is too long: {name}");
        }
        WriteUInt16(stream, checked((ushort)(utf16Length + 1)));
        stream.Write(Encoding.BigEndianUnicode.GetBytes(units.ToString()));
        WriteUInt16(stream, 0);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteSingle(Stream stream, float value) =>
        WriteUInt32(stream, BitConverter.SingleToUInt32Bits(value));

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));
}
