using System.Collections.Concurrent;
using System.Numerics;

namespace MandelbrotViewer;

// Shared math + constants (§2). CPU render paths (§3) below.
public static class Mandelbrot
{
    // View / iteration constants (§2.2-2.3, §8 spot values)
    public const double InitialCenterX = -0.5;
    public const double InitialCenterY = 0.0;
    public const double InitialScale = 9.36;
    public const double DefaultJcx = -0.7;
    public const double DefaultJcy = 0.27015;
    public const double PaletteGamma = 0.35;
    public const int MinIter = 50;
    public const int MaxIter = 50000;
    public const int DefaultIter = 256;
    public const int IterStep = 50;
    public const double FloatPrecisionLimit = 1e-3;
    public const double BulbRadiusSq = 0.0625;
    public static readonly uint BlackArgb = 0xFF000000;

    // AutoIterForScale(scale) = clamp(2000*(1+log10(1.5/(scale/2))),50,50000) (§2.3)
    public static int AutoIterForScale(double scale)
    {
        double half = Math.Max(double.Epsilon, scale * 0.5);
        double v = 2000.0 * (1.0 + Math.Log10(1.5 / half));
        if (v < MinIter) return MinIter;
        if (v > MaxIter) return MaxIter;
        return (int)v;
    }

    // Escape loop, mandatory incremental-squares form (§2.1). Returns iter + |z|².
    public static (int Iter, double MagSq) Escape(double ccx, double ccy, double z0x, double z0y, int maxIter)
    {
        double zx = z0x, zy = z0y;
        double zx2 = zx * zx, zy2 = zy * zy;
        int iter = 0;
        while (iter < maxIter && zx2 + zy2 <= 4.0)
        {
            zy = 2.0 * zx * zy + ccy;
            zx = zx2 - zy2 + ccx;
            zx2 = zx * zx;
            zy2 = zy * zy;
            iter++;
        }
        return (iter, zx2 + zy2);
    }

    public static (int Iter, float MagSq) EscapeFloat(float ccx, float ccy, float z0x, float z0y, int maxIter)
    {
        float zx = z0x, zy = z0y;
        float zx2 = zx * zx, zy2 = zy * zy;
        int iter = 0;
        while (iter < maxIter && zx2 + zy2 <= 4.0f)
        {
            zy = 2.0f * zx * zy + ccy;
            zx = zx2 - zy2 + ccx;
            zx2 = zx * zx;
            zy2 = zy * zy;
            iter++;
        }
        return (iter, zx2 + zy2);
    }

    // Smooth coloring (§2.4): smooth = iter+1-log2(0.5*ln(max(mod,4))).
    public static double Smooth(int iter, double magSq) =>
        iter + 1.0 - Math.Log2(0.5 * Math.Log(Math.Max(magSq, 4.0)));

    public static double SmoothFloat(int iter, float magSq) =>
        iter + 1.0 - Math.Log2(0.5 * Math.Log(Math.Max(magSq, 4.0f)));

    // Interior early-out, Mandelbrot only (§2.5). Conservative exact math.
    public static bool IsInterior(double px, double py)
    {
        double q = (px - 0.25) * (px - 0.25) + py * py;
        if (q * (q + (px - 0.25)) <= 0.25 * py * py)
            return true;
        double xm1 = px + 1.0;
        if (xm1 * xm1 + py * py <= BulbRadiusSq)
            return true;
        return false;
    }

    // View math (§2.2): whole-image grid, tile offsets for tiling invariance (§1.5.2).
    public static (double PixelSize, double TopY, double OriginX) ViewMath(
        double cx, double cy, double scale, int fullW, int fullH, int k)
    {
        double pixelSize = scale / (fullW * k);
        double topY = cy - (fullH * k * 0.5) * pixelSize;
        double originX = cx - (fullW * k * 0.5) * pixelSize;
        return (pixelSize, topY, originX);
    }

