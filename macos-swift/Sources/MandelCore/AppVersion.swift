public enum AppVersion {
    public static let major = 0
    public static let minor = 3
    public static let patch = 0
    public static let full = "\(major).\(minor).\(patch)"
    public static let display = patch == 0 ? "\(major).\(minor)" : full
}
