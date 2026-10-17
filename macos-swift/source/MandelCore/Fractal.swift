import Foundation

/// CPU render request. scale = complex WIDTH of the whole image (§2.2).
public struct RenderParams {
    public var width: Int
    public var height: Int
    public var centerX: Double
    public var centerY: Double
    public var scale: Double
    public var maxIter: Int
    public var palette: Int
    public var aa: Int
    public var useDouble: Bool
    public var julia: Bool
    public var jcx: Double
    public var jcy: Double
    public var offsetX: Int
    public var offsetY: Int
    public var fullWidth: Int
    public var fullHeight: Int
    public var isCancelled: () -> Bool

    public init(width: Int, height: Int, centerX: Double, centerY: Double,
                scale: Double, maxIter: Int, palette: Int, aa: Int,
                useDouble: Bool, julia: Bool = false,
                jcx: Double = Spec.defaultJcx, jcy: Double = Spec.defaultJcy,
                offsetX: Int = 0, offsetY: Int = 0,
                fullWidth: Int = 0, fullHeight: Int = 0,
                isCancelled: @escaping () -> Bool = { false }) {
        self.width = width; self.height = height
        self.centerX = centerX; self.centerY = centerY; self.scale = scale
        self.maxIter = maxIter; self.palette = palette; self.aa = aa
        self.useDouble = useDouble; self.julia = julia
        self.jcx = jcx; self.jcy = jcy
        self.offsetX = offsetX; self.offsetY = offsetY
        self.fullWidth = fullWidth == 0 ? width : fullWidth
        self.fullHeight = fullHeight == 0 ? height : fullHeight
        self.isCancelled = isCancelled
    }
}

/// CPU engine: 4 code paths (scalar/vector × float/double, §3.2).
/// Vector paths process rows with genuine SIMD4 lanes and alive masks;
/// scratch arrays are allocated once per row (never per block), so the
/// double vector path is bit-identical to scalar double at full speed.
public enum Fractal {
    /// Always true on Apple Silicon; the scalar fallbacks stay selectable
    /// (mirrors the reference path selection).
    public static var simdAvailable = true

    /// SPEC §2.5 cardioid + period-2 bulb, always in double.
    public static func interior(px: Double, py: Double) -> Bool {
        let q = (px - 0.25) * (px - 0.25) + py * py
        if q * (q + (px - 0.25)) <= Spec.cardioidTest * py * py { return true }
        let dx = px + 1.0
        return dx * dx + py * py <= Spec.bulbTest
    }

    /// SPEC §2.1 escape loop, double. Returns (count, |z|² at escape).
    public static func escapeD(ccx: Double, ccy: Double, z0x: Double,
                              z0y: Double, maxIter: Int) -> (Int, Double) {
        var zx = z0x, zy = z0y, i = 0
        var zx2 = zx * zx, zy2 = zy * zy
        while i < maxIter && zx2 + zy2 <= 4.0 {
            let nzy = 2.0 * zx * zy + ccy
            let nzx = zx2 - zy2 + ccx
            zy = nzy; zx = nzx
            zx2 = zx * zx; zy2 = zy * zy
            i += 1
        }
        return (i, zx2 + zy2)
    }

    /// Same loop in Float. Cardioid stays double (caller-side).
    public static func escapeF(ccx: Float, ccy: Float, z0x: Float,
                              z0y: Float, maxIter: Int) -> (Int, Float) {
        var zx = z0x, zy = z0y, i = 0
        var zx2 = zx * zx, zy2 = zy * zy
        while i < maxIter && zx2 + zy2 <= 4.0 {
            let nzy = 2.0 * zx * zy + ccy
            let nzx = zx2 - zy2 + ccx
            zy = nzy; zx = nzx
            zx2 = zx * zx; zy2 = zy * zy
            i += 1
        }
        return (i, zx2 + zy2)
    }

