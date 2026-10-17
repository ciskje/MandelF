namespace MandelbrotViewer;

// GPU engine, API-agnostic contract §4.1, backed by OpenGL 4.1 (the GPU API
// the macOS platform provides, §1.6) through GlEngine (offscreen).
// Precision: 32-bit float only — macOS OpenGL has no FP64 and the Metal route
// is blocked — so 64-bit is exposed as unavailable, which the contract allows
// ("a GPU implementation that only supports float may expose 64-bit as
// unavailable"). The UI offers only the 32-bit choice with the GPU engine;
// double requests fail fast with a readable error (fallback to CPU, §4.9).
public sealed class GpuEngine : IDisposable
{
    private GlEngine? _gl;
    private readonly object _gate = new();

    public string LastError { get; private set; } = "Not initialized.";
    public string DeviceShortName { get; private set; } = "";
    public string Details { get; private set; } = "Not initialized";
    public bool IsReady { get; private set; }
    public (int X, int Y) TuneSplit { get; private set; } = (1, 1);

    public static string[] AdapterNames()
    {
        try
        {
            var r = GlEngine.ProbeRenderer();
            if (string.IsNullOrWhiteSpace(r)) return Array.Empty<string>();
            return new[] { r };
        }
        catch { return Array.Empty<string>(); }
    }

    public static string Shorten(string name)
    {
        foreach (var p in new[] { "GeForce ", "Radeon " })
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return name[p.Length..].Trim();
        return name;
    }

    public bool TryInitialize(string? deviceName)
    {
        lock (_gate)
        {
            try
            {
                GpuViewParams.AssertSize();
                DisposeGl();
                var gl = new GlEngine();
                gl.Initialize();
                string renderer = gl.Renderer;
                if (!string.IsNullOrEmpty(deviceName) && deviceName != renderer)
                {
                    LastError = $"GPU device '{deviceName}' not found (available: '{renderer}').";
                    gl.Dispose();
                    IsReady = false;
                    return false;
                }
                // Auto = the single device (most capable by construction).
                TuneSplit = gl.TuneBlocks();
                DeviceShortName = Shorten(renderer);
                GlEngine.NoteRenderer(renderer);
                ulong memMB = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576;
                Details = $"GL {gl.GlVersion}, {renderer}, {memMB} MB, split {TuneSplit.X}x{TuneSplit.Y} (32-bit)";
                _gl = gl;
                IsReady = true;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                DisposeGl();
                IsReady = false;
                return false;
            }
        }
    }

    private void NeedReady(bool wantDouble)
    {
        if (!IsReady || _gl is null)
            throw new InvalidOperationException("GPU engine not initialized" + (string.IsNullOrEmpty(LastError) ? "." : $": {LastError}"));
        if (wantDouble)
            throw new InvalidOperationException(
                "GPU double precision is unavailable on this platform (32-bit only) — falling back to CPU.");
    }

    // Whole-frame render into ARGB buffer (§4.6). Returns whether double was used.
    public (int[] Pixels, bool UsedDouble) Render(int w, int h, double cx, double cy,
        double scale, int maxIter, Palette palette, int k, CancellationToken ct,
        bool julia, double jcx, double jcy, bool useDouble)
    {
        lock (_gate)
        {
            NeedReady(useDouble);
            var px = _gl!.Render(w, h, cx, cy, scale, maxIter, palette, k, julia, jcx, jcy,
                0, 0, w, h, ct);
            return (px, false);
        }
    }

    public int[] RenderTile(int w, int h, double cx, double cy, double scale, int maxIter,
        Palette palette, int k, CancellationToken ct, int offsetX, int offsetY,
        int fullWidth, int fullHeight, double jcx, double jcy, bool julia, bool useDouble)
    {
        lock (_gate)
        {
            NeedReady(useDouble);
            return _gl!.Render(w, h, cx, cy, scale, maxIter, palette, k, julia, jcx, jcy,
                offsetX, offsetY, fullWidth, fullHeight, ct);
        }
    }

    // Offscreen colored render for preview/export (§4.7).
    public int[]? RenderPreviewToImage(int w, int h, double cx, double cy, double scale,
        int maxIter, Palette palette, int k, bool julia, double jcx, double jcy,
        int fullW, int fullH, int offsetX, int offsetY, bool useDouble)
    {
        try
        {
            lock (_gate)
            {
                NeedReady(useDouble);
                return _gl!.Render(w, h, cx, cy, scale, maxIter, palette, k, julia, jcx, jcy,
                    offsetX, offsetY, fullW, fullH, CancellationToken.None);
            }
        }
        catch { return null; }
    }

    // Validation readback of raw per-sample iteration counts (§4.3 bench shape).
    public uint[] ReadCounts(int w, int h, double cx, double cy, double scale, int maxIter)
    {
        lock (_gate)
        {
            NeedReady(false);
            return _gl!.ReadCounts(w, h, cx, cy, scale, maxIter);
        }
    }

    // Standardized iterations-only benchmark, 8 s budget (§4.8).
    public (double Seconds, int Frames) Benchmark(int w, int h, int maxIter,
        CancellationToken ct, Action<double>? tick)
    {
        lock (_gate)
        {
            NeedReady(false);
            return _gl!.Benchmark(w, h, BenchmarkStandard.CenterX, BenchmarkStandard.CenterY,
                BenchmarkStandard.Scale, maxIter, ct, tick);
        }
    }

    private void DisposeGl()
    {
        try { _gl?.Dispose(); } catch { }
        _gl = null;
    }

    public void Dispose()
    {
        lock (_gate) { DisposeGl(); } // best-effort, never throws
    }
}
