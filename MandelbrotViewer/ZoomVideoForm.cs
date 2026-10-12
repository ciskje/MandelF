namespace MandelbrotViewer;

// Zoom video MP4: interpolates from the current view to the full set (scale in
// logarithm, linear center, auto iterations per frame) and renders every frame with
// the active engine at view resolution and AA1x. Encodes with ffmpeg if present
// (otherwise the PNG sequence remains). Cancelable.
public partial class ZoomVideoForm : Form
{
    // Option B: no sample ceiling. On-chip SSAA never materializes the
    // W*aa x H*aa grid, so AA stays as selected even on large views;
    // the cost is render time (~aa^2), shown by the frame progress.

    private readonly double _endCx, _endCy, _endScale;
    private readonly Palette _palette;
    private readonly int _aa;
    private readonly bool _useCuda;
    private readonly bool _useDirectX;
    private readonly bool _julia;
    private readonly double _jcx, _jcy;
    private readonly int _viewW, _viewH;
    private CancellationTokenSource? _cts;
    private bool _rendering;
    private string _resultPath = ""; // MP4 created or PNG folder (for Open)

    public ZoomVideoForm(double endCx, double endCy, double endScale, Palette palette, int aa,
        RenderEngine engine, bool useCuda, bool useDouble, Size viewSize,
        bool julia, double juliaCx, double juliaCy)
    {
        InitializeComponent();
        Program.ApplyIcon(this);

        _endCx = endCx;
        _endCy = endCy;
        _endScale = endScale;
        _palette = palette;
        _aa = Math.Max(1, aa);
        _useCuda = useCuda;
        _useDirectX = engine == RenderEngine.DirectX && DxMandelbrot.IsReady;
        radPrec32.Checked = !useDouble;
        radPrec64.Checked = useDouble;
        rowPrec.Enabled = _useCuda || UseCpu; // precision matters for CUDA and CPU (DX = float)
        _julia = julia;
        _jcx = juliaCx;
        _jcy = juliaCy;
        _viewW = Math.Max(320, viewSize.Width);
        _viewH = Math.Max(240, viewSize.Height);

        UpdateInfo();
    }

    // AA chosen in the dialog (As view = the active one) or of the view.
    private int SelectedAa() =>
        cmbAAVid.SelectedIndex <= 0 ? _aa : 1 << (cmbAAVid.SelectedIndex - 1);

    private void UpdateInfo()
    {
        string engineLabel = _useCuda ? $"CUDA {(UseDouble ? "64-bit" : "32-bit")}"
            : _useDirectX ? "DirectX" : $"CPU {(UseDouble ? "64-bit" : "32-bit")} {Mandelbrot.CpuSimdLabel}";
        string mode = _julia ? "Julia" : "Mandelbrot";
        int effAa = SelectedAa();
        lblInfo.Text = $"From the current view to the full set ({_viewW}×{_viewH} AA{effAa}x, {mode}, " +
            $"{engineLabel}, auto iter): ffmpeg " +
            (FindFfmpeg() != null ? "found → direct MP4." : "absent → PNG sequence remains.");
    }

    private void CmbAAVid_Changed(object? sender, EventArgs e) => UpdateInfo();

    // Precision selected in this dialog (read on the UI thread; the render
    // worker receives a captured copy). Enabled with CUDA and CPU engines.
    private bool UseDouble => radPrec64.Checked;

    // CPU engine is active when neither GPU engine is (no stored flag).
    private bool UseCpu => !_useCuda && !_useDirectX;

    private void Prec_CheckedChanged(object? sender, EventArgs e) => UpdateInfo();

