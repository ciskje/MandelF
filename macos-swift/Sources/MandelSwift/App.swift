import AppKit
import SwiftUI

@main
struct MandelApp: App {
    var body: some Scene {
        WindowGroup(AppVersion.display) {
            ContentView()
        }
    }
}

struct ContentView: View {
    @State private var centerX = -0.5
    @State private var centerY = 0.0
    @State private var baseHeight = 2.8
    @State private var maxIter = 256
    @GestureState private var zoom: CGFloat = 1
    @GestureState private var drag: CGSize = .zero

    private let barH: CGFloat = 28

    var body: some View {
        GeometryReader { geo in
            let w = max(1, Int(geo.size.width))
            let h = max(1, Int(geo.size.height - barH))
            // 1/3 preview while gesturing, full-res on release.
            let div = (zoom != 1 || drag != CGSize.zero) ? 3 : 1
            let rw = max(1, w / div)
            let rh = max(1, h / div)
            let vh = baseHeight / Double(zoom)
            let step = vh / Double(rh)
            let cx = centerX - Double(drag.width) * step
            let cy = centerY - Double(drag.height) * step
            VStack(spacing: 0) {
                controls(cx: cx, cy: cy, vh: vh, w: rw, h: rh)
                    .frame(height: barH)
                if let img = nsImage(pixels: Mandelbrot.render(width: rw, height: rh, centerX: cx,
                                                               centerY: cy, viewHeight: vh,
                                                               maxIter: maxIter), w: rw, h: rh) {
                    Image(nsImage: img)
                        .resizable()
                        .frame(width: geo.size.width, height: CGFloat(h))
                }
            }
            .gesture(
                DragGesture()
                    .updating($drag) { v, s, _ in s = v.translation }
                    .onEnded { v in
                        // Preview step: gesture state is still live here.
                        let s = baseHeight / Double(zoom) / Double(max(1, h / 3))
                        centerX -= Double(v.translation.width) * s
                        centerY -= Double(v.translation.height) * s
                    }
                    .simultaneously(with:
                        MagnificationGesture()
                            .updating($zoom) { v, s, _ in s = v.magnification }
                            .onEnded { v in baseHeight /= Double(v.magnification) }
                    )
            )
            .onTapGesture(count: 2) { reset() }
        }
        .frame(minWidth: 480, minHeight: 360)
    }

    private func controls(cx: Double, cy: Double, vh: Double, w: Int, h: Int) -> some View {
        HStack {
            Stepper("Iterations: \(maxIter)", value: $maxIter, in: 32 ... 4096, step: 32)
            Button("Reset") { reset() }
            Spacer()
            Text(String(format: "%.5f, %.5f  h=%.4f  %dx%d", cx, cy, vh, w, h))
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 8)
    }

    private func reset() {
        centerX = -0.5
        centerY = 0.0
        baseHeight = 2.8
        maxIter = 256
    }
}

func nsImage(pixels: [UInt8], w: Int, h: Int) -> NSImage? {
    guard let provider = CGDataProvider(data: Data(pixels) as CFData),
          let cg = CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32,
                           bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(),
                           bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                           provider: provider, decode: nil, shouldInterpolate: false,
                           intent: .defaultIntent) else { return nil }
    return NSImage(cgImage: cg, size: NSSize(width: w, height: h))
}
