using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Mathematics;
using DxgiFormat = Vortice.DXGI.Format;

namespace MandelbrotViewer;

// Realtime DirectX 11 backend: fullscreen triangle + HLSL pixel shader that computes
// the fractal every frame (float). Presents to a swapchain bound to the panel's handle.
internal static class DxMandelbrot
{
    // Fullscreen triangle without a vertex buffer (SV_VertexID).
    // VS inputs/outputs: input id (uint, SV_VertexID: 0, 1, 2 for the three
    // triangle corners, no vertex buffer bound); output SV_Position clip-space
    // position covering the whole render target (the pixel shader then runs once
    // per pixel). The bit trick expands the 3 ids to (-1,+1), (+3,+1), (-1,-3).
    private const string VsSource = @"
float4 VS(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}";

    // Render pixel shader with on-chip SSAA. Cbuffer Params inputs: cx/cy (view
    // center, complex units), scale (complex width), aspect (fullH/fullW, keeps the
    // complex height proportional), invW/invH (1/fullW, 1/fullH for pixel mapping),
    // maxIter (escape loop bound), aa (SSAA factor n, >=1), fullW/fullH + offsetX/offsetY
    // (tile -> whole-image mapping, keeps tiled export pixel-identical), jcx/jcy + julia
    // (Julia mode: z(0) = pixel, c = constant; else Mandelbrot).
    // Palette: sampled from the Lut texture (same entries as the CPU table, gamma
    // baked; hardware linear filtering interpolates between entries, no banding).
    // Output: SV_Target float4, RGB = average of the n x n subsamples, A = 1.
    private const string PsSource = @"
cbuffer Params : register(b0)
{
    float cx; float cy; float scale; float aspect;
    float invW; float invH; int maxIter; int aa;
    int fullW; int fullH; int offsetX; int offsetY;
    float jcx; float jcy; int julia; int jpad;
};

Texture1D<float4> Lut : register(t0);
SamplerState LutSampler : register(s0);

// Graded: palette lookup through the Lut texture, aligned with the CPU table
// (PaletteColors.GetLut) and GpuMandelbrot.LutColor (CUDA).
// Input raw (float: smoothed iterations / maxIter, 0..1, gamma baked in the table).
// Output float3: hardware-lerped RGB between adjacent entries.
float3 Graded(float raw)
{
    return Lut.SampleLevel(LutSampler, saturate(raw), 0).rgb;
}

// InteriorBulb: main cardioid + period-2 bulb test, mirrors the CPU early-out.
// Input c (float2: pixel complex coordinates, i.e. the Mandelbrot constant).
// Output bool: true when the point is known interior (renders black, loop skipped).
bool InteriorBulb(float2 c)
{
    float2 q = c - float2(0.25, 0.0);
    float q2 = dot(q, q);
    if (q2 * (q2 + q.x) <= 0.25 * c.y * c.y) return true;
    float2 d = c + float2(1.0, 0.0);
    return dot(d, d) <= 0.0625;
}

// PS: full on-chip SSAA color of one output pixel.
// Input pos (float4, SV_Position: pixel coordinates within the current render
// target, i.e. the tile when exporting). Centered sub-pixel offsets
// ((jx,jy)+0.5)/n-0.5 distribute the n x n samples around the pixel center.
// Output SV_Target float4: acc/(n*n) averaged RGB (interior = black), A = 1.
// Per subsample: pixel -> complex (px), cardioid/bulb early-out (Mandelbrot only),
// escape loop with smooth log/log correction, LUT palette lookup. No W*n x H*n
// target: VRAM stays O(W x H).
float4 PS(float4 pos : SV_Position) : SV_Target
{
    int n = max(aa, 1);
    float3 acc = 0.0;
    for (int jy = 0; jy < n; jy++)
    {
        for (int jx = 0; jx < n; jx++)
        {
            float2 sub = (float2((float)jx, (float)jy) + 0.5) / (float)n - 0.5;
            float2 p = float2((float)offsetX, (float)offsetY) + pos.xy + sub;
            float2 px = float2(cx + (p.x * invW - 0.5) * scale,
                               cy + (p.y * invH - 0.5) * scale * aspect);
            float3 col = 0.0;
            // Cardioid + period-2 bulb early-out (Mandelbrot only, mirrors the CPU):
            // interior points skip the escape loop entirely.
            if (julia == 0 && InteriorBulb(px))
            {
                // col stays black, no work.
            }
            else
            {
                // Julia: z(0) = pixel point, c = constant; Mandelbrot: z(0) = 0, c = pixel.
                float2 z = julia != 0 ? px : 0.0;
                float2 cc = julia != 0 ? float2(jcx, jcy) : px;
                int iter = 0;
                while (iter < maxIter && dot(z, z) <= 4.0)
                {
                    z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + cc;
                    iter++;
                }
                if (iter < maxIter)
                {
                    float mod2 = max(dot(z, z), 4.0);
                    float smoothIterations = (float)iter + 1.0 - log(log(sqrt(mod2))) / log(2.0);
                    // Raw position into the Lut texture (gamma baked in the table,
                    // aligned with the CPU table and GpuMandelbrot.LutColor).
                    col = Graded(saturate(smoothIterations / (float)maxIter));
                }
            }
            acc += col;
        }
    }
    return float4(acc / (float)(n * n), 1.0);
}";

