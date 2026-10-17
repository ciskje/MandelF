// Mandelbrot CUDA kernels, compiled with nvcc to PTX at build time.
// One thread per output pixel (render) or per sample (benchmark).
// Same math as the CPU path (Mandelbrot.cs): incremental squares loop,
// cardioid/bulb early-out in double, 4096-entry palette LUT with lerp,
// smooth log/log correction, Julia branch, tiled global coordinates.
// Compiled with FMA contraction enabled (nvcc/NVRTC default) for speed:
// fused multiply-adds may round boundary pixels differently than the CPU
// (rare single-pixel flips, amplified only in 32-bit deep zoom).

typedef struct
{
    double CenterX;
    double CenterY;
    double PixelSize;
    double TopY;
    int W;
    int H;
    int MaxIter;
    int Supersample;
    int JuliaOn;
    double Jcx;
    double Jcy;
    int FullW;
    int FullH;
    int OffsetX;
    int OffsetY;
} GpuViewParams;

#define LUT_SIZE 4096

// Layout check: 4 doubles (32) + 5 ints (20) + pad (4) + 2 doubles (16) + 4 ints (16) = 88.
static_assert(sizeof(GpuViewParams) == 88, "GpuViewParams must be 88 bytes to match C# layout");

// Main cardioid + period-2 bulb test. Conservative in exact math
// (only true interior). Float coordinates are promoted to double,
// like the CPU float render path.
__device__ inline bool IsInteriorBulbD(double px, double py)
{
    double q = (px - 0.25) * (px - 0.25) + py * py;
    if (q * (q + (px - 0.25)) <= 0.25 * py * py) return true;
    double dx = px + 1.0;
    return dx * dx + py * py <= 0.0625;
}

// Palette lookup through the cached device table (same entries as
// PaletteColors.GetLut on the CPU): linear interpolation, no banding.
// Bit-mirrors PaletteColors.ColorFromLut (position and channel lerp run in
// double from a precomputed 1/maxIter, then truncate; FMA may fuse the
// final adds, unlike the CPU).
// Param lut: device table of LUT_SIZE packed ARGB colors (gamma baked).
// Param smooth: smoothed escape value nu. Param maxIter: view budget.
// Returns packed 32-bit ARGB color (alpha always 0xFF).
__device__ inline int LutColor(const int* __restrict__ lut, double smooth, int maxIter)
{
    double inv = 1.0 / (double)(maxIter > 0 ? maxIter : 1);
    double pos = smooth * inv * (double)LUT_SIZE - 0.5;
    if (pos <= 0.0) return lut[0];
    if (pos >= (double)(LUT_SIZE - 1)) return lut[LUT_SIZE - 1];
    int i0 = (int)pos;
    double f = pos - (double)i0;
    int c0 = lut[i0], c1 = lut[i0 + 1];
    double r = (double)((c0 >> 16) & 255) + f * (double)(((c1 >> 16) & 255) - ((c0 >> 16) & 255));
    double g = (double)((c0 >> 8) & 255) + f * (double)(((c1 >> 8) & 255) - ((c0 >> 8) & 255));
    double b = (double)(c0 & 255) + f * (double)((c1 & 255) - (c0 & 255));
    return (int)(0xFF000000u | ((unsigned int)r << 16) | ((unsigned int)g << 8) | (unsigned int)b);
}

// Benchmark kernel, single precision: escape-iteration count of one
// elementary sample, no coloring, no smoothing, no SSAA averaging.
// 2D grid: thread (x, y) maps directly to sample (x, y), so no integer
// division/modulo is needed (the rasterizer-equivalent layout; consecutive
// x threads still write consecutive words, fully coalesced).
extern "C" __global__ void FloatBenchKernel(int* __restrict__ iters, GpuViewParams p)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;
    if (x >= p.W || y >= p.H) return;
    int idx = y * p.W + x;
    float pixel = (float)p.PixelSize;
    float cx = (float)p.CenterX + ((float)x - (float)p.W * 0.5f) * pixel;
    float cy = (float)p.TopY + (float)y * pixel;

    float zx = 0.0f, zy = 0.0f, zx2 = 0.0f, zy2 = 0.0f;
    int iter = 0;
    while (iter < p.MaxIter && zx2 + zy2 <= 4.0f)
    {
        zy = 2.0f * zx * zy + cy;
        zx = zx2 - zy2 + cx;
        zx2 = zx * zx;
        zy2 = zy * zy;
        ++iter;
    }
    iters[idx] = iter;
}

// Benchmark kernel, double precision: same contract in float64 (2D grid,
// see FloatBenchKernel above).
extern "C" __global__ void DoubleBenchKernel(int* __restrict__ iters, GpuViewParams p)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;
    if (x >= p.W || y >= p.H) return;
    int idx = y * p.W + x;
    double cx = p.CenterX + ((double)x - (double)p.W * 0.5) * p.PixelSize;
    double cy = p.TopY + (double)y * p.PixelSize;

    double zx = 0.0, zy = 0.0, zx2 = 0.0, zy2 = 0.0;
    int iter = 0;
    while (iter < p.MaxIter && zx2 + zy2 <= 4.0)
    {
        zy = 2.0 * zx * zy + cy;
        zx = zx2 - zy2 + cx;
        zx2 = zx * zx;
        zy2 = zy * zy;
        ++iter;
    }
    iters[idx] = iter;
}

