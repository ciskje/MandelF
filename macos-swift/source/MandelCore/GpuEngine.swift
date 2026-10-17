import Foundation
import Metal

/// SPEC §4.2 view parameters: field order, 8-byte packing, exactly 88 bytes.
public struct GpuViewParams {
    public var centerX: Double
    public var centerY: Double
    public var pixelSize: Double
    public var topY: Double
    public var w: Int32
    public var h: Int32
    public var maxIter: Int32
    public var supersample: Int32
    public var juliaOn: Int32
    public var jcx: Double
    public var jcy: Double
    public var fullW: Int32
    public var fullH: Int32
    public var offsetX: Int32
    public var offsetY: Int32

    public init(_ p: RenderParams) {
        let fw = p.fullWidth, fh = p.fullHeight
        centerX = p.centerX; centerY = p.centerY
        pixelSize = p.scale / Double(fw * p.aa)
        topY = p.centerY - Double(fh * p.aa) * 0.5 * pixelSize
        w = Int32(p.width); h = Int32(p.height)
        maxIter = Int32(p.maxIter); supersample = Int32(p.aa)
        juliaOn = p.julia ? 1 : 0
        jcx = p.jcx; jcy = p.jcy
        fullW = Int32(fw); fullH = Int32(fh)
        offsetX = Int32(p.offsetX); offsetY = Int32(p.offsetY)
    }
}

/// Float mirror uploaded to Metal (Apple Silicon has no double in MSL).
/// Converted from the 88-byte host struct with correct rounding.
public struct GpuViewParamsF {
    public var centerX: Float
    public var centerY: Float
    public var pixelSize: Float
    public var topY: Float
    public var w: Int32
    public var h: Int32
    public var maxIter: Int32
    public var supersample: Int32
    public var juliaOn: Int32
    public var jcx: Float
    public var jcy: Float
    public var fullW: Int32
    public var fullH: Int32
    public var offsetX: Int32
    public var offsetY: Int32

    public init(_ p: GpuViewParams) {
        centerX = Float(p.centerX); centerY = Float(p.centerY)
        pixelSize = Float(p.pixelSize); topY = Float(p.topY)
        w = p.w; h = p.h; maxIter = p.maxIter
        supersample = p.supersample; juliaOn = p.juliaOn
        jcx = Float(p.jcx); jcy = Float(p.jcy)
        fullW = p.fullW; fullH = p.fullH
        offsetX = p.offsetX; offsetY = p.offsetY
    }
}

