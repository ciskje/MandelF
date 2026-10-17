import Foundation

/// SPEC §6.1 standard benchmark. CPU double (default) and float workloads.
public enum Benchmark {
    public static let width = 960
    public static let height = 540
    public static let samples = 518_400
    public static let centerX = -0.7499302568795561
    public static let centerY = -0.015139113925433963
    public static let scale = 1.0453474311811176e-04
    public static let maxIter = 10915 // AutoIterForScale(scale)
    public static let budget = 8.0

    public struct Result {
        public var frames: Int
        public var seconds: Double
        public var mpixelS: Double { Double(frames * samples) / seconds / 1e6 }
    }

    /// Frames until the 8 s budget elapses. tick(elapsed, frames) → false
    /// cancels. Progress ticks at most ~every 1 s (caller-side throttle).
    public static func run(useDouble: Bool,
                           tick: (Double, Int) -> Bool) -> Result? {
        let t0 = Date()
        var frames = 0
        var lastTick = t0
        while true {
            guard let _ = Fractal.benchFrame(centerX: centerX, centerY: centerY,
                                             scale: scale, maxIter: maxIter,
                                             useDouble: useDouble,
                                             isCancelled: { false }) else { return nil }
            frames += 1
            let el = Date().timeIntervalSince(t0)
            if el - lastTick.timeIntervalSince(t0) >= 1.0 || el >= budget {
                lastTick = Date()
                if !tick(el, frames) { return nil }
            }
            if el >= budget { break }
        }
        let seconds = Date().timeIntervalSince(t0)
        return Result(frames: frames, seconds: seconds)
    }

    /// Rate units: Gpixel/s ≥ 1e9, Mpixel/s ≥ 1e6, kpixel/s ≥ 1e3 (§6.1).
    public static func formatRate(_ samplesPerSec: Double) -> String {
        if samplesPerSec >= 1e9 { return String(format: "%.2f Gpixel/s", samplesPerSec / 1e9) }
        if samplesPerSec >= 1e6 { return String(format: "%.2f Mpixel/s", samplesPerSec / 1e6) }
        if samplesPerSec >= 1e3 { return String(format: "%.1f kpixel/s", samplesPerSec / 1e3) }
        return String(format: "%.0f pixel/s", samplesPerSec)
    }

    /// SPEC §6.1 CSV row. Header on first write, invariant culture.
    public static func csvAppend(to url: URL, engine: String, device: String,
                                 precision: String, run: Int,
                                 frames: Int, seconds: Double,
                                 mpixelS: Double) throws {
        let iso = ISO8601DateFormatter()
        iso.timeZone = TimeZone(secondsFromGMT: 0)
        iso.formatOptions = [.withInternetDateTime]
        func q(_ s: String) -> String {
            (s.contains(",") || s.contains("\"") || s.contains("\n"))
                ? "\"\(s.replacingOccurrences(of: "\"", with: "\"\""))\"" : s
        }
        let row = "\(q(iso.string(from: Date()))),\(q(engine)),\(q(device))," +
            "\(q(precision)),\(run),\(frames)," +
            "\(String(format: "%.2f", seconds)),\(String(format: "%.1f", mpixelS))\n"
        let fm = FileManager.default
        if !fm.fileExists(atPath: url.path) {
            try "timestamp,engine,device,precision,run,frames,seconds,mpixel_s\n"
                .write(to: url, atomically: true, encoding: .utf8)
        }
        let h = try FileHandle(forWritingTo: url)
        defer { try? h.close() }
        try h.seekToEnd()
        h.write(Data(row.utf8))
    }
}
