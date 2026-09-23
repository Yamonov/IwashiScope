import CoreGraphics
import Foundation

enum ConfusionColorMode: String, CaseIterable, Identifiable, Sendable {
    case none = "C"
    case protan = "P"
    case deutan = "D"
    case tritan = "T"

    var id: Self { self }

    var cone: PhysiologicalCone? {
        switch self {
        case .none: nil
        case .protan: .long
        case .deutan: .medium
        case .tritan: .short
        }
    }
}

/// Candidate stimuli with the same two retained cone responses as the measured stimulus.
/// This is a reference-stimulus display, not a prediction of a person's perceived palette.
struct ChromaticityConfusionBand: Sendable {
    struct Stop: Sendable {
        let location: Double
        let point: PhysiologicalChromaticityPoint
        /// Same D50 white-Y_F=100 convention as the measured LMS.
        let lms: Vector3
        /// Unbounded linear-light carrier, not a restriction to the sRGB gamut.
        let linearRGB: Vector3
    }

    struct Section: Sendable {
        let segment: CIE2006Chromaticity.LineSegment
        let lineParameterRange: ClosedRange<Double>
        let stops: [Stop]

        /// Perspective-correct interpolation: xy_F is linear along the segment, while
        /// the matched stimulus amplitude is a ratio of affine functions of position.
        func sample(at location: Double) -> Stop? {
            guard location.isFinite, (0...1).contains(location), stops.count >= 2,
                  let first = stops.first, let last = stops.last else { return nil }
            if location <= first.location { return first }
            if location >= last.location { return last }
            var lower = 0, upper = stops.count-1
            while upper-lower > 1 {
                let middle = (lower+upper)/2
                if stops[middle].location < location { lower=middle } else { upper=middle }
            }
            let a=stops[lower], b=stops[upper]
            if location == b.location { return b }
            let span=b.location-a.location
            guard span > 0 else { return b }
            let t=(location-a.location)/span
            let aXYZ=CIE2006Chromaticity.xyzF(from:a.lms), bXYZ=CIE2006Chromaticity.xyzF(from:b.lms)
            let aSum=aXYZ.first+aXYZ.second+aXYZ.third, bSum=bXYZ.first+bXYZ.second+bXYZ.third
            guard aSum.isFinite, bSum.isFinite, aSum > 0, bSum > 0 else { return nil }
            let inverseA=(1-t)/aSum, inverseB=t/bSum
            let normalizer=inverseA+inverseB
            guard normalizer.isFinite, normalizer > 0 else { return nil }
            let wa=inverseA/normalizer, wb=inverseB/normalizer
            func mix(_ a:Vector3,_ b:Vector3)->Vector3 {
                .init(first:wa*a.first+wb*b.first,second:wa*a.second+wb*b.second,third:wa*a.third+wb*b.third)
            }
            return Stop(location:location,
                        point:.init(x:a.point.x+t*(b.point.x-a.point.x),y:a.point.y+t*(b.point.y-a.point.y)),
                        lms:mix(a.lms,b.lms),linearRGB:mix(a.linearRGB,b.linearRGB))
        }
    }

    struct Candidate: Sendable {
        let point: PhysiologicalChromaticityPoint
        let lms: Vector3
        let linearRGB: Vector3
        /// Diagnostic sRGB membership only; the full band never uses this as a display filter.
        var rgb: Vector3? { ChromaticityConfusionBand.inGamutRGB(linearRGB) }
    }

    static let lineWidth: CGFloat = 20
    /// Tolerance for diagnostic sRGB membership, not for display-profile mapping.
    static let gamutTolerance = 1e-9
    let referenceLMS: Vector3
    let colorMode: ConfusionColorMode
    let segment: CIE2006Chromaticity.LineSegment
    let sections: [Section]

    var referenceLuminance: Double { CIE2006Chromaticity.xyzF(from: referenceLMS).second }

