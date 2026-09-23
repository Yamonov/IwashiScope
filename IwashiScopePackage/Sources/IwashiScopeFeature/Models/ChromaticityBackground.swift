import CoreGraphics
import Foundation

/// Display-only spectral-mixture palette. Never feeds measurement or confusion-line calculations.
/// Each fan triangle represents a nonnegative mixture of a D50 reference and two spectral lights.
/// The same mixture is evaluated in XYZ_F for position and CIE 1931 XYZ for display RGB.
struct ChromaticityBackgroundPalette: Sendable {
    static let shared = ChromaticityBackgroundPalette()

    struct Vertex: Sendable {
        let point: PhysiologicalChromaticityPoint
        let xyzF: Vector3
        let xyz1931: Vector3
        let linearRGB: Vector3
    }

    struct Triangle: Sendable {
        let a: Vertex
        let b: Vertex
        let c: Vertex

        func weights(at point: PhysiologicalChromaticityPoint) -> Vector3? {
            let bx = b.point.x - a.point.x
            let by = b.point.y - a.point.y
            let cx = c.point.x - a.point.x
            let cy = c.point.y - a.point.y
            let determinant = bx * cy - by * cx
            guard abs(determinant) > 1e-14, point.isFinite else { return nil }
            let px = point.x - a.point.x
            let py = point.y - a.point.y
            let wb = (px * cy - py * cx) / determinant
            let wc = (bx * py - by * px) / determinant
            let wa = 1 - wb - wc
            // Allow roundoff on shared edges of the very narrow 1 nm red-end triangles.
            // This tolerance is in barycentric weights, far below a display pixel.
            guard wa >= -1e-9, wb >= -1e-9, wc >= -1e-9 else { return nil }
            return Vector3(first: wa, second: wb, third: wc)
        }

        func mix(_ keyPath: KeyPath<Vertex, Vector3>, weights: Vector3) -> Vector3 {
            let av = a[keyPath: keyPath], bv = b[keyPath: keyPath], cv = c[keyPath: keyPath]
            return Vector3(
                first: av.first * weights.first + bv.first * weights.second + cv.first * weights.third,
                second: av.second * weights.first + bv.second * weights.second + cv.second * weights.third,
                third: av.third * weights.first + bv.third * weights.second + cv.third * weights.third
            )
        }
    }

    struct Sample: Sendable {
        let xyzF: Vector3
        let xyz1931: Vector3
        let linearRGB: Vector3
        let rgb: Vector3
    }

    let white: Vertex
    let triangles: [Triangle]

    init?() {
        var whiteF = Vector3(first: 0, second: 0, third: 0)
        var white1931 = whiteF
        // A fixed display reference formed from the bundled 5 nm D50 spectrum.
        // The common bin width cancels when both observers are normalized by the XYZ_F sum.
        for sample in CIEReferenceIlluminant.d50.samples {
            guard let lms = CIE2006Chromaticity.spectralLMS(at: sample.wavelength),
                  let xyz = Self.observer1931(at: sample.wavelength) else { continue }
            whiteF = Self.add(whiteF, Self.scale(CIE2006Chromaticity.xyzF(from: lms), by: sample.value))
            white1931 = Self.add(white1931, Self.scale(xyz, by: sample.value))
        }
        let sum = whiteF.first + whiteF.second + whiteF.third
        guard sum.isFinite, sum > 0 else { return nil }
        let sourceWhite = Self.scale(white1931, by: 1 / sum)
        guard let white = Self.vertex(xyzF: whiteF, xyz1931: white1931, sourceWhite: sourceWhite) else {
            return nil
        }
        var rim: [Vertex] = []
        for sample in ChromaticityDisplayLocus.samples {
            guard let vertex = Self.vertex(xyzF: sample.xyzF, xyz1931: sample.xyz1931,
                                           sourceWhite: sourceWhite) else { return nil }
            rim.append(vertex)
        }
        // Wavelength order is NOT a simple polygon in xy_F: the S=0 red tail
        // doubles back, so its white-centered fan overlaps the closing purple fan.
        // Use a single non-overlapping mixture domain. The original spectral locus
        // and measured LMS are kept elsewhere and are not changed by this choice.
        rim = Self.convexRim(rim)
        guard rim.count >= 3 else { return nil }
        self.white = white
        triangles = rim.indices.map { index in
            Triangle(a: white, b: rim[index], c: rim[(index + 1) % rim.count])
        }
    }

