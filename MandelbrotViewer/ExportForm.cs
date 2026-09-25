using System.Drawing.Imaging;

namespace MandelbrotViewer;

// High-resolution PNG export of the current view: offscreen render with the
// active engine (CPU/CUDA/DirectX) at the chosen width, saved as PNG.
// Supersampling is computed per tile to keep temporary memory bounded;
// cancellable during the render.
public partial class ExportForm : Form
{
    private const int TileSize = 512;

    private readonly double _cx, _cy, _scale;
    private readonly int _maxIter;
    private readonly Palette _palette;
    private readonly int _aa;
    private readonly bool _useCuda;
    private readonly bool _useDirectX;
    private readonly bool _julia;
    private readonly double _jcx, _jcy;
    private readonly double _aspect;
    private readonly int _viewW0, _viewH0;
    private CancellationTokenSource? _cts;
    private bool _rendering;

    public ExportForm(double cx, double cy, double scale, int maxIter, Palette palette, int aa,
        RenderEngine engine, bool useCuda, bool useDouble, Size viewSize,
        bool julia = false, double juliaCx = 0, double juliaCy = 0, int presetDefault = -1)
    {
        InitializeComponent();
        if (presetDefault >= 0 && presetDefault < cmbPreset.Items.Count)
            cmbPreset.SelectedIndex = presetDefault; // screenshot: starts from View, all free
        Program.ApplyIcon(this);

        _cx = cx;
        _cy = cy;
        _scale = scale;
        _maxIter = maxIter;
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
        _aspect = Math.Max(1, viewSize.Height) / (double)Math.Max(1, viewSize.Width);
        _viewW0 = Math.Max(320, viewSize.Width);
        _viewH0 = Math.Max(240, viewSize.Height);
        txtW.Text = _viewW0.ToString();
        txtH.Text = _viewH0.ToString();
        UpdateCustomEnabled();
        UpdateEngineLabel();
        UpdateInfo();
    }

    // Precision selected in this dialog (read on the UI thread; the render
    // worker receives a captured copy). Enabled with CUDA and CPU engines.
    private bool UseDouble => radPrec64.Checked;

    // CPU engine is active when neither GPU engine is (no stored flag).
    private bool UseCpu => !_useCuda && !_useDirectX;

    private void UpdateEngineLabel()
    {
        string engineLabel = _useCuda ? $"CUDA {(UseDouble ? "64-bit" : "32-bit")}"
            : _useDirectX ? "DirectX" : $"CPU {(UseDouble ? "64-bit" : "32-bit")}";
        string modeLabel = _julia ? $"Julia c={_jcx:+0.000000;-0.000000} {_jcy:+0.000000;-0.000000}i" : "Mandelbrot";
        lblInfo.Text = $"Engine: {engineLabel} — {modeLabel}, {_palette}, {_maxIter} iterations. " +
            $"Render tiled {TileSize}×{TileSize}: AA unchanged even at high resolutions.";
    }

    private void Prec_CheckedChanged(object? sender, EventArgs e) => UpdateEngineLabel();

    // Requested resolution and AA: preset or validated custom boxes.
    private bool ResolveSettings(out int w, out int h, out int reqAa, out int effAa)
    {
        w = h = reqAa = effAa = 0;
        switch (cmbPreset.SelectedIndex)
        {
            case 1: w = 1920; h = 1080; break; // Full HD
            case 2: w = 2560; h = 1440; break; // 2K
            case 3: w = 3840; h = 2160; break; // 4K
            case 4: w = 7680; h = 4320; break; // 8K
            case 5: w = 7680; h = 2160; break; // double 4K 16:9+16:9
            case 6: // custom: strict validation
                if (!int.TryParse(txtW.Text.Trim(), out w)
                    || !int.TryParse(txtH.Text.Trim(), out h)
                    || w < 320 || w > 16384 || h < 320 || h > 16384)
                    return false;
                break;
            default: w = _viewW0; h = _viewH0; break; // current view
        }
        reqAa = cmbAAExp.SelectedIndex <= 0 ? _aa : 1 << (cmbAAExp.SelectedIndex - 1);
        effAa = reqAa;
        return true;
    }