    static func make(lms: Vector3?, mode: ConfusionColorMode) -> Self? {
        guard let lms, let cone = mode.cone, let palette = ChromaticityBackgroundPalette.shared,
              let matcher = Matcher(reference: lms, mode: mode, whiteY: palette.white.xyzF.second),
              let point = CIE2006Chromaticity.point(from: CIE2006Chromaticity.xyzF(from: lms)),
              let segment = spectralSegment(through: point, cone: cone) else { return nil }

        // Keep every finite response-matched candidate across the physical locus.
        // Display-profile mapping is a separate operation and must not shorten this line.
        let knots = triangleKnots(segment: segment, palette: palette, measuredPoint: point)
        var pieces: [Piece] = []
        for (a, b) in zip(knots, knots.dropFirst()) where b - a > 1e-12 {
            let first = interpolate(segment, at: a)
            let middle = interpolate(segment, at: (a + b) / 2)
            let last = interpolate(segment, at: b)
            // Narrow red-end fan triangles can overlap near white. A midpoint alone can
            // choose one that does not contain both ends and leave a tiny artificial gap.
            guard let triangle = palette.triangles.first(where: {
                $0.weights(at: middle) != nil && $0.weights(at: first) != nil && $0.weights(at: last) != nil
            }) else { continue }
            let start = a, end = b
            guard end - start > 1e-12 else { continue }
            let steps = max(1, Int(ceil((end - start) * 1024)))
            var colors: [(Double, Candidate)] = []
            for index in 0...steps {
                let t = start + (end - start) * Double(index) / Double(steps)
                let position = interpolate(segment, at: t)
                guard let value = basis(at: position, in: triangle, matcher: matcher),
                      let candidate = matcher.match(value, at: position) else {
                    colors.removeAll()
                    break
                }
                colors.append((t, candidate))
            }
            guard colors.count >= 2 else { continue }
            if let previous = pieces.last, abs(previous.end - start) < 1e-10 {
                pieces[pieces.count - 1].end = end
                pieces[pieces.count - 1].colors.append(contentsOf: colors.dropFirst())
            } else {
                pieces.append(Piece(start: start, end: end, colors: colors))
            }
        }
        let sections = pieces.map { piece in
            Section(segment: .init(start: interpolate(segment, at: piece.start), end: interpolate(segment, at: piece.end)),
                    lineParameterRange: piece.start...piece.end,
                    stops: piece.colors.map { t, candidate in
                        Stop(location: (t - piece.start) / (piece.end - piece.start), point: candidate.point,
                             lms: candidate.lms, linearRGB: candidate.linearRGB)
                    })
        }
        // Invalid/nonphysical signals still retain the existing empty-state behavior.
        return Self(referenceLMS: lms, colorMode: mode, segment: segment, sections: sections)
    }

    /// Also returns out-of-gamut candidates for numerical verification; their rgb is nil.
    static func matchedCandidate(
        at point: PhysiologicalChromaticityPoint, referenceLMS: Vector3, mode: ConfusionColorMode
    ) -> Candidate? {
        guard let palette = ChromaticityBackgroundPalette.shared,
              let matcher = Matcher(reference: referenceLMS, mode: mode, whiteY: palette.white.xyzF.second),
              let sample = palette.sample(at: point) else { return nil }
        let value = Basis(lms: scale(lms(fromXYZF: sample.xyzF), by: matcher.whiteGain), linearRGB: sample.linearRGB)
        return matcher.match(value, at: point)
    }

    /// Exact inverse of the existing CIE 2006 LMS → XYZ_F matrix, not an XYZ1931 conversion.
    static func lms(fromXYZF xyz: Vector3) -> Vector3 {
        let s = xyz.third / 1.93485343
        let x = xyz.first - 0.36476327 * s
        let determinant = 1.94735469 * 0.34832189 + 1.41445123 * 0.68990272
        return Vector3(first: (0.34832189 * x + 1.41445123 * xyz.second) / determinant,
                       second: (-0.68990272 * x + 1.94735469 * xyz.second) / determinant, third: s)
    }

    static func inGamutRGB(_ linear: Vector3) -> Vector3? {
        let values = [linear.first, linear.second, linear.third]
        guard values.allSatisfy({ $0.isFinite && $0 >= -gamutTolerance && $0 <= 1 + gamutTolerance }) else { return nil }
        let encoded = values.map { channel in
            let value = min(1, max(0, channel)) // Floating-point boundary noise only, after rejection above.
            return value <= 0.0031308 ? 12.92 * value : 1.055 * pow(value, 1 / 2.4) - 0.055
        }
        return Vector3(first: encoded[0], second: encoded[1], third: encoded[2])
    }

    private struct Basis {
        let lms: Vector3
        let linearRGB: Vector3
    }

    private struct Piece {
        let start: Double
        var end: Double
        var colors: [(Double, Candidate)]
    }