// Render kernel, single precision: full on-chip SSAA color of one output
// pixel. Same math as ComputePixelFloat: cardioid test in double on the
// host-order coordinates, escape and smooth in float, integer accumulation
// (FMA contraction may flip rare boundary pixels vs the CPU).
// 2D grid: thread (x, y) maps directly to output pixel (x, y), no index
// division (thread/block layout shared with the benchmark kernels).
// Param idx contract, tile mapping and LUT averaging as in DoubleKernel below.
extern "C" __global__ void FloatKernel(int* __restrict__ pixels, GpuViewParams p, const int* __restrict__ lut)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;
    if (x >= p.W || y >= p.H) return;
    int idx = y * p.W + x;
    int k = p.Supersample;
    // Exact host op order (Mandelbrot.cs RenderTile/ComputePixelFloat).
    double originX = p.CenterX - (double)(p.FullW * k) * 0.5 * p.PixelSize;
    long long sumR = 0, sumG = 0, sumB = 0;
    for (int sy = 0; sy < k; sy++)
    {
        int globalBy = (p.OffsetY + y) * k + sy;
        double py = p.TopY + (double)globalBy * p.PixelSize;
        for (int sx = 0; sx < k; sx++)
        {
            int globalBx = (p.OffsetX + x) * k + sx;
            double dpx = originX + (double)globalBx * p.PixelSize;
            float px = (float)dpx;
            float fpy = (float)py;
            int color;
            if (p.JuliaOn == 0 && IsInteriorBulbD(dpx, py))
            {
                color = (int)0xFF000000;
            }
            else
            {
                float zx = p.JuliaOn != 0 ? px : 0.0f;
                float zy = p.JuliaOn != 0 ? fpy : 0.0f;
                float ccx = p.JuliaOn != 0 ? (float)p.Jcx : px;
                float ccy = p.JuliaOn != 0 ? (float)p.Jcy : fpy;
                float zx2 = zx * zx, zy2 = zy * zy;
                int iter = 0;
                while (iter < p.MaxIter && zx2 + zy2 <= 4.0f)
                {
                    zy = 2.0f * zx * zy + ccy;
                    zx = zx2 - zy2 + ccx;
                    zx2 = zx * zx;
                    zy2 = zy * zy;
                    ++iter;
                }
                float smoothIterations = iter >= p.MaxIter
                    ? (float)p.MaxIter
                    : (float)iter + 1.0f - log2f(0.5f * logf(fmaxf(zx2 + zy2, 4.0f)));
                color = iter >= p.MaxIter
                    ? (int)0xFF000000
                    : LutColor(lut, (double)smoothIterations, p.MaxIter);
            }
            sumR += (color >> 16) & 255;
            sumG += (color >> 8) & 255;
            sumB += color & 255;
        }
    }
    long long samples = (long long)k * k;
    pixels[idx] = (int)(0xFF000000u | ((unsigned int)(sumR / samples) << 16) | ((unsigned int)(sumG / samples) << 8) | (unsigned int)(sumB / samples));
}

// Render kernel, double precision: full on-chip SSAA color of one output
// pixel. Same math as ComputePixelDouble: host-order originX coordinates,
// integer channel accumulation with truncating average (FMA contraction may
// flip rare boundary pixels vs the CPU).
// The thread loops over its k x k subsamples on the global k-times grid;
// VRAM traffic stays O(W x H), the W*k x H*k grid is never materialized.
// 2D grid like FloatKernel above (no index division).
extern "C" __global__ void DoubleKernel(int* __restrict__ pixels, GpuViewParams p, const int* __restrict__ lut)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;
    if (x >= p.W || y >= p.H) return;
    int idx = y * p.W + x;
    int k = p.Supersample;
    // Exact host op order (Mandelbrot.cs RenderTile/ComputePixelDouble).
    double originX = p.CenterX - (double)(p.FullW * k) * 0.5 * p.PixelSize;
    long long sumR = 0, sumG = 0, sumB = 0;
    for (int sy = 0; sy < k; sy++)
    {
        int globalBy = (p.OffsetY + y) * k + sy;
        double py = p.TopY + (double)globalBy * p.PixelSize;
        for (int sx = 0; sx < k; sx++)
        {
            int globalBx = (p.OffsetX + x) * k + sx;
            double px = originX + (double)globalBx * p.PixelSize;
            int color;
            if (p.JuliaOn == 0 && IsInteriorBulbD(px, py))
            {
                color = (int)0xFF000000;
            }
            else
            {
                double zx = p.JuliaOn != 0 ? px : 0.0;
                double zy = p.JuliaOn != 0 ? py : 0.0;
                double ccx = p.JuliaOn != 0 ? p.Jcx : px;
                double ccy = p.JuliaOn != 0 ? p.Jcy : py;
                double zx2 = zx * zx, zy2 = zy * zy;
                int iter = 0;
                while (iter < p.MaxIter && zx2 + zy2 <= 4.0)
                {
                    zy = 2.0 * zx * zy + ccy;
                    zx = zx2 - zy2 + ccx;
                    zx2 = zx * zx;
                    zy2 = zy * zy;
                    ++iter;
                }
                double smoothIterations = iter >= p.MaxIter
                    ? (double)p.MaxIter
                    : (double)iter + 1.0 - log2(0.5 * log(fmax(zx2 + zy2, 4.0)));
                color = iter >= p.MaxIter
                    ? (int)0xFF000000
                    : LutColor(lut, smoothIterations, p.MaxIter);
            }
            sumR += (color >> 16) & 255;
            sumG += (color >> 8) & 255;
            sumB += color & 255;
        }
    }
    long long samples = (long long)k * k;
    pixels[idx] = (int)(0xFF000000u | ((unsigned int)(sumR / samples) << 16) | ((unsigned int)(sumG / samples) << 8) | (unsigned int)(sumB / samples));
}
