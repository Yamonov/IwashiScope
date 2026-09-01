using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.Layout;

public enum UiParitySection
{
    Spectrum,
    ReferenceSpectrumControls,
    ReflectanceIlluminantComparison,
    LightingRenderingTabs,
    MeasurementHeader,
    ColorimetricValues,
    SrgbEncoding,
    AdobeRgbEncoding,
    DisplayP3Encoding,
    LightingInformation,
    JspstEvaluation,
    Iso3664Evaluation,
    CriTlciSummary,
    ReflectanceHistory,
    LightingHistory,
    ReflectanceExport,
    LightingExport,
    DetailedLog,
}

public sealed record UiParityProfile(
    MeasurementMode Mode,
    IReadOnlyList<UiParitySection> CentralSections,
    IReadOnlyList<UiParitySection> MeasurementValueSections,
    UiParitySection HistorySection,
    UiParitySection ExportSection)
{
    public bool Shows(UiParitySection section) =>
        CentralSections.Contains(section) ||
        MeasurementValueSections.Contains(section) ||
        HistorySection == section ||
        ExportSection == section ||
        section == UiParitySection.DetailedLog;
}

public static class UiParityProfiles
{
    private static readonly IReadOnlyList<UiParitySection> ReflectanceValues =
    [
        UiParitySection.MeasurementHeader,
        UiParitySection.ColorimetricValues,
        UiParitySection.SrgbEncoding,
        UiParitySection.AdobeRgbEncoding,
        UiParitySection.DisplayP3Encoding,
        UiParitySection.LightingInformation,
        UiParitySection.CriTlciSummary,
    ];

    private static readonly IReadOnlyList<UiParitySection> AmbientValues =
    [
        UiParitySection.MeasurementHeader,
        UiParitySection.ColorimetricValues,
        UiParitySection.LightingInformation,
        UiParitySection.JspstEvaluation,
        UiParitySection.Iso3664Evaluation,
        UiParitySection.CriTlciSummary,
    ];

    private static readonly IReadOnlyList<UiParitySection> EmissiveValues =
    [
        UiParitySection.MeasurementHeader,
        UiParitySection.ColorimetricValues,
        UiParitySection.SrgbEncoding,
        UiParitySection.AdobeRgbEncoding,
        UiParitySection.DisplayP3Encoding,
        UiParitySection.LightingInformation,
        UiParitySection.JspstEvaluation,
        UiParitySection.Iso3664Evaluation,
        UiParitySection.CriTlciSummary,
    ];

    public static UiParityProfile For(MeasurementMode mode) => mode switch
    {
        MeasurementMode.Reflectance => new UiParityProfile(
            mode,
            [
                UiParitySection.Spectrum,
                UiParitySection.ReflectanceIlluminantComparison,
            ],
            ReflectanceValues,
            UiParitySection.ReflectanceHistory,
            UiParitySection.ReflectanceExport),
        MeasurementMode.Ambient => new UiParityProfile(
            mode,
            [
                UiParitySection.Spectrum,
                UiParitySection.ReferenceSpectrumControls,
                UiParitySection.LightingRenderingTabs,
            ],
            AmbientValues,
            UiParitySection.LightingHistory,
            UiParitySection.LightingExport),
        MeasurementMode.Emissive => new UiParityProfile(
            mode,
            [
                UiParitySection.Spectrum,
                UiParitySection.ReferenceSpectrumControls,
                UiParitySection.LightingRenderingTabs,
            ],
            EmissiveValues,
            UiParitySection.LightingHistory,
            UiParitySection.LightingExport),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