    // Benchmark shader: only the iteration count of the fractal, no
    // coloring, no smooth and no sample averaging. Each pixel of the
    // grid is an elementary sample: identical to the work of the CUDA/CPU kernels.
    private const string BenchPsSource = @"
// BenchPS: iterations-only benchmark shader, no coloring, no smoothing, no SSAA.
// Cbuffer Params inputs: cx/cy (zone center), scale (complex width), aspect
// (gridH/gridW), invW/invH (1/gridW, 1/gridH for sample mapping), maxIter
// (loop bound); aa unused, always 1 (each grid pixel is one elementary sample).
// Input pos (float4, SV_Position: sample coordinates within the grid target).
// Output SV_Target float4: grayscale frac(iter*0.125) (black when iter >= maxIter).
// The value only pins the loop against dead-code elimination; the benchmark counts
// frames completed (event queries), not pixel values. Same incremental-squares loop
// as GpuMandelbrot.FloatBenchKernel, so both engines measure the identical workload.
cbuffer Params : register(b0)
{
    float cx; float cy; float scale; float aspect;
    float invW; float invH; int maxIter; int aa;
};

float4 BenchPS(float4 pos : SV_Position) : SV_Target
{
    float2 c = float2(cx + (pos.x * invW - 0.5) * scale,
                        cy + (pos.y * invH - 0.5) * scale * aspect);
    // Same formulation as the CUDA benchmark kernel (GpuMandelbrot.FloatBenchKernel):
    // incremental squares, so the two engines measure the identical workload.
    float zx = 0.0, zy = 0.0, zx2 = 0.0, zy2 = 0.0;
    int iter = 0;
    while (iter < maxIter && zx2 + zy2 <= 4.0)
    {
        zy = 2.0 * zx * zy + c.y;
        zx = zx2 - zy2 + c.x;
        zx2 = zx * zx;
        zy2 = zy * zy;
        iter++;
    }
    // The output depends on the iterations: the compiler cannot eliminate the loop.
    float v = iter >= maxIter ? 0.0 : frac(float(iter) * 0.125);
    return float4(v, v, v, 1.0);
}
";

    [StructLayout(LayoutKind.Sequential)]
    private struct DxParams
    {
        public float Cx, Cy, Scale, Aspect;
        public float InvW, InvH;
        public int MaxIter, Aa;
        public int FullW, FullH, OffsetX, OffsetY;
        public float JuliaCx, JuliaCy;
        public int JuliaOn, JuliaPad;
    }

    private static ID3D11Device? _device;
    private static ID3D11DeviceContext? _context;
    private static IDXGISwapChain1? _swapChain;
    private static ID3D11RenderTargetView? _rtv;
    private static ID3D11VertexShader? _vs;
    private static ID3D11PixelShader? _ps;
    private static ID3D11Buffer? _cbuffer;
    private static ID3D11PixelShader? _benchPs;
    private static int _backWidth, _backHeight;

    // Offscreen benchmark (v2.5.20): render target in the tested GPU's memory,
    // no swapchain and no Present — no DWM, no cross-GPU copy to the
    // monitor's card (which skewed the headless cards, see SPECS).
    // Draw returns immediately (commands queued): frame completion is detected
    // with a ring of event queries.
    private static ID3D11Texture2D? _benchTarget;
    private static ID3D11RenderTargetView? _benchRtv;
    private static ID3D11Query?[] _benchQueries = Array.Empty<ID3D11Query?>();
    private static int _benchGridW, _benchGridH;

    // Palette table for the colored shader: 1D RGBA texture with the CPU table
    // entries (gamma baked), hardware linear filtering interpolates between
    // entries. Refilled when palette or iteration budget changes.
    private static ID3D11Texture1D? _lutTex;
    private static ID3D11ShaderResourceView? _lutSrv;
    private static ID3D11SamplerState? _lutSampler;
    private static Palette _lutPalette = (Palette)(-1);

