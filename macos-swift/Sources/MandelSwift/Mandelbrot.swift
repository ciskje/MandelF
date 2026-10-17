import Foundation

/// Escape-time core. Returns the iteration count in 0...maxIter,
/// where maxIter means "inside the set".
enum Mandelbrot {
    static func iterations(cx: Double, cy: Double, maxIter: Int) -> Int {
        var zx = 0.0
        var zy = 0.0
        var i = 0
        while i < maxIter {
            let zx2 = zx * zx
            let zy2 = zy * zy
            if zx2 + zy2 > 4.0 { break }
            zy = 2.0 * zx * zy + cy
            zx = zx2 - zy2 + cx
            i += 1
        }
        return i
    }

    /// RGBA render (8 bit, alpha-last). Flat sine palette, in-set pixels black.
    // ponytail: scalar loop; vectorize/Metal only if measurably too slow.
    static func render(width: Int, height: Int, centerX: Double, centerY: Double,
                       viewHeight: Double, maxIter: Int) -> [UInt8] {
        var px = [UInt8](repeating: 0, count: width * height * 4)
        let step = viewHeight / Double(height)
        let x0 = centerX - step * Double(width) / 2.0
        let y0 = centerY - step * Double(height) / 2.0
        for py in 0 ..< height {
            let cy = y0 + step * Double(py)
            for x in 0 ..< width {
                let i = iterations(cx: x0 + step * Double(x), cy: cy, maxIter: maxIter)
                let o = (py * width + x) * 4
                if i < maxIter {
                    let f = Double(i)
                    px[o] = UInt8(128.0 + 127.0 * sin(0.024 * f))
                    px[o + 1] = UInt8(128.0 + 127.0 * sin(0.024 * f + 2.1))
                    px[o + 2] = UInt8(128.0 + 127.0 * sin(0.024 * f + 4.2))
                }
                px[o + 3] = 255
            }
        }
        return px
    }
}
