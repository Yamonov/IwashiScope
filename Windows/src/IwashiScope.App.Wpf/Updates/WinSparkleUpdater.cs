using System.Reflection;
using System.Runtime.InteropServices;

namespace IwashiScope.App.Wpf.Updates;

internal sealed class WinSparkleUpdater : IDisposable
{
    internal const string AppcastUrl =
        "https://yamonov.github.io/IwashiScope/appcast-windows.xml";
    internal const string EdDsaPublicKey =
        "g7hHfIKHUp7kyMctJVsrKI0EHa7OyGRQgRhFRDdFHXM=";
    internal const string PackageVersion = "0.9.4";

    private WinSparkleNative.CanShutdownCallback? _canShutdownCallback;
    private WinSparkleNative.ShutdownRequestCallback? _shutdownRequestCallback;
    private readonly IWinSparkleNativeApi _native;

    public WinSparkleUpdater()
        : this(new WinSparkleNativeApi())
    {
    }

    internal WinSparkleUpdater(IWinSparkleNativeApi native)
    {
        _native = native;
    }

    public bool IsInitialized { get; private set; }
    public string? InitializationError { get; private set; }

    public bool TryInitialize(
        string language,
        Func<bool> canShutdown,
        Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(canShutdown);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        if (IsInitialized)
        {
            return true;
        }

        try
        {
            var assembly = typeof(WinSparkleUpdater).Assembly;
            var version = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.9.5.2";
            var buildVersion = assembly?.GetName().Version?.ToString() ?? version;

            _native.SetAppcastUrl(AppcastUrl);
            if (_native.SetEdDsaPublicKey(EdDsaPublicKey) != 1)
            {
                throw new InvalidOperationException(
                    "WinSparkle rejected the configured EdDSA public key.");
            }

            _native.SetAppDetails("Yamonov", "IwashiScope", version);
            _native.SetAppBuildVersion(buildVersion);
            _native.SetLanguage(
                language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                    ? "en"
                    : "ja");

            _canShutdownCallback = () =>
            {
                try
                {
                    return canShutdown() ? 1 : 0;
                }
                catch
                {
                    return 0;
                }
            };
            _shutdownRequestCallback = () =>
            {
                try
                {
                    requestShutdown();
                }
                catch
                {
                    // A callback failure must not terminate the process abruptly.
                }
            };
            _native.SetCanShutdownCallback(_canShutdownCallback);
            _native.SetShutdownRequestCallback(_shutdownRequestCallback);
            _native.Initialize();

            IsInitialized = true;
            InitializationError = null;
            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
            BadImageFormatException or
            EntryPointNotFoundException or
            SEHException or
            InvalidOperationException)
        {
            InitializationError = exception.Message;
            return false;
        }
    }

    public void CheckForUpdates()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                InitializationError ?? "WinSparkle is not initialized.");
        }
        _native.CheckForUpdatesWithUi();
    }

    public void Dispose()
    {
        if (!IsInitialized)
        {
            return;
        }

        _native.Cleanup();
        IsInitialized = false;
        _canShutdownCallback = null;
        _shutdownRequestCallback = null;
    }
}

internal interface IWinSparkleNativeApi
{
    void Initialize();
    void Cleanup();
    void SetAppcastUrl(string url);
    int SetEdDsaPublicKey(string publicKey);
    void SetAppDetails(string companyName, string appName, string appVersion);
    void SetAppBuildVersion(string buildVersion);
    void SetLanguage(string language);
    void SetCanShutdownCallback(WinSparkleNative.CanShutdownCallback callback);
    void SetShutdownRequestCallback(WinSparkleNative.ShutdownRequestCallback callback);
    void CheckForUpdatesWithUi();
}

internal sealed class WinSparkleNativeApi : IWinSparkleNativeApi
{
    public void Initialize() => WinSparkleNative.Initialize();
    public void Cleanup() => WinSparkleNative.Cleanup();
    public void SetAppcastUrl(string url) => WinSparkleNative.SetAppcastUrl(url);
    public int SetEdDsaPublicKey(string publicKey) =>
        WinSparkleNative.SetEdDsaPublicKey(publicKey);
    public void SetAppDetails(string companyName, string appName, string appVersion) =>
        WinSparkleNative.SetAppDetails(companyName, appName, appVersion);
    public void SetAppBuildVersion(string buildVersion) =>
        WinSparkleNative.SetAppBuildVersion(buildVersion);
    public void SetLanguage(string language) => WinSparkleNative.SetLanguage(language);
    public void SetCanShutdownCallback(WinSparkleNative.CanShutdownCallback callback) =>
        WinSparkleNative.SetCanShutdownCallback(callback);
    public void SetShutdownRequestCallback(WinSparkleNative.ShutdownRequestCallback callback) =>
        WinSparkleNative.SetShutdownRequestCallback(callback);
    public void CheckForUpdatesWithUi() => WinSparkleNative.CheckForUpdatesWithUi();
}

internal static class WinSparkleNative
{
    private const string LibraryName = "WinSparkle.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int CanShutdownCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ShutdownRequestCallback();

    [DllImport(LibraryName, EntryPoint = "win_sparkle_init",
        CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void Initialize();

    [DllImport(LibraryName, EntryPoint = "win_sparkle_cleanup",
        CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_appcast_url",
        CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetAppcastUrl(string url);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_eddsa_public_key",
        CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern int SetEdDsaPublicKey(string publicKey);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_app_details",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetAppDetails(
        string companyName,
        string appName,
        string appVersion);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_app_build_version",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetAppBuildVersion(string buildVersion);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_lang",
        CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetLanguage(string language);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_can_shutdown_callback",
        CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetCanShutdownCallback(CanShutdownCallback callback);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_set_shutdown_request_callback",
        CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void SetShutdownRequestCallback(ShutdownRequestCallback callback);

    [DllImport(LibraryName, EntryPoint = "win_sparkle_check_update_with_ui",
        CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    internal static extern void CheckForUpdatesWithUi();
}
