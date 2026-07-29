namespace IwashiScope.Infrastructure.Windows.Logging;

public enum SpotreadLogKind
{
    Lifecycle,
    StandardError,
    Protocol,
    InputPending,
    InputSent,
    InputFailed,
}

public sealed record SpotreadLogEntry(
    DateTimeOffset Timestamp,
    SpotreadLogKind Kind,
    string Message);

public sealed class BoundedLogBuffer
{
    public const int MaximumCharacters = 2_000_000;
    public const int RetainedCharacters = 1_600_000;
    private readonly object _gate = new();
    private readonly System.Text.StringBuilder _text = new();

    public event Action<string>? Appended;
    public event Action<string>? Reset;

    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    public void Append(SpotreadLogEntry entry)
    {
        var line =
            $"{entry.Timestamp:O} [{entry.Kind}] {entry.Message}{Environment.NewLine}";
        string? resetText = null;
        lock (_gate)
        {
            _text.Append(line);
            if (_text.Length > MaximumCharacters)
            {
                _text.Remove(0, _text.Length - RetainedCharacters);
                resetText = _text.ToString();
            }
        }

        if (resetText is not null)
        {
            Reset?.Invoke(resetText);
        }
        else
        {
            Appended?.Invoke(line);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _text.Clear();
        }
        Reset?.Invoke(string.Empty);
    }
}
