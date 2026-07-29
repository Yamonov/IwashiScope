using System.Text;

namespace IwashiScope.Infrastructure.Windows.Process;

internal static class SpotreadPipeProtocol
{
    public static Encoding InputEncoding { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string FrameCommand(string command) =>
        command.EndsWith('\r') || command.EndsWith('\n')
            ? command
            : command + Environment.NewLine;
}
