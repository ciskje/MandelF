import Foundation

/// SPEC §6.3 zoom video: frame table + ffmpeg encode (or kept PNG sequence).
public enum Video {
    public struct Frame {
        public var scale: Double
        public var centerX: Double
        public var centerY: Double
        public var maxIter: Int
    }

    /// Transition from the current view to the full set (§6.3).
    public static func frames(fromScale: Double, fromX: Double, fromY: Double,
                              count n: Int) -> [Frame] {
        (0 ..< n).map { i in
            let t = Double(i) / Double(n - 1)
            let te = 1.0 - pow(1.0 - t, 3.0)
            let scale = fromScale * pow(Spec.fullScale / fromScale, te)
            // center follows the scale linearly toward (-0.5, 0)
            let f = (fromScale - scale) / (fromScale - Spec.fullScale)
            return Frame(scale: scale,
                         centerX: fromX + (Spec.homeX - fromX) * f,
                         centerY: fromY + (Spec.homeY - fromY) * f,
                         maxIter: Spec.autoIter(scale: scale))
        }
    }

    /// An `ffmpeg` executable found on the system (§6.3).
    public static func findFFmpeg() -> String? {
        for dir in ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"] {
            let p = dir + "/ffmpeg"
            if FileManager.default.isExecutableFile(atPath: p) { return p }
        }
        return nil
    }

    /// Encodes f%04d.png → MP4 (SPEC args). Returns nil on success, else the
    /// last-10-lines stderr error. Terminating via `cancel` kills the tree.
    public static func encode(framesDir: URL, fps: Int, count n: Int,
                              out: URL, cancel: Locked<Bool>) -> String? {
        guard let ffmpeg = findFFmpeg() else { return "ffmpeg not found" }
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: ffmpeg)
        proc.currentDirectoryURL = framesDir
        proc.arguments = ["-y", "-framerate", "\(fps)", "-i", "f%04d.png",
                          "-frames:v", "\(n)",
                          "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                          "-c:v", "libx264", "-pix_fmt", "yuv420p",
                          "-crf", "18", out.path]
        let err = Pipe()
        proc.standardError = err
        proc.standardOutput = FileHandle.nullDevice
        // Drain asynchronously: reading after wait deadlocks (§6.3).
        var errData = Data()
        err.fileHandleForReading.readabilityHandler = { h in
            errData.append(h.availableData)
        }
        do { try proc.run() } catch { return "\(error)" }
        while proc.isRunning {
            if cancel.value { proc.terminate(); break }
            Thread.sleep(forTimeInterval: 0.1)
        }
        proc.waitUntilExit()
        err.fileHandleForReading.readabilityHandler = nil
        guard proc.terminationStatus == 0, !cancel.value else {
            let tail = (String(data: errData, encoding: .utf8) ?? "")
                .split(separator: "\n").suffix(10).joined(separator: "\n")
            return cancel.value ? "cancelled" : tail
        }
        return nil
    }
}
