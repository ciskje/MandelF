using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MandelbrotViewer;

// Precise main-thread detection (GLFW/Cocoa requires main-thread init).
// pthread_main_np on macOS, getpid()==gettid() on Linux.
internal static partial class GlMainThread
{
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_main_np")]
    private static partial int MacMainNp();
    [LibraryImport("libc", EntryPoint = "getpid")]
    private static partial int NixGetPid();
    [LibraryImport("libc", EntryPoint = "gettid")]
    private static partial int NixGetTid();

    public static bool IsMainThread()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return MacMainNp() != 0;
            if (OperatingSystem.IsLinux()) return NixGetPid() == NixGetTid();
        }
        catch { }
        return Environment.CurrentManagedThreadId == 1;
    }
}

// Internal OpenGL 4.1 engine (float, offscreen). This is the "whatever GPU API
// the host platform provides" choice of §1.6: macOS offers OpenGL 4.1.
//
// THREADING CONTRACT (GLFW/Cocoa): Init + window creation + Dispose must run
// on the process MAIN thread (both call sites comply: CLI Main and the
// Avalonia UI thread). After that, all GL work may run on any worker thread:
// each entry makes the context current on the calling thread. The _gate lock
// serializes everything, which is the §4.1 single gate.
internal sealed unsafe class GlEngine : IDisposable
{
    public const int BenchFlightMin = 4;
    public const double BenchTdrSafeSeconds = 0.8;
    public const double FrameTimeoutSeconds = 60.0;

    private readonly object _gate = new();
    private bool _disposed;
    private bool _owned; // true once this instance initialized GLFW
    private bool _counted; // true once this instance incremented _liveEngines

    private Glfw _glfw = null!;
    private GL _gl = null!;
    private Silk.NET.GLFW.WindowHandle* _win;
    private uint _renderProg, _benchProg;
    private RLoc _rl;
    private BLoc _bl;
    private uint _vao;
    private uint _fboColor, _texColor, _cw, _ch;
    private uint _fboCount, _texCount, _bw, _bh;
    private uint _texLut;
    private Palette _lutPal = (Palette)(-1);
    private int _lutIter = -1;
    private (int X, int Y) _split = (1, 1);

    private struct RLoc
    {
        public int Cx, Cy, Px, Top, W, H, MI, K, J, Jx, Jy, FW, FH, OX, OY, Lut;
    }

    private struct BLoc
    {
        public int Cx, Cy, Px, Top, W, H, MI;
    }

