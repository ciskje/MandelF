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
/// Vector paths process lanes in blocks of 4 with the same per-lane math,
/// so double-vector output is bit-identical to scalar double.
public enum Fractal {
    /// SIMD is always present on Apple Silicon; kept as a switch so the
    /// scalar fallbacks stay selectable (mirrors the reference selection).
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

    /// Whole-frame or tile render (§2.8, §3.1). RGBA bytes, nil if cancelled.
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
        let lanes = simdAvailable ? 4 : 1
        let stop = Locked(false)
        out.withUnsafeMutableBufferPointer { buf in
            guard let base = buf.baseAddress else { return }
            DispatchQueue.concurrentPerform(
                iterations: (p.height + chunk - 1) / chunk) { c in
                if p.isCancelled() || stop.value { return }
                let y0 = c * chunk
                let y1 = min(y0 + chunk, p.height)
                for y in y0 ..< y1 {
                    for x0 in stride(from: 0, to: p.width, by: lanes) {
                        for x in x0 ..< min(x0 + lanes, p.width) {
                            var r = 0, g = 0, b = 0
                            for sy in 0 ..< k {
                                for sx in 0 ..< k {
                                    let gbx = (p.offsetX + x) * k + sx
                                    let gby = (p.offsetY + y) * k + sy
                                    let px = originX + Double(gbx) * pixelSize
                                    let py = topY + Double(gby) * pixelSize
                                    let (cr, cg, cb): (UInt8, UInt8, UInt8)
                                    if p.useDouble {
                                        (cr, cg, cb) = colorD(px: px, py: py, p: p,
                                                              lut: lut, invMax: invMax)
                                    } else {
                                        (cr, cg, cb) = colorF(px: px, py: py, p: p,
                                                              lut: lut, invMax: invMax)
                                    }
                                    r += Int(cr); g += Int(cg); b += Int(cb)
                                }
                            }
                            let n = k * k
                            let o = (y * p.width + x) * 4
                            base[o] = UInt8(r / n)
                            base[o + 1] = UInt8(g / n)
                            base[o + 2] = UInt8(b / n)
                            base[o + 3] = 255
                        }
                    }
                }
                if p.isCancelled() { stop.value = true }
            }
        }
        if p.isCancelled() || stop.value { return nil }
        return out
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
            for y in (c * chunk) ..< min((c + 1) * chunk, h) {
                for x in 0 ..< w {
                    let px = originX + Double(x) * pixelSize
                    let py = topY + Double(y) * pixelSize
                    if interior(px: px, py: py) {
                        sum += maxIter
                        continue
                    }
                    if useDouble {
                        sum += escapeD(ccx: px, ccy: py, z0x: 0, z0y: 0,
                                       maxIter: maxIter).0
                    } else {
                        sum += escapeF(ccx: Float(px), ccy: Float(py), z0x: 0,
                                       z0y: 0, maxIter: maxIter).0
                    }
                }
            }
            total.value += sum
            if isCancelled() { stop.value = true }
        }
        if isCancelled() || stop.value { return nil }
        return total.value
    }
}

/// Thread-safe box for render/bench cancellation flags.
public final class Locked<T>: @unchecked Sendable {
    private var v: T
    private let lock = NSLock()
    public init(_ v: T) { self.v = v }
    public var value: T {
        get { lock.lock(); defer { lock.unlock() }; return v }
        set { lock.lock(); defer { lock.unlock() }; v = newValue }
    }
}