    private struct Matcher {
        let reference: Vector3
        let retained: [Int]
        let dominant: Int
        let target: Double
        let whiteGain: Double

        init?(reference: Vector3, mode: ConfusionColorMode, whiteY: Double) {
            guard [reference.first, reference.second, reference.third].allSatisfy({ $0.isFinite && $0 >= 0 }),
                  whiteY.isFinite, whiteY > 0 else { return nil }
            switch mode {
            case .none: return nil
            case .protan: retained = [1, 2]
            case .deutan: retained = [0, 2]
            case .tritan: retained = [0, 1]
            }
            dominant = component(reference, retained[0]) >= component(reference, retained[1]) ? retained[0] : retained[1]
            target = component(reference, dominant)
            guard target > 0 else { return nil }
            self.reference = reference
            // The palette's D50 XYZ_F is normalized to sum=1; measured LMS uses white Y_F=100.
            // Convert candidate LMS to that SAME relative-white scale, without per-cone gains.
            whiteGain = 100 / whiteY
        }

        func match(_ basis: Basis, at point: PhysiologicalChromaticityPoint) -> Candidate? {
            let denominator = component(basis.lms, dominant)
            guard denominator > 0 else { return nil }
            let factor = target / denominator
            let matchedLMS = scale(basis.lms, by: factor)
            let rgb = scale(basis.linearRGB, by: factor)
            guard [matchedLMS.first, matchedLMS.second, matchedLMS.third, rgb.first, rgb.second, rgb.third].allSatisfy(\.isFinite),
                  retained.allSatisfy({ abs(component(matchedLMS, $0) - component(reference, $0)) <= 1e-9 * max(1, target) }) else { return nil }
            return Candidate(point: point, lms: matchedLMS, linearRGB: rgb)
        }
    }

    private static func basis(
        at point: PhysiologicalChromaticityPoint, in triangle: ChromaticityBackgroundPalette.Triangle, matcher: Matcher
    ) -> Basis? {
        guard let weights = triangle.weights(at: point) else { return nil }
        return Basis(lms: scale(lms(fromXYZF: triangle.mix(\.xyzF, weights: weights)), by: matcher.whiteGain),
                     linearRGB: triangle.mix(\.linearRGB, weights: weights))
    }

    private static func triangleKnots(
        segment: CIE2006Chromaticity.LineSegment, palette: ChromaticityBackgroundPalette,
        measuredPoint: PhysiologicalChromaticityPoint
    ) -> [Double] {
        let dx = segment.end.x - segment.start.x, dy = segment.end.y - segment.start.y
        let squaredLength = dx * dx + dy * dy
        var knots = [0.0, 1.0]
        knots.append(min(1, max(0, ((measuredPoint.x - segment.start.x) * dx + (measuredPoint.y - segment.start.y) * dy) / squaredLength)))
        for triangle in palette.triangles {
            let a = triangle.a.point, b = triangle.b.point
            let ex = b.x - a.x, ey = b.y - a.y
            let ax = a.x - segment.start.x, ay = a.y - segment.start.y
            let determinant = dx * ey - dy * ex
            if abs(determinant) > 1e-14 {
                let t = (ax * ey - ay * ex) / determinant
                let u = (ax * dy - ay * dx) / determinant
                if t >= 0, t <= 1, u >= -1e-10, u <= 1 + 1e-10 { knots.append(t) }
            } else if abs(ax * dy - ay * dx) < 1e-12 {
                for vertex in [a, b] {
                    let t = ((vertex.x - segment.start.x) * dx + (vertex.y - segment.start.y) * dy) / squaredLength
                    if (0...1).contains(t) { knots.append(t) }
                }
            }
        }
        return knots.sorted().reduce(into: []) { values, value in
            if values.last.map({ abs($0 - value) > 1e-10 }) ?? true { values.append(value) }
        }
    }

    private static func component(_ vector: Vector3, _ index: Int) -> Double {
        switch index { case 0: vector.first; case 1: vector.second; default: vector.third }
    }

    private static func scale(_ vector: Vector3, by factor: Double) -> Vector3 {
        Vector3(first: vector.first * factor, second: vector.second * factor, third: vector.third * factor)
    }

