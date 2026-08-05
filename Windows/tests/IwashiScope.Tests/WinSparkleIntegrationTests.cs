using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using IwashiScope.App.Wpf.Updates;

namespace IwashiScope.Tests;

public sealed class WinSparkleIntegrationTests
{
    private static readonly XNamespace Sparkle =
        "http://www.andymatuschak.org/xml-namespaces/sparkle";

    [Fact]
    public void UsesPinnedOfficialPackageAndWindowsOnlyFeed()
    {
        var windowsRoot = FindWindowsRoot();
        var project = File.ReadAllText(Path.Combine(
            windowsRoot,
            "src",
            "IwashiScope.App.Wpf",
            "IwashiScope.App.Wpf.csproj"));

        Assert.Contains("<PackageReference Include=\"WinSparkle\"", project);
        Assert.Contains($"Version=\"{WinSparkleUpdater.PackageVersion}\"", project);
        Assert.Contains("GeneratePathProperty=\"true\"", project);
        Assert.Contains("<PlatformTarget>x64</PlatformTarget>", project);
        Assert.Contains("Link=\"WinSparkle.dll\"", project);
        Assert.Equal(
            "https://yamonov.github.io/IwashiScope/appcast-windows.xml",
            WinSparkleUpdater.AppcastUrl);
        Assert.True(Uri.TryCreate(
            WinSparkleUpdater.AppcastUrl,
            UriKind.Absolute,
            out var feed));
        Assert.Equal(Uri.UriSchemeHttps, feed!.Scheme);
    }

