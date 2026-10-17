using System.Text.Json;

namespace MandelbrotViewer;

// Settings persisted between launches
// (%APPDATA%\MandelbrotViewer\settings.json).
public sealed class AppSettings
{
    public bool IterAuto { get; set; }
    public int MaxIter { get; set; } = 256;
    public int Palette { get; set; }
    public int AaIndex { get; set; }
    public string Engine { get; set; } = nameof(RenderEngine.Cuda);
    // Chosen video card ("" = auto).
    public string Gpu { get; set; } = "";
    // CUDA precision: true = single 32-bit (float), false = double 64-bit (default).
    public bool Single { get; set; }
    // Active Julia mode.
    public bool Julia { get; set; }
    // Julia constant c (real part).
    public double Jcx { get; set; } = -0.7;
    // Julia constant c (imaginary part).
    public double Jcy { get; set; } = 0.27015;
    public int WinX { get; set; }
    public int WinY { get; set; }
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool Maximized { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MandelbrotViewer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return new AppSettings();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings(); // missing or corrupt file: defaults
        }
    }

    public void Save()
    {
        // Minimal validation: never persist absurd values.
        MaxIter = Math.Clamp(MaxIter, 50, 50000);

        string path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