    public static (double Px, double Py) SubsamplePoint(
        double originX, double topY, double pixelSize,
        int offsetX, int offsetY, int x, int y, int k, int sx, int sy)
    {
        long globalBx = (long)(offsetX + x) * k + sx;
        long globalBy = (long)(offsetY + y) * k + sy;
        return (originX + globalBx * pixelSize, topY + globalBy * pixelSize);
    }

    // Zoom around complex point p (§5.5).
    public static (double Cx, double Cy, double Scale) Zoom(
        double cx, double cy, double scale, double px, double py, double factor) =>
        (px + (cx - px) * factor, py + (cy - py) * factor, scale * factor);

    // ---- Render entry-point contract (§2.8, §3) ----

    // Headless/testable core: renders into a raw 32-bit ARGB buffer (row-major,
    // stride in ints).
    public static void RenderInto(int[] pixels, int w, int h,
        double cx, double cy, double scale, int maxIter, Palette palette, int k,
        CancellationToken ct, int offsetX, int offsetY, int fullWidth, int fullHeight,
        double jcx, double jcy, bool julia, bool useDouble)
    {
        unsafe
        {
            fixed (int* basePtr = pixels)
                RenderCore(basePtr, w, w, h, cx, cy, scale, maxIter, palette, k,
                    ct, offsetX, offsetY, fullWidth, fullHeight, jcx, jcy, julia, useDouble);
        }
    }

    private static unsafe void RenderCore(int* dst, int stride, int w, int h,
        double cx, double cy, double scale, int maxIter, Palette palette, int k,
        CancellationToken ct, int offsetX, int offsetY, int fullWidth, int fullHeight,
        double jcx, double jcy, bool julia, bool useDouble)
    {
        int fw = fullWidth <= 0 ? w : fullWidth;
        int fh = fullHeight <= 0 ? h : fullHeight;
        var (pixelSize, topY, originX) = ViewMath(cx, cy, scale, fw, fh, k);
        int[] lut = PaletteColors.GetLut(palette, maxIter);
        double invMax = 1.0 / maxIter;
        bool simd = Vector.IsHardwareAccelerated;
        var opts = new ParallelOptions { CancellationToken = ct };
        int chunk = Math.Max(8, h / (Math.Max(1, Environment.ProcessorCount) * 8));
        var part = Partitioner.Create(0, h, chunk);

        Parallel.ForEach(part, opts, range =>
        {
            for (int y = range.Item1; y < range.Item2; y++)
            {
                opts.CancellationToken.ThrowIfCancellationRequested();
                int* row = dst + (nint)y * stride;
                if (useDouble)
                {
                    if (simd) RenderRowVectorDouble(row, y, w, originX, topY, pixelSize, offsetX, offsetY, k, maxIter, lut, invMax, jcx, jcy, julia);
                    else RenderRowScalar(row, y, w, originX, topY, pixelSize, offsetX, offsetY, k, maxIter, lut, invMax, jcx, jcy, julia);
                }
                else
                {
                    if (simd) RenderRowVectorFloat(row, y, w, originX, topY, pixelSize, offsetX, offsetY, k, maxIter, lut, invMax, jcx, jcy, julia);
                    else RenderRowScalarFloat(row, y, w, originX, topY, pixelSize, offsetX, offsetY, k, maxIter, lut, invMax, jcx, jcy, julia);
                }
            }
        });
    }

    private static unsafe void RenderRowScalar(int* row, int y, int w,
        double originX, double topY, double pixelSize, int offX, int offY, int k,
        int maxIter, int[] lut, double invMax, double jcx, double jcy, bool julia, int x0 = 0)
    {
        int div = k * k;
        for (int x = x0; x < w; x++)
        {
            long sr = 0, sg = 0, sb = 0;
            for (int sy = 0; sy < k; sy++)
                for (int sx = 0; sx < k; sx++)
                {
                    var (px, py) = SubsamplePoint(originX, topY, pixelSize, offX, offY, x, y, k, sx, sy);
                    int c = PixelDouble(px, py, maxIter, lut, invMax, jcx, jcy, julia);
                    sr += (c >> 16) & 0xFF; sg += (c >> 8) & 0xFF; sb += c & 0xFF;
                }
            row[x] = unchecked((int)0xFF000000) | ((int)(sr / div) << 16) | ((int)(sg / div) << 8) | (int)(sb / div);
        }
    }

