namespace MandelbrotViewer;

// CSV export of benchmark results (GUI and CLI): one row per run with
// timestamp, engine, device, precision, frames, seconds and MPixel/s.
// Colonne: timestamp,engine,device,precision,run,frames,seconds,mpixel_s.
internal static class BenchmarkCsv
{
    private const string Header = "timestamp,engine,device,precision,run,frames,seconds,mpixel_s";

    public static void AppendRow(string path, string engine, string device, string precision,
        int run, int frames, double seconds, double mpixel)
    {
        string row = string.Join(",",
            DateTime.UtcNow.ToString("o"),
            Esc(engine), Esc(device), Esc(precision),
            run.ToString(),
            frames.ToString(),
            seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            mpixel.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        bool header = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var w = new StreamWriter(path, append: true);
        if (header) w.WriteLine(Header);
        w.WriteLine(row);
    }

    private static string Esc(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
