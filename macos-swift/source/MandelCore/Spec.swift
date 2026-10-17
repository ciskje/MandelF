import Foundation

/// SPEC §1–§2 shared constants. Fidelity spot values live here.
public enum Spec {
    public static let fullScale = 9.36
    public static let homeX = -0.5
    public static let homeY = 0.0
    public static let defaultJcx = -0.7
    public static let defaultJcy = 0.27015
    public static let gamma = 0.35
    public static let lutSize = 4096
    public static let minIter = 50
    public static let maxIterLimit = 50000
    public static let defaultIter = 256
    public static let iterStep = 50
    public static let bulbTest: Double = 0.0625
    public static let cardioidTest: Double = 0.25
    public static let wantsDoubleScale = 1e-3
    public static let dragThreshold = 5.0
    public static let previewThrottleMs = 80
    public static let historyLimit = 200
    public static let logLines = 200
    public static let tile = 512
    public static let exportMin = 320
    public static let exportMax = 16384
    public static let videoFrames = [60, 120, 240, 480]
    public static let videoFps = [24, 30, 60]
    public static let backFrames = 120
    public static let backFps = 30

    /// SPEC §2.3. Known: evaluates to 1011 at 9.36 (prose says ≈1944),
    /// 10915 at the benchmark scale. Formula kept exactly as written.
    public static func autoIter(scale: Double) -> Int {
        let half = max(Double.leastNormalMagnitude, scale * 0.5)
        let v = 2000.0 * (1.0 + log10(1.5 / half))
        return min(maxIterLimit, max(minIter, Int(v)))
    }

    public static func clampIter(_ v: Int) -> Int {
        min(maxIterLimit, max(minIter, v))
    }
}