    private static unsafe void RenderRowScalarFloat(int* row, int y, int w,
        double originX, double topY, double pixelSize, int offX, int offY, int k,
        int maxIter, int[] lut, double invMax, double jcx, double jcy, bool julia, int x0 = 0)
    {
        int div = k * k;
        for (int x = x0; x < w; x++)
        {
            long sr = 0, sg = 0, sb = 0;
            for (int sy = 0; sy < k; sy++)
                for (int sx = 0; sx < k; sx++)
                {
                    var (px, py) = SubsamplePoint(originX, topY, pixelSize, offX, offY, x, y, k, sx, sy);
                    int c = PixelFloat(px, py, maxIter, lut, invMax, (float)jcx, (float)jcy, julia);
                    sr += (c >> 16) & 0xFF; sg += (c >> 8) & 0xFF; sb += c & 0xFF;
                }
            row[x] = unchecked((int)0xFF000000) | ((int)(sr / div) << 16) | ((int)(sg / div) << 8) | (int)(sb / div);
        }
    }

    public static int PixelDouble(double px, double py, int maxIter, int[] lut,
        double invMax, double jcx, double jcy, bool julia)
    {
        if (!julia && IsInterior(px, py))
            return unchecked((int)BlackArgb);
        double z0x, z0y, ccx, ccy;
        if (julia) { z0x = px; z0y = py; ccx = jcx; ccy = jcy; }
        else { z0x = 0; z0y = 0; ccx = px; ccy = py; }
        var (iter, mag) = Escape(ccx, ccy, z0x, z0y, maxIter);
        if (iter >= maxIter) return unchecked((int)BlackArgb);
        return PaletteColors.ColorFromLut(lut, Smooth(iter, mag), invMax);
    }

    public static int PixelFloat(double pxd, double pyd, int maxIter, int[] lut,
        double invMax, float jcx, float jcy, bool julia)
    {
        // Interior test in double on double coords (host-order, §2.5); escape in float.
        if (!julia && IsInterior(pxd, pyd))
            return unchecked((int)BlackArgb);
        float ccx, ccy, z0x, z0y;
        if (julia) { z0x = (float)pxd; z0y = (float)pyd; ccx = jcx; ccy = jcy; }
        else { z0x = 0; z0y = 0; ccx = (float)pxd; ccy = (float)pyd; }
        var (iter, mag) = EscapeFloat(ccx, ccy, z0x, z0y, maxIter);
        if (iter >= maxIter) return unchecked((int)BlackArgb);
        return PaletteColors.ColorFromLut(lut, SmoothFloat(iter, mag), invMax);
    }

