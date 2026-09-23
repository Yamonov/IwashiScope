using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace IwashiScope.App.Wpf.ColorManagement;

/// <summary>Per-window, read-only profile watcher. Has no access to measurement persistence.</summary>
public sealed class DisplayProfileController : IDisposable
{
    private readonly Window _window;
    private readonly Action<DisplayColorContext> _changed;
    private readonly DispatcherTimer _timer;
    private HwndSource? _source;
    private DisplayColorContext? _current;
    private string? _fingerprint;
    private bool _busy, _disposed;
    public DisplayProfileController(Window window, Action<DisplayColorContext> changed)
    {
        _window = window; _changed = changed;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Refresh(), window.Dispatcher);
        window.SourceInitialized += Initialized;
        window.Activated += Activated;
        window.LocationChanged += Activated;
        window.Closed += Closed;
        if (new WindowInteropHelper(window).Handle != 0) Initialized(window, EventArgs.Empty);
    }
    private void Initialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WindowMessage);
        Refresh();
    }
    private void Activated(object? sender, EventArgs args) => Refresh();
    private void Closed(object? sender, EventArgs args) => Dispose();
    private nint WindowMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message is 0x007E or 0x001A or 0x02E0) Refresh(); // display/settings/DPI
        return 0;
    }
    public async void Refresh()
    {
        if (_disposed || _busy) return;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == 0) return;
        _busy = true;
        try
        {
            var loaded = await Task.Run(() => Load(hwnd, _fingerprint));
            if (_disposed) { loaded?.Context.Transform?.Dispose(); return; }
            if (loaded == null) return;
            var previous = _current;
            _current = loaded.Context; _fingerprint = loaded.Fingerprint;
            DisplayColorContext.SetContext(_window, _current);
            _changed(_current);
            previous?.Transform?.Dispose();
        }
        finally { _busy = false; }
    }
    private sealed record Loaded(string Fingerprint, DisplayColorContext Context);
    internal static DisplayColorContext ReadCurrent(nint hwnd) => Load(hwnd, null)!.Context;
    private static Loaded? Load(nint hwnd, string? previous)
    {
        var device = new StringBuilder(32); var path = new StringBuilder(32768); var mode = -1;
        try
        {
            var error = NativeDisplayInfo(hwnd, device, 32, path, 32768, out mode);
            byte[]? bytes = null;
            string? warning = null;
            if (error == 0 && path.Length > 0)
            {
                var file = new FileInfo(path.ToString());
                if (file.Length is < 128 or > 32 * 1024 * 1024) throw new InvalidDataException("Invalid display ICC size.");
                bytes = File.ReadAllBytes(file.FullName);
            }
            else warning = $"有効なモニターICCを取得できません / No effective monitor ICC (Windows {error}).";
            var fingerprint = $"{device}|{path}|{mode}|{(bytes == null ? "none" : Convert.ToHexString(SHA256.HashData(bytes)))}";
            if (fingerprint == previous) return null;
            var transform = new IccColorTransform(bytes);
            return new(fingerprint, new(transform, device.ToString(), bytes == null ? "" : path.ToString(), mode, warning));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            var fingerprint = $"error|{device}|{path}|{mode}|{exception.Message}";
            return fingerprint == previous ? null : new(fingerprint, new(null, device.ToString(), "", mode,
                "ICC表示を使用できません / ICC display unavailable: " + exception.Message));
        }
    }
    [DllImport(IccColorTransform.Native.Dll, EntryPoint = "iw_display_info", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int NativeDisplayInfo(nint window, StringBuilder device, uint deviceCount, StringBuilder profile, uint profileCount, out int advancedMode);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _source?.RemoveHook(WindowMessage);
        _window.SourceInitialized -= Initialized; _window.Activated -= Activated;
        _window.LocationChanged -= Activated; _window.Closed -= Closed;
        _current?.Transform?.Dispose(); _current = null;
    }
}
