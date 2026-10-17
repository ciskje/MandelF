import AppKit
import Foundation
import MandelCore
import SwiftUI

/// Main state + render orchestration (§5). All mutations on the main actor.
/// Engine selection (SPEC §5.10, §5.2 row 1).
public enum EngineSel: Hashable {
    case cpu, gpu
}

@MainActor final class ViewerModel: ObservableObject {
    // Settings-backed controls
    @Published var maxIterManual = Spec.defaultIter
    @Published var iterAuto = false
    @Published var palette = 0
    @Published var aaIndex = 0
    @Published var useDouble = true
    @Published var engineSel: EngineSel = .cpu
    @Published var gpuChoice = ""
    var metal: MetalEngine?
    @Published var julia = false
    @Published var jcx = Spec.defaultJcx
    @Published var jcy = Spec.defaultJcy
    // View (SPEC §2.2: scale = complex WIDTH)
    @Published var centerX = Spec.homeX
    @Published var centerY = Spec.homeY
    @Published var scale = Spec.fullScale
    // Output
    @Published var image: NSImage?
    @Published var status = "Ready"
    @Published var computing = false

    var history = History()
    @Published private(set) var canBack = false
    @Published private(set) var canForward = false
    private var token = 0
    private var lastPreview = Date.distantPast
    private var backTask: Task<Void, Never>?
    var viewW = 800.0
    var viewH = 600.0

    var aa: Int { 1 << aaIndex }
    var effIter: Int { iterAuto ? Spec.autoIter(scale: scale) : Spec.clampIter(maxIterManual) }

    init() {
        let s = Store.load()
        maxIterManual = s.maxIter; iterAuto = s.iterAuto; palette = s.palette
        aaIndex = s.aaIndex; useDouble = !s.single
        julia = s.julia; jcx = s.jcx; jcy = s.jcy
        gpuChoice = s.gpu
        AppLog.shared.add("settings loaded")
        if s.engine == "Gpu" {
            selectEngine(.gpu) // checked only if init succeeds (§5.10)
        }
    }

    func saveSettings() {
        var s = AppSettings()
        s.iterAuto = iterAuto; s.maxIter = maxIterManual; s.palette = palette
        s.aaIndex = aaIndex; s.engine = engineSel == .gpu ? "Gpu" : "Cpu"
        s.gpu = gpuChoice; s.single = !useDouble
        s.julia = julia; s.jcx = jcx; s.jcy = jcy
        Store.save(s)
    }

    func zone() -> ViewZone {
        ViewZone(centerX: centerX, centerY: centerY, scale: scale,
                 maxIter: effIter, julia: julia, jcx: jcx, jcy: jcy)
    }

    func pushHistory(_ z: ViewZone) {
        history.push(z)
        syncNav()
    }

    func doBack() -> ViewZone? {
        let r = history.goBack(from: zone())
        syncNav()
        return r
    }

    func doForward() -> ViewZone? {
        let r = history.goForward(from: zone())
        syncNav()
        return r
    }

    private func syncNav() {
        canBack = history.canBack
        canForward = history.canForward
    }

    // MARK: - Engine selection (§5.10)

    /// GPU selected only if init succeeds; else revert to CPU with a reason.
    func selectEngine(_ e: EngineSel) {
        if e == .gpu {
            let eng = metal ?? MetalEngine()
            if eng.tryInitialize(deviceName: gpuChoice.isEmpty ? nil : gpuChoice) {
                metal = eng
                engineSel = .gpu
                useDouble = false // float-only GPU (§2.6, §4)
                status = "GPU on '\(eng.deviceShortName)' ready."
            } else {
                metal = nil
                engineSel = .cpu
                let want = gpuChoice.isEmpty ? "Auto" : gpuChoice
                status = "GPU on '\(want)': \(eng.lastError) — switching to CPU"
                AppLog.shared.add("GPU init failed: \(eng.lastError)")
            }
        } else {
            engineSel = .cpu
        }
        requestRender(w: Int(viewW), h: Int(viewH), preview: false)
    }

    func selectDevice(_ name: String) {
        gpuChoice = name
        if engineSel == .gpu { selectEngine(.gpu) } // re-init or fall back
    }

    func activeMetal() -> MetalEngine? {
        engineSel == .gpu ? metal : nil
    }

    /// Render on the active engine. fellBack=true means the GPU failed and
    /// the CPU result was returned instead (§4.9).
    nonisolated static func renderOn(_ eng: MetalEngine?,
                                     _ p: RenderParams) -> (px: [UInt8]?, fellBack: Bool) {
        if let eng {
            if let px = eng.render(p) { return (px, false) }
            return (Fractal.render(p), true)
        }
        return (Fractal.render(p), false)
    }