    // Vector double: blocks of Vector<double>.Count lanes + scalar tail (§3.2).
    // One subsample row per lane block; lane i at gridBase + i*k (exact integer
    // grid, one multiply + one add) → bit-identical to scalar path.
    private static unsafe void RenderRowVectorDouble(int* row, int y, int w,
        double originX, double topY, double pixelSize, int offX, int offY, int k,
        int maxIter, int[] lut, double invMax, double jcx, double jcy, bool julia)
    {
        int n = Vector<double>.Count;
        var vOriginX = new Vector<double>(originX);
        var vPixelSize = new Vector<double>(pixelSize);
        var vK = new Vector<double>(k);
        double[] tmpX = new double[n];
        int x = 0;
        int div = k * k;
        double[] laneIdx = new double[n];
        for (int i = 0; i < n; i++) laneIdx[i] = i;
        var vLane = new Vector<double>(laneIdx);
        for (; x + n <= w; x += n)
        {
            long[] sR = new long[n], sG = new long[n], sB = new long[n];
            for (int sy = 0; sy < k; sy++)
            {
                long globalBy = (long)(offY + y) * k + sy;
                double py = topY + globalBy * pixelSize;
                var vPy = new Vector<double>(py);
                for (int sx = 0; sx < k; sx++)
                {
                    long gridBase = (long)(offX + x) * k + sx;
                    // per-lane coords: originX + (gridBase + i*k)*pixelSize
                    var idx = vLane * vK + new Vector<double>((double)gridBase);
                    var vPx = vOriginX + idx * vPixelSize;
                    vPx.CopyTo(tmpX);
                    double[] pyArr = new double[n];
                    vPy.CopyTo(pyArr);
                    int[] iters = new int[n];
                    double[] mags = new double[n];
                    EscapeBlockDouble(vPx, vPy, iters, mags, maxIter, jcx, jcy, julia, tmpX, pyArr);
                    for (int i = 0; i < n; i++)
                    {
                        int c;
                        if (iters[i] >= maxIter) c = unchecked((int)BlackArgb);
                        else c = PaletteColors.ColorFromLut(lut, Smooth(iters[i], mags[i]), invMax);
                        sR[i] += (c >> 16) & 0xFF; sG[i] += (c >> 8) & 0xFF; sB[i] += c & 0xFF;
                    }
                }
            }
            for (int i = 0; i < n; i++)
                row[x + i] = unchecked((int)0xFF000000) | ((int)(sR[i] / div) << 16) | ((int)(sG[i] / div) << 8) | (int)(sB[i] / div);
        }
        // scalar tail (must agree lane-by-lane with vector core)
        RenderRowScalar(row, y, w, originX, topY, pixelSize, offX, offY, k, maxIter, lut, invMax, jcx, jcy, julia, x);
    }

    private static void EscapeBlockDouble(Vector<double> vPx, Vector<double> vPy,
        int[] iters, double[] mags, int maxIter, double jcx, double jcy, bool julia,
        double[] pxArr, double[] pyArr)
    {
        int n = Vector<double>.Count;
        Vector<double> ccx, ccy, zx, zy;
        var vJcx = new Vector<double>(jcx);
        var vJcy = new Vector<double>(jcy);
        if (julia) { zx = vPx; zy = vPy; ccx = vJcx; ccy = vJcy; }
        else { zx = Vector<double>.Zero; zy = Vector<double>.Zero; ccx = vPx; ccy = vPy; }
        var alive = Vector<double>.One;
        var iterV = Vector<double>.Zero;
        // interior lanes seeded alive=0, iter=maxIter (Mandelbrot only, test in double)
        if (!julia)
        {
            double[] a = new double[n];
            alive.CopyTo(a);
            double[] it = new double[n];
            for (int i = 0; i < n; i++)
                if (IsInterior(pxArr[i], pyArr[i])) { a[i] = 0; it[i] = maxIter; }
            alive = new Vector<double>(a);
            iterV = new Vector<double>(it);
        }
        var zx2 = zx * zx;
        var zy2 = zy * zy;
        var four = new Vector<double>(4.0);
        var two = new Vector<double>(2.0);
        var zero = Vector<double>.Zero;
        for (int iter = 0; iter < maxIter; iter++)
        {
            var mag = zx2 + zy2;
            var esc = Vector.GreaterThan(mag, four);
            alive = Vector.ConditionalSelect(esc, zero, alive);
            if (!Vector.GreaterThanAny(alive, zero))
            {
                // all escaped or interior: record mags for escaped lanes below
                break;
            }
            iterV += alive;
            var nzy = two * zx * zy + ccy;
            var nzx = zx2 - zy2 + ccx;
            var nzx2 = nzx * nzx;
            var nzy2 = nzy * nzy;
            var stillAlive = Vector.GreaterThan(alive, zero);
            zx = Vector.ConditionalSelect(stillAlive, nzx, zx);
            zy = Vector.ConditionalSelect(stillAlive, nzy, zy);
            zx2 = Vector.ConditionalSelect(stillAlive, nzx2, zx2);
            zy2 = Vector.ConditionalSelect(stillAlive, nzy2, zy2);
        }
        double[] itF = new double[n], mgF = new double[n];
        iterV.CopyTo(itF);
        (zx2 + zy2).CopyTo(mgF);
        for (int i = 0; i < n; i++) { iters[i] = (int)itF[i]; mags[i] = mgF[i]; }
    }

