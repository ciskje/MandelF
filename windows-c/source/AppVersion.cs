namespace MandelbrotViewer;

// Application version in X.Y.Z format (simplified Semantic Versioning).
// Bump: X = breaking change, Y = new feature, Z = fix/refactor/docs.
// The UI shows Display: if Z is 0 the short notation "X.Y" is used.
public static class AppVersion
{
    public const int Major = 7;
    public const int Minor = 3;
    public const int Patch = 1;

    public static string Full => $"{Major}.{Minor}.{Patch}";

    public static string Display => Patch == 0 ? $"{Major}.{Minor}" : Full;
}