    private void UpdateInfo()
    {
        if (!ResolveSettings(out int w, out int h, out int reqAa, out int effAa))
        {
            lblResult.Text = "Custom dimensions: integers 320…16384 px.";
            return;
        }
        double mpixel = w * (double)h / 1e6;
        lblResult.Text = $"{w}×{h} ({mpixel:0.#} MPixel), AA{effAa}x — " +
            $"{w * (long)effAa * h * effAa / 1e6:0.#} MPixel of samples.";
    }

    private void CmbPreset_Changed(object? sender, EventArgs e)
    {
        UpdateCustomEnabled();
        UpdateInfo();
    }

    private void UpdateCustomEnabled()
    {
        bool custom = cmbPreset.SelectedIndex == 6;
        txtW.Enabled = custom;
        txtH.Enabled = custom;
    }

    private void CmbWidth_Changed(object? sender, EventArgs e) => UpdateInfo();

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_rendering) { _cts?.Cancel(); return; }
        if (!ResolveSettings(out int w, out int h, out _, out int effAa))
        {
            lblResult.Text = "Custom dimensions: integers 320…16384 px.";
            return;
        }

        _rendering = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool useDouble = UseDouble; // capture on the UI thread (worker must not touch controls)
        btnStart.Text = "Cancel";
        btnClose.Enabled = false;
        progressBar.Visible = true;
        lblResult.Text = $"Rendering {w}×{h} AA{effAa}x…";

        try
        {
            Bitmap? bmp = await Task.Run(() => RenderExport(w, h, effAa, useDouble, token), token);
            try
            {
                if (IsDisposed) return;
                token.ThrowIfCancellationRequested();
                if (bmp == null)
                {
                    lblResult.Text = "Render failed (DirectX not ready?).";
                    return;
                }
                using var dlg = new SaveFileDialog
                {
                    Filter = "PNG (*.png)|*.png",
                    FileName = "mandelbrot.png",
                    DefaultExt = "png",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                bmp.Save(dlg.FileName, ImageFormat.Png);
                lblResult.Text = $"Saved {w}×{h} AA{effAa}x to {Path.GetFileName(dlg.FileName)}";
            }
            finally
            {
                bmp?.Dispose();
            }
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
            _cts?.Dispose();
            _cts = null;
            _rendering = false;
            btnStart.Text = "Start";
            btnClose.Enabled = true;
            progressBar.Visible = false;
        }
    }

    // Renders the view at the chosen resolution with the active engine
    // (null when DirectX is not ready; the DX preview is synchronous and not cancellable
    // - closing discards the result).
    private Bitmap? RenderExport(int w, int h, int aa, bool useDouble, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y += TileSize)
            {
                int tileH = Math.Min(TileSize, h - y);
                for (int x = 0; x < w; x += TileSize)
                {
                    ct.ThrowIfCancellationRequested();
                    int tileW = Math.Min(TileSize, w - x);
                    using var tile = new Bitmap(tileW, tileH, PixelFormat.Format32bppArgb);
                    if (_useCuda)
                    {
                        GpuMandelbrot.RenderTile(tile, _cx, _cy, _scale, _maxIter, _palette, aa, useDouble,
                            ct, x, y, w, h, _jcx, _jcy, _julia);
                    }
                    else if (_useDirectX)
                    {
                        using Bitmap? rendered = DxMandelbrot.RenderPreviewToBitmap(_cx, _cy, _scale, tileW, tileH,
                            _maxIter, aa, _palette, w, h, x, y, _jcx, _jcy, _julia);
                        if (rendered == null)
                            throw new InvalidOperationException("DirectX tile render failed.");
                        CopyTile(rendered, tile, 0, 0);
                    }
                    else
                    {
                        Mandelbrot.RenderTile(tile, _cx, _cy, _scale, _maxIter, _palette, aa, ct,
                            x, y, w, h, _jcx, _jcy, _julia, useDouble);
                    }
                    CopyTile(tile, result, x, y);
                }
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void CopyTile(Bitmap source, Bitmap destination, int destinationX, int destinationY)
    {
        using var g = Graphics.FromImage(destination);
        g.DrawImageUnscaled(source, destinationX, destinationY);
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