    private static unsafe void RenderRowVectorFloat(int* row, int y, int w,
        double originX, double topY, double pixelSize, int offX, int offY, int k,
        int maxIter, int[] lut, double invMax, double jcx, double jcy, bool julia)
    {
        int n = Vector<float>.Count;
        int x = 0;
        int div = k * k;
        float[] tmpPx = new float[n];
        for (; x + n <= w; x += n)
        {
            long[] sR = new long[n], sG = new long[n], sB = new long[n];
            for (int sy = 0; sy < k; sy++)
            {
                long globalBy = (long)(offY + y) * k + sy;
                double pyd = topY + globalBy * pixelSize;
                float py = (float)pyd;
                var vPy = new Vector<float>(py);
                for (int sx = 0; sx < k; sx++)
                {
                    long gridBase = (long)(offX + x) * k + sx;
                    for (int i = 0; i < n; i++)
                        tmpPx[i] = (float)(originX + (gridBase + (long)i * k) * pixelSize);
                    var vPx = new Vector<float>(tmpPx);
                    double[] pxd = new double[n];
                    for (int i = 0; i < n; i++) pxd[i] = originX + (gridBase + (long)i * k) * pixelSize;
                    double[] pydArr = new double[n];
                    for (int i = 0; i < n; i++) pydArr[i] = pyd;
                    int[] iters = new int[n];
                    float[] mags = new float[n];
                    EscapeBlockFloat(vPx, vPy, iters, mags, maxIter, (float)jcx, (float)jcy, julia, pxd, pydArr);
                    for (int i = 0; i < n; i++)
                    {
                        int c = iters[i] >= maxIter ? unchecked((int)BlackArgb)
                            : PaletteColors.ColorFromLut(lut, SmoothFloat(iters[i], mags[i]), invMax);
                        sR[i] += (c >> 16) & 0xFF; sG[i] += (c >> 8) & 0xFF; sB[i] += c & 0xFF;
                    }
                }
            }
            for (int i = 0; i < n; i++)
                row[x + i] = unchecked((int)0xFF000000) | ((int)(sR[i] / div) << 16) | ((int)(sG[i] / div) << 8) | (int)(sB[i] / div);
        }
        RenderRowScalarFloat(row, y, w, originX, topY, pixelSize, offX, offY, k, maxIter, lut, invMax, jcx, jcy, julia, x);
    }

    private static void EscapeBlockFloat(Vector<float> vPx, Vector<float> vPy,
        int[] iters, float[] mags, int maxIter, float jcx, float jcy, bool julia,
        double[] pxArr, double[] pyArr)
    {
        int n = Vector<float>.Count;
        Vector<float> ccx, ccy, zx, zy;
        var vJcx = new Vector<float>(jcx);
        var vJcy = new Vector<float>(jcy);
        if (julia) { zx = vPx; zy = vPy; ccx = vJcx; ccy = vJcy; }
        else { zx = Vector<float>.Zero; zy = Vector<float>.Zero; ccx = vPx; ccy = vPy; }
        var alive = Vector<float>.One;
        var iterV = Vector<float>.Zero;
        if (!julia)
        {
            float[] a = new float[n];
            alive.CopyTo(a);
            float[] it = new float[n];
            for (int i = 0; i < n; i++)
                if (IsInterior(pxArr[i], pyArr[i])) { a[i] = 0; it[i] = maxIter; }
            alive = new Vector<float>(a);
            iterV = new Vector<float>(it);
        }
        var zx2 = zx * zx;
        var zy2 = zy * zy;
        var four = new Vector<float>(4.0f);
        var two = new Vector<float>(2.0f);
        var zero = Vector<float>.Zero;
        for (int iter = 0; iter < maxIter; iter++)
        {
            var esc = Vector.GreaterThan(zx2 + zy2, four);
            alive = Vector.ConditionalSelect(esc, zero, alive);
            if (!Vector.GreaterThanAny(alive, zero)) break;
            iterV += alive;
            var nzy = two * zx * zy + ccy;
            var nzx = zx2 - zy2 + ccx;
            var nzx2 = nzx * nzx;
            var nzy2 = nzy * nzy;
            var stillAlive = Vector.GreaterThan(alive, zero);
            zx = Vector.ConditionalSelect(stillAlive, nzx, zx);
            zy = Vector.ConditionalSelect(stillAlive, nzy, zy);
            zx2 = Vector.ConditionalSelect(stillAlive, nzx2, zx2);
            zy2 = Vector.ConditionalSelect(stillAlive, nzy2, zy2);
        }
        float[] itF = new float[n], mgF = new float[n];
        iterV.CopyTo(itF);
        (zx2 + zy2).CopyTo(mgF);
        for (int i = 0; i < n; i++) { iters[i] = (int)itF[i]; mags[i] = mgF[i]; }
    }

