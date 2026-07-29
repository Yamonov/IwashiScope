using IwashiScope.Core.Models;

namespace IwashiScope.Infrastructure.Windows.Process;

public sealed record ProcessLaunchSpec
{
    public required string FileName { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>();
}

public static class ExecutableLocator
{
    public const string SpotreadOverrideEnvironmentVariable = "IWASHISCOPE_SPOTREAD_PATH";

    public static string? FindSpotread(string? appBaseDirectory = null)
    {
        var overridePath = Environment.GetEnvironmentVariable(SpotreadOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var baseDirectory = appBaseDirectory ?? AppContext.BaseDirectory;
        var appLocal = Path.Combine(baseDirectory, "iwashiscope-spotread.exe");
        if (File.Exists(appLocal))
        {
            return appLocal;
        }

        foreach (var ancestor in Ancestors(baseDirectory))
        {
            var developmentPath = Path.Combine(
                ancestor,
                "_reference",
                "IwashiScope",
                "Argyll_V3.5.0",
                "spectro",
                "iwashiscope-spotread.exe");
            if (File.Exists(developmentPath))
            {
                return developmentPath;
            }
        }
        return null;
    }

    public static ProcessLaunchSpec Real(string path) =>
        new()
        {
            FileName = Path.GetFullPath(path),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)),
        };

    public static IReadOnlyList<string> Arguments(
        MeasurementMode mode,
        int instrumentIndex) =>
        mode.SpotreadArguments(instrumentIndex);

    private static IEnumerable<string> Ancestors(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}
