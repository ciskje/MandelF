import AppKit
import MandelCore
import SwiftUI

// MARK: - Export (§6.2)

struct ExportSheet: View {
    @ObservedObject var model: ViewerModel
    @Environment(\.dismiss) private var dismiss
    @State private var preset = 3
    @State private var customW = "3840"
    @State private var customH = "2160"
    @State private var aaSel = 0 // 0 = As view, 1..4 = 1x..8x
    @State private var useDouble = true
    @State private var progress = ""
    @State private var working = false
    @State private var cancel = Locked(false)

    static let presets: [(String, Int, Int)] = [
        ("Current view", 0, 0), ("Full HD 1920×1080", 1920, 1080),
        ("2K 2560×1440", 2560, 1440), ("4K 3840×2160", 3840, 2160),
        ("8K 7680×4320", 7680, 4320), ("Double 4K 7680×2160", 7680, 2160),
        ("Custom…", 0, 0),
    ]

    var size: (Int, Int)? {
        if preset == 0 {
            return (max(Spec.exportMin, Int(model.viewW)),
                    max(240, Int(model.viewH)))
        }
        if preset == 6 {
            guard let w = Int(customW), let h = Int(customH),
                  (Spec.exportMin ... Spec.exportMax).contains(w),
                  (Spec.exportMin ... Spec.exportMax).contains(h)
            else { return nil }
            return (w, h)
        }
        return (Self.presets[preset].1, Self.presets[preset].2)
    }

    var aa: Int { aaSel == 0 ? model.aa : 1 << (aaSel - 1) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Picker("Size:", selection: $preset) {
                ForEach(0 ..< Self.presets.count, id: \.self) { i in
                    Text(Self.presets[i].0).tag(i)
                }
            }
            if preset == 6 {
                HStack {
                    TextField("W", text: $customW).frame(width: 80)
                    Text("×")
                    TextField("H", text: $customH).frame(width: 80)
                }
                if size == nil {
                    Text("Custom dimensions: integers 320…16384 px.")
                        .foregroundStyle(.red).font(.caption)
                }
            }
            Picker("AA:", selection: $aaSel) {
                Text("As view").tag(0); Text("1x").tag(1)
                Text("2x").tag(2); Text("4x").tag(3); Text("8x").tag(4)
            }.pickerStyle(.segmented)
            Picker("Precision:", selection: $useDouble) {
                Text("32").tag(false); Text("64").tag(true)
            }.pickerStyle(.segmented)
            if let (w, h) = size {
                Text(String(format: "%d×%d (%.1f MPixel), AA%dx — %.1f MPixel of samples.",
                            w, h, Double(w * h) / 1e6, aa,
                            Double(w * aa * h * aa) / 1e6))
                    .font(.caption).foregroundStyle(.secondary)
            }
            if !progress.isEmpty { Text(progress).font(.caption) }
            HStack {
                Spacer()
                Button("Cancel") {
                    if working { cancel.value = true } else { dismiss() }
                }.keyboardShortcut(.cancelAction)
                Button("Render") { start() }.disabled(size == nil || working)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding()
        .frame(width: 420)
    }

    private func start() {
        guard let (w, h) = size else { return }
        working = true
        progress = "Rendering \(w)×\(h) AA\(aa)x…"
        let z = model.zone()
        let pal = model.palette, dbl = useDouble, k = aa, it = model.effIter
        let flag = Locked(false)
        cancel = flag
        Task.detached(priority: .userInitiated) {
            var big = [UInt8](repeating: 0, count: w * h * 4)
            let t = Spec.tile
            outer: for ty in stride(from: 0, to: h, by: t) {
                for tx in stride(from: 0, to: w, by: t) {
                    if flag.value { break outer }
                    let tw = min(t, w - tx), th = min(t, h - ty)
                    let p = RenderParams(width: tw, height: th, centerX: z.centerX,
                                         centerY: z.centerY, scale: z.scale,
                                         maxIter: it, palette: pal, aa: k,
                                         useDouble: dbl, julia: z.julia,
                                         jcx: z.jcx, jcy: z.jcy,
                                         offsetX: tx, offsetY: ty,
                                         fullWidth: w, fullHeight: h)
                    guard let tile = Fractal.render(p) else { break outer }
                    for row in 0 ..< th {
                        let src = row * tw * 4
                        let dst = ((ty + row) * w + tx) * 4
                        big.replaceSubrange(dst ..< dst + tw * 4,
                                            with: tile[src ..< src + tw * 4])
                    }
                }
            }
            let result = big
            await MainActor.run {
                working = false
                if flag.value {
                    progress = "Cancelled."
                    return
                }
                let panel = NSSavePanel()
                panel.allowedContentTypes = [.png]
                panel.nameFieldStringValue = "mandelbrot.png"
                if panel.runModal() == .OK, let url = panel.url {
                    progress = Png.write(rgba: result, width: w, height: h, to: url)
                        ? "Saved \(url.lastPathComponent)" : "Save failed."
                } else {
                    progress = ""
                }
            }
        }
    }
}

// MARK: - Video (§6.3)

struct VideoSheet: View {
    @ObservedObject var model: ViewerModel
    @Environment(\.dismiss) private var dismiss
    @State private var frameIdx = 1 // 120
    @State private var fpsIdx = 1 // 30
    @State private var aaSel = 0
    @State private var useDouble = true
    @State private var progress = ""
    @State private var working = false
    @State private var keptDir: URL?
    @State private var cancel = Locked(false)

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Picker("Frames:", selection: $frameIdx) {
                ForEach(0 ..< Spec.videoFrames.count, id: \.self) { i in
                    Text("\(Spec.videoFrames[i])").tag(i)
                }
            }.pickerStyle(.segmented)
            Picker("FPS:", selection: $fpsIdx) {
                ForEach(0 ..< Spec.videoFps.count, id: \.self) { i in
                    Text("\(Spec.videoFps[i])").tag(i)
                }
            }.pickerStyle(.segmented)
            Picker("AA:", selection: $aaSel) {
                Text("As view").tag(0); Text("1x").tag(1)
                Text("2x").tag(2); Text("4x").tag(3); Text("8x").tag(4)
            }.pickerStyle(.segmented)
            if !progress.isEmpty { Text(progress).font(.caption) }
            if let keptDir {
                Button("Reveal PNG sequence in Finder") {
                    NSWorkspace.shared.activateFileViewerSelecting([keptDir])
                }
            }
            HStack {
                Spacer()
                Button("Cancel") {
                    if working { cancel.value = true } else { dismiss() }
                }.keyboardShortcut(.cancelAction)
                Button("Start") { start() }.disabled(working)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding()
        .frame(width: 400)
    }

