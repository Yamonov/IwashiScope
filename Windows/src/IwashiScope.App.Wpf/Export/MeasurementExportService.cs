using System.IO;
using System.Text.RegularExpressions;
using IwashiScope.Core.Export;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.App.Wpf.Export;

public sealed record MeasurementExportOptions
{
    public bool SpectrumPng { get; init; } = true;
    public bool CriPng { get; init; } = true;
    public bool Tm30Png { get; init; } = true;
    public bool Csv { get; init; } = true;
    public bool Ase { get; init; } = true;
    public bool UsePracticalSpectrumRange { get; init; } = true;
    public bool ShowD50 { get; init; }
    public bool ShowD65 { get; init; }
    public SpectrumYAxisConfiguration? SpectrumYAxisConfiguration { get; init; }

    public static MeasurementExportOptions ForDrag(
        MeasurementMode mode, bool practicalRange, SpectrumYAxisConfiguration yAxis) => new()
    {
        Ase = mode == MeasurementMode.Reflectance,
        SpectrumPng = mode.IsLighting(),
        CriPng = mode.IsLighting(),
        Tm30Png = mode.IsLighting(),
        Csv = false,
        ShowD50 = false,
        ShowD65 = false,
        UsePracticalSpectrumRange = practicalRange,
        SpectrumYAxisConfiguration = yAxis,
    };
}

public sealed class MeasurementExportService
{
    public static bool CanExportDate(MeasurementHistoryDateGroup group, MeasurementMode mode) =>
        group.Entries.Count > 0 && group.Entries.All(entry => entry.Measurement.Mode == mode) &&
        (mode == MeasurementMode.Reflectance
            ? group.Entries.All(entry => entry.Measurement.Lab is not null)
            : group.Entries.Any(entry => entry.Measurement.Spectrum.Count > 0 ||
                entry.Measurement.Cri is not null || entry.Measurement.Tm30 is not null));

    public static byte[] DateSwatches(
        MeasurementHistoryDateGroup group, IReadOnlyList<MeasurementHistoryEntry> orderedEntries)
    {
        if (!CanExportDate(group, MeasurementMode.Reflectance))
            throw new InvalidDataException("The date group has no exportable Lab swatches.");
        var names = MeasurementExportFileNamer.BaseNames(group.Entries, orderedEntries);
        return AdobeSwatchExchangeEncoder.Encode(
            group.Entries.Select(entry => new AdobeLabSwatch(names[entry.Id], entry.Measurement.Lab!)).ToArray(),
            group.ExportName);
    }

