using System.Globalization;

namespace MandelbrotViewer;

// CSV row format + append with header on first write (§6.1).
public static class BenchmarkCsv
{
    public const string Header = "timestamp,engine,device,precision,run,frames,seconds,mpixel_s";

    public static string FormatRow(DateTime utc, string engine, string device, string precision,
        int run, int frames, double seconds, double mpixelS) =>
        string.Join(",", new[]
        {
            utc.ToString("o", CultureInfo.InvariantCulture),
            Quote(engine), Quote(device), Quote(precision),
            run.ToString(CultureInfo.InvariantCulture),
            frames.ToString(CultureInfo.InvariantCulture),
            seconds.ToString("0.00", CultureInfo.InvariantCulture),
            mpixelS.ToString("0.#", CultureInfo.InvariantCulture),
        });

    private static string Quote(string f) =>
        f.Contains(',') || f.Contains('"') || f.Contains('\n')
            ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;

    public static void AppendRow(string path, string row)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        bool needHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var w = new StreamWriter(path, append: true);
        if (needHeader) w.WriteLine(Header);
        w.WriteLine(row);
    }
}
