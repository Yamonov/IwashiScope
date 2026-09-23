using System.Reflection;
using IwashiScope.App.Wpf;

namespace IwashiScope.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void AppAssemblyIdentifiesVersion11()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?
            .Version;

        Assert.Equal("1.1", informationalVersion);
        var build = int.Parse(assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(item => item.Key == "BuildNumber").Value!);
        Assert.InRange(build, 67, ushort.MaxValue - 1);
        Assert.Equal($"1.1.0.{build}", fileVersion);
        Assert.Equal(new Version(1, 1, 0, build), assembly.GetName().Version);
    }
}
