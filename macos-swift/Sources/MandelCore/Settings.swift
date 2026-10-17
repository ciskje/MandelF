import Foundation

/// SPEC §7.1 settings. Field names match the JSON schema exactly.
public struct AppSettings: Codable {
    public var iterAuto = false
    public var maxIter = Spec.defaultIter
    public var palette = 0
    public var aaIndex = 0
    public var engine = "Cpu"
    public var gpu = ""
    public var single = false
    public var julia = false
    public var jcx = Spec.defaultJcx
    public var jcy = Spec.defaultJcy
    public var winX = 0, winY = 0, winW = 0, winH = 0
    public var maximized = false

    enum CodingKeys: String, CodingKey {
        case iterAuto = "IterAuto", maxIter = "MaxIter", palette = "Palette"
        case aaIndex = "AaIndex", engine = "Engine", gpu = "Gpu"
        case single = "Single", julia = "Julia", jcx = "Jcx", jcy = "Jcy"
        case winX = "WinX", winY = "WinY", winW = "WinW", winH = "WinH"
        case maximized = "Maximized"
    }

    public init() {}
}

/// SPEC §7: ~/Library/Application Support/MandelbrotViewer + zone/ subfolder.
public enum Store {
    public static var dir: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/MandelbrotViewer",
                                    isDirectory: true)
    }

    public static var zoneDir: URL { dir.appendingPathComponent("zone", isDirectory: true) }
    static var settingsFile: URL { dir.appendingPathComponent("settings.json") }

    /// Missing or corrupt → defaults, no error. Values clamped (§7.1).
    public static func load() -> AppSettings {
        guard let data = try? Data(contentsOf: settingsFile),
              var s = try? JSONDecoder().decode(AppSettings.self, from: data)
        else { return AppSettings() }
        s.maxIter = Spec.clampIter(s.maxIter)
        s.palette = min(6, max(0, s.palette))
        s.aaIndex = min(3, max(0, s.aaIndex))
        if !s.jcx.isFinite || !s.jcy.isFinite {
            s.jcx = Spec.defaultJcx; s.jcy = Spec.defaultJcy
        }
        if s.engine != "Cpu" { s.engine = "Cpu" } // no GPU engine in this build
        return s
    }

    /// Best effort, never throws (§7.1).
    public static func save(_ s: AppSettings) {
        var s = s
        s.maxIter = Spec.clampIter(s.maxIter)
        do {
            try FileManager.default.createDirectory(at: dir,
                                                    withIntermediateDirectories: true)
            let enc = JSONEncoder()
            enc.outputFormatting = [.prettyPrinted, .sortedKeys]
            try enc.encode(s).write(to: settingsFile, options: .atomic)
        } catch { /* never block closing */ }
    }

    public static func writeZone(_ z: ViewZone, to url: URL) throws {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        try enc.encode(z).write(to: url, options: .atomic)
    }

    public static func readZone(from url: URL) throws -> ViewZone {
        try JSONDecoder().decode(ViewZone.self, from: Data(contentsOf: url))
    }

    /// *.json favorites ordered case-insensitively (§5.9).
    public static func favorites() -> [URL] {
        (try? FileManager.default.contentsOfDirectory(at: zoneDir,
                                                       includingPropertiesForKeys: nil)) ?? []
            .filter { $0.pathExtension.lowercased() == "json" }
            .sorted { $0.lastPathComponent.lowercased() < $1.lastPathComponent.lowercased() }
    }
}