    func engineLabel() -> String {
        if engineSel == .gpu, let m = metal {
            return "GPU-float \(m.deviceShortName)"
                + (MetalEngine.wantsDouble(scale: scale) ? " [beyond float!]" : "")
        }
        return "CPU-" + (useDouble ? "double" : "float")
    }

    func apply(_ z: ViewZone, push: Bool) {
        if push { pushHistory(z) }
        centerX = z.centerX; centerY = z.centerY; scale = z.scale
        maxIterManual = Spec.clampIter(z.maxIter); iterAuto = false
        julia = z.julia; jcx = z.jcx; jcy = z.jcy
        requestRender(w: Int(viewW), h: Int(viewH), preview: false)
    }

    // MARK: - Render pipeline (§3.1, §5.10)

    func requestRender(w: Int, h: Int, preview: Bool) {
        viewW = Double(max(1, w)); viewH = Double(max(1, h))
        if preview {
            if Date().timeIntervalSince(lastPreview) * 1000 < Double(Spec.previewThrottleMs) {
                return // §5.5 throttle
            }
            lastPreview = Date()
        }
        token += 1
        let t = token
        let rw = max(1, preview ? w / 2 : w)
        let rh = max(1, preview ? h / 2 : h)
        let params = RenderParams(width: rw, height: rh, centerX: centerX,
                                  centerY: centerY, scale: scale,
                                  maxIter: effIter, palette: palette,
                                  aa: preview ? 1 : aa, useDouble: useDouble,
                                  julia: julia, jcx: jcx, jcy: jcy)
        let auto = iterAuto
        let eng = activeMetal()
        computing = true
        status = "Computing \(preview ? "preview " : "")\(rw)x\(rh), iter=\(params.maxIter)..."
        Task.detached(priority: .userInitiated) {
            let (px, fellBack) = Self.renderOn(eng, params)
            await MainActor.run {
                guard t == self.token, let px, !px.isEmpty else { return }
                if fellBack {
                    self.status = "GPU render failed — falling back to CPU"
                }
                if let cg = Png.cgImage(rgba: px, width: rw, height: rh) {
                    self.image = NSImage(cgImage: cg,
                                         size: NSSize(width: rw, height: rh))
                }
                self.computing = false
                self.status = self.statusText(w: rw, h: rh, auto: auto,
                                              preview: preview)
            }
        }
    }

    func cancelRenders() {
        token += 1
        computing = false
    }

    // MARK: - Status (§5.4, CPU form)

    static func sign6(_ v: Double) -> String {
        String(format: v < 0 ? "%.6f" : "+%.6f", v)
    }

    func statusText(w: Int, h: Int, auto: Bool, preview: Bool) -> String {
        var s = "Center \(Self.sign6(centerX)) \(Self.sign6(centerY))i"
        s += String(format: " | width %.2E", scale)
        s += " | iter \(effIter)" + (auto ? " (auto)" : "")
        s += " | \(Palette.names[palette])"
        if aa > 1 { s += " AA\(aa)x" }
        s += " | engine " + engineLabel()
        if julia { s += String(format: " | Julia c=%.4f %.4fi", jcx, jcy) }
        if preview { s += " (preview)" }
        return s
    }

    // MARK: - Navigation (§5.5–§5.8, §5.11)

    func complexAt(topPoint: CGPoint, size: CGSize) -> (Double, Double) {
        let px = centerX + (topPoint.x / size.width - 0.5) * scale
        let aspect = size.height / size.width
        let py = centerY + (topPoint.y / size.height - 0.5) * scale * aspect
        return (px, py)
    }

    func zoom(at p: (Double, Double), factor: Double, push: Bool = true) {
        if push { pushHistory(zone()) }
        centerX = p.0 + (centerX - p.0) * factor
        centerY = p.1 + (centerY - p.1) * factor
        scale *= factor
    }

    func panBy(topDelta: CGSize) {
        let step = scale / viewW
        centerX -= topDelta.width * step
        centerY -= topDelta.height * step
    }

    func reset() {
        pushHistory(zone())
        centerX = Spec.homeX; centerY = Spec.homeY; scale = Spec.fullScale
        if julia { jcx = Spec.defaultJcx; jcy = Spec.defaultJcy }
    }