    [Fact]
    public void WindowsAndMacUseTheSameValidEdDsaPublicKey()
    {
        var keyBytes = Convert.FromBase64String(WinSparkleUpdater.EdDsaPublicKey);
        Assert.Equal(32, keyBytes.Length);

        var repositoryRoot = FindRepositoryRoot();
        var macInfoPlist = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "IwashiScope",
            "Info.plist"));
        Assert.Contains(
            $"<string>{WinSparkleUpdater.EdDsaPublicKey}</string>",
            macInfoPlist,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppcastNeverOffersMacOrUnsignedPayloadsToWindows()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            "docs",
            "appcast-windows.xml"));

        Assert.Equal("rss", document.Root?.Name.LocalName);
        foreach (var enclosure in document.Descendants("enclosure"))
        {
            Assert.Equal("windows-x64", (string?)enclosure.Attribute(Sparkle + "os"));
            Assert.False(string.IsNullOrWhiteSpace(
                (string?)enclosure.Attribute(Sparkle + "edSignature")));
            Assert.StartsWith(
                "https://",
                (string?)enclosure.Attribute("url"),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseBuildCreatesAnInstallableWinSparklePayload()
    {
        var windowsRoot = FindWindowsRoot();
        var releaseScript = File.ReadAllText(Path.Combine(
            windowsRoot,
            "Scripts",
            "Build-Release.ps1"));
        var installerScript = File.ReadAllText(Path.Combine(
            windowsRoot,
            "Scripts",
            "Build-WindowsInstaller.ps1"));
        var signingTestScript = File.ReadAllText(Path.Combine(
            windowsRoot,
            "Scripts",
            "Test-WinSparkleSigning.ps1"));
        var installerSource = File.ReadAllText(Path.Combine(
            windowsRoot,
            "Scripts",
            "installer",
            "IwashiScopeAppInstaller.cs"));
        var installerCore = File.ReadAllText(Path.Combine(
            windowsRoot,
            "Scripts",
            "installer",
            "IwashiScopeInstallerCore.cs"));

        Assert.Contains("Build-WindowsInstaller.ps1", releaseScript);
        Assert.Contains("[string] $Version = '0.9.6'", releaseScript);
        Assert.Contains("Windows-x64-Setup.exe", releaseScript);
        Assert.Contains("[string] $Version = '0.9.6'", installerScript);
        Assert.Contains("Compress-Archive -Path (Join-Path $payloadFull '*')", installerScript);
        Assert.Contains("Test-WindowsInstaller.ps1", installerScript);
        Assert.Contains("/platform:x64", installerScript);
        Assert.Contains("payload.zip", installerScript);
        Assert.Contains(@"LocalApplicationData", installerSource);
        Assert.Contains(@"Programs", installerSource);
        Assert.Contains("IwashiScopeInstaller.exe", installerSource);
        Assert.Contains("--uninstall", installerSource);
        Assert.Contains("--cleanup", installerSource);
        Assert.Contains("DeleteRunningExecutableSafely", installerSource);
        Assert.Contains("IwashiScope-Uninstall-", installerSource);
        Assert.Contains("WaitForExit(15000)", installerSource);
        Assert.Contains("InstallMetadataSnapshot.Capture", installerSource);
        Assert.DoesNotContain("cmd.exe", installerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runas", installerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZipFile.ExtractToDirectory", installerSource);
        Assert.Contains("ExtractZipSafely", installerCore);
        Assert.Contains("ReplaceDirectoryTransactional", installerCore);
        Assert.Contains("DeleteInstallDirectorySafely", installerCore);
        Assert.Contains("FileRenameInfo", installerCore);
        Assert.Contains("FileDispositionInfoEx", installerCore);
        Assert.Contains("ReparsePoint", installerCore);
        Assert.Contains("<sparkle:version>0.9.6</sparkle:version>", signingTestScript);
        Assert.Contains("<sparkle:version>0.9.5.3</sparkle:version>", signingTestScript);
    }

    [Fact]
    public void AppcastGeneratorRequiresSignedX64InstallerMetadata()
    {
        var script = File.ReadAllText(Path.Combine(
            FindWindowsRoot(),
            "Scripts",
            "New-WindowsAppcast.ps1"));

        Assert.Contains("[Parameter(Mandatory = $true)]", script);
        Assert.Contains("[string] $EdSignature", script);
        Assert.Contains("sparkle:os=\"windows-x64\"", script);
        Assert.Contains("sparkle:edSignature", script);
        Assert.Contains("length=\"$($installer.Length)\"", script);
        Assert.Contains("Installer filename must be", script);
        Assert.Contains("ProductVersion/FileVersion", script);
        Assert.Contains("winsparkle-tool.exe", script);
        Assert.Contains("verify", script);
        Assert.Contains("SourceAppcastPath", script);
        Assert.Contains("Test-WindowsAppcast.ps1", script);
    }

    [Fact]
    public void NativeImportsUseCdeclAndApplicationDirectoryOnly()
    {
        var methods = typeof(WinSparkleNative)
            .GetMethods(System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static)
            .Where(method => method.GetCustomAttributes(
                typeof(DllImportAttribute),
                inherit: false).Length > 0)
            .ToArray();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var import = method.GetCustomAttributes(
                typeof(DllImportAttribute),
                inherit: false).Cast<DllImportAttribute>().Single();
            var search = method.GetCustomAttributes(
                typeof(DefaultDllImportSearchPathsAttribute),
                inherit: false).Cast<DefaultDllImportSearchPathsAttribute>().Single();

            Assert.Equal("WinSparkle.dll", import.Value);
            Assert.Equal(CallingConvention.Cdecl, import.CallingConvention);
            Assert.Equal(DllImportSearchPath.ApplicationDirectory, search.Paths);
        }
    }

    [Fact]
    public void HelpMenuExposesLocalizedManualUpdateCheck()
    {
        var windowsRoot = FindWindowsRoot();
        var xaml = File.ReadAllText(Path.Combine(
            windowsRoot,
            "src",
            "IwashiScope.App.Wpf",
            "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(
            windowsRoot,
            "src",
            "IwashiScope.App.Wpf",
            "ViewModels",
            "MainWindowViewModel.cs"));

        Assert.Contains("Header=\"{Binding CheckForUpdatesLabel}\"", xaml);
        Assert.Contains("Click=\"CheckForUpdates_Click\"", xaml);
        Assert.Contains("アップデートを確認…", viewModel);
        Assert.Contains("Check for Updates…", viewModel);
    }

    [Fact]
    public void LifecycleConfiguresSecurityCallbacksAndCleanup()
    {
        var native = new FakeWinSparkleNativeApi();
        using var updater = new WinSparkleUpdater(native);
        var shutdownRequested = false;

        Assert.True(updater.TryInitialize(
            "en-US",
            () => true,
            () => shutdownRequested = true));

        Assert.True(updater.IsInitialized);
        Assert.Equal(WinSparkleUpdater.AppcastUrl, native.AppcastUrl);
        Assert.Equal(WinSparkleUpdater.EdDsaPublicKey, native.PublicKey);
        Assert.Equal("Yamonov", native.CompanyName);
        Assert.Equal("IwashiScope", native.AppName);
        Assert.False(string.IsNullOrWhiteSpace(native.AppVersion));
        Assert.Equal("0.9.6.0", native.BuildVersion);
        Assert.Equal("en", native.Language);
        Assert.Equal(1, native.InitializeCount);
        Assert.Equal(1, native.CanShutdownCallback!());

        native.ShutdownRequestCallback!();
        Assert.True(shutdownRequested);

        updater.CheckForUpdates();
        Assert.Equal(1, native.CheckCount);

        updater.Dispose();
        Assert.False(updater.IsInitialized);
        Assert.Equal(1, native.CleanupCount);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void UpdateShutdownRejectsUnsavedOrBusySessions(
        bool hasUnsavedChanges,
        bool isBusy,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateShutdownPolicy.CanShutdown(hasUnsavedChanges, isBusy));
    }

    [Fact]
    public void JapaneseLocaleIsPassedToWinSparkle()
    {
        var native = new FakeWinSparkleNativeApi();
        using var updater = new WinSparkleUpdater(native);

        Assert.True(updater.TryInitialize("ja-JP", () => false, () => { }));
        Assert.Equal("ja", native.Language);
        Assert.Equal(0, native.CanShutdownCallback!());
    }

    [Fact]
    public void InvalidPublicKeyPreventsNativeInitialization()
    {
        var native = new FakeWinSparkleNativeApi { PublicKeyResult = 0 };
        using var updater = new WinSparkleUpdater(native);

        Assert.False(updater.TryInitialize("ja", () => true, () => { }));
        Assert.False(updater.IsInitialized);
        Assert.Contains("EdDSA public key", updater.InitializationError);
        Assert.Equal(0, native.InitializeCount);
        Assert.Throws<InvalidOperationException>(updater.CheckForUpdates);
    }

    private static string FindWindowsRoot(
        [CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IwashiScope.Windows.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Windows source root was not found.");
    }

    private static string FindRepositoryRoot()
    {
        var windowsRoot = FindWindowsRoot();
        var repositoryRoot = Directory.GetParent(windowsRoot)?.FullName;
        if (repositoryRoot is not null &&
            File.Exists(Path.Combine(repositoryRoot, "IwashiScope", "Info.plist")))
        {
            return repositoryRoot;
        }

        return windowsRoot;
    }

    private sealed class FakeWinSparkleNativeApi : IWinSparkleNativeApi
    {
        public int PublicKeyResult { get; init; } = 1;
        public string? AppcastUrl { get; private set; }
        public string? PublicKey { get; private set; }
        public string? CompanyName { get; private set; }
        public string? AppName { get; private set; }
        public string? AppVersion { get; private set; }
        public string? BuildVersion { get; private set; }
        public string? Language { get; private set; }
        public int InitializeCount { get; private set; }
        public int CleanupCount { get; private set; }
        public int CheckCount { get; private set; }
        public WinSparkleNative.CanShutdownCallback? CanShutdownCallback { get; private set; }
        public WinSparkleNative.ShutdownRequestCallback? ShutdownRequestCallback { get; private set; }

        public void Initialize() => InitializeCount++;
        public void Cleanup() => CleanupCount++;
        public void SetAppcastUrl(string url) => AppcastUrl = url;
        public int SetEdDsaPublicKey(string publicKey)
        {
            PublicKey = publicKey;
            return PublicKeyResult;
        }
        public void SetAppDetails(
            string companyName,
            string appName,
            string appVersion)
        {
            CompanyName = companyName;
            AppName = appName;
            AppVersion = appVersion;
        }
        public void SetAppBuildVersion(string buildVersion) =>
            BuildVersion = buildVersion;
        public void SetLanguage(string language) => Language = language;
        public void SetCanShutdownCallback(
            WinSparkleNative.CanShutdownCallback callback) =>
            CanShutdownCallback = callback;
        public void SetShutdownRequestCallback(
            WinSparkleNative.ShutdownRequestCallback callback) =>
            ShutdownRequestCallback = callback;
        public void CheckForUpdatesWithUi() => CheckCount++;
    }
}
