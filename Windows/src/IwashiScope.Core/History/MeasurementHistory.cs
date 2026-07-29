using IwashiScope.Core.Models;

namespace IwashiScope.Core.History;

public sealed record MeasurementHistoryEntry(
    Guid Id,
    string? Name,
    SpotMeasurement Measurement,
    SpotreadInstrumentIdentity? InstrumentIdentity = null)
{
    public static MeasurementHistoryEntry Create(
        SpotMeasurement measurement,
        string? name = null,
        SpotreadInstrumentIdentity? instrumentIdentity = null) =>
        new(
            Guid.NewGuid(),
            NormalizeName(name),
            measurement,
            instrumentIdentity);

    public string DisplayName(int sequence) =>
        Name ?? $"Measurement {sequence}";

    public static string? NormalizeName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var singleLine = name
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return singleLine.Length == 0 ? null : singleLine;
    }
}

public sealed record MeasurementHistoryModeState(
    MeasurementMode Mode,
    IReadOnlyList<Guid> PresentationOrder,
    IReadOnlySet<Guid> SelectedEntryIds,
    Guid? ActiveEntryId,
    Guid? SelectionAnchorId);

public sealed class MeasurementHistory
{
    private readonly List<MeasurementHistoryEntry> _acquisitionOrder = [];
    private readonly Dictionary<MeasurementMode, List<Guid>> _presentationOrder =
        Enum.GetValues<MeasurementMode>().ToDictionary(mode => mode, _ => new List<Guid>());
    private readonly Dictionary<MeasurementMode, HashSet<Guid>> _selectedIds =
        Enum.GetValues<MeasurementMode>().ToDictionary(mode => mode, _ => new HashSet<Guid>());
    private readonly Dictionary<MeasurementMode, Guid?> _activeIds =
        Enum.GetValues<MeasurementMode>().ToDictionary(mode => mode, _ => (Guid?)null);
    private readonly Dictionary<MeasurementMode, Guid?> _anchorIds =
        Enum.GetValues<MeasurementMode>().ToDictionary(mode => mode, _ => (Guid?)null);
    private MeasurementMode _lastSelectionMode = MeasurementMode.Reflectance;

    public IReadOnlyList<MeasurementHistoryEntry> AcquisitionOrder => _acquisitionOrder;

    // Compatibility projection for the most recently manipulated mode.
    public IReadOnlySet<Guid> SelectedIds => SelectedIdsFor(_lastSelectionMode);
    public Guid? ActiveId => ActiveIdFor(_lastSelectionMode);
    public Guid? AnchorId => AnchorIdFor(_lastSelectionMode);

    public MeasurementHistoryEntry Add(
        SpotMeasurement measurement,
        string? name = null,
        SpotreadInstrumentIdentity? instrumentIdentity = null)
    {
        var entry = MeasurementHistoryEntry.Create(measurement, name, instrumentIdentity);
        _acquisitionOrder.Add(entry);
        _presentationOrder[measurement.Mode].Add(entry.Id);
        SelectExclusive(entry.Id);
        return entry;
    }

    public IReadOnlyList<MeasurementHistoryEntry> Ordered(MeasurementMode mode)
    {
        var records = _acquisitionOrder.ToDictionary(entry => entry.Id);
        return _presentationOrder[mode]
            .Where(records.ContainsKey)
            .Select(id => records[id])
            .ToArray();
    }

    public IReadOnlySet<Guid> SelectedIdsFor(MeasurementMode mode) => _selectedIds[mode];
    public Guid? ActiveIdFor(MeasurementMode mode) => _activeIds[mode];
    public Guid? AnchorIdFor(MeasurementMode mode) => _anchorIds[mode];

    public MeasurementHistoryEntry? ActiveEntryFor(MeasurementMode mode)
    {
        var active = ActiveIdFor(mode);
        return active is null
            ? null
            : _acquisitionOrder.FirstOrDefault(entry => entry.Id == active);
    }

    public void SelectExclusive(Guid id)
    {
        var entry = EnsureExists(id);
        var mode = entry.Measurement.Mode;
        _lastSelectionMode = mode;
        _selectedIds[mode].Clear();
        _selectedIds[mode].Add(id);
        _activeIds[mode] = id;
        _anchorIds[mode] = id;
    }

