using System.Text.Json;
using System.Text.Json.Serialization;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Workspace;

public enum MeasurementSidebarTab
{
    MeasurementValues,
    SpotreadLog,
}

public sealed record WorkspaceHistoryModeState
{
    public required MeasurementMode Mode { get; init; }
    public IReadOnlyList<Guid> PresentationOrder { get; init; } = [];
    public HashSet<Guid> SelectedEntryIds { get; init; } = [];
    public Guid? ActiveEntryId { get; init; }
    public Guid? SelectionAnchorId { get; init; }
}

public sealed record WorkspaceHistorySnapshot
{
    public IReadOnlyList<MeasurementHistoryEntry> Entries { get; init; } = [];
    public IReadOnlyList<WorkspaceHistoryModeState> Modes { get; init; } = [];
}

public sealed record WorkspaceState
{
    public MeasurementMode? SelectedMode { get; init; }
    public MeasurementSidebarTab SelectedSidebarTab { get; init; } =
        MeasurementSidebarTab.MeasurementValues;
    public required WorkspaceHistorySnapshot History { get; init; }
}

public sealed record WorkspaceDocument
{
    public const int CurrentFormatVersion = 1;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
    public required WorkspaceState Workspace { get; init; }

    public static WorkspaceDocument Create(
        MeasurementHistory history,
        MeasurementMode? selectedMode,
        MeasurementSidebarTab selectedSidebarTab)
    {
        var document = new WorkspaceDocument
        {
            Workspace = new WorkspaceState
            {
                SelectedMode = selectedMode,
                SelectedSidebarTab = selectedSidebarTab,
                History = new WorkspaceHistorySnapshot
                {
                    Entries = history.AcquisitionOrder.ToArray(),
                    Modes = history.SnapshotModeStates()
                        .Select(state => new WorkspaceHistoryModeState
                        {
                            Mode = state.Mode,
                            PresentationOrder = state.PresentationOrder,
                            SelectedEntryIds = state.SelectedEntryIds.ToHashSet(),
                            ActiveEntryId = state.ActiveEntryId,
                            SelectionAnchorId = state.SelectionAnchorId,
                        })
                        .ToArray(),
                },
            },
        };
        WorkspaceSerializer.Validate(document);
        return document;
    }

    public void RestoreInto(MeasurementHistory history)
    {
        WorkspaceSerializer.Validate(this);
        history.Restore(
            Workspace.History.Entries,
            Workspace.History.Modes.Select(mode => new MeasurementHistoryModeState(
                mode.Mode,
                mode.PresentationOrder,
                mode.SelectedEntryIds,
                mode.ActiveEntryId,
                mode.SelectionAnchorId)));
    }
}

public static class WorkspaceSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(WorkspaceDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static WorkspaceDocument Deserialize(string json)
    {
        WorkspaceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<WorkspaceDocument>(json, Options)
                ?? throw new InvalidDataException("Workspace is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Workspace JSON is invalid.", exception);
        }

        Validate(document);
        return document;
    }

    public static void Validate(WorkspaceDocument document)
    {
        if (document.FormatVersion != WorkspaceDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported workspace format version {document.FormatVersion}.");
        }

        var entries = document.Workspace.History.Entries;
        var ids = entries.Select(entry => entry.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new InvalidDataException("Workspace contains duplicate measurement IDs.");
        }

        var modes = document.Workspace.History.Modes;
        if (modes.Count != Enum.GetValues<MeasurementMode>().Length ||
            modes.Select(state => state.Mode).Distinct().Count() != modes.Count)
        {
            throw new InvalidDataException("Workspace has invalid mode states.");
        }

        foreach (var mode in Enum.GetValues<MeasurementMode>())
        {
            var state = modes.SingleOrDefault(candidate => candidate.Mode == mode)
                ?? throw new InvalidDataException($"Workspace has no mode state for {mode}.");
            var expected = entries
                .Where(entry => entry.Measurement.Mode == mode)
                .Select(entry => entry.Id)
                .ToHashSet();
            if (state.PresentationOrder.Count != expected.Count ||
                state.PresentationOrder.Distinct().Count() != state.PresentationOrder.Count ||
                !state.PresentationOrder.All(expected.Contains) ||
                !state.SelectedEntryIds.All(expected.Contains))
            {
                throw new InvalidDataException($"Invalid mode state for {mode}.");
            }

            if (state.SelectedEntryIds.Count == 0)
            {
                if (state.ActiveEntryId is not null || state.SelectionAnchorId is not null)
                {
                    throw new InvalidDataException($"Empty {mode} selection has active or anchor ID.");
                }
            }
            else if (state.ActiveEntryId is not { } active ||
                     !state.SelectedEntryIds.Contains(active) ||
                     state.SelectionAnchorId is not { } anchor ||
                     !expected.Contains(anchor))
            {
                throw new InvalidDataException($"Invalid active or anchor ID for {mode}.");
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
