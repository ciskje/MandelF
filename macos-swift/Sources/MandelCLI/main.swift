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
    // No GPU engine in this build (Metal pending): report, don't crash (§4.9).
    print("GPU engine: not available in this build (Metal pending)")
    print("devices: (none)")
    print("CPU: \(Diagnostics.cpuName()) — use --bench-cpu")
default:
    usage()
}
