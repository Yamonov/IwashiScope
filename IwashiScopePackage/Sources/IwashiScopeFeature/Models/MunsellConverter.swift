import Foundation

struct MunsellNotation: Equatable, Sendable {
    let hue: Double?
    let hueDesignator: String?
    let value: Double
    let chroma: Double

    var formatted: String {
        guard let hue, let hueDesignator else {
            return "N \(Self.number(value))"
        }
        return "\(Self.number(hue))\(hueDesignator) \(Self.number(value))/\(Self.number(chroma))"
    }

    private static func number(_ value: Double) -> String {
        let rounded = (value * 10).rounded() / 10
        if abs(rounded - rounded.rounded()) < 0.000_001 {
            return String(Int(rounded.rounded()))
        }
        return String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), rounded)
    }
}

enum MunsellConverter {
    private struct ColorimetrySample {
        let wavelength: Double
        let illuminant: Double
        let xBar: Double
        let yBar: Double
        let zBar: Double
    }

    private struct ReflectancePoint {
        let wavelength: Double
        let value: Double
    }

    private struct LabPoint {
        let lightness: Double
        let a: Double
        let b: Double

        var chroma: Double { hypot(a, b) }
    }

    private struct RenotationSample {
        let astmHue: Double
        let value: Double
        let chroma: Double
        let lab: LabPoint
    }

    private struct WeightedSample {
        let sample: RenotationSample
        let squaredDistance: Double
    }

    private static let illuminantCWhite = (x: 0.9807059717, y: 1.0, z: 1.1822494939)
    private static let neighborCount = 12
    private static let neutralChromaThreshold = 0.5
    private static let renotationSamples = loadRenotationSamples()
    private static let colorimetrySamples = loadColorimetrySamples()

    static func convert(reflectanceSpectrum: [SpectralSample]) -> MunsellNotation? {
        guard let xyz = illuminantCXYZ(reflectanceSpectrum: reflectanceSpectrum),
              !renotationSamples.isEmpty else {
            return nil
        }

        let normalizedXYZ = (
            x: xyz.first / 100,
            y: xyz.second / 100,
            z: xyz.third / 100
        )
        let target = labPoint(from: normalizedXYZ, white: illuminantCWhite)
        guard [normalizedXYZ.x, normalizedXYZ.y, normalizedXYZ.z,
               target.lightness, target.a, target.b]
            .allSatisfy(\.isFinite) else {
            return nil
        }

        let value = munsellValue(forLuminanceFactor: xyz.second)
        let nearbyByValue = renotationSamples.filter { abs($0.value - value) <= 1.1 }
        let pool = nearbyByValue.isEmpty ? renotationSamples : nearbyByValue
        let neighbors = pool
            .map { sample in
                let deltaL = sample.lab.lightness - target.lightness
                let deltaA = sample.lab.a - target.a
                let deltaB = sample.lab.b - target.b
                return WeightedSample(
                    sample: sample,
                    squaredDistance: deltaL * deltaL + deltaA * deltaA + deltaB * deltaB
                )
            }
            .sorted { $0.squaredDistance < $1.squaredDistance }
            .prefix(neighborCount)

        guard !neighbors.isEmpty else { return nil }

        var sine = 0.0
        var cosine = 0.0
        var chromaScale = 0.0
        var totalWeight = 0.0
        for neighbor in neighbors {
            let weight = 1 / (neighbor.squaredDistance + 0.000_001)
            let angle = neighbor.sample.astmHue * 2 * .pi / 100
            sine += weight * sin(angle)
            cosine += weight * cos(angle)
            chromaScale += weight * neighbor.sample.chroma / max(neighbor.sample.lab.chroma, 0.000_001)
            totalWeight += weight
        }

        guard totalWeight.isFinite, totalWeight > 0 else { return nil }
        let chroma = min(max(target.chroma * chromaScale / totalWeight, 0), 50)
        if chroma < neutralChromaThreshold {
            return MunsellNotation(hue: nil, hueDesignator: nil, value: value, chroma: 0)
        }

        var astmHue = atan2(sine, cosine) * 100 / (2 * .pi)
        if astmHue <= 0 {
            astmHue += 100
        }
        let hue = hueNotation(fromASTMHue: astmHue)
        return MunsellNotation(
            hue: hue.prefix,
            hueDesignator: hue.designator,
            value: value,
            chroma: chroma
        )
    }

