import XCTest
@testable import MandelSwift

final class MandelbrotTests: XCTestCase {
    func testOriginIsInside() {
        XCTAssertEqual(Mandelbrot.iterations(cx: 0, cy: 0, maxIter: 100), 100)
    }

    func testBulbIsInside() {
        XCTAssertEqual(Mandelbrot.iterations(cx: -1, cy: 0, maxIter: 100), 100)
    }

    func testFarPointEscapesImmediately() {
        XCTAssertEqual(Mandelbrot.iterations(cx: 2, cy: 0, maxIter: 100), 1)
    }

    func testRenderSize() {
        XCTAssertEqual(Mandelbrot.render(width: 4, height: 3, centerX: -0.5, centerY: 0,
                                         viewHeight: 2.8, maxIter: 10).count, 48)
    }
}