    public static bool IsReady => _device != null;
    public static string LastError { get; private set; } = "";
    // Name of the video card currently in use ("" if not initialized).
    public static string AdapterName { get; private set; } = "";

    // Dedicated + shared memory of the adapter in use (ulong.MaxValue if unknown).
    public static ulong AdapterDedicatedBytes { get; private set; } = ulong.MaxValue;
    public static ulong AdapterSharedBytes { get; private set; } = ulong.MaxValue;

    // Error of the last failed enumeration (diagnostic), empty if OK.
    public static string EnumerationError { get; private set; } = "";

    // Available hardware video cards (software WARP renderers excluded).
    // Note:
    // Do NOT read or compare DedicatedVideoMemory directly: it is a
    // PointerUSize (SIZE_T) and the implicit conversion of SharpGen goes through 32 bits
    // (UIntPtr.ToUInt32), which throws OverflowException with GPUs of more than 4 GB — that is
    // why the enumeration stayed empty on machines with modern GPUs
    // (cf. note v2.3.8 "wrapper overflow"). The software renderers are
    // excluded by name: "Microsoft Basic Render Driver" is the WARP of D3D11.
    public static IReadOnlyList<string> AdapterNames()
    {
        var names = new List<string>();
        EnumerationError = "";
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; i < 32; i++)
            {
                if (factory.EnumAdapters(i, out IDXGIAdapter adapter).Failure) break;
                using (adapter)
                {
                    string name = adapter.Description.Description;
                    if (!name.StartsWith("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            EnumerationError = $"{ex.GetType().Name}: {ex.Message}";
        }
        return names;
    }

    // Reads the memory of the chosen adapter (or of the first hardware one if auto).
    // DXGI sizes above 4 GB overflow the wrapper's 32-bit conversion:
    // in that case (or if the adapter is not found) it stays ulong.MaxValue
    // = abundant/unknown memory and the VRAM check is skipped.
    private static void RefreshAdapterMemory(string? adapterName)
    {
        AdapterDedicatedBytes = ulong.MaxValue;
        AdapterSharedBytes = ulong.MaxValue;
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; i < 32; i++)
            {
                if (factory.EnumAdapters(i, out IDXGIAdapter adapter).Failure) break;
                using (adapter)
                {
                    string name = adapter.Description.Description;
                    if (name.StartsWith("Microsoft Basic", StringComparison.OrdinalIgnoreCase)) continue;
                    if (adapterName != null &&
                        !string.Equals(name, adapterName, StringComparison.OrdinalIgnoreCase)) continue;
                    var d = adapter.Description;
                    AdapterDedicatedBytes = SafeMemBytes(() => (ulong)(d.DedicatedVideoMemory / (1024 * 1024)) * 1024ul * 1024ul);
                    AdapterSharedBytes = SafeMemBytes(() => (ulong)(d.SharedSystemMemory / (1024 * 1024)) * 1024ul * 1024ul);
                    return;
                }
            }
        }
        catch { /* unknown memory: no check */ }
    }

    private static ulong SafeMemBytes(Func<ulong> read)
    {
        try { return read(); }
        catch { return ulong.MaxValue; }
    }

    // Param adapterName (string?): Card to use (exact DXGI name); null = default hardware adapter.
    //   With a requested card, the device is created explicitly on the chosen adapter.
    public static bool TryInitialize(IntPtr hwnd, int width, int height, string? adapterName = null)
    {
        if (!EnsureDevice(adapterName))
            return false;
        LastError = "";
        string step = "start";
        try
        {
            step = "DXGI.CreateDXGIFactory2";
            using IDXGIFactory2 factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);

            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var desc = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = DxgiFormat.R8G8B8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Ignore,
            };
            var fullscreen = new SwapChainFullscreenDescription { Windowed = true };
            step = "IDXGIFactory2.CreateSwapChainForHwnd";
            _swapChain = factory.CreateSwapChainForHwnd(_device!, hwnd, desc, fullscreen);

