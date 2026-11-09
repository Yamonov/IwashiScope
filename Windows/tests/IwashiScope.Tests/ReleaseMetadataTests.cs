using System.Reflection;
using IwashiScope.App.Wpf;

namespace IwashiScope.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void AppAssemblyIdentifiesReleaseVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?
            .Version;

        var build = int.Parse(assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(item => item.Key == "BuildNumber").Value!);
        Assert.InRange(build, 85, ushort.MaxValue - 1);
        var expectedVersion = new Version(1, 2, 3, build);
        Assert.Equal(expectedVersion.ToString(3), informationalVersion);
        Assert.Equal(expectedVersion.ToString(), fileVersion);
        Assert.Equal(expectedVersion, assembly.GetName().Version);
    }
}