let mslSource = """
#include <metal_stdlib>
using namespace metal;
struct GpuViewParamsF {
// Float mirror of GpuViewParams: Apple Silicon Metal has no double at all,
// so the 88-byte double struct (host contract, §4.2) is converted here.
    float CenterX; float CenterY; float PixelSize; float TopY;
    int W; int H; int MaxIter; int Supersample; int JuliaOn;
    float Jcx; float Jcy;
    int FullW; int FullH; int OffsetX; int OffsetY;
};
inline bool interiorBulb(float px, float py) {
    float q = (px - 0.25f) * (px - 0.25f) + py * py;
    if (q * (q + (px - 0.25f)) <= 0.25f * py * py) return true;
    float dx = px + 1.0f;
    return dx * dx + py * py <= 0.0625f;
}
kernel void mandelRenderF(texture2d<float, access::write> outTex [[texture(0)]],
                          constant GpuViewParamsF &P [[buffer(0)]],
                          texture1d<float, access::sample> lutTex [[texture(1)]],
                          sampler lutSmp [[sampler(0)]],
                          uint2 gid [[thread_position_in_grid]]) {
    if (gid.x >= (uint)P.W || gid.y >= (uint)P.H) return;
    int FW = P.FullW == 0 ? P.W : P.FullW;
    float originX = P.CenterX - (float)FW * (float)P.Supersample * 0.5f * P.PixelSize;
    int k = P.Supersample;
    float invMax = 1.0f / (float)P.MaxIter;
    uint r = 0, g = 0, b = 0;
    for (int sy = 0; sy < k; sy++) for (int sx = 0; sx < k; sx++) {
        int gbx = (P.OffsetX + (int)gid.x) * k + sx;
        int gby = (P.OffsetY + (int)gid.y) * k + sy;
        float px = originX + (float)gbx * (float)P.PixelSize;
        float py = (float)P.TopY + (float)gby * (float)P.PixelSize;
        float cr = 0.0f, cg = 0.0f, cb = 0.0f;
        float zx, zy, ccx, ccy;
        if (P.JuliaOn != 0) { zx = px; zy = py; ccx = (float)P.Jcx; ccy = (float)P.Jcy; }
        else { zx = 0.0f; zy = 0.0f; ccx = px; ccy = py; }
        bool black = false;
        if (P.JuliaOn == 0 && interiorBulb(px, py)) black = true;
        else {
            float zx2 = zx * zx, zy2 = zy * zy;
            int iter = 0;
            while (iter < P.MaxIter && zx2 + zy2 <= 4.0f) {
                float nzy = 2.0f * zx * zy + ccy;
                float nzx = zx2 - zy2 + ccx;
                zy = nzy; zx = nzx;
                zx2 = zx * zx; zy2 = zy * zy;
                iter++;
            }
            if (iter < P.MaxIter) {
                float mod = max(zx2 + zy2, 4.0f);
                float smooth = (float)iter + 1.0f - log2(0.5f * log(mod));
                float3 c = lutTex.sample(lutSmp, clamp(smooth * invMax, 0.0f, 1.0f)).rgb;
                cr = c.r * 255.0f; cg = c.g * 255.0f; cb = c.b * 255.0f;
            } else black = true;
        }
        if (!black) { r += (uint)cr; g += (uint)cg; b += (uint)cb; }
    }
    uint n = (uint)(k * k);
    outTex.write(float4((float)(r / n) / 255.0f, (float)(g / n) / 255.0f,
                         (float)(b / n) / 255.0f, 1.0f), gid);
}
kernel void mandelBenchF(device uint *counts [[buffer(1)]],
                         constant GpuViewParamsF &P [[buffer(0)]],
                         uint2 gid [[thread_position_in_grid]]) {
    if (gid.x >= (uint)P.W || gid.y >= (uint)P.H) return;
    float ps = (float)P.PixelSize;
    float cx = (float)P.CenterX + ((float)gid.x - (float)P.W * 0.5f) * ps;
    float cy = (float)P.TopY + (float)gid.y * ps;
    float zx = 0.0f, zy = 0.0f, zx2 = 0.0f, zy2 = 0.0f;
    int iter = 0;
    while (iter < P.MaxIter && zx2 + zy2 <= 4.0f) {
        float nzy = 2.0f * zx * zy + cy;
        float nzx = zx2 - zy2 + cx;
        zy = nzy; zx = nzx;
        zx2 = zx * zx; zy2 = zy * zy;
        iter++;
    }
    counts[gid.y * (uint)P.W + gid.x] = (uint)iter;
}
"""

/// SPEC §4.1 host interface over Metal. Float programs only: Apple Silicon
/// has no FP64 in Metal, so 64-bit is exposed as unavailable (§2.6, §4).
public final class MetalEngine {
    private var device: MTLDevice?
    private var queue: MTLCommandQueue?
    private var renderPipe: MTLComputePipelineState?
    private var benchPipe: MTLComputePipelineState?
    private var sampler: MTLSamplerState?
    private var lutKey = ""
    private var lutTex: MTLTexture?
    private var outTex: MTLTexture?
    private var outW = 0
    private var outH = 0
    private var blockW = 32
    private var blockH = 8 // tuned default 32x8 (§4.5)
    private let gate = NSLock() // §4.1 single gate
    public private(set) var lastError = ""
    public private(set) var deviceShortName = ""
    public private(set) var details = ""
    public private(set) var deviceName = ""

    public init() {}

    /// Sorted device names, software/virtual renderers excluded (§4.1).
    public static func adapterNames() -> [String] {
        MTLCopyAllDevices()
            .map(\.name)
            .filter {
                !$0.localizedCaseInsensitiveContains("software")
                    && !$0.localizedCaseInsensitiveContains("virtual")
            }
            .sorted()
    }

    public static func wantsDouble(scale: Double) -> Bool {
        scale < Spec.wantsDoubleScale
    }

    static func shortName(_ name: String) -> String {
        name.hasPrefix("Apple ") ? String(name.dropFirst(6)) : name
    }