    /// Monotone-chain hull of real spectral vertices. Shared vertices carry the same
    /// XYZ_F and XYZ1931 values into both adjacent triangles, including the red/purple
    /// join. Collinear/backtracking wavelengths must not create competing mixtures.
    private static func convexRim(_ vertices: [Vertex]) -> [Vertex] {
        let sorted = vertices.sorted {
            $0.point.x == $1.point.x ? $0.point.y < $1.point.y : $0.point.x < $1.point.x
        }
        let unique = sorted.reduce(into: [Vertex]()) { result, vertex in
            if result.last?.point != vertex.point { result.append(vertex) }
        }
        guard unique.count >= 3 else { return [] }
        func chain(_ input: [Vertex]) -> [Vertex] {
            var result: [Vertex] = []
            for vertex in input {
                while result.count >= 2 {
                    let a = result[result.count-2].point
                    let b = result[result.count-1].point
                    let c = vertex.point
                    let cross = (b.x-a.x)*(c.y-a.y) - (b.y-a.y)*(c.x-a.x)
                    // Roundoff tolerance in normalized xy_F area, not display pixels.
                    if cross > 1e-14 { break }
                    result.removeLast()
                }
                result.append(vertex)
            }
            return result
        }
        return Array(chain(unique).dropLast()) + Array(chain(Array(unique.reversed())).dropLast())
    }

    func sample(at point: PhysiologicalChromaticityPoint) -> Sample? {
        guard point.isFinite else { return nil }
        for triangle in triangles {
            if let weights = triangle.weights(at: point) {
                return Sample(
                    xyzF: triangle.mix(\.xyzF, weights: weights),
                    xyz1931: triangle.mix(\.xyz1931, weights: weights),
                    linearRGB: triangle.mix(\.linearRGB, weights: weights),
                    rgb: Self.displayRGB(triangle.mix(\.linearRGB, weights: weights))
                )
            }
        }
        return nil
    }

    /// Row zero is the top of the plot (high y_F). Outside the spectral locus stays transparent.
    func rgba(width: Int, height: Int) -> [UInt8]? {
        guard width > 0, height > 0, width <= 4096, height <= 4096 else { return nil }
        let xSpan = CIE2006Chromaticity.xRange.upperBound
        let ySpan = CIE2006Chromaticity.yRange.upperBound
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        // Rasterize triangle bounding boxes, not all triangles for every pixel.
        for triangle in triangles {
            let xs = [triangle.a.point.x, triangle.b.point.x, triangle.c.point.x]
            let ys = [triangle.a.point.y, triangle.b.point.y, triangle.c.point.y]
            let x0 = max(0, Int(floor((xs.min() ?? 0) / xSpan * Double(width))))
            let x1 = min(width - 1, Int(ceil((xs.max() ?? 0) / xSpan * Double(width))))
            let y0 = max(0, Int(floor((1 - (ys.max() ?? 0) / ySpan) * Double(height))))
            let y1 = min(height - 1, Int(ceil((1 - (ys.min() ?? 0) / ySpan) * Double(height))))
            guard x0 <= x1, y0 <= y1 else { continue }
            for row in y0...y1 {
                let y = (1 - (Double(row) + 0.5) / Double(height)) * ySpan
                for column in x0...x1 {
                    let point = PhysiologicalChromaticityPoint(x: (Double(column) + 0.5) / Double(width) * xSpan, y: y)
                    guard let weights = triangle.weights(at: point) else { continue }
                    let rgb = Self.displayRGB(triangle.mix(\.linearRGB, weights: weights))
                    let index = (row * width + column) * 4
                    pixels[index] = Self.byte(rgb.first)
                    pixels[index + 1] = Self.byte(rgb.second)
                    pixels[index + 2] = Self.byte(rgb.third)
                    pixels[index + 3] = 255
                }
            }
        }
        return pixels
    }