    private func start() {
        let n = Spec.videoFrames[frameIdx], fps = Spec.videoFps[fpsIdx]
        if Spec.fullScale / model.scale < 2.0 {
            progress = "You are already at the full set: frame a region first..."
            return
        }
        let w = max(Spec.exportMin, Int(model.viewW))
        let h = max(240, Int(model.viewH))
        let k = aaSel == 0 ? model.aa : 1 << (aaSel - 1)
        let z = model.zone()
        let pal = model.palette, dbl = useDouble
        let hasFfmpeg = Video.findFFmpeg() != nil
        var outURL: URL?
        if hasFfmpeg {
            let panel = NSSavePanel()
            panel.allowedContentTypes = [.mpeg4Movie]
            panel.nameFieldStringValue = "mandelbrot.mp4"
            guard panel.runModal() == .OK, let url = panel.url else { return }
            outURL = url
        }
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("MandelSwift-video-\(UUID().uuidString.prefix(8))",
                                    isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        working = true
        keptDir = nil
        let flag = Locked(false)
        cancel = flag
        Task.detached(priority: .userInitiated) {
            let frames = Video.frames(fromScale: z.scale, fromX: z.centerX,
                                      fromY: z.centerY, count: n)
            var ok = true
            for (i, f) in frames.enumerated() {
                if flag.value { ok = false; break }
                await MainActor.run {
                    progress = "Frame \(i + 1)/\(n)…"
                }
                let p = RenderParams(width: w, height: h, centerX: f.centerX,
                                     centerY: f.centerY, scale: f.scale,
                                     maxIter: f.maxIter, palette: pal, aa: k,
                                     useDouble: dbl)
                if let px = Fractal.render(p) {
                    let url = dir.appendingPathComponent(String(format: "f%04d.png", i))
                    ok = Png.write(rgba: px, width: w, height: h, to: url) && ok
                } else { ok = false; break }
            }
            let err: String? = if flag.value {
                "cancelled"
            } else if ok, let outURL, hasFfmpeg {
                Video.encode(framesDir: dir, fps: fps, count: n,
                             out: outURL, cancel: flag)
            } else {
                nil
            }
            let remove = err == nil // delete temp dir on success OR failure (§6.3)
            if remove { try? FileManager.default.removeItem(at: dir) }
            await MainActor.run {
                working = false
                if let err {
                    progress = hasFfmpeg ? "Video failed: \(err)" : "Cancelled."
                } else if hasFfmpeg {
                    progress = "Saved \(outURL?.lastPathComponent ?? "")"
                } else {
                    progress = "ffmpeg not found — PNG sequence kept."
                    keptDir = dir
                }
            }
        }
    }
}

// MARK: - Benchmark (§6.1)

struct BenchmarkSheet: View {
    @ObservedObject var model: ViewerModel
    @Environment(\.dismiss) private var dismiss
    @State private var preview: NSImage?
    @State private var progress = "Starting…"
    @State private var result = ""
    @State private var detail = ""
    @State private var runs: [(frames: Int, seconds: Double)] = []
    @State private var running = true
    @State private var cancel = Locked(false)

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("960x540 1x AA = 960x540 elementary samples without averaging, 10915 max iterations, scale 1.0453474311811176e-04, precision CPU \(model.useDouble ? "64-bit" : "32-bit") — minimum duration 8 seconds.")
                .font(.caption).foregroundStyle(.secondary)
            HStack {
                if let preview {
                    Image(nsImage: preview).resizable()
                        .frame(width: 320, height: 180)
                }
                VStack(alignment: .leading) {
                    Text(progress).font(.body)
                    if !result.isEmpty {
                        Text(result).font(.title2)
                        Text(detail).font(.caption).foregroundStyle(.secondary)
                    }
                    chart
                }
            }
            HStack {
                Spacer()
                Button("Export CSV") { exportCSV() }.disabled(runs.isEmpty)
                Button(running ? "Cancel" : "Close") {
                    if running { cancel.value = true } else { dismiss() }
                }.keyboardShortcut(.cancelAction)
            }
        }
        .padding()
        .frame(width: 640)
        .onAppear { start() }
    }