    static func illuminantCXYZ(
        reflectanceSpectrum: [SpectralSample]
    ) -> Vector3? {
        let reflectancePoints = normalizedReflectancePoints(reflectanceSpectrum)
        guard reflectancePoints.count >= 2,
              let firstReflectance = reflectancePoints.first,
              let lastReflectance = reflectancePoints.last,
              let firstReference = colorimetrySamples.first,
              let lastReference = colorimetrySamples.last else {
            return nil
        }

        let lowerBound = max(firstReflectance.wavelength, firstReference.wavelength)
        let upperBound = min(lastReflectance.wavelength, lastReference.wavelength)
        guard upperBound - lowerBound >= 1 else { return nil }

        var wavelengths = [lowerBound]
        wavelengths.append(contentsOf: colorimetrySamples.lazy
            .map(\.wavelength)
            .filter { $0 > lowerBound && $0 < upperBound })
        wavelengths.append(upperBound)

        var xIntegral = 0.0
        var yIntegral = 0.0
        var zIntegral = 0.0
        var normalizationIntegral = 0.0
        var previous: (wavelength: Double, x: Double, y: Double,
                       z: Double, normalization: Double)?

        for wavelength in wavelengths {
            guard let reflectance = interpolateReflectance(
                reflectancePoints,
                at: wavelength
            ), let reference = interpolateColorimetry(at: wavelength) else {
                return nil
            }

            let scaledReflectance = max(reflectance / 100, 0)
            let current = (
                wavelength: wavelength,
                x: scaledReflectance * reference.illuminant * reference.xBar,
                y: scaledReflectance * reference.illuminant * reference.yBar,
                z: scaledReflectance * reference.illuminant * reference.zBar,
                normalization: reference.illuminant * reference.yBar
            )
            if let previous {
                let interval = current.wavelength - previous.wavelength
                xIntegral += interval * (previous.x + current.x) / 2
                yIntegral += interval * (previous.y + current.y) / 2
                zIntegral += interval * (previous.z + current.z) / 2
                normalizationIntegral += interval
                    * (previous.normalization + current.normalization) / 2
            }
            previous = current
        }

        guard normalizationIntegral.isFinite,
              normalizationIntegral > 0 else {
            return nil
        }
        let scale = 100 / normalizationIntegral
        let result = Vector3(
            first: xIntegral * scale,
            second: yIntegral * scale,
            third: zIntegral * scale
        )
        return [result.first, result.second, result.third].allSatisfy(\.isFinite)
            ? result
            : nil
    }

    private static func normalizedReflectancePoints(
        _ spectrum: [SpectralSample]
    ) -> [ReflectancePoint] {
        let sorted = spectrum
            .filter { $0.wavelength.isFinite && $0.value.isFinite }
            .sorted { $0.wavelength < $1.wavelength }
        var points: [ReflectancePoint] = []
        for sample in sorted {
            let point = ReflectancePoint(
                wavelength: sample.wavelength,
                value: sample.value
            )
            if let last = points.last,
               abs(last.wavelength - point.wavelength) < 0.000_000_001 {
                points[points.count - 1] = point
            } else {
                points.append(point)
            }
        }
        return points
    }

    private static func interpolateReflectance(
        _ points: [ReflectancePoint],
        at wavelength: Double
    ) -> Double? {
        guard let first = points.first,
              let last = points.last,
              wavelength >= first.wavelength,
              wavelength <= last.wavelength else {
            return nil
        }
        var lower = 0
        var upper = points.count - 1
        while lower + 1 < upper {
            let midpoint = (lower + upper) / 2
            if points[midpoint].wavelength <= wavelength {
                lower = midpoint
            } else {
                upper = midpoint
            }
        }
        let firstPoint = points[lower]
        if abs(firstPoint.wavelength - wavelength) < 0.000_000_001 {
            return firstPoint.value
        }
        let secondPoint = points[upper]
        let interval = secondPoint.wavelength - firstPoint.wavelength
        guard interval > 0 else { return firstPoint.value }
        let fraction = (wavelength - firstPoint.wavelength) / interval
        return firstPoint.value + fraction * (secondPoint.value - firstPoint.value)
    }

    private static func interpolateColorimetry(
        at wavelength: Double
    ) -> ColorimetrySample? {
        guard let first = colorimetrySamples.first,
              let last = colorimetrySamples.last,
              wavelength >= first.wavelength,
              wavelength <= last.wavelength else {
            return nil
        }
        let position = wavelength - first.wavelength
        let lowerIndex = min(max(Int(floor(position)), 0), colorimetrySamples.count - 1)
        let lower = colorimetrySamples[lowerIndex]
        if lowerIndex == colorimetrySamples.count - 1
            || abs(lower.wavelength - wavelength) < 0.000_000_001 {
            return lower
        }
        let upper = colorimetrySamples[lowerIndex + 1]
        let fraction = (wavelength - lower.wavelength)
            / (upper.wavelength - lower.wavelength)
        return ColorimetrySample(
            wavelength: wavelength,
            illuminant: lower.illuminant
                + fraction * (upper.illuminant - lower.illuminant),
            xBar: lower.xBar + fraction * (upper.xBar - lower.xBar),
            yBar: lower.yBar + fraction * (upper.yBar - lower.yBar),
            zBar: lower.zBar + fraction * (upper.zBar - lower.zBar)
        )
    }

