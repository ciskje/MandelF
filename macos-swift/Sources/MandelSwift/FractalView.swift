import AppKit
import MandelCore
import SwiftUI

/// Bitmap view with SPEC §5.5 mouse + §5.6 keyboard (AppKit precision).
struct FractalView: NSViewRepresentable {
    @ObservedObject var model: ViewerModel

    func makeNSView(context: Context) -> FractalNSView {
        let v = FractalNSView()
        v.model = model
        return v
    }

    func updateNSView(_ v: FractalNSView, context: Context) {
        v.model = model
        v.show(model.image)
    }
}

final class FractalNSView: NSView {
    var model: ViewerModel?
    private let iv = NSImageView()
    private var downPos: NSPoint?
    private var downButton = 0
    private var startZone: ViewZone?
    private var moved = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        iv.imageScaling = .scaleAxesIndependently
        addSubview(iv)
        iv.frame = bounds
        iv.autoresizingMask = [.width, .height]
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.activeAlways,
            .mouseEnteredAndExited, .mouseMoved], owner: self))
    }

    @available(*, unavailable)
    required init?(coder _: NSCoder) { nil }

    override var acceptsFirstResponder: Bool { true }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    func show(_ img: NSImage?) {
        guard iv.image !== img else { return }
        iv.image = img
    }

    private func topPoint(_ p: NSPoint) -> CGPoint {
        CGPoint(x: p.x, y: bounds.height - p.y)
    }

    // MARK: - Mouse (§5.5)

    override func mouseDown(with e: NSEvent) { press(e, button: 0) }
    override func rightMouseDown(with e: NSEvent) { press(e, button: 1) }

    private func press(_ e: NSEvent, button: Int) {
        window?.makeFirstResponder(self)
        downPos = convert(e.locationInWindow, from: nil)
        downButton = button
        moved = false
        Task { @MainActor in
            self.startZone = self.model?.zone()
        }
    }

    override func mouseDragged(with e: NSEvent) { drag(e) }
    override func rightMouseDragged(with e: NSEvent) { drag(e) }

    private func drag(_ e: NSEvent) {
        guard let start = downPos, let m = model else { return }
        let p = convert(e.locationInWindow, from: nil)
        let dx = p.x - start.x, dy = p.y - start.y
        if hypot(dx, dy) < Spec.dragThreshold { return }
        moved = true
        Task { @MainActor in
            // top-based delta: AppKit y is up.
            m.panBy(topDelta: CGSize(width: -dx, height: dy))
            m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: true)
        }
        downPos = p
    }

    override func mouseUp(with e: NSEvent) { release(e, button: 0) }
    override func rightMouseUp(with e: NSEvent) { release(e, button: 1) }

    private func release(_ e: NSEvent, button: Int) {
        guard let m = model else { return }
        let p = convert(e.locationInWindow, from: nil)
        let tp = topPoint(p)
        Task { @MainActor [moved, startZone] in
            if !moved {
                let pt = m.complexAt(topPoint: tp, size: m.viewSize)
                if m.julia, button == 0 {
                    m.pushHistory(m.zone())
                    m.jcx = pt.0; m.jcy = pt.1
                } else {
                    m.zoom(at: pt, factor: button == 0 ? 0.5 : 2.0)
                }
                m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: false)
            } else if let z = startZone {
                m.pushHistory(z)
                m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: false)
            }
        }
        downPos = nil
    }

    override func scrollWheel(with e: NSEvent) {
        guard let m = model, !e.isDirectionInvertedFromDevice else { return }
        let p = convert(e.locationInWindow, from: nil)
        let tp = topPoint(p)
        let f = e.deltaY > 0 ? 0.7 : 1.43 // §5.5
        Task { @MainActor in
            let pt = m.complexAt(topPoint: tp, size: m.viewSize)
            m.zoom(at: pt, factor: f)
            m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: false)
        }
    }

    override func magnify(with e: NSEvent) {
        guard let m = model else { return }
        Task { @MainActor in
            m.zoom(at: (m.centerX, m.centerY), factor: 1.0 / Double(1.0 + e.magnification),
                   push: false)
            m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: true)
        }
    }

    // MARK: - Keyboard (§5.6, macOS equivalents per §1.6)

    override func keyDown(with e: NSEvent) {
        guard let m = model else { return }
        let flags = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if e.keyCode == 53 { // Esc: cancel back-to-set only
            Task { @MainActor in m.stopBackToSet() }
            return
        }
        if flags.contains(.command) || flags.contains(.control) || flags.contains(.option) {
            super.keyDown(with: e) // menus own these
            return
        }
        guard let ch = e.charactersIgnoringModifiers else { return }
        Task { @MainActor in
            switch ch.lowercased() {
            case "r": m.reset()
            case "s": m.savePNG()
            case "+", "=": m.maxIterManual = Spec.clampIter(m.maxIterManual + 50)
            case "-": m.maxIterManual = Spec.clampIter(m.maxIterManual - 50)
            default: break
            }
            switch e.keyCode {
            case 123: m.arrow(dx: fines(e) ? -0.01 : -0.1, dy: 0) // left, in view units
            case 124: m.arrow(dx: fines(e) ? 0.01 : 0.1, dy: 0)
            case 125: m.arrow(dx: 0, dy: fines(e) ? -0.01 : -0.1)
            case 126: m.arrow(dx: 0, dy: fines(e) ? 0.01 : 0.1)
            default: break
            }
            m.requestRender(w: Int(m.viewW), h: Int(m.viewH), preview: false)
        }
        func fines(_ e: NSEvent) -> Bool {
            e.modifierFlags.contains(.shift)
        }
    }
}

private extension ViewerModel {
    var viewSize: CGSize { CGSize(width: viewW, height: viewH) }

    /// Arrow pan in fractions of the view (§5.6: 1/10, Shift 1/100).
    func arrow(dx: Double, dy: Double) {
        pushHistory(zone())
        centerX += dx * scale
        centerY += dy * scale
    }
}
