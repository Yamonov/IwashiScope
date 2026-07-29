import AppKit
import ImageIO
import SwiftUI
import UniformTypeIdentifiers

@MainActor
enum MeasurementExportImageRenderer {
    static let pixelWidth = 3_000
    static let rasterScale: CGFloat = 3
    private static let logicalSize = CGSize(width: 1_000, height: 500)

    static func spectrumPNG(
        measurement: SpotMeasurement,
        usesPracticalSpectrumRange: Bool,
        includesD50Reference: Bool,
        includesD65Reference: Bool
    ) throws -> Data {
        try png(
            SpectrumChartView(
                mode: measurement.mode,
                measurement: measurement,
                calibrationCompleted: true,
                showsReferenceControls: false,
                usesPracticalSpectrumRange: usesPracticalSpectrumRange,
                roundsPlotAreaCorners: false,
                initialShowsD50Reference: includesD50Reference,
                initialShowsD65Reference: includesD65Reference
            )
        )
    }

    static func criPNG(measurement: SpotMeasurement) throws -> Data {
        try png(ColorRenderingChartView(measurement: measurement))
    }

    static func tm30PNG(measurement: SpotMeasurement) throws -> Data {
        try png(TM30ColorVectorGraphicView(measurement: measurement))
    }

    private static func png<Content: View>(_ content: Content) throws -> Data {
        let canvas = MeasurementExportImageCanvas(
            size: logicalSize,
            displayScale: rasterScale,
            content: content
        )
        let hostingView = NSHostingView(rootView: canvas)
        hostingView.frame = CGRect(origin: .zero, size: logicalSize)
        hostingView.appearance = NSAppearance(named: .aqua)
        hostingView.wantsLayer = true
        hostingView.layoutSubtreeIfNeeded()
        applyContentsScale(rasterScale, to: hostingView.layer)

        guard let bitmap = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: Int(logicalSize.width * rasterScale),
            pixelsHigh: Int(logicalSize.height * rasterScale),
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ) else {
            throw MeasurementExportError.imageRenderingFailed
        }
        bitmap.size = logicalSize

        guard let graphicsContext = NSGraphicsContext(
            bitmapImageRep: bitmap
        ) else {
            throw MeasurementExportError.imageRenderingFailed
        }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = graphicsContext
        graphicsContext.cgContext.setFillColor(NSColor.white.cgColor)
        graphicsContext.cgContext.fill(
            CGRect(origin: .zero, size: logicalSize)
        )
        hostingView.displayIgnoringOpacity(hostingView.bounds, in: graphicsContext)
        NSGraphicsContext.restoreGraphicsState()

        guard bitmap.pixelsWide == pixelWidth,
              let image = bitmap.cgImage else {
            throw MeasurementExportError.pngEncodingFailed
        }
        return try pngData(
            from: try sRGBImageWithoutAlpha(from: image)
        )
    }

    static func applyContentsScale(
        _ scale: CGFloat,
        to layer: CALayer?
    ) {
        guard let layer else { return }
        layer.contentsScale = scale
        for sublayer in layer.sublayers ?? [] {
            applyContentsScale(scale, to: sublayer)
        }
    }

    private static func sRGBImageWithoutAlpha(
        from image: CGImage
    ) throws -> CGImage {
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(
                  data: nil,
                  width: image.width,
                  height: image.height,
                  bitsPerComponent: 8,
                  bytesPerRow: 0,
                  space: colorSpace,
                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue
                      | CGBitmapInfo.byteOrder32Big.rawValue
              ) else {
            throw MeasurementExportError.imageRenderingFailed
        }

        let bounds = CGRect(
            x: 0,
            y: 0,
            width: image.width,
            height: image.height
        )
        context.setFillColor(
            red: 1,
            green: 1,
            blue: 1,
            alpha: 1
        )
        context.fill(bounds)
        context.setRenderingIntent(.relativeColorimetric)
        context.draw(image, in: bounds)

        guard let convertedImage = context.makeImage() else {
            throw MeasurementExportError.imageRenderingFailed
        }
        return convertedImage
    }

    private static func pngData(from image: CGImage) throws -> Data {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(
            data,
            UTType.png.identifier as CFString,
            1,
            nil
        ) else {
            throw MeasurementExportError.pngEncodingFailed
        }

        let properties: [CFString: Any] = [
            kCGImagePropertyHasAlpha: false,
            kCGImagePropertyPNGDictionary: [
                kCGImagePropertyPNGsRGBIntent: NSNumber(value: 0),
            ],
        ]
        CGImageDestinationAddImage(
            destination,
            image,
            properties as CFDictionary
        )
        guard CGImageDestinationFinalize(destination) else {
            throw MeasurementExportError.pngEncodingFailed
        }
        return data as Data
    }
}

private struct MeasurementExportImageCanvas<Content: View>: View {
    private static var contentInset: CGFloat { 20 }

    let size: CGSize
    let displayScale: CGFloat
    let content: Content

    var body: some View {
        ZStack {
            Color.white
            content
                .frame(
                    width: size.width - Self.contentInset * 2,
                    height: size.height - Self.contentInset * 2
                )
                .padding(Self.contentInset)
        }
        .frame(width: size.width, height: size.height)
        .environment(\.colorScheme, .light)
        .environment(\.displayScale, displayScale)
    }
}
