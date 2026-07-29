using IwashiScope.App.Wpf.Localization;
using IwashiScope.Core.Session;

namespace IwashiScope.App.Wpf.ViewModels;

public sealed record CalibrationPromptPresentation(
    string Title,
    string Instruction);

public static class CalibrationPromptPresentations
{
    public static CalibrationPromptPresentation For(
        CalibrationPrompt? prompt,
        LocalizationCatalog localization)
    {
        string T(string japanese, string english) =>
            localization.Text(japanese, english);

        var condition = prompt?.Condition ?? "unknown";
        var identifier = DisplayableIdentifier(prompt?.Identifier);

        return condition switch
        {
            "reflectiveWhite" or "reflectiveWhiteClick" =>
                new(
                    T("白色基準でキャリブレーション", "Calibrate on White Reference"),
                    identifier is null
                        ? T(
                            "測定器を付属の白色基準に置いてください。",
                            "Place the instrument on its supplied white reference.")
                        : localization.Language == "ja"
                            ? $"測定器を付属の白色基準（S/N {identifier}）に置いてください。"
                            : $"Place the instrument on its supplied white reference (S/N {identifier})."),
            "sensorCalibrationPosition" =>
                new(
                    T("測定器を校正位置へ", "Move the Instrument to Calibration Position"),
                    T(
                        "センサーのダイヤルをキャリブレーション位置に合わせてください。",
                        "Set the sensor dial to the calibration position.")),
            "ambientDark" =>
                new(
                    T("環境光アダプターを遮光", "Block Light from the Ambient Adapter"),
                    T(
                        "環境光アダプターを取り付け、キャップで遮光してください。",
                        "Attach the ambient-light adapter and block it with its cap.")),
            "emissiveDark" =>
                new(
                    T("測定器を遮光", "Block Light from the Instrument"),
                    T(
                        "測定器にキャップを付けるか、暗い面または校正基準に置いてください。",
                        "Cap the instrument or place it on a dark surface or calibration reference.")),
            "reflectiveDark" =>
                new(
                    T("暗部キャリブレーション", "Dark Calibration"),
                    T(
                        "測定器をライトトラップに置くか、周囲の面から離して遮光してください。",
                        "Place the instrument on a light trap, or keep it away from surrounding surfaces and block all light.")),
            "glossBlack" =>
                new(
                    T("黒色光沢基準でキャリブレーション", "Calibrate on Glossy Black Reference"),
                    T(
                        "測定器を黒色光沢基準に置いてください。",
                        "Place the instrument on the glossy black reference.")),
            "transmissiveWhite" or "userOperatedTransmissiveWhite" =>
                new(
                    T("透過白基準でキャリブレーション", "Calibrate on Transmission White Reference"),
                    T(
                        "測定器を透過白基準光源に置き、光路を安定させてください。",
                        "Place the instrument on the transmission white-reference source and stabilize the optical path.")),
            "transmissiveDark" or "userOperatedTransmissiveDark" =>
                new(
                    T("透過暗部キャリブレーション", "Transmission Dark Calibration"),
                    T(
                        "適切な遮光材で透過光路を完全に遮ってください。",
                        "Completely block the transmission optical path with suitable light-blocking material.")),
            "userOperatedReflectiveWhite" =>
                new(
                    T("反射白基準でキャリブレーション", "Calibrate on Reflective White Reference"),
                    T(
                        "測定器の手順に従って反射白基準を測定してください。",
                        "Follow the instrument instructions to measure the reflective white reference.")),
            "changeFilter" =>
                new(
                    T("測定器のフィルターを変更", "Change the Instrument Filter"),
                    identifier is null
                        ? T(
                            "spotreadが指定するフィルターへ変更してください。",
                            "Change to the filter specified by spotread.")
                        : localization.Language == "ja"
                            ? $"測定器のフィルターを「{identifier}」に変更してください。"
                            : $"Change the instrument filter to “{identifier}”."),
            "emissiveWhite" =>
                new(
                    T("白色パッチを測定", "Measure a White Patch"),
                    T(
                        "測定器を100%の白色パッチに置いてください。",
                        "Place the instrument on a 100% white patch.")),
            "emissive80Percent" =>
                new(
                    T("白色パッチを測定", "Measure a White Patch"),
                    T(
                        "測定器を80%の白色パッチに置いてください。",
                        "Place the instrument on an 80% white patch.")),
            "emissiveGrey" or "emissiveGreyDarker" or "emissiveGreyLighter" =>
                new(
                    T("グレーパッチを測定", "Measure a Gray Patch"),
                    T(
                        "測定器の表示に従い、指定された明るさのグレーパッチを表示してください。",
                        "Follow the instrument display and show a gray patch at the specified brightness.")),
            "message" when identifier is not null =>
                new(
                    T("キャリブレーションの準備", "Prepare Calibration"),
                    identifier),
            _ =>
                new(
                    T("キャリブレーションの準備", "Prepare Calibration"),
                    T(
                        "測定器の表示に従って準備してください。",
                        "Follow the instrument display to prepare for calibration.")),
        };
    }

    private static string? DisplayableIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            identifier.Contains('\uFFFD') ||
            identifier.Any(char.IsControl))
        {
            return null;
        }
        return identifier.Trim();
    }
}
