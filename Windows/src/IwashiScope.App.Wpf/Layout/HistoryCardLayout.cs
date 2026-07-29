using System.Windows;

namespace IwashiScope.App.Wpf.Layout;

public static class HistoryCardLayout
{
    public const double Width = 110;
    public const double Height = 110;

    public const double ReflectanceColorAreaHeight = 75;
    public const double ReflectanceLabAreaHeight = 35;
    public const double ReflectanceNameFieldHeight = 20;
    public const double ReflectanceNameFieldBottomPadding = 3;
    public const double ReflectanceNameFieldTop =
        ReflectanceColorAreaHeight -
        ReflectanceNameFieldHeight -
        ReflectanceNameFieldBottomPadding;

    public const double LightingSpectrumHeight = 44;
    public const double LightingDividerHeight = 1;
    public const double LightingCriHeight = 44;
    public const double LightingTitleAreaHeight = 21;

    public static GridLength ReflectanceColorArea =>
        new(ReflectanceColorAreaHeight);

    public static GridLength ReflectanceLabArea =>
        new(ReflectanceLabAreaHeight);

    public static GridLength LightingContentArea =>
        new(LightingSpectrumHeight + LightingDividerHeight + LightingCriHeight);

    public static GridLength LightingTitleArea =>
        new(LightingTitleAreaHeight);

    public static Thickness ItemMargin => new(6, 3, 6, 8);

    public static Thickness ReflectanceNameMargin =>
        new(6, ReflectanceNameFieldTop, 6, 0);
}
