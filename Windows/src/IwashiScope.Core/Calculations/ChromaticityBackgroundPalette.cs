using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

/// <summary>Display-only spectral mixture. Position uses XYZ_F, display color uses the same mixture in 1931 XYZ.</summary>
public sealed class ChromaticityBackgroundPalette
{
    public static ChromaticityBackgroundPalette Shared { get; } = new();
    public sealed record Vertex(PhysiologicalPoint Point, Vector3 XyzF, Vector3 Xyz1931, Vector3 LinearRgb);
    public sealed record Sample(Vector3 XyzF, Vector3 Xyz1931, Vector3 LinearRgb, Vector3 Rgb);
    public sealed record Triangle(Vertex A, Vertex B, Vertex C)
    {
        public Vector3? Weights(PhysiologicalPoint point)
        {
            var bx = B.Point.X - A.Point.X; var by = B.Point.Y - A.Point.Y;
            var cx = C.Point.X - A.Point.X; var cy = C.Point.Y - A.Point.Y;
            var determinant = bx * cy - by * cx;
            if (Math.Abs(determinant) <= 1e-14 || !point.IsFinite) return null;
            var px = point.X - A.Point.X; var py = point.Y - A.Point.Y;
            var wb = (px * cy - py * cx) / determinant; var wc = (bx * py - by * px) / determinant; var wa = 1 - wb - wc;
            return wa >= -1e-9 && wb >= -1e-9 && wc >= -1e-9 ? new(wa, wb, wc) : null;
        }
        public Vector3 Mix(Func<Vertex, Vector3> selector, Vector3 weights)
        {
            var a = selector(A); var b = selector(B); var c = selector(C);
            return new(a.First * weights.First + b.First * weights.Second + c.First * weights.Third,
                a.Second * weights.First + b.Second * weights.Second + c.Second * weights.Third,
                a.Third * weights.First + b.Third * weights.Second + c.Third * weights.Third);
        }
    }
    public Vertex White { get; }
    public IReadOnlyList<Triangle> Triangles { get; }

    public ChromaticityBackgroundPalette()
    {
        var whiteF = new Vector3(0, 0, 0); var white1931 = whiteF;
        foreach (var sample in CieReferenceIlluminants.Samples(CieReferenceIlluminant.D50))
        {
            if (Cie2006Chromaticity.SpectralLms(sample.Wavelength) is not { } lms) continue;
            var position = (sample.Wavelength - ColorRenderingReferenceData.StartWavelength) / ColorRenderingReferenceData.Interval;
            var index = (int)Math.Round(position);
            if (Math.Abs(position - index) >= 1e-9 || index < 0 || index >= ColorRenderingReferenceData.XBar.Length) continue;
            var xyz = new Vector3(ColorRenderingReferenceData.XBar[index], ColorRenderingReferenceData.YBar[index], ColorRenderingReferenceData.ZBar[index]);
            whiteF = Add(whiteF, Scale(Cie2006Chromaticity.XyzF(lms), sample.Value));
            white1931 = Add(white1931, Scale(xyz, sample.Value));
        }
        var sourceWhite = Scale(white1931, 1 / (whiteF.First + whiteF.Second + whiteF.Third));
        White = MakeVertex(whiteF, white1931, sourceWhite);
        var rim = ConvexRim(ChromaticityDisplayLocus.Samples.Select(s => MakeVertex(s.XyzF, s.Xyz1931, sourceWhite))).ToArray();
        Triangles = rim.Select((v, i) => new Triangle(White, v, rim[(i + 1) % rim.Length])).ToArray();
    }

