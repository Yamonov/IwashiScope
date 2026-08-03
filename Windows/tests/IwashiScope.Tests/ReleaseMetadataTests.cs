using System.Reflection;
using IwashiScope.App.Wpf;

namespace IwashiScope.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void AppAssemblyIdentifiesVersion0953()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?
            .Version;

        Assert.Equal("0.9.5.3", informationalVersion);
        Assert.Equal("0.9.5.3", fileVersion);
        Assert.Equal(new Version(0, 9, 5, 3), assembly.GetName().Version);
    }
}
