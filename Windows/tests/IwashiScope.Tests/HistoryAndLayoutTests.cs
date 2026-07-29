using IwashiScope.App.Wpf.Layout;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class HistoryAndLayoutTests
{
    [Fact]
    public void HistoryCardContentExactlyMatchesFixedMacCardHeight()
    {
        Assert.Equal(110, HistoryCardLayout.Width);
        Assert.Equal(110, HistoryCardLayout.Height);

        Assert.Equal(
            HistoryCardLayout.Height,
            HistoryCardLayout.ReflectanceColorAreaHeight +
            HistoryCardLayout.ReflectanceLabAreaHeight);
        Assert.Equal(
            HistoryCardLayout.Height,
            HistoryCardLayout.LightingSpectrumHeight +
            HistoryCardLayout.LightingDividerHeight +
            HistoryCardLayout.LightingCriHeight +
            HistoryCardLayout.LightingTitleAreaHeight);
        Assert.Equal(52, HistoryCardLayout.ReflectanceNameFieldTop);
    }

    [Fact]
    public void SelectionGeometryIsModeLocalAndUsesPresentationOrder()
    {
        var history = new MeasurementHistory();
        var a = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 1), "A");
        var b = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 2), "B");
        var c = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 3), "C");
        var d = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance, 4), "D");
        var ambient = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient), "Ambient");

        history.SelectExclusive(b.Id);
        history.SelectRange(d.Id);
        Assert.Equal([b.Id, c.Id, d.Id], history.Ordered(MeasurementMode.Reflectance)
            .Where(entry => history.SelectedIdsFor(MeasurementMode.Reflectance).Contains(entry.Id))
            .Select(entry => entry.Id));
        Assert.Equal(ambient.Id, history.ActiveIdFor(MeasurementMode.Ambient));

        history.Toggle(c.Id);
        Assert.Equal([b.Id, d.Id], history.Ordered(MeasurementMode.Reflectance)
            .Where(entry => history.SelectedIdsFor(MeasurementMode.Reflectance).Contains(entry.Id))
            .Select(entry => entry.Id));
        Assert.Equal(d.Id, history.ActiveIdFor(MeasurementMode.Reflectance));
        Assert.Equal(b.Id, history.AnchorIdFor(MeasurementMode.Reflectance));

        history.SelectRange(a.Id, additive: true);
        Assert.Equal([a.Id, b.Id, d.Id], history.Ordered(MeasurementMode.Reflectance)
            .Where(entry => history.SelectedIdsFor(MeasurementMode.Reflectance).Contains(entry.Id))
            .Select(entry => entry.Id));
    }

    [Fact]
    public void MultiCardReorderPreservesRelativeOrderAndWrapsAtEnds()
    {
        var history = new MeasurementHistory();
        var entries = Enumerable.Range(1, 5)
            .Select(index => history.Add(
                TestMeasurementFactory.Create(MeasurementMode.Reflectance, index),
                ((char)('A' + index - 1)).ToString()))
            .ToArray();
        history.SetSelection(
            MeasurementMode.Reflectance,
            [entries[1].Id, entries[3].Id],
            entries[3].Id,
            entries[1].Id);

        history.MoveSelectionToStart(MeasurementMode.Reflectance);
        Assert.Equal(
            ["B", "D", "A", "C", "E"],
            history.Ordered(MeasurementMode.Reflectance).Select(entry => entry.Name));

        history.MoveSelectionToEnd(MeasurementMode.Reflectance);
        Assert.Equal(
            ["A", "C", "E", "B", "D"],
            history.Ordered(MeasurementMode.Reflectance).Select(entry => entry.Name));
    }

    [Fact]
    public void HistoryFooterGeometryMatchesMacFractions()
    {
        Assert.Equal(
            334,
            HistoryFooterLayout.CollapsedHeight(
                availableHeight: 800,
                analysisContentHeight: 466,
                MeasurementMode.Reflectance));
        Assert.Equal(
            100,
            HistoryFooterLayout.CollapsedHeight(
                availableHeight: 800,
                analysisContentHeight: 962,
                MeasurementMode.Ambient));
        Assert.Equal(720, HistoryFooterLayout.Height(800, 100, null, expanded: true));
        Assert.Equal(720, HistoryFooterLayout.ResizedHeight(800, 900));
        Assert.Equal(100, HistoryFooterLayout.ResizedHeight(800, 20));
        Assert.Equal(0, HistoryFooterLayout.ResizedHeight(double.NaN, 100));
    }
}
