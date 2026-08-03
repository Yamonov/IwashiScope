import Foundation

struct LabABChartScale: Equatable, Sendable {
    let limit: Double

    static func resolve(a: Double, b: Double) -> LabABChartScale {
        guard a.isFinite, b.isFinite else {
            return LabABChartScale(limit: 100)
        }

        let magnitude = max(abs(a), abs(b))
        let limit: Double
        switch magnitude {
        case ...5:
            limit = 5
        case ...10:
            limit = 10
        case ...25:
            limit = 25
        case ...50:
            limit = 50
        default:
            limit = 100
        }
        return LabABChartScale(limit: limit)
    }

    var domain: ClosedRange<Double> {
        -limit...limit
    }

    var positiveLabel: String {
        Int(limit).formatted()
    }

    var negativeLabel: String {
        (-Int(limit)).formatted()
    }
}
