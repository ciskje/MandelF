import AppKit
import MandelCore
import SwiftUI

/// SPEC §5 main window (macOS mappings per §1.6: Cmd = Ctrl, Cmd+[/] = back).
@main
struct MandelApp: App {
    @StateObject private var model = ViewerModel()
    @State private var sheet: Sheet?
    @State private var favorites: [URL] = []

    enum Sheet: Identifiable {
        case export, video, benchmark, log, about
        var id: Int { hashValue }
    }

    @Environment(\.scenePhase) private var phase

    var body: some Scene {
        WindowGroup("Mandelbrot Set Viewer v\(AppVersion.display)") {
            ContentView(model: model, sheet: $sheet, favorites: $favorites)
                .frame(minWidth: 640, minHeight: 480)
                .onAppear {
                    // Bare SPM executable: become a regular app
                    // (dock icon, menu bar) once NSApp exists.
                    NSApp.setActivationPolicy(.regular)
                    NSApp.activate(ignoringOtherApps: true)
                    // Single-window utility: drop the redundant File > Close
                    // (Quit already exists; the red dot still closes).
                    if let fileMenu = NSApp.mainMenu?.item(withTitle: "File")?.submenu,
                       let close = fileMenu.items.first(where: {
                           $0.action == #selector(NSWindow.performClose(_:))
                       }) {
                        fileMenu.removeItem(close)
                    }
                }
                .onDisappear { model.saveSettings() }
                .onChange(of: phase) {
                    if phase == .background { model.saveSettings() }
                }
        }
        .defaultSize(width: 1152, height: 720)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About MandelSwift...") { sheet = .about }
            }
            CommandMenu("File") {
                Button("Load zone...") { model.loadZone() }
                    .keyboardShortcut("o", modifiers: .command)
                Button("Save zone...") { model.saveZone() }
                    .keyboardShortcut("s", modifiers: .command)
                Divider()
                Button("Save image as...") { model.savePNG() }
                    .keyboardShortcut("S", modifiers: [.command, .shift])
                Divider()
                Button("Benchmark...") { sheet = .benchmark }
                    .keyboardShortcut("b", modifiers: .command)
            }
            CommandMenu("View") {
                Button("Back") { goBack() }.keyboardShortcut("[", modifiers: .command)
                    .disabled(!model.canBack)
                Button("Forward") { goForward() }.keyboardShortcut("]", modifiers: .command)
                    .disabled(!model.canForward)
                Divider()
                Button("Julia mode") { toggleJulia() }
                    .keyboardShortcut("j", modifiers: .command)
                Divider()
                Button("Save favorite zone...") { model.saveFavorite() }
                    .keyboardShortcut("d", modifiers: .command)
                Menu("Favorite zones") { favoriteItems(delete: false) }
                Menu("Delete favorite") { favoriteItems(delete: true) }
            }
            CommandMenu("Generate") {
                Button("Screenshot...") { sheet = .export }
                    .keyboardShortcut("E", modifiers: [.command, .shift])
                Button("Zoom video...") { sheet = .video }
                    .keyboardShortcut("V", modifiers: [.command, .shift])
                Divider()
                Button("Back to set (RealTime)") { model.backToSet() }
                    .keyboardShortcut("R", modifiers: [.command, .shift])
            }
            CommandMenu("Help") {
                Button("Show log / diagnostics...") { sheet = .log }
            }
        }
    }

    private func goBack() {
        if let z = model.doBack() { model.apply(z, push: false) }
    }

    private func goForward() {
        if let z = model.doForward() { model.apply(z, push: false) }
    }

    private func toggleJulia() {
        model.pushHistory(model.zone())
        model.julia.toggle()
        model.requestRender(w: Int(model.viewW), h: Int(model.viewH), preview: false)
    }

    @ViewBuilder
    private func favoriteItems(delete: Bool) -> some View {
        let favs = Store.favorites()
        if favs.isEmpty {
            Text("(none)").disabled(true)
        } else {
            ForEach(favs, id: \.self) { url in
                Button(url.deletingPathExtension().lastPathComponent) {
                    if delete {
                        try? FileManager.default.removeItem(at: url)
                    } else if let z = try? Store.readZone(from: url),
                              z.validated() == nil {
                        model.pushHistory(model.zone())
                        model.apply(z, push: false)
                    }
                }
            }
        }
    }
}

struct ContentView: View {
    @ObservedObject var model: ViewerModel
    @Binding var sheet: MandelApp.Sheet?
    @Binding var favorites: [URL]

