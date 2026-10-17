import Foundation
import MandelCore

// ponytail: one runnable check (no Xcode here, so no XCTest).
// SPEC §8 math/behavior spot values.
func check(_ cond: Bool, _ msg: String) {
    guard cond else { print("FAIL: \(msg)"); exit(1) }
    print("ok: \(msg)")
}

check(Fractal.escapeD(ccx: 0, ccy: 0, z0x: 0, z0y: 0, maxIter: 100).0 == 100,
      "origin inside")
check(Fractal.escapeD(ccx: -1, ccy: 0, z0x: 0, z0y: 0, maxIter: 100).0 == 100,
      "bulb inside")
check(Fractal.escapeD(ccx: 2, ccy: 0, z0x: 0, z0y: 0, maxIter: 100).0 == 2,
      "far point escapes")
check(Fractal.render(RenderParams(width: 4, height: 3, centerX: -0.5,
                                  centerY: 0, scale: 2.8, maxIter: 10,
                                  palette: 0, aa: 1,
                                  useDouble: true))?.count == 48, "render size")

// §2.3 (formula as written: 1011, not 1944)
check(Spec.autoIter(scale: 9.36) == 1011, "autoIter(9.36)=1011")
check(Spec.autoIter(scale: 1.0453474311811176e-04) == 10915, "autoIter(bench)=10915")
check(Spec.autoIter(scale: 1e30) == 50, "autoIter clamps low")
check(Spec.autoIter(scale: 1e-30) == 50000, "autoIter clamps high")

// §2.4 palettes + LUT
check(Palette.stops[0][2] == (255, 120, 0), "Fire t=0.5")
check(Palette.stops[1][3] == (130, 215, 255), "Ice t=0.75")
check(Palette.stops[6][0] == (55, 32, 12), "Forest t=0")
let lut = Palette.lut(palette: 0, maxIter: 10915)
check(lut.count == 4096, "LUT size")
check((lut[0] >> 24) == 0xFF, "LUT opaque")

// §2.5 early-out (Mandelbrot only)
check(Fractal.interior(px: -1, py: 0), "bulb c=-1 interior")
check(Fractal.interior(px: 0, py: 0), "cardioid c=0 interior")
check(!Fractal.interior(px: 2, py: 0), "c=2 exterior")

// §5.8/§7.2 zone round-trip + validation
let z = ViewZone(centerX: -0.5, centerY: 0, scale: 9.36, maxIter: 256,
                 julia: true, jcx: -0.4, jcy: 0.6)
let tmp = FileManager.default.temporaryDirectory
    .appendingPathComponent("selfcheck-\(UUID().uuidString).json")
try! Store.writeZone(z, to: tmp)
let back = try! Store.readZone(from: tmp)
check(back == z, "zone round-trip")
try? FileManager.default.removeItem(at: tmp)
let bad = ViewZone(centerX: 0, centerY: 0, scale: 0, maxIter: 256)
check(bad.validated() != nil, "zone scale>0 rejected")

// §6.1 CSV format
let csv = tmp.appendingPathExtension("csv")
try! Benchmark.csvAppend(to: csv, engine: "CPU", device: "Test, X",
                         precision: "64-bit", run: 1, frames: 4,
                         seconds: 8.0, mpixelS: 0.2)
let csvText = try! String(contentsOf: csv)
check(csvText.hasPrefix("timestamp,engine,device,precision,run,frames,seconds,mpixel_s\n"),
      "CSV header")
check(csvText.contains("\"Test, X\""), "CSV quoting")
try? FileManager.default.removeItem(at: csv)

// §1.5 tiling invariance: tile == whole render
let whole = Fractal.render(RenderParams(width: 64, height: 48, centerX: -0.5,
                                        centerY: 0, scale: 9.36, maxIter: 300,
                                        palette: 0, aa: 2, useDouble: true))!
let tile = Fractal.render(RenderParams(width: 64, height: 48, centerX: -0.5,
                                       centerY: 0, scale: 9.36, maxIter: 300,
                                       palette: 0, aa: 2, useDouble: true,
                                       offsetX: 0, offsetY: 0,
                                       fullWidth: 64, fullHeight: 48))!
check(whole == tile, "tile pixel-identical")

// §3.2 double vector == scalar (lanes on vs off)
Fractal.simdAvailable = true
let vec = Fractal.render(RenderParams(width: 48, height: 32, centerX: -0.75,
                                      centerY: 0.02, scale: 0.01, maxIter: 2000,
                                      palette: 2, aa: 1, useDouble: true))!
Fractal.simdAvailable = false
let sca = Fractal.render(RenderParams(width: 48, height: 32, centerX: -0.75,
                                      centerY: 0.02, scale: 0.01, maxIter: 2000,
                                      palette: 2, aa: 1, useDouble: true))!
Fractal.simdAvailable = true
check(vec == sca, "double vector bit-identical to scalar")

// §4: 88-byte view params, Metal adapters, GPU smoke render.
check(MemoryLayout<GpuViewParams>.size == 88, "GpuViewParams 88 bytes")
let adapters = MetalEngine.adapterNames()
check(!adapters.isEmpty, "Metal adapters present")
let mac = Diagnostics.hostMac()
check(mac.isEmpty || mac.range(of: #"^[0-9a-f]{2}(:[0-9a-f]{2}){5}$"#, options: .regularExpression) != nil,
      "hostMAC format: \(mac.isEmpty ? "(none)" : mac)")
let eng = MetalEngine()
check(eng.tryInitialize(deviceName: nil), "Metal init: \(eng.lastError)")
let gp = RenderParams(width: 64, height: 48, centerX: -0.5, centerY: 0,
                      scale: 9.36, maxIter: 300, palette: 0, aa: 1,
                      useDouble: false)
let cpuF = Fractal.render(gp)!
let gpu = eng.render(gp)
check(gpu != nil && gpu!.count == cpuF.count, "GPU render ok")
if let gpu {
    var diff = 0
    for i in stride(from: 0, to: gpu.count, by: 4) {
        if abs(Int(gpu[i]) - Int(cpuF[i])) > 1
            || abs(Int(gpu[i + 1]) - Int(cpuF[i + 1])) > 1
            || abs(Int(gpu[i + 2]) - Int(cpuF[i + 2])) > 1 { diff += 1 }
    }
    let frac = Double(diff) / Double(64 * 48)
    check(frac < 0.02, "GPU near-identical to CPU float (\(diff) px)")
}

print("SelfCheck passed")