    /// Clip the centerline to the spectral polygon. The view does not clip the 20 pt stroke,
    /// so its round end caps remain round at the boundary.
    static func spectralSegment(
        through point: PhysiologicalChromaticityPoint, cone: PhysiologicalCone
    ) -> CIE2006Chromaticity.LineSegment? {
        guard point.isFinite,
              let line = CIE2006Chromaticity.confusionLine(through: point, cone: cone) else { return nil }
        let polygon = ChromaticityDisplayLocus.points
        guard contains(point, polygon: polygon) else { return nil }
        let dx = line.end.x - line.start.x, dy = line.end.y - line.start.y
        let lengthSquared = dx * dx + dy * dy
        guard lengthSquared > 1e-20 else { return nil }
        let pointT = ((point.x - line.start.x) * dx + (point.y - line.start.y) * dy) / lengthSquared
        var intersections = [0.0, 1.0]
        for index in polygon.indices {
            let a = polygon[index], b = polygon[(index + 1) % polygon.count]
            let ex = b.x - a.x, ey = b.y - a.y
            let ax = a.x - line.start.x, ay = a.y - line.start.y
            let determinant = dx * ey - dy * ex
            if abs(determinant) > 1e-14 {
                let t = (ax * ey - ay * ex) / determinant
                let u = (ax * dy - ay * dx) / determinant
                if t >= -1e-10, t <= 1 + 1e-10, u >= -1e-10, u <= 1 + 1e-10 {
                    intersections.append(min(1, max(0, t)))
                }
            } else if abs(ax * dy - ay * dx) < 1e-12 {
                // Several long-wavelength vertices lie on the same S=0 edge.
                for vertex in [a, b] {
                    let t = ((vertex.x - line.start.x) * dx + (vertex.y - line.start.y) * dy) / lengthSquared
                    if (0...1).contains(t) { intersections.append(t) }
                }
            }
        }
        let sorted = intersections.sorted().reduce(into: [Double]()) { values, value in
            if values.last.map({ abs($0 - value) > 1e-10 }) ?? true { values.append(value) }
        }
        var intervals: [ClosedRange<Double>] = []
        for (a, b) in zip(sorted, sorted.dropFirst()) where b - a > 1e-10 {
            guard contains(interpolate(line, at: (a + b) / 2), polygon: polygon) else { continue }
            if let previous = intervals.last, abs(previous.upperBound - a) < 1e-10 {
                intervals[intervals.count - 1] = previous.lowerBound...b
            } else {
                intervals.append(a...b)
            }
        }
        guard let interval = intervals.first(where: {
            pointT >= $0.lowerBound - 1e-10 && pointT <= $0.upperBound + 1e-10
        }) else { return nil }
        return .init(start: interpolate(line, at: interval.lowerBound),
                     end: interpolate(line, at: interval.upperBound))
    }

    private static func interpolate(
        _ segment: CIE2006Chromaticity.LineSegment, at t: Double
    ) -> PhysiologicalChromaticityPoint {
        .init(x: segment.start.x + t * (segment.end.x - segment.start.x),
              y: segment.start.y + t * (segment.end.y - segment.start.y))
    }

    private static func contains(
        _ point: PhysiologicalChromaticityPoint, polygon: [PhysiologicalChromaticityPoint]
    ) -> Bool {
        var inside = false
        for index in polygon.indices {
            let a = polygon[index], b = polygon[(index + 1) % polygon.count]
            let dx = b.x - a.x, dy = b.y - a.y
            let length = hypot(dx, dy)
            if length > 1e-14,
               abs((point.x - a.x) * dy - (point.y - a.y) * dx) <= 1e-10 * length,
               point.x >= min(a.x, b.x) - 1e-10, point.x <= max(a.x, b.x) + 1e-10,
               point.y >= min(a.y, b.y) - 1e-10, point.y <= max(a.y, b.y) + 1e-10 { return true }
            if (a.y > point.y) != (b.y > point.y),
               point.x < a.x + (point.y - a.y) * dx / dy { inside.toggle() }
        }
        return inside
    }
}

struct ChromaticityDiagramGeometry {
    static let aspectRatio = 0.8 / 0.9
    let scale: CGFloat
    let plot: CGRect

    init(size: CGSize) {
        let inset = ChromaticityConfusionBand.lineWidth / 2 + 1
        scale = max(0, min((size.width - 2 * inset) / 0.8, (size.height - 2 * inset) / 0.9))
        plot = CGRect(x: (size.width - 0.8 * scale) / 2, y: (size.height - 0.9 * scale) / 2,
                      width: 0.8 * scale, height: 0.9 * scale)
    }
}
