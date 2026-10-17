import Foundation
import MandelCore

// ponytail: one runnable check (no Xcode here, so no XCTest).
func check(_ cond: Bool, _ msg: String) {
    guard cond else { print("FAIL: \(msg)"); exit(1) }
    print("ok: \(msg)")
}

check(Mandelbrot.iterations(cx: 0, cy: 0, maxIter: 100) == 100, "origin inside")
check(Mandelbrot.iterations(cx: -1, cy: 0, maxIter: 100) == 100, "bulb inside")
check(Mandelbrot.iterations(cx: 2, cy: 0, maxIter: 100) == 2, "far point escapes")
check(Mandelbrot.render(width: 4, height: 3, centerX: -0.5, centerY: 0,
                        viewHeight: 2.8, maxIter: 10).count == 48, "render size")
print("SelfCheck passed")