    private var chart: some View {
        // Current + session runs, max*1.15 (§6.1; reference bars omitted:
        // the SPEC examples are other machines' values).
        Canvas { ctx, size in
            let vals = runs.map { Double($0.frames * Benchmark.samples) / $0.seconds / 1e6 }
            guard let top = vals.max(), top > 0 else { return }
            let span = top * 1.15
            let rowH = min(22.0, size.height / Double(max(1, vals.count)))
            for (i, v) in vals.enumerated() {
                let w = size.width * (v / span)
                let r = CGRect(x: 0, y: Double(i) * rowH + rowH * 0.19,
                               width: w, height: rowH * 0.62)
                ctx.fill(Path(r),
                         with: .color(i == vals.count - 1 ? Color(red: 36 / 255,
                                                                  green: 113 / 255,
                                                                  blue: 163 / 255) : .gray))
                ctx.draw(Text(String(format: "%.1f", v)).font(.caption),
                         at: CGPoint(x: w + 4, y: r.midY))
            }
        }
        .frame(height: 80)
    }

    private func start() {
        // Colored preview first (Fire, AA 1x).
        let p = RenderParams(width: 320, height: 180,
                             centerX: Benchmark.centerX, centerY: Benchmark.centerY,
                             scale: Benchmark.scale, maxIter: Benchmark.maxIter,
                             palette: 0, aa: 1, useDouble: true)
        if let px = Fractal.render(p),
           let cg = Png.cgImage(rgba: px, width: 320, height: 180) {
            preview = NSImage(cgImage: cg, size: NSSize(width: 320, height: 180))
        }
        let dbl = model.useDouble
        let flag = Locked(false)
        cancel = flag
        let t0 = Date()
        var lastTick = t0
        Task.detached(priority: .userInitiated) {
            guard let r = Benchmark.run(useDouble: dbl, tick: { el, frames in
                if flag.value { return false }
                let now = Date()
                if now.timeIntervalSince(lastTick) >= 1.0 {
                    lastTick = now
                    let rate = Double(frames * Benchmark.samples) / max(el, 0.2)
                    let pct = min(100, Int(el / Benchmark.budget * 100))
                    Task { @MainActor in
                        progress = "\(pct)%  |  \(Benchmark.formatRate(rate))"
                    }
                }
                return true
            }) else {
                await MainActor.run {
                    running = false
                    progress = "Cancelled."
                    AppLog.shared.add("Benchmark CPU: cancelled")
                }
                return
            }
            await MainActor.run {
                running = false
                let mpix = r.mpixelS
                runs.append((r.frames, r.seconds))
                result = String(format: "%.1f Mpixel/s", mpix)
                detail = String(format: "%d frames (%.1f frame/s) 960x540 AA1x in %.2f s on %@",
                                r.frames, Double(r.frames) / r.seconds,
                                r.seconds, Diagnostics.cpuName())
                progress = "Done."
            }
        }
    }

