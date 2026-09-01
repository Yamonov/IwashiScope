using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using IwashiScope.App.Wpf.Localization;
using IwashiScope.App.Wpf.Layout;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Core.Workspace;
using IwashiScope.Infrastructure.Windows.Process;
using IwashiScope.Infrastructure.Windows.Session;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.App.Wpf.ViewModels;

public sealed class HistoryItemViewModel : ObservableObject
{
    private readonly Action<Guid, string?> _rename;
    private string _name;

    public HistoryItemViewModel(
        MeasurementHistoryEntry entry,
        int sequence,
        bool isSelected,
        IReadOnlyList<UserIlluminantSlot> registeredSlots,
        bool japanese,
        Action<Guid, string?> rename)
    {
        Entry = entry;
        Sequence = sequence;
        IsSelected = isSelected;
        IsAveragingStack = false;
        AveragingAcceptedCount = 0;
        RegisteredUserIlluminantSlots = registeredSlots;
        IsJapanese = japanese;
        _rename = rename;
        _name = entry.Name ?? string.Empty;
    }

    private HistoryItemViewModel(
        SpotMeasurement measurement,
        int acceptedCount,
        string name,
        bool japanese)
    {
        Entry = new MeasurementHistoryEntry(Guid.Empty, name, measurement);
        Sequence = 0;
        IsSelected = false;
        IsAveragingStack = true;
        AveragingAcceptedCount = acceptedCount;
        RegisteredUserIlluminantSlots = [];
        IsJapanese = japanese;
        _rename = (_, _) => { };
        _name = name;
    }

    public static HistoryItemViewModel AveragingStack(
        SpotMeasurement measurement,
        int acceptedCount,
        string name,
        bool japanese) =>
        new(measurement, acceptedCount, name, japanese);

    public MeasurementHistoryEntry Entry { get; }
    public Guid Id => Entry.Id;
    public int Sequence { get; }
    public bool IsSelected { get; }
    public bool IsAveragingStack { get; }
    public bool IsRegularHistoryItem => !IsAveragingStack;
    public bool CanSelect => !IsAveragingStack;
    public bool CanRename => !IsAveragingStack;
    public int AveragingAcceptedCount { get; }
    public IReadOnlyList<UserIlluminantSlot> RegisteredUserIlluminantSlots { get; }
    public bool IsJapanese { get; }
    public bool HasUserIlluminantRegistration => RegisteredUserIlluminantSlots.Count > 0;
    public IReadOnlyList<string> UserIlluminantBadgeTexts => RegisteredUserIlluminantSlots
        .Select(slot => $"🔒{UserIlluminantSlots.Title(slot, IsJapanese)}")
        .ToArray();
    public string DateKey => Entry.Measurement.CapturedAt.ToLocalTime().ToString("yyyy/MM/dd");
    public string RegisterUserIlluminantLabel => IsJapanese
        ? "ユーザー定義光源に登録"
        : "Register as User Illuminant";
    public string RemoveUserIlluminantLabel => IsJapanese
        ? "ユーザー定義光源から削除"
        : "Remove from User Illuminants";
    public string RenameLabel => IsJapanese ? "名前を付ける" : "Rename";
    public string UserDefined1Label => UserIlluminantSlots.Title(UserIlluminantSlot.User1, IsJapanese);
    public string UserDefined2Label => UserIlluminantSlots.Title(UserIlluminantSlot.User2, IsJapanese);
    public string UserDefined3Label => UserIlluminantSlots.Title(UserIlluminantSlot.User3, IsJapanese);
    public double ItemWidth => IsAveragingStack ? HistoryCardLayout.Width + 9 : HistoryCardLayout.Width;
    public double ItemHeight => IsAveragingStack ? HistoryCardLayout.Height + 8 : HistoryCardLayout.Height;
    public bool ShowsAverageBadge =>
        IsAveragingStack || Entry.Measurement.AveragedMeasurement is not null;
    public string AverageBadgeText => IsAveragingStack
        ? $"{AveragingAcceptedCount}回"
        : Entry.Measurement.AveragedMeasurement is { } averaged
            ? $"平均 {averaged.SampleCount}回"
            : string.Empty;
    public SpotMeasurement Measurement => Entry.Measurement;
    public string Timestamp => Entry.Measurement.CapturedAt.ToLocalTime().ToString("g");
    public string ModeBadge => Entry.Measurement.Mode.ProtocolName();
    public string Instrument => Entry.InstrumentIdentity?.DisplayName ?? "—";
    public string Summary => Entry.Measurement.Mode == MeasurementMode.Reflectance
        ? Entry.Measurement.Lab is { } lab
            ? $"L* {lab.First:0.0}  a* {lab.Second:0.0}  b* {lab.Third:0.0}"
            : "Lab —"
        : $"CCT {Entry.Measurement.Cct:0} K  ·  {Entry.Measurement.Lux:0} lx";
    public bool IsReflectance => Entry.Measurement.Mode == MeasurementMode.Reflectance;
    public bool IsLighting => !IsReflectance;
    public string LabLText => Entry.Measurement.Lab?.First.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
    public string LabAText => Entry.Measurement.Lab?.Second.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
    public string LabBText => Entry.Measurement.Lab?.Third.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
    public Brush SwatchBrush
    {
        get
        {
            if (Entry.Measurement.Lab is not { } lab)
            {
                return Brushes.LightGray;
            }
            var color = LabColorConverter.Convert(lab, Entry.Measurement.LabWhitePoint).Srgb;
            return new SolidColorBrush(Color.FromRgb(color.RedByte, color.GreenByte, color.BlueByte));
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                _rename(Id, value);
            }
        }
    }
}

public enum ReflectanceIlluminantSourceKind
{
    Cie,
    User1,
    User2,
    User3,
}

