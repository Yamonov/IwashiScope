using System.Runtime.InteropServices;
using IwashiScope.App.Wpf.ColorManagement;

namespace IwashiScope.Tests;

internal static class IccTestProfiles
{
    private const string Dll = "IwashiScope.DisplayColor.dll";
    [StructLayout(LayoutKind.Sequential)] internal struct XyY(double x, double y, double luminance)
    { public double X = x, Y = y, Luminance = luminance; }
    [StructLayout(LayoutKind.Sequential)] internal struct Primaries(XyY r, XyY g, XyY b)
    { public XyY R = r, G = g, B = b; }
    internal static byte[] Wide(double version = 4.3, double gamma = 2.2)
    {
        var white = new XyY(.34567, .35850, 1);
        var primaries = new Primaries(new(.64, .33, 1), new(.21, .71, 1), new(.15, .06, 1));
        var curves = Enumerable.Range(0, 3).Select(_ => cmsBuildGamma(0, gamma)).ToArray();
        var profile = cmsCreateRGBProfile(ref white, ref primaries, curves);
        try { cmsSetProfileVersion(profile, version); return Save(profile); }
        finally { foreach (var curve in curves) cmsFreeToneCurve(curve); IccColorTransform.Native.cmsCloseProfile(profile); }
    }
    internal static byte[] LabLut()
    {
        // Synthetic test-only RGB display: B2A maps encoded PCS Lab to RGB.
        // No rXYZ/gXYZ/bXYZ or TRCs exist; a matrix-only implementation cannot pass.
        var profile = cmsCreateProfilePlaceholder(0);
        var pipeline = cmsPipelineAlloc(0, 3, 3);
        var white = Marshal.AllocHGlobal(24);
        try
        {
            cmsSetProfileVersion(profile, 4.3); cmsSetDeviceClass(profile, 0x6D6E7472);
            cmsSetColorSpace(profile, 0x52474220); cmsSetPCS(profile, 0x4C616220);
            Marshal.Copy(new[] { .9642, 1.0, .8249 }, 0, white, 3);
            Assert.NotEqual(0, cmsWriteTag(profile, 0x77747074, white));
            Assert.NotEqual(0, cmsPipelineInsertStage(pipeline, 1, cmsStageAllocToneCurves(0, 3, 0)));
            var table = new List<ushort>();
            for (var r = 0; r < 2; r++) for (var g = 0; g < 2; g++) for (var b = 0; b < 2; b++)
                table.AddRange([(ushort)(r * 65535), (ushort)(g * 65535), (ushort)(b * 65535)]);
            Assert.NotEqual(0, cmsPipelineInsertStage(pipeline, 1, cmsStageAllocCLut16bit(0, 2, 3, 3, table.ToArray())));
            Assert.NotEqual(0, cmsPipelineInsertStage(pipeline, 1, cmsStageAllocToneCurves(0, 3, 0)));
            Assert.NotEqual(0, cmsWriteTag(profile, 0x41324230, pipeline));
            Assert.NotEqual(0, cmsWriteTag(profile, 0x42324130, pipeline));
            return Save(profile);
        }
        finally { Marshal.FreeHGlobal(white); cmsPipelineFree(pipeline); IccColorTransform.Native.cmsCloseProfile(profile); }
    }
    private static byte[] Save(nint profile)
    {
        Assert.NotEqual(0, profile); uint size = 0;
        Assert.NotEqual(0, cmsSaveProfileToMem(profile, null, ref size));
        var bytes = new byte[size]; Assert.NotEqual(0, cmsSaveProfileToMem(profile, bytes, ref size)); return bytes;
    }
    [DllImport(Dll)] private static extern nint cmsBuildGamma(nint context, double gamma);
    [DllImport(Dll)] private static extern void cmsFreeToneCurve(nint curve);
    [DllImport(Dll)] private static extern nint cmsCreateRGBProfile(ref XyY white, ref Primaries primaries, nint[] curves);
    [DllImport(Dll)] private static extern void cmsSetProfileVersion(nint profile, double version);
    [DllImport(Dll)] private static extern int cmsSaveProfileToMem(nint profile, [Out] byte[]? bytes, ref uint length);
    [DllImport(Dll)] private static extern nint cmsCreateProfilePlaceholder(nint context);
    [DllImport(Dll)] private static extern void cmsSetDeviceClass(nint profile, uint value);
    [DllImport(Dll)] private static extern void cmsSetColorSpace(nint profile, uint value);
    [DllImport(Dll)] private static extern void cmsSetPCS(nint profile, uint value);
    [DllImport(Dll)] private static extern int cmsWriteTag(nint profile, uint tag, nint data);
    [DllImport(Dll)] private static extern nint cmsPipelineAlloc(nint context, uint input, uint output);
    [DllImport(Dll)] private static extern void cmsPipelineFree(nint pipeline);
    [DllImport(Dll)] private static extern int cmsPipelineInsertStage(nint pipeline, int location, nint stage);
    [DllImport(Dll)] private static extern nint cmsStageAllocToneCurves(nint context, uint channels, nint curves);
    [DllImport(Dll)] private static extern nint cmsStageAllocCLut16bit(nint context, uint grid, uint input, uint output, ushort[] table);
}
