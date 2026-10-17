import Foundation
import MandelCore

/// Headless CLI (§6.6, CPU subset): --bench-cpu [float] [--csv f].
/// --diag-gpu / --bench-gpu report the missing GPU engine without crashing.
func usage() -> Never {
    fputs("usage: MandelCLI --bench-cpu [float] [--csv file] | --diag-gpu [device] | --bench-gpu [device] [--csv file]\n", stderr)
    exit(2)
}

var args = CommandLine.arguments.dropFirst()
guard let flag = args.first, flag.hasPrefix("--") else { usage() }
args = args.dropFirst()
var csv: String?
var positional: String?
var it = args.makeIterator()
while let a = it.next() {
    if a == "--csv", let f = it.next() { csv = f } else if !a.hasPrefix("--") { positional = a }
}

switch flag {
case "--bench-cpu":
    let useDouble = positional != "float"
    let precision = useDouble ? "64-bit" : "float"
    var best = 0.0
    for run in 1 ... 3 {
        guard let r = Benchmark.run(useDouble: useDouble, tick: { el, _ in
            fputs(String(format: "run %d: %.1fs...\n", run, el), stderr)
            return true
        }) else { exit(1) }
        let mpix = r.mpixelS
        best = max(best, mpix)
        print(String(format: "run %d: %.1f Mpixel/s (%d frames in %.2fs)", run, mpix, r.frames, r.seconds))
        if let csv {
            try? Benchmark.csvAppend(to: URL(fileURLWithPath: csv), engine: "CPU",
                                     device: Diagnostics.cpuName(), precision: precision,
                                     run: run, frames: r.frames, seconds: r.seconds, mpixelS: mpix)
        }
    }
    print(String(format: "best: %.1f Mpixel/s", best))
case "--diag-gpu", "--bench-gpu":
    let names = MetalEngine.adapterNames()
    print("devices: \(names.isEmpty ? "(none)" : names.joined(separator: ", "))")
    let want: String? = (positional?.isEmpty ?? true) ? nil : positional
    let eng = MetalEngine()
    guard eng.tryInitialize(deviceName: want) else {
        print("IsReady: false  LastError: \(eng.lastError)")
        exit(1)
    }
    print("IsReady: true  device: \(eng.deviceName)")
    print("details: \(eng.details)")
    if flag == "--diag-gpu" {
        // Headless init test: 800x600 render, 200 iterations (§6.6).
        let p = RenderParams(width: 800, height: 600, centerX: -0.5, centerY: 0,
                             scale: Spec.fullScale, maxIter: 200,
                             palette: 0, aa: 1, useDouble: false)
        print("test render: \(eng.render(p) != nil ? "ok" : "FAILED: \(eng.lastError)")")
        exit(eng.render(p) != nil ? 0 : 1)
    }
    var best = 0.0
    for run in 1 ... 3 {
        guard let r = eng.bench(maxIter: Benchmark.maxIter, tick: { el, _ in
            fputs(String(format: "run %d: %.1fs...\n", run, el), stderr)
            return true
        }, isCancelled: { false }) else { exit(1) }
        let mpix = Double(r.frames * Benchmark.samples) / r.seconds / 1e6
        best = max(best, mpix)
        print(String(format: "run %d: %.1f Mpixel/s (%d frames in %.2fs)", run, mpix, r.frames, r.seconds))
        if let csv {
            try? Benchmark.csvAppend(to: URL(fileURLWithPath: csv), engine: "GPU",
                                     device: eng.deviceShortName, precision: "32-bit",
                                     run: run, frames: r.frames, seconds: r.seconds, mpixelS: mpix)
        }
    }
    print(String(format: "best: %.1f Mpixel/s", best))
default:
    usage()
}