    /// Select device (nil = auto = most memory), load programs, autotune.
    /// False on any failure, with human-readable lastError (§4.1, §4.9).
    public func tryInitialize(deviceName: String?) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        precondition(MemoryLayout<GpuViewParams>.size == 88, "GpuViewParams must be 88 bytes")
        let devices = MTLCopyAllDevices()
        let dev: MTLDevice?
        if let want = deviceName, !want.isEmpty {
            dev = devices.first { $0.name == want }
            if dev == nil {
                lastError = "no such device '\(want)'"
                return false
            }
        } else {
            dev = devices.max {
                $0.recommendedMaxWorkingSetSize < $1.recommendedMaxWorkingSetSize
            }
        }
        guard let dev else {
            lastError = "no Metal device found"
            return false
        }
        do {
            let lib = try dev.makeLibrary(source: mslSource, options: nil)
            guard let rf = lib.makeFunction(name: "mandelRenderF"),
                  let bf = lib.makeFunction(name: "mandelBenchF")
            else {
                lastError = "kernel function missing"
                return false
            }
            renderPipe = try dev.makeComputePipelineState(function: rf)
            benchPipe = try dev.makeComputePipelineState(function: bf)
            let desc = MTLSamplerDescriptor()
            desc.minFilter = .linear; desc.magFilter = .linear
            desc.sAddressMode = .clampToEdge
            sampler = dev.makeSamplerState(descriptor: desc)
            guard let q = dev.makeCommandQueue() else {
                lastError = "no command queue"
                return false
            }
            device = dev
            queue = q
            self.deviceName = dev.name
            deviceShortName = Self.shortName(dev.name)
            tuneBlocks()
            let mem = dev.recommendedMaxWorkingSetSize / 1_000_000
            details = "mem \(mem) MB, block \(blockW)x\(blockH) float-only, unified"
            AppLog.shared.add("GPU on '\(deviceShortName)' ready (\(details))")
            return true
        } catch {
            lastError = "\(error)"
            return false
        }
    }

    // MARK: - Autotune (§4.5)

    private func timeBench(w: Int, h: Int, maxIter: Int, bw: Int, bh: Int,
                           rounds: Int) -> Double {
        var params = benchParams(w: w, h: h, maxIter: maxIter)
        var best = Double.infinity
        var samples: [Double] = []
        for _ in 0 ..< rounds {
            let t0 = Date()
            guard dispatchBench(params: &params, w: w, h: h, bw: bw, bh: bh,
                                counts: nil) else { return .infinity }
            samples.append(Date().timeIntervalSince(t0))
        }
        samples.sort()
        best = samples[samples.count / 2]
        return best
    }

    private func tuneBlocks() {
        // Probe: real benchmark grid 960x540, 2000 iters, center (-0.75, 0).
        let w = Benchmark.width, h = Benchmark.height
        // Warmup: 8 launches at 32x8.
        var params = benchParams(w: w, h: h, maxIter: 2000)
        for _ in 0 ..< 8 {
            _ = dispatchBench(params: &params, w: w, h: h, bw: 32, bh: 8, counts: nil)
        }
        let candidates = [(32, 4), (32, 8), (32, 16), (32, 32)]
        var timed: [(Double, (Int, Int))] = []
        for c in candidates { // median of 5 rounds each
            timed.append((timeBench(w: w, h: h, maxIter: 2000, bw: c.0, bh: c.1,
                                    rounds: 5), c))
        }
        timed.sort { $0.0 < $1.0 }
        let def = timeBench(w: w, h: h, maxIter: 2000, bw: 32, bh: 8, rounds: 5)
        let best = timed.first ?? (def, (32, 8))
        if best.0.isFinite, def.isFinite, def > 0,
           (def - best.0) / def >= 0.015 { // 1.5% margin (§4.5)
            blockW = best.1.0
            blockH = best.1.1
        } else {
            blockW = 32
            blockH = 8
        }
    }

    private func benchParams(w: Int, h: Int, maxIter: Int) -> GpuViewParams {
        var gp = GpuViewParams(RenderParams(width: w, height: h,
                                            centerX: Benchmark.centerX,
                                            centerY: Benchmark.centerY,
                                            scale: Benchmark.scale,
                                            maxIter: maxIter, palette: 0, aa: 1,
                                            useDouble: false))
        // Probe pixel 0.01/960 (§4.5).
        gp.pixelSize = 0.01 / Double(w)
        gp.centerX = -0.75
        gp.centerY = 0
        gp.topY = -Double(h) * 0.5 * gp.pixelSize
        return gp
    }

    // MARK: - Render (§4.6)

    private func ensureLut(palette: Int, maxIter: Int) {
        guard let dev = device else { return }
        let key = "\(palette):\(maxIter)"
        if key == lutKey, lutTex != nil { return }
        let table = Palette.lut(palette: palette, maxIter: maxIter)
        var bytes = [UInt8](repeating: 0, count: Spec.lutSize * 4)
        for (i, v) in table.enumerated() {
            bytes[i * 4] = UInt8((v >> 16) & 0xFF)
            bytes[i * 4 + 1] = UInt8((v >> 8) & 0xFF)
            bytes[i * 4 + 2] = UInt8(v & 0xFF)
            bytes[i * 4 + 3] = 0xFF
        }
        let desc = MTLTextureDescriptor()
        desc.textureType = .type1D
        desc.pixelFormat = .rgba8Unorm
        desc.width = Spec.lutSize
        desc.usage = .shaderRead
        guard let tex = dev.makeTexture(descriptor: desc) else { return }
        tex.replace(region: MTLRegionMake1D(0, Spec.lutSize), mipmapLevel: 0,
                    withBytes: bytes, bytesPerRow: Spec.lutSize * 4)
        lutTex = tex
        lutKey = key
    }

    @discardableResult
    private func dispatchBench(params: inout GpuViewParams, w: Int, h: Int,
                               bw: Int, bh: Int, counts: MTLBuffer?) -> Bool {
        guard let dev = device, let queue, let benchPipe else { return false }
        guard let fresh = dev.makeBuffer(length: max(1, w * h * 4),
                                         options: .storageModeShared) else { return false }
        let target = counts ?? fresh
        guard let cmd = queue.makeCommandBuffer(),
              let enc = cmd.makeComputeCommandEncoder()
        else { return false }
        enc.setComputePipelineState(benchPipe)
        var fparams = GpuViewParamsF(params)
        enc.setBytes(&fparams, length: MemoryLayout<GpuViewParamsF>.size, index: 0)
        enc.setBuffer(target, offset: 0, index: 1)
        enc.dispatchThreads(MTLSize(width: w, height: h, depth: 1),
                            threadsPerThreadgroup: MTLSize(width: bw, height: bh, depth: 1))
        enc.endEncoding()
        cmd.commit()
        cmd.waitUntilCompleted()
        return cmd.status == .completed
    }

    /// Whole/tile float render into RGBA bytes (§4.6). Nil on failure.
    public func render(_ p: RenderParams) -> [UInt8]? {
        gate.lock()
        defer { gate.unlock() }
        guard let dev = device, let queue, let renderPipe, let sampler else {
            lastError = "engine not initialized"
            return nil
        }
        if p.useDouble {
            lastError = "double precision not supported on this GPU (float-only)"
            return nil
        }
        ensureLut(palette: p.palette, maxIter: p.maxIter)
        guard let lut = lutTex else {
            lastError = "LUT upload failed"
            return nil
        }
        if outTex == nil || outW != p.width || outH != p.height {
            let desc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .rgba8Unorm,
                                                                width: p.width,
                                                                height: p.height,
                                                                mipmapped: false)
            desc.usage = [.shaderWrite, .shaderRead]
            desc.storageMode = .shared
            guard let tex = dev.makeTexture(descriptor: desc) else {
                lastError = "render target failed"
                return nil
            }
            outTex = tex
            outW = p.width
            outH = p.height
        }
        guard let out = outTex,
              let cmd = queue.makeCommandBuffer(),
              let enc = cmd.makeComputeCommandEncoder()
        else {
            lastError = "encode failed"
            return nil
        }
        let params = GpuViewParams(p)
        var fparams = GpuViewParamsF(params)
        enc.setComputePipelineState(renderPipe)
        enc.setTexture(out, index: 0)
        enc.setBytes(&fparams, length: MemoryLayout<GpuViewParamsF>.size, index: 0)
        enc.setTexture(lut, index: 1)
        enc.setSamplerState(sampler, index: 0)
        enc.dispatchThreads(MTLSize(width: p.width, height: p.height, depth: 1),
                            threadsPerThreadgroup: MTLSize(width: blockW,
                                                           height: blockH, depth: 1))
        enc.endEncoding()
        cmd.commit()
        cmd.waitUntilCompleted()
        guard cmd.status == .completed else {
            lastError = "render failed (\(cmd.status))"
            return nil
        }
        var bytes = [UInt8](repeating: 0, count: p.width * p.height * 4)
        out.getBytes(&bytes, bytesPerRow: p.width * 4,
                     from: MTLRegionMake2D(0, 0, p.width, p.height), mipmapLevel: 0)
        return bytes
    }

    // MARK: - Benchmark (§4.8 offscreen-equivalent: no present, CPU clock)

    /// Standardized iterations-only benchmark, 8 s budget (§4.8 offscreen:
    /// no present, CPU clock, in-flight ring 4..256, ≤0.8 s queued work).
    /// Nil if cancelled.
    public func bench(maxIter: Int, tick: (Double, Int) -> Bool,
                      isCancelled: () -> Bool) -> (seconds: Double, frames: Int)? {
        gate.lock()
        defer { gate.unlock() }
        guard device != nil, benchPipe != nil else {
            lastError = "engine not initialized"
            return nil
        }
        let w = Benchmark.width, h = Benchmark.height
        // VRAM pre-check (§4.8): required vs working set, fail fast.
        let need = w * h * 4 * 2
        if let dev = device, dev.recommendedMaxWorkingSetSize > 0,
           need > dev.recommendedMaxWorkingSetSize {
            lastError = "not enough device memory"
            return nil
        }
        let params = GpuViewParams(RenderParams(width: w, height: h,
                                                centerX: Benchmark.centerX,
                                                centerY: Benchmark.centerY,
                                                scale: Benchmark.scale,
                                                maxIter: maxIter, palette: 0,
                                                aa: 1, useDouble: false))
        var fparams = GpuViewParamsF(params)
        guard let queue, let benchPipe else { return nil }
        let slots = 16 // in-flight ring (§4.8: 4..256)
        let sema = DispatchSemaphore(value: slots)
        var bufs: [MTLBuffer] = []
        for _ in 0 ..< slots {
            guard let b = device?.makeBuffer(length: max(1, w * h * 4),
                                             options: .storageModeShared) else { return nil }
            bufs.append(b)
        }
        let frames = Locked(0)
        let failed = Locked(false)
        var slot = 0
        let t0 = Date()
        var last = t0
        var stopped = false
        while !stopped {
            if isCancelled() { stopped = true; break }
            if Date().timeIntervalSince(t0) >= Benchmark.budget { break }
            if sema.wait(timeout: .now() + 60) == .timedOut { // 60 s frame timeout (§4.8)
                lastError = "GPU hung during the benchmark"
                return nil
            }
            guard let cmd = queue.makeCommandBuffer(),
                  let enc = cmd.makeComputeCommandEncoder() else {
                failed.value = true
                sema.signal()
                break
            }
            enc.setComputePipelineState(benchPipe)
            enc.setBytes(&fparams, length: MemoryLayout<GpuViewParamsF>.size, index: 0)
            enc.setBuffer(bufs[slot], offset: 0, index: 1)
            slot = (slot + 1) % slots
            enc.dispatchThreads(MTLSize(width: w, height: h, depth: 1),
                                threadsPerThreadgroup: MTLSize(width: blockW,
                                                               height: blockH, depth: 1))
            enc.endEncoding()
            cmd.addCompletedHandler { _ in
                frames.value += 1
                sema.signal()
            }
            cmd.commit()
            let el = Date().timeIntervalSince(t0)
            if Date().timeIntervalSince(last) >= 1.0 || el >= Benchmark.budget {
                last = Date()
                if !tick(el, frames.value) { stopped = true }
            }
        }
        if isCancelled() { return nil }
        if failed.value {
            lastError = "benchmark dispatch failed"
            return nil
        }
        // Drain in-flight frames, then stop the clock at the last completion.
        for _ in 0 ..< slots {
            if sema.wait(timeout: .now() + 60) == .timedOut {
                lastError = "GPU hung during the benchmark"
                return nil
            }
        }
        let seconds = Date().timeIntervalSince(t0)
        for _ in 0 ..< slots { sema.signal() }
        return (seconds, frames.value)
    }

    public func dispose() {
        gate.lock()
        defer { gate.unlock() }
        outTex = nil
        lutTex = nil
        renderPipe = nil
        benchPipe = nil
        queue = nil
        device = nil
    }
}
