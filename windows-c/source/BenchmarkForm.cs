namespace MandelbrotViewer;

// Standard benchmark: fixed zone at high iterations for 8 seconds, measures the
// iterations per second of the selected engine (with CPU fallback).
public partial class BenchmarkForm : Form
{
    // Standard test parameters live in BenchmarkStandard (shared with the
    // --bench-dx CLI), used directly so results stay comparable.

    private readonly RenderEngine _engine;
    private readonly bool _useCuda;
    private readonly bool _useDirectX;
    private readonly bool _useDouble;
    private readonly string _deviceLabel;
    private CancellationTokenSource? _cts;
    private bool _running;
    private double _measuredMpixel;
    private int _lastFrames;
    private double _lastSeconds;

    public BenchmarkForm(RenderEngine engine, bool useDouble)
    {
        InitializeComponent();
        Program.ApplyIcon(this);

        _useCuda = engine == RenderEngine.Cuda && GpuMandelbrot.IsReady;
        _useDirectX = engine == RenderEngine.DirectX && DxMandelbrot.IsReady;
        _useDouble = useDouble;
        _engine = _useDirectX ? RenderEngine.DirectX : _useCuda ? RenderEngine.Cuda : RenderEngine.Cpu;
        // Device that actually runs the test (the benchmark uses the current
        // engine initialization, so show it next to the result).
        _deviceLabel = _useCuda ? GpuMandelbrot.DeviceShortName
            : _useDirectX ? DxMandelbrot.ShortAdapterName(DxMandelbrot.AdapterName)
            : Diagnostics.CpuName();
        string note = engine switch
        {
            RenderEngine.Cuda when !_useCuda => " (CUDA not ready, using CPU)",
            RenderEngine.DirectX when !_useDirectX => " (DirectX not ready, using CPU)",
            _ => "",
        };
        string precision = _useCuda ? $"CUDA {(_useDouble ? "64-bit" : "32-bit")}" : _useDirectX ? "DirectX float" : $"CPU {(_useDouble ? "64-bit" : "32-bit")} {Mandelbrot.CpuSimdLabel}";
        lblInfo.Text = $"Engine: {RenderEngineInfo.DisplayName(_engine)}{note}\n" +
            $"Zone {BenchmarkStandard.Width}x{BenchmarkStandard.Height} {BenchmarkStandard.Aa}x AA = {BenchmarkStandard.Width * BenchmarkStandard.Aa}x{BenchmarkStandard.Height * BenchmarkStandard.Aa} elementary samples without averaging, " +
            $"{BenchmarkStandard.MaxIter} max iterations, scale {BenchmarkStandard.Scale}, precision {precision} — " +
            $"minimum duration {BenchmarkStandard.Budget.TotalSeconds:F0} seconds (DirectX offscreen, no Present).";
        lblResult.Text = "—";
        chartPanel.Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke((MethodInvoker)(() =>
        {
            if (!IsDisposed) BtnStart_Click(this, EventArgs.Empty);
        }));
    }

