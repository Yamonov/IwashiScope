using System.Text;

namespace IwashiScope.Protocol;

public sealed class Utf8JsonLineStream
{
    private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
    private readonly StringBuilder _buffer = new();
    private bool _finished;

    public int BufferedCharacterCount => _buffer.Length;

    public IReadOnlyList<string> Consume(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        if (bytes.IsEmpty)
        {
            return [];
        }

        var characters = new char[bytes.Length + 2];
        _decoder.Convert(
            bytes,
            characters,
            flush: false,
            out var bytesUsed,
            out var charactersUsed,
            out var completed);
        if (bytesUsed != bytes.Length || !completed)
        {
            throw new InvalidDataException("Unable to consume the complete UTF-8 chunk.");
        }

        _buffer.Append(characters, 0, charactersUsed);
        return ExtractCompleteLines();
    }

    public IReadOnlyList<string> Consume(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ObjectDisposedException.ThrowIf(_finished, this);
        _buffer.Append(chunk);
        return ExtractCompleteLines();
    }

    public IReadOnlyList<string> Finish()
    {
        if (_finished)
        {
            return [];
        }

        _finished = true;
        Span<char> trailing = stackalloc char[2];
        _decoder.Convert(
            ReadOnlySpan<byte>.Empty,
            trailing,
            flush: true,
            out _,
            out var charactersUsed,
            out _);
        if (charactersUsed > 0)
        {
            _buffer.Append(trailing[..charactersUsed]);
        }

        var lines = ExtractCompleteLines().ToList();
        var finalLine = _buffer.ToString().Trim();
        _buffer.Clear();
        if (finalLine.Length > 0)
        {
            lines.Add(finalLine);
        }
        return lines;
    }

    private IReadOnlyList<string> ExtractCompleteLines()
    {
        var lines = new List<string>();
        while (true)
        {
            var newline = IndexOf(_buffer, '\n');
            if (newline < 0)
            {
                break;
            }

            var line = _buffer.ToString(0, newline);
            _buffer.Remove(0, newline + 1);
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int IndexOf(StringBuilder builder, char value)
    {
        for (var index = 0; index < builder.Length; index++)
        {
            if (builder[index] == value)
            {
                return index;
            }
        }
        return -1;
    }
}
