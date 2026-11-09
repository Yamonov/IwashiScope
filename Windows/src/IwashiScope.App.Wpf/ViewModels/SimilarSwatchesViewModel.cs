using System.IO;
using System.Windows.Media;
using IwashiScope.App.Wpf.Localization;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Import;
using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.ViewModels;

public sealed class SimilarSwatchesViewModel : ObservableObject
{
    private readonly LocalizationCatalog _localization;
    private readonly Func<Vector3?, Brush> _labBrush;
    private LabSwatchLibrary? _library;
    private SpotMeasurement? _measurement;
    private IReadOnlyList<LabSwatchMatch> _matches = [];
    private CancellationTokenSource? _import;
    private CancellationTokenSource? _comparison;
    private ImportError _error;
    private LabSwatchRanking _ranking;

    internal SimilarSwatchesViewModel(LocalizationCatalog localization, Func<Vector3?, Brush> labBrush)
    {
        _localization = localization;
        _labBrush = labBrush;
        _localization.LanguageChanged += RefreshLabelsAndCards;
    }

    public string LoadLabel => T("スウォッチファイルを読み込む", "Load Swatch File");
    public string RankingLabel => T("近似色の並び順", "Similar Color Sort Order");
    public string StandardLabel => T("標準", "Standard");
    public string ColorToneFirstLabel => T("色み優先", "Color Tone First");
    public string Title => _library is not { } library
        ? T("近似色", "Similar Colors")
        : IsColorToneFirst
            ? T($"{library.Name} の近似色（色み優先順）", $"Similar Colors in {library.Name} (Color Tone First)")
            : T($"{library.Name} の近似色", $"Similar Colors in {library.Name}");

    public bool IsStandard
    {
        get => _ranking == LabSwatchRanking.Standard;
        set { if (value) SetRanking(LabSwatchRanking.Standard); }
    }

    public bool IsColorToneFirst
    {
        get => _ranking == LabSwatchRanking.ColorToneFirst;
        set { if (value) SetRanking(LabSwatchRanking.ColorToneFirst); }
    }

    public bool CanLoad => _import is null;
    public IReadOnlyList<SimilarSwatchCard> Cards { get; private set; } = [];

    public string Status => _error switch
    {
        ImportError.NoLab => T("このファイルには読み込めるLabスウォッチがありません。", "This file contains no usable Lab swatches."),
        ImportError.Invalid => T("形式が正しくないか、スウォッチファイルが大きすぎます。", "The swatch file is invalid, incomplete or too large."),
        ImportError.Unreadable => T("スウォッチファイルを開けませんでした。", "Unable to open the swatch file."),
        _ when _import is not null => T("読み込み中…", "Loading swatches…"),
        _ when _comparison is not null => T("比較中…", "Comparing swatches…"),
        _ when _library is not null && Cards.Count == 0 => T(
            "比較する反射原稿の測定結果を選んでください。", "Select a reflectance measurement to compare."),
        _ => string.Empty,
    };

    public bool ShowsStatus => Status.Length != 0;

    internal async Task LoadAsync(string path)
    {
        if (_import is not null) return;
        using var cancellation = new CancellationTokenSource();
        _import = cancellation;
        _error = ImportError.None;
        OnPropertyChanged(nameof(CanLoad));
        RefreshStatus();
        try
        {
            var library = await Task.Run(
                () => AdobeLabSwatchReader.ReadFile(path, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (library.Swatches.Count == 0)
            {
                _error = ImportError.NoLab;
                return;
            }
            _library = library;
            OnPropertyChanged(nameof(Title));
            await CompareAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            _error = ImportError.Invalid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _error = ImportError.Unreadable;
        }
        finally
        {
            _import = null;
            OnPropertyChanged(nameof(CanLoad));
            RefreshStatus();
        }
    }

    internal void SetMeasurement(SpotMeasurement? measurement)
    {
        if (ReferenceEquals(_measurement, measurement)) return;
        _measurement = measurement;
        _ = CompareAsync();
    }

    private void SetRanking(LabSwatchRanking ranking)
    {
        if (_ranking == ranking) return;
        _ranking = ranking;
        OnPropertyChanged(nameof(IsStandard));
        OnPropertyChanged(nameof(IsColorToneFirst));
        OnPropertyChanged(nameof(Title));
        _ = CompareAsync();
    }

    internal void CancelPendingWork()
    {
        _import?.Cancel();
        _comparison?.Cancel();
    }

    internal void RefreshDisplayColors()
    {
        Cards = _matches.Select(match => new SimilarSwatchCard(
            match.Swatch.UnnamedRecordNumber is { } index
                ? T($"スウォッチ {index}", $"Swatch {index}")
                : match.Swatch.Name,
            match.Swatch.Lab, match.DeltaE2000, match.DeltaE76, _labBrush(match.Swatch.Lab))).ToArray();
        OnPropertyChanged(nameof(Cards));
        RefreshStatus();
    }

    private async Task CompareAsync()
    {
        _comparison?.Cancel();
        if (_library is not { } library) return;
        var measurement = _measurement;
        var ranking = _ranking;
        using var cancellation = new CancellationTokenSource();
        _comparison = cancellation;
        _matches = [];
        RefreshDisplayColors();
        try
        {
            var matches = await Task.Run(() => ReferenceLab(measurement) is { } lab
                ? library.NearestSwatches(lab, ranking, cancellation.Token) : [], cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _matches = matches;
            RefreshDisplayColors();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_comparison, cancellation))
            {
                _comparison = null;
                RefreshStatus();
            }
        }
    }

    private static Vector3? ReferenceLab(SpotMeasurement? measurement)
    {
        if (measurement is not { Mode: MeasurementMode.Reflectance }) return null;
        if (measurement.Lab is { IsFinite: true } lab &&
            string.Equals(measurement.LabWhitePoint ?? "D50", "D50", StringComparison.OrdinalIgnoreCase))
            return lab;
        try
        {
            return ReflectanceIlluminantColorComparisonCalculator.ReferenceLab(measurement);
        }
        catch (ColorAppearanceException)
        {
            return null;
        }
    }

    private void RefreshLabelsAndCards()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LoadLabel));
        OnPropertyChanged(nameof(RankingLabel));
        OnPropertyChanged(nameof(StandardLabel));
        OnPropertyChanged(nameof(ColorToneFirstLabel));
        RefreshDisplayColors();
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ShowsStatus));
    }

    private string T(string japanese, string english) => _localization.Text(japanese, english);
    private enum ImportError { None, NoLab, Invalid, Unreadable }
}

public sealed record SimilarSwatchCard(
    string Name, Vector3 Lab, double DeltaE2000, double DeltaE76, Brush SwatchBrush);
