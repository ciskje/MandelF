import Foundation

/// SPEC §7.2 in-memory ring of the last 200 timestamped lines.
public final class AppLog: @unchecked Sendable {
    public static let shared = AppLog()
    private var lines: [String] = []
    private let lock = NSLock()
    private static let fmt: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f
    }()

    public func add(_ msg: String) {
        lock.lock()
        lines.append("\(Self.fmt.string(from: Date()))  \(msg)")
        if lines.count > Spec.logLines { lines.removeFirst(lines.count - Spec.logLines) }
        lock.unlock()
    }

    public var all: [String] {
        lock.lock(); defer { lock.unlock() }; return lines
    }
}