    public void Toggle(Guid id)
    {
        var entry = EnsureExists(id);
        var mode = entry.Measurement.Mode;
        _lastSelectionMode = mode;
        var selection = _selectedIds[mode];
        if (!selection.Add(id))
        {
            selection.Remove(id);
        }

        _activeIds[mode] = selection.Contains(id)
            ? id
            : _presentationOrder[mode]
                .Where(selection.Contains)
                .Select(value => (Guid?)value)
                .LastOrDefault();
        _anchorIds[mode] ??= id;
        NormalizeEmptySelection(mode);
    }

    public void SelectRange(Guid id, bool additive = false)
    {
        var target = EnsureExists(id);
        var mode = target.Measurement.Mode;
        _lastSelectionMode = mode;
        if (_anchorIds[mode] is not { } anchorId ||
            _acquisitionOrder.FirstOrDefault(entry => entry.Id == anchorId) is not { } anchor ||
            anchor.Measurement.Mode != mode)
        {
            SelectExclusive(id);
            return;
        }

        var order = _presentationOrder[mode];
        var anchorIndex = order.IndexOf(anchorId);
        var targetIndex = order.IndexOf(id);
        if (!additive)
        {
            _selectedIds[mode].Clear();
        }
        foreach (var selected in order
                     .Skip(Math.Min(anchorIndex, targetIndex))
                     .Take(Math.Abs(anchorIndex - targetIndex) + 1))
        {
            _selectedIds[mode].Add(selected);
        }

        _activeIds[mode] = id;
    }

    public void SetSelection(
        MeasurementMode mode,
        IEnumerable<Guid> ids,
        Guid? activeId = null,
        Guid? anchorId = null)
    {
        var orderedIds = ids.Distinct().ToArray();
        var validIds = _presentationOrder[mode].ToHashSet();
        if (!orderedIds.All(validIds.Contains))
        {
            throw new KeyNotFoundException("Selection contains an entry from another mode.");
        }

        _lastSelectionMode = mode;
        _selectedIds[mode].Clear();
        _selectedIds[mode].UnionWith(orderedIds);
        _activeIds[mode] = activeId is { } active && _selectedIds[mode].Contains(active)
            ? active
            : _presentationOrder[mode]
                .Where(_selectedIds[mode].Contains)
                .Select(value => (Guid?)value)
                .LastOrDefault();
        _anchorIds[mode] = anchorId is { } anchor && validIds.Contains(anchor)
            ? anchor
            : _activeIds[mode];
        NormalizeEmptySelection(mode);
    }

    public void SelectAll(MeasurementMode mode)
    {
        _lastSelectionMode = mode;
        _selectedIds[mode].Clear();
        _selectedIds[mode].UnionWith(_presentationOrder[mode]);
        _activeIds[mode] = _presentationOrder[mode]
            .Select(value => (Guid?)value)
            .LastOrDefault();
        _anchorIds[mode] = _activeIds[mode];
        NormalizeEmptySelection(mode);
    }

    public void DeselectAll(MeasurementMode mode)
    {
        _lastSelectionMode = mode;
        _selectedIds[mode].Clear();
        NormalizeEmptySelection(mode);
    }

    public void Rename(Guid id, string? name)
    {
        var index = _acquisitionOrder.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Measurement '{id}' does not exist.");
        }

