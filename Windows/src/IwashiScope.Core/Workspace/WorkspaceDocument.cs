using System.Text.Json;
using System.Text.Json.Serialization;
using IwashiScope.Core.Calculations;
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
    public IReadOnlyList<UserIlluminantRegistration> UserIlluminantRegistrations { get; init; } = [];
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
                    UserIlluminantRegistrations = history.SnapshotUserIlluminants(),
                },
            },
        };
        WorkspaceSerializer.Validate(document);
        return document;
    }

    public WorkspaceDocument ContainingOnly(IReadOnlySet<MeasurementMode> includedModes)
    {
        var entries = Workspace.History.Entries
            .Where(entry => includedModes.Contains(entry.Measurement.Mode))
            .ToArray();
        var entryIds = entries.Select(entry => entry.Id).ToHashSet();
        var modes = Workspace.History.Modes.Select(state =>
        {
            var selectedIds = state.SelectedEntryIds.Where(entryIds.Contains).ToHashSet();
            return state with
            {
                PresentationOrder = state.PresentationOrder.Where(entryIds.Contains).ToArray(),
                SelectedEntryIds = selectedIds,
                ActiveEntryId = state.ActiveEntryId is { } active && selectedIds.Contains(active)
                    ? active : null,
                SelectionAnchorId = state.SelectionAnchorId is { } anchor && entryIds.Contains(anchor)
                    ? anchor : null,
            };
        }).ToArray();
        var registrations = Workspace.History.UserIlluminantRegistrations
            .Where(registration => entryIds.Contains(registration.EntryId))
            .ToArray();
        var filtered = this with
        {
            Workspace = Workspace with
            {
                History = Workspace.History with
                {
                    Entries = entries,
                    Modes = modes,
                    UserIlluminantRegistrations = registrations,
                },
            },
        };
        WorkspaceSerializer.Validate(filtered);
        return filtered;
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
                mode.SelectionAnchorId)),
            Workspace.History.UserIlluminantRegistrations);
    }

    public int ImportInto(
        MeasurementHistory history,
        IReadOnlySet<MeasurementMode> selectedModes)
    {
        WorkspaceSerializer.Validate(this);
        var entries = Workspace.History.Entries
            .Where(entry => selectedModes.Contains(entry.Measurement.Mode))
            .ToArray();
        var entriesById = entries.ToDictionary(entry => entry.Id);
        var existingSources = history.SnapshotUserIlluminants()
            .Select(registration => history.UserIlluminantEntry(registration.Slot))
            .OfType<MeasurementHistoryEntry>()
            .ToArray();
        var newSources = new List<MeasurementHistoryEntry>();
        foreach (var registration in Workspace.History.UserIlluminantRegistrations
                     .OrderBy(registration => registration.Slot))
        {
            if (!entriesById.TryGetValue(registration.EntryId, out var source)) continue;
            if (existingSources.Any(existing => SameIlluminant(existing.Measurement, source.Measurement)) ||
                newSources.Any(existing => SameIlluminant(existing.Measurement, source.Measurement)))
            {
                continue;
            }
            newSources.Add(source);
        }
        var occupied = history.AvailableUserIlluminantSlots;
        var freeSlots = Enum.GetValues<UserIlluminantSlot>()
            .Where(slot => !occupied.Contains(slot)).ToArray();
        if (newSources.Count > freeSlots.Length)
        {
            throw new InvalidDataException(
                "The 100 user illuminant slots are full. No history was imported.");
        }

        var addedIds = new Dictionary<Guid, Guid>();
        foreach (var entry in entries)
        {
            var added = history.Add(entry.Measurement, entry.Name, entry.InstrumentIdentity);
            addedIds[entry.Id] = added.Id;
        }
        for (var index = 0; index < newSources.Count; index++)
        {
            history.RegisterUserIlluminant(
                addedIds[newSources[index].Id], freeSlots[index]);
        }
        return entries.Length;
    }

    private static bool SameIlluminant(SpotMeasurement first, SpotMeasurement second) =>
        first.Mode == second.Mode &&
        Math.Abs((first.CapturedAt - second.CapturedAt).TotalMilliseconds) < 1 &&
        first.Spectrum.SequenceEqual(second.Spectrum);
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

        var registrations = document.Workspace.History.UserIlluminantRegistrations;
        if (registrations.Select(registration => registration.Slot).Distinct().Count() !=
            registrations.Count)
        {
            throw new InvalidDataException("Workspace has duplicate user illuminant slots.");
        }
        foreach (var registration in registrations)
        {
            var entry = entries.FirstOrDefault(candidate => candidate.Id == registration.EntryId);
            if (entry is null ||
                entry.Measurement.Mode is not (MeasurementMode.Ambient or MeasurementMode.Emissive) ||
                IlluminantSpectrumDefinition
                    .NormalizeUserSamples(entry.Measurement.Spectrum).Count < 2)
            {
                throw new InvalidDataException("Workspace has an invalid user illuminant registration.");
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
