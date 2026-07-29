using System.Text;
using System.Text.Json;

namespace IwashiScope.Infrastructure.Windows.Storage;

public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(
        string path,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The target has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, data.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static Task WriteAllTextAsync(
        string path,
        string text,
        CancellationToken cancellationToken = default) =>
        WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(text), cancellationToken);
}

public static class WindowsFileNameSanitizer
{
    private static readonly HashSet<string> ReservedNames =
        new(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            },
            StringComparer.OrdinalIgnoreCase);

    public static string Sanitize(string value, string fallback = "Measurement")
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = value
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray();
        var result = new string(characters).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result))
        {
            result = fallback;
        }
        if (ReservedNames.Contains(result))
        {
            result = $"_{result}";
        }
        return result.Length > 120 ? result[..120].TrimEnd('.', ' ') : result;
    }
}

public sealed record AppSettings
{
    public string Language { get; init; } = "ja";
    public bool UsePracticalSpectrumRange { get; init; } = true;
    public bool IncludeD50Reference { get; init; }
    public bool IncludeD65Reference { get; init; }
    public int InstrumentIndex { get; init; } = 1;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IwashiScope",
            "settings.json");
    }

    public string Path { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }
        try
        {
            await using var stream = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(
                       stream,
                       Options,
                       cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        AtomicFile.WriteAllTextAsync(
            Path,
            JsonSerializer.Serialize(settings, Options),
            cancellationToken);
}