    private func exportCSV() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = "benchmark.csv"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let dbl = model.useDouble
        for (i, run) in runs.enumerated() {
            let mpix = Double(run.frames * Benchmark.samples) / run.seconds / 1e6
            try? Benchmark.csvAppend(to: url, engine: "CPU",
                                     device: Diagnostics.cpuName(),
                                     precision: dbl ? "64-bit" : "32-bit",
                                     run: i + 1, frames: run.frames,
                                     seconds: run.seconds, mpixelS: mpix)
        }
    }
}

// MARK: - Log / diagnostics (§6.4, CPU-only adaptation)

struct LogSheet: View {
    @ObservedObject var model: ViewerModel
    @Environment(\.dismiss) private var dismiss

    var text: String {
        var s = "Mandelbrot Set Viewer   v\(AppVersion.full)\n"
        s += "\(Diagnostics.osString())   Swift (no .NET runtime)\n\n"
        s += "=== Engine ===\nCPU (in use)\n\n"
        s += "=== GPU engine ===\nNot available in this build (Metal pending).\n\n"
        s += "=== GPU selection ===\nPrecision: \(model.useDouble ? "64-bit" : "32-bit")\n\n"
        let cur = Store.load()
        s += "=== Saved settings ===\nengine=\(cur.engine) palette=\(cur.palette) " +
            "aa=\(cur.aaIndex) iter=\(cur.maxIter)\(cur.iterAuto ? " (auto)" : "") " +
            "precision=\(cur.single ? "32" : "64") julia=\(cur.julia)\n\n"
        s += "=== Event log ===\n"
        let lines = AppLog.shared.all
        s += lines.isEmpty ? "(empty)" : lines.joined(separator: "\n")
        return s
    }

    var body: some View {
        VStack {
            ScrollView {
                Text(text).font(.system(.body, design: .monospaced))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled).padding()
            }
            HStack {
                Spacer()
                Button("Copy to clipboard") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(text, forType: .string)
                }
                Button("Close") { dismiss() }.keyboardShortcut(.defaultAction)
            }.padding()
        }
        .frame(width: 720, height: 480)
    }
}

// MARK: - About (§6.5)

struct AboutSheet: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Mandelbrot Set Viewer v\(AppVersion.display)").font(.title2)
            Text("CPU multicore engine (SIMD lanes, 32/64-bit). GPU: Metal pending.")
                .foregroundStyle(.secondary)
            Divider()
            Text("""
            Left click: zoom 2x in · Right click: zoom 2x out · Wheel: zoom 0.7/1.43
            Drag (5px): pan · Arrows: pan 1/10 (Shift 1/100) · R: reset · S: save PNG
            +/-: iterations · Cmd+Left/Right: history back/forward · Cmd+J: Julia mode
            Cmd+D: save favorite · Cmd+O/S: load/save zone · Cmd+B: benchmark
            Cmd+Shift+E: screenshot · Cmd+Shift+V: zoom video · Cmd+Shift+R: back to set
            Palettes: Fire, Ice, Thermal, Ocean, Violet, Desert, Forest
            Auto iterations grow with zoom · AA 1x/2x/4x/8x · Engine CPU · GPU Auto
            """).font(.caption)
            HStack {
                Spacer()
                Button("Close") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding()
        .frame(width: 460)
    }
}
