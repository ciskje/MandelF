using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Numerics;

namespace MandelbrotViewer;

// Computation logic of the Mandelbrot (and Julia) set with smooth coloring.
// Mandelbrot: z(n+1) = z(n)^2 + c, with z(0) = 0. If |z| > 2 within maxIter, c is outside.
// Julia: same iteration but z(0) = pixel point and c = fixed constant.
public static class Mandelbrot
{
    // Renders the fractal into the given bitmap (whole-frame path).
    // Precision follows RenderTile: SIMD double or float by explicit user choice.
    // Thin wrapper over RenderTile with zero offsets and full size equal
    // to the bitmap size; kept so interactive view and video frames need no tiling math.
    // Runs on a worker thread via Parallel.For with cancellation support.
    // Param bmp (Bitmap): Input/output: destination bitmap, overwritten in place via LockBits
    //   (Format32bppArgb). Input pixels are ignored; output pixels receive the SSAA-averaged
    //   ARGB colors of the requested view. Must be non-empty.
    // Param centerX (double): Input: view center on the real axis (complex units).
    // Param centerY (double): Input: view center on the imaginary axis (complex units).
    // Param scale (double): Input: complex width of the view. The complex height derives
    //   from it via the bitmap aspect ratio, so the image never stretches.
    // Param maxIter (int): Input: escape-iteration budget per subsample (clamped 50…50000
    //   by the UI; auto mode derives it from the zoom via AutoIterForScale).
    //   Points still bounded after maxIter render black (interior).
    // Param palette (Palette): Input: color palette for exterior points (5-stop gradient).
    // Param supersample (int): Input: antialias factor k (1 = off). Each output pixel
    //   averages k×k subsamples (cost ~k²). Computed on-chip per output pixel, without
    //   allocating a k-times buffer.
    // Param ct (CancellationToken): Input: cancellation token. Observed between rows; an obsolete render
    //   (pan/zoom superseded) aborts with OperationCanceledException and leaves bmp untouched
    //   past UnlockBits.
    // Param juliaCx (double): Input: constant c, real part (Julia mode only).
    // Param juliaCy (double): Input: constant c, imaginary part (Julia mode only).
    // Param julia (bool): Input: mode switch. True = Julia set (z(0) = pixel, c fixed);
    //   false = Mandelbrot (z(0) = 0, c = pixel).
    // Param useDouble (bool): Input: precision switch, like the other engines (no
    //   fallback). True = double 64-bit everywhere; false = float 32-bit everywhere,
    //   including deep zoom and Julia (less precise there, by user choice).
    public static void Render(Bitmap bmp, double centerX, double centerY, double scale, int maxIter, Palette palette, int supersample, CancellationToken ct, double juliaCx = 0, double juliaCy = 0, bool julia = false, bool useDouble = true)
    {
        RenderTile(bmp, centerX, centerY, scale, maxIter, palette, supersample, ct,
            0, 0, bmp.Width, bmp.Height, juliaCx, juliaCy, julia, useDouble);
    }

    // Packed inputs of one tile render, so row workers take two arguments.
    private readonly struct TileArgs
    {
        public readonly int W, H, K, Samples, MaxIter, OffsetX, OffsetY;
        public readonly double PixelSize, TopY, OriginX, InvMaxIter;
        public readonly int[] Lut;
        public readonly bool IsMandelbrot, Julia;
        public readonly double JuliaCx, JuliaCy;

        public TileArgs(int w, int h, int k, int samples, int maxIter, int offsetX, int offsetY,
            double pixelSize, double topY, double originX, int[] lut, double invMaxIter,
            bool isMandelbrot, bool julia, double juliaCx, double juliaCy)
        {
            W = w; H = h; K = k; Samples = samples; MaxIter = maxIter;
            OffsetX = offsetX; OffsetY = offsetY;
            PixelSize = pixelSize; TopY = topY; OriginX = originX;
            Lut = lut; InvMaxIter = invMaxIter;
            IsMandelbrot = isMandelbrot; Julia = julia; JuliaCx = juliaCx; JuliaCy = juliaCy;
        }
    }

    // Locked bitmap target for unsafe row writes (pointer + row stride in ints).
    private readonly unsafe struct TileTarget
    {
        public readonly int* Dst;
        public readonly int Stride;

        public TileTarget(int* dst, int stride) { Dst = dst; Stride = stride; }
    }

    // Row chunk per parallel task: small enough to balance uneven rows
    // (interior rows cost maxIter everywhere), large enough to amortize scheduling.
    private static int RenderRowChunk(int h) =>
        Math.Max(8, h / (Math.Max(1, Environment.ProcessorCount) * 8));
    // On-chip SSAA: each output pixel computes its k×k subsamples and accumulates
    // them locally, so memory stays O(W×H) and no W*k×H*k buffer is allocated.
    // Sample coordinates use the global k-times grid, so tiled output is
    // pixel-identical to a whole-frame render at the same view and AA.
    // Renders one output tile using the coordinate system of the complete image.
    // On-chip SSAA: each output pixel computes its k×k subsamples and accumulates
    // them locally, so memory stays O(W×H) and no W*k×H*k buffer is allocated.
    // Sample coordinates use the global k-times grid, so tiled output is
    // pixel-identical to a whole-frame render at the same view and AA.
    // Precision: SIMD double or float by explicit user choice (no fallback, like
    // the CUDA engine); scalar fallback without SIMD hardware in both precisions.
    // Param bmp (Bitmap): Input/output: tile bitmap, overwritten in place via LockBits.
    //   Output pixels receive the SSAA-averaged ARGB colors of the tile region.
    // Param centerX (double): Input: whole-image view center, real axis (complex units).
    // Param centerY (double): Input: whole-image view center, imaginary axis (complex units).
    // Param scale (double): Input: complex width of the whole image (not of the tile).
    // Param maxIter (int): Input: escape-iteration budget per subsample; interior
    //   (iter ≥ maxIter) renders black.
    // Param palette (Palette): Input: color palette for exterior points.
    // Param supersample (int): Input: antialias factor k (1 = off, identity average).
    // Param ct (CancellationToken): Input: cancellation token, observed by the Parallel.For rows.
    // Param offsetX (int): Input: tile origin X in whole-image output pixels (columns skipped).
    // Param offsetY (int): Input: tile origin Y in whole-image output pixels (rows skipped).
    // Param fullWidth (int): Input: whole-image width in output pixels (defines pixelSize
    //   together with scale, and the global grid fullWidth*k).
    // Param fullHeight (int): Input: whole-image height in output pixels (defines the
    //   complex height via aspect, and the global grid fullHeight*k).
    // Param juliaCx (double): Input: constant c, real part (Julia mode only).
    // Param juliaCy (double): Input: constant c, imaginary part (Julia mode only).
    // Param julia (bool): Input: mode switch (true = Julia, false = Mandelbrot).
    // Param useDouble (bool): Input: precision switch. True = double 64-bit;
    //   false = float 32-bit at any scale and mode.
    public static void RenderTile(Bitmap bmp, double centerX, double centerY, double scale, int maxIter, Palette palette,
        int supersample, CancellationToken ct, int offsetX, int offsetY, int fullWidth, int fullHeight,
        double juliaCx = 0, double juliaCy = 0, bool julia = false, bool useDouble = true)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        if (w <= 0 || h <= 0) return;

