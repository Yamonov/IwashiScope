using System.Reflection;
using IwashiScope.App.Wpf;

namespace IwashiScope.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void AppAssemblyIdentifiesVersion0952()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal("0.9.5.2", informationalVersion);
        Assert.Equal(new Version(0, 9, 5, 2), assembly.GetName().Version);
    }
}
