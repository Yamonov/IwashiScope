using IwashiScope.Infrastructure.Windows.Logging;

namespace IwashiScope.Tests;

public sealed class LogBufferTests
{
    [Fact]
    public void OversizedLogResetsToRetainedTailAndClearNotifies()
    {
        var buffer = new BoundedLogBuffer();
        string? reset = null;
        buffer.Reset += value => reset = value;

        buffer.Append(new SpotreadLogEntry(
            DateTimeOffset.UnixEpoch,
            SpotreadLogKind.StandardError,
            new string('x', BoundedLogBuffer.MaximumCharacters + 100)));

        Assert.NotNull(reset);
        Assert.Equal(BoundedLogBuffer.RetainedCharacters, buffer.Text.Length);
        Assert.EndsWith(Environment.NewLine, buffer.Text);

        buffer.Clear();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(string.Empty, reset);
    }
}