    /// Whole-frame or tile render (§2.8, §3.1). RGBA bytes, nil if cancelled.
    /// Rows run on all cores (chunk = max(8, h/(procs*8))); each row uses the
    /// scalar or 4-lane vector core by precision and simdAvailable.
    public static func render(_ p: RenderParams) -> [UInt8]? {
        let k = p.aa
        let lut = Palette.lut(palette: p.palette, maxIter: p.maxIter)
        let invMax = 1.0 / Double(p.maxIter)
        let pixelSize = p.scale / Double(p.fullWidth * k)
        let originX = p.centerX - Double(p.fullWidth * k) * 0.5 * pixelSize
        let topY = p.centerY - Double(p.fullHeight * k) * 0.5 * pixelSize
        var out = [UInt8](repeating: 0, count: p.width * p.height * 4)
        let procs = max(1, ProcessInfo.processInfo.activeProcessorCount)
        let chunk = max(8, p.height / (procs * 8)) // §3.1
        let vector = simdAvailable
        let stop = Locked(false)
        out.withUnsafeMutableBufferPointer { buf in
            guard let base = buf.baseAddress else { return }
            DispatchQueue.concurrentPerform(
                iterations: (p.height + chunk - 1) / chunk) { c in
                if p.isCancelled() || stop.value { return }
                let y0 = c * chunk
                let y1 = min(y0 + chunk, p.height)
                for y in y0 ..< y1 {
                    if vector, p.useDouble {
                        renderRowVD(y: y, p: p, k: k, lut: lut, invMax: invMax,
                                    originX: originX, topY: topY,
                                    pixelSize: pixelSize, base: base)
                    } else if vector {
                        renderRowVF(y: y, p: p, k: k, lut: lut, invMax: invMax,
                                    originX: originX, topY: topY,
                                    pixelSize: pixelSize, base: base)
                    } else {
                        renderRowScalar(y: y, p: p, k: k, lut: lut,
                                        invMax: invMax, originX: originX,
                                        topY: topY, pixelSize: pixelSize,
                                        base: base)
                    }
                }
                if p.isCancelled() { stop.value = true }
            }
        }
        if p.isCancelled() || stop.value { return nil }
        return out
    }

    static func renderRowScalar(y: Int, p: RenderParams, k: Int, lut: [UInt32],
                                invMax: Double, originX: Double, topY: Double,
                                pixelSize: Double, base: UnsafeMutablePointer<UInt8>) {
        for x in 0 ..< p.width {
            var r = 0, g = 0, b = 0
            for sy in 0 ..< k {
                for sx in 0 ..< k {
                    let gbx = (p.offsetX + x) * k + sx
                    let gby = (p.offsetY + y) * k + sy
                    let px = originX + Double(gbx) * pixelSize
                    let py = topY + Double(gby) * pixelSize
                    let (cr, cg, cb): (UInt8, UInt8, UInt8)
                    if p.useDouble {
                        (cr, cg, cb) = colorD(px: px, py: py, p: p, lut: lut,
                                              invMax: invMax)
                    } else {
                        (cr, cg, cb) = colorF(px: px, py: py, p: p, lut: lut,
                                              invMax: invMax)
                    }
                    r += Int(cr); g += Int(cg); b += Int(cb)
                }
            }
            writePixel(base: base, width: p.width, x: x, y: y,
                       r: r, g: g, b: b, n: k * k)
        }
    }

    static func colorD(px: Double, py: Double, p: RenderParams,
                       lut: [UInt32], invMax: Double) -> (UInt8, UInt8, UInt8) {
        var ccx = px, ccy = py, z0x = 0.0, z0y = 0.0
        if p.julia {
            z0x = px; z0y = py; ccx = p.jcx; ccy = p.jcy
        } else if interior(px: px, py: py) {
            return (0, 0, 0)
        }
        let (iter, mod) = escapeD(ccx: ccx, ccy: ccy, z0x: z0x, z0y: z0y,
                                  maxIter: p.maxIter)
        if iter >= p.maxIter { return (0, 0, 0) }
        let smooth = Double(iter) + 1.0 - log2(0.5 * log(max(mod, 4.0)))
        return Palette.lookup(lut, smooth: smooth, invMaxIter: invMax)
    }

