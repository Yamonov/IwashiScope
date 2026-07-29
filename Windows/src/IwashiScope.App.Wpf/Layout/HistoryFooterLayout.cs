using IwashiScope.Core.Models;

namespace IwashiScope.App.Wpf.Layout;

public static class HistoryFooterLayout
{
    public const double MinimumCollapsedHeight = 100;
    public const double MaximumCollapsedFraction = 0.75;
    public const double ExpandedFraction = 0.9;
    public const double EstimatedSpectrumAnalysisHeight = 466;
    public const double EstimatedLightingRenderingHeight = 480;
    public const double RenderingGroupSpacing = 16;

    public static double EstimatedAnalysisHeight(MeasurementMode mode) =>
        mode == MeasurementMode.Reflectance
            ? EstimatedSpectrumAnalysisHeight
            : EstimatedSpectrumAnalysisHeight +
              RenderingGroupSpacing +
              EstimatedLightingRenderingHeight;

    public static double CollapsedHeight(
        double availableHeight,
        double analysisContentHeight,
        MeasurementMode mode)
    {
        if (!double.IsFinite(availableHeight) || availableHeight <= 0)
        {
            return 0;
        }
        var resolvedAnalysis = double.IsFinite(analysisContentHeight) &&
                               analysisContentHeight > 0
            ? analysisContentHeight
            : EstimatedAnalysisHeight(mode);
        var unused = Math.Max(0, availableHeight - resolvedAnalysis);
        var preferred = Math.Max(MinimumCollapsedHeight, unused);
        return Math.Min(
            availableHeight,
            Math.Min(preferred, availableHeight * MaximumCollapsedFraction));
    }

    public static double Height(
        double availableHeight,
        double collapsedHeight,
        double? manuallyResizedHeight,
        bool expanded)
    {
        if (!double.IsFinite(availableHeight) || availableHeight <= 0)
        {
            return 0;
        }
        if (expanded)
        {
            return availableHeight * ExpandedFraction;
        }
        return manuallyResizedHeight is { } manual && double.IsFinite(manual)
            ? ResizedHeight(availableHeight, manual)
            : Math.Min(Math.Max(0, collapsedHeight), availableHeight);
    }

    public static double ResizedHeight(double availableHeight, double proposedHeight)
    {
        if (!double.IsFinite(availableHeight) ||
            availableHeight <= 0 ||
            !double.IsFinite(proposedHeight))
        {
            return 0;
        }
        var maximum = availableHeight * ExpandedFraction;
        var minimum = Math.Min(MinimumCollapsedHeight, maximum);
        return Math.Min(maximum, Math.Max(minimum, proposedHeight));
    }
}