    private static string FormatPixels(double perSec) => perSec switch
    {
        >= 1e9 => $"{perSec / 1e9:F2} Gpixel/s",
        >= 1e6 => $"{perSec / 1e6:F2} Mpixel/s",
        >= 1e3 => $"{perSec / 1e3:F1} kpixel/s",
        _ => $"{perSec:F0} pixel/s",
    };


    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_running) { _cts?.Cancel(); return; }

        _running = true;
        _cts = new CancellationTokenSource();
        btnStart.Text = "Cancel";
        btnClose.Enabled = false;
        lblResult.Text = "…";
        lblDetail.Text = "";
        lblLive.Text = "0%  |  —";
        lblLive.Refresh();

        // First frame of the benchmark zone made visible for all engines:
        // DirectX renders offscreen (colored) and shows it in previewBox, like CUDA/CPU.
        try
        {
            var preview = await Task.Run(() => RenderBenchmarkPreview(_cts.Token), _cts.Token);
            if (preview != null && !IsDisposed)
            {
                var old = previewBox.Image;
                previewBox.Image = preview;
                previewBox.Visible = true;
                old?.Dispose();
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        IProgress<BenchmarkProgress> progress = new Progress<BenchmarkProgress>(UpdateProgress);

        try
        {
            await Task.Delay(150, _cts.Token);
            double seconds;
            int frames;
            if (_useDirectX)
            {
                (seconds, frames) = await Task.Run(
                    () => BenchmarkDirectX(progress, _cts.Token),
                    _cts.Token);
            }
            else
            {
                ( _, seconds, frames) = await Task.Run(() =>
                    _useCuda
                        ? GpuMandelbrot.BenchmarkGpu(BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter, BenchmarkStandard.Aa, _useDouble, BenchmarkStandard.Budget, progress, _cts.Token)
                        : _useDouble
                        ? Mandelbrot.BenchmarkCpu(BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter, BenchmarkStandard.Aa, BenchmarkStandard.Budget, progress, _cts.Token)
                        : Mandelbrot.BenchmarkCpuFloat(BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter, BenchmarkStandard.Aa, BenchmarkStandard.Budget, progress, _cts.Token),
                    _cts.Token);
            }

            _measuredMpixel = BenchmarkStandard.PixelsPerSecond(frames, seconds) / 1e6;
            _lastFrames = frames;
            _lastSeconds = seconds;
            btnCsv.Enabled = true;
            lblResult.Text = FormatPixels(_measuredMpixel * 1e6);
            lblDetail.Text = _useDirectX
                ? $"{frames} frame ({frames / seconds:F1} frame/s) {BenchmarkStandard.Width}x{BenchmarkStandard.Height} AA{BenchmarkStandard.Aa}x in {seconds:F1} s on {_deviceLabel}"
                : $"{frames} frame {BenchmarkStandard.Width}x{BenchmarkStandard.Height} AA{BenchmarkStandard.Aa}x in {seconds:F1} s on {_deviceLabel}";
            lblLive.Text = "";
            chartPanel.Invalidate();
        }
        catch (OperationCanceledException)
        {
            lblResult.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            AppLog.Add("Benchmark " + (_useDirectX ? "DirectX" : (_useCuda ? "CUDA" : "CPU")) + ": " + ex.Message);
            lblResult.Text = "Error";
            lblDetail.Text = ex.Message;
            lblLive.Text = "";
            chartPanel.Invalidate();
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _running = false;
            btnStart.Text = "Start";
            btnClose.Enabled = true;
        }
    }

    private void UpdateProgress(BenchmarkProgress progress)
    {
        double fraction = Math.Clamp(progress.ElapsedSeconds / BenchmarkStandard.Budget.TotalSeconds, 0.0, 1.0);
        string liveRate = progress.ElapsedSeconds > 0.2
            ? FormatPixels(BenchmarkStandard.PixelsPerSecond(progress.Frames, progress.ElapsedSeconds))
            : "—";
        lblLive.Text = $"{fraction * 100:0}%  |  {liveRate}";
        lblLive.Refresh();
    }

    private void ChartPanel_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(chartPanel.BackColor);
        float left = 168;
        float right = 56;
        float top = 22;
        float bottom = 24;
        float plotWidth = chartPanel.ClientSize.Width - left - right;
        float plotHeight = chartPanel.ClientSize.Height - top - bottom;
        if (plotWidth <= 0 || plotHeight <= 0) return;

        using var titleFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 7.5f);
        using var gridPen = new Pen(Color.Gainsboro);
        using var axisPen = new Pen(Color.Gray);
        using var textBrush = new SolidBrush(Color.DimGray);
        using var titleBrush = new SolidBrush(Color.FromArgb(45, 45, 48));
        using var actualBrush = new SolidBrush(Color.FromArgb(36, 113, 163));
        using var cudaBrush = new SolidBrush(Color.FromArgb(226, 126, 34));
        using var dxBrush = new SolidBrush(Color.FromArgb(74, 143, 87));
        using var cpuBrush = new SolidBrush(Color.FromArgb(112, 128, 144));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var rightAligned = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        // Historical references, measured with the standardized test (best of 3 runs of
        // 8 s: DirectX offscreen `--bench-dx`, CUDA `--bench-cuda` float 32-bit and double
        // 64-bit, CPU double `--bench-cpu` and CPU float `--bench-cpu float`). Unit: MPixel/s. The DirectX and CUDA benchmark
        // kernels use the same iteration loop (incremental squares), so those bars are a fair
        // engine-to-engine comparison of the identical workload. The CPU benchmark additionally
        // skips known-interior points via the cardioid/bulb test and runs the escape loop
        // through the SIMD vector core (v2.19.2/v2.19.4).
        var bars = new (string Label, double Value, Brush Brush)[]
        {
            ("Risultato", _measuredMpixel, actualBrush),
            ("CUDA 5070 Ti 32-bit", 310.7, cudaBrush),
            ("CUDA 4070 SUPER 32-bit", 250.1, cudaBrush),
            ("CUDA 5070 Ti 64-bit", 6.7, cudaBrush),
            ("CUDA 4070 S. 64-bit", 5.3, cudaBrush),
            ("DirectX 5070 Ti", 338, dxBrush),
            ("DirectX 4070 SUPER", 262.4, dxBrush),
            ("DirectX AMD Radeon", 5.2, dxBrush),
            ("CPU 9900X AVX512", 13.8, cpuBrush),
            ("CPU 9900X float AVX512", 21.3, cpuBrush),
        }.OrderByDescending(b => b.Value).ToArray();
        double maximum = bars.Max(b => b.Value) * 1.15;

        e.Graphics.DrawString("Confronto prestazioni (MPixel/s)", titleFont, titleBrush, left, 2);
        for (int step = 0; step <= 2; step++)
        {
            float x = left + plotWidth * step / 2f;
            e.Graphics.DrawLine(gridPen, x, top, x, top + plotHeight);
            string value = $"{maximum * step / 2:0}";
            e.Graphics.DrawString(value, labelFont, textBrush, new RectangleF(x - 24, top + plotHeight + 1, 48, 16), centered);
        }
        e.Graphics.DrawLine(axisPen, left, top, left, top + plotHeight);

        float rowHeight = plotHeight / bars.Length;
        float barHeight = Math.Min(22f, rowHeight * 0.62f);
        for (int i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            float width = (float)(Math.Max(0, bar.Value) / maximum * plotWidth);
            float x = left;
            float y = top + rowHeight * i + (rowHeight - barHeight) / 2f;
            e.Graphics.DrawString(bar.Label, labelFont, textBrush, new RectangleF(0, y, left - 10, barHeight), rightAligned);
            e.Graphics.FillRectangle(bar.Brush, x, y, width, barHeight);
            string value = bar.Value > 0 ? $"{bar.Value:0.#}" : "—";
            float valueX = Math.Min(x + width + 4, chartPanel.ClientSize.Width - right + 4);
            e.Graphics.DrawString(value, labelFont, titleBrush, valueX, y + (barHeight - labelFont.Height) / 2f);
        }
    }

    private void BtnClose_Click(object? sender, EventArgs e) => Close();

    // Appends the measured result to a CSV (one row per measurement).
    private void BtnCsv_Click(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "benchmark.csv",
            OverwritePrompt = false,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (engine, device, precision) = LastResultId();
            BenchmarkCsv.AppendRow(dlg.FileName, engine, device, precision,
                run: 1, _lastFrames, _lastSeconds, _measuredMpixel);
            lblLive.Text = $"CSV: row appended to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            lblLive.Text = $"CSV failed: {ex.Message}";
        }
    }

    private (string Engine, string Device, string Precision) LastResultId()
    {
        if (_useCuda)
            return ("CUDA", GpuMandelbrot.DeviceShortName, _useDouble ? "64-bit" : "32-bit");
        if (_useDirectX)
            return ("DirectX", DxMandelbrot.ShortAdapterName(DxMandelbrot.AdapterName), "float");
        return ("CPU", Diagnostics.CpuName(), "double");
    }

    // First frame of the benchmark zone (960x540, AA1x) made visible for the
    // engines that do not draw on a swapchain (CUDA and CPU): shows the tested
    // zone, as DirectX already does with the first frame on the swapchain.
    private Bitmap? RenderBenchmarkPreview(CancellationToken ct)
    {
        if (_useDirectX)
            return DxMandelbrot.RenderPreviewToBitmap(BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.Width, BenchmarkStandard.Height, BenchmarkStandard.MaxIter, 1, Palette.Fire);
        var bmp = new Bitmap(BenchmarkStandard.Width, BenchmarkStandard.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            if (_useCuda)
                GpuMandelbrot.Render(bmp, BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.MaxIter, Palette.Fire, 1, _useDouble, ct);
            else
                Mandelbrot.Render(bmp, BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, BenchmarkStandard.MaxIter, Palette.Fire, 1, ct);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            return null;
        }
    }

    // Standardized offscreen DirectX benchmark: renders the elementary-samples grid
    // (960x540 AA1x) with the iterations-only shader on
    // an in-memory render target, with no sample averaging and no Present.
    // Frame completion is detected with event queries: the per-frame work
    // is therefore identical to the CUDA and CPU benchmarks, and DWM/cross-GPU copy
    // do not skew cards without a monitor.
    private static async Task<(double Seconds, int Frames)> BenchmarkDirectX(IProgress<BenchmarkProgress> progress, CancellationToken ct)
    {
        int gridW = BenchmarkStandard.Width * BenchmarkStandard.Aa;
        int gridH = BenchmarkStandard.Height * BenchmarkStandard.Aa;
        DxMandelbrot.BeginBenchmarkOffscreen(gridW, gridH);
        try
        {
            double lastReport = 0;
            var (seconds, frames) = await Task.Run(() =>
                DxMandelbrot.RunBenchmarkFramesOffscreen(BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale, gridW, gridH, BenchmarkStandard.MaxIter, BenchmarkStandard.Budget,
                    (f, elapsed) =>
                    {
                        if (elapsed - lastReport >= BenchmarkProgress.ReportInterval.TotalSeconds)
                        {
                            progress.Report(new BenchmarkProgress(elapsed, 0, f));
                            lastReport = elapsed;
                        }
                    }, ct), ct);
            progress.Report(new BenchmarkProgress(seconds, 0, frames));
            return (seconds, frames);
        }
        finally
        {
            DxMandelbrot.EndBenchmarkOffscreen();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        previewBox.Image?.Dispose();
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
