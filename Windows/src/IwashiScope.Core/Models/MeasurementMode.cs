namespace IwashiScope.Core.Models;

public enum MeasurementMode
{
    Reflectance,
    Ambient,
    Emissive,
}

public static class MeasurementModeExtensions
{
    public static string ProtocolName(this MeasurementMode mode) => mode switch
    {
        MeasurementMode.Reflectance => "reflectance",
        MeasurementMode.Ambient => "ambient",
        MeasurementMode.Emissive => "emissive",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool IsLighting(this MeasurementMode mode) =>
        mode is MeasurementMode.Ambient or MeasurementMode.Emissive;

    public static IReadOnlyList<string> SpotreadArguments(
        this MeasurementMode mode,
        int instrumentIndex = 1)
    {
        if (instrumentIndex < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentIndex),
                "Instrument indices are one-based.");
        }

        var arguments = new List<string> { "-J", "-v", "-s", "-H" };
        switch (mode)
        {
            case MeasurementMode.Reflectance:
                break;
            case MeasurementMode.Ambient:
                arguments.Add("-a");
                break;
            case MeasurementMode.Emissive:
                arguments.AddRange(["-e", "-T"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        arguments.Add("-c");
        arguments.Add(instrumentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return arguments;
    }

    public static MeasurementMode ParseProtocolName(string value) => value switch
    {
        "reflectance" => MeasurementMode.Reflectance,
        "ambient" => MeasurementMode.Ambient,
        "emissive" => MeasurementMode.Emissive,
        _ => throw new FormatException($"Unknown measurement mode '{value}'."),
    };
}
