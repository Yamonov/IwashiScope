using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace IwashiScope.Tests;

internal static class WpfTestRendering
{
    // Per-test-process only: deterministic offscreen rendering, including remote/disconnected sessions.
    // Does not change product rendering, registry, display settings or user preferences.
    [ModuleInitializer]
    internal static void Initialize() => RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
}