    // ---- CPU benchmarks (§3.3) ----
    // Counts total escape iterations over repeated frames until budget elapses.
    // No coloring/smoothing/SSAA; applies cardioid/bulb early-out (intentional
    // asymmetry vs GPU bench). Same row chunking as rendering.

    public static (long TotalIters, double Seconds, int Frames) BenchmarkCpu(
        int w, int h, double cx, double cy, double scale, int maxIter,
        CancellationToken ct, Action<BenchmarkProgress>? tick = null) =>
        BenchmarkCore(w, h, cx, cy, scale, maxIter, useDouble: true, ct, tick);

    public static (long TotalIters, double Seconds, int Frames) BenchmarkCpuFloat(
        int w, int h, double cx, double cy, double scale, int maxIter,
        CancellationToken ct, Action<BenchmarkProgress>? tick = null) =>
        BenchmarkCore(w, h, cx, cy, scale, maxIter, useDouble: false, ct, tick);

    private static (long TotalIters, double Seconds, int Frames) BenchmarkCore(
        int w, int h, double cx, double cy, double scale, int maxIter, bool useDouble,
        CancellationToken ct, Action<BenchmarkProgress>? tick)
    {
        var (pixelSize, topY, originX) = ViewMath(cx, cy, scale, w, h, 1);
        int chunk = Math.Max(8, h / (Math.Max(1, Environment.ProcessorCount) * 8));
        var part = Partitioner.Create(0, h, chunk);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double budget = BenchmarkStandard.BudgetSeconds;
        long total = 0;
        int frames = 0;
        double lastReport = 0;
        var opts = new ParallelOptions { CancellationToken = ct };
        while (sw.Elapsed.TotalSeconds < budget)
        {
            ct.ThrowIfCancellationRequested();
            long frameSum = 0;
            object gate = new();
            Parallel.ForEach(part, opts, () => 0L,
                (range, state, local) =>
                {
                    for (int y = range.Item1; y < range.Item2; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            long gb = x; // k=1, offset 0
                            long gby = y;
                            double px = originX + gb * pixelSize;
                            double py = topY + gby * pixelSize;
                            int it;
                            if (IsInterior(px, py)) it = maxIter;
                            else if (useDouble) it = Escape(px, py, 0, 0, maxIter).Iter;
                            else
                            {
                                var r = EscapeFloat((float)px, (float)py, 0, 0, maxIter);
                                it = r.Iter;
                            }
                            local += it;
                        }
                    }
                    return local;
                },
                local => { lock (gate) frameSum += local; });
            total += frameSum;
            frames++;
            double s = sw.Elapsed.TotalSeconds;
            if (tick is not null && s - lastReport >= BenchmarkProgress.ReportIntervalSeconds)
            {
                lastReport = s;
                tick(new BenchmarkProgress(frames, total, s, frames * (double)BenchmarkStandard.SamplesPerFrame / Math.Max(s, 1e-9)));
            }
            // per-frame cancel check (§3.3)
            ct.ThrowIfCancellationRequested();
        }
        double secs = sw.Elapsed.TotalSeconds;
        tick?.Invoke(new BenchmarkProgress(frames, total, secs, frames * (double)BenchmarkStandard.SamplesPerFrame / Math.Max(secs, 1e-9)));
        return (total, secs, frames);
    }
}