    static func colorF(px: Double, py: Double, p: RenderParams,
                       lut: [UInt32], invMax: Double) -> (UInt8, UInt8, UInt8) {
        if !p.julia && interior(px: px, py: py) { return (0, 0, 0) }
        let ccx: Float, ccy: Float, z0x: Float, z0y: Float
        if p.julia {
            z0x = Float(px); z0y = Float(py)
            ccx = Float(p.jcx); ccy = Float(p.jcy)
        } else {
            z0x = 0; z0y = 0; ccx = Float(px); ccy = Float(py)
        }
        let (iter, mod) = escapeF(ccx: ccx, ccy: ccy, z0x: z0x, z0y: z0y,
                                  maxIter: p.maxIter)
        if iter >= p.maxIter { return (0, 0, 0) }
        let smooth = Float(iter) + 1.0 - log2(0.5 * log(max(mod, 4.0)))
        return Palette.lookup(lut, smooth: Double(smooth), invMaxIter: invMax)
    }

    /// Vector double row: one scratch set per row, SIMD4 blocks with alive
    /// masks, interior lanes seeded done. Bit-identical to scalar double.
    static func renderRowVD(y: Int, p: RenderParams, k: Int, lut: [UInt32],
                            invMax: Double, originX: Double, topY: Double,
                            pixelSize: Double, base: UnsafeMutablePointer<UInt8>) {
        let w = p.width, w4 = (w + 3) & ~3
        var it = [Int](repeating: 0, count: w4)
        var md = [Double](repeating: 4.0, count: w4)
        var al = [Bool](repeating: false, count: w4)
        var zxr = [Double](repeating: 0, count: w4)
        var zyr = [Double](repeating: 0, count: w4)
        var ccx = [Double](repeating: 0, count: w4)
        var ccy = [Double](repeating: 0, count: w4)
        var rA = [Int](repeating: 0, count: w)
        var gA = [Int](repeating: 0, count: w)
        var bA = [Int](repeating: 0, count: w)
        let four = SIMD4<Double>(repeating: 4.0)
        for sy in 0 ..< k {
            let py = topY + Double((p.offsetY + y) * k + sy) * pixelSize
            for sx in 0 ..< k {
                var nAlive = 0
                for x in 0 ..< w {
                    let px = originX + Double((p.offsetX + x) * k + sx) * pixelSize
                    if !p.julia && interior(px: px, py: py) {
                        al[x] = false; it[x] = p.maxIter
                    } else {
                        zxr[x] = p.julia ? px : 0; zyr[x] = p.julia ? py : 0
                        ccx[x] = p.julia ? p.jcx : px; ccy[x] = p.julia ? p.jcy : py
                        al[x] = true; it[x] = p.maxIter; nAlive += 1
                    }
                }
                var iter = 0
                while iter < p.maxIter && nAlive > 0 {
                    var x = 0
                    while x < w4 {
                        if al[x] || al[x + 1] || al[x + 2] || al[x + 3] {
                            let zx = SIMD4(zxr[x], zxr[x + 1], zxr[x + 2], zxr[x + 3])
                            let zy = SIMD4(zyr[x], zyr[x + 1], zyr[x + 2], zyr[x + 3])
                            let cx = SIMD4(ccx[x], ccx[x + 1], ccx[x + 2], ccx[x + 3])
                            let cy = SIMD4(ccy[x], ccy[x + 1], ccy[x + 2], ccy[x + 3])
                            let zx2 = zx * zx, zy2 = zy * zy
                            let esc = (zx2 + zy2) .> four
                            for l in 0 ..< 4 where al[x + l] && esc[l] {
                                al[x + l] = false; nAlive -= 1
                                it[x + l] = iter; md[x + l] = (zx2 + zy2)[l]
                            }
                            let nzx = zx2 - zy2 + cx, nzy = 2.0 * zx * zy + cy
                            for l in 0 ..< 4 where al[x + l] {
                                zxr[x + l] = nzx[l]; zyr[x + l] = nzy[l]
                            }
                        }
                        x += 4
                    }
                    iter += 1
                }
                for x in 0 ..< w where it[x] < p.maxIter {
                    let smooth = Double(it[x]) + 1.0 - log2(0.5 * log(max(md[x], 4.0)))
                    let (cr, cg, cb) = Palette.lookup(lut, smooth: smooth, invMaxIter: invMax)
                    rA[x] += Int(cr); gA[x] += Int(cg); bA[x] += Int(cb)
                }
            }
        }
        let n = k * k
        for x in 0 ..< w {
            writePixel(base: base, width: w, x: x, y: y, r: rA[x], g: gA[x], b: bA[x], n: n)
        }
    }

