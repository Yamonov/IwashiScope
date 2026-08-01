using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace IwashiScope.Tests;

public sealed class UiAssetParityTests
{
    private static readonly int[] RequiredIconSizes =
        [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [Fact]
    public void WindowsIconContainsEveryRequiredPngFrame()
    {
        var root = FindRepositoryRoot();
        var iconPath = Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "Resources",
            "Icons",
            "IwashiScope.ico");
        var bytes = File.ReadAllBytes(iconPath);

        Assert.Equal((ushort)0, ReadUInt16(bytes, 0));
        Assert.Equal((ushort)1, ReadUInt16(bytes, 2));
        Assert.Equal((ushort)RequiredIconSizes.Length, ReadUInt16(bytes, 4));

        var actualSizes = new List<int>();
        for (var index = 0; index < RequiredIconSizes.Length; index++)
        {
            var entryOffset = 6 + (index * 16);
            var width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            var payloadLength = ReadUInt32(bytes, entryOffset + 8);
            var payloadOffset = ReadUInt32(bytes, entryOffset + 12);

            Assert.Equal(width, height);
            Assert.True(payloadLength > 8);
            Assert.Equal(
                [137, 80, 78, 71, 13, 10, 26, 10],
                bytes.AsSpan((int)payloadOffset, 8).ToArray());
            actualSizes.Add(width);
        }

        Assert.Equal(RequiredIconSizes, actualSizes);
    }

    [Fact]
    public void WindowAndModeSelectionUseMacAppIconWithoutTextIconSubstitutes()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "IwashiScope.App.Wpf.csproj");
        var xamlPath = Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "MainWindow.xaml");
        var titleIconPath = Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "Resources",
            "Icons",
            "IwashiScope-128.png");

        var project = File.ReadAllText(projectPath);
        var xaml = File.ReadAllText(xamlPath);
        var titleIconHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(titleIconPath)));

        Assert.Contains(
            "<ApplicationIcon>Resources\\Icons\\IwashiScope.ico</ApplicationIcon>",
            project,
            StringComparison.Ordinal);
        Assert.Contains("Icon=\"Resources/Icons/IwashiScope.ico\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Source=\"Resources/Icons/IwashiScope-128.png\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Equal(
            "BB5C6F06ADE2EAC99FE05B14BF0014B779D434D4AA72FECDD7307F77343EE9D2",
            titleIconHash);

        foreach (var textSubstitute in new[] { "⌁", "▤", "☀", "◉", "⚠" })
        {
            Assert.DoesNotContain(textSubstitute, xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OptionalLightingRowsFollowTheSwiftValueConditions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "MainWindow.xaml"));

        foreach (var condition in new[]
                 {
                     "HasLux",
                     "HasCct",
                     "HasDuv",
                     "HasEv",
                     "HasClosestPlanckian",
                     "HasClosestDaylight",
                 })
        {
            Assert.Equal(
                2,
                CountOccurrences(
                    xaml,
                    $"Visibility=\"{{Binding {condition}, Converter={{StaticResource BoolToVisibility}}}}\""));
        }

        Assert.Contains("Text=\"{Binding LabGroupLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "SelectedIndex=\"{Binding SelectedRenderingTabIndex}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RgbTableHasThreeAlignedColorSpacesAndPerRowWarnings()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "MainWindow.xaml"));

        Assert.Contains("Text=\"{Binding RgbValuesLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"HEX（sRGB）\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"sRGB\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Adobe RGB\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Display P3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"R\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"G\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"B\"", xaml, StringComparison.Ordinal);
        Assert.Equal(
            3,
            CountOccurrences(
                xaml,
                "Template=\"{StaticResource GamutWarningIconTemplate}\""));
        Assert.Contains("<ColumnDefinition Width=\"20\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Grid.Row=\"0\" Grid.Column=\"4\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReflectanceAndLightingHistoryKeepSeparateThumbnailRenderers()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IwashiScope.App.Wpf",
            "MainWindow.xaml"));

        Assert.Contains("Background=\"{Binding SwatchBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "<controls:LightingHistoryChart Measurement=\"{Binding Measurement}\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "<controls:LightingHistoryChart"));
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BitConverter.ToUInt16(bytes, offset);

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BitConverter.ToUInt32(bytes, offset);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot(
        [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(sourceFilePath),
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory,
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(start!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IwashiScope.Windows.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Repository root was not found from {sourceFilePath}, " +
            $"{AppContext.BaseDirectory}, or {Environment.CurrentDirectory}.");
    }
}