    // ---- gate + context binding (call on any thread; init/dispose on main) ----
    private T Run<T>(Func<T> f)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlEngine));
            _glfw.MakeContextCurrent(_win);
            return f();
        }
    }

    private void InitContext(bool owned)
    {
        _glfw = Glfw.GetApi();
        if (!_glfw.Init()) throw new InvalidOperationException("GLFW init failed.");
        _glfw.WindowHint(WindowHintBool.Visible, false);
        _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 1);
        _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        _glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
        _glfw.WindowHint(WindowHintInt.Samples, 0);
        _glfw.WindowHint(WindowHintInt.DepthBits, 0);
        _glfw.WindowHint(WindowHintInt.StencilBits, 0);
        _win = _glfw.CreateWindow(16, 16, "MandelbrotViewer-GPU", null, null);
        if (_win == null) throw new InvalidOperationException("Could not create hidden GL context.");
        _glfw.MakeContextCurrent(_win);
        _gl = GL.GetApi(s => _glfw.GetProcAddress(s));
        _owned = owned;
    }

    public void Dispose()
    {
        try
        {
            // Main thread only (GLFW contract); never throws (§4.1).
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_counted) { lock (_probeGate) _liveEngines--; _counted = false; }
                if (_owned && _win != null)
                {
                    try
                    {
                        _glfw.MakeContextCurrent(_win);
                        if (_renderProg != 0) _gl.DeleteProgram(_renderProg);
                        if (_benchProg != 0) _gl.DeleteProgram(_benchProg);
                        if (_vao != 0) _gl.DeleteVertexArray(_vao);
                        if (_fboColor != 0) _gl.DeleteFramebuffer(_fboColor);
                        if (_texColor != 0) _gl.DeleteTexture(_texColor);
                        if (_fboCount != 0) _gl.DeleteFramebuffer(_fboCount);
                        if (_texCount != 0) _gl.DeleteTexture(_texCount);
                        if (_texLut != 0) _gl.DeleteTexture(_texLut);
                        _glfw.MakeContextCurrent(null);
                        _glfw.DestroyWindow(_win);
                        _glfw.Terminate();
                    }
                    catch { }
                    _win = null;
                    _owned = false;
                }
            }
        }
        catch { } // never throws (§4.1)
    }

    // ---- static probe (renderer string). Main thread only (GLFW contract);
    // both call sites comply (CLI Main, UI thread). Result cached. Never
    // Terminates while an engine instance is alive (the terminator would kill
    // its context): ProbeRenderer runs only when no engine owns GLFW.
    private static string? _probeCache;
    private static readonly object _probeGate = new();
    private static int _liveEngines;

    // Seed the adapter cache after a successful init (avoids later temp probes).
    public static void NoteRenderer(string renderer)
    {
        lock (_probeGate)
        {
            if (_probeCache == null && !string.IsNullOrWhiteSpace(renderer))
                _probeCache = renderer;
        }
    }

    public static string? ProbeRenderer()
    {
        // Off the main thread GLFW would deadlock: report no devices instead.
        if (!GlMainThread.IsMainThread()) return null;
        lock (_probeGate)
        {
            if (_probeCache != null) return _probeCache == "" ? null : _probeCache;
            if (_liveEngines > 0) return null; // engine owns GLFW; skip temp probe
            string? name = null;
            Glfw? glfw = null;
            Silk.NET.GLFW.WindowHandle* w = null;
            try
            {
                glfw = Glfw.GetApi();
                if (glfw.Init())
                {
                    glfw.WindowHint(WindowHintBool.Visible, false);
                    glfw.WindowHint(WindowHintInt.ContextVersionMajor, 4);
                    glfw.WindowHint(WindowHintInt.ContextVersionMinor, 1);
                    glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
                    glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
                    w = glfw.CreateWindow(16, 16, "probe", null, null);
                    if (w != null)
                    {
                        glfw.MakeContextCurrent(w);
                        var gl = GL.GetApi(s => glfw.GetProcAddress(s));
                        name = gl.GetStringS(GLEnum.Renderer);
                        glfw.MakeContextCurrent(null);
                    }
                }
            }
            catch { }
            finally
            {
                try
                {
                    if (glfw != null)
                    {
                        if (w != null) glfw.DestroyWindow(w);
                        glfw.Terminate();
                    }
                }
                catch { }
            }
            if (name is not null && (name.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Software", StringComparison.OrdinalIgnoreCase)))
                name = null; // software-only renderers excluded (§4.1)
            _probeCache = name ?? "";
            return name;
        }
    }

    // ---- init ----
    public string Renderer { get; private set; } = "";
    public string GlVersion { get; private set; } = "";
    public bool Ready { get; private set; }

    public void Initialize()
    {
        // Main thread only (GLFW contract).
        if (!GlMainThread.IsMainThread())
            throw new InvalidOperationException("GPU init must run on the main thread.");
        InitContext(owned: true);
        lock (_probeGate) { _liveEngines++; _counted = true; }
        try
        {
            Run<object?>(() =>
        {
            Renderer = _gl.GetStringS(GLEnum.Renderer) ?? "";
            GlVersion = _gl.GetStringS(GLEnum.Version) ?? "";
            if (string.IsNullOrWhiteSpace(Renderer))
                throw new InvalidOperationException("GL renderer query failed.");
            _renderProg = BuildProgram(GpuKernels.FullscreenVertex, GpuKernels.RenderFragmentFloat);
            _benchProg = BuildProgram(GpuKernels.FullscreenVertex, GpuKernels.BenchFragmentFloat);
            _rl = new RLoc
            {
                Cx = U(_renderProg, "uCx"), Cy = UOpt(_renderProg, "uCy"),
                Px = U(_renderProg, "uPx"), Top = U(_renderProg, "uTop"),
                W = UOpt(_renderProg, "uW"), H = UOpt(_renderProg, "uH"),
                MI = U(_renderProg, "uMI"), K = U(_renderProg, "uK"),
                J = U(_renderProg, "uJ"), Jx = U(_renderProg, "uJx"),
                Jy = U(_renderProg, "uJy"), FW = U(_renderProg, "uFW"),
                FH = UOpt(_renderProg, "uFH"), OX = U(_renderProg, "uOX"),
                OY = U(_renderProg, "uOY"), Lut = U(_renderProg, "uLut"),
            };
            _bl = new BLoc
            {
                Cx = UOpt(_benchProg, "uCx"), Cy = UOpt(_benchProg, "uCy"),
                Px = U(_benchProg, "uPx"), Top = U(_benchProg, "uTop"),
                W = U(_benchProg, "uW"), H = UOpt(_benchProg, "uH"),
                MI = U(_benchProg, "uMI"),
            };
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            Check("init");
            Ready = true;
            return null;
        });
        }
        catch
        {
            if (_counted) { lock (_probeGate) _liveEngines--; _counted = false; }
            try { Dispose(); } catch { }
            throw;
        }
    }

    private int U(uint prog, string name)
    {
        int l = _gl.GetUniformLocation(prog, name);
        if (l < 0) throw new InvalidOperationException($"Uniform '{name}' not found.");
        return l;
    }

    // Optional uniform (pruned by the GLSL compiler when unused): -1 = skip.
    private int UOpt(uint prog, string name) => _gl.GetUniformLocation(prog, name);

    private void Set1(int loc, float v)
    {
        if (loc >= 0) _gl.Uniform1(loc, v);
    }

    private void Set1i(int loc, int v)
    {
        if (loc >= 0) _gl.Uniform1(loc, v);
    }

    private uint BuildProgram(string vsSrc, string fsSrc)
    {
        uint vs = _gl.CreateShader(GLEnum.VertexShader);
        _gl.ShaderSource(vs, vsSrc);
        _gl.CompileShader(vs);
        _gl.GetShader(vs, GLEnum.CompileStatus, out int vok);
        if (vok == 0) throw new InvalidOperationException("VS: " + _gl.GetShaderInfoLog(vs));
        uint fs = _gl.CreateShader(GLEnum.FragmentShader);
        _gl.ShaderSource(fs, fsSrc);
        _gl.CompileShader(fs);
        _gl.GetShader(fs, GLEnum.CompileStatus, out int fok);
        if (fok == 0) throw new InvalidOperationException("FS: " + _gl.GetShaderInfoLog(fs));
        uint pr = _gl.CreateProgram();
        _gl.AttachShader(pr, vs);
        _gl.AttachShader(pr, fs);
        _gl.LinkProgram(pr);
        _gl.GetProgram(pr, GLEnum.LinkStatus, out int lok);
        if (lok == 0) throw new InvalidOperationException("Link: " + _gl.GetProgramInfoLog(pr));
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return pr;
    }

    private void Check(string what)
    {
        var e = _gl.GetError();
        if (e != GLEnum.NoError)
            throw new InvalidOperationException($"GL error at {what}: {e}");
    }

    // ---- resources ----
    private void EnsureColor(int w, int h)
    {
        if (_fboColor != 0 && _cw == w && _ch == h) return;
        if (_fboColor != 0) { _gl.DeleteFramebuffer(_fboColor); _gl.DeleteTexture(_texColor); }
        _texColor = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _texColor);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, (uint)w, (uint)h, 0,
            GLEnum.Rgba, GLEnum.UnsignedByte, null);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _fboColor = _gl.GenFramebuffer();
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fboColor);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, _texColor, 0);
        if (_gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("Color FBO incomplete.");
        _cw = (uint)w; _ch = (uint)h;
    }

    private void EnsureCount(int w, int h)
    {
        if (_fboCount != 0 && _bw == w && _bh == h) return;
        if (_fboCount != 0) { _gl.DeleteFramebuffer(_fboCount); _gl.DeleteTexture(_texCount); }
        _texCount = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _texCount);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.R32ui, (uint)w, (uint)h, 0,
            GLEnum.RedInteger, GLEnum.UnsignedInt, null);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _fboCount = _gl.GenFramebuffer();
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fboCount);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, _texCount, 0);
        if (_gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("Count FBO incomplete.");
        _bw = (uint)w; _bh = (uint)h;
    }

    private void EnsureLut(Palette pal, int maxIter)
    {
        if (_texLut != 0 && _lutPal == pal && _lutIter == maxIter) return;
        int[] lut = PaletteColors.GetLut(pal, maxIter);
        byte[] bytes = new byte[4096 * 4];
        for (int i = 0; i < 4096; i++)
        {
            int c = lut[i];
            bytes[i * 4] = (byte)((c >> 16) & 0xFF);
            bytes[i * 4 + 1] = (byte)((c >> 8) & 0xFF);
            bytes[i * 4 + 2] = (byte)(c & 0xFF);
            bytes[i * 4 + 3] = 0xFF;
        }
        if (_texLut == 0) _texLut = _gl.GenTexture();
        _gl.ActiveTexture(GLEnum.Texture1);
        _gl.BindTexture(GLEnum.Texture2D, _texLut);
        fixed (byte* p = bytes)
            _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, 4096, 1, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte, p);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.ActiveTexture(GLEnum.Texture0);
        _lutPal = pal; _lutIter = maxIter;
    }

    // ---- colored render (offscreen) ----
    public int[] Render(int w, int h, double cx, double cy, double scale, int maxIter,
        Palette pal, int k, bool julia, double jcx, double jcy,
        int offX, int offY, int fullW, int fullH, CancellationToken ct)
    {
        return Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var (pixelSize, topY, _) = Mandelbrot.ViewMath(cx, cy, scale,
                fullW <= 0 ? w : fullW, fullH <= 0 ? h : fullH, k);
            EnsureColor(w, h);
            EnsureLut(pal, maxIter);
            _gl.BindFramebuffer(GLEnum.Framebuffer, _fboColor);
            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _gl.UseProgram(_renderProg);
            Set1(_rl.Cx, (float)cx);
            Set1(_rl.Cy, (float)cy);
            Set1(_rl.Px, (float)pixelSize);
            Set1(_rl.Top, (float)topY);
            Set1i(_rl.W, w); Set1i(_rl.H, h);
            Set1i(_rl.MI, maxIter); Set1i(_rl.K, k);
            Set1i(_rl.J, julia ? 1 : 0);
            Set1(_rl.Jx, (float)jcx); Set1(_rl.Jy, (float)jcy);
            Set1i(_rl.FW, fullW <= 0 ? w : fullW);
            Set1i(_rl.FH, fullH <= 0 ? h : fullH);
            Set1i(_rl.OX, offX); Set1i(_rl.OY, offY);
            _gl.ActiveTexture(GLEnum.Texture1);
            _gl.BindTexture(GLEnum.Texture2D, _texLut);
            Set1i(_rl.Lut, 1);
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(GLEnum.Triangles, 0, 3);
            byte[] rgba = new byte[w * h * 4];
            fixed (byte* p = rgba)
                _gl.ReadPixels(0, 0, (uint)w, (uint)h, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            Check("render");
            ct.ThrowIfCancellationRequested();
            int[] argb = new int[w * h];
            for (int i = 0; i < argb.Length; i++)
                argb[i] = unchecked((int)0xFF000000) | (rgba[i * 4] << 16) | (rgba[i * 4 + 1] << 8) | rgba[i * 4 + 2];
            return argb;
        });
    }

    // ---- launch-layout autotune (§4.5 GL mapping) ----
    // Candidates: frame split factors (1,1),(2,1),(1,2),(2,2). Probe grid is
    // the real benchmark grid 960x540 at reduced iterations (2000), center
    // (-0.75, 0). Warmup 8 draws + Finish; each candidate timed with rounds
    // interleaved, median of 5. Default (1,1) kept unless the winner is clearly
    // faster ((default-best)/default >= 0.015).
    public (int X, int Y) TuneBlocks()
    {
        return Run(() =>
        {
            const int w = 960, h = 540, mi = 2000;
            EnsureCount(w, h);
            SetBenchUniforms(-0.75, 0, 0.01 / 960 * 960, w, h, mi);
            for (int i = 0; i < 8; i++) DrawBench(w, h, 1, 1);
            _gl.Finish();
            var cands = new[] { (1, 1), (2, 1), (1, 2), (2, 2) };
            double[][] times = new double[cands.Length][];
            for (int i = 0; i < cands.Length; i++) times[i] = new double[5];
            var sw = new System.Diagnostics.Stopwatch();
            for (int round = 0; round < 5; round++)
                for (int c = 0; c < cands.Length; c++)
                {
                    sw.Restart();
                    DrawBench(w, h, cands[c].Item1, cands[c].Item2);
                    _gl.Finish();
                    times[c][round] = sw.Elapsed.TotalMilliseconds;
                }
            int best = 0;
            double bestMed = Median(times[0]);
            for (int c = 1; c < cands.Length; c++)
            {
                double m = Median(times[c]);
                if (m < bestMed) { bestMed = m; best = c; }
            }
            double def = Median(times[0]);
            _split = (def - bestMed) / def >= 0.015 ? cands[best] : (1, 1);
            return _split;
        });
    }

    private static double Median(double[] v)
    {
        var s = (double[])v.Clone();
        Array.Sort(s);
        return s[s.Length / 2];
    }

    private void SetBenchUniforms(double cx, double cy, double scale, int w, int h, int mi)
    {
        double px = scale / w;
        double top = cy - h * 0.5 * px;
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fboCount);
        _gl.UseProgram(_benchProg);
        Set1(_bl.Cx, (float)cx);
        Set1(_bl.Cy, (float)cy);
        Set1(_bl.Px, (float)px);
        Set1(_bl.Top, (float)top);
        Set1i(_bl.W, w); Set1i(_bl.H, h);
        Set1i(_bl.MI, mi);
        _gl.BindVertexArray(_vao);
    }

    private void DrawBench(int w, int h, int sx, int sy)
    {
        // Split the frame into sx*sy viewport tiles (same FBO, same uniforms:
        // gl_FragCoord is viewport-relative... CAREFUL: viewport offset shifts
        // FragCoord, breaking the sample mapping. Use full-frame draw with
        // scissor tiles instead: scissor only limits writes, coords stay whole.
        _gl.Viewport(0, 0, (uint)w, (uint)h);
        int tw = (w + sx - 1) / sx, th = (h + sy - 1) / sy;
        _gl.Enable(GLEnum.ScissorTest);
        for (int ty = 0; ty < sy; ty++)
            for (int tx = 0; tx < sx; tx++)
            {
                // GL origin bottom-left; tile rows from bottom.
                _gl.Scissor(tx * tw, ty * th, (uint)Math.Min(tw, w - tx * tw), (uint)Math.Min(th, h - ty * th));
                _gl.DrawArrays(GLEnum.Triangles, 0, 3);
            }
        _gl.Disable(GLEnum.ScissorTest);
    }

    // ---- standardized benchmark (§4.8 interactive shape, GL fences) ----
    public (double Seconds, int Frames) Benchmark(int w, int h, double cx, double cy,
        double scale, int maxIter, CancellationToken ct, Action<double>? tick)
    {
        return Run(() =>
        {
            ulong required = (ulong)w * (ulong)h * 4 * 2;
            ulong avail = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (required > avail)
                throw new InvalidOperationException(
                    $"GPU benchmark needs {required / 1048576} MB, only {avail / 1048576} MB available.");
            EnsureCount(w, h);
            SetBenchUniforms(cx, cy, scale, w, h, maxIter);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double budget = BenchmarkStandard.BudgetSeconds;
            // estimate per-frame time (BenchFlightMin draws)
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < BenchFlightMin; i++) DrawBenchTuned(w, h);
            _gl.Finish();
            double frameT = Math.Max(1e-6, t0.Elapsed.TotalSeconds / BenchFlightMin);
            int batch = Math.Clamp((int)Math.Round(BenchTdrSafeSeconds / frameT), 4, 256);
            int frames = 0;
            double lastTick = 0;
            nint fence = 0;
            try
            {
                while (sw.Elapsed.TotalSeconds < budget)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < batch; i++) DrawBenchTuned(w, h);
                    frames += batch;
                    if (fence != 0) _gl.DeleteSync(fence);
                    fence = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
                    WaitFence(fence, ct); // 60 s timeout slices
                    double s = sw.Elapsed.TotalSeconds;
                    if (tick is not null && s - lastTick >= 0.5) { lastTick = s; tick(s); }
                }
            }
            finally { if (fence != 0) _gl.DeleteSync(fence); }
            double secs = sw.Elapsed.TotalSeconds;
            Check("bench"); // sticky GL errors would reveal broken draws
            tick?.Invoke(secs);
            return (secs, frames);
        });
    }

    private void DrawBenchTuned(int w, int h) => DrawBench(w, h, _split.X, _split.Y);

    // Validation readback: run the benchmark kernel once and return the raw
    // per-sample iteration counts (§4.3: iterations-only, no early-out).
    // Proves the benchmark draws really execute the escape loop.
    public uint[] ReadCounts(int w, int h, double cx, double cy, double scale, int maxIter)
    {
        return Run(() =>
        {
            EnsureCount(w, h);
            SetBenchUniforms(cx, cy, scale, w, h, maxIter);
            DrawBench(w, h, 1, 1);
            _gl.Finish();
            uint[] counts = new uint[w * h];
            fixed (uint* p = counts)
                _gl.ReadPixels(0, 0, (uint)w, (uint)h, GLEnum.RedInteger, GLEnum.UnsignedInt, p);
            Check("readcounts");
            return counts;
        });
    }

    private void WaitFence(nint fence, CancellationToken ct)
    {
        // DrainOne equivalent: tight poll slices with cancel check and a
        // 60 s per-frame timeout → readable hang error, never wait forever.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var st = _gl.ClientWaitSync(fence, SyncObjectMask.Bit, 100_000_000);
            if (st == GLEnum.AlreadySignaled || st == GLEnum.ConditionSatisfied)
                return;
            if (st == GLEnum.WaitFailed)
                throw new InvalidOperationException("GPU fence wait failed (device lost?).");
            if (sw.Elapsed.TotalSeconds > FrameTimeoutSeconds)
                throw new TimeoutException(
                    "GPU hung during the benchmark (fence not signaled in 60 s).");
        }
    }
}
