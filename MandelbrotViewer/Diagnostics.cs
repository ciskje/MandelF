using SharpGen.Runtime;
using Vortice.DXGI;

namespace MandelbrotViewer;

// Command-line diagnostics without UI (MandelbrotViewer --diag-dx | --diag-gpu):
// verifies the initialization of the DirectX and CUDA engines. Useful for troubleshooting
// when the main window does not start or an engine does not initialize.
internal static class Diagnostics
{
    public static void DiagDx(string? adapterName = null)
    {
        Console.WriteLine("DXGI cards: " + string.Join(", ", DxMandelbrot.AdapterNames())
            + (DxMandelbrot.EnumerationError.Length > 0 ? "   [ENUM ERROR: " + DxMandelbrot.EnumerationError + "]" : ""));

        // Detailed enumeration: shows the result of every adapter and every
        // exception (AdapterNames returns only the valid names and hides the errors).
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            Console.WriteLine("Factory DXGI1: OK");
            for (uint i = 0; i < 32; i++)
            {
                Result enumRes = factory.EnumAdapters(i, out IDXGIAdapter adapter);
                if (enumRes.Failure)
                {
                    Console.WriteLine($"  EnumAdapters({i}): end ({enumRes.Code})");
                    break;
                }
                using (adapter)
                {
                    try
                    {
                        var desc = adapter.Description;
                        Console.WriteLine($"  Adapter {i}: {desc.Description} | VRAM {desc.DedicatedVideoMemory / (1024 * 1024)} MB");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Adapter {i}: description read FAILED: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DXGI factory/enum failed: {ex.GetType().Name}: {ex.Message}");
        }

        using var f = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
        f.CreateControl();
        var handle = f.Handle; // forces creation of the native handle
        bool ok = DxMandelbrot.TryInitialize(handle, 800, 600, adapterName);
        Console.WriteLine("IsReady: " + ok);
        Console.WriteLine("AdapterName: " + DxMandelbrot.AdapterName);
        Console.WriteLine("LastError: " + DxMandelbrot.LastError);
        if (ok)
        {
            try
            {
                DxMandelbrot.Render(MandelbrotForm.StartCenterX, MandelbrotForm.StartCenterY, MandelbrotForm.StartScale, 800, 600, 200, 1, Palette.Fire);
                Console.WriteLine("Render: OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Render failed: " + ex);
            }
        }
        DxMandelbrot.Dispose();
    }

    public static void DiagGpu()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "diag_gpu.txt");
        try
        {
            var devices = GpuMandelbrot.DeviceNames();
            bool ok = GpuMandelbrot.TryInitialize();
            using var bmp = new Bitmap(320, 240);
            if (ok)
                GpuMandelbrot.Render(bmp, MandelbrotForm.StartCenterX, MandelbrotForm.StartCenterY, MandelbrotForm.StartScale, 200, Palette.Fire, 1, false, CancellationToken.None);
            File.WriteAllText(path, $"Devices: {string.Join(", ", devices)}\nReady: {ok}\nDevice: {GpuMandelbrot.DeviceName}\nDetails: {GpuMandelbrot.DeviceDetails}\nError: {GpuMandelbrot.LastError}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, $"Exception: {ex}");
        }
        finally
        {
            GpuMandelbrot.Dispose();
        }
    }

    // Standard triple test on every available DirectX card (or only on the
    // given one): per card runs the standardized benchmark and prints the values
    // in MPixel/s with the best one — the measure used for the benchmark graph
    // history (compressed name).
    // Offscreen without window nor Present (headless): only shader + event query,
    // so DWM and cross-GPU copy do not skew the headless cards.
    // Param adapterName (string?): Input, card filter (exact DXGI name); null = all cards.
    // Param runs (int): Input, repetitions per card.
    // Param budget (TimeSpan): Input, time budget of each single run.
    // Param csvPath (string?): Input, CSV file for one row per run; null = no CSV output.
    // Shared trials loop for the CLI benchmarks: runs the trial up to `runs`
    // times, prints each result, appends CSV rows, returns the best MPixel/s.
    private static double RunTrials(string engine, string device, string precision,
        int runs, string? csvPath, Func<(int Frames, double Seconds)> trial, string tag = "")
    {
        double best = 0;
        for (int r = 1; r <= runs; r++)
        {
            try
            {
                var (frames, seconds) = trial();
                double mps = BenchmarkStandard.PixelsPerSecond(frames, seconds) / 1e6;
                best = Math.Max(best, mps);
                Console.WriteLine($"  {tag}run {r}: {frames} frames in {seconds:0.00} s  →  {mps:0.#} MPixel/s");
                WriteCsvRow(csvPath, engine, device, precision, r, frames, seconds, mps);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {tag}run {r} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return best;
    }

    public static void BenchDx(string? adapterName, int runs, TimeSpan budget, string? csvPath = null)
    {
        int gridW = BenchmarkStandard.Width * BenchmarkStandard.Aa;
        int gridH = BenchmarkStandard.Height * BenchmarkStandard.Aa;
        Console.WriteLine($"Standardized DirectX benchmark (offscreen, no Present): {runs} runs of {budget.TotalSeconds:0} s per card, " +
            $"area {BenchmarkStandard.Width}x{BenchmarkStandard.Height} AA{BenchmarkStandard.Aa} " +
            $"(grid {gridW}x{gridH}, {BenchmarkStandard.PixelsPerFrame / 1e6:0.##} MPixel/frame), " +
            $"{BenchmarkStandard.MaxIter} iter, iterations only, completion via event query.");

        IReadOnlyList<string> cards = adapterName != null
            ? new[] { adapterName }
            : DxMandelbrot.AdapterNames();
        if (cards.Count == 0)
        {
            Console.WriteLine("No DirectX card available. " +
                (DxMandelbrot.EnumerationError.Length > 0 ? "(" + DxMandelbrot.EnumerationError + ")" : ""));
            return;
        }

        var bestPerCard = new List<(string Short, double Best)>();
        foreach (string card in cards)
        {
            string shortName = DxMandelbrot.ShortAdapterName(card);
            Console.WriteLine();
            Console.WriteLine($"=== {shortName} ===");
            if (!DxMandelbrot.TryInitializeHeadless(card))
            {
                Console.WriteLine("  init failed: " + DxMandelbrot.LastError);
                continue;
            }

            double best = 0;
            try
            {
                try
                {
                    DxMandelbrot.BeginBenchmarkOffscreen(gridW, gridH);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  offscreen grid not created: " + ex.Message);
                    continue;
                }
                best = RunTrials("DirectX", shortName, "float", runs, csvPath, () =>
                {
                    var (seconds, frames) = DxMandelbrot.RunBenchmarkFramesOffscreen(
                        BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                        gridW, gridH, BenchmarkStandard.MaxIter, budget, null, CancellationToken.None);
                    return (frames, seconds);
                });
            }
            finally
            {
                DxMandelbrot.EndBenchmarkOffscreen();
                DxMandelbrot.Dispose();
            }

            if (best > 0)
            {
                bestPerCard.Add((shortName, best));
                Console.WriteLine($"  BEST: {best:0.#} MPixel/s");
            }
            else
            {
                Console.WriteLine("  no valid measurement");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary (best per card, MPixel/s) ===");
        foreach (var (shortName, best) in bestPerCard)
            Console.WriteLine($"  {shortName}: {best:0.#}");
    }

    // Standard triple test on every available CUDA device (or only on the
    // given one): per device runs the standardized benchmark in float 32-bit and
    // in double 64-bit and prints the values in MPixel/s with the best one —
    // the measure used for the benchmark graph history. CUDA counterpart of BenchDx.
    // Param deviceName (string?): Input, device filter (exact CUDA name); null = all devices.
    // Param runs (int): Input, repetitions per device and precision.
    // Param budget (TimeSpan): Input, time budget of each single run.
    // Param csvPath (string?): Input, CSV file for one row per run; null = no CSV output.
    public static void BenchCuda(string? deviceName, int runs, TimeSpan budget, string? csvPath = null)
    {
        Console.WriteLine($"Standardized CUDA benchmark: {runs} runs of {budget.TotalSeconds:0} s per device, " +
            $"area {BenchmarkStandard.Width}x{BenchmarkStandard.Height} AA{BenchmarkStandard.Aa} " +
            $"({BenchmarkStandard.PixelsPerFrame / 1e6:0.##} MPixel/frame), " +
            $"{BenchmarkStandard.MaxIter} iter, iterations only (float 32-bit + double 64-bit).");

        IReadOnlyList<string> devices = deviceName != null
            ? new[] { deviceName }
            : GpuMandelbrot.DeviceNames();
        if (devices.Count == 0)
        {
            Console.WriteLine("No CUDA device available. (" + GpuMandelbrot.LastError + ")");
            return;
        }

        var bestPerDevice = new List<(string Short, double Best32, double Best64)>();
        foreach (string device in devices)
        {
            string shortName = GpuMandelbrot.Shorten(device);
            Console.WriteLine();
            Console.WriteLine($"=== {shortName} ===");
            if (!GpuMandelbrot.TryInitialize(device))
            {
                Console.WriteLine("  init failed: " + GpuMandelbrot.LastError);
                continue;
            }

            double best32 = 0, best64 = 0;
            foreach (bool useDouble in new[] { false, true })
            {
                string tag = useDouble ? "64-bit" : "32-bit";
                double m = RunTrials("CUDA", shortName, tag, runs, csvPath, () =>
                {
                    var (_, seconds, frames) = GpuMandelbrot.BenchmarkGpu(
                        BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                        BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter,
                        BenchmarkStandard.Aa, useDouble, budget, null, CancellationToken.None);
                    return (frames, seconds);
                }, tag + " ");
                if (useDouble) best64 = m; else best32 = m;
            }
            GpuMandelbrot.Dispose();

            if (best32 > 0 || best64 > 0)
            {
                bestPerDevice.Add((shortName, best32, best64));
                Console.WriteLine($"  BEST 32-bit: {best32:0.#} MPixel/s");
                Console.WriteLine($"  BEST 64-bit: {best64:0.#} MPixel/s");
            }
            else
            {
                Console.WriteLine("  no valid measurement");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary (best per device, MPixel/s) ===");
        foreach (var (shortName, best32, best64) in bestPerDevice)
            Console.WriteLine($"  {shortName}: 32-bit {best32:0.#} | 64-bit {best64:0.#}");
    }

    // CPU model name (from registry, without suffixes), e.g.
    // "AMD Ryzen 9 9900X" or "Intel Core i7-14700K".
    public static string CpuName()
    {
        try
        {
            string full = (Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString", "") as string ?? "").Trim();
            if (full.Length == 0) return "CPU";
            int at = full.IndexOf(" @ ", StringComparison.Ordinal);
            if (at > 0) full = full[..at];
            return full.Replace("(R)", "").Replace("(TM)", "")
                .Replace(" Processor", "").Replace(" 12-Core", "").Replace(" 16-Core", "")
                .Replace(" 8-Core", "").Replace(" 6-Core", "").Replace(" 24-Core", "")
                .Replace(" 32-Core", "").Replace("  ", " ").Trim();
        }
        catch
        {
            return "CPU";
        }
    }

    // Standard triple test on the CPU: runs the standardized benchmark
    // (Mandelbrot.BenchmarkCpu double or BenchmarkCpuFloat) and prints the values
    // in MPixel/s with the best one — the measure used for the CPU history of the
    // benchmark graph (with model name).
    // Param runs (int): Input, repetitions.
    // Param budget (TimeSpan): Input, time budget of each single run.
    // Param csvPath (string?): Input, CSV file for one row per run; null = no CSV output.
    // Param useFloat (bool): Input, true for the float workload (own history, like
    //   CUDA 32-bit), false for double (default, the GUI benchmark workload).
    public static void BenchCpu(int runs, TimeSpan budget, string? csvPath = null, bool useFloat = false)
    {
        string precision = useFloat ? "float" : "double";
        Console.WriteLine($"Standardized CPU benchmark ({CpuName()}): {runs} runs of {budget.TotalSeconds:0} s, " +
            $"area {BenchmarkStandard.Width}x{BenchmarkStandard.Height} AA{BenchmarkStandard.Aa} " +
            $"({BenchmarkStandard.PixelsPerFrame / 1e6:0.##} MPixel/frame), " +
            $"{BenchmarkStandard.MaxIter} iter, {precision}, iterations only.");

        double best = RunTrials("CPU", CpuName(), precision, runs, csvPath, () =>
        {
            var (_, seconds, frames) = useFloat
                ? Mandelbrot.BenchmarkCpuFloat(
                    BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                    BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter,
                    BenchmarkStandard.Aa, budget, null, CancellationToken.None)
                : Mandelbrot.BenchmarkCpu(
                    BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                    BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter,
                    BenchmarkStandard.Aa, budget, null, CancellationToken.None);
            return (frames, seconds);
        });

        Console.WriteLine();
        Console.WriteLine($"=== Summary (best, MPixel/s): {CpuName()}: {best:0.#} ===");
    }

    // Writes a CSV row if requested (non-fatal errors: warn and continue).
    private static void WriteCsvRow(string? csvPath, string engine, string device, string precision,
        int run, int frames, double seconds, double mps)
    {
        if (csvPath == null) return;
        try
        {
            BenchmarkCsv.AppendRow(csvPath, engine, device, precision, run, frames, seconds, mps);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CSV not written ({csvPath}): {ex.Message}");
        }
    }
}