    public Task<IReadOnlyList<string>> ExportLightingDateAsync(
        string parentDirectory, MeasurementHistoryDateGroup group,
        IReadOnlyList<MeasurementHistoryEntry> orderedEntries, MeasurementMode mode,
        bool practicalRange, SpectrumYAxisConfiguration yAxis,
        CancellationToken cancellationToken = default)
    {
        if (!mode.IsLighting() || !CanExportDate(group, mode))
            throw new InvalidDataException("The date group has no exportable lighting measurements.");
        return ExportAsync(Path.Combine(parentDirectory, group.ExportName), group.Entries,
            MeasurementExportOptions.ForDrag(mode, practicalRange, yAxis), orderedEntries, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ExportAsync(
        string directory,
        IReadOnlyList<MeasurementHistoryEntry> entries,
        MeasurementExportOptions options,
        IReadOnlyList<MeasurementHistoryEntry>? orderedEntries = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        var baseNames = MeasurementExportFileNamer.BaseNames(
            entries,
            orderedEntries ?? entries);

        var reflectance = entries
            .Where(entry => entry.Measurement.Mode == MeasurementMode.Reflectance)
            .ToArray();
        if (options.Ase && reflectance.Length > 0)
        {
            var swatches = reflectance
                .Where(entry => entry.Measurement.Lab is not null)
                .Select(entry => new AdobeLabSwatch(
                    baseNames[entry.Id],
                    entry.Measurement.Lab!))
                .ToArray();
            if (swatches.Length > 0)
            {
                var path = UniquePath(
                    directory,
                    Path.GetFileNameWithoutExtension(
                        MeasurementExportFileNamer.CombinedSwatchFileName(
                            reflectance,
                            orderedEntries ?? entries)),
                    ".ase");
                await AtomicFile.WriteAllBytesAsync(
                    path,
                    AdobeSwatchExchangeEncoder.Encode(swatches),
                    cancellationToken).ConfigureAwait(false);
                paths.Add(path);
            }
        }

        foreach (var entry in entries)
        {
            var baseName = baseNames[entry.Id];
            var measurement = entry.Measurement;
            if (options.SpectrumPng && measurement.Spectrum.Count > 0)
            {
                var path = UniquePath(directory, $"{baseName}-Spectrum", ".png");
                await AtomicFile.WriteAllBytesAsync(
                    path,
                    ChartPngRenderer.Spectrum(
                        measurement,
                        options.UsePracticalSpectrumRange,
                        options.ShowD50,
                        options.ShowD65,
                        options.SpectrumYAxisConfiguration,
                        entry.Name),
                    cancellationToken).ConfigureAwait(false);
                paths.Add(path);
            }

            if (options.Csv)
            {
                var path = UniquePath(directory, baseName, ".csv");
                var bytes = measurement.Mode.IsLighting()
                    ? MeasurementCsvEncoder.Lighting(measurement)
                    : MeasurementCsvEncoder.Spectrum(measurement);
                await AtomicFile.WriteAllBytesAsync(path, bytes, cancellationToken)
                    .ConfigureAwait(false);
                paths.Add(path);
            }

            if (measurement.Mode.IsLighting() && measurement.Cri is not null && options.CriPng)
            {
                var path = UniquePath(directory, $"{baseName}-CRI", ".png");
                await AtomicFile.WriteAllBytesAsync(
                    path,
                    ChartPngRenderer.Cri(measurement),
                    cancellationToken).ConfigureAwait(false);
                paths.Add(path);
            }

            if (measurement.Mode.IsLighting() && measurement.Tm30 is not null && options.Tm30Png)
            {
                var path = UniquePath(directory, $"{baseName}-TM-30-15", ".png");
                await AtomicFile.WriteAllBytesAsync(
                    path,
                    ChartPngRenderer.Tm30(measurement),
                    cancellationToken).ConfigureAwait(false);
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string UniquePath(string directory, string baseName, string extension)
    {
        var safeBaseName = WindowsFileNameSanitizer.Sanitize(baseName);
        var candidate = Path.Combine(directory, safeBaseName + extension);
        for (var suffix = 2; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{safeBaseName} {suffix}{extension}");
        }
        return candidate;
    }
}

public static partial class MeasurementExportFileNamer
{
    public static string CombinedSwatchFileName(
        IReadOnlyList<MeasurementHistoryEntry> entries,
        IReadOnlyList<MeasurementHistoryEntry> orderedEntries)
    {
        var baseNames = BaseNames(entries, orderedEntries);
        if (entries.Count == 0 || !baseNames.TryGetValue(entries[0].Id, out var firstBaseName))
        {
            return "001-Swatch.ase";
        }
        return entries.Count == 1
            ? $"{firstBaseName}-Swatch.ase"
            : $"{firstBaseName}-Selected-Swatches.ase";
    }

    public static IReadOnlyDictionary<Guid, string> BaseNames(
        IReadOnlyList<MeasurementHistoryEntry> entries,
        IReadOnlyList<MeasurementHistoryEntry> orderedEntries)
    {
        var orderedIndices = orderedEntries
            .Select((entry, index) => (entry.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var digits = Math.Max(3, Math.Max(orderedEntries.Count, 1).ToString().Length);
        var result = new Dictionary<Guid, string>();
        foreach (var (entry, fallbackIndex) in entries.Select((entry, index) => (entry, index)))
        {
            var sequence = (orderedIndices.TryGetValue(entry.Id, out var orderedIndex)
                ? orderedIndex
                : fallbackIndex) + 1;
            var prefix = sequence.ToString($"D{digits}");
            var component = SanitizeComponent(entry.Name);
            result.Add(
                entry.Id,
                string.IsNullOrEmpty(component) ? prefix : $"{prefix}-{component}");
        }
        return result;
    }

    public static string SanitizeComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var mapped = new string(value
            .Select(character => invalid.Contains(character) || char.IsControl(character)
                ? '-'
                : character)
            .ToArray());
        var compact = WhitespaceAroundHyphens().Replace(
            MultipleHyphens().Replace(
                MultipleWhitespace().Replace(mapped, " "),
                "-"),
            "-");
        var trimmed = compact.Trim(' ', '\t', '\r', '\n', '.', '-');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }
        return WindowsFileNameSanitizer.Sanitize(trimmed);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleWhitespace();

    [GeneratedRegex("-+")]
    private static partial Regex MultipleHyphens();

    [GeneratedRegex(@"\s*-\s*")]
    private static partial Regex WhitespaceAroundHyphens();
}

public sealed class DragExportCache : IDisposable
{
    private readonly string _root;
    private readonly MeasurementExportService _exports = new();

    public DragExportCache()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "IwashiScope",
            $"drag-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        CleanupExpiredDirectories();
    }

    public Task<IReadOnlyList<string>> CreateAsync(
        IReadOnlyList<MeasurementHistoryEntry> entries,
        MeasurementExportOptions options,
        IReadOnlyList<MeasurementHistoryEntry>? orderedEntries = null,
        CancellationToken cancellationToken = default) =>
        _exports.ExportAsync(Path.Combine(_root, Guid.NewGuid().ToString("N")),
            entries, options, orderedEntries, cancellationToken);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Explorer or a drop target may still be reading a file. The next run cleans it.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep a usable dropped file instead of failing application shutdown.
        }
    }

    private static void CleanupExpiredDirectories()
    {
        var parent = Path.Combine(Path.GetTempPath(), "IwashiScope");
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(parent, "drag-*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-2))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