            step = "CreateViews (render target)";
            CreateViews(width, height);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"[{step}] {ex.GetType().Name}: {ex.Message}";
            Dispose();
            return false;
        }
    }

    // Initializes device, context and shaders on the chosen card without a window or a
    // swapchain (headless): for the offscreen benchmark, which presents nothing.
    // Param adapterName (string?): Card to use (exact DXGI name); null = default.
    public static bool TryInitializeHeadless(string? adapterName = null) =>
        EnsureDevice(adapterName);

    // Creates (or reuses) device, context and shaders on the chosen adapter. The swapchain
    // stays the responsibility of TryInitialize (it needs a window).
    private static bool EnsureDevice(string? adapterName)
    {
        bool same = adapterName == null ||
            string.Equals(AdapterName, adapterName, StringComparison.OrdinalIgnoreCase);
        if (IsReady)
        {
            if (same) return true;
            Dispose(); // card change: the device must be recreated on the chosen adapter
        }
        LastError = "";
        string step = "start";
        try
        {
            step = "DXGI.CreateDXGIFactory2";
            using IDXGIFactory2 factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);

            // If the user chose a card, find the matching DXGI adapter
            // and pass it explicitly to D3D11CreateDevice (with DriverType.Unknown, as
            // required when passing an adapter); otherwise the default adapter.
            IDXGIAdapter? chosen = null;
            if (adapterName != null)
            {
                step = "searching for the requested DXGI adapter";
                for (uint i = 0; i < 32; i++)
                {
                    if (factory.EnumAdapters(i, out IDXGIAdapter adapter).Failure) break;
                    if (string.Equals(adapter.Description.Description, adapterName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = adapter; // the dispose happens after the device creation
                        break;
                    }
                    adapter.Dispose();
                }
                if (chosen == null)
                    throw new InvalidOperationException($"Video card not found: {adapterName}");
            }

            step = chosen != null
                ? $"D3D11CreateDevice (chosen adapter: {adapterName})"
                : "D3D11CreateDevice (default hardware adapter)";
            var result = D3D11.D3D11CreateDevice(
                chosen, chosen != null ? DriverType.Unknown : DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_0 },
                out ID3D11Device device,
                out FeatureLevel level,
                out ID3D11DeviceContext context);
            chosen?.Dispose(); // the device keeps its own reference to the adapter
            if (result.Failure)
                throw new InvalidOperationException($"D3D11CreateDevice failed (HRESULT 0x{result.Code:X8})");
            _device = device;
            _context = context;
            AdapterName = adapterName ?? "Auto (default hardware adapter)";
            RefreshAdapterMemory(adapterName);

            step = "D3DCompiler.Compile (shaders)";
            ReadOnlyMemory<byte> vsCode = Compiler.Compile(VsSource, "VS", "mandelbrot-vs", "vs_5_0");
            ReadOnlyMemory<byte> psCode = Compiler.Compile(PsSource, "PS", "mandelbrot-ps", "ps_5_0");
            _vs = _device.CreateVertexShader(vsCode.Span);
            _ps = _device.CreatePixelShader(psCode.Span);

            step = "D3DCompiler.Compile (benchmark shader)";
            ReadOnlyMemory<byte> benchCode = Compiler.Compile(BenchPsSource, "BenchPS", "mandelbrot-bench-ps", "ps_5_0");
            _benchPs = _device.CreatePixelShader(benchCode.Span);

            step = "CreateBuffer (constant buffer)";
            _cbuffer = _device.CreateBuffer(
                (uint)Marshal.SizeOf<DxParams>(),
                BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write);

            step = "CreateSamplerState (palette table sampler)";
            _lutSampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
            });

            return true;
        }
        catch (Exception ex)
        {
            LastError = $"[{step}] {ex.GetType().Name}: {ex.Message}";
            Dispose();
            return false;
        }
    }

    // Refills the palette texture when palette or iteration budget changed.
    // Same entries as PaletteColors.GetLut (packed ARGB); the shader samples them
    // with linear filtering, so adjacent entries interpolate like the CPU table.
    // Param palette (Palette): Input: palette baked into the texture.
    private static void EnsureLut(Palette palette)
    {
        if (_device == null || _context == null)
            throw new InvalidOperationException("DirectX not initialized.");
        if (_lutTex != null && _lutSrv != null && _lutPalette == palette)
            return;
        int[] table = PaletteColors.GetLut(palette);
        _lutSrv?.Dispose(); _lutSrv = null;
        _lutTex?.Dispose();
        var desc = new Texture1DDescription
        {
            Width = (uint)table.Length,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None,
        };
        _lutTex = _device.CreateTexture1D(desc);
        _lutSrv = _device.CreateShaderResourceView(_lutTex);
        // Packed ARGB int -> R,G,B,A bytes (R8G8B8A8 expects R first in memory).
        MappedSubresource mapped = _context.Map(_lutTex, 0, MapMode.WriteDiscard);
        try
        {
            unsafe
            {
                byte* dst = (byte*)mapped.DataPointer;
                for (int i = 0; i < table.Length; i++)
                {
                    int c = table[i];
                    dst[i * 4 + 0] = (byte)((c >> 16) & 0xFF);
                    dst[i * 4 + 1] = (byte)((c >> 8) & 0xFF);
                    dst[i * 4 + 2] = (byte)(c & 0xFF);
                    dst[i * 4 + 3] = 0xFF;
                }
            }
        }
        finally
        {
            _context.Unmap(_lutTex, 0);
        }
        _lutPalette = palette;
    }

    private static void CreateViews(int width, int height)
    {
        _backWidth = width;
        _backHeight = height;
        _rtv?.Dispose();
        using ID3D11Texture2D backbuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device!.CreateRenderTargetView(backbuffer);
        _context!.RSSetViewport(new Viewport(width, height));
    }

    public static void Resize(int width, int height)
    {
        if (!IsReady) return;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _backWidth && height == _backHeight) return;
        _rtv?.Dispose();
        _rtv = null;
        _swapChain!.ResizeBuffers(0, (uint)width, (uint)height, DxgiFormat.Unknown, SwapChainFlags.None);
        CreateViews(width, height);
    }

    // Builds the parameters of a colored frame (palette comes from the Lut texture).
    private static DxParams BuildParams(double centerX, double centerY, double scale, int width, int height, int maxIter, int aa,
        int fullWidth = 0, int fullHeight = 0, int offsetX = 0, int offsetY = 0,
        double juliaCx = 0, double juliaCy = 0, bool julia = false)
    {
        fullWidth = fullWidth > 0 ? fullWidth : width;
        fullHeight = fullHeight > 0 ? fullHeight : height;
        return new DxParams
        {
            Cx = (float)centerX,
            Cy = (float)centerY,
            Scale = (float)scale,
            Aspect = (float)fullHeight / Math.Max(1, fullWidth),
            InvW = 1f / Math.Max(1, fullWidth),
            InvH = 1f / Math.Max(1, fullHeight),
            MaxIter = maxIter,
            Aa = Math.Max(1, aa),
            FullW = fullWidth,
            FullH = fullHeight,
            OffsetX = offsetX,
            OffsetY = offsetY,
            JuliaCx = (float)juliaCx,
            JuliaCy = (float)juliaCy,
            JuliaOn = julia ? 1 : 0,
        };
    }

    // Pipeline common to all frames (Render, offscreen benchmark, RenderPreviewToBitmap):
    // constants, shaders, palette table binding, render target, viewport and draw
    // of the fullscreen triangle. The Lut binding is skipped while the table is
    // missing (benchmark-only sessions); the benchmark shader samples nothing.
    private static void DrawFrame(DxParams pars, ID3D11RenderTargetView rtv, ID3D11PixelShader ps, int width, int height)
    {
        MappedSubresource mapped = _context!.Map(_cbuffer!, 0, MapMode.WriteDiscard);
        mapped.AsSpan<DxParams>(1)[0] = pars;
        _context.Unmap(_cbuffer!, 0);

        _context.OMSetRenderTargets(rtv);
        _context.VSSetShader(_vs);
        _context.PSSetShader(ps);
        _context.PSSetConstantBuffer(0, _cbuffer!);
        if (_lutSrv != null && _lutSampler != null)
        {
            _context.PSSetShaderResource(0, _lutSrv);
            _context.PSSetSampler(0, _lutSampler);
        }
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.RSSetViewport(new Viewport(width, height));
        _context.Draw(3, 0);
    }

    public static void Render(double centerX, double centerY, double scale, int width, int height, int maxIter, int aa, Palette palette, double juliaCx = 0, double juliaCy = 0, bool julia = false)
    {
        if (!IsReady) return;
        EnsureLut(palette);
        DrawFrame(BuildParams(centerX, centerY, scale, width, height, maxIter, aa,
            juliaCx: juliaCx, juliaCy: juliaCy, julia: julia), _rtv!, _ps!, width, height);
        _swapChain!.Present(0, PresentFlags.None);
    }

    // Prepares the offscreen benchmark: render target in the GPU memory at the
    // dimensions of the elementary samples grid (e.g. 960x540 AA1x =
    // 960x540, ~2 MB in R8G8B8A8) plus a ring of event queries to detect
    // the real completion of the frames. No swapchain, no Present.
    public static void BeginBenchmarkOffscreen(int width, int height)
    {
        if (!IsReady) throw new InvalidOperationException("DirectX not initialized.");
        EndBenchmarkOffscreen();
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        // Memory estimate: R8G8B8A8 render target (4 bytes/pixel) x2 margin.
        // If the adapter does not have enough RAM the driver goes into TDR/stall without errors:
        // better a clear error right away than an infinite hang in DrainOne.
        ulong need = (ulong)width * (ulong)height * 4ul * 2ul;
        ulong have = AdapterDedicatedBytes >= ulong.MaxValue - AdapterSharedBytes
            ? ulong.MaxValue : AdapterDedicatedBytes + AdapterSharedBytes;
        if (have != ulong.MaxValue && need > have)
        {
            string msg = "Insufficient GPU memory for the benchmark (" + width + "x" + height + " = ~" + (need / 1048576) + " MB required, ~" + (have / 1048576) + " MB on " + AdapterName + "): reduce AA or use another card.";
            throw new InvalidOperationException(msg);
        }

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        _benchTarget = _device!.CreateTexture2D(desc);
        _benchRtv = _device.CreateRenderTargetView(_benchTarget);
        _benchQueries = new ID3D11Query?[BenchFlightMax];
        for (int i = 0; i < _benchQueries.Length; i++)
            _benchQueries[i] = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        _benchGridW = width;
        _benchGridH = height;
    }

    // Releases the offscreen benchmark resources.
    public static void EndBenchmarkOffscreen()
    {
        if (_benchQueries != null)
        {
            foreach (var q in _benchQueries)
                q?.Dispose();
            _benchQueries = Array.Empty<ID3D11Query?>();
        }
        _benchRtv?.Dispose(); _benchRtv = null;
        _benchTarget?.Dispose(); _benchTarget = null;
        // Restore the swapchain render target and viewport for subsequent renders.
        if (_rtv != null && _context != null)
        {
            _context.OMSetRenderTargets(_rtv);
            _context.RSSetViewport(new Viewport(_backWidth, _backHeight));
        }
    }

    // Frames in flight: how many Draws stay queued before waiting for the oldest to
    // complete. A deep queue keeps the GPU saturated so the measured rate is the card's
    // true compute throughput, not the per-frame submit/wait round-trip (shallow queues
    // starve the GPU: probe on 5070 Ti, 4 in flight ~150 vs 256 in flight ~337 MPix/s).
    // The depth is sized at run time from the measured frame time so that slow cards
    // never queue more than ~BenchTdrSafeSeconds of work (avoids TDR / device-removed),
    // while fast cards run at full depth.
    private const int BenchFlightMin = 4;
    private const int BenchFlightMax = 256;
    private const double BenchTdrSafeSeconds = 0.8;
    private static int _benchInflight = BenchFlightMin;

    // Standard offscreen measurement loop: renders iterations-only frames on the
    // samples grid for the given budget and returns the effective seconds and the number
    // of frames really completed by the GPU (event query). Shared by the benchmark
    // GUI and by `--bench-dx`. No Present: DWM and cross-GPU copy excluded.
    // The pipeline state (constants, shaders, render target, viewport) is set once
    // before the loop: the benchmark parameters are constant, and per-frame Map/state
    // calls would stall the CPU on the in-flight frames and depress the throughput.
    // Per frame only Draw + End(query) remain.
    // Param tick: Optional periodic callback (completed frames, seconds).
    public static (double Seconds, int Frames) RunBenchmarkFramesOffscreen(double centerX, double centerY, double scale, int gridW, int gridH, int maxIter, TimeSpan budget, Action<int, double>? tick, CancellationToken ct)
    {
        if (_benchRtv == null || _benchQueries.Length == 0)
            throw new InvalidOperationException("Offscreen benchmark not prepared (BeginBenchmarkOffscreen).");

        var pars = new DxParams
        {
            Cx = (float)centerX,
            Cy = (float)centerY,
            Scale = (float)scale,
            Aspect = (float)gridH / Math.Max(1, gridW),
            InvW = 1f / Math.Max(1, gridW),
            InvH = 1f / Math.Max(1, gridH),
            MaxIter = maxIter,
            Aa = 1,
        };
        MappedSubresource mapped = _context!.Map(_cbuffer!, 0, MapMode.WriteDiscard);
        mapped.AsSpan<DxParams>(1)[0] = pars;
        _context.Unmap(_cbuffer!, 0);
        _context.OMSetRenderTargets(_benchRtv);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_benchPs!);
        _context.PSSetConstantBuffer(0, _cbuffer!);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.RSSetViewport(new Viewport(gridW, gridH));

        // Size the in-flight depth from the measured per-frame time: deep enough to
        // saturate the GPU (true throughput), but never queueing more than
        // ~BenchTdrSafeSeconds of work (keeps slow cards clear of TDR). A small
        // TDR-safe batch (BenchFlightMin frames) is enough to estimate the rate.
        {
            int est = Math.Min(BenchFlightMin, _benchQueries.Length);
            var estQ = new Queue<(ID3D11Query Query, int Seq)>();
            for (int i = 0; i < est; i++)
            {
                _context.Draw(3, 0);
                _context.End(_benchQueries[i]!);
                estQ.Enqueue((_benchQueries[i]!, i));
            }
            int estCompleted = 0;
            double estSeconds = 0;
            var swEst = System.Diagnostics.Stopwatch.StartNew();
            while (estQ.Count > 0)
                DrainOne(estQ, swEst, ref estCompleted, ref estSeconds, ct);
            double frameTime = Math.Max(1e-6, swEst.Elapsed.TotalSeconds / est);
            _benchInflight = (int)Math.Round(BenchTdrSafeSeconds / frameTime);
            if (_benchInflight < BenchFlightMin) _benchInflight = BenchFlightMin;
            if (_benchInflight > BenchFlightMax) _benchInflight = BenchFlightMax;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int submitted = 0, completed = 0;
        double completedSeconds = 0;
        double lastTick = 0;
        var pending = new Queue<(ID3D11Query Query, int Seq)>();

        while (sw.Elapsed < budget)
        {
            ct.ThrowIfCancellationRequested();
            while (pending.Count >= _benchInflight)
                DrainOne(pending, sw, ref completed, ref completedSeconds, ct);
            var query = _benchQueries[submitted % _benchQueries.Length]!;
            _context.Draw(3, 0);
            _context.End(query);
            pending.Enqueue((query, submitted));
            submitted++;
            DrainReady(pending, sw, ref completed, ref completedSeconds);
            if (sw.Elapsed.TotalSeconds - lastTick >= 0.5)
            {
                tick?.Invoke(completed, sw.Elapsed.TotalSeconds);
                lastTick = sw.Elapsed.TotalSeconds;
            }
        }

        // Drain the queue: the queued but not yet completed frames count,
        // the time stops at the completion of the last one.
        _context!.Flush();
        while (pending.Count > 0)
            DrainOne(pending, sw, ref completed, ref completedSeconds, ct);
        tick?.Invoke(completed, completedSeconds);
        return (completedSeconds > 0 ? completedSeconds : sw.Elapsed.TotalSeconds, completed);
    }

    // True if the event query fired (the GPU passed the matching `End`).
    // With pData NULL, `GetData` only does the status check: S_OK = ready, S_FALSE
    // = still queued; with `DoNotFlush` it does not send queued work to the GPU.
    private static bool QuerySignaled(ID3D11Query query, AsyncGetDataFlags flags)
    {
        try
        {
            return _context!.GetData(query, IntPtr.Zero, 0, flags) == SharpGen.Runtime.Result.Ok;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // On device removed GetData throws instead of returning S_FALSE:
            // map it to a readable error with the reason of the removal.
            var removed = _device!.DeviceRemovedReason;
            if (removed.Failure)
                throw new InvalidOperationException(
                    $"GPU hung during the benchmark (device removed, HRESULT 0x{removed.Code:X8}): frame too heavy for {AdapterName} at this grid.");
            throw;
        }
    }

    // Counts the frames whose event query already fired (without flush).
    private static void DrainReady(Queue<(ID3D11Query Query, int Seq)> pending,
        System.Diagnostics.Stopwatch sw, ref int completed, ref double completedSeconds)
    {
        while (pending.Count > 0 && QuerySignaled(pending.Peek().Query, AsyncGetDataFlags.DoNotFlush))
        {
            pending.Dequeue();
            completed++;
            completedSeconds = sw.Elapsed.TotalSeconds;
        }
    }

    // Maximum wait of a single frame before declaring the GPU hung.
    private static readonly TimeSpan BenchmarkFrameTimeout = TimeSpan.FromSeconds(60);

    // Waits for the oldest frame in the queue (blocking, interruptible).
    private static void DrainOne(Queue<(ID3D11Query Query, int Seq)> pending,
        System.Diagnostics.Stopwatch sw, ref int completed, ref double completedSeconds, CancellationToken ct)
    {
        var (query, _) = pending.Dequeue();
        // Poll with flush: the GPU advances while the CPU waits (blocking
        // native GetData would not accept the CancellationToken). With timeout and
        // device-removed check: without it, a card that cannot hold the benchmark grid
        // (Windows TDR, OOM) stays waiting forever without errors.
        var waitStart = sw.Elapsed;
        // Tight poll without sleep: at AA1x the frames last ~1 ms and every
        // wait interval (~1-15 ms) would depress the throughput; the expensive checks
        // (cancel, device-removed, timeout) run every 1024 polls.
        int spins = 0;
        while (!QuerySignaled(query, AsyncGetDataFlags.None))
        {
            if ((++spins & 1023) != 0) continue;
            ct.ThrowIfCancellationRequested();
            var removed = _device!.DeviceRemovedReason;
            if (removed.Failure)
            {
                string msg = $"GPU hung during the benchmark (device removed, HRESULT 0x{removed.Code:X8}): frame too heavy for {AdapterName} at this grid.";
                throw new InvalidOperationException(msg);
            }
            if (sw.Elapsed - waitStart > BenchmarkFrameTimeout)
            {
                string msg = $"GPU timeout ({BenchmarkFrameTimeout.TotalSeconds:0} s) waiting for a frame on {AdapterName}: card too slow or driver hung.";
                throw new TimeoutException(msg);
            }

        }
        completed++;
        completedSeconds = sw.Elapsed.TotalSeconds;
    }

    // Compressed name for the UI and the history: "NVIDIA GeForce RTX 5070 Ti"
    // → "RTX 5070 Ti", "AMD Radeon(TM) Graphics" → "AMD Radeon Graphics".
    public static string ShortAdapterName(string fullName) =>
        GpuMandelbrot.Shorten(fullName);

    // Captures the backbuffer in a Bitmap (for Save PNG).
    public static Bitmap Capture()
    {
        if (!IsReady) throw new InvalidOperationException("DirectX not initialized.");
        using ID3D11Texture2D backbuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        return ReadTextureToBitmap(backbuffer, _backWidth, _backHeight);
    }

    // Renders a COLORED frame of the area onto a render-target outside the
    // swapchain and returns it as a Bitmap: the benchmark preview for the
    // DirectX engine (without presenting anything on the main window).
    public static Bitmap? RenderPreviewToBitmap(double centerX, double centerY, double scale, int width, int height, int maxIter, int aa, Palette palette,
        int fullWidth = 0, int fullHeight = 0, int offsetX = 0, int offsetY = 0,
        double juliaCx = 0, double juliaCy = 0, bool julia = false)
    {
        if (!IsReady || _context is null) return null;
        try
        {
            var desc = new Texture2DDescription
            {
                Width = (uint)Math.Max(1, width),
                Height = (uint)Math.Max(1, height),
                MipLevels = 1,
                ArraySize = 1,
                // B8G8R8A8: same byte layout of Bitmap Format32bppArgb (B,G,R,A),
                // so the channels do not come out inverted on read.
                Format = DxgiFormat.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };
            using ID3D11Texture2D target = _device!.CreateTexture2D(desc);
            using ID3D11RenderTargetView rtv = _device.CreateRenderTargetView(target);

            EnsureLut(palette);
            DrawFrame(BuildParams(centerX, centerY, scale, width, height, maxIter, aa,
                fullWidth, fullHeight, offsetX, offsetY, juliaCx, juliaCy, julia), rtv, _ps!, width, height);

            var bmp = ReadTextureToBitmap(target, width, height);

            // Restore the swapchain render target and viewport for subsequent renders
            // (if there is one: in headless it does not exist).
            if (_rtv != null)
            {
                _context.OMSetRenderTargets(_rtv);
                _context.RSSetViewport(new Viewport(_backWidth, _backHeight));
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // Copies the content of a texture in a Bitmap (for capture/preview).
    private static Bitmap ReadTextureToBitmap(ID3D11Texture2D source, int width, int height)
    {
        Texture2DDescription stg = source.Description;
        stg.Usage = ResourceUsage.Staging;
        stg.BindFlags = BindFlags.None;
        stg.CPUAccessFlags = CpuAccessFlags.Read;
        stg.MiscFlags = ResourceOptionFlags.None;
        using ID3D11Texture2D staging = _device!.CreateTexture2D(stg);
        _context!.CopyResource(staging, source); // CopyResource(dst, src): copies the rendered texture to the staging

        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read);
        try
        {
            var bmp = new Bitmap(
                width, height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                int rowBytes = width * 4;
                int srcPitch = (int)mapped.RowPitch;
                var row = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(mapped.DataPointer + y * srcPitch, row, 0, rowBytes);
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, rowBytes);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    public static void Dispose()
    {
        EndBenchmarkOffscreen();
        _lutSrv?.Dispose(); _lutSrv = null;
        _lutTex?.Dispose(); _lutTex = null;
        _lutSampler?.Dispose(); _lutSampler = null;
        _lutPalette = (Palette)(-1);
        _cbuffer?.Dispose(); _cbuffer = null;
        _benchPs?.Dispose(); _benchPs = null;
        _ps?.Dispose(); _ps = null;
        _vs?.Dispose(); _vs = null;
        _rtv?.Dispose(); _rtv = null;
        _swapChain?.Dispose(); _swapChain = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }
}
