using IwashiScope.Infrastructure.Windows.Process;

namespace IwashiScope.Tests;

public sealed class SpotreadPipeProtocolTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("k")]
    [InlineData("N")]
    [InlineData("q")]
    public void CommandsAreBomlessUtf8AndCrLfTerminated(string command)
    {
        var framed = SpotreadPipeProtocol.FrameCommand(command);
        var bytes = SpotreadPipeProtocol.InputEncoding.GetBytes(framed);

        Assert.Equal(command + "\r\n", framed);
        Assert.Equal((byte)command[0], bytes[0]);
        Assert.Equal((byte)'\r', bytes[^2]);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void ExistingLineTerminatorIsNotDuplicated()
    {
        Assert.Equal("k\n", SpotreadPipeProtocol.FrameCommand("k\n"));
    }
}