        _acquisitionOrder[index] = _acquisitionOrder[index] with
        {
            Name = MeasurementHistoryEntry.NormalizeName(name),
        };
    }

    public void MoveSelectionBefore(MeasurementMode mode, Guid targetId)
    {
        var order = _presentationOrder[mode];
        if (!order.Contains(targetId))
        {
            throw new KeyNotFoundException($"Target '{targetId}' does not exist in {mode}.");
        }

        var moving = order.Where(_selectedIds[mode].Contains).ToArray();
        if (moving.Length == 0 || moving.Contains(targetId))
        {
            return;
        }

        order.RemoveAll(_selectedIds[mode].Contains);
        var targetIndex = order.IndexOf(targetId);
        order.InsertRange(targetIndex, moving);
    }

    public void MoveSelectionAfter(MeasurementMode mode, Guid targetId)
    {
        var order = _presentationOrder[mode];
        if (!order.Contains(targetId))
        {
            throw new KeyNotFoundException($"Target '{targetId}' does not exist in {mode}.");
        }

        var moving = order.Where(_selectedIds[mode].Contains).ToArray();
        if (moving.Length == 0 || moving.Contains(targetId))
        {
            return;
        }

        order.RemoveAll(_selectedIds[mode].Contains);
        var targetIndex = order.IndexOf(targetId);
        order.InsertRange(targetIndex + 1, moving);
    }

    public void MoveSelectionToStart(MeasurementMode mode)
    {
        var order = _presentationOrder[mode];
        var moving = order.Where(_selectedIds[mode].Contains).ToArray();
        order.RemoveAll(_selectedIds[mode].Contains);
        order.InsertRange(0, moving);
    }

    public void MoveSelectionToEnd(MeasurementMode mode)
    {
        var order = _presentationOrder[mode];
        var moving = order.Where(_selectedIds[mode].Contains).ToArray();
        order.RemoveAll(_selectedIds[mode].Contains);
        order.AddRange(moving);
    }

    public IReadOnlyList<MeasurementHistoryEntry> DeleteSelected() =>
        DeleteSelected(_lastSelectionMode);

    public IReadOnlyList<MeasurementHistoryEntry> DeleteSelected(MeasurementMode mode)
    {
        var selection = _selectedIds[mode];
        var removed = _acquisitionOrder.Where(entry => selection.Contains(entry.Id)).ToArray();
        _acquisitionOrder.RemoveAll(entry => selection.Contains(entry.Id));
        _presentationOrder[mode].RemoveAll(selection.Contains);
        selection.Clear();
        NormalizeEmptySelection(mode);
        _lastSelectionMode = mode;
        return removed;
    }

    public IReadOnlyList<MeasurementHistoryModeState> SnapshotModeStates() =>
        Enum.GetValues<MeasurementMode>()
            .Select(mode => new MeasurementHistoryModeState(
                mode,
                _presentationOrder[mode].ToArray(),
                _selectedIds[mode].ToHashSet(),
                _activeIds[mode],
                _anchorIds[mode]))
            .ToArray();

    public void Restore(
        IEnumerable<MeasurementHistoryEntry> acquisitionOrder,
        IEnumerable<MeasurementHistoryModeState> modeStates)
    {
        var entries = acquisitionOrder.ToArray();
        var ids = entries.Select(entry => entry.Id).ToHashSet();
        if (ids.Count != entries.Length)
        {
            throw new InvalidDataException("Workspace contains duplicate measurement IDs.");
        }

        var states = modeStates.ToArray();
        if (states.Length != Enum.GetValues<MeasurementMode>().Length ||
            states.Select(state => state.Mode).Distinct().Count() != states.Length)
        {
            throw new InvalidDataException("Workspace has invalid mode states.");
        }

        foreach (var mode in Enum.GetValues<MeasurementMode>())
        {
            var state = states.SingleOrDefault(candidate => candidate.Mode == mode)
                ?? throw new InvalidDataException($"Workspace has no presentation order for {mode}.");
            var expected = entries
                .Where(entry => entry.Measurement.Mode == mode)
                .Select(entry => entry.Id)
                .ToHashSet();
            var selected = state.SelectedEntryIds.ToHashSet();
            if (state.PresentationOrder.Count != expected.Count ||
                state.PresentationOrder.Distinct().Count() != state.PresentationOrder.Count ||
                !state.PresentationOrder.All(expected.Contains) ||
                !selected.All(expected.Contains))
            {
                throw new InvalidDataException($"Invalid presentation order or selection for {mode}.");
            }
            if (selected.Count == 0)
            {
                if (state.ActiveEntryId is not null || state.SelectionAnchorId is not null)
                {
                    throw new InvalidDataException($"Empty {mode} selection has active or anchor ID.");
                }
            }
            else if (state.ActiveEntryId is not { } active ||
                     !selected.Contains(active) ||
                     state.SelectionAnchorId is not { } anchor ||
                     !expected.Contains(anchor))
            {
                throw new InvalidDataException($"Invalid active or anchor ID for {mode}.");
            }
        }

        _acquisitionOrder.Clear();
        _acquisitionOrder.AddRange(entries);
        foreach (var state in states)
        {
            var mode = state.Mode;
            _presentationOrder[mode].Clear();
            _presentationOrder[mode].AddRange(state.PresentationOrder);
            _selectedIds[mode].Clear();
            _selectedIds[mode].UnionWith(state.SelectedEntryIds);
            _activeIds[mode] = state.ActiveEntryId;
            _anchorIds[mode] = state.SelectionAnchorId;
        }
    }

    private void NormalizeEmptySelection(MeasurementMode mode)
    {
        if (_selectedIds[mode].Count != 0)
        {
            return;
        }
        _activeIds[mode] = null;
        _anchorIds[mode] = null;
    }

    private MeasurementHistoryEntry EnsureExists(Guid id) =>
        _acquisitionOrder.FirstOrDefault(entry => entry.Id == id)
        ?? throw new KeyNotFoundException($"Measurement '{id}' does not exist.");
}
