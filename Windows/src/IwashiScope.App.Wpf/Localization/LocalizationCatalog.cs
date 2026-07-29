using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IwashiScope.App.Wpf.Localization;

public sealed class LocalizationCatalog
{
    private readonly IReadOnlyDictionary<string, string> _english;

    public LocalizationCatalog()
    {
        _english = LoadEnglish();
    }

    public string Language { get; private set; } = "ja";
    public event Action? LanguageChanged;

    public void SetLanguage(string language)
    {
        var normalized = language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ja";
        if (Language == normalized)
        {
            return;
        }
        Language = normalized;
        CultureInfo.CurrentUICulture = normalized == "en"
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("ja-JP");
        LanguageChanged?.Invoke();
    }

    public string Text(string japaneseSource, string? englishFallback = null)
    {
        if (Language == "ja")
        {
            return japaneseSource;
        }
        return _english.TryGetValue(japaneseSource, out var english)
            ? english
            : englishFallback ?? japaneseSource;
    }

    public int EntryCount => _english.Count;

    private static IReadOnlyDictionary<string, string> LoadEnglish()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IwashiScope.Localizable.xcstrings")
            ?? throw new InvalidOperationException("Localizable.xcstrings is not embedded.");
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("strings").EnumerateObject())
        {
            if (property.Value.TryGetProperty("localizations", out var localizations) &&
                localizations.TryGetProperty("en", out var english) &&
                english.TryGetProperty("stringUnit", out var unit) &&
                unit.TryGetProperty("value", out var value))
            {
                result[property.Name] = value.GetString() ?? property.Name;
            }
        }
        return result;
    }
}