    private static func hueNotation(fromASTMHue astmHue: Double) -> (prefix: Double, designator: String) {
        let designators = ["R", "YR", "Y", "GY", "G", "BG", "B", "PB", "P", "RP"]
        var normalized = astmHue.truncatingRemainder(dividingBy: 100)
        if normalized <= 0 { normalized += 100 }
        var segment = Int(floor(normalized / 10))
        var prefix = normalized - Double(segment) * 10
        if prefix < 0.000_001 {
            segment = (segment + designators.count - 1) % designators.count
            prefix = 10
        } else {
            segment %= designators.count
        }
        return (prefix, designators[segment])
    }

    private static func munsellValue(forLuminanceFactor luminance: Double) -> Double {
        let target = min(max(luminance, 0), 100)
        var lower = 0.0
        var upper = 10.0
        for _ in 0..<60 {
            let candidate = (lower + upper) / 2
            if astmLuminance(forMunsellValue: candidate) < target {
                lower = candidate
            } else {
                upper = candidate
            }
        }
        return (lower + upper) / 2
    }

    private static func astmLuminance(forMunsellValue value: Double) -> Double {
        0.00081939 * pow(value, 5)
            - 0.020484 * pow(value, 4)
            + 0.23352 * pow(value, 3)
            - 0.22533 * pow(value, 2)
            + 1.1914 * value
    }

    private static func labPoint(
        from xyz: (x: Double, y: Double, z: Double),
        white: (x: Double, y: Double, z: Double)
    ) -> LabPoint {
        let fx = labPivot(xyz.x / white.x)
        let fy = labPivot(xyz.y / white.y)
        let fz = labPivot(xyz.z / white.z)
        return LabPoint(
            lightness: 116 * fy - 16,
            a: 500 * (fx - fy),
            b: 200 * (fy - fz)
        )
    }

    private static func labPivot(_ value: Double) -> Double {
        let epsilon = 216.0 / 24_389.0
        let kappa = 24_389.0 / 27.0
        return value > epsilon ? cbrt(value) : (kappa * value + 16) / 116
    }

    private static func loadRenotationSamples() -> [RenotationSample] {
        guard let url = Bundle.module.url(
            forResource: "MunsellRenotationAll",
            withExtension: "csv"
        ), let content = try? String(contentsOf: url, encoding: .utf8) else {
            return []
        }

        return content.split(whereSeparator: \.isNewline).dropFirst().compactMap { line in
            let fields = line.split(separator: ",", omittingEmptySubsequences: false)
            guard fields.count == 6,
                  let value = Double(fields[1]),
                  let chroma = Double(fields[2]),
                  let x = Double(fields[3]),
                  let y = Double(fields[4]),
                  let rawY = Double(fields[5]),
                  abs(y) > 0.000_000_001,
                  let astmHue = astmHue(from: String(fields[0])) else {
                return nil
            }

            let luminance = rawY * 0.975 / 100
            let xyz = (
                x: x * luminance / y,
                y: luminance,
                z: (1 - x - y) * luminance / y
            )
            let sampleLab = labPoint(from: xyz, white: illuminantCWhite)
            guard [sampleLab.lightness, sampleLab.a, sampleLab.b].allSatisfy(\.isFinite),
                  sampleLab.chroma > 0.000_001 else {
                return nil
            }
            return RenotationSample(
                astmHue: astmHue,
                value: value,
                chroma: chroma,
                lab: sampleLab
            )
        }
    }

    private static func astmHue(from label: String) -> Double? {
        let designators = ["BG", "GY", "YR", "RP", "PB", "B", "G", "Y", "R", "P"]
        let codes = ["BG": 2, "GY": 4, "YR": 6, "RP": 8, "PB": 10,
                     "B": 1, "G": 3, "Y": 5, "R": 7, "P": 9]
        guard let designator = designators.first(where: { label.hasSuffix($0) }),
              let code = codes[designator],
              let prefix = Double(label.dropLast(designator.count)) else {
            return nil
        }
        let preciseHue = Double(10 * ((7 - code + 10) % 10)) + prefix
        return abs(preciseHue) < 0.000_001 ? 100 : preciseHue
    }

    private static func loadColorimetrySamples() -> [ColorimetrySample] {
        guard let url = Bundle.module.url(
            forResource: "MunsellColorimetryCIE1931",
            withExtension: "csv"
        ), let content = try? String(contentsOf: url, encoding: .utf8) else {
            return []
        }

        return content.split(whereSeparator: \.isNewline).dropFirst().compactMap { line in
            let fields = line.split(separator: ",", omittingEmptySubsequences: false)
            guard fields.count == 5,
                  let wavelength = Double(fields[0]),
                  let illuminant = Double(fields[1]),
                  let xBar = Double(fields[2]),
                  let yBar = Double(fields[3]),
                  let zBar = Double(fields[4]) else {
                return nil
            }
            return ColorimetrySample(
                wavelength: wavelength,
                illuminant: illuminant,
                xBar: xBar,
                yBar: yBar,
                zBar: zBar
            )
        }
        .sorted { $0.wavelength < $1.wavelength }
    }
}