        int k = Math.Max(1, supersample);
        int fullBigW = fullWidth * k;
        int fullBigH = fullHeight * k;
        double pixelSize = scale / fullBigW;
        // To keep the aspect ratio, the complex height derives from the width.
        double topY = centerY - (fullBigH * 0.5) * pixelSize;
        double originX = centerX - (fullBigW * 0.5) * pixelSize;
        int samples = k * k;
        // Fast path data, hoisted out of the per-subsample loop: the palette table
        // removes Pow + stop search, invMaxIter removes the division.
        int[] lut = PaletteColors.GetLut(palette);
        double invMaxIter = 1.0 / Math.Max(1, maxIter);
        bool isMandelbrot = !julia;
        var args = new TileArgs(w, h, k, samples, maxIter, offsetX, offsetY,
            pixelSize, topY, originX, lut, invMaxIter, isMandelbrot, julia, juliaCx, juliaCy);

        // Precision + code path, by explicit user choice like the CUDA engine:
        // float when 32-bit is selected (any scale and mode), else double.
        bool useVector = Vector.IsHardwareAccelerated;
        bool useFloat = useVector && !useDouble;

        var rect = new Rectangle(0, 0, w, h);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride / 4; // ints per row
            unsafe
            {
                var target = new TileTarget((int*)data.Scan0, stride);
                var options = new ParallelOptions { CancellationToken = ct };
                var partitioner = Partitioner.Create(0, h, RenderRowChunk(h));
                // Same sample coordinates on the global k-times grid in every path,
                // so tiled output stays pixel-identical to a whole-frame render.
                if (!useVector)
                {
                    if (useFloat)
                    {
                        Parallel.ForEach(partitioner, options, range =>
                        {
                            for (int y = range.Item1; y < range.Item2; y++)
                                RenderRowScalarFloat(in args, target, y);
                        });
                    }
                    else
                    {
                        Parallel.ForEach(partitioner, options, range =>
                        {
                            for (int y = range.Item1; y < range.Item2; y++)
                                RenderRowScalar(in args, target, y);
                        });
                    }
                }
                else if (useFloat)
                {
                    Parallel.ForEach(partitioner, options, range =>
                    {
                        for (int y = range.Item1; y < range.Item2; y++)
                            RenderRowVectorFloat(in args, target, y);
                    });
                }
                else
                {
                    Parallel.ForEach(partitioner, options, range =>
                    {
                        for (int y = range.Item1; y < range.Item2; y++)
                            RenderRowVectorDouble(in args, target, y);
                    });
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // Scalar double row (fallback without SIMD hardware): one output pixel at a
    // time, k×k subsamples averaged. Bit-identical to the vector double path.
    private static unsafe void RenderRowScalar(in TileArgs a, TileTarget t, int y)
    {
        int* row = t.Dst + (long)y * t.Stride;
        for (int x = 0; x < a.W; x++)
        {
            long r = 0, g = 0, b = 0;
            for (int sy = 0; sy < a.K; sy++)
            {
                int globalBy = a.OffsetY * a.K + y * a.K + sy;
                double py = a.TopY + globalBy * a.PixelSize;
                for (int sx = 0; sx < a.K; sx++)
                {
                    int globalBx = a.OffsetX * a.K + x * a.K + sx;
                    double px = a.OriginX + globalBx * a.PixelSize;

                    int c;
                    // Cardioid + period-2 bulb early-out (Mandelbrot only):
                    // interior points skip the escape loop entirely.
                    if (a.IsMandelbrot && IsInteriorBulb(px, py))
                    {
                        c = unchecked((int)0xFF000000);
                    }
                    else
                    {
                        // Julia: z(0) = pixel point, c = constant; Mandelbrot: z(0) = 0, c = pixel.
                        double zx = a.Julia ? px : 0, zy = a.Julia ? py : 0;
                        double ccx = a.Julia ? a.JuliaCx : px, ccy = a.Julia ? a.JuliaCy : py;
                        double zx2 = zx * zx, zy2 = zy * zy;
                        int iter = 0;

                        while (iter < a.MaxIter && zx2 + zy2 <= 4.0)
                        {
                            zy = 2.0 * zx * zy + ccy;
                            zx = zx2 - zy2 + ccx;
                            zx2 = zx * zx;
                            zy2 = zy * zy;
                            iter++;
                        }

                        if (iter >= a.MaxIter)
                        {
                            c = unchecked((int)0xFF000000); // inside -> black
                        }
                        else
                        {
                            // Fast smooth: log2(0.5*ln(mod2)) equals the legacy
                            // ln(ln(sqrt(mod2)))/ln2 with no sqrt and one less log.
                            // The palette table is linearly interpolated, so no
                            // quantization banding shows in smooth gradients.
                            double mod = zx2 + zy2;
                            if (mod < 4.0) mod = 4.0;
                            double smooth = iter + 1.0 - Math.Log2(0.5 * Math.Log(mod));
                            c = PaletteColors.ColorFromLut(a.Lut, smooth, a.InvMaxIter);
                        }
                    }
                    r += (c >> 16) & 0xFF;
                    g += (c >> 8) & 0xFF;
                    b += c & 0xFF;
                }
            }
            if (a.Samples == 1)
                row[x] = unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
            else
                row[x] = unchecked((int)(0xFF000000u | ((uint)(r / a.Samples) << 16) | ((uint)(g / a.Samples) << 8) | (uint)(b / a.Samples)));
        }
    }

    // Scalar float row (fallback without SIMD hardware): one output pixel at a
    // time through ComputePixelFloat. Matches the vector float path lane by lane.
    private static unsafe void RenderRowScalarFloat(in TileArgs a, TileTarget t, int y)
    {
        int* row = t.Dst + (long)y * t.Stride;
        for (int x = 0; x < a.W; x++)
            row[x] = ComputePixelFloat(in a, x, y);
    }

    // SIMD double row: full Vector<double>.Count blocks via the vector escape
    // core, scalar tail for the remainder. Bit-identical to RenderRowScalar.
    private static unsafe void RenderRowVectorDouble(in TileArgs a, TileTarget t, int y)
    {
        int n = Vector<double>.Count;
        int blocks = a.W / n;
        int* row = t.Dst + (long)y * t.Stride;
        Span<int> colors = stackalloc int[n];
        Span<long> r = stackalloc long[n];
        Span<long> g = stackalloc long[n];
        Span<long> bb = stackalloc long[n];
        for (int b = 0; b < blocks; b++)
        {
            int x0 = b * n;
            r.Clear(); g.Clear(); bb.Clear();
            for (int sy = 0; sy < a.K; sy++)
            {
                int globalBy = a.OffsetY * a.K + y * a.K + sy;
                double py = a.TopY + globalBy * a.PixelSize;
                for (int sx = 0; sx < a.K; sx++)
                {
                    // Global subsample grid x of lane 0; lane i sits at +i*K.
                    int gridBase = a.OffsetX * a.K + x0 * a.K + sx;
                    EscapeBlockDouble(gridBase, a.K, a.OriginX, a.PixelSize, py, in a, colors);
                    for (int i = 0; i < n; i++)
                    {
                        int c = colors[i];
                        r[i] += (c >> 16) & 0xFF;
                        g[i] += (c >> 8) & 0xFF;
                        bb[i] += c & 0xFF;
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (a.Samples == 1)
                    row[x0 + i] = unchecked((int)(0xFF000000u | ((uint)r[i] << 16) | ((uint)g[i] << 8) | (uint)bb[i]));
                else
                    row[x0 + i] = unchecked((int)(0xFF000000u | ((uint)(r[i] / a.Samples) << 16) | ((uint)(g[i] / a.Samples) << 8) | (uint)(bb[i] / a.Samples)));
            }
        }
        // Scalar tail: identical arithmetic to the vector lanes.
        for (int x = blocks * n; x < a.W; x++)
            row[x] = ComputePixelDouble(in a, x, y);
    }

    // SIMD float row (explicit 32-bit setting): same structure in float.
    // Approximate vs double (like CUDA 32-bit); tiling-independent by construction.
    private static unsafe void RenderRowVectorFloat(in TileArgs a, TileTarget t, int y)
    {
        int n = Vector<float>.Count;
        int blocks = a.W / n;
        int* row = t.Dst + (long)y * t.Stride;
        Span<int> colors = stackalloc int[n];
        Span<long> r = stackalloc long[n];
        Span<long> g = stackalloc long[n];
        Span<long> bb = stackalloc long[n];
        for (int b = 0; b < blocks; b++)
        {
            int x0 = b * n;
            r.Clear(); g.Clear(); bb.Clear();
            for (int sy = 0; sy < a.K; sy++)
            {
                int globalBy = a.OffsetY * a.K + y * a.K + sy;
                double py = a.TopY + globalBy * a.PixelSize;
                for (int sx = 0; sx < a.K; sx++)
                {
                    int gridBase = a.OffsetX * a.K + x0 * a.K + sx;
                    EscapeBlockFloat(gridBase, a.K, a.OriginX, a.PixelSize, py, in a, colors);
                    for (int i = 0; i < n; i++)
                    {
                        int c = colors[i];
                        r[i] += (c >> 16) & 0xFF;
                        g[i] += (c >> 8) & 0xFF;
                        bb[i] += c & 0xFF;
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (a.Samples == 1)
                    row[x0 + i] = unchecked((int)(0xFF000000u | ((uint)r[i] << 16) | ((uint)g[i] << 8) | (uint)bb[i]));
                else
                    row[x0 + i] = unchecked((int)(0xFF000000u | ((uint)(r[i] / a.Samples) << 16) | ((uint)(g[i] / a.Samples) << 8) | (uint)(bb[i] / a.Samples)));
            }
        }
        for (int x = blocks * n; x < a.W; x++)
            row[x] = ComputePixelFloat(in a, x, y);
    }

    // Vector escape core, double precision: one subsample for a full lane block.
    // Each lane runs the exact scalar op sequence, so output is bit-identical to
    // the scalar path regardless of block/tile decomposition.
    // Param gridBase (int): Input: global subsample grid x of lane 0.
    // Param gridStep (int): Input: grid stride between lanes (= supersample k).
    // Param originX (double): Input: complex x of grid 0 (tile mapping).
    // Param pixelSize (double): Input: complex units per subsample step.
    // Param py (double): Input: complex y shared by all lanes.
    // Param a (TileArgs): Input: iteration budget, palette table, Julia switch.
    // Param colors (Span<int>): Output: packed ARGB color per lane.
    private static void EscapeBlockDouble(int gridBase, int gridStep, double originX, double pixelSize,
        double py, in TileArgs a, Span<int> colors)
    {
        int n = Vector<double>.Count;
        // Per-lane coordinates replicate the scalar formula originX + gb*pixelSize
        // exactly (integer grid converted exactly, one mult + one add per lane).
        Span<double> grid = stackalloc double[n];
        Span<double> seedAlive = stackalloc double[n];
        Span<double> seedIter = stackalloc double[n];
        bool anyAlive = false;
        for (int i = 0; i < n; i++)
        {
            int gb = gridBase + i * gridStep;
            grid[i] = gb;
            double px = originX + gb * pixelSize;
            if (a.IsMandelbrot && IsInteriorBulb(px, py))
            {
                seedAlive[i] = 0.0;
                seedIter[i] = a.MaxIter; // forced black, no work
            }
            else
            {
                seedAlive[i] = 1.0;
                seedIter[i] = 0.0;
                anyAlive = true;
            }
        }
        if (!anyAlive)
        {
            for (int i = 0; i < n; i++) colors[i] = unchecked((int)0xFF000000);
            return;
        }

        var gbVec = new Vector<double>(grid);
        var cx = new Vector<double>(originX) + gbVec * new Vector<double>(pixelSize);
        var cy = new Vector<double>(py);
        Vector<double> ccx, ccy, zx, zy;
        if (a.Julia)
        {
            // Julia: z(0) = pixel point, c = constant.
            zx = cx; zy = cy;
            ccx = new Vector<double>(a.JuliaCx);
            ccy = new Vector<double>(a.JuliaCy);
        }
        else
        {
            // Mandelbrot: z(0) = 0, c = pixel.
            zx = Vector<double>.Zero; zy = Vector<double>.Zero;
            ccx = cx; ccy = cy;
        }
        var zx2 = zx * zx;
        var zy2 = zy * zy;
        var alive = new Vector<double>(seedAlive);
        var iters = new Vector<double>(seedIter);
        var vFour = new Vector<double>(4.0);
        var vTwo = new Vector<double>(2.0);
        int round = 0;
        while (round < a.MaxIter)
        {
            var escaped = Vector.GreaterThan(zx2 + zy2, vFour);
            alive = Vector.ConditionalSelect(escaped, Vector<double>.Zero, alive);
            if (Vector.EqualsAll(alive, Vector<double>.Zero)) break;
            iters += alive;
            // Same op order as the scalar loop: zy, zx, then their squares.
            var nzy = vTwo * zx * zy + ccy;
            var nzx = zx2 - zy2 + ccx;
            var nzy2 = nzy * nzy;
            var nzx2 = nzx * nzx;
            // Frozen lanes keep their escape-time state for the smooth term.
            zx = Vector.ConditionalSelect(escaped, zx, nzx);
            zy = Vector.ConditionalSelect(escaped, zy, nzy);
            zx2 = Vector.ConditionalSelect(escaped, zx2, nzx2);
            zy2 = Vector.ConditionalSelect(escaped, zy2, nzy2);
            round++;
        }
        for (int i = 0; i < n; i++)
        {
            int iter = (int)iters[i];
            if (iter >= a.MaxIter)
            {
                colors[i] = unchecked((int)0xFF000000); // inside -> black
            }
            else
            {
                double mod = zx2[i] + zy2[i];
                if (mod < 4.0) mod = 4.0;
                double smooth = iter + 1.0 - Math.Log2(0.5 * Math.Log(mod));
                colors[i] = PaletteColors.ColorFromLut(a.Lut, smooth, a.InvMaxIter);
            }
        }
    }

    // Vector escape core, single precision: same shape as
    // the double core, chosen by explicit user setting at any scale and mode.
    // Lane coordinates use the scalar float formula, so blocks
    // and scalar tails agree exactly; values are approximate vs double.
    private static void EscapeBlockFloat(int gridBase, int gridStep, double originX, double pixelSize,
        double py, in TileArgs a, Span<int> colors)
    {
        int n = Vector<float>.Count;
        Span<float> grid = stackalloc float[n];
        Span<float> seedAlive = stackalloc float[n];
        Span<float> seedIter = stackalloc float[n];
        bool anyAlive = false;
        for (int i = 0; i < n; i++)
        {
            int gb = gridBase + i * gridStep;
            float px = (float)(originX + gb * pixelSize);
            grid[i] = px;
            if (a.IsMandelbrot && IsInteriorBulb(originX + gb * pixelSize, py))
            {
                seedAlive[i] = 0f;
                seedIter[i] = a.MaxIter; // forced black, no work
            }
            else
            {
                seedAlive[i] = 1f;
                seedIter[i] = 0f;
                anyAlive = true;
            }
        }
        if (!anyAlive)
        {
            for (int i = 0; i < n; i++) colors[i] = unchecked((int)0xFF000000);
            return;
        }

        var cpx = new Vector<float>(grid);
        var cpy = new Vector<float>((float)py);
        Vector<float> ccx, ccy, zx, zy;
        if (a.Julia)
        {
            // Julia: z(0) = pixel point, c = constant.
            zx = cpx; zy = cpy;
            ccx = new Vector<float>((float)a.JuliaCx);
            ccy = new Vector<float>((float)a.JuliaCy);
        }
        else
        {
            // Mandelbrot: z(0) = 0, c = pixel.
            zx = Vector<float>.Zero; zy = Vector<float>.Zero;
            ccx = cpx; ccy = cpy;
        }
        var zx2 = zx * zx;
        var zy2 = zy * zy;
        var alive = new Vector<float>(seedAlive);
        var iters = new Vector<float>(seedIter);
        var vFour = new Vector<float>(4f);
        var vTwo = new Vector<float>(2f);
        int round = 0;
        while (round < a.MaxIter)
        {
            var escaped = Vector.GreaterThan(zx2 + zy2, vFour);
            alive = Vector.ConditionalSelect(escaped, Vector<float>.Zero, alive);
            if (Vector.EqualsAll(alive, Vector<float>.Zero)) break;
            iters += alive;
            var nzy = vTwo * zx * zy + ccy;
            var nzx = zx2 - zy2 + ccx;
            var nzy2 = nzy * nzy;
            var nzx2 = nzx * nzx;
            zx = Vector.ConditionalSelect(escaped, zx, nzx);
            zy = Vector.ConditionalSelect(escaped, zy, nzy);
            zx2 = Vector.ConditionalSelect(escaped, zx2, nzx2);
            zy2 = Vector.ConditionalSelect(escaped, zy2, nzy2);
            round++;
        }
        for (int i = 0; i < n; i++)
        {
            int iter = (int)iters[i];
            if (iter >= a.MaxIter)
            {
                colors[i] = unchecked((int)0xFF000000); // inside -> black
            }
            else
            {
                float mod = zx2[i] + zy2[i];
                if (mod < 4f) mod = 4f;
                float smooth = iter + 1f - MathF.Log2(0.5f * MathF.Log(mod));
                colors[i] = PaletteColors.ColorFromLut(a.Lut, smooth, a.InvMaxIter);
            }
        }
    }

    // Scalar tail pixel, double precision: full k×k average with the exact lane
    // arithmetic, used for columns past the last full vector block.
    private static int ComputePixelDouble(in TileArgs a, int x, int y)
    {
        long r = 0, g = 0, b = 0;
        for (int sy = 0; sy < a.K; sy++)
        {
            int globalBy = a.OffsetY * a.K + y * a.K + sy;
            double py = a.TopY + globalBy * a.PixelSize;
            for (int sx = 0; sx < a.K; sx++)
            {
                int globalBx = a.OffsetX * a.K + x * a.K + sx;
                double px = a.OriginX + globalBx * a.PixelSize;
                int c;
                if (a.IsMandelbrot && IsInteriorBulb(px, py))
                {
                    c = unchecked((int)0xFF000000);
                }
                else
                {
                    double zx = a.Julia ? px : 0, zy = a.Julia ? py : 0;
                    double ccx = a.Julia ? a.JuliaCx : px, ccy = a.Julia ? a.JuliaCy : py;
                    double zx2 = zx * zx, zy2 = zy * zy;
                    int iter = 0;
                    while (iter < a.MaxIter && zx2 + zy2 <= 4.0)
                    {
                        zy = 2.0 * zx * zy + ccy;
                        zx = zx2 - zy2 + ccx;
                        zx2 = zx * zx;
                        zy2 = zy * zy;
                        iter++;
                    }
                    if (iter >= a.MaxIter)
                    {
                        c = unchecked((int)0xFF000000);
                    }
                    else
                    {
                        double mod = zx2 + zy2;
                        if (mod < 4.0) mod = 4.0;
                        double smooth = iter + 1.0 - Math.Log2(0.5 * Math.Log(mod));
                        c = PaletteColors.ColorFromLut(a.Lut, smooth, a.InvMaxIter);
                    }
                }
                r += (c >> 16) & 0xFF;
                g += (c >> 8) & 0xFF;
                b += c & 0xFF;
            }
        }
        if (a.Samples == 1)
            return unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
        return unchecked((int)(0xFF000000u | ((uint)(r / a.Samples) << 16) | ((uint)(g / a.Samples) << 8) | (uint)(b / a.Samples)));
    }

    // Scalar tail pixel, single precision: full k×k average with the exact lane
    // arithmetic, used for columns past the last full vector block.
    private static int ComputePixelFloat(in TileArgs a, int x, int y)
    {
        long r = 0, g = 0, b = 0;
        for (int sy = 0; sy < a.K; sy++)
        {
            int globalBy = a.OffsetY * a.K + y * a.K + sy;
            double py = a.TopY + globalBy * a.PixelSize;
            for (int sx = 0; sx < a.K; sx++)
            {
                int globalBx = a.OffsetX * a.K + x * a.K + sx;
                float px = (float)(a.OriginX + globalBx * a.PixelSize);
                float fpy = (float)py;
                int c;
                if (a.IsMandelbrot && IsInteriorBulb(a.OriginX + globalBx * a.PixelSize, py))
                {
                    c = unchecked((int)0xFF000000);
                }
                else
                {
                    // Julia: z(0) = pixel point, c = constant; Mandelbrot: z(0) = 0, c = pixel.
                    float zx = a.Julia ? px : 0, zy = a.Julia ? fpy : 0;
                    float ccx = a.Julia ? (float)a.JuliaCx : px, ccy = a.Julia ? (float)a.JuliaCy : fpy;
                    float zx2 = zx * zx, zy2 = zy * zy;
                    int iter = 0;
                    while (iter < a.MaxIter && zx2 + zy2 <= 4f)
                    {
                        zy = 2f * zx * zy + ccy;
                        zx = zx2 - zy2 + ccx;
                        zx2 = zx * zx;
                        zy2 = zy * zy;
                        iter++;
                    }
                    if (iter >= a.MaxIter)
                    {
                        c = unchecked((int)0xFF000000);
                    }
                    else
                    {
                        float mod = zx2 + zy2;
                        if (mod < 4f) mod = 4f;
                        float smooth = iter + 1f - MathF.Log2(0.5f * MathF.Log(mod));
                        c = PaletteColors.ColorFromLut(a.Lut, smooth, a.InvMaxIter);
                    }
                }
                r += (c >> 16) & 0xFF;
                g += (c >> 8) & 0xFF;
                b += c & 0xFF;
            }
        }
        if (a.Samples == 1)
            return unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
        return unchecked((int)(0xFF000000u | ((uint)(r / a.Samples) << 16) | ((uint)(g / a.Samples) << 8) | (uint)(b / a.Samples)));
    }

    // Main cardioid + period-2 bulb test for the Mandelbrot set (double precision).
    // Param px (double): Input: point real coordinate (c in z = z^2 + c).
    // Param py (double): Input: point imaginary coordinate.
    // Returns (bool): Output: true when the point is known interior (renders black).
    internal static bool IsInteriorBulb(double px, double py)
    {
        double q = (px - 0.25) * (px - 0.25) + py * py;
        if (q * (q + (px - 0.25)) <= 0.25 * py * py) return true;
        double dx = px + 1.0;
        return dx * dx + py * py <= 0.0625;
    }

    // Shared frame loop for the CPU benchmarks: repeats timed frames until the
    // budget expires, rows run in parallel through the given row function.
    private static (long TotalIters, double Seconds, int Frames) RunCpuBenchmark(
        int bigH, TimeSpan budget, IProgress<BenchmarkProgress>? progress, CancellationToken ct,
        Func<int, long> row)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long total = 0;
        int frames = 0;
        bool first = true;
        TimeSpan lastReport = TimeSpan.Zero;
        var options = new ParallelOptions { CancellationToken = ct };
        var partitioner = Partitioner.Create(0, bigH, RenderRowChunk(bigH));
        while (sw.Elapsed < budget)
        {
            ct.ThrowIfCancellationRequested();
            long frame = 0;
            object gate = new();
            Parallel.ForEach(partitioner, options, () => 0L,
                (range, state, local) =>
                {
                    for (int y = range.Item1; y < range.Item2; y++)
                        local += row(y);
                    return local;
                },
                local => { lock (gate) frame += local; });
            total += frame;
            frames++;
            if (first || sw.Elapsed - lastReport >= BenchmarkProgress.ReportInterval)
            {
                progress?.Report(new BenchmarkProgress(sw.Elapsed.TotalSeconds, total, frames));
                lastReport = sw.Elapsed;
                first = false;
            }
        }
        progress?.Report(new BenchmarkProgress(sw.Elapsed.TotalSeconds, total, frames));
        return (total, sw.Elapsed.TotalSeconds, frames);
    }

    // Scalar benchmark row, double precision: non-SIMD fallback (same workload
    // and early-outs as the vector core).
    private static long BenchmarkRowScalarDouble(int y, double centerX, double topY, double pixelSize, int bigW, int maxIter)
    {
        double cy = topY + y * pixelSize;
        long sum = 0;
        for (int x = 0; x < bigW; x++)
        {
            double cx = centerX + (x - bigW * 0.5) * pixelSize;
            // Cardioid + bulb early-out: known interior counts maxIter
            // without running the escape loop (same rule as the render path).
            if (IsInteriorBulb(cx, cy))
            {
                sum += maxIter;
                continue;
            }
            double zx = 0, zy = 0, zx2 = 0, zy2 = 0;
            int iter = 0;
            while (iter < maxIter && zx2 + zy2 <= 4.0)
            {
                zy = 2.0 * zx * zy + cy;
                zx = zx2 - zy2 + cx;
                zx2 = zx * zx;
                zy2 = zy * zy;
                iter++;
            }
            sum += iter;
        }
        return sum;
    }

    // Scalar benchmark row, single precision: float escape loop with the shared
    // double bulb classification (mirrors the production float render path).
    private static long BenchmarkRowScalarFloat(int y, double centerX, double topY, double pixelSize, int bigW, int maxIter)
    {
        double cy = topY + y * pixelSize;
        float fcy = (float)cy;
        long sum = 0;
        for (int x = 0; x < bigW; x++)
        {
            double dx = centerX + (x - bigW * 0.5) * pixelSize;
            if (IsInteriorBulb(dx, cy))
            {
                sum += maxIter;
                continue;
            }
            float cx = (float)dx;
            float zx = 0, zy = 0, zx2 = 0, zy2 = 0;
            int iter = 0;
            while (iter < maxIter && zx2 + zy2 <= 4f)
            {
                zy = 2f * zx * zy + fcy;
                zx = zx2 - zy2 + cx;
                zx2 = zx * zx;
                zy2 = zy * zy;
                iter++;
            }
            sum += iter;
        }
        return sum;
    }

    // Vector benchmark row: full lane blocks plus a scalar tail.
    // Param y (int): Input: grid row.
    // Param centerX (double): Input: zone center, real axis.
    // Param topY (double): Input: complex y of grid row 0.
    // Param pixelSize (double): Input: complex units per grid step.
    // Param bigW (int): Input: grid width in elementary samples.
    // Param maxIter (int): Input: iteration budget per sample.
    // Returns (long): Output: summed escape iterations of the row.
    private static long BenchmarkRowVector(int y, double centerX, double topY, double pixelSize, int bigW, int maxIter)
    {
        int n = Vector<double>.Count;
        int blocks = bigW / n;
        double cy = topY + y * pixelSize;
        double halfW = bigW * 0.5;
        long sum = 0;
        for (int b = 0; b < blocks; b++)
            sum += BenchmarkBlockDouble(b * n, cy, centerX, halfW, pixelSize, maxIter);
        for (int x = blocks * n; x < bigW; x++)
        {
            double cx = centerX + (x - halfW) * pixelSize;
            if (IsInteriorBulb(cx, cy))
            {
                sum += maxIter;
                continue;
            }
            double zx = 0, zy = 0, zx2 = 0, zy2 = 0;
            int iter = 0;
            while (iter < maxIter && zx2 + zy2 <= 4.0)
            {
                zy = 2.0 * zx * zy + cy;
                zx = zx2 - zy2 + cx;
                zx2 = zx * zx;
                zy2 = zy * zy;
                iter++;
            }
            sum += iter;
        }
        return sum;
    }

    // Vector escape counting for one lane block (benchmark, no coloring).
    // Lane coordinates replicate the scalar formula exactly, so block sums are
    // independent of the block decomposition.
    // Param x0 (int): Input: grid column of lane 0.
    // Param cy (double): Input: complex y shared by all lanes.
    // Param centerX (double): Input: zone center, real axis.
    // Param halfW (double): Input: bigW * 0.5, precomputed.
    // Param pixelSize (double): Input: complex units per grid step.
    // Param maxIter (int): Input: iteration budget per sample.
    // Returns (long): Output: summed escape iterations of the block.
    private static long BenchmarkBlockDouble(int x0, double cy, double centerX, double halfW, double pixelSize, int maxIter)
    {
        int n = Vector<double>.Count;
        Span<double> grid = stackalloc double[n];
        Span<double> seedAlive = stackalloc double[n];
        Span<double> seedIter = stackalloc double[n];
        bool anyAlive = false;
        for (int i = 0; i < n; i++)
        {
            int gx = x0 + i;
            grid[i] = gx;
            double px = centerX + (gx - halfW) * pixelSize;
            if (IsInteriorBulb(px, cy))
            {
                seedAlive[i] = 0.0;
                seedIter[i] = maxIter;
            }
            else
            {
                seedAlive[i] = 1.0;
                seedIter[i] = 0.0;
                anyAlive = true;
            }
        }
        if (!anyAlive) return (long)n * maxIter;

        var gbVec = new Vector<double>(grid);
        var halfVec = new Vector<double>(halfW);
        var pixelVec = new Vector<double>(pixelSize);
        var cx = new Vector<double>(centerX) + (gbVec - halfVec) * pixelVec;
        var cyVec = new Vector<double>(cy);
        var zx = Vector<double>.Zero;
        var zy = Vector<double>.Zero;
        var zx2 = Vector<double>.Zero;
        var zy2 = Vector<double>.Zero;
        var alive = new Vector<double>(seedAlive);
        var iters = new Vector<double>(seedIter);
        var vFour = new Vector<double>(4.0);
        var vTwo = new Vector<double>(2.0);
        int round = 0;
        while (round < maxIter)
        {
            var escaped = Vector.GreaterThan(zx2 + zy2, vFour);
            alive = Vector.ConditionalSelect(escaped, Vector<double>.Zero, alive);
            if (Vector.EqualsAll(alive, Vector<double>.Zero)) break;
            iters += alive;
            var nzy = vTwo * zx * zy + cyVec;
            var nzx = zx2 - zy2 + cx;
            var nzy2 = nzy * nzy;
            var nzx2 = nzx * nzx;
            zx = Vector.ConditionalSelect(escaped, zx, nzx);
            zy = Vector.ConditionalSelect(escaped, zy, nzy);
            zx2 = Vector.ConditionalSelect(escaped, zx2, nzx2);
            zy2 = Vector.ConditionalSelect(escaped, zy2, nzy2);
            round++;
        }
        long sum = 0;
        for (int i = 0; i < n; i++) sum += (long)iters[i];
        return sum;
    }

    // Automatic iterations based on the zoom: 2000 for the initial view
    // (half side = 1.5) plus 2000 every 10x, i.e. 2000 * (1 + log10(1.5 / half)),
    // with half = scale / 2 (half of the visible width). Clamp 50-50000.
    // Single source for the view, the zoom video and the benchmark zone value.
    // Param scale (double): Input: complex width of the view. Smaller scale (deeper zoom)
    //   yields more iterations; non-positive scales are guarded via double.Epsilon.
    // Returns (int): Output: iteration budget in [50, 50000] (~1944 at the initial view
    //   scale 9.36, 10915 at the benchmark zone).
    public static int AutoIterForScale(double scale)
    {
        double half = Math.Max(double.Epsilon, scale * 0.5);
        double iter = 2000.0 * (1.0 + Math.Log10(1.5 / half));
        if (iter < 50.0) return 50;
        if (iter > 50000.0) return 50000;
        return (int)iter;
    }

    // CPU benchmark: repeats the escape computation (without coloring) for the given
    // budget and counts the total iterations.
    // The progress to the UI is limited (every 3 s) to not skew the measure.
    // Each grid cell is one elementary sample (no SSAA averaging); the metric is
    // frames × samples/frame / seconds, comparable across engines.
    // Param centerX: Input: benchmark zone center, real axis (complex units).
    // Param centerY: Input: benchmark zone center, imaginary axis (complex units).
    // Param scale: Input: complex width of the benchmark zone.
    // Param w: Input: grid width in elementary samples (before supersample).
    // Param h: Input: grid height in elementary samples (before supersample).
    // Param maxIter: Input: iteration budget per sample (auto formula at the zone scale).
    // Param supersample: Input: grid multiplier k; the measured grid is w*k × h*k
    //   (standard uses 1).
    // Param budget: Input: time budget per run (8 s standard); frames repeat until elapsed.
    // Param progress: Input (nullable): UI progress sink, reported at most every
    //   ReportInterval plus a final report; null disables reporting.
    // Param ct: Input: cancellation token; checked each frame and by the Parallel.For.
    // Returns: Output: tuple (TotalIters = summed escape iterations over all frames,
    //   Seconds = effective elapsed seconds, Frames = completed frames).
    public static (long TotalIters, double Seconds, int Frames) BenchmarkCpu(double centerX, double centerY, double scale, int w, int h, int maxIter, int supersample, TimeSpan budget, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        int k = Math.Max(1, supersample);
        int bigW = w * k;
        int bigH = h * k;
        double pixelSize = scale / bigW;
        double topY = centerY - (bigH * 0.5) * pixelSize;

        // The production CPU engine renders through SIMD vectors, so the benchmark
        // measures the same vector core (same workload, same early-outs).
        Func<int, long> row = Vector.IsHardwareAccelerated
            ? y => BenchmarkRowVector(y, centerX, topY, pixelSize, bigW, maxIter)
            : y => BenchmarkRowScalarDouble(y, centerX, topY, pixelSize, bigW, maxIter);
        return RunCpuBenchmark(bigH, budget, progress, ct, row);
    }

    // CPU benchmark, single precision: same standardized workload as BenchmarkCpu
    // but through the float vector core (mirrors the production float render path:
    // double bulb classification, float escape loop). Like CUDA 32-bit vs 64-bit,
    // this is a separate workload with its own history, comparable against the
    // float GPU bars. Same parameter/return contract as BenchmarkCpu.
    // Param centerX: Input: benchmark zone center, real axis (complex units).
    // Param centerY: Input: benchmark zone center, imaginary axis (complex units).
    // Param scale: Input: complex width of the benchmark zone.
    // Param w: Input: grid width in elementary samples (before supersample).
    // Param h: Input: grid height in elementary samples (before supersample).
    // Param maxIter: Input: iteration budget per sample (auto formula at the zone scale).
    // Param supersample: Input: grid multiplier k; the measured grid is w*k × h*k
    //   (standard uses 1).
    // Param budget: Input: time budget per run (8 s standard); frames repeat until elapsed.
    // Param progress: Input (nullable): UI progress sink, reported at most every
    //   ReportInterval plus a final report; null disables reporting.
    // Param ct: Input: cancellation token; checked each frame and by the parallel rows.
    // Returns: Output: tuple (TotalIters = summed escape iterations over all frames,
    //   Seconds = effective elapsed seconds, Frames = completed frames).
    public static (long TotalIters, double Seconds, int Frames) BenchmarkCpuFloat(double centerX, double centerY, double scale, int w, int h, int maxIter, int supersample, TimeSpan budget, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        int k = Math.Max(1, supersample);
        int bigW = w * k;
        int bigH = h * k;
        double pixelSize = scale / bigW;
        double topY = centerY - (bigH * 0.5) * pixelSize;

        Func<int, long> row = Vector.IsHardwareAccelerated
            ? y => BenchmarkRowFloat(y, centerX, topY, pixelSize, bigW, maxIter)
            : y => BenchmarkRowScalarFloat(y, centerX, topY, pixelSize, bigW, maxIter);
        return RunCpuBenchmark(bigH, budget, progress, ct, row);
    }

    // Vector benchmark row, single precision: full lane blocks plus a scalar tail.
    // Same contract as BenchmarkRowVector, float arithmetic throughout (except the
    // double bulb classification, shared with the production float render path).
    private static long BenchmarkRowFloat(int y, double centerX, double topY, double pixelSize, int bigW, int maxIter)
    {
        int n = Vector<float>.Count;
        int blocks = bigW / n;
        double cy = topY + y * pixelSize;
        float fcy = (float)cy;
        double halfW = bigW * 0.5;
        long sum = 0;
        for (int b = 0; b < blocks; b++)
            sum += BenchmarkBlockFloat(b * n, cy, fcy, centerX, halfW, pixelSize, maxIter);
        for (int x = blocks * n; x < bigW; x++)
        {
            double dx = centerX + (x - halfW) * pixelSize;
            if (IsInteriorBulb(dx, cy))
            {
                sum += maxIter;
                continue;
            }
            float cx = (float)dx;
            float zx = 0, zy = 0, zx2 = 0, zy2 = 0;
            int iter = 0;
            while (iter < maxIter && zx2 + zy2 <= 4f)
            {
                zy = 2f * zx * zy + fcy;
                zx = zx2 - zy2 + cx;
                zx2 = zx * zx;
                zy2 = zy * zy;
                iter++;
            }
            sum += iter;
        }
        return sum;
    }

    // Vector escape counting for one lane block, single precision.
    // Lane coordinates replicate the scalar float formula exactly, so block sums
    // are independent of the block decomposition.
    // Param x0 (int): Input: grid column of lane 0.
    // Param cy (double): Input: complex y for the bulb classification.
    // Param fcy (float): Input: complex y for the float loop.
    // Param centerX (double): Input: zone center, real axis.
    // Param halfW (double): Input: bigW * 0.5, precomputed.
    // Param pixelSize (double): Input: complex units per grid step.
    // Param maxIter (int): Input: iteration budget per sample.
    // Returns (long): Output: summed escape iterations of the block.
    private static long BenchmarkBlockFloat(int x0, double cy, float fcy, double centerX, double halfW, double pixelSize, int maxIter)
    {
        int n = Vector<float>.Count;
        Span<float> grid = stackalloc float[n];
        Span<float> seedAlive = stackalloc float[n];
        Span<float> seedIter = stackalloc float[n];
        bool anyAlive = false;
        for (int i = 0; i < n; i++)
        {
            int gx = x0 + i;
            double dx = centerX + (gx - halfW) * pixelSize;
            grid[i] = (float)dx;
            if (IsInteriorBulb(dx, cy))
            {
                seedAlive[i] = 0f;
                seedIter[i] = maxIter;
            }
            else
            {
                seedAlive[i] = 1f;
                seedIter[i] = 0f;
                anyAlive = true;
            }
        }
        if (!anyAlive) return (long)n * maxIter;

        var cx = new Vector<float>(grid);
        var cyVec = new Vector<float>(fcy);
        var zx = Vector<float>.Zero;
        var zy = Vector<float>.Zero;
        var zx2 = Vector<float>.Zero;
        var zy2 = Vector<float>.Zero;
        var alive = new Vector<float>(seedAlive);
        var iters = new Vector<float>(seedIter);
        var vFour = new Vector<float>(4f);
        var vTwo = new Vector<float>(2f);
        int round = 0;
        while (round < maxIter)
        {
            var escaped = Vector.GreaterThan(zx2 + zy2, vFour);
            alive = Vector.ConditionalSelect(escaped, Vector<float>.Zero, alive);
            if (Vector.EqualsAll(alive, Vector<float>.Zero)) break;
            iters += alive;
            var nzy = vTwo * zx * zy + cyVec;
            var nzx = zx2 - zy2 + cx;
            var nzy2 = nzy * nzy;
            var nzx2 = nzx * nzx;
            zx = Vector.ConditionalSelect(escaped, zx, nzx);
            zy = Vector.ConditionalSelect(escaped, zy, nzy);
            zx2 = Vector.ConditionalSelect(escaped, zx2, nzx2);
            zy2 = Vector.ConditionalSelect(escaped, zy2, nzy2);
            round++;
        }
        long sum = 0;
        for (int i = 0; i < n; i++) sum += (long)iters[i];
        return sum;
    }
}