    func backToSet() {
        if Spec.fullScale / scale < 2.0 {
            status = "Already at the full set: frame a zone first."
            return
        }
        backTask?.cancel()
        backTask = Task { @MainActor in
            let s0 = scale, x0 = centerX, y0 = centerY
            let savedAA = aaIndex
            aaIndex = 0 // forced 1x during animation (§5.11)
            for i in 0 ..< Spec.backFrames {
                if Task.isCancelled { break }
                let t = Double(i) / Double(Spec.backFrames - 1)
                let te = 1.0 - pow(1.0 - t, 3.0)
                scale = s0 * pow(Spec.fullScale / s0, te)
                let f = (s0 - scale) / (s0 - Spec.fullScale)
                centerX = x0 + (Spec.homeX - x0) * f
                centerY = y0 + (Spec.homeY - y0) * f
                status = "Back to set \(i + 1)/\(Spec.backFrames)... (Esc to stop)"
                requestRenderNow()
                try? await Task.sleep(nanoseconds: 33_000_000)
            }
            aaIndex = savedAA
            if Task.isCancelled {
                status = "Back to set interrupted."
            } else {
                status = statusText(w: Int(viewW), h: Int(viewH),
                                    auto: iterAuto, preview: false)
            }
            requestRender(w: Int(viewW), h: Int(viewH), preview: false)
        }
    }

    func stopBackToSet() { backTask?.cancel(); backTask = nil }

    /// Synchronous-feeling full render for the animation loop.
    private func requestRenderNow() {
        let w = max(1, Int(viewW) / 2), h = max(1, Int(viewH) / 2)
        let params = RenderParams(width: w, height: h, centerX: centerX,
                                  centerY: centerY, scale: scale,
                                  maxIter: effIter, palette: palette, aa: 1,
                                  useDouble: useDouble, julia: julia,
                                  jcx: jcx, jcy: jcy)
        let (px, _) = Self.renderOn(activeMetal(), params)
        if let px = px,
           let cg = Png.cgImage(rgba: px, width: w, height: h) {
            image = NSImage(cgImage: cg, size: NSSize(width: w, height: h))
        }
    }

    // MARK: - Zones / favorites / PNG (§5.7, §5.9)

    func saveZone() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "zone.json"
        if panel.runModal() == .OK, let url = panel.url {
            do { try Store.writeZone(zone(), to: url) }
            catch { status = "Save zone failed: \(error.localizedDescription)" }
        }
    }

    func loadZone() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        if panel.runModal() == .OK, let url = panel.url {
            do {
                let z = try Store.readZone(from: url)
                if let reason = z.validated() {
                    status = "Load zone failed: \(reason)"
                } else {
                    applyLoaded(z)
                }
            } catch {
                status = "Load zone failed: \(error.localizedDescription)"
            }
        }
    }

    private func applyLoaded(_ z: ViewZone) {
        pushHistory(zone())
        centerX = z.centerX; centerY = z.centerY; scale = z.scale
        maxIterManual = Spec.clampIter(z.maxIter); iterAuto = false
        julia = z.julia; jcx = z.jcx; jcy = z.jcy
        requestRender(w: Int(viewW), h: Int(viewH), preview: false)
    }

    func saveFavorite() {
        do {
            try FileManager.default.createDirectory(at: Store.zoneDir,
                                                    withIntermediateDirectories: true)
        } catch {
            status = "Favorite failed: \(error.localizedDescription)"
            return
        }
        let panel = NSSavePanel()
        panel.directoryURL = Store.zoneDir
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "favorite.json"
        if panel.runModal() == .OK, let url = panel.url {
            // Must stay inside the zone folder (§5.9).
            guard url.path.hasPrefix(Store.zoneDir.path) else {
                status = "Favorite must stay inside the zone folder."
                return
            }
            do { try Store.writeZone(zone(), to: url) }
            catch { status = "Favorite failed: \(error.localizedDescription)" }
        }
    }

    func savePNG() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.nameFieldStringValue = "mandelbrot.png"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let p = RenderParams(width: Int(viewW), height: Int(viewH),
                             centerX: centerX, centerY: centerY, scale: scale,
                             maxIter: effIter, palette: palette, aa: aa,
                             useDouble: useDouble, julia: julia,
                             jcx: jcx, jcy: jcy)
        let eng = activeMetal()
        Task.detached(priority: .userInitiated) {
            let (px, fellBack) = Self.renderOn(eng, p)
            guard let px = px else { return }
            let ok = Png.write(rgba: px, width: p.width, height: p.height, to: url)
            await MainActor.run {
                self.status = ok ? "Saved \(url.lastPathComponent)"
                    + (fellBack ? " (CPU fallback)" : "") : "Save PNG failed."
            }
        }
    }
}