public sealed record CieIlluminantOptionViewModel(
    CieReferenceIlluminant? Illuminant,
    string DisplayName);

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LocalizationCatalog _localization = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly MeasurementHistoryPersistenceStore _historyPersistenceStore = new();
    private readonly MeasurementSessionController _session;
    private readonly MeasurementSidebarTabCoordinator _sidebarTabCoordinator = new();
    private AppSettings _settings = new();
    private MeasurementMode _mode = MeasurementMode.Reflectance;
    private SpotMeasurement? _activeMeasurement;
    private bool _usePracticalRange;
    private SpectrumYAxisConfiguration _spectrumYAxisConfiguration =
        SpectrumYAxisConfiguration.ForMeasurementMode(MeasurementMode.Reflectance);
    private bool _showD50;
    private bool _showD65;
    private int _instrumentIndex = 1;
    private string _errorMessage = string.Empty;
    private bool _isBrowsingRestoredWorkspace;
    private int _selectedTabIndex;
    private int _selectedRenderingTabIndex;
    private string? _lastSavedFingerprint;
    private bool _isModeSelectionVisible = true;
    private ReflectanceIlluminantSourceKind _reflectanceIlluminantSourceKind =
        ReflectanceIlluminantSourceKind.Cie;
    private CieIlluminantOptionViewModel? _selectedCieIlluminantOption;
    private bool _appliesChromaticAdaptation = true;
    private CancellationTokenSource? _historyPersistenceCancellation;
    private Task _historyPersistenceTask = Task.CompletedTask;
    private string? _lastPersistedHistoryFingerprint;
    private string _historyPersistenceError = string.Empty;

    public MainWindowViewModel()
    {
        _session = new MeasurementSessionController(LaunchSpec, _mode);
        _session.Changed += SessionChanged;
        _session.Log.Appended += line => Dispatch(() => LogAppended?.Invoke(line));
        _session.Log.Reset += text => Dispatch(() => LogReset?.Invoke(text));

        MeasureCommand = new AsyncRelayCommand(
            _ => RunAsync(_session.MeasureAsync),
            _ => CanMeasure);
        AverageMeasurementCommand = new AsyncRelayCommand(
            _ => ToggleAveragingMeasurementAsync(),
            _ => CanToggleAveragingMeasurement);
        CalibrateCommand = new AsyncRelayCommand(
            _ => RunAsync(_session.BeginCalibrationAsync),
            _ => CanCalibrate);
        ConfirmCalibrationCommand = new AsyncRelayCommand(
            _ => RunAsync(_session.ConfirmCalibrationAsync),
            _ => NeedsCalibrationConfirmation);
        SkipCalibrationCommand = new AsyncRelayCommand(
            _ => RunAsync(_session.SkipCalibrationAsync),
            _ => CanSkipCalibration);
        RetryCommand = new AsyncRelayCommand(
            _ => RunAsync(_session.RetryAsync),
            _ => CanRetry);
        RestartCommand = new AsyncRelayCommand(_ => RestartAsync());
        ConnectCommand = new AsyncRelayCommand(_ => ConnectInstrumentAsync());
        ChangeModeCommand = new AsyncRelayCommand(ChangeModeAsync);
        ReturnToModeSelectionCommand = new AsyncRelayCommand(_ => ReturnToModeSelectionAsync());
        DeleteCommand = new RelayCommand(_ => DeleteSelection(), _ => SelectedCount > 0);
        DeleteAllHistoryCommand = new RelayCommand(
            _ => DeleteAllHistory(),
            _ => _session.History.DeletableCount(Mode) > 0);
        MoveUpCommand = new RelayCommand(_ => MoveSelection(up: true), _ => SelectedCount > 0);
        MoveDownCommand = new RelayCommand(_ => MoveSelection(up: false), _ => SelectedCount > 0);
        SelectAllCommand = new RelayCommand(
            _ => SelectAll(),
            _ => _session.History.Ordered(Mode).Count > 0);
        DeselectAllCommand = new RelayCommand(_ => DeselectAll(), _ => SelectedCount > 0);
        ClearLogCommand = new RelayCommand(_ => _session.Log.Clear());
        RefreshCieIlluminantOptions();
    }

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<CieIlluminantOptionViewModel> CieIlluminantOptions { get; } = [];
    public AsyncRelayCommand MeasureCommand { get; }
    public AsyncRelayCommand AverageMeasurementCommand { get; }
    public AsyncRelayCommand CalibrateCommand { get; }
    public AsyncRelayCommand ConfirmCalibrationCommand { get; }
    public AsyncRelayCommand SkipCalibrationCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand ChangeModeCommand { get; }
    public AsyncRelayCommand ReturnToModeSelectionCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand DeleteAllHistoryCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public event Action<string>? LogAppended;
    public event Action<string>? LogReset;
    public event Action? HistoryRefreshed;
    public bool IsRefreshingHistory { get; private set; }

    public MeasurementMode Mode
    {
        get => _mode;
        private set
        {
            if (Set(ref _mode, value))
            {
                YAxisConfiguration = SpectrumYAxisConfiguration.ForMeasurementMode(value);
                OnPropertyChanged(nameof(IsReflectance));
                OnPropertyChanged(nameof(IsLighting));
                OnPropertyChanged(nameof(ModeTitle));
                OnPropertyChanged(nameof(ModeSubtitle));
                OnPropertyChanged(nameof(ModeDetail));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ShowsSrgbEncoding));
                OnPropertyChanged(nameof(ShowsStandardsEvaluation));
                OnPropertyChanged(nameof(ShowsReferenceControls));
                OnPropertyChanged(nameof(ShowsLightingRenderingTabs));
                OnPropertyChanged(nameof(ShowsReflectanceHistory));
                OnPropertyChanged(nameof(ShowsLightingHistory));
                OnPropertyChanged(nameof(ShowsReflectanceExport));
                OnPropertyChanged(nameof(ShowsLightingExport));
                OnPropertyChanged(nameof(ShowsMunsellValue));
                OnPropertyChanged(nameof(MunsellValueText));
            }
        }
    }

    public SpotMeasurement? ActiveMeasurement
    {
        get => _activeMeasurement;
        private set
        {
            if (Set(ref _activeMeasurement, value))
            {
                if (value?.ValidatedPracticalSpectrumRange is null && _usePracticalRange)
                {
                    _usePracticalRange = false;
                    OnPropertyChanged(nameof(UsePracticalRange));
                }
                RaiseMeasurementProperties();
                RaiseReflectanceIlluminantProperties();
            }
        }
    }

    public bool IsReflectance => Mode == MeasurementMode.Reflectance;
    public string? ActiveMeasurementName => ActiveEntry?.Name;
    public bool IsLighting => Mode.IsLighting();
    public bool IsModeSelectionVisible
    {
        get => _isModeSelectionVisible;
        private set
        {
            if (Set(ref _isModeSelectionVisible, value))
            {
                OnPropertyChanged(nameof(IsWorkspaceVisible));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }
    public bool IsWorkspaceVisible => !IsModeSelectionVisible;
    public int SelectedCount => _session.History.SelectedIdsFor(Mode).Count;
    public bool HasSelectedLab => SelectedEntries().Any(entry => entry.Measurement.Lab is not null);
    public bool HasSelectedSpectrum => SelectedEntries().Any(entry => entry.Measurement.Spectrum.Count > 0);
    public bool HasSelectedCri => SelectedEntries().Any(entry => entry.Measurement.Cri is not null);
    public bool HasSelectedTm30 => SelectedEntries().Any(entry => entry.Measurement.Tm30 is not null);
    public bool CanExportSelection => SelectedCount > 0;
    public bool HasMeasurements => HistoryItems.Count > 0;
    public int DeletableHistoryCount => _session.History.DeletableCount(Mode);
    public bool HasDeletableHistory => DeletableHistoryCount > 0;
    public bool IsBrowsingRestoredWorkspace
    {
        get => _isBrowsingRestoredWorkspace;
        private set => Set(ref _isBrowsingRestoredWorkspace, value);
    }

    public bool UsePracticalRange
    {
        get => _usePracticalRange;
        set
        {
            if (Set(ref _usePracticalRange, value))
            {
                SaveSettingsSoon();
                RaiseReflectanceIlluminantProperties();
            }
        }
    }

    public SpectrumYAxisConfiguration YAxisConfiguration
    {
        get => _spectrumYAxisConfiguration;
        private set
        {
            if (Set(ref _spectrumYAxisConfiguration, value.Normalize()))
            {
                OnPropertyChanged(nameof(IsSpectrumYAxisAutomatic));
                OnPropertyChanged(nameof(IsSpectrumYAxisFixed));
                OnPropertyChanged(nameof(SpectrumYAxisFixedUpperBound));
            }
        }
    }

    public bool IsSpectrumYAxisAutomatic
    {
        get => YAxisConfiguration.Mode == SpectrumYAxisMode.Automatic;
        set
        {
            if (value)
            {
                YAxisConfiguration = YAxisConfiguration with { Mode = SpectrumYAxisMode.Automatic };
            }
        }
    }

    public bool IsSpectrumYAxisFixed
    {
        get => YAxisConfiguration.Mode == SpectrumYAxisMode.Fixed;
        set
        {
            if (value)
            {
                YAxisConfiguration = YAxisConfiguration with { Mode = SpectrumYAxisMode.Fixed };
            }
        }
    }

    public double SpectrumYAxisFixedUpperBound
    {
        get => YAxisConfiguration.FixedUpperBound;
        set => YAxisConfiguration = YAxisConfiguration with { FixedUpperBound = value };
    }

    public bool ShowD50
    {
        get => _showD50;
        set
        {
            if (Set(ref _showD50, value))
            {
                SaveSettingsSoon();
            }
        }
    }

    public bool ShowD65
    {
        get => _showD65;
        set
        {
            if (Set(ref _showD65, value))
            {
                SaveSettingsSoon();
            }
        }
    }

    public int InstrumentIndex
    {
        get => _instrumentIndex;
        set
        {
            var normalized = Math.Clamp(value, 1, 32);
            if (Set(ref _instrumentIndex, normalized))
            {
                _session.InstrumentIndex = normalized;
                SaveSettingsSoon();
            }
        }
    }

    public string Language
    {
        get => _localization.Language;
        set
        {
            _localization.SetLanguage(value);
            SaveSettingsSoon();
            RefreshCieIlluminantOptions();
            RefreshHistory();
            RaiseReflectanceIlluminantProperties();
            OnPropertyChanged(string.Empty);
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }

    public int SelectedRenderingTabIndex
    {
        get => _selectedRenderingTabIndex;
        set => Set(ref _selectedRenderingTabIndex, value);
    }

    public CieIlluminantOptionViewModel? SelectedCieIlluminantOption
    {
        get => _selectedCieIlluminantOption;
        set
        {
            if (Set(ref _selectedCieIlluminantOption, value))
            {
                RaiseReflectanceIlluminantProperties();
            }
        }
    }

    public bool IsCieIlluminantSourceSelected
    {
        get => _reflectanceIlluminantSourceKind == ReflectanceIlluminantSourceKind.Cie;
        set
        {
            if (value) SelectReflectanceIlluminantSource(ReflectanceIlluminantSourceKind.Cie);
        }
    }

    public bool IsUser1IlluminantSourceSelected
    {
        get => _reflectanceIlluminantSourceKind == ReflectanceIlluminantSourceKind.User1;
        set
        {
            if (value) SelectReflectanceIlluminantSource(ReflectanceIlluminantSourceKind.User1);
        }
    }

    public bool IsUser2IlluminantSourceSelected
    {
        get => _reflectanceIlluminantSourceKind == ReflectanceIlluminantSourceKind.User2;
        set
        {
            if (value) SelectReflectanceIlluminantSource(ReflectanceIlluminantSourceKind.User2);
        }
    }

    public bool IsUser3IlluminantSourceSelected
    {
        get => _reflectanceIlluminantSourceKind == ReflectanceIlluminantSourceKind.User3;
        set
        {
            if (value) SelectReflectanceIlluminantSource(ReflectanceIlluminantSourceKind.User3);
        }
    }

    public bool HasUser1Illuminant =>
        _session.History.UserIlluminantEntry(UserIlluminantSlot.User1) is not null;
    public bool HasUser2Illuminant =>
        _session.History.UserIlluminantEntry(UserIlluminantSlot.User2) is not null;
    public bool HasUser3Illuminant =>
        _session.History.UserIlluminantEntry(UserIlluminantSlot.User3) is not null;

    public bool AppliesChromaticAdaptation
    {
        get => _appliesChromaticAdaptation;
        set
        {
            if (Set(ref _appliesChromaticAdaptation, value))
            {
                RaiseReflectanceIlluminantProperties();
            }
        }
    }

    public IlluminantSpectrumDefinition? SelectedIlluminantSource
    {
        get
        {
            if (_reflectanceIlluminantSourceKind == ReflectanceIlluminantSourceKind.Cie)
            {
                return SelectedCieIlluminantOption?.Illuminant is { } illuminant
                    ? IlluminantSpectrumDefinition.Cie(illuminant)
                    : null;
            }
            var slot = _reflectanceIlluminantSourceKind switch
            {
                ReflectanceIlluminantSourceKind.User1 => UserIlluminantSlot.User1,
                ReflectanceIlluminantSourceKind.User2 => UserIlluminantSlot.User2,
                _ => UserIlluminantSlot.User3,
            };
            return _session.History.UserIlluminantEntry(slot) is { } entry
                ? IlluminantSpectrumDefinition.User(
                    slot,
                    entry,
                    _localization.Language == "ja")
                : null;
        }
    }

    public ReflectanceIlluminantSpectrumResult? ReflectanceIlluminantResult =>
        ReflectanceIlluminantSpectrumCalculator.Calculate(
            ActiveMeasurement,
            SelectedIlluminantSource,
            UsePracticalRange ? ActiveMeasurement?.ValidatedPracticalSpectrumRange : null);

    public ReflectanceIlluminantColorComparisonResult? ReflectanceColorComparisonResult =>
        ReflectanceIlluminantColorComparisonCalculator.Calculate(
            ActiveMeasurement,
            SelectedIlluminantSource,
            AppliesChromaticAdaptation);

    public bool HasReflectanceIlluminantSelection => SelectedIlluminantSource is not null;
    public bool HasReflectanceColorComparison => ReflectanceColorComparisonResult is not null;
    public bool ShowsUserIlluminantMetadata =>
        SelectedIlluminantSource?.OriginKind == IlluminantSpectrumOriginKind.User;
    public string UserIlluminantMetadataText
    {
        get
        {
            var source = SelectedIlluminantSource;
            var name = source?.UserName;
            var measuredAt = source?.MeasuredAt?.ToLocalTime().ToString("g");
            return string.Join("　", new[] { name, measuredAt }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
    public string SelectedIlluminantTitle => SelectedIlluminantSource?.DisplayName ?? string.Empty;
    public string SimulatedPatchTitle => ReflectanceColorComparisonResult is null
        ? string.Empty
        : $"{SelectedIlluminantTitle}{(AppliesChromaticAdaptation ? T("・色順応", " · Adapted") : string.Empty)}";
    public Brush MeasuredReflectancePatchBrush => LabBrush(ActiveMeasurement?.Lab);
    public Brush SimulatedReflectancePatchBrush => LabBrush(ReflectanceColorComparisonResult?.SimulatedLab);
    public string DeltaE00Text => ReflectanceColorComparisonResult?.DeltaE2000.ToString("0.00") ?? "—";
    public string DeltaE76Text => ReflectanceColorComparisonResult?.DeltaE76.ToString("0.00") ?? "—";
    public string DeltaLText => SignedDifference(ReflectanceColorComparisonResult?.DeltaL);
    public string DeltaAText => SignedDifference(ReflectanceColorComparisonResult?.DeltaA);
    public string DeltaBText => SignedDifference(ReflectanceColorComparisonResult?.DeltaB);

    public string CieReferenceIlluminantLabel => T("CIE参考光源", "CIE Reference Illuminant");
    public string UserDefined1Label => UserIlluminantSlots.Title(
        UserIlluminantSlot.User1,
        _localization.Language == "ja");
    public string UserDefined2Label => UserIlluminantSlots.Title(
        UserIlluminantSlot.User2,
        _localization.Language == "ja");
    public string UserDefined3Label => UserIlluminantSlots.Title(
        UserIlluminantSlot.User3,
        _localization.Language == "ja");
    public string IlluminantComparisonGroupLabel => T(
        "光源による反射光スペクトル",
        "Reflected Spectrum by Illuminant");
    public string MeasuredReflectanceLegendLabel => T("計測反射率", "Measured Reflectance");
    public string SelectedIlluminantLegendLabel => T("選択光源", "Selected Illuminant");
    public string ReflectedLightLegendLabel => T("反射光", "Reflected Light");
    public string ColorAppearanceComparisonLabel => T("色の見え方比較", "Color Appearance Comparison");
    public string ApplyChromaticAdaptationLabel => T("色順応を適用", "Apply Chromatic Adaptation");
    public string MeasuredD50Label => T("計測値（D50）", "Measured (D50)");
    public string DifferenceAfterAdaptationLabel => AppliesChromaticAdaptation
        ? T("色順応後の差", "Difference After Adaptation")
        : T("光源白色のままの差", "Difference Without Adaptation");
    public string UvWarningText => T(
        "UVデータを含まない反射測定からの予測です。蛍光増白紙・蛍光インキでは実際の反射光と一致しない場合があります。",
        "This prediction uses a reflectance measurement without UV data. Fluorescent papers or inks may differ from the actual reflected light.");
    public string ChromaticAdaptationExplanation => AppliesChromaticAdaptation
        ? T(
            "Bradford色順応変換で選択光源の白色点をD50へ合わせ、D50 LabでΔEを計算しています。",
            "Bradford chromatic adaptation maps the selected illuminant white to D50; differences are calculated in D50 Lab.")
        : T(
            "選択光源の白色点をそのままD50 Labへ換算し、計測値との差を計算しています。",
            "The selected illuminant white is converted directly to D50 Lab without chromatic adaptation.");
    public string DeleteHistoryLabel => T("履歴を削除", "Delete History");
    public string DeleteHistoryConfirmationTitle => T(
        $"{ModeTitle}の履歴を削除しますか？",
        $"Delete {ModeTitle} History?");
    public string DeleteHistoryConfirmationMessage => T(
        $"このモードで削除可能な{DeletableHistoryCount}件の測定履歴を削除します。ユーザー定義光源に登録中の履歴は残ります。この操作は取り消せません。",
        $"Delete {DeletableHistoryCount} removable measurements in this mode. Histories registered as user illuminants will remain. This cannot be undone.");

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string HistoryPersistenceError
    {
        get => _historyPersistenceError;
        private set
        {
            if (Set(ref _historyPersistenceError, value))
            {
                OnPropertyChanged(nameof(HasHistoryPersistenceError));
            }
        }
    }
    public bool HasHistoryPersistenceError =>
        !string.IsNullOrWhiteSpace(HistoryPersistenceError);
    public string LogText => _session.Log.Text;
    public string ModeTitle => ModeText(Mode);
    public string ModeSubtitle => Mode switch
    {
        MeasurementMode.Reflectance => T("印刷物・用紙・色票", "Prints, paper, and swatches"),
        MeasurementMode.Ambient => T("照度・CRI・TLCI・TM-30", "Illuminance, CRI, TLCI, and TM-30"),
        _ => T("ディスプレイ・ライトボックス・発光体", "Displays, light boxes, and emitters"),
    };
    public string ModeDetail => Mode switch
    {
        MeasurementMode.Reflectance => T(
            "反射スペクトルとXYZ、D50 Labを測定します。",
            "Measures reflectance spectrum, XYZ, and D50 Lab."),
        MeasurementMode.Ambient => T(
            "入射光のLux、CCT、Duv、演色評価値を測定します。",
            "Measures incident-light Lux, CCT, Duv, and color rendering values."),
        _ => T(
            "対象に測定器を当て、発光分光分布とXYZ（Y＝輝度）、CCT、Duvを測定します。光源用途では演色指標も表示します。",
            "Place the instrument on the target to measure emissive spectrum, XYZ (Y = luminance), CCT, and Duv. Color rendering metrics are also shown for light-source use."),
    };
    public string WindowTitle => IsModeSelectionVisible
        ? "IwashiScope"
        : $"IwashiScope　　{ModeTitle}";
    public string TransportName => "iwashiscope-spotread.exe";
    public string StatusText => IsBrowsingRestoredWorkspace
        ? T("保存データを閲覧中", "Browsing saved measurements")
        : StateText(_session.State.Phase);
    public string StatusTitle => IsBrowsingRestoredWorkspace
        ? T("ワークスペースを表示中", "Viewing Workspace")
        : _session.State.Phase switch
        {
            MeasurementSessionPhase.Idle => T("待機中", "Idle"),
            MeasurementSessionPhase.Launching => T("spotreadを起動中", "Launching spotread"),
            MeasurementSessionPhase.CalibrationRecommended => T("キャリブレーションしてください", "Calibration Required"),
            MeasurementSessionPhase.AwaitingCalibrationSetup =>
                CalibrationTitle,
            MeasurementSessionPhase.WaitingForInstrument =>
                T("測定器を操作してください", "Operate the Instrument"),
            MeasurementSessionPhase.Calibrating => T("キャリブレーション中", "Calibrating"),
            MeasurementSessionPhase.Ready => T("測定待機中", "Ready to Measure"),
            MeasurementSessionPhase.Measuring => T("測定中", "Measuring"),
            MeasurementSessionPhase.Recovering => T("spotreadの応答を待っています", "Waiting for spotread"),
            MeasurementSessionPhase.RetryAvailable => T("操作を再試行できます", "Operation Can Be Retried"),
            MeasurementSessionPhase.ConfigurationRequired => T("測定器の設定を確認してください", "Check Instrument Configuration"),
            MeasurementSessionPhase.Workspace => T("ワークスペースを表示中", "Viewing Workspace"),
            MeasurementSessionPhase.Stopped => T("spotreadは停止しました", "spotread Has Stopped"),
            MeasurementSessionPhase.Failed => T("spotreadを実行できません", "Unable to Run spotread"),
            _ => StateText(_session.State.Phase),
        };
    public string StatusDetail => IsBrowsingRestoredWorkspace
        ? T(
            "保存された測定結果を表示しています。測定器には接続していません。",
            "Displaying saved measurements. No instrument is connected.")
        : _session.State.Phase switch
        {
            MeasurementSessionPhase.Idle => T("測定モードを選択してください。", "Select a measurement mode."),
            MeasurementSessionPhase.Launching => T(
                "測定器への接続と初期化を待っています。",
                "Waiting for instrument connection and initialization."),
            MeasurementSessionPhase.CalibrationRecommended => T(
                "測定前に測定器をキャリブレーションします。開始後、表示される手順に従ってください。",
                "Calibrate the instrument before measurement. Follow the displayed instructions after starting."),
            MeasurementSessionPhase.AwaitingCalibrationSetup =>
                $"{CalibrationInstruction}\n{T("設置後、測定器を動かさず8〜10秒程度待ってからキャリブレーションを開始してください。", "After positioning it, wait about 8–10 seconds without moving the instrument, then start calibration.")}",
            MeasurementSessionPhase.WaitingForInstrument =>
                _session.State.CurrentCalibrationPrompt?.RawText ??
                T("測定器のスイッチを押してください。", "Press the instrument switch."),
            MeasurementSessionPhase.Calibrating => T(
                "測定器から完了通知が返るまで、そのままお待ちください。",
                "Wait without moving the instrument until it reports completion."),
            MeasurementSessionPhase.Ready => Mode == MeasurementMode.Ambient
                ? T(
                    "本体の設定を環境光測定位置にし、測定位置に静置して測定ボタンまたは測定器本体のスイッチを押してください",
                    "Set the instrument to its ambient-light position, place it at the measurement location, then press Measure or the instrument switch.")
                : T(
                    "測定対象に測定器を置き、測定ボタンまたは測定器本体のスイッチを押してください。",
                    "Place the instrument on the target, then press Measure or the instrument switch."),
            MeasurementSessionPhase.Measuring => T(
                "スペクトルと測色値を取得しています。測定器を動かさないでください。",
                "Acquiring spectrum and colorimetric values. Do not move the instrument."),
            MeasurementSessionPhase.Recovering => T(
                "エラー処理が終わるまで測定器を操作しないでください。応答が戻らない場合は自動的に強制再起動します。",
                "Do not operate the instrument until error handling completes. It will force restart automatically if no response returns."),
            MeasurementSessionPhase.RetryAvailable or MeasurementSessionPhase.ConfigurationRequired =>
                _session.State.CurrentIssue?.Reason ??
                T("測定器の状態を確認してから再試行してください。", "Check the instrument state, then retry."),
            MeasurementSessionPhase.Workspace => T(
                "保存された測定結果を表示しています。測定器には接続していません。",
                "Displaying saved measurements. No instrument is connected."),
            MeasurementSessionPhase.Stopped => T(
                "再起動するか、別の測定モードを選択してください。",
                "Restart or select a different measurement mode."),
            MeasurementSessionPhase.Failed =>
                _session.State.CurrentIssue?.Reason ??
                ErrorMessage ??
                T("不明なエラーが発生しました。", "An unknown error occurred."),
            _ => string.Empty,
        };
    public bool IsCalibrationDone =>
        _session.CalibrationCompleted && _session.State.Phase == MeasurementSessionPhase.Ready;
    public bool ShowsMeasureControls =>
        !IsBrowsingRestoredWorkspace && _session.State.Phase == MeasurementSessionPhase.Ready;
    public bool ShowsInitialCalibrationControl =>
        !IsBrowsingRestoredWorkspace &&
        _session.State.Phase == MeasurementSessionPhase.CalibrationRecommended;
    public bool ShowsRetryControl =>
        _session.State.Phase is MeasurementSessionPhase.RetryAvailable or
            MeasurementSessionPhase.ConfigurationRequired;
    public bool ShowsConnectControl =>
        IsBrowsingRestoredWorkspace || _session.State.Phase == MeasurementSessionPhase.Workspace;
    public bool ShowsRestartControl =>
        _session.State.Phase is MeasurementSessionPhase.Failed or MeasurementSessionPhase.Stopped;
    public string InstrumentName => ActiveEntry?.InstrumentIdentity?.DisplayName
        ?? _session.State.Instrument?.DisplayName
        ?? T("測定器未選択", "No instrument");
    public string XyzText => VectorText(ActiveMeasurement?.Xyz, "X", "Y", "Z");
    public string LabGroupLabel => $"{ActiveMeasurement?.LabWhitePoint ?? "D50"} Lab";
    public string LabText => VectorText(ActiveMeasurement?.Lab, "L*", "a*", "b*");
    public string XText => VectorComponent(ActiveMeasurement?.Xyz?.First);
    public string YText => VectorComponent(ActiveMeasurement?.Xyz?.Second);
    public string ZText => VectorComponent(ActiveMeasurement?.Xyz?.Third);
    public string LStarText => VectorComponent(ActiveMeasurement?.Lab?.First);
    public string AStarText => VectorComponent(ActiveMeasurement?.Lab?.Second);
    public string BStarText => VectorComponent(ActiveMeasurement?.Lab?.Third);
    public string MunsellValueText =>
        Mode == MeasurementMode.Reflectance && ActiveMeasurement is { Lab: not null } measurement
            ? MunsellConverter.Convert(measurement.Spectrum)?.Formatted ?? "—"
            : "—";
    public string PeakText => ActiveMeasurement?.PeakValue is { } peak
        ? $"{peak:0.####} @ {ActiveMeasurement.PeakWavelength:0.#} nm"
        : "—";
    public string LuxText => NumberUnit(ActiveMeasurement?.Lux, "lx", "0.0");
    public string CctText => NumberUnit(ActiveMeasurement?.Cct, "K", "0");
    public string DuvText => ActiveMeasurement?.Duv?.ToString("+0.00000;-0.00000;0.00000", CultureInfo.InvariantCulture) ?? "—";
    public string EvText => NumberUnit(ActiveMeasurement?.SuggestedEv100, "EV", "0.0");
    public string CriText => ActiveMeasurement?.Cri is { } cri
        ? $"Ra {cri.Ra:0.0}  ·  R9 {cri.R9:0.0}"
        : "—";
    public string TlciText => ActiveMeasurement?.Tlci is { } tlci ? $"Qa {tlci.Qa:0.0}" : "—";
    public string Tm30Text => ActiveMeasurement?.Tm30 is { } tm30
        ? $"Rf {tm30.FidelityIndex:0.0}  ·  Rg {tm30.GamutIndex:0.0}"
        : "—";
    public string PracticalRangeText => ActiveMeasurement?.ValidatedPracticalSpectrumRange is { } range
        ? $"{range.Start:0}–{range.End:0} nm"
        : T("全波長範囲", "Full wavelength range");
    public string SrgbHex => ColorConversion?.Srgb.Hex ?? "—";
    public string SrgbRedText => RgbComponentText(ColorConversion?.Srgb.RedByte);
    public string SrgbGreenText => RgbComponentText(ColorConversion?.Srgb.GreenByte);
    public string SrgbBlueText => RgbComponentText(ColorConversion?.Srgb.BlueByte);
    public string AdobeRgbRedText => RgbComponentText(ColorConversion?.AdobeRgb.RedByte);
    public string AdobeRgbGreenText => RgbComponentText(ColorConversion?.AdobeRgb.GreenByte);
    public string AdobeRgbBlueText => RgbComponentText(ColorConversion?.AdobeRgb.BlueByte);
    public string DisplayP3RedText => RgbComponentText(ColorConversion?.DisplayP3.RedByte);
    public string DisplayP3GreenText => RgbComponentText(ColorConversion?.DisplayP3.GreenByte);
    public string DisplayP3BlueText => RgbComponentText(ColorConversion?.DisplayP3.BlueByte);
    public string SrgbGamutWarning => ColorConversion?.Srgb.IsOutOfGamut == true
        ? T("sRGB色域外（クリップ表示）", "Outside sRGB gamut (clipped)")
        : string.Empty;
    public bool HasSrgbGamutWarning => !string.IsNullOrEmpty(SrgbGamutWarning);
    public string AdobeGamutWarning => ColorConversion?.AdobeRgb.IsOutOfGamut == true
        ? T("Adobe RGB (1998)色域外（クリップ表示）", "Outside Adobe RGB (1998) gamut (clipped)")
        : string.Empty;
    public bool HasAdobeGamutWarning => !string.IsNullOrEmpty(AdobeGamutWarning);
    public string DisplayP3GamutWarning => ColorConversion?.DisplayP3.IsOutOfGamut == true
        ? T("Display P3色域外（クリップ表示）", "Outside Display P3 gamut (clipped)")
        : string.Empty;
    public bool HasDisplayP3GamutWarning => !string.IsNullOrEmpty(DisplayP3GamutWarning);
    public Brush SwatchBrush
    {
        get
        {
            var color = ColorConversion?.Srgb;
            return color is null
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromRgb(color.RedByte, color.GreenByte, color.BlueByte));
        }
    }

    public UiParityProfile UiProfile => UiParityProfiles.For(Mode);
    public bool ShowsSrgbEncoding =>
        UiProfile.Shows(UiParitySection.SrgbEncoding) && ActiveMeasurement?.Lab is not null;
    public bool ShowsStandardsEvaluation =>
        UiProfile.Shows(UiParitySection.JspstEvaluation);
    public bool ShowsReferenceControls =>
        UiProfile.Shows(UiParitySection.ReferenceSpectrumControls);
    public bool ShowsLightingRenderingTabs =>
        UiProfile.Shows(UiParitySection.LightingRenderingTabs);
    public bool ShowsReflectanceHistory =>
        UiProfile.HistorySection == UiParitySection.ReflectanceHistory;
    public bool ShowsLightingHistory =>
        UiProfile.HistorySection == UiParitySection.LightingHistory;
    public bool ShowsReflectanceExport =>
        UiProfile.ExportSection == UiParitySection.ReflectanceExport;
    public bool ShowsLightingExport =>
        UiProfile.ExportSection == UiParitySection.LightingExport;
    public bool HasActiveMeasurement => ActiveMeasurement is not null;
    public bool HasAveragedMeasurement => ActiveMeasurement?.AveragedMeasurement is not null;
    public string AveragedMeasurementBadgeText =>
        ActiveMeasurement?.AveragedMeasurement is { } averaged
            ? T($"平均 {averaged.SampleCount}回", $"Average {averaged.SampleCount}")
            : string.Empty;
    public string AveragedMeasurementCountText =>
        ActiveMeasurement?.AveragedMeasurement is { } averaged
            ? averaged.MeasurementCount is { } actual
                ? $"{averaged.SampleCount}（{actual}）"
                : averaged.SampleCount.ToString(CultureInfo.CurrentCulture)
            : "—";
    public string AveragedOutlierCountText =>
        ActiveMeasurement?.AveragedMeasurement?.OutlierCount is { } outliers
            ? outliers.ToString(CultureInfo.CurrentCulture)
            : "—";
    public string AveragedConvergenceText =>
        ActiveMeasurement?.AveragedMeasurement is
            { Relative95UncertaintyPercent: { } uncertainty,
              ConvergenceTier: { } tier }
            ? $"{ConvergenceName(tier)}・95% ±{uncertainty:0.00}%"
            : "—";
    public bool HasLab => ActiveMeasurement?.Lab is not null;
    public bool ShowsMunsellValue =>
        Mode == MeasurementMode.Reflectance && ActiveMeasurement?.Lab is not null;
    public bool HasMonochrome => ActiveMeasurement?.Monochrome is not null;
    public bool HasLightingMetrics => ActiveMeasurement is { } measurement &&
        (measurement.Lux is not null ||
         measurement.Cct is not null ||
         measurement.Duv is not null ||
         measurement.SuggestedEv100 is not null ||
         measurement.ClosestPlanckian is not null ||
         measurement.ClosestDaylight is not null ||
         measurement.LightingMetricIssues.Count > 0);
    public bool HasCriOrTlci => ActiveMeasurement?.Cri is not null || ActiveMeasurement?.Tlci is not null;
    public bool HasCri => ActiveMeasurement?.Cri is not null;
    public bool HasTlci => ActiveMeasurement?.Tlci is not null;
    public bool HasTm30 => ActiveMeasurement?.Tm30 is not null;
    public bool HasLux => ActiveMeasurement?.Lux is not null;
    public bool HasCct => ActiveMeasurement?.Cct is not null;
    public bool HasDuv => ActiveMeasurement?.Duv is not null;
    public bool HasEv => ActiveMeasurement?.SuggestedEv100 is not null;
    public bool HasClosestPlanckian => ActiveMeasurement?.ClosestPlanckian is not null;
    public bool HasClosestDaylight => ActiveMeasurement?.ClosestDaylight is not null;
    public bool HasPracticalRange => ActiveMeasurement?.ValidatedPracticalSpectrumRange is not null;
    public string MeasurementTimeText => ActiveMeasurement?.CapturedAt
        .ToLocalTime()
        .ToString("T", CultureInfo.CurrentCulture) ?? string.Empty;
    public string MonochromeYText => ActiveMeasurement?.Monochrome?.Y.ToString("0.000", CultureInfo.CurrentCulture) ?? "—";
    public string MonochromeLText => ActiveMeasurement?.Monochrome?.LStar.ToString("0.000", CultureInfo.CurrentCulture) ?? "—";
    public string ClosestPlanckianText => ActiveMeasurement?.ClosestPlanckian is { } value
        ? $"{value.Kelvin:0} K  ·  ΔE00 {value.DeltaE2000:0.0}"
        : "—";
    public string ClosestDaylightText => ActiveMeasurement?.ClosestDaylight is { } value
        ? $"{value.Kelvin:0} K  ·  ΔE00 {value.DeltaE2000:0.0}"
        : "—";
    public string CriRaText => ActiveMeasurement?.Cri is { } cri ? $"{cri.Ra:0.0}" : "—";
    public string TlciQaText => ActiveMeasurement?.Tlci is { } tlci ? $"{tlci.Qa:0.0}" : "—";
    public string Tm30RfText => ActiveMeasurement?.Tm30 is { } tm30 ? $"{tm30.FidelityIndex:0.0}" : "—";
    public string Tm30RgText => ActiveMeasurement?.Tm30 is { } tm30 ? $"{tm30.GamutIndex:0.0}" : "—";
    public string Tm30CautionText => ActiveMeasurement?.Tm30?.Caution == true
        ? T("基準光の適用範囲外です", "Outside the reference illuminant applicability range")
        : string.Empty;
    public bool HasTm30Caution => ActiveMeasurement?.Tm30?.Caution == true;
    public string InstrumentMetadataName => InstrumentName;
    public string WavelengthRangeText => ActiveMeasurement is { } measurement
        ? $"{measurement.SpectrumStart:0.#}–{measurement.SpectrumEnd:0.#} nm"
        : IsBrowsingRestoredWorkspace ? T("保存データなし", "No saved data") : T("測定後に表示", "Shown after measurement");
    public string DataPointCountText => ActiveMeasurement is { } measurement
        ? measurement.Spectrum.Count.ToString(CultureInfo.CurrentCulture)
        : IsBrowsingRestoredWorkspace ? T("保存データなし", "No saved data") : T("測定後に表示", "Shown after measurement");
    public string PracticalWavelengthRangeText => ActiveMeasurement?.ValidatedPracticalSpectrumRange is { } range
        ? $"{range.Start:0.#}–{range.End:0.#} nm"
        : T("保存データなし", "No saved data");
    public string ModeSelectionTitle => T("測定モードを選択", "Select Measurement Mode");
    public string ToolTagline => T("分光・測色ツール", "Spectral and Color Measurement Tool");
    public string ModeSelectionDetail => T(
        "選択後にspotreadを高解像度モードで起動します",
        "spotread starts in high-resolution mode after selection");
    public string StartModeLabel => T("このモードで開始", "Start in This Mode");
    public bool IsSpotreadAvailable => ExecutableLocator.FindSpotread() is not null;
    public string SpotreadAvailabilityText => IsSpotreadAvailable
        ? T("spotreadを確認しました", "spotread was found")
        : T(
            "spotreadが見つかりません。起動後に設定方法を表示します。",
            "spotread was not found. Setup instructions will be shown after startup.");
    public bool IsSpotreadRunning => _session.IsRunning;
    public string DebugStatusText => IsSpotreadRunning
        ? T("spotread実行中", "spotread Running")
        : T("spotread停止中", "spotread Stopped");
    public string DebugModeText => IsModeSelectionVisible ? "—" : ModeTitle;
    public string DebugPathText => _session.ExecutablePath ?? "—";
    public string MeasurementValuesLabel => T("測定値", "Measurements");
    public string MeasurementResultLabel => T("測定結果", "Measurement Result");
    public string NoMeasurementTitle => T("測定結果はまだありません", "No measurements yet");
    public string NoMeasurementDetail => T(
        "測定すると、ここへスペクトルを表示します。",
        "Take a measurement to display its spectrum here.");
    public string WaitingForValuesTitle => T("測定値を待っています", "Waiting for a measurement");
    public string WaitingForValuesDetail => T(
        "測定後、解析した値をここへ表示します。",
        "Analyzed values appear here after measurement.");
    public string HistoryFooterTooltip => T(
        "クリックで展開・縮小、上下ドラッグで高さを調整",
        "Click to expand or collapse; drag vertically to resize");
    public string ColorimetricValuesLabel => T("測色値", "Colorimetric Values");
    public string MunsellValueLabel => T("マンセル値", "Munsell Value");
    public string MunsellValuePremiseLabel => T(
        "（CIE標準イルミナントC・CIE 1931 2°標準観測者）",
        "(CIE Standard Illuminant C · CIE 1931 2° Standard Observer)");
    public string LabChartAccessibilityLabel => T("a*b*グラフ", "a*b* Graph");
    public string LabChartAccessibilityValue => ActiveMeasurement?.Lab is { } lab
        ? $"a* {lab.Second:0.000}, b* {lab.Third:0.000}"
        : T("データなし", "No data");
    public string LightingInformationLabel => T("光源情報", "Lighting Information");
    public string InstrumentAndSerialLabel => T("測定器名（シリアル）", "Instrument (Serial)");
    public string WavelengthRangeLabel => T("波長範囲", "Wavelength Range");
    public string DataPointCountLabel => T("データ点数", "Data Points");
    public string PracticalWavelengthRangeLabel => T("実用波長範囲", "Practical Wavelength Range");
    public string UsePracticalAreaLabel => T("実用エリアを使用する", "Use Practical Area");
    public string SpectrumYAxisLabel => T("縦軸", "Vertical Axis");
    public string SpectrumYAxisAutomaticLabel => T("自動", "Automatic");
    public string SpectrumYAxisFixedLabel => T("固定", "Fixed");
    public string SpectrumYAxisSliderAccessibilityLabel => T(
        "スペクトル縦軸の固定上限",
        "Fixed spectrum vertical-axis upper bound");
    public string ReferenceSpectrumLabel => T("基準分光分布", "Reference Spectral Distribution");
    public string ReferenceNormalizationLabel => T(
        "560 nmで測定値に合わせて表示",
        "Normalized to the measurement at 560 nm");
    public string D50ReferenceLabel => T("CIE D50（ISO 3664参照）", "CIE D50 (ISO 3664 reference)");
    public string D65ReferenceLabel => T("CIE D65（ISO 3668参照）", "CIE D65 (ISO 3668 reference)");
    public string RenderingGroupLabel => T(
        "演色評価数（Color Rendering Index / CRI）",
        "Color Rendering Index (CRI)");
    public string Tm30VectorLegendText => T(
        "基準光　　測定光　　色相ビンの変位",
        "Reference　　Test　　Hue-bin shift");
    public string Tm30GuideOneText => T(
        "① プランク軌跡上の光源（概略）",
        "① Source on Planckian locus (approx.)");
    public string Tm30GuideTwoText => T(
        "② 実用光源（概略）",
        "② Practical source (approx.)");
    public string Tm30RfRgPlotLabel => T("Rf–Rgプロット", "Rf–Rg Plot");
    public string Tm30SamplesLabel => T("99色評価用試料", "99 Color Evaluation Samples");
    public string Tm30SamplesAxisLabel => T("色評価用試料（CES）", "Color Evaluation Samples (CES)");
    public string ExportSelectionText => Language == "ja"
        ? $"{SelectedCount}件選択"
        : $"{SelectedCount} selected";

    public string JspstSummary
    {
        get
        {
            if (ActiveMeasurement is not { } measurement)
            {
                return "—";
            }
            var result = PrintingViewingConditionEvaluator.Evaluate(measurement);
            return $"{PrintingViewingConditionEvaluator.StandardName}: {result.SummaryStatus} · " +
                   $"Δu′v′ {result.ChromaticityDistance:0.00000} · " +
                   $"{result.IlluminanceClassification}";
        }
    }

    public string IsoSummary
    {
        get
        {
            if (ActiveMeasurement is not { } measurement)
            {
                return "—";
            }
            var result = Iso3664NumericEvaluator.Evaluate(measurement);
            return $"{Iso3664NumericEvaluator.StandardName}: {result.SummaryStatus} · " +
                   $"{result.IlluminanceCondition} · " +
                   T("測定可能な数値項目のみ。完全適合判定ではありません。", "Measurable numeric criteria only; not a complete conformity assessment.");
        }
    }

    private PrintingViewingConditionEvaluation? JspstEvaluation =>
        ActiveMeasurement is { } measurement
            ? PrintingViewingConditionEvaluator.Evaluate(measurement)
            : null;
    private Iso3664NumericEvaluation? IsoEvaluation =>
        ActiveMeasurement is { } measurement
            ? Iso3664NumericEvaluator.Evaluate(measurement)
            : null;

    public string JspstSummaryTitle
    {
        get
        {
            if (JspstEvaluation is not { } evaluation)
            {
                return T("数値基準を判定できません", "Unable to assess numeric criteria");
            }
            if (Mode == MeasurementMode.Ambient)
            {
                if (evaluation.SummaryStatus == EvaluationStatus.Meets)
                {
                    return T("測定した数値項目が基準範囲内", "Measured numeric criteria are within range");
                }
                if (evaluation.SummaryStatus == EvaluationStatus.Unavailable)
                {
                    return T("数値基準を判定できません", "Unable to assess numeric criteria");
                }
                return evaluation.IlluminanceClassification switch
                {
                    IlluminanceClassification.TooDark => T(
                        "数値基準外（事務所衛生基準規則第10条に不適）",
                        "Outside numeric criteria (does not meet Article 10 of the Ordinance on Health Standards in the Office)"),
                    IlluminanceClassification.DisplayComparison or
                        IlluminanceClassification.GeneralOffice or
                        IlluminanceClassification.TooBright => T(
                            "数値基準外（印刷物同士の比較に不適）",
                            "Outside numeric criteria (unsuitable for comparing printed matter)"),
                    _ => T("数値基準外", "Outside numeric criteria"),
                };
            }
            return evaluation.LightSourceStatus switch
            {
                EvaluationStatus.Meets => T(
                    "光源の測定した数値項目が基準範囲内",
                    "Measured light-source numeric criteria are within range"),
                EvaluationStatus.Unavailable => T(
                    "光源の数値基準を判定できません",
                    "Unable to assess light-source numeric criteria"),
                _ => T("光源の数値基準外", "Light-source numeric criteria are outside range"),
            };
        }
    }

    public string JspstSummaryDetail
    {
        get
        {
            if (Mode == MeasurementMode.Emissive)
            {
                return T(
                    "D50色度と演色性を評価しています。作業面照度は環境光モードで測定してください。",
                    "Evaluating D50 chromaticity and color rendering. Measure work-surface illuminance in ambient mode.");
            }
            return JspstEvaluation?.IlluminanceClassification switch
            {
                IlluminanceClassification.DisplayComparison => T(
                    "D50色度と演色性を評価しています。作業面照度はディスプレイとの比較に適しています。",
                    "Evaluating D50 chromaticity and color rendering. Work-surface illuminance is suitable for display comparison."),
                IlluminanceClassification.TooDark => T(
                    "作業面照度が一般的な事務作業の300 lxを下回っています。",
                    "Work-surface illuminance is below 300 lx for ordinary office work."),
                _ => T(
                    "D50色度、演色性、作業面照度の測定可能な数値項目を評価しています。",
                    "Evaluating measurable numeric criteria for D50 chromaticity, color rendering, and work-surface illuminance."),
            };
        }
    }

    public string JspstCctText => ActiveMeasurement?.Cct is { } value ? $"{value:0} K" : T("データなし", "No data");
    public string JspstChromaticityText => JspstEvaluation?.ChromaticityDistance is { } value
        ? $"{value:0.00000}"
        : T("データなし", "No data");
    public string JspstRaText => JspstEvaluation?.AverageCri is { } value
        ? $"{value:0.0}"
        : T("データなし", "No data");
    public string JspstMinimumRiText => JspstEvaluation?.MinimumSpecialCri is { } value
        ? $"{value.Value:0.0}"
        : T("データなし", "No data");
    public string JspstIlluminanceText => Mode == MeasurementMode.Emissive
        ? T("環境光モードで測定", "Measure in Ambient Mode")
        : JspstEvaluation?.Illuminance is { } value
            ? $"{value:0} lx"
            : T("データなし", "No data");
    public string JspstIlluminanceRequirement => JspstEvaluation?.IlluminanceClassification switch
    {
        IlluminanceClassification.DisplayComparison => T(
            "ディスプレイとの比較に適している",
            "Suitable for display comparison"),
        IlluminanceClassification.GeneralOffice or IlluminanceClassification.TooDark => T(
            "事務所衛生基準規則：一般的な事務作業 300 lx以上",
            "Office health standard: at least 300 lx for ordinary office work"),
        _ => T("基準 1,500〜2,500 lx", "Criterion: 1,500–2,500 lx"),
    };

    public string IsoSummaryTitle
    {
        get
        {
            var prefix = Mode == MeasurementMode.Ambient
                ? T("測定した数値項目", "Measured numeric criteria")
                : T("演色性の測定項目", "Measured color-rendering criteria");
            return IsoEvaluation?.SummaryStatus switch
            {
                EvaluationStatus.Meets => T(
                    $"{prefix}が基準範囲内",
                    $"{prefix} are within the criteria"),
                EvaluationStatus.Caution or EvaluationStatus.DoesNotMeet or EvaluationStatus.Fails => T(
                    $"{prefix}が基準範囲外",
                    $"{prefix} are outside the criteria"),
                _ => T(
                    $"{prefix}を判定できません",
                    $"Unable to assess {prefix}"),
            };
        }
    }
    public string IsoSummaryDetail => Mode == MeasurementMode.Emissive
        ? T(
            "RfとRaを評価しています。P3・P4の照度は環境光モードで測定してください。完全な規格適合判定ではありません。",
            "Evaluating Rf and Ra. Measure P3/P4 illuminance in ambient mode. This is not a complete conformity assessment.")
        : T(
            "Rf、RaとP3・P4の照度範囲のみを評価しています。完全な規格適合判定ではありません。",
            "Only Rf, Ra, and P3/P4 illuminance ranges are evaluated. This is not a complete conformity assessment.");
    public string IsoRfText => IsoEvaluation?.FidelityIndex is { } rf ? $"{rf:0.0}" : T("データなし", "No data");
    public string IsoRaText => IsoEvaluation?.AverageCri is { } ra ? $"{ra:0.0}" : T("データなし", "No data");
    public string IsoIlluminanceText
    {
        get
        {
            var measured = IsoEvaluation?.Illuminance is { } lux ? $"（{lux:0} lx）" : string.Empty;
            return IsoEvaluation?.IlluminanceCondition switch
            {
                Iso3664IlluminanceCondition.P3 => T($"P3照度範囲{measured}", $"P3 illuminance range{measured}"),
                Iso3664IlluminanceCondition.P4 => T($"P4照度範囲{measured}", $"P4 illuminance range{measured}"),
                Iso3664IlluminanceCondition.Outside => T(
                    $"P3・P4照度範囲外{measured}",
                    $"Outside P3/P4 illuminance ranges{measured}"),
                _ => Mode == MeasurementMode.Emissive
                    ? T("環境光モードで測定", "Measure in Ambient Mode")
                    : T("データなし", "No data"),
            };
        }
    }

    public bool CanMeasure =>
        !IsBrowsingRestoredWorkspace &&
        _session.State.Phase is MeasurementSessionPhase.Ready or MeasurementSessionPhase.Workspace;
    public bool ShowsAveragingControls =>
        !IsBrowsingRestoredWorkspace &&
        (_session.SupportsSpectrumAnalysis || _session.IsAveragingMeasurement) &&
        (_session.State.Phase == MeasurementSessionPhase.Ready ||
         _session.IsAveragingMeasurement);
    public bool CanToggleAveragingMeasurement =>
        !IsBrowsingRestoredWorkspace &&
        !_session.IsFinalizingAveragingMeasurement &&
        _session.State.Phase == MeasurementSessionPhase.Ready &&
        (_session.IsCollectingAveragingMeasurements ||
         (_session.SupportsSpectrumAnalysis && !_session.IsAveragingMeasurement));
    public bool IsAveragingMeasurement => _session.IsAveragingMeasurement;
    public bool IsFinalizingAveragingMeasurement =>
        _session.IsFinalizingAveragingMeasurement;
    public int AveragingAcceptedCount => _session.AveragingAccumulator.AcceptedCount;
    public int AveragingMeasurementCount =>
        _session.AveragingAccumulator.MeasurementAttemptCount;
    public int AveragingOutlierCount => _session.AveragingAccumulator.OutlierCount;
    public double AveragingProgressValue =>
        100d * AveragingAcceptedCount / AveragingMeasurementAccumulator.MaximumCount;
    public string AveragingProgressText =>
        $"{AveragingAcceptedCount}（{AveragingMeasurementCount}）/{AveragingMeasurementAccumulator.MaximumCount}";
    public double AveragingIndicatorOpacity =>
        !IsAveragingMeasurement || AveragingMeasurementCount == 0 ? 0.28 : 1;
    public Brush AveragingProgressBrush =>
        _session.AveragingAccumulator.ProgressTier switch
        {
            AveragingProgressTier.Minimum => BrushFrom("#A86C38"),
            AveragingProgressTier.Recommended => BrushFrom("#2784D9"),
            AveragingProgressTier.Sufficient => BrushFrom("#2E9B57"),
            _ => BrushFrom("#D34A43"),
        };
    public string AveragingOutlierText => T(
        $"異常値 {AveragingOutlierCount}件",
        $"Outliers {AveragingOutlierCount}");
    public string AveragingConvergenceText
    {
        get
        {
            if (_session.AveragingAccumulator.Convergence is not { } convergence)
            {
                return T("収束度 —（6回から表示）", "Convergence — (shown from 6 readings)");
            }
            var name = ConvergenceName(convergence.Tier);
            return T(
                $"収束度 {name}・95% ±{convergence.Relative95UncertaintyPercent:0.00}%",
                $"Convergence {name} · 95% ±{convergence.Relative95UncertaintyPercent:0.00}%");
        }
    }
    public double AveragingConvergenceValue =>
        100d * (_session.AveragingAccumulator.Convergence?.Progress ?? 0);
    public double AveragingConvergenceOpacity =>
        _session.AveragingAccumulator.Convergence is null ? 0.28 : 1;
    public Brush AveragingConvergenceBrush =>
        _session.AveragingAccumulator.Convergence?.Tier switch
        {
            AveragingConvergenceTier.Converging => BrushFrom("#A86C38"),
            AveragingConvergenceTier.Stable => BrushFrom("#2784D9"),
            AveragingConvergenceTier.SufficientlyStable => BrushFrom("#2E9B57"),
            _ => BrushFrom("#D34A43"),
        };
    public string AveragingMessageText => _session.AveragingMessage is { } message
        ? Language == "ja" ? message.Japanese : message.English
        : string.Empty;
    public bool HasAveragingMessage => !string.IsNullOrWhiteSpace(AveragingMessageText);
    public string AverageMeasurementButtonLabel
    {
        get
        {
            if (_session.IsFinalizingAveragingMeasurement)
            {
                return T("平均値を再計算中…", "Recalculating Average…");
            }
            if (!_session.IsAveragingMeasurement)
            {
                return T("平均化測定を開始", "Start Averaging");
            }
            return _session.AveragingAccumulator.CanOutputAverage
                ? T("平均値を出力", "Output Average")
                : T("平均値モードを終了", "End Averaging Mode");
        }
    }
    public bool CanCalibrate =>
        !IsBrowsingRestoredWorkspace &&
        _session.State.Phase is MeasurementSessionPhase.Ready or
            MeasurementSessionPhase.Workspace or
            MeasurementSessionPhase.CalibrationRecommended;
    public bool CanRetry =>
        _session.State.Phase is MeasurementSessionPhase.RetryAvailable or
            MeasurementSessionPhase.ConfigurationRequired or
            MeasurementSessionPhase.Failed;
    public bool IsBusy =>
        !IsBrowsingRestoredWorkspace &&
        _session.State.Phase is MeasurementSessionPhase.Launching or
            MeasurementSessionPhase.Measuring or
            MeasurementSessionPhase.Calibrating or
            MeasurementSessionPhase.Recovering;
    public string BusyMessage => StateText(_session.State.Phase);
    public bool NeedsCalibrationConfirmation =>
        _session.State.Phase == MeasurementSessionPhase.AwaitingCalibrationSetup;
    public bool CanSkipCalibration =>
        NeedsCalibrationConfirmation &&
        _session.State.CurrentCalibrationPrompt?.AllowsSkip == true;
    private CalibrationPromptPresentation CalibrationPresentation =>
        CalibrationPromptPresentations.For(
            _session.State.CurrentCalibrationPrompt,
            _localization);
    public string CalibrationTitle => CalibrationPresentation.Title;
    public string CalibrationInstruction => CalibrationPresentation.Instruction;

    public string AppTitle => "IwashiScope";
    public string FileLabel => T("ファイル", "File");
    public string ViewLabel => T("表示", "View");
    public string HelpLabel => T("ヘルプ", "Help");
    public string JapaneseLabel => T("日本語", "Japanese");
    public string EnglishLabel => T("英語", "English");
    public string LicensesAndSourceLabel => T(
        "ライセンスとソースコード",
        "Licenses and Source Code");
    public string CheckForUpdatesLabel => T(
        "アップデートを確認…",
        "Check for Updates…");
    public string OpenWorkspaceLabel => T("ワークスペースを復帰...", "Restore Workspace...");
    public string SaveWorkspaceLabel => T("ワークスペースを保存...", "Save Workspace...");
    public string ExportLabel => T("書き出し...", "Export...");
    public string ExportActionLabel => T("書き出し", "Export");
    public string SwatchExportLabel => T("スウォッチ", "Swatch");
    public string SpectrumPngExportLabel => T(
        "スペクトル画像（幅3,000 px PNG）",
        "Spectrum Image (3,000 px-wide PNG)");
    public string SpectrumCsvExportLabel => T("スペクトルCSV", "Spectrum CSV");
    public string CriPngExportLabel => T(
        "CRI画像（幅3,000 px PNG）",
        "CRI Image (3,000 px-wide PNG)");
    public string Tm30PngExportLabel => T(
        "TM-30-15画像（幅3,000 px PNG）",
        "TM-30-15 Image (3,000 px-wide PNG)");
    public string LightingCsvExportLabel => T(
        "CSV（スペクトル、CRI、TM-30-15）",
        "CSV (Spectrum, CRI, TM-30-15)");
    public string MeasureLabel => T("測定", "Measure");
    public string AveragingQualityLabel => T("平均化測定", "Averaging Measurement");
    public string AveragedReadingCountLabel => T("採用数（実測数）", "Accepted (Measured)");
    public string AveragedOutlierCountLabel => T("異常値数", "Outliers");
    public string AveragedConvergenceLabel => T("収束度", "Convergence");
    public string CalibrateLabel => T("キャリブレーション", "Calibrate");
    public string RecalibrateLabel => T("再キャリブレーション", "Recalibrate");
    public string ContinueLabel => T("キャリブレーション", "Calibrate");
    public string SkipLabel => T("今回はスキップ", "Skip This Time");
    public string RetryLabel => T("再試行", "Retry");
    public string RestartLabel => T("spotreadを強制再起動", "Force Restart spotread");
    public string ConnectLabel => T("測定器に接続", "Connect Instrument");
    public string ReturnToModeSelectionLabel => T("モード選択へ戻る", "Back to Mode Selection");
    public string RgbValuesLabel => T("RGB値", "RGB Values");
    public string SpectrumLabel => T("スペクトル", "Spectrum");
    public string ColorRenderingLabel => T("演色評価", "Color Rendering");
    public string EvaluationLabel => T("規格評価", "Standards");
    public string LogLabel => T("spotread詳細ログ", "Detailed spotread Log");
    public string HistoryLabel => T("測定履歴", "Measurement History");
    public string SettingsLabel => T("設定", "Settings");
    public string PracticalLabel => T("実用波長範囲", "Practical wavelength range");
    public string ClearLogLabel => T("ログを消去", "Clear Log");
    public string FollowLatestLabel => T("最新へ追従", "Follow Latest");
    public string D50Label => T("D50線", "D50 Curve");
    public string D65Label => T("D65線", "D65 Curve");
    public string ReflectanceTitle => T("反射原稿", "Reflectance");
    public string AmbientTitle => T("環境光", "Ambient Light");
    public string EmissiveTitle => T("発光", "Emissive");
    public string ReflectanceSubtitle => T("印刷物・用紙・色票", "Prints, paper, and swatches");
    public string AmbientSubtitle => T("照度・CRI・TLCI・TM-30", "Illuminance, CRI, TLCI, and TM-30");
    public string EmissiveSubtitle => T(
        "ディスプレイ・ライトボックス・発光体",
        "Displays, light boxes, and emitters");
    public string ReflectanceDetail => T(
        "反射スペクトルとXYZ、D50 Labを測定します。",
        "Measures reflectance spectrum, XYZ, and D50 Lab.");
    public string AmbientDetail => T(
        "入射光のLux、CCT、Duv、演色評価値を測定します。",
        "Measures incident-light Lux, CCT, Duv, and color rendering values.");
    public string EmissiveDetail => T(
        "対象に測定器を当て、発光分光分布とXYZ（Y＝輝度）、CCT、Duvを測定します。光源用途では演色指標も表示します。",
        "Place the instrument on the target to measure emissive spectrum, XYZ (Y = luminance), CCT, and Duv. Color rendering metrics are also shown for light-source use.");
    public string JspstGroupLabel => T("印刷学会基準（JSPST-1998）", "JSPST-1998");
    public string IsoGroupLabel => T(
        "ISO 3664:2025（測定可能な数値項目）",
        "ISO 3664:2025 (Measurable Numeric Criteria)");
    public string IlluminanceLabel => T("照度", "Illuminance");
    public string SuggestedEvLabel => T("推奨EV（ISO 100）", "Suggested EV (ISO 100)");
    public string ClosestPlanckianLabel => T("最接近黒体軌跡", "Closest Planckian Locus");
    public string ClosestDaylightLabel => T("最接近昼光軌跡", "Closest Daylight Locus");
    public string JspstCctLabel => T("相関色温度", "Correlated Color Temperature");
    public string JspstChromaticityLabel => T("D50色度差 Δu′v′", "D50 Chromaticity Difference Δu′v′");
    public string AverageCriLabel => T("平均演色評価数 Ra", "Average Color Rendering Index Ra");
    public string MinimumRiLabel => T("最小Ri（R9〜R15）", "Minimum Ri (R9–R15)");
    public string WorkSurfaceIlluminanceLabel => T("作業面照度", "Work-surface Illuminance");
    public string IsoRfLabel => T("TM-30-15 Rf", "TM-30-15 Rf");
    public string P1P2Label => T("P1・P2観察条件", "P1/P2 Viewing Conditions");
    public string P3P4Label => T("P3・P4観察条件", "P3/P4 Viewing Conditions");
    public string NotAssessedLabel => T("判定しない", "Not Assessed");
    public string UvUnavailableLabel => T("UV条件を測定できないため", "UV conditions cannot be measured");
    public string JspstCctRequirement => T("目標 5000 K（許容幅規定なし）", "Target 5000 K (no tolerance specified)");
    public string JspstChromaticityRequirement => T("基準 ≤ 0.00400", "Criterion ≤ 0.00400");
    public string JspstRaRequirement => T("基準 ≥ 95.0", "Criterion ≥ 95.0");
    public string JspstRiRequirement => T("基準 ≥ 90.0", "Criterion ≥ 90.0");
    public string IsoRfRequirement => T("基準 ≥ 95.0", "Criterion ≥ 95.0");
    public string IsoRaRequirement => T("基準 > 90.0", "Criterion > 90.0");
    public string IsoIlluminanceRequirement => T(
        "P3 1,500〜2,500 lx／P4 375〜625 lx（照度のみ）",
        "P3 1,500–2,500 lx / P4 375–625 lx (illuminance only)");

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        _localization.SetLanguage(_settings.Language);
        try
        {
            if (await _historyPersistenceStore.LoadAsync() is { } persistedHistory)
            {
                persistedHistory.RestoreInto(_session.History);
                _lastPersistedHistoryFingerprint = HistoryPersistenceFingerprint(
                    persistedHistory);
            }
        }
        catch (Exception exception)
        {
            HistoryPersistenceError = T(
                $"保存された測定履歴を読み込めませんでした。{exception.Message}",
                $"Unable to load saved measurement history. {exception.Message}");
        }
        _usePracticalRange = false;
        _showD50 = false;
        _showD65 = false;
        _instrumentIndex = _settings.InstrumentIndex;
        _session.InstrumentIndex = _instrumentIndex;
        RefreshCieIlluminantOptions();
        RefreshHistory();
        OnPropertyChanged(string.Empty);
        if (Enum.TryParse<MeasurementMode>(
                Environment.GetEnvironmentVariable("IWASHISCOPE_AUTOSTART_MODE"),
                ignoreCase: true,
                out var autoStartMode))
        {
            await ChangeModeAsync(autoStartMode);
        }
    }

    public IReadOnlyList<MeasurementHistoryEntry> SelectedEntries() =>
        _session.History.Ordered(Mode)
            .Where(entry => _session.History.SelectedIdsFor(Mode).Contains(entry.Id))
            .ToArray();

    public IReadOnlyList<MeasurementHistoryEntry> OrderedEntries() =>
        _session.History.Ordered(Mode);

    public void SynchronizeSelection(IEnumerable<Guid> ids, Guid? activeId)
    {
        var selected = ids.ToArray();
        _session.History.SetSelection(
            Mode,
            selected,
            activeId,
            _session.History.AnchorIdFor(Mode));
        RefreshHistory();
    }

    public void DeleteSelection()
    {
        _session.History.DeleteSelected(Mode);
        RefreshHistory();
    }

    public void DeleteAllHistory()
    {
        _session.History.DeleteAll(Mode);
        RefreshHistory();
    }

    public bool RegisterUserIlluminant(Guid entryId, UserIlluminantSlot slot)
    {
        var registered = _session.History.RegisterUserIlluminant(entryId, slot);
        if (registered)
        {
            RefreshHistory();
            RaiseReflectanceIlluminantProperties();
        }
        return registered;
    }

    public int RemoveUserIlluminantRegistrations(Guid entryId)
    {
        var removed = _session.History.RemoveUserIlluminantRegistrations(entryId);
        if (removed > 0)
        {
            EnsureSelectedUserIlluminantRemainsAvailable();
            RefreshHistory();
            RaiseReflectanceIlluminantProperties();
        }
        return removed;
    }

    public void SelectAll()
    {
        _session.History.SelectAll(Mode);
        RefreshHistory();
    }

    public void DeselectAll()
    {
        _session.History.DeselectAll(Mode);
        RefreshHistory();
    }

    public void ReorderSelectionBefore(Guid? targetId)
    {
        if (targetId is { } target)
        {
            _session.History.MoveSelectionBefore(Mode, target);
        }
        else
        {
            _session.History.MoveSelectionToEnd(Mode);
        }
        RefreshHistory();
    }

    public WorkspaceDocument CreateWorkspaceDocument() =>
        WorkspaceDocument.Create(
            _session.History,
            IsModeSelectionVisible ? null : Mode,
            SelectedTabIndex == 1
                ? MeasurementSidebarTab.SpotreadLog
                : MeasurementSidebarTab.MeasurementValues);

    public async Task SaveWorkspaceAsync(string path)
    {
        var document = CreateWorkspaceDocument();
        await AtomicFile.WriteAllTextAsync(path, WorkspaceSerializer.Serialize(document));
        _lastSavedFingerprint = WorkspaceFingerprint(document);
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    public async Task RestoreWorkspaceAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var document = WorkspaceSerializer.Deserialize(json);
        await _session.StopAsync();
        document.RestoreInto(_session.History);
        Mode = document.Workspace.SelectedMode ?? MeasurementMode.Reflectance;
        SelectedTabIndex = document.Workspace.SelectedSidebarTab == MeasurementSidebarTab.SpotreadLog
            ? 1
            : 0;
        IsBrowsingRestoredWorkspace = true;
        IsModeSelectionVisible = document.Workspace.SelectedMode is null;
        _lastSavedFingerprint = WorkspaceFingerprint(document);
        ErrorMessage = string.Empty;
        RefreshHistory();
    }

    public bool HasUnsavedChanges =>
        _lastSavedFingerprint is null
            ? _session.History.AcquisitionOrder.Count > 0
            : _lastSavedFingerprint != WorkspaceFingerprint(CreateWorkspaceDocument());

    public async Task ConnectInstrumentAsync()
    {
        IsBrowsingRestoredWorkspace = false;
        await StartCurrentModeAsync();
    }

    public async Task StopInstrumentAsync()
    {
        await _session.StopAsync();
        RefreshFromSession();
    }

    public async ValueTask DisposeAsync()
    {
        _historyPersistenceCancellation?.Cancel();
        await _historyPersistenceTask;
        await SaveCurrentHistoryAsync();
        await SaveSettingsAsync();
        await _session.DisposeAsync();
    }

    private MeasurementHistoryEntry? ActiveEntry => _session.History.ActiveEntryFor(Mode);
    private LabColorConversion? ColorConversion =>
        ActiveMeasurement?.Lab is { } lab
            ? LabColorConverter.Convert(lab, ActiveMeasurement.LabWhitePoint)
            : null;

    private ProcessLaunchSpec LaunchSpec(MeasurementMode mode)
    {
        var real = ExecutableLocator.FindSpotread()
            ?? throw new FileNotFoundException(
                "iwashiscope-spotread.exe was not found. Place it beside IwashiScope.exe or set IWASHISCOPE_SPOTREAD_PATH.");
        return ExecutableLocator.Real(real);
    }

    private async Task ChangeModeAsync(object? parameter)
    {
        if (parameter is not MeasurementMode mode)
        {
            return;
        }
        var startsFromSelection = IsModeSelectionVisible;
        if (!startsFromSelection && mode == Mode)
        {
            return;
        }
        Mode = mode;
        IsModeSelectionVisible = false;
        UsePracticalRange = false;
        ShowD50 = false;
        ShowD65 = false;
        SelectedTabIndex = 0;
        SelectedRenderingTabIndex = 0;
        ErrorMessage = string.Empty;
        RefreshHistory();
        if (!IsBrowsingRestoredWorkspace)
        {
            await StartCurrentModeAsync();
        }
    }

    private async Task ReturnToModeSelectionAsync()
    {
        await _session.StopAsync();
        IsModeSelectionVisible = true;
        SelectedTabIndex = 0;
        SelectedRenderingTabIndex = 0;
        ErrorMessage = string.Empty;
        RefreshFromSession();
    }

    private async Task RestartAsync()
    {
        IsBrowsingRestoredWorkspace = false;
        await StartCurrentModeAsync();
    }

    private Task StartCurrentModeAsync() =>
        RunAsync(cancellationToken => _session.StartAsync(Mode, cancellationToken));

    private Task ToggleAveragingMeasurementAsync()
    {
        if (_session.IsAveragingMeasurement)
        {
            return RunAsync(_session.FinishOrCancelAveragingMeasurementAsync);
        }

        _session.StartAveragingMeasurement();
        RefreshFromSession();
        return Task.CompletedTask;
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        string? operationError = null;
        try
        {
            ErrorMessage = string.Empty;
            await operation(CancellationToken.None);
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        RefreshFromSession(operationError);
    }

    private void SessionChanged() => Dispatch(() => RefreshFromSession());

    private void RefreshFromSession(string? operationError = null)
    {
        var currentSidebarTab = SelectedTabIndex == 1
            ? MeasurementSidebarTab.SpotreadLog
            : MeasurementSidebarTab.MeasurementValues;
        var selectedSidebarTab = _sidebarTabCoordinator.Observe(
            _session.State.Phase,
            _session.CalibrationCompleted,
            IsBrowsingRestoredWorkspace,
            currentSidebarTab);
        SelectedTabIndex = selectedSidebarTab == MeasurementSidebarTab.SpotreadLog
            ? 1
            : 0;

        ErrorMessage = SessionErrorPresentation.Resolve(
            _session.State.CurrentIssue,
            operationError);
        RefreshHistory();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(IsCalibrationDone));
        OnPropertyChanged(nameof(ShowsMeasureControls));
        OnPropertyChanged(nameof(ShowsInitialCalibrationControl));
        OnPropertyChanged(nameof(ShowsRetryControl));
        OnPropertyChanged(nameof(ShowsConnectControl));
        OnPropertyChanged(nameof(ShowsRestartControl));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyMessage));
        OnPropertyChanged(nameof(IsSpotreadRunning));
        OnPropertyChanged(nameof(DebugStatusText));
        OnPropertyChanged(nameof(DebugModeText));
        OnPropertyChanged(nameof(DebugPathText));
        OnPropertyChanged(nameof(InstrumentName));
        OnPropertyChanged(nameof(InstrumentMetadataName));
        OnPropertyChanged(nameof(CanMeasure));
        OnPropertyChanged(nameof(ShowsAveragingControls));
        OnPropertyChanged(nameof(CanToggleAveragingMeasurement));
        OnPropertyChanged(nameof(IsAveragingMeasurement));
        OnPropertyChanged(nameof(IsFinalizingAveragingMeasurement));
        OnPropertyChanged(nameof(AveragingAcceptedCount));
        OnPropertyChanged(nameof(AveragingMeasurementCount));
        OnPropertyChanged(nameof(AveragingOutlierCount));
        OnPropertyChanged(nameof(AveragingProgressValue));
        OnPropertyChanged(nameof(AveragingProgressText));
        OnPropertyChanged(nameof(AveragingIndicatorOpacity));
        OnPropertyChanged(nameof(AveragingProgressBrush));
        OnPropertyChanged(nameof(AveragingOutlierText));
        OnPropertyChanged(nameof(AveragingConvergenceText));
        OnPropertyChanged(nameof(AveragingConvergenceValue));
        OnPropertyChanged(nameof(AveragingConvergenceOpacity));
        OnPropertyChanged(nameof(AveragingConvergenceBrush));
        OnPropertyChanged(nameof(AveragingMessageText));
        OnPropertyChanged(nameof(HasAveragingMessage));
        OnPropertyChanged(nameof(AverageMeasurementButtonLabel));
        OnPropertyChanged(nameof(CanCalibrate));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(NeedsCalibrationConfirmation));
        OnPropertyChanged(nameof(CanSkipCalibration));
        OnPropertyChanged(nameof(CalibrationTitle));
        OnPropertyChanged(nameof(CalibrationInstruction));
        MeasureCommand.RaiseCanExecuteChanged();
        AverageMeasurementCommand.RaiseCanExecuteChanged();
        CalibrateCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        ConfirmCalibrationCommand.RaiseCanExecuteChanged();
        SkipCalibrationCommand.RaiseCanExecuteChanged();
    }

    private void RefreshHistory()
    {
        IsRefreshingHistory = true;
        try
        {
            var entries = _session.History.Ordered(Mode);
            var selected = _session.History.SelectedIdsFor(Mode);
            HistoryItems.Clear();
            for (var index = 0; index < entries.Count; index++)
            {
                HistoryItems.Add(new HistoryItemViewModel(
                    entries[index],
                    index + 1,
                    selected.Contains(entries[index].Id),
                    _session.History.UserIlluminantSlotsFor(entries[index].Id),
                    _localization.Language == "ja",
                    Rename));
            }
            if (_session.IsAveragingMeasurement &&
                _session.AveragingAccumulator.LatestAcceptedMeasurement is { } preview)
            {
                HistoryItems.Add(
                    HistoryItemViewModel.AveragingStack(
                        preview,
                        _session.AveragingAccumulator.AcceptedCount,
                        T("平均化測定中", "Averaging"),
                        _localization.Language == "ja"));
            }
        }
        finally
        {
            IsRefreshingHistory = false;
        }

        ActiveMeasurement = _session.IsAveragingMeasurement
            ? _session.LatestMeasurement
            : ActiveEntry?.Measurement;
        RaiseReflectanceIlluminantProperties();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ActiveMeasurementName));
        OnPropertyChanged(nameof(HasMeasurements));
        OnPropertyChanged(nameof(DeletableHistoryCount));
        OnPropertyChanged(nameof(HasDeletableHistory));
        OnPropertyChanged(nameof(DeleteHistoryConfirmationTitle));
        OnPropertyChanged(nameof(DeleteHistoryConfirmationMessage));
        OnPropertyChanged(nameof(InstrumentName));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ExportSelectionText));
        OnPropertyChanged(nameof(HasSelectedLab));
        OnPropertyChanged(nameof(HasSelectedSpectrum));
        OnPropertyChanged(nameof(HasSelectedCri));
        OnPropertyChanged(nameof(HasSelectedTm30));
        OnPropertyChanged(nameof(CanExportSelection));
        DeleteCommand.RaiseCanExecuteChanged();
        DeleteAllHistoryCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        HistoryRefreshed?.Invoke();
        ScheduleHistoryPersistence();
    }

    private void Rename(Guid id, string? name)
    {
        _session.History.Rename(id, name);
        OnPropertyChanged(nameof(ActiveMeasurementName));
        RaiseReflectanceIlluminantProperties();
        ScheduleHistoryPersistence();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void MoveSelection(bool up)
    {
        var ordered = _session.History.Ordered(Mode);
        var selected = _session.History.SelectedIdsFor(Mode);
        if (selected.Count == 0)
        {
            return;
        }

        var indices = ordered
            .Select((entry, index) => (entry, index))
            .Where(pair => selected.Contains(pair.entry.Id))
            .Select(pair => pair.index)
            .ToArray();
        if (up)
        {
            if (indices[0] == 0)
            {
                _session.History.MoveSelectionToEnd(Mode);
            }
            else
            {
                _session.History.MoveSelectionBefore(Mode, ordered[indices[0] - 1].Id);
            }
        }
        else if (indices[^1] == ordered.Count - 1)
        {
            _session.History.MoveSelectionToStart(Mode);
        }
        else
        {
            _session.History.MoveSelectionAfter(Mode, ordered[indices[^1] + 1].Id);
        }
        RefreshHistory();
    }

    private void RaiseMeasurementProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(XyzText), nameof(LabGroupLabel), nameof(LabText),
                     nameof(XText), nameof(YText), nameof(ZText),
                     nameof(LStarText), nameof(AStarText), nameof(BStarText),
                     nameof(MunsellValueText), nameof(LabChartAccessibilityValue),
                     nameof(PeakText), nameof(LuxText),
                     nameof(CctText), nameof(DuvText), nameof(EvText), nameof(CriText),
                     nameof(TlciText), nameof(Tm30Text), nameof(PracticalRangeText),
                     nameof(SrgbHex),
                     nameof(SrgbRedText), nameof(SrgbGreenText), nameof(SrgbBlueText),
                     nameof(AdobeRgbRedText), nameof(AdobeRgbGreenText), nameof(AdobeRgbBlueText),
                     nameof(DisplayP3RedText), nameof(DisplayP3GreenText), nameof(DisplayP3BlueText),
                     nameof(SrgbGamutWarning), nameof(HasSrgbGamutWarning),
                     nameof(AdobeGamutWarning), nameof(HasAdobeGamutWarning),
                     nameof(DisplayP3GamutWarning), nameof(HasDisplayP3GamutWarning),
                     nameof(SwatchBrush),
                     nameof(JspstSummary), nameof(IsoSummary),
                     nameof(ShowsSrgbEncoding), nameof(HasActiveMeasurement),
                     nameof(HasAveragedMeasurement), nameof(AveragedMeasurementBadgeText),
                     nameof(AveragedMeasurementCountText), nameof(AveragedOutlierCountText),
                     nameof(AveragedConvergenceText), nameof(HasLab),
                     nameof(ShowsMunsellValue),
                     nameof(HasMonochrome), nameof(HasLightingMetrics), nameof(HasCriOrTlci),
                     nameof(HasCri), nameof(HasTlci), nameof(HasTm30), nameof(HasLux),
                     nameof(HasCct), nameof(HasDuv), nameof(HasEv),
                     nameof(HasClosestPlanckian), nameof(HasClosestDaylight),
                     nameof(HasPracticalRange), nameof(MeasurementTimeText),
                     nameof(MonochromeYText), nameof(MonochromeLText),
                     nameof(ClosestPlanckianText), nameof(ClosestDaylightText),
                     nameof(CriRaText), nameof(TlciQaText), nameof(Tm30RfText),
                     nameof(Tm30RgText), nameof(Tm30CautionText), nameof(HasTm30Caution),
                     nameof(InstrumentMetadataName), nameof(WavelengthRangeText),
                     nameof(DataPointCountText), nameof(PracticalWavelengthRangeText),
                     nameof(JspstSummaryTitle), nameof(JspstSummaryDetail),
                     nameof(JspstCctText), nameof(JspstChromaticityText),
                     nameof(JspstRaText), nameof(JspstMinimumRiText),
                     nameof(JspstIlluminanceText), nameof(JspstIlluminanceRequirement),
                     nameof(IsoSummaryTitle), nameof(IsoSummaryDetail),
                     nameof(IsoRfText), nameof(IsoRaText), nameof(IsoIlluminanceText),
                  })
        {
            OnPropertyChanged(property);
        }
    }

    private void RefreshCieIlluminantOptions()
    {
        var selectedIlluminant = SelectedCieIlluminantOption?.Illuminant;
        CieIlluminantOptions.Clear();
        CieIlluminantOptions.Add(new CieIlluminantOptionViewModel(
            null,
            CieReferenceIlluminantLabel));
        foreach (var illuminant in Enum.GetValues<CieReferenceIlluminant>())
        {
            CieIlluminantOptions.Add(new CieIlluminantOptionViewModel(
                illuminant,
                CieReferenceIlluminants.DisplayName(
                    illuminant,
                    _localization.Language == "ja")));
        }
        SelectedCieIlluminantOption = CieIlluminantOptions.FirstOrDefault(option =>
            option.Illuminant == selectedIlluminant) ?? CieIlluminantOptions[0];
    }

    private void SelectReflectanceIlluminantSource(
        ReflectanceIlluminantSourceKind sourceKind)
    {
        var isAvailable = sourceKind switch
        {
            ReflectanceIlluminantSourceKind.User1 => HasUser1Illuminant,
            ReflectanceIlluminantSourceKind.User2 => HasUser2Illuminant,
            ReflectanceIlluminantSourceKind.User3 => HasUser3Illuminant,
            _ => true,
        };
        if (!isAvailable || _reflectanceIlluminantSourceKind == sourceKind)
        {
            return;
        }
        _reflectanceIlluminantSourceKind = sourceKind;
        RaiseReflectanceIlluminantProperties();
    }

    private void EnsureSelectedUserIlluminantRemainsAvailable()
    {
        var isAvailable = _reflectanceIlluminantSourceKind switch
        {
            ReflectanceIlluminantSourceKind.User1 => HasUser1Illuminant,
            ReflectanceIlluminantSourceKind.User2 => HasUser2Illuminant,
            ReflectanceIlluminantSourceKind.User3 => HasUser3Illuminant,
            _ => true,
        };
        if (!isAvailable)
        {
            _reflectanceIlluminantSourceKind = ReflectanceIlluminantSourceKind.Cie;
        }
    }

    private void RaiseReflectanceIlluminantProperties()
    {
        EnsureSelectedUserIlluminantRemainsAvailable();
        foreach (var property in new[]
                 {
                     nameof(IsCieIlluminantSourceSelected),
                     nameof(IsUser1IlluminantSourceSelected),
                     nameof(IsUser2IlluminantSourceSelected),
                     nameof(IsUser3IlluminantSourceSelected),
                     nameof(HasUser1Illuminant),
                     nameof(HasUser2Illuminant),
                     nameof(HasUser3Illuminant),
                     nameof(SelectedIlluminantSource),
                     nameof(ReflectanceIlluminantResult),
                     nameof(ReflectanceColorComparisonResult),
                     nameof(HasReflectanceIlluminantSelection),
                     nameof(HasReflectanceColorComparison),
                     nameof(ShowsUserIlluminantMetadata),
                     nameof(UserIlluminantMetadataText),
                     nameof(SelectedIlluminantTitle),
                     nameof(SimulatedPatchTitle),
                     nameof(MeasuredReflectancePatchBrush),
                     nameof(SimulatedReflectancePatchBrush),
                     nameof(DeltaE00Text),
                     nameof(DeltaE76Text),
                     nameof(DeltaLText),
                     nameof(DeltaAText),
                     nameof(DeltaBText),
                     nameof(DifferenceAfterAdaptationLabel),
                     nameof(ChromaticAdaptationExplanation),
                 })
        {
            OnPropertyChanged(property);
        }
    }

    private static Brush LabBrush(Vector3? lab)
    {
        if (lab is not { IsFinite: true })
        {
            return Brushes.LightGray;
        }
        var color = LabColorConverter.Convert(lab, "D50").Srgb;
        return new SolidColorBrush(Color.FromRgb(color.RedByte, color.GreenByte, color.BlueByte));
    }

    private static string SignedDifference(double? value) =>
        value?.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) ?? "—";

    private WorkspaceDocument CreateHistoryPersistenceDocument() =>
        WorkspaceDocument.Create(
            _session.History,
            null,
            MeasurementSidebarTab.MeasurementValues);

    private void ScheduleHistoryPersistence()
    {
        var document = CreateHistoryPersistenceDocument();
        var fingerprint = HistoryPersistenceFingerprint(document);
        if (fingerprint == _lastPersistedHistoryFingerprint)
        {
            return;
        }

        _historyPersistenceCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _historyPersistenceCancellation = cancellation;
        var previousTask = _historyPersistenceTask;
        _historyPersistenceTask = PersistHistoryAfterDelayAsync(
            previousTask,
            document,
            fingerprint,
            cancellation.Token);
    }

    private async Task PersistHistoryAfterDelayAsync(
        Task previousTask,
        WorkspaceDocument document,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            await previousTask;
            await Task.Delay(250, cancellationToken);
            await _historyPersistenceStore.SaveAsync(document, cancellationToken);
            _lastPersistedHistoryFingerprint = fingerprint;
            Dispatch(() => HistoryPersistenceError = string.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Dispatch(() => HistoryPersistenceError = T(
                $"測定履歴を保存できませんでした。{exception.Message}",
                $"Unable to save measurement history. {exception.Message}"));
        }
    }

    private async Task SaveCurrentHistoryAsync()
    {
        try
        {
            var document = CreateHistoryPersistenceDocument();
            await _historyPersistenceStore.SaveAsync(document);
            _lastPersistedHistoryFingerprint = HistoryPersistenceFingerprint(document);
            HistoryPersistenceError = string.Empty;
        }
        catch (Exception exception)
        {
            HistoryPersistenceError = T(
                $"測定履歴を保存できませんでした。{exception.Message}",
                $"Unable to save measurement history. {exception.Message}");
        }
    }

    private static string HistoryPersistenceFingerprint(WorkspaceDocument document) =>
        WorkspaceSerializer.Serialize(document with { SavedAt = DateTimeOffset.UnixEpoch });

    private void SaveSettingsSoon() => _ = SaveSettingsAsync();

    private Task SaveSettingsAsync()
    {
        _settings = new AppSettings
        {
            Language = _localization.Language,
            UsePracticalSpectrumRange = UsePracticalRange,
            IncludeD50Reference = ShowD50,
            IncludeD65Reference = ShowD65,
            InstrumentIndex = InstrumentIndex,
        };
        return _settingsStore.SaveAsync(_settings);
    }

    private static string WorkspaceFingerprint(WorkspaceDocument document) =>
        WorkspaceSerializer.Serialize(document with { SavedAt = DateTimeOffset.UnixEpoch });

    private static string RgbComponentText(byte? component) =>
        component?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static string VectorComponent(double? component) =>
        component?.ToString("0.000", CultureInfo.CurrentCulture) ?? "—";

    private string T(string japanese, string english) => _localization.Text(japanese, english);

    private string ConvergenceName(AveragingConvergenceTier tier) => tier switch
    {
        AveragingConvergenceTier.HighVariation => T("変動大", "High variation"),
        AveragingConvergenceTier.Converging => T("収束中", "Converging"),
        AveragingConvergenceTier.Stable => T("安定", "Stable"),
        AveragingConvergenceTier.SufficientlyStable => T("十分に安定", "Sufficiently stable"),
        _ => tier.ToString(),
    };

    private static Brush BrushFrom(string value) =>
        (Brush)new BrushConverter().ConvertFromString(value)!;

    private string ModeText(MeasurementMode mode) => mode switch
    {
        MeasurementMode.Reflectance => T("反射原稿", "Reflectance"),
        MeasurementMode.Ambient => T("環境光", "Ambient Light"),
        _ => T("発光", "Emissive"),
    };

    private string StateText(MeasurementSessionPhase phase) => phase switch
    {
        MeasurementSessionPhase.Idle => T("待機中", "Idle"),
        MeasurementSessionPhase.Launching => T("測定器を起動中", "Launching instrument"),
        MeasurementSessionPhase.WaitingForInstrument => T("測定器を確認中", "Waiting for instrument"),
        MeasurementSessionPhase.Ready => T("測定できます", "Ready to measure"),
        MeasurementSessionPhase.Measuring => T("測定中…", "Measuring…"),
        MeasurementSessionPhase.Calibrating => T("校正中…", "Calibrating…"),
        MeasurementSessionPhase.AwaitingCalibrationSetup => T("校正の準備をしてください", "Prepare calibration"),
        MeasurementSessionPhase.Recovering => T("接続を復旧中…", "Recovering connection…"),
        MeasurementSessionPhase.RetryAvailable => T("再試行できます", "Retry available"),
        MeasurementSessionPhase.ConfigurationRequired => T("設定を確認してください", "Configuration required"),
        MeasurementSessionPhase.Failed => T("接続に失敗しました", "Connection failed"),
        MeasurementSessionPhase.Workspace => T("測定結果", "Measurement result"),
        MeasurementSessionPhase.Stopped => T("停止中", "Stopped"),
        _ => phase.ToString(),
    };

    private static string VectorText(Vector3? vector, string first, string second, string third) =>
        vector is null
            ? "—"
            : $"{first} {vector.First:0.00}   {second} {vector.Second:0.00}   {third} {vector.Third:0.00}";

    private static string NumberUnit(double? value, string unit, string format) =>
        value is { } number
            ? $"{number.ToString(format, CultureInfo.InvariantCulture)} {unit}"
            : "—";

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action);
        }
    }
}
