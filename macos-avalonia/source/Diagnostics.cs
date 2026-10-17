using System.Runtime.InteropServices;

namespace MandelbrotViewer;

// CLI diagnostics helpers (§6.6). GPU enumeration deferred to GpuEngine (Fase 5).
public static class Diagnostics
{
    public static string CpuName()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("sysctl", "-n machdep.cpu.brand_string")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p is not null)
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { }
        }
        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    public static string BuildDiagnosticLog(string engineSelected, string engineInUse,
        GpuEngine gpu, AppSettings saved)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Mandelbrot Set Viewer   v{AppVersion.Full}  {DateTime.Now}");
        sb.AppendLine($"{RuntimeInformation.OSDescription}  {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine("=== Engine ===");
        sb.AppendLine($"Selected: {engineSelected}  In use: {engineInUse}");
        sb.AppendLine("=== GPU engine ===");
        sb.AppendLine($"Ready: {(gpu.IsReady ? "yes" : "no")}");
        sb.AppendLine($"Device: {gpu.DeviceShortName}");
        sb.AppendLine($"LastError: {gpu.LastError}");
        sb.AppendLine($"Devices: {string.Join(", ", GpuEngine.AdapterNames())}");
        sb.AppendLine($"Details: {gpu.Details}");
        sb.AppendLine("=== GPU selection ===");
        sb.AppendLine($"Gpu: {(string.IsNullOrEmpty(saved.Gpu) ? "Auto" : saved.Gpu)}  Precision: {(saved.Single ? "32-bit" : "64-bit")}");
        sb.AppendLine("=== Saved settings ===");
        sb.AppendLine($"Engine={saved.Engine} Palette={saved.Palette} AA={saved.AaIndex} Iter={saved.MaxIter} Auto={saved.IterAuto} Julia={saved.Julia} c={saved.Jcx} {saved.Jcy}i");
        sb.AppendLine("=== Event log ===");
        var lines = AppLog.Snapshot();
        sb.AppendLine(lines.Length == 0 ? "(empty)" : string.Join(Environment.NewLine, lines));
        return sb.ToString();
    }

    // ---- CLI (§6.6): --diag-gpu [device], --bench-gpu [device] [--csv f],
    // --bench-cpu [float] [--csv f]. Returns process exit code. Never opens UI.
    // Plus headless helper --render-png (verification/icon art, same engine):
    //   --render-png OUT W H CX CY SCALE MAXITER PALETTE AA [float|double] [julia JCX JCY]
    public static int RunCli(string[] args)
    {
        string? csv = null;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--csv") csv = args[i + 1];
        // First positional after the flag = device (for --bench-cpu: precision mode).
        string? positional = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && a != csv);

        if (args[0] == "--diag-gpu")
            return DiagGpu(positional);
        if (args[0] == "--bench-gpu")
            return BenchGpu(positional, csv);
        if (args[0] == "--bench-cpu")
            return BenchCpu(positional, csv);
        if (args[0] == "--render-png")
            return RenderPng(args.Skip(1).ToArray());
        if (args[0] == "--bench-check")
            return BenchCheck(args.Skip(1).ToArray());
        Console.Error.WriteLine($"Unknown command '{args[0]}'. Expected --diag-gpu, --bench-gpu, --bench-cpu, --render-png, --bench-check.");
        return 2;
    }

    // Proof-of-work check: read raw iteration counts from the GPU benchmark
    // kernel and compare against the CPU float escape loop on the same grid
    // (same mapping, no early-out on either side). Any skipped/shortcut work
    // shows up as massive mismatch.
    private static int BenchCheck(string[] a)
    {
        int w = 96, h = 60;
        double scale = 2.0e-04; // default: deep-zoom chaotic zone (demanding counts)
        int mi = 10000;
        if ((a.Length >= 1 && !int.TryParse(a[0], out w)) ||
            (a.Length >= 2 && !int.TryParse(a[1], out h)) ||
            (a.Length >= 3 && !double.TryParse(a[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out scale)) ||
            (a.Length >= 4 && !int.TryParse(a[3], out mi)))
        {
            Console.Error.WriteLine("usage: --bench-check [W H [SCALE [MAXITER]]]");
            return 2;
        }
        const double cx = BenchmarkStandard.CenterX, cy = BenchmarkStandard.CenterY;
        using var gpu = new GpuEngine();
        if (!gpu.TryInitialize(null)) { Console.Error.WriteLine("GPU init: " + gpu.LastError); return 1; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        uint[] g = gpu.ReadCounts(w, h, cx, cy, scale, mi);
        double gpuMs = sw.Elapsed.TotalMilliseconds;
        double px = scale / w;
        double top = cy - h * 0.5 * px;
        long exact = 0;
        long absDiff = 0;
        long cpuSum = 0;
        uint maxDiff = 0;
        sw.Restart();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float ccx = (float)(cx + (x - w * 0.5) * px);
                float ccy = (float)(top + y * px);
                int it = Mandelbrot.EscapeFloat(ccx, ccy, 0, 0, mi).Iter;
                cpuSum += it;
                uint gi = g[y * w + x];
                if ((uint)it == gi) exact++;
                uint d = (uint)Math.Abs(it - (int)gi);
                absDiff += d;
                if (d > maxDiff) maxDiff = d;
            }
        double cpuMs = sw.Elapsed.TotalMilliseconds;
        long n = (long)w * h;
        Console.WriteLine($"grid {w}x{h} ({n} samples), maxIter {mi}, scale {scale:e}");
        Console.WriteLine($"exact match: {exact}/{n} ({100.0 * exact / n:0.00}%)");
        Console.WriteLine($"mean abs diff: {(double)absDiff / n:0.000} iters, max diff: {maxDiff}");
        Console.WriteLine($"GPU readback: {gpuMs:0} ms, CPU float loop: {cpuMs:0} ms");
        Console.WriteLine($"mean GPU count: {g.Average(v => (double)v):0} / mean CPU count: {(double)cpuSum / n:0}");
        return exact * 100 / n >= 95 ? 0 : 1;
    }

    private static int RenderPng(string[] a)
    {
        // OUT W H CX CY SCALE MAXITER PALETTE AA [float|double] [julia JCX JCY]
        try
        {
            if (a.Length < 9)
            {
                Console.Error.WriteLine("usage: --render-png OUT W H CX CY SCALE MAXITER PALETTE AA [float|double] [julia JCX JCY]");
                return 2;
            }
            string outPath = a[0];
            int w = int.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            int h = int.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
            double cx = double.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
            double cy = double.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture);
            double scale = double.Parse(a[5], System.Globalization.CultureInfo.InvariantCulture);
            int maxIter = int.Parse(a[6], System.Globalization.CultureInfo.InvariantCulture);
            if (!Enum.TryParse<Palette>(a[7], ignoreCase: true, out var pal)) return 2;
            int k = int.Parse(a[8], System.Globalization.CultureInfo.InvariantCulture);
            bool useDouble = true;
            bool julia = false;
            double jcx = 0, jcy = 0;
            bool useGpu = false;
            for (int i = 9; i < a.Length; i++)
            {
                if (a[i] == "float") useDouble = false;
                else if (a[i] == "double") useDouble = true;
                else if (a[i] == "gpu") useGpu = true;
                else if (a[i] == "julia" && i + 2 < a.Length)
                {
                    julia = true;
                    jcx = double.Parse(a[++i], System.Globalization.CultureInfo.InvariantCulture);
                    jcy = double.Parse(a[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            if (w < 1 || h < 1 || w > 16384 || h > 16384 || k < 1 || k > 8) return 2;
            int[] buf;
            if (useGpu)
            {
                if (useDouble) { Console.Error.WriteLine("GPU is 32-bit only; add 'float'."); return 2; }
                using var gpu = new GpuEngine();
                if (!gpu.TryInitialize(null)) { Console.Error.WriteLine("GPU init: " + gpu.LastError); return 1; }
                buf = gpu.RenderTile(w, h, cx, cy, scale, maxIter, pal, k, CancellationToken.None,
                    0, 0, w, h, jcx, jcy, julia, useDouble: false);
            }
            else
            {
                buf = new int[w * h];
                Mandelbrot.RenderInto(buf, w, h, cx, cy, scale, maxIter, pal, k,
                    CancellationToken.None, 0, 0, w, h, jcx, jcy, julia, useDouble);
            }
            PngWriter.WriteArgb(outPath, buf, w, h);
            Console.WriteLine($"wrote {outPath} ({w}x{h})");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"render-png failed: {ex.Message}");
            return 1;
        }
    }

    private static int DiagGpu(string? device)
    {
        try
        {
            string[] names;
            try { names = GpuEngine.AdapterNames(); }
            catch (Exception ex)
            {
                Console.WriteLine($"Device enumeration error: {ex.Message}");
                names = Array.Empty<string>();
            }
            Console.WriteLine($"Devices ({names.Length}):");
            foreach (var n in names) Console.WriteLine($"  {n}");
            using var gpu = new GpuEngine();
            bool ok = gpu.TryInitialize(device);
            Console.WriteLine($"IsReady: {ok}");
            Console.WriteLine($"Device: {gpu.DeviceShortName}");
            Console.WriteLine($"LastError: {gpu.LastError}");
            Console.WriteLine($"Details: {gpu.Details}");
            if (ok)
            {
                // Headless test render 800x600, 200 iterations (§6.6).
                int[] px = new int[800 * 600];
                Mandelbrot.RenderInto(px, 800, 600, Mandelbrot.InitialCenterX,
                    Mandelbrot.InitialCenterY, Mandelbrot.InitialScale, 200,
                    Palette.Fire, 1, CancellationToken.None, 0, 0, 800, 600, 0, 0, false, true);
                Console.WriteLine($"Test render 800x600 @200iter: ok, sample=0x{px[300 * 800 + 400]:X8}");
            }
            // diag_gpu.txt next to the executable (§6.6).
            try
            {
                string dir = AppContext.BaseDirectory;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"devices: {string.Join("; ", names)}");
                sb.AppendLine($"ready: {ok}");
                sb.AppendLine($"device: {gpu.DeviceShortName}");
                sb.AppendLine($"details: {gpu.Details}");
                sb.AppendLine($"error: {gpu.LastError}");
                File.WriteAllText(Path.Combine(dir, "diag_gpu.txt"), sb.ToString());
            }
            catch (Exception ex) { Console.WriteLine($"(could not write diag_gpu.txt: {ex.Message})"); }
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"diag-gpu failed: {ex}");
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diag_gpu.txt"),
                    $"exception: {ex}");
            }
            catch { }
            return 1;
        }
    }

    private static int BenchGpu(string? device, string? csv)
    {
        // Standardized benchmark, 3 runs of the 8 s budget per device (§6.6),
        // offscreen (no present). GPU backend is 32-bit float only.
        string[] devices;
        try { devices = GpuEngine.AdapterNames(); }
        catch (Exception ex)
        {
            Console.WriteLine($"Device enumeration error: {ex.Message}");
            return 1;
        }
        if (!string.IsNullOrEmpty(device))
        {
            if (!devices.Contains(device))
            {
                Console.WriteLine($"GPU device '{device}' not found. Available: {string.Join(", ", devices)}");
                return 1;
            }
            devices = new[] { device };
        }
        if (devices.Length == 0)
        {
            Console.WriteLine("No GPU devices available.");
            return 1;
        }
        int rc = 0;
        foreach (var dev in devices)
        {
            using var gpu = new GpuEngine();
            if (!gpu.TryInitialize(dev))
            {
                Console.WriteLine($"GPU '{dev}': init failed: {gpu.LastError}");
                rc = 1;
                continue;
            }
            Console.WriteLine($"GPU benchmark (32-bit) on '{gpu.DeviceShortName}': 3 runs x {BenchmarkStandard.BudgetSeconds}s, " +
                $"{BenchmarkStandard.W}x{BenchmarkStandard.H} AA1x, iter={BenchmarkStandard.Iterations}");
            double best = 0;
            for (int run = 1; run <= 3; run++)
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                try
                {
                    var (secs, frames) = gpu.Benchmark(BenchmarkStandard.W, BenchmarkStandard.H,
                        BenchmarkStandard.Iterations, cts.Token,
                        s => Console.Write($"\rrun {run}: {100 * s / BenchmarkStandard.BudgetSeconds:0}% "));
                    Console.WriteLine();
                    double mpixel = frames * (double)BenchmarkStandard.SamplesPerFrame / Math.Max(secs, 1e-9) / 1e6;
                    best = Math.Max(best, mpixel);
                    Console.WriteLine($"run {run}: {mpixel:0.#} Mpixel/s ({frames} frames in {secs:0.00} s)");
                    if (!string.IsNullOrEmpty(csv))
                        BenchmarkCsv.AppendRow(csv, BenchmarkCsv.FormatRow(DateTime.UtcNow, "GPU",
                            gpu.DeviceShortName, "32-bit", run, frames, secs, mpixel));
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"run {run}: cancelled.");
                    return 130;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"run {run}: {ex.Message}");
                    rc = 1;
                }
            }
            Console.WriteLine($"best on {gpu.DeviceShortName}: {best:0.#} Mpixel/s");
        }
        return rc;
    }

    private static int BenchCpu(string? mode, string? csv)
    {
        bool useFloat = string.Equals(mode, "float", StringComparison.OrdinalIgnoreCase);
        if (mode is not null && !useFloat)
        {
            Console.Error.WriteLine($"--bench-cpu: unknown mode '{mode}' (expected 'float' or nothing).");
            return 2;
        }
        Console.WriteLine($"CPU benchmark ({(useFloat ? "float" : "double")}): 3 runs x {BenchmarkStandard.BudgetSeconds}s, " +
            $"{BenchmarkStandard.W}x{BenchmarkStandard.H} AA1x, iter={BenchmarkStandard.Iterations}, scale={BenchmarkStandard.Scale:e}");
        string device = CpuName();
        double best = 0;
        for (int run = 1; run <= 3; run++)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            try
            {
                var (total, secs, frames) = useFloat
                    ? Mandelbrot.BenchmarkCpuFloat(BenchmarkStandard.W, BenchmarkStandard.H,
                        BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                        BenchmarkStandard.Iterations, cts.Token, null)
                    : Mandelbrot.BenchmarkCpu(BenchmarkStandard.W, BenchmarkStandard.H,
                        BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                        BenchmarkStandard.Iterations, cts.Token, null);
                double mpixel = frames * (double)BenchmarkStandard.SamplesPerFrame / Math.Max(secs, 1e-9) / 1e6;
                best = Math.Max(best, mpixel);
                Console.WriteLine($"run {run}: {mpixel:0.#} Mpixel/s ({frames} frames in {secs:0.00} s, totalIters={total})");
                if (!string.IsNullOrEmpty(csv))
                    BenchmarkCsv.AppendRow(csv, BenchmarkCsv.FormatRow(DateTime.UtcNow, "CPU", device,
                        useFloat ? "float" : "64-bit", run, frames, secs, mpixel));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"run {run}: cancelled.");
                return 130;
            }
        }
        Console.WriteLine($"best: {best:0.#} Mpixel/s on {device}");
        return 0;
    }
}
