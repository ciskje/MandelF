public enum AppVersion {
    public static let major = 2
    public static let minor = 24
    public static let patch = 2
    public static let full = "\(major).\(minor).\(patch)"
    public static let display = patch == 0 ? "\(major).\(minor)" : full
}