    // Path of ffmpeg (PATH) or null if absent.
    internal static string? FindFfmpeg()
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string cand = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(cand)) return cand;
            }
            catch
            {
                // Invalid PATH entries: ignore and continue.
            }
        }
        return null;
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_rendering) { _cts?.Cancel(); return; }
        int frames = int.Parse((string)cmbFrames.SelectedItem!);
        int fps = int.Parse((string)cmbFps.SelectedItem!);
        int effAa = SelectedAa();

        if (MandelbrotForm.StartScale / _endScale < 2.0)
        {
            MessageBox.Show(this,
                "You are already at the full set: frame a region first to generate the video.",
                "Zoom video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "MP4 (*.mp4)|*.mp4",
            FileName = "mandelbrot-zoom.mp4",
            DefaultExt = "mp4",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _rendering = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool useDouble = UseDouble; // capture on the UI thread (worker must not touch controls)
        var progress = new Progress<int>(i =>
        {
            progressBar.Value = Math.Clamp((i + 1) * 100 / frames, 0, 100);
            lblResult.Text = $"Frame {i + 1}/{frames}…";
        });
        btnStart.Text = "Cancel";
        btnClose.Enabled = false;
        btnOpen.Enabled = false;
        _resultPath = "";

        // Direction: from the current view to the full set (the "already at the set"
        // case is blocked above with an error).
        double startCx = _endCx, startCy = _endCy, startScale = _endScale;
        string tmpDir = Path.Combine(Path.GetTempPath(),
            "MandelC-video-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmpDir);
            await Task.Run(() => RenderFrames(tmpDir, frames, startCx, startCy, startScale, effAa, useDouble, progress, token), token);
            token.ThrowIfCancellationRequested();

            string? ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                _resultPath = tmpDir;
                btnOpen.Enabled = true;
                lblResult.Text = $"ffmpeg absent: PNG sequence in {tmpDir}";
                tmpDir = ""; // do not delete: it is the result
                return;
            }
            lblResult.Text = "Encoding MP4…";
            await Task.Run(() => RunFfmpeg(ffmpeg, tmpDir, frames, fps, dlg.FileName, token), token);
            _resultPath = dlg.FileName;
            btnOpen.Enabled = true;
            lblResult.Text = $"Video saved: {Path.GetFileName(dlg.FileName)} ({frames} frames, {fps} fps)";
        }
        catch (OperationCanceledException)
        {
            lblResult.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            lblResult.Text = "Error: " + ex.Message;
        }
        finally
        {
            if (tmpDir.Length > 0)
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { }
            }
            _cts?.Dispose();
            _cts = null;
            _rendering = false;
            btnStart.Text = "Start";
            btnClose.Enabled = true;
            progressBar.Value = 0;
        }
    }

    private void RenderFrames(string tmpDir, int frames,
        double startCx, double startCy, double startScale, int effAa, bool useDouble,
        IProgress<int> progress, CancellationToken ct)
    {
        for (int i = 0; i < frames; i++)
        {
            ct.ThrowIfCancellationRequested();
            FrameAt(i, frames, startCx, startCy, startScale,
                out double cx, out double cy, out double scale, out int maxIter);
            using var bmp = RenderFrame(cx, cy, scale, maxIter, effAa, useDouble, ct);
            if (bmp == null) throw new InvalidOperationException("Render failed (DirectX not ready?).");
            bmp.Save(Path.Combine(tmpDir, $"f{i:0000}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
            progress.Report(i);
        }
    }

    // Parameters of the i-th frame of the zoom (same interpolation as the video:
    // geometric scale ease-out, center following the zoom, auto iterations).
    private void FrameAt(int i, int frames, double startCx, double startCy, double startScale,
        out double cx, out double cy, out double scale, out int maxIter)
    {
        double endScale = MandelbrotForm.StartScale;
        double span = endScale - startScale;
        double t = frames > 1 ? i / (double)(frames - 1) : 1.0;
        double te = 1.0 - Math.Pow(1.0 - t, 3.0);
        scale = startScale * Math.Pow(endScale / startScale, te);
        double u = span > 0 ? (scale - startScale) / span : 1.0;
        cx = startCx + (MandelbrotForm.StartCenterX - startCx) * u;
        cy = startCy + (MandelbrotForm.StartCenterY - startCy) * u;
        maxIter = Mandelbrot.AutoIterForScale(scale);
    }

    private Bitmap? RenderFrame(double cx, double cy, double scale, int maxIter, int effAa, bool useDouble, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_useCuda)
        {
            var bmp = new Bitmap(_viewW, _viewH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GpuMandelbrot.Render(bmp, cx, cy, scale, maxIter, _palette, effAa, useDouble, ct, _jcx, _jcy, _julia);
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }
        if (_useDirectX)
            return DxMandelbrot.RenderPreviewToBitmap(cx, cy, scale, _viewW, _viewH, maxIter, effAa, _palette, 0, 0, 0, 0, _jcx, _jcy, _julia);
        var cpu = new Bitmap(_viewW, _viewH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            Mandelbrot.Render(cpu, cx, cy, scale, maxIter, _palette, effAa, ct, _jcx, _jcy, _julia, useDouble);
            return cpu;
        }
        catch
        {
            cpu.Dispose();
            throw;
        }
    }

    // Encodes the PNGs with ffmpeg (on a worker thread: never on the UI thread). The stderr
    // is drained asynchronously DURING the wait: reading it after the WaitForExit
    // deadlocks as soon as the pipe fills (ffmpeg logs every frame). On
    // cancellation the process is killed.
    private static void RunFfmpeg(string ffmpeg, string tmpDir, int frames, int fps,
        string output, CancellationToken ct)
    {
        // Pre-flight: without frames the encode fails with "no packets".
        if (Directory.GetFiles(tmpDir, "f*.png").Length == 0)
            throw new InvalidOperationException("No rendered frames to encode.");
        using var proc = new System.Diagnostics.Process();
        proc.StartInfo.FileName = ffmpeg;
        // pad to even dimensions: the view often has odd sides and yuv420p/libx264
        // rejects them ("Could not open encoder" + "no packets", exit 0xDFABA7BB).
        proc.StartInfo.Arguments = $"-y -framerate {fps} -i \"{Path.Combine(tmpDir, "f%04d.png")}\" " +
            $"-frames:v {frames} -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" " +
            $"-c:v libx264 -pix_fmt yuv420p -crf 18 \"{output}\"";
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.StartInfo.RedirectStandardError = true;
        var stderr = new System.Text.StringBuilder();
        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        proc.Start();
        proc.BeginErrorReadLine();
        try
        {
            while (!proc.WaitForExit(500))
                ct.ThrowIfCancellationRequested();
            proc.WaitForExit(); // rejoins the async readers
        }
        catch
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                "ffmpeg failed (exit " + proc.ExitCode + "): " + LastLines(stderr.ToString(), 10));
    }

    private static string LastLines(string text, int n)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - n)));
    }

    // Opens the result (MP4 with the default player, PNG folder in Explorer).
    private void BtnOpen_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_resultPath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_resultPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            lblResult.Text = "Open failed: " + ex.Message;
        }
    }

    private void BtnClose_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