    /// Vector float row: same shape in SIMD4<Float>, cardioid in double.
    static func renderRowVF(y: Int, p: RenderParams, k: Int, lut: [UInt32],
                            invMax: Double, originX: Double, topY: Double,
                            pixelSize: Double, base: UnsafeMutablePointer<UInt8>) {
        let w = p.width, w4 = (w + 3) & ~3
        var it = [Int](repeating: 0, count: w4)
        var md = [Float](repeating: 4.0, count: w4)
        var al = [Bool](repeating: false, count: w4)
        var zxr = [Float](repeating: 0, count: w4)
        var zyr = [Float](repeating: 0, count: w4)
        var ccx = [Float](repeating: 0, count: w4)
        var ccy = [Float](repeating: 0, count: w4)
        var rA = [Int](repeating: 0, count: w)
        var gA = [Int](repeating: 0, count: w)
        var bA = [Int](repeating: 0, count: w)
        let four = SIMD4<Float>(repeating: 4.0)
        for sy in 0 ..< k {
            let py = topY + Double((p.offsetY + y) * k + sy) * pixelSize
            for sx in 0 ..< k {
                var nAlive = 0
                for x in 0 ..< w {
                    let px = originX + Double((p.offsetX + x) * k + sx) * pixelSize
                    if !p.julia && interior(px: px, py: py) {
                        al[x] = false; it[x] = p.maxIter
                    } else {
                        zxr[x] = p.julia ? Float(px) : 0; zyr[x] = p.julia ? Float(py) : 0
                        ccx[x] = p.julia ? Float(p.jcx) : Float(px)
                        ccy[x] = p.julia ? Float(p.jcy) : Float(py)
                        al[x] = true; it[x] = p.maxIter; nAlive += 1
                    }
                }
                var iter = 0
                while iter < p.maxIter && nAlive > 0 {
                    var x = 0
                    while x < w4 {
                        if al[x] || al[x + 1] || al[x + 2] || al[x + 3] {
                            let zx = SIMD4(zxr[x], zxr[x + 1], zxr[x + 2], zxr[x + 3])
                            let zy = SIMD4(zyr[x], zyr[x + 1], zyr[x + 2], zyr[x + 3])
                            let cx = SIMD4(ccx[x], ccx[x + 1], ccx[x + 2], ccx[x + 3])
                            let cy = SIMD4(ccy[x], ccy[x + 1], ccy[x + 2], ccy[x + 3])
                            let zx2 = zx * zx, zy2 = zy * zy
                            let esc = (zx2 + zy2) .> four
                            for l in 0 ..< 4 where al[x + l] && esc[l] {
                                al[x + l] = false; nAlive -= 1
                                it[x + l] = iter; md[x + l] = (zx2 + zy2)[l]
                            }
                            let nzx = zx2 - zy2 + cx, nzy = 2.0 * zx * zy + cy
                            for l in 0 ..< 4 where al[x + l] {
                                zxr[x + l] = nzx[l]; zyr[x + l] = nzy[l]
                            }
                        }
                        x += 4
                    }
                    iter += 1
                }
                for x in 0 ..< w where it[x] < p.maxIter {
                    let smooth = Float(it[x]) + 1.0 - log2(0.5 * log(max(md[x], 4.0)))
                    let (cr, cg, cb) = Palette.lookup(lut, smooth: Double(smooth), invMaxIter: invMax)
                    rA[x] += Int(cr); gA[x] += Int(cg); bA[x] += Int(cb)
                }
            }
        }
        let n = k * k
        for x in 0 ..< w {
            writePixel(base: base, width: w, x: x, y: y, r: rA[x], g: gA[x], b: bA[x], n: n)
        }
    }

    static func writePixel(base: UnsafeMutablePointer<UInt8>, width: Int,
                           x: Int, y: Int, r: Int, g: Int, b: Int, n: Int) {
        let o = (y * width + x) * 4
        base[o] = UInt8(r / n)
        base[o + 1] = UInt8(g / n)
        base[o + 2] = UInt8(b / n)
        base[o + 3] = 255
    }

