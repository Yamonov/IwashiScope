using System.Globalization;

namespace IwashiScope.Core.History;

public sealed record MeasurementHistoryDateGroup(DateOnly Date, IReadOnlyList<MeasurementHistoryEntry> Entries)
{
    public string Title => Date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
    public string ExportName => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public static class MeasurementHistoryDateGrouping
{
    public static DateOnly LocalDate(MeasurementHistoryEntry entry, TimeZoneInfo? timeZone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.Measurement.CapturedAt, timeZone ?? TimeZoneInfo.Local).Date);

    public static string DateKey(MeasurementHistoryEntry entry) =>
        LocalDate(entry).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public static IReadOnlyList<MeasurementHistoryDateGroup> Groups(
        IReadOnlyList<MeasurementHistoryEntry> orderedEntries, TimeZoneInfo? timeZone = null) =>
        orderedEntries.GroupBy(entry => LocalDate(entry, timeZone))
            .OrderByDescending(group => group.Key)
            .Select(group => new MeasurementHistoryDateGroup(group.Key, group.ToArray())).ToArray();
}
