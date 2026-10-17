import Foundation

/// SPEC §2.4 palettes, LUT builder (4096) and lookup. Single source of truth.
public enum Palette: Int, CaseIterable {
    case fire = 0, ice, thermal, ocean, violet, desert, forest

    public static let names = ["Fire", "Ice", "Thermal", "Ocean",
                               "Violet", "Desert", "Forest"]

    // 7 palettes × 5 stops at t = 0, .25, .5, .75, 1.
    public static let stops: [[(r: Int, g: Int, b: Int)]] = [
        [(0,0,0), (170,20,0), (255,120,0), (255,220,90), (255,255,225)],
        [(0,0,0), (0,35,110), (0,130,220), (130,215,255), (240,255,255)],
        [(0,0,0), (75,10,105), (195,25,85), (250,135,30), (252,250,180)],
        [(0,0,0), (0,40,95), (0,120,175), (85,200,225), (235,250,255)],
        [(60,0,80), (210,0,190), (120,40,220), (0,200,230), (210,255,255)],
        [(0,0,0), (95,50,10), (185,110,40), (235,190,110), (255,245,220)],
        [(55,32,12), (115,78,32), (95,125,45), (60,170,70), (205,235,175)],
    ]

    public var name: String { Self.names[rawValue] }

    private static var cache: [String: [UInt32]] = [:]
    private static let lock = NSLock()

    /// Packed 32-bit ARGB LUT, cached per (palette, maxIter). Do not mutate.
    public static func lut(palette: Int, maxIter: Int) -> [UInt32] {
        let key = "\(palette):\(maxIter)"
        lock.lock()
        if let hit = cache[key] { lock.unlock(); return hit }
        lock.unlock()
        let table = build(palette: palette)
        lock.lock()
        cache[key] = table
        lock.unlock()
        return table
    }

    // (maxIter is part of the cache key only; entries are resolution-free.)
    private static func build(palette: Int) -> [UInt32] {
        let stops = stops[palette]
        var out = [UInt32](repeating: 0, count: Spec.lutSize)
        for i in 0 ..< Spec.lutSize {
            let raw = (Double(i) + 0.5) / Double(Spec.lutSize)
            let t = pow(raw, Spec.gamma)
            let segment = t * 4.0
            let seg = min(3, Int(segment))
            let f = segment - Double(seg)
            let a = stops[seg], b = stops[seg + 1]
            let r = Int(Double(a.r) + f * Double(b.r - a.r))
            let g = Int(Double(a.g) + f * Double(b.g - a.g))
            let bl = Int(Double(a.b) + f * Double(b.b - a.b))
            out[i] = 0xFF00_0000 | UInt32(r) << 16 | UInt32(g) << 8 | UInt32(bl)
        }
        return out
    }

    /// SPEC §2.4 lookup. Channels truncated.
    public static func lookup(_ lut: [UInt32], smooth: Double,
                             invMaxIter: Double) -> (UInt8, UInt8, UInt8) {
        let pos = smooth * invMaxIter * Double(Spec.lutSize) - 0.5
        if pos <= 0 { return split(lut[0]) }
        if pos >= Double(Spec.lutSize - 1) { return split(lut[Spec.lutSize - 1]) }
        let i0 = Int(pos)
        let f = pos - Double(i0)
        let (r0, g0, b0) = split(lut[i0])
        let (r1, g1, b1) = split(lut[i0 + 1])
        return (UInt8(Double(r0) + f * Double(Int(r1) - Int(r0))),
                UInt8(Double(g0) + f * Double(Int(g1) - Int(g0))),
                UInt8(Double(b0) + f * Double(Int(b1) - Int(b0))))
    }

    static func split(_ argb: UInt32) -> (UInt8, UInt8, UInt8) {
        (UInt8((argb >> 16) & 0xFF), UInt8((argb >> 8) & 0xFF), UInt8(argb & 0xFF))
    }
}
