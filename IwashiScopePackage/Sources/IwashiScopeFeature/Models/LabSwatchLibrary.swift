struct LabSwatchLibrary: Equatable, Sendable {
    enum Ranking {
        case standard
        case colorToneFirst

        var lightnessFactor: Double {
            self == .colorToneFirst ? 2 : 1
        }
    }

    struct Match: Identifiable {
        let id: Int
        let swatch: AdobeLabSwatch
        let deltaE2000: Double
        let deltaE76: Double
        fileprivate let rankingScore: Double
    }

    let name: String
    let swatches: [AdobeLabSwatch]

    func nearestSwatches(to lab: Vector3, ranking: Ranking = .standard) -> [Match] {
        let limit = 6
        var nearest: [Match] = []
        for (index, swatch) in swatches.enumerated() {
            let score = CIEColorDifference.deltaE2000(lab, swatch.lab, lightnessFactor: ranking.lightnessFactor)
            guard score.isFinite else { continue }
            let position = nearest.firstIndex { $0.rankingScore > score } ?? nearest.endIndex
            guard position < limit, !nearest.contains(where: { $0.swatch == swatch }) else { continue }
            nearest.insert(
                Match(
                    id: index,
                    swatch: swatch,
                    deltaE2000: ranking == .standard ? score : CIEColorDifference.deltaE2000(lab, swatch.lab),
                    deltaE76: CIEColorDifference.deltaE76(lab, swatch.lab),
                    rankingScore: score
                ),
                at: position
            )
            if nearest.count > limit {
                nearest.removeLast()
            }
        }
        return nearest
    }
}