    // Wavelength order doubles back at S=0. A non-overlapping convex hull shares
    // the same spectral mixtures at each join; the 441-point physical locus is unchanged.
    private static IEnumerable<Vertex> ConvexRim(IEnumerable<Vertex> vertices)
    {
        var sorted = vertices.OrderBy(v => v.Point.X).ThenBy(v => v.Point.Y).DistinctBy(v => v.Point).ToArray();
        static List<Vertex> Chain(IEnumerable<Vertex> input)
        {
            var result = new List<Vertex>();
            foreach (var vertex in input)
            {
                while (result.Count >= 2)
                {
                    var a = result[^2].Point; var b = result[^1].Point; var c = vertex.Point;
                    if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 1e-14) break;
                    result.RemoveAt(result.Count - 1);
                }
                result.Add(vertex);
            }
            return result;
        }
        var lower = Chain(sorted); var upper = Chain(sorted.Reverse());
        return lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1));
    }

    public Sample? SampleAt(PhysiologicalPoint point)
    {
        if (!point.IsFinite) return null;
        foreach (var triangle in Triangles)
        {
            if (triangle.Weights(point) is not { } weights) continue;
            var rgb = triangle.Mix(v => v.LinearRgb, weights);
            return new(triangle.Mix(v => v.XyzF, weights), triangle.Mix(v => v.Xyz1931, weights), rgb, DisplayRgb(rgb));
        }
        return null;
    }

    /// <summary>RGBA, top row at high y_F; transparent outside the shared display locus.</summary>
    public byte[]? Rgba(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;
        var pixels = new byte[width * height * 4];
        foreach (var triangle in Triangles)
        {
            var points = new[] { triangle.A.Point, triangle.B.Point, triangle.C.Point };
            var x0 = Math.Max(0, (int)Math.Floor(points.Min(p => p.X) / 0.8 * width));
            var x1 = Math.Min(width - 1, (int)Math.Ceiling(points.Max(p => p.X) / 0.8 * width));
            var y0 = Math.Max(0, (int)Math.Floor((1 - points.Max(p => p.Y) / 0.9) * height));
            var y1 = Math.Min(height - 1, (int)Math.Ceiling((1 - points.Min(p => p.Y) / 0.9) * height));
            for (var row = y0; row <= y1; row++)
            for (var column = x0; column <= x1; column++)
            {
                var point = new PhysiologicalPoint((column + 0.5) / width * 0.8, (1 - (row + 0.5) / height) * 0.9);
                if (triangle.Weights(point) is not { } weights) continue;
                var rgb = DisplayRgb(triangle.Mix(v => v.LinearRgb, weights)); var index = (row * width + column) * 4;
                pixels[index] = Byte(rgb.First); pixels[index + 1] = Byte(rgb.Second); pixels[index + 2] = Byte(rgb.Third); pixels[index + 3] = 255;
            }
        }
        return pixels;
    }

    private static Vertex MakeVertex(Vector3 xyzF, Vector3 xyz1931, Vector3 sourceWhite)
    {
        var sum = xyzF.First + xyzF.Second + xyzF.Third;
        var normalizedF = Scale(xyzF, 1 / sum); var normalized1931 = Scale(xyz1931, 1 / sum);
        var d65 = new Vector3(0.3127 / 0.3290, 1, (1 - 0.3127 - 0.3290) / 0.3290);
        var xyz = BradfordChromaticAdaptation.Adapt(normalized1931, sourceWhite, d65) ?? throw new InvalidOperationException("Invalid display white.");
        var rgb = new Vector3((12831d / 3959) * xyz.First - (329d / 214) * xyz.Second - (1974d / 3959) * xyz.Third,
            (-851781d / 878810) * xyz.First + (1648619d / 878810) * xyz.Second + (36519d / 878810) * xyz.Third,
            (705d / 12673) * xyz.First - (2585d / 12673) * xyz.Second + (705d / 667) * xyz.Third);
        return new(Cie2006Chromaticity.Point(xyzF)!.Value, normalizedF, normalized1931, rgb);
    }
    private static Vector3 DisplayRgb(Vector3 linear)
    {
        var r = Math.Max(0, linear.First); var g = Math.Max(0, linear.Second); var b = Math.Max(0, linear.Third);
        var peak = Math.Max(r, Math.Max(g, b));
        return peak > 0 ? new(Encode(r / peak), Encode(g / peak), Encode(b / peak)) : new(0, 0, 0);
    }
    public static double Encode(double value) => value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
    private static Vector3 Scale(Vector3 v, double f) => new(v.First * f, v.Second * f, v.Third * f);
    private static Vector3 Add(Vector3 a, Vector3 b) => new(a.First + b.First, a.Second + b.Second, a.Third + b.Third);
}
