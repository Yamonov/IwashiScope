using IwashiScope.Core.Models;
using static IwashiScope.Core.Calculations.ChromaticityMath;

namespace IwashiScope.Core.Calculations;

/// <summary>Raw reference stimuli matching P=M/S, D=L/S or T=L/M. Display mapping is separate.</summary>
public sealed record ChromaticityConfusionBand(Vector3 ReferenceLms, ConfusionColorMode ColorMode,
    ChromaticitySegment Segment, IReadOnlyList<ChromaticityConfusionBand.Section> Sections)
{
    public const double LineWidth = 20;
    public sealed record Stop(double Location, PhysiologicalPoint Point, Vector3 Lms, Vector3 LinearRgb);
    public sealed record Section(ChromaticitySegment Segment, double Start, double End, IReadOnlyList<Stop> Stops)
    {
        public Stop? SampleAt(double location)
        {
            if (!double.IsFinite(location) || location < 0 || location > 1 || Stops.Count < 2) return null;
            if (location <= Stops[0].Location) return Stops[0];
            if (location >= Stops[^1].Location) return Stops[^1];
            var lower = 0; var upper = Stops.Count - 1;
            while (upper - lower > 1) { var mid = (lower + upper) / 2; if (Stops[mid].Location < location) lower = mid; else upper = mid; }
            var a = Stops[lower]; var b = Stops[upper];
            if (location == b.Location) return b;
            var t = (location - a.Location) / (b.Location - a.Location);
            var aSum = Sum(Cie2006Chromaticity.XyzF(a.Lms)); var bSum = Sum(Cie2006Chromaticity.XyzF(b.Lms));
            if (!double.IsFinite(aSum) || !double.IsFinite(bSum) || aSum <= 0 || bSum <= 0) return null;
            var ia = (1 - t) / aSum; var ib = t / bSum; var normalizer = ia + ib;
            if (!double.IsFinite(normalizer) || normalizer <= 0) return null;
            Vector3 Mix(Vector3 x, Vector3 y) => Add(Scale(x, ia / normalizer), Scale(y, ib / normalizer));
            return new(location, new(a.Point.X + t * (b.Point.X - a.Point.X), a.Point.Y + t * (b.Point.Y - a.Point.Y)),
                Mix(a.Lms, b.Lms), Mix(a.LinearRgb, b.LinearRgb));
        }
    }
    public sealed record Candidate(PhysiologicalPoint Point, Vector3 Lms, Vector3 LinearRgb);
    public static ChromaticityConfusionBand? Make(Vector3? lms, ConfusionColorMode mode)
    {
        if (lms is not { } reference || Matcher.Create(reference, mode) is not { } matcher ||
            Cie2006Chromaticity.Point(Cie2006Chromaticity.XyzF(reference)) is not { } point) return null;
        var cone = mode switch { ConfusionColorMode.P => PhysiologicalCone.Long, ConfusionColorMode.D => PhysiologicalCone.Medium, _ => PhysiologicalCone.Short };
        if (SpectralSegment(point, cone) is not { } segment) return null;
        var palette = ChromaticityBackgroundPalette.Shared;
        var knots = TriangleKnots(segment, palette, point);
        var pieces = new List<(double Start, double End, List<(double T, Candidate Color)> Colors)>();
        for (var i = 0; i < knots.Count - 1; i++)
        {
            var start = knots[i]; var end = knots[i + 1];
            if (end - start <= 1e-12) continue;
            var triangle = palette.Triangles.FirstOrDefault(t => t.Weights(segment.At(start)) != null &&
                t.Weights(segment.At((start + end) / 2)) != null && t.Weights(segment.At(end)) != null);
            if (triangle == null) continue;
            var steps = Math.Max(1, (int)Math.Ceiling((end - start) * 1024));
            var colors = new List<(double T, Candidate Color)>();
            for (var index = 0; index <= steps; index++)
            {
                var t = start + (end - start) * index / steps; var position = segment.At(t);
                if (triangle.Weights(position) is not { } weights) { colors.Clear(); break; }
                var basis = Scale(LmsFromXyzF(triangle.Mix(v => v.XyzF, weights)), matcher.WhiteGain);
                if (matcher.Match(basis, triangle.Mix(v => v.LinearRgb, weights), position) is not { } color) { colors.Clear(); break; }
                colors.Add((t, color));
            }
            if (colors.Count < 2) continue;
            if (pieces.Count > 0 && Math.Abs(pieces[^1].End - start) < 1e-10)
            {
                var previous = pieces[^1]; previous.Colors.AddRange(colors.Skip(1)); pieces[^1] = (previous.Start, end, previous.Colors);
            }
            else pieces.Add((start, end, colors));
        }
        return new(reference, mode, segment, pieces.Select(p => new Section(new(segment.At(p.Start), segment.At(p.End)),
            p.Start, p.End, p.Colors.Select(c => new Stop((c.T - p.Start) / (p.End - p.Start), c.Color.Point, c.Color.Lms, c.Color.LinearRgb)).ToArray())).ToArray());
    }
    public static Candidate? MatchedCandidate(PhysiologicalPoint point, Vector3 reference, ConfusionColorMode mode)
    {
        if (Matcher.Create(reference, mode) is not { } matcher || ChromaticityBackgroundPalette.Shared.SampleAt(point) is not { } sample) return null;
        return matcher.Match(Scale(LmsFromXyzF(sample.XyzF), matcher.WhiteGain), sample.LinearRgb, point);
    }
    public static Vector3 LmsFromXyzF(Vector3 xyz)
    {
        var s = xyz.Third / 1.93485343; var x = xyz.First - 0.36476327 * s;
        var determinant = 1.94735469 * 0.34832189 + 1.41445123 * 0.68990272;
        return new((0.34832189 * x + 1.41445123 * xyz.Second) / determinant,
            (-0.68990272 * x + 1.94735469 * xyz.Second) / determinant, s);
    }
    private sealed record Matcher(Vector3 Reference, int[] Retained, int Dominant, double Target, double WhiteGain)
    {
        public static Matcher? Create(Vector3 reference, ConfusionColorMode mode)
        {
            if (!Nonnegative(reference) || mode == ConfusionColorMode.C) return null;
            var retained = mode switch { ConfusionColorMode.P => new[] { 1, 2 }, ConfusionColorMode.D => new[] { 0, 2 }, _ => new[] { 0, 1 } };
            var dominant = Component(reference, retained[0]) >= Component(reference, retained[1]) ? retained[0] : retained[1];
            var target = Component(reference, dominant);
            return target > 0 ? new(reference, retained, dominant, target, 100 / ChromaticityBackgroundPalette.Shared.White.XyzF.Second) : null;
        }
        public Candidate? Match(Vector3 basis, Vector3 rgb, PhysiologicalPoint point)
        {
            var denominator = Component(basis, Dominant);
            if (denominator <= 0) return null;
            var factor = Target / denominator; var lms = Scale(basis, factor); var linear = Scale(rgb, factor);
            return lms.IsFinite && linear.IsFinite && Retained.All(i => Math.Abs(Component(lms, i) - Component(Reference, i)) <= 1e-9 * Math.Max(1, Target))
                ? new(point, lms, linear) : null;
        }
    }
    private static List<double> TriangleKnots(ChromaticitySegment segment, ChromaticityBackgroundPalette palette, PhysiologicalPoint point)
    {
        var dx = segment.End.X - segment.Start.X; var dy = segment.End.Y - segment.Start.Y; var length = dx * dx + dy * dy;
        var knots = new List<double> { 0, 1, Math.Clamp(((point.X - segment.Start.X) * dx + (point.Y - segment.Start.Y) * dy) / length, 0, 1) };
        foreach (var triangle in palette.Triangles)
        {
            var a = triangle.A.Point; var b = triangle.B.Point;
            var ex = b.X - a.X; var ey = b.Y - a.Y; var ax = a.X - segment.Start.X; var ay = a.Y - segment.Start.Y;
            var determinant = dx * ey - dy * ex;
            if (Math.Abs(determinant) > 1e-14)
            {
                var t = (ax * ey - ay * ex) / determinant; var u = (ax * dy - ay * dx) / determinant;
                if (t >= 0 && t <= 1 && u >= -1e-10 && u <= 1 + 1e-10) knots.Add(t);
            }
            else if (Math.Abs(ax * dy - ay * dx) < 1e-12)
                foreach (var vertex in new[] { a, b })
                {
                    var t = ((vertex.X - segment.Start.X) * dx + (vertex.Y - segment.Start.Y) * dy) / length;
                    if (t >= 0 && t <= 1) knots.Add(t);
                }
        }
        var sorted = new List<double>();
        foreach (var value in knots.Order()) if (sorted.Count == 0 || Math.Abs(sorted[^1] - value) > 1e-10) sorted.Add(value);
        return sorted;
    }

    public static ChromaticitySegment? SpectralSegment(PhysiologicalPoint point, PhysiologicalCone cone)
    {
        if (!point.IsFinite || Cie2006Chromaticity.ConfusionLine(point, cone) is not { } line) return null;
        var polygon = ChromaticityDisplayLocus.Points;
        if (!Contains(point, polygon)) return null;
        var dx = line.End.X - line.Start.X; var dy = line.End.Y - line.Start.Y; var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 1e-20) return null;
        var pointT = ((point.X - line.Start.X) * dx + (point.Y - line.Start.Y) * dy) / lengthSquared;
        var intersections = new List<double> { 0, 1 };
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            var ex = b.X - a.X; var ey = b.Y - a.Y; var ax = a.X - line.Start.X; var ay = a.Y - line.Start.Y;
            var determinant = dx * ey - dy * ex;
            if (Math.Abs(determinant) > 1e-14)
            {
                var t = (ax * ey - ay * ex) / determinant; var u = (ax * dy - ay * dx) / determinant;
                if (t >= -1e-10 && t <= 1 + 1e-10 && u >= -1e-10 && u <= 1 + 1e-10) intersections.Add(Math.Clamp(t, 0, 1));
            }
            else if (Math.Abs(ax * dy - ay * dx) < 1e-12)
                foreach (var vertex in new[] { a, b })
                {
                    var t = ((vertex.X - line.Start.X) * dx + (vertex.Y - line.Start.Y) * dy) / lengthSquared;
                    if (t >= 0 && t <= 1) intersections.Add(t);
                }
        }
        var sorted = new List<double>();
        foreach (var value in intersections.Order()) if (sorted.Count == 0 || Math.Abs(sorted[^1] - value) > 1e-10) sorted.Add(value);
        var intervals = new List<(double Start, double End)>();
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var a = sorted[i]; var b = sorted[i + 1];
            if (b - a <= 1e-10 || !Contains(line.At((a + b) / 2), polygon)) continue;
            if (intervals.Count > 0 && Math.Abs(intervals[^1].End - a) < 1e-10) intervals[^1] = (intervals[^1].Start, b);
            else intervals.Add((a, b));
        }
        foreach (var interval in intervals)
            if (pointT >= interval.Start - 1e-10 && pointT <= interval.End + 1e-10) return new(line.At(interval.Start), line.At(interval.End));
        return null;
    }

    public static bool Contains(PhysiologicalPoint point, IReadOnlyList<PhysiologicalPoint> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; var dx = b.X - a.X; var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 1e-14 && Math.Abs((point.X - a.X) * dy - (point.Y - a.Y) * dx) <= 1e-10 * length &&
                point.X >= Math.Min(a.X, b.X) - 1e-10 && point.X <= Math.Max(a.X, b.X) + 1e-10 &&
                point.Y >= Math.Min(a.Y, b.Y) - 1e-10 && point.Y <= Math.Max(a.Y, b.Y) + 1e-10) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < a.X + (point.Y - a.Y) * dx / dy) inside = !inside;
        }
        return inside;
    }
}
