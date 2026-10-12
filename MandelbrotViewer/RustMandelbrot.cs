namespace MandelbrotViewer;

using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Rust CPU kernel (mandel-ffi.dll, built from MandelR's mandel-core):
// same formulas/geometry/LUT as the C# scalar path, SIMD dispatch
// (AVX512 > AVX2 > scalar) inside the DLL. The UI keeps its own
// progress/cancellation by calling once per band (view/export) or once per
// frame (benchmark); tiles are pixel-identical to whole-frame renders.
internal static class RustMandelbrot
{
    private const string Dll = "mandel_ffi";
    private const int BandRows = 128;

    [StructLayout(LayoutKind.Sequential)]
    private struct ViewParams
    {
        public double Cx, Cy, Scale;
        public uint MaxIter, Ssaa, Palette, DoublePrecision;
        public uint JuliaEnabled;
        public double Jcx, Jcy;
        public uint TileW, TileH, FullW, FullH, OffX, OffY;
    }

    private static class Native
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mandel_render_tile(ref ViewParams p,
            [Out] int[] pixels); // ARGB bit patterns; int[] for Marshal.Copy

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mandel_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mandel_last_error(byte[] buf, uint len);
    }

    private static bool? _available;
    private static string _lastError = "";

    // True when the DLL loads and answers with a version string.
    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue) return _available.Value;
            try
            {
                string v = Marshal.PtrToStringAnsi(Native.mandel_version()) ?? "";
                _available = v.StartsWith("mandel-ffi", StringComparison.Ordinal);
                if (!_available.Value) _lastError = "unexpected version: " + v;
            }
            catch (Exception ex)
            {
                _available = false;
                _lastError = ex.Message;
            }
            return _available.Value;
        }
    }

    public static string LastError
    {
        get
        {
            if (_lastError.Length > 0) return _lastError;
            try
            {
                var buf = new byte[512];
                Native.mandel_last_error(buf, (uint)buf.Length);
                int n = Array.IndexOf(buf, (byte)0);
                return System.Text.Encoding.UTF8.GetString(buf, 0, n < 0 ? buf.Length : n);
            }
            catch
            {
                return "Rust render failed.";
            }
        }
    }

    // Full-frame render into a bitmap (banded so the caller stays responsive).
    // Param bmp (Bitmap): Input/output: destination, Format32bppArgb, overwritten in place.
    // Param ct: Input: cancellation token; checked between bands.
    public static void Render(Bitmap bmp, double centerX, double centerY, double scale, int maxIter,
        Palette palette, int supersample, CancellationToken ct,
        double juliaCx = 0, double juliaCy = 0, bool julia = false, bool useDouble = true)
    {
        RenderTile(bmp, centerX, centerY, scale, maxIter, palette, supersample, ct,
            0, 0, bmp.Width, bmp.Height, juliaCx, juliaCy, julia, useDouble);
    }

    // One output tile with global image coordinates (mirrors Mandelbrot.RenderTile).
    public static void RenderTile(Bitmap bmp, double centerX, double centerY, double scale, int maxIter,
        Palette palette, int supersample, CancellationToken ct,
        int offsetX, int offsetY, int fullWidth, int fullHeight,
        double juliaCx = 0, double juliaCy = 0, bool julia = false, bool useDouble = true)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        if (w <= 0 || h <= 0) return;

        var rect = new Rectangle(0, 0, w, h);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y0 = 0; y0 < h; y0 += BandRows)
            {
                ct.ThrowIfCancellationRequested();
                int bh = Math.Min(BandRows, h - y0);
                var p = new ViewParams
                {
                    Cx = centerX,
                    Cy = centerY,
                    Scale = scale,
                    MaxIter = (uint)Math.Max(1, maxIter),
                    Ssaa = (uint)Math.Max(1, supersample),
                    Palette = (uint)palette,
                    DoublePrecision = useDouble ? 1u : 0u,
                    JuliaEnabled = julia ? 1u : 0u,
                    Jcx = juliaCx,
                    Jcy = juliaCy,
                    TileW = (uint)w,
                    TileH = (uint)bh,
                    FullW = (uint)fullWidth,
                    FullH = (uint)fullHeight,
                    OffX = (uint)offsetX,
                    OffY = (uint)(offsetY + y0),
                };
                var band = new int[w * bh];
                int rc = Native.mandel_render_tile(ref p, band);
                if (rc != 0)
                    throw new InvalidOperationException($"Rust render failed (code {rc}): {LastError}");
                Marshal.Copy(band, 0, data.Scan0 + (nint)(y0 * data.Stride), band.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // Standardized benchmark loop (same contract as Mandelbrot.BenchmarkCpu):
    // full-frame FFI calls repeat until the budget elapses; the DLL
    // parallelizes inside each frame. TotalIters is 0 (the FFI returns pixels,
    // not iteration counts); frames/seconds drive the MPixel/s metric.
    public static (long TotalIters, double Seconds, int Frames) BenchmarkRust(
        double centerX, double centerY, double scale, int w, int h, int maxIter, int supersample,
        bool useDouble, TimeSpan budget, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var p = new ViewParams
        {
            Cx = centerX,
            Cy = centerY,
            Scale = scale,
            MaxIter = (uint)Math.Max(1, maxIter),
            Ssaa = (uint)Math.Max(1, supersample),
            Palette = (uint)Palette.Fire,
            DoublePrecision = useDouble ? 1u : 0u,
            JuliaEnabled = 0,
            Jcx = 0,
            Jcy = 0,
            TileW = (uint)w,
            TileH = (uint)h,
            FullW = (uint)w,
            FullH = (uint)h,
            OffX = 0,
            OffY = 0,
        };
        var frame = new int[w * h];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        bool first = true;
        TimeSpan lastReport = TimeSpan.Zero;
        while (sw.Elapsed < budget)
        {
            ct.ThrowIfCancellationRequested();
            int rc = Native.mandel_render_tile(ref p, frame);
            if (rc != 0)
                throw new InvalidOperationException($"Rust benchmark failed (code {rc}): {LastError}");
            frames++;
            if (first || sw.Elapsed - lastReport >= BenchmarkProgress.ReportInterval)
            {
                progress?.Report(new BenchmarkProgress(sw.Elapsed.TotalSeconds, 0, frames));
                lastReport = sw.Elapsed;
                first = false;
            }
        }
        progress?.Report(new BenchmarkProgress(sw.Elapsed.TotalSeconds, 0, frames));
        return (0, sw.Elapsed.TotalSeconds, frames);
    }
}
