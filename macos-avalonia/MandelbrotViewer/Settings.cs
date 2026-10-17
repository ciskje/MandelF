using System.Text.Json;
using System.Text.Json.Serialization;

namespace MandelbrotViewer;

// View zone record (§5.8): Julia fields default so old files load as Mandelbrot.
public sealed record ViewZone(
    double CenterX,
    double CenterY,
    double Scale,
    int MaxIter,
    bool Julia = false,
    double Jcx = Mandelbrot.DefaultJcx,
    double Jcy = Mandelbrot.DefaultJcy);

public sealed class AppSettings
{
    public bool IterAuto { get; set; } = false;
    public int MaxIter { get; set; } = 256;
    public int Palette { get; set; } = 0;
    public int AaIndex { get; set; } = 0;
    public string Engine { get; set; } = "Gpu";
    public string Gpu { get; set; } = "";
    public bool Single { get; set; } = false;   // true = 32-bit float
    public bool Julia { get; set; } = false;
    public double Jcx { get; set; } = Mandelbrot.DefaultJcx;
    public double Jcy { get; set; } = Mandelbrot.DefaultJcy;
    public int WinX { get; set; } = 0;
    public int WinY { get; set; } = 0;
    public int WinW { get; set; } = 0;
    public int WinH { get; set; } = 0;
    public bool Maximized { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DataDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        // macOS: prefer ~/Library/Application Support (§7.1 OS-standard location)
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support");
        }
        return Path.Combine(baseDir, "MandelbrotViewer");
    }

    public static string SettingsPath() => Path.Combine(DataDir(), "settings.json");
    public static string ZonesDir() => Path.Combine(DataDir(), "zone");

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsPath();
            if (!File.Exists(path))
                return new AppSettings();
            string json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (s is null) return new AppSettings();
            s.MaxIter = Math.Clamp(s.MaxIter, Mandelbrot.MinIter, Mandelbrot.MaxIter);
            s.Palette = Math.Clamp(s.Palette, 0, 6);
            s.AaIndex = Math.Clamp(s.AaIndex, 0, 3);
            if (!double.IsFinite(s.Jcx)) s.Jcx = Mandelbrot.DefaultJcx;
            if (!double.IsFinite(s.Jcy)) s.Jcy = Mandelbrot.DefaultJcy;
            return s;
        }
        catch
        {
            return new AppSettings(); // missing/corrupt → defaults, no error (§7.1)
        }
    }

    public void Save()
    {
        try
        {
            MaxIter = Math.Clamp(MaxIter, Mandelbrot.MinIter, Mandelbrot.MaxIter);
            Directory.CreateDirectory(DataDir());
            File.WriteAllText(SettingsPath(), JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // best-effort, never block closing (§7.1)
        }
    }

    public static ViewZone? LoadZone(string path)
    {
        string json = File.ReadAllText(path);
        var z = JsonSerializer.Deserialize<ViewZone>(json, JsonOpts);
        if (z is null) throw new InvalidDataException("Empty zone file.");
        if (!double.IsFinite(z.CenterX) || !double.IsFinite(z.CenterY) ||
            !double.IsFinite(z.Scale) || z.Scale <= 0)
            throw new InvalidDataException("Invalid zone: need finite center and Scale > 0.");
        return z;
    }

    public static void SaveZone(string path, ViewZone zone)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(zone, JsonOpts));
    }
}
