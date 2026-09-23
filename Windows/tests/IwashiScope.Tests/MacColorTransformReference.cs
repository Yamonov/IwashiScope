using System.IO;
using System.Text.Json;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

/// <summary>Independent native transform probes, injected for algorithm parity only; never shipped in the app.</summary>
internal static class MacColorTransformReference
{
    public static Func<Vector3, Vector3> For(string profile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mac-named-space-transforms.json")));
        var probes = document.RootElement.EnumerateArray().Single(p => p.GetProperty("name").GetString() == profile).GetProperty("probes");
        static Vector3 V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        var x = V(probes[1].GetProperty("outputLinearRGB")); var y = V(probes[2].GetProperty("outputLinearRGB")); var z = V(probes[3].GetProperty("outputLinearRGB"));
        return v => new(x.First * v.First + y.First * v.Second + z.First * v.Third,
            x.Second * v.First + y.Second * v.Second + z.Second * v.Third,
            x.Third * v.First + y.Third * v.Second + z.Third * v.Third);
    }
}
