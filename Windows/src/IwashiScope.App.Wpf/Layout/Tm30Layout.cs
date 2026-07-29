using System.Windows;
using System.Windows.Controls;

namespace IwashiScope.App.Wpf.Layout;

public sealed record Tm30LayoutResult(
    Rect Vector,
    Rect Scores,
    Rect RfRg,
    Rect Samples)
{
    public IReadOnlyList<Rect> Rectangles => [Vector, Scores, RfRg, Samples];
}

public static class Tm30Layout
{
    public const double Padding = 8;
    public const double Gap = 12;
    public const double WideThreshold = 720;

    public static Tm30LayoutResult Calculate(double availableWidth, double availableHeight)
    {
        if (!double.IsFinite(availableWidth) ||
            !double.IsFinite(availableHeight) ||
            availableWidth <= 0 ||
            availableHeight <= 0)
        {
            return new Tm30LayoutResult(Rect.Empty, Rect.Empty, Rect.Empty, Rect.Empty);
        }

        var innerWidth = Math.Max(1, availableWidth - 2 * Padding);
        var innerHeight = Math.Max(1, availableHeight - 2 * Padding);
        var vectorSide = Math.Max(
            1,
            Math.Min(innerHeight, innerWidth * (availableWidth >= WideThreshold ? 0.44 : 0.48)));
        var vector = new Rect(
            Padding,
            Padding + Math.Max(0, (innerHeight - vectorSide) / 2),
            vectorSide,
            vectorSide);
        var rightLeft = vector.Right + Gap;
        var rightWidth = Math.Max(1, availableWidth - Padding - rightLeft);

        if (availableWidth >= WideThreshold)
        {
            var topHeight = Math.Max(1, innerHeight * 0.52);
            var scoresWidth = Math.Min(140, Math.Max(105, rightWidth * 0.28));
            var scores = new Rect(rightLeft, Padding, scoresWidth, topHeight);
            var rfRg = new Rect(
                scores.Right + Gap,
                Padding,
                Math.Max(1, rightWidth - scoresWidth - Gap),
                topHeight);
            var samples = new Rect(
                rightLeft,
                Padding + topHeight + Gap,
                rightWidth,
                Math.Max(1, innerHeight - topHeight - Gap));
            return new Tm30LayoutResult(vector, scores, rfRg, samples);
        }

        var scoreHeight = Math.Min(88, Math.Max(64, innerHeight * 0.20));
        var plotHeight = Math.Min(155, Math.Max(105, innerHeight * 0.34));
        var sampleTop = Padding + scoreHeight + Gap + plotHeight + Gap;
        var scoresNarrow = new Rect(rightLeft, Padding, rightWidth, scoreHeight);
        var rfRgNarrow = new Rect(
            rightLeft,
            Padding + scoreHeight + Gap,
            rightWidth,
            plotHeight);
        var samplesNarrow = new Rect(
            rightLeft,
            sampleTop,
            rightWidth,
            Math.Max(1, availableHeight - Padding - sampleTop));
        return new Tm30LayoutResult(vector, scoresNarrow, rfRgNarrow, samplesNarrow);
    }
}

public sealed class Tm30ResponsivePanel : Panel
{
    public Tm30ResponsivePanel()
    {
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveDimension(availableSize.Width, 760);
        var height = ResolveDimension(availableSize.Height, 440);
        var layout = Tm30Layout.Calculate(width, height);
        MeasureChild(0, layout.Vector.Size);
        MeasureChild(1, layout.Scores.Size);
        MeasureChild(2, layout.RfRg.Size);
        MeasureChild(3, layout.Samples.Size);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = Tm30Layout.Calculate(finalSize.Width, finalSize.Height);
        ArrangeChild(0, layout.Vector);
        ArrangeChild(1, layout.Scores);
        ArrangeChild(2, layout.RfRg);
        ArrangeChild(3, layout.Samples);
        return finalSize;
    }

    private void MeasureChild(int index, Size size)
    {
        if (InternalChildren.Count > index)
        {
            InternalChildren[index].Measure(size);
        }
    }

    private void ArrangeChild(int index, Rect rectangle)
    {
        if (InternalChildren.Count > index)
        {
            InternalChildren[index].Arrange(rectangle);
        }
    }

    private static double ResolveDimension(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
