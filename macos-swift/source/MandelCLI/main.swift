import Foundation
import MandelCore

/// Headless CLI (§6.6): --bench-cpu, --diag-gpu, --bench-gpu, plus
/// --shot for doc screenshots.
func usage() -> Never {
    fputs("usage: MandelCLI --bench-cpu [float] [--csv file] | --diag-gpu [device] | --bench-gpu [device] [--csv file] | --shot out W H cx cy scale maxIter palette aa [julia [jcx jcy]]\n", stderr)
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
                                     device: Diagnostics.cpuName() + Diagnostics.hostMacTag(), precision: precision,
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
                                     device: eng.deviceShortName + Diagnostics.hostMacTag(), precision: "32-bit",
                                     run: run, frames: r.frames, seconds: r.seconds, mpixelS: mpix)
        }
    }
    print(String(format: "best: %.1f Mpixel/s", best))
case "--shot":
    // Headless PNG render for docs/screenshots.
    let a = Array(args)
    guard a.count >= 8, let w = Int(a[1]), let h = Int(a[2]),
          let cx = Double(a[3]), let cy = Double(a[4]), let scale = Double(a[5]),
          let mi = Int(a[6]), let pal = Int(a[7]), let aa = Int(a.count > 8 ? a[8] : "1"),
          (0 ..< 7).contains(pal), [1, 2, 4, 8].contains(aa)
    else {
        fputs("usage: MandelCLI --shot out.png W H cx cy scale maxIter palette aa [julia [jcx jcy]]\n", stderr)
        exit(2)
    }
    let julia = a.count > 9 && a[9] == "julia"
    let p = RenderParams(width: w, height: h, centerX: cx, centerY: cy,
                         scale: scale, maxIter: Spec.clampIter(mi), palette: pal,
                         aa: aa, useDouble: true, julia: julia,
                         jcx: a.count > 10 ? Double(a[10]) ?? Spec.defaultJcx : Spec.defaultJcx,
                         jcy: a.count > 11 ? Double(a[11]) ?? Spec.defaultJcy : Spec.defaultJcy)
    guard let px = Fractal.render(p),
          Png.write(rgba: px, width: w, height: h, to: URL(fileURLWithPath: a[0]))
    else { exit(1) }
    print("saved \(a[0])")
default:
    usage()
}