    private static func vertex(xyzF: Vector3, xyz1931: Vector3, sourceWhite: Vector3) -> Vertex? {
        let sum = xyzF.first + xyzF.second + xyzF.third
        guard sum.isFinite, sum > 0, let point = CIE2006Chromaticity.point(from: xyzF) else { return nil }
        let normalizedF = scale(xyzF, by: 1 / sum)
        let normalized1931 = scale(xyz1931, by: 1 / sum)
        // This adaptation is for the background's display encoding only, not the measured point.
        let d65 = Vector3(first: 0.3127 / 0.3290, second: 1, third: (1 - 0.3127 - 0.3290) / 0.3290)
        guard let displayXYZ = BradfordChromaticAdaptation.adapt(
            normalized1931, sourceWhite: sourceWhite, destinationWhite: d65
        ) else { return nil }
        // CIE 1931 XYZ (D65) → linear sRGB; rational matrix from CSS Color 4 §19.
        let rgb = Vector3(
            first: (12831.0 / 3959) * displayXYZ.first - (329.0 / 214) * displayXYZ.second - (1974.0 / 3959) * displayXYZ.third,
            second: (-851781.0 / 878810) * displayXYZ.first + (1648619.0 / 878810) * displayXYZ.second + (36519.0 / 878810) * displayXYZ.third,
            third: (705.0 / 12673) * displayXYZ.first - (2585.0 / 12673) * displayXYZ.second + (705.0 / 667) * displayXYZ.third
        )
        guard [rgb.first, rgb.second, rgb.third].allSatisfy(\.isFinite) else { return nil }
        return Vertex(point: point, xyzF: normalizedF, xyz1931: normalized1931, linearRGB: rgb)
    }

    private static func observer1931(at wavelength: Double) -> Vector3? {
        let position = (wavelength - ColorRenderingReferenceData.startWavelength) / ColorRenderingReferenceData.interval
        let index = Int(position.rounded())
        guard abs(position - Double(index)) < 1e-9,
              ColorRenderingReferenceData.xBar.indices.contains(index),
              ColorRenderingReferenceData.yBar.indices.contains(index),
              ColorRenderingReferenceData.zBar.indices.contains(index) else { return nil }
        return Vector3(first: ColorRenderingReferenceData.xBar[index],
                       second: ColorRenderingReferenceData.yBar[index],
                       third: ColorRenderingReferenceData.zBar[index])
    }

    private static func displayRGB(_ linear: Vector3) -> Vector3 {
        let r = max(0, linear.first), g = max(0, linear.second), b = max(0, linear.third)
        let peak = max(r, g, b)
        guard peak > 0 else { return Vector3(first: 0, second: 0, third: 0) }
        // xy has no brightness: use the brightest in-range rendering at each position.
        return Vector3(first: encode(r / peak), second: encode(g / peak), third: encode(b / peak))
    }

    private static func encode(_ value: Double) -> Double {
        value <= 0.0031308 ? 12.92 * value : 1.055 * pow(value, 1 / 2.4) - 0.055
    }

    private static func byte(_ value: Double) -> UInt8 { UInt8(min(255, max(0, (value * 255).rounded()))) }

    private static func scale(_ v: Vector3, by factor: Double) -> Vector3 {
        Vector3(first: v.first * factor, second: v.second * factor, third: v.third * factor)
    }

    private static func add(_ a: Vector3, _ b: Vector3) -> Vector3 {
        Vector3(first: a.first + b.first, second: a.second + b.second, third: a.third + b.third)
    }
}

@MainActor
enum ChromaticityBackgroundImage {
    static let opacity = 0.38
    private struct Entry {
        let profile: CGColorSpace
        let image: CGImage
    }
    private static var cache: [Entry] = []

    static func image(for profile: CGColorSpace?) -> CGImage? {
        guard let profile else { return nil }
        if let index = cache.firstIndex(where: { CFEqual($0.profile, profile) }) {
            let entry = cache.remove(at: index)
            cache.append(entry)
            return entry.image
        }
        guard let renderer = ChromaticityIllustrationBackground(destinationSpace: profile),
              let image = renderer.image(width: 1024, height: 1152) else { return nil }
        // Bound memory while allowing the user's three-display setup to reuse images.
        cache.append(Entry(profile: profile, image: image))
        if cache.count > 3 { cache.removeFirst() }
        return image
    }
}
