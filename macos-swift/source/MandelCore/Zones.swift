import Foundation

/// SPEC §5.8 zone record. Julia fields default so old files load as Mandelbrot.
public struct ViewZone: Codable, Equatable {
    public var centerX: Double
    public var centerY: Double
    public var scale: Double
    public var maxIter: Int
    public var julia: Bool
    public var jcx: Double
    public var jcy: Double

    public init(centerX: Double, centerY: Double, scale: Double,
                maxIter: Int, julia: Bool = false,
                jcx: Double = Spec.defaultJcx, jcy: Double = Spec.defaultJcy) {
        self.centerX = centerX; self.centerY = centerY; self.scale = scale
        self.maxIter = maxIter; self.julia = julia; self.jcx = jcx; self.jcy = jcy
    }

    enum CodingKeys: String, CodingKey {
        case centerX = "CenterX", centerY = "CenterY", scale = "Scale"
        case maxIter = "MaxIter", julia = "Julia", jcx = "Jcx", jcy = "Jcy"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        centerX = try c.decode(Double.self, forKey: .centerX)
        centerY = try c.decode(Double.self, forKey: .centerY)
        scale = try c.decode(Double.self, forKey: .scale)
        maxIter = try c.decode(Int.self, forKey: .maxIter)
        julia = try c.decodeIfPresent(Bool.self, forKey: .julia) ?? false
        jcx = try c.decodeIfPresent(Double.self, forKey: .jcx) ?? Spec.defaultJcx
        jcy = try c.decodeIfPresent(Double.self, forKey: .jcy) ?? Spec.defaultJcy
    }

    /// Finite coordinates and Scale > 0 (§5.9), else a readable reason.
    public func validated() -> String? {
        if !centerX.isFinite || !centerY.isFinite { return "center is not finite" }
        if !scale.isFinite || scale <= 0 { return "scale must be > 0" }
        if !jcx.isFinite || !jcy.isFinite { return "Julia constant is not finite" }
        return nil
    }
}

/// SPEC §5.8 two-stack history, back capped at 200, push clears forward.
public struct History {
    public private(set) var back: [ViewZone] = []
    public private(set) var fwd: [ViewZone] = []

    public init() {}

    public mutating func push(_ z: ViewZone) {
        back.append(z)
        if back.count > Spec.historyLimit {
            back.removeFirst(back.count - Spec.historyLimit)
        }
        fwd.removeAll()
    }

    public mutating func goBack(from current: ViewZone) -> ViewZone? {
        guard let z = back.popLast() else { return nil }
        fwd.append(current)
        return z
    }

    public mutating func goForward(from current: ViewZone) -> ViewZone? {
        guard let z = fwd.popLast() else { return nil }
        back.append(current)
        return z
    }

    public var canBack: Bool { !back.isEmpty }
    public var canForward: Bool { !fwd.isEmpty }
}
