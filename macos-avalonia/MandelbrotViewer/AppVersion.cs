namespace MandelbrotViewer;

public static class AppVersion
{
    public const int Major = 2;
    public const int Minor = 22;
    public const int Patch = 7;

    public static string Full => $"{Major}.{Minor}.{Patch}";

    // UI displays X.Y when Patch==0, else X.Y.Z (§1.2)
    public static string Display => Patch == 0 ? $"{Major}.{Minor}" : Full;
}
