import Foundation

/// SPEC §6.4/§6.6 diagnostics: CPU name via sysctl, OS string, host MAC tag.
public enum Diagnostics {
    public static func cpuName() -> String {
        var size = 0
        sysctlbyname("machdep.cpu.brand_string", nil, &size, nil, 0)
        var buf = [CChar](repeating: 0, count: size)
        sysctlbyname("machdep.cpu.brand_string", &buf, &size, nil, 0)
        let s = String(cString: buf)
        return s.isEmpty ? "Apple Silicon" : s
    }

    /// Host MAC address ("aa:bb:cc:dd:ee:ff", en0 first, else the first en*
    /// interface that has one). "" when unavailable. Identifies the host in
    /// benchmark device labels / CSV so equal cards on different machines
    /// do not collide in history.
    public static func hostMac() -> String {
        guard let out = run("/sbin/ifconfig", ["-a"]) else { return "" }
        var iface = ""
        var macs: [String: String] = [:]
        for line in out.split(separator: "\n", omittingEmptySubsequences: true) {
            let isHeader = line.first != nil && line.first != " " && line.first != "\t"
            if isHeader, let name = line.split(separator: ":").first.map(String.init) {
                iface = name
            } else if let r = line.range(of: #"ether ([0-9a-f]{2}(?::[0-9a-f]{2}){5})"#,
                                         options: .regularExpression) {
                // "ether aa:bb:..." -> keep the address part
                let m = line[r].dropFirst("ether ".count)
                macs[iface] = String(m)
            }
        }
        return macs["en0"] ?? macs.first(where: { $0.key.hasPrefix("en") })?.value ?? ""
    }

    private static func run(_ path: String, _ args: [String]) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do {
            try p.run()
        } catch {
            return nil
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return p.terminationStatus == 0 ? String(decoding: data, as: UTF8.self) : nil
    }

    public static func osString() -> String {
        let v = ProcessInfo.processInfo.operatingSystemVersion
        return "macOS \(v.majorVersion).\(v.minorVersion).\(v.patchVersion)"
    }

    /// Device label suffix for benchmark CSV/detail lines: " aa:bb:..." or ""
    /// when no MAC is available (append after the CPU/GPU device name).
    public static func hostMacTag() -> String {
        let m = hostMac()
        return m.isEmpty ? "" : " " + m
    }
}
