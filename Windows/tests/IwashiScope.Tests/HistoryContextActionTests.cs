using IwashiScope.Core.Calculations;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class HistoryContextActionTests
{
    [Theory]
    [InlineData(MeasurementMode.Reflectance)]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void ContextMenuUsesMultipleSelectionOnlyForASelectedAnchor(MeasurementMode mode)
    {
        var history = new MeasurementHistory();
        var first = history.Add(Measurement(mode, 1));
        var second = history.Add(Measurement(mode, 1));
        var third = history.Add(Measurement(mode, 1));
        history.SelectExclusive(first.Id);
        history.Toggle(second.Id);
        Assert.True(history.ContextMenuIds(mode, first.Id).SetEquals([first.Id, second.Id]));
        Assert.True(history.ContextMenuIds(mode, third.Id).SetEquals([third.Id]));
        Assert.Empty(history.ContextMenuIds(mode, Guid.NewGuid()));
        Assert.Single(history.DeleteEntries(mode, history.ContextMenuIds(mode, third.Id)));
        Assert.True(history.SelectedIdsFor(mode).SetEquals([first.Id, second.Id]));
    }

    [Theory]
    [InlineData(MeasurementMode.Reflectance)]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void DateDeletionKeepsOtherModesDaysAndNewReadings(MeasurementMode mode)
    {
        var history = new MeasurementHistory();
        var first = history.Add(Measurement(mode, 1));
        var second = history.Add(Measurement(mode, 1));
        var otherDay = history.Add(Measurement(mode, 2));
        var otherMode = mode == MeasurementMode.Ambient ? MeasurementMode.Reflectance : MeasurementMode.Ambient;
        var other = history.Add(Measurement(otherMode, 1));
        var group = MeasurementHistoryDateGrouping.Groups(history.Ordered(mode), TimeZoneInfo.Utc)
            .Single(day => day.Date == new DateOnly(2026, 9, 1));
        var targets = group.Entries.Select(entry => entry.Id).ToHashSet();
        var newReading = history.Add(Measurement(mode, 1));
        targets.Add(other.Id);
        targets.Add(Guid.NewGuid());
        Assert.Equal(2, history.DeleteEntries(mode, targets).Count);
        Assert.Equal([newReading.Id, otherDay.Id], history.Ordered(mode).Select(entry => entry.Id));
        Assert.NotNull(history.Entry(other.Id));
        Assert.Equal(newReading.Id, history.ActiveIdFor(mode));
        Assert.Empty(history.DeleteEntries(mode, targets));
    }

    [Theory]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void RegistrationAfterConfirmationWasOpenedStillProtectsTheRecord(MeasurementMode mode)
    {
        var history = new MeasurementHistory();
        var first = history.Add(Measurement(mode, 1));
        var second = history.Add(Measurement(mode, 1));
        var targets = history.DeletableIds(mode, [first.Id, second.Id]);
        Assert.True(history.RegisterUserIlluminant(first.Id, UserIlluminantSlot.User1));
        Assert.Empty(history.DeletableIds(mode, [first.Id]));
        Assert.Single(history.DeleteEntries(mode, targets));
        Assert.Equal(first.Id, history.UserIlluminantEntryId(UserIlluminantSlot.User1));
        Assert.Equal(first.Id, Assert.Single(history.Ordered(mode)).Id);
    }

    [Fact]
    public void DateGroupsSortNewestDatesFirstAndKeepManualOrderWithinEachDate()
    {
        var a = MeasurementHistoryEntry.Create(Measurement(MeasurementMode.Reflectance, 1), "a");
        var b = MeasurementHistoryEntry.Create(Measurement(MeasurementMode.Reflectance, 2), "b");
        var c = MeasurementHistoryEntry.Create(Measurement(MeasurementMode.Reflectance, 1), "c");
        var groups = MeasurementHistoryDateGrouping.Groups([c, b, a], TimeZoneInfo.Utc);
        Assert.Equal(["2026/09/02", "2026/09/01"], groups.Select(group => group.Title));
        Assert.Equal("2026-09-01", groups[1].ExportName);
        Assert.Equal([c.Id, a.Id], groups[1].Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void DateGroupingUsesTheViewingTimeZone()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("TestJST", TimeSpan.FromHours(9), "TestJST", "TestJST");
        var a = MeasurementHistoryEntry.Create(Measurement(MeasurementMode.Reflectance, 1) with
            { CapturedAt = new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero) });
        var b = MeasurementHistoryEntry.Create(a.Measurement with
            { CapturedAt = new DateTimeOffset(2026, 9, 1, 15, 30, 0, TimeSpan.Zero) });
        Assert.Equal(["2026/09/02", "2026/09/01"],
            MeasurementHistoryDateGrouping.Groups([a, b], tokyo).Select(group => group.Title));
    }

    private static SpotMeasurement Measurement(MeasurementMode mode, int day) =>
        TestMeasurementFactory.Create(mode) with { CapturedAt = new DateTimeOffset(2026, 9, day, 12, 0, 0, TimeSpan.Zero) };
}