    /// Iterations-only frame for the CPU benchmark (§3.3, §6.1).
    /// Same §2.2 mapping at k=1, cardioid applied, no color.
    public static func benchFrame(centerX: Double, centerY: Double,
                                 scale: Double, maxIter: Int, useDouble: Bool,
                                 isCancelled: () -> Bool) -> Int? {
        let w = Benchmark.width, h = Benchmark.height
        let pixelSize = scale / Double(w)
        let originX = centerX - Double(w) * 0.5 * pixelSize
        let topY = centerY - Double(h) * 0.5 * pixelSize
        let procs = max(1, ProcessInfo.processInfo.activeProcessorCount)
        let chunk = max(8, h / (procs * 8))
        let total = Locked(0)
        let stop = Locked(false)
        DispatchQueue.concurrentPerform(
            iterations: (h + chunk - 1) / chunk) { c in
            if isCancelled() || stop.value { return }
            var sum = 0
            if useDouble {
                let w4 = (w + 3) & ~3
                var it = [Int](repeating: 0, count: w4)
                var al = [Bool](repeating: false, count: w4)
                var zxr = [Double](repeating: 0, count: w4)
                var zyr = [Double](repeating: 0, count: w4)
                var ccx = [Double](repeating: 0, count: w4)
                var ccy = [Double](repeating: 0, count: w4)
                for y in (c * chunk) ..< min((c + 1) * chunk, h) {
                    let py = topY + Double(y) * pixelSize
                    var nAlive = 0
                    for x in 0 ..< w {
                        let px = originX + Double(x) * pixelSize
                        if interior(px: px, py: py) {
                            al[x] = false; it[x] = maxIter
                        } else {
                            zxr[x] = 0; zyr[x] = 0
                            ccx[x] = px; ccy[x] = py
                            al[x] = true; it[x] = maxIter; nAlive += 1
                        }
                    }
                    var iter = 0
                    while iter < maxIter && nAlive > 0 {
                        var x = 0
                        while x < w4 {
                            if al[x] || al[x + 1] || al[x + 2] || al[x + 3] {
                                let zx = SIMD4(zxr[x], zxr[x + 1], zxr[x + 2], zxr[x + 3])
                                let zy = SIMD4(zyr[x], zyr[x + 1], zyr[x + 2], zyr[x + 3])
                                let cx = SIMD4(ccx[x], ccx[x + 1], ccx[x + 2], ccx[x + 3])
                                let cy = SIMD4(ccy[x], ccy[x + 1], ccy[x + 2], ccy[x + 3])
                                let zx2 = zx * zx, zy2 = zy * zy
                                let esc = (zx2 + zy2) .> SIMD4<Double>(repeating: 4.0)
                                for l in 0 ..< 4 where al[x + l] && esc[l] {
                                    al[x + l] = false; nAlive -= 1; it[x + l] = iter
                                }
                                let nzx = zx2 - zy2 + cx, nzy = 2.0 * zx * zy + cy
                                for l in 0 ..< 4 where al[x + l] {
                                    zxr[x + l] = nzx[l]; zyr[x + l] = nzy[l]
                                }
                            }
                            x += 4
                        }
                        iter += 1
                    }
                    sum += it[0 ..< w].reduce(0, +)
                    if isCancelled() { stop.value = true; return }
                }
            } else {
                for y in (c * chunk) ..< min((c + 1) * chunk, h) {
                    let py = topY + Double(y) * pixelSize
                    for x in 0 ..< w {
                        let px = originX + Double(x) * pixelSize
                        if interior(px: px, py: py) {
                            sum += maxIter
                            continue
                        }
                        sum += escapeF(ccx: Float(px), ccy: Float(py), z0x: 0,
                                       z0y: 0, maxIter: maxIter).0
                    }
                    if isCancelled() { stop.value = true; return }
                }
            }
            total.value += sum
            if isCancelled() { stop.value = true }
        }
        if isCancelled() || stop.value { return nil }
        return total.value
    }
}

/// Thread-safe box for render/bench shared state.
public final class Locked<T>: @unchecked Sendable {
    private var v: T
    private let lock = NSLock()
    public init(_ v: T) { self.v = v }
    public var value: T {
        get { lock.lock(); defer { lock.unlock() }; return v }
        set { lock.lock(); defer { lock.unlock() }; v = newValue }
    }
}