    var body: some View {
        VStack(spacing: 0) {
            toolbarRow0
            toolbarRow1
            GeometryReader { geo in
                FractalView(model: model)
                    .onAppear {
                        model.requestRender(w: Int(geo.size.width),
                                            h: Int(geo.size.height), preview: false)
                    }
                    .onChange(of: geo.size) {
                        model.requestRender(w: Int(geo.size.width),
                                            h: Int(geo.size.height), preview: false)
                    }
            }
            statusBar
        }
        .sheet(item: $sheet) { s in
            switch s {
            case .export: ExportSheet(model: model)
            case .video: VideoSheet(model: model)
            case .benchmark: BenchmarkSheet(model: model)
            case .log: LogSheet(model: model)
            case .about: AboutSheet()
            }
        }
    }

    // MARK: - Toolbar (§5.2, two rows)

    private var toolbarRow0: some View {
        HStack(spacing: 8) {
            Button("↩ Home") { model.backToSet() }
                .help("Back to set with RealTime animation (Ctrl+Shift+R)")
            Button("Reset") {
                model.reset()
                model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                    preview: false)
            }.help("Restore the initial set view (R key)")
            Button("Benchmark") { sheet = .benchmark }
                .help("Open the standard benchmark (Ctrl+B)")
            Text("Iterations:")
            TextField("", value: $model.maxIterManual, format: .number)
                .frame(width: 70)
                .help("Maximum iterations per pixel (+/- keys)")
                .onSubmit {
                    model.maxIterManual = Spec.clampIter(model.maxIterManual)
                    model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                        preview: false)
                }
            Toggle("Auto", isOn: $model.iterAuto)
                .help("Automatic iterations: grow with zoom level")
                .onChange(of: model.iterAuto) {
                    model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                        preview: false)
                }
            Text("Palette:")
            Picker("", selection: $model.palette) {
                ForEach(0 ..< Palette.names.count, id: \.self) { i in
                    Text(Palette.names[i]).tag(i)
                }
            }
            .frame(width: 110)
            .help("Fractal color palette (Fire, Ice, Thermal, Ocean, Violet, Desert, Forest)")
            .onChange(of: model.palette) {
                model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                    preview: false)
            }
            Text("AA:")
            Picker("", selection: $model.aaIndex) {
                Text("1x").tag(0); Text("2x").tag(1)
                Text("4x").tag(2); Text("8x").tag(3)
            }
            .frame(width: 60)
            .help("Antialiasing: 1x disabled, 2x/4x/8x average of nearby pixels")
            .onChange(of: model.aaIndex) {
                model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                    preview: false)
            }
            Spacer()
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
    }

    private var toolbarRow1: some View {
        HStack(spacing: 8) {
            Text("Engine:")
            Picker("", selection: $model.engineSel) {
                Text("CPU").tag(EngineSel.cpu)
                Text("GPU").tag(EngineSel.gpu)
            }
            .pickerStyle(.segmented)
            .frame(width: 130)
            .help("CPU multicore engine (always available) / GPU engine: real-time GPU rendering (uses the GPU available on this machine)")
            .onChange(of: model.engineSel) { model.selectEngine(model.engineSel) }
            if model.engineSel == .gpu {
                Text("GPU:")
                Picker("", selection: $model.gpuChoice) {
                    Text("Auto").tag("")
                    ForEach(MetalEngine.adapterNames(), id: \.self) { name in
                        Text(name).tag(name)
                    }
                }
                .frame(width: 200)
                .help("Video card to use (Auto = the most powerful)")
                .onChange(of: model.gpuChoice) { model.selectDevice(model.gpuChoice) }
            }
            Text("Precision:")
            Picker("", selection: $model.useDouble) {
                Text("32").tag(false)
                Text("64").tag(true)
            }
            .pickerStyle(.segmented)
            .frame(width: 90)
            .help("CPU/GPU precision 32-bit (float): faster, less precise. / 64-bit (double): more precise, slower. (GPU is float-only)")
            .onChange(of: model.useDouble) {
                if model.engineSel == .gpu { model.useDouble = false }
                model.requestRender(w: Int(model.viewW), h: Int(model.viewH),
                                    preview: false)
            }
            if model.julia {
                Text(String(format: "Julia c=%.4f %.4fi", model.jcx, model.jcy))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
    }

    private var statusBar: some View {
        HStack {
            Text(model.status).font(.caption).lineLimit(1).truncationMode(.tail)
            Spacer()
            Text("Click: zoom | Wheel: zoom | Drag/arrows: pan | R: reset | S: save")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 2)
    }
}
