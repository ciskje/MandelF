using System.Text.Json;

namespace MandelbrotViewer;

public partial class MandelbrotForm : Form
{
    // Initial view: full set with margin (re in [-2.68, 1.68], im in [-2.34, 2.34]).
    internal const double StartCenterX = -0.5;
    internal const double StartCenterY = 0.0;
    internal const double StartScale = 9.36; // width in complex units

    private double _centerX = StartCenterX;
    private double _centerY = StartCenterY;
    private double _scale = StartScale;
    private RenderEngine _engine = RenderEngine.Cpu;
    private AppSettings _settings = new();
    private bool _suspendRender = true; // true until the constructor applies the settings
    private string? _gpuSelection;      // selected video card (null = auto)

    private Bitmap? _fractal;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _realTimeCts;
    private bool _realTimeActive;
    private System.Windows.Forms.Timer _resizeTimer = null!;
    private System.Windows.Forms.Timer _dxTimer = null!;
    private bool _dxDirty = true; // next DirectX frame to redraw

    // Click = zoom, dragging = pan.
    private const int DragThresholdPx = 5; // below: it's a click, above: it's a drag
    private const int PanThrottleMs = 80;  // resample pan at most every 80 ms
    private bool _dragging;
    private MouseButtons _dragButton;
    private Point _downPos;   // where the button was pressed
    private readonly Stack<ViewZone> _backZones = new();   // history: previous views
    private readonly Stack<ViewZone> _forwardZones = new(); // history: subsequent views
    private ViewZone? _dragStartZone; // view at drag start (for history)
    private ToolStripMenuItem _backItem = null!;
    private ToolStripMenuItem _forwardItem = null!;
    private ToolStripMenuItem _favoritesMenu = null!;
    private ToolStripMenuItem _removeFavMenu = null!;
    private ToolStripMenuItem _juliaItem = null!;
    private bool _julia; // Julia mode: c fixed, z(0) = pixel point
    private double _jcx = DefaultJcx, _jcy = DefaultJcy;
    private const double DefaultJcx = -0.7;
    private const double DefaultJcy = 0.27015;
    private Point _lastPos;   // last position during drag
    private bool _moved;      // true if drag threshold exceeded
    private int _lastPanTick; // Environment.TickCount of last pan render

    public MandelbrotForm()
    {
        InitializeComponent();
        Text += $" v{AppVersion.Display}";
        Program.ApplyIcon(this);

        _resizeTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); RenderAsync(); };

        _dxTimer = new System.Windows.Forms.Timer { Interval = 16 }; // loop realtime ~60fps
        _dxTimer.Tick += DxTimer_Tick;
        _dxTimer.Start();

        LoadSettings();
        _suspendRender = false;
        BuildViewMenu();

        Shown += async (s, e) =>
        {
            // GPU dropdown: union of DirectX cards and CUDA devices.
            var dxNames = DxMandelbrot.AdapterNames();
            var cudaNames = await Task.Run(GpuMandelbrot.DeviceNames);
            foreach (string n in dxNames.Concat(cudaNames).Distinct().OrderBy(n => n))
                if (!cmbGpu.Items.Contains(n)) cmbGpu.Items.Add(n);

            string savedGpu = _settings.Gpu;
            if (!string.IsNullOrEmpty(savedGpu) && cmbGpu.Items.Contains(savedGpu))
                cmbGpu.SelectedItem = savedGpu;
            _gpuSelection = cmbGpu.SelectedIndex > 0 ? cmbGpu.SelectedItem!.ToString() : null;

            bool dxOk = DxMandelbrot.TryInitialize(dxPanel.Handle, Math.Max(1, dxPanel.Width), Math.Max(1, dxPanel.Height), _gpuSelection);
            if (dxOk) radioDx.Enabled = true;
            if (_settings.Engine == nameof(RenderEngine.DirectX) && dxOk)
                radioDx.Checked = true;
            ApplyEngineVisibility(); // first paint (bitmap, or DX if preselected)

            bool gpu = await Task.Run(() => GpuMandelbrot.TryInitialize(_gpuSelection));
            if (gpu && !IsDisposed)
            {
                radioCuda.Enabled = true;
                // Use GPU if it's the saved preference (default for new installations).
                if (_settings.Engine == nameof(RenderEngine.Cuda))
                    radioCuda.Checked = true;
            }
            if (_settings.Engine == nameof(RenderEngine.Rust) && RustMandelbrot.IsAvailable)
                radioRust.Checked = true;

            // If a GPU engine is not available, show the reason (previously it stayed gray).
            var issues = new List<string>();
            if (!dxOk) issues.Add("DirectX: " + (DxMandelbrot.LastError.Length > 0 ? DxMandelbrot.LastError : "initialization failed"));
            if (!gpu) issues.Add("CUDA: " + (GpuMandelbrot.LastError.Length > 0 ? GpuMandelbrot.LastError : "no device available"));
            if (issues.Count > 0 && !IsDisposed)
                lblStatus.Text = string.Join("   |   ", issues);
        };
    }

    private int MaxIter => chkIterAuto.Checked ? AutoIter() : (int)numIter.Value;

    // Antialiasing factor from dropdown (1x = disabled, 2x/4x/8x = enabled).
    private int AaFactor => cmbAA.SelectedIndex > 0 ? 1 << cmbAA.SelectedIndex : 1;

    private Palette ActivePalette => cmbPalette.SelectedIndex < 0
        ? Palette.Fire
        : (Palette)cmbPalette.SelectedIndex;

    // User-selected CUDA precision: 64-bit (double) if radio 64, otherwise 32-bit (float).
    private bool UseDoublePrecision => radPrec64.Checked;

    private int AutoIter()
    {
        // The more you zoom in, the more iterations are needed for sharp edges.
        // 2000 at initial view (half side 1.5) + 2000 per 10x: 2000*(1+log10(1.5/half)).
        int iter = Mandelbrot.AutoIterForScale(_scale);
        return Math.Clamp(iter, (int)numIter.Minimum, (int)numIter.Maximum);
    }

    // ---------- Rendering ----------

    // Param preview (bool): True during dragging: no AA and 1/4 of the pixels.
    private async void RenderAsync(bool preview = false)
    {
        if (_suspendRender) return;
        int fullW = Math.Max(1, pictureBox.Width);
        int fullH = Math.Max(1, pictureBox.Height);
        // Fast preview: half per side (= quarter of pixels), no antialiasing.
        int w = preview ? Math.Max(1, fullW / 2) : fullW;
        int h = preview ? Math.Max(1, fullH / 2) : fullH;

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var token = cts.Token;

        double cx = _centerX, cy = _centerY, scale = _scale;
        int maxIter = MaxIter;
        Palette palette = ActivePalette;
        int aa = preview ? 1 : AaFactor;
        bool useCuda = _engine == RenderEngine.Cuda && GpuMandelbrot.IsReady;
        bool useRust = _engine == RenderEngine.Rust && RustMandelbrot.IsAvailable;
        bool gpuDouble = false;
        if (chkIterAuto.Checked)
            numIter.Value = maxIter; // in auto the number is disabled but shows the value used

        lblStatus.Text = $"Computing {(preview ? "preview " : "")}{w}x{h}, iter={maxIter}...";
        SetBusyCursor(true);

        try
        {
            var bmp = new Bitmap(w, h);
            if (useCuda)
                gpuDouble = await Task.Run(() => GpuMandelbrot.Render(bmp, cx, cy, scale, maxIter, palette, aa, UseDoublePrecision, token, _jcx, _jcy, _julia), token);
            else if (useRust)
            {
                try
                {
                    await Task.Run(() => RustMandelbrot.Render(bmp, cx, cy, scale, maxIter, palette, aa, token, _jcx, _jcy, _julia, UseDoublePrecision), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    bmp.Dispose();
                    _engine = RenderEngine.Cpu;
                    radioCpu.Checked = true;
                    ApplyEngineVisibility();
                    lblStatus.Text = "Rust: " + ex.Message + " — falling back to CPU";
                    return;
                }
            }
            else
                await Task.Run(() => Mandelbrot.Render(bmp, cx, cy, scale, maxIter, palette, aa, token, _jcx, _jcy, _julia, UseDoublePrecision), token);

            if (preview && (bmp.Width != fullW || bmp.Height != fullH))
                bmp = Upscale(bmp, fullW, fullH);

            if (token.IsCancellationRequested) { bmp.Dispose(); return; }

            var old = _fractal;
            _fractal = bmp;
            pictureBox.Image = _fractal;
            old?.Dispose();

            string engineLabel = useCuda ? $"CUDA-{(gpuDouble ? "double" : "float")} {GpuMandelbrot.DeviceShortName}" : useRust ? $"Rust-{(UseDoublePrecision ? "double" : "float")}" : $"CPU-{(UseDoublePrecision ? "double" : "float")}";
            string juliaLabel = _julia ? $" | Julia c={_jcx:+0.000000;-0.000000} {_jcy:+0.000000;-0.000000}i" : "";
            lblStatus.Text = $"Center {cx:+0.000000;-0.000000} {cy:+0.000000;-0.000000}i | width {scale:E2} | iter {maxIter}{(chkIterAuto.Checked ? " (auto)" : "")} | {ActivePalette}{(aa > 1 ? $" AA{aa}x" : "")} | engine {engineLabel}{juliaLabel}{(preview ? " (preview)" : "")}";
        }
        catch (OperationCanceledException) { /* rendering superseded, ignore */ }
        finally
        {
            // Only the latest render owns the cursor: stale superseded renders
            // must not touch it, but the latest one must always restore it,
            // even when cancelled (otherwise AppStarting stays stuck on).
            if (ReferenceEquals(_renderCts, cts)) SetBusyCursor(false);
        }
    }

    // Sets (or restores) the busy cursor on the form and all descendants
    // (recursive: covers pictureBox, menus and status bar). Uses AppStarting
    // (arrow+hourglass) not Wait, because during async render the UI stays
    // interactive (pan/zoom cancel and restart the computation); `UseWaitCursor`
    // cannot be used because it forces the full hourglass.
    // On restore, the image panels go back to Cross (their Designer cursor),
    // not Default, otherwise the crosshair is lost after the first render.
    private void SetBusyCursor(bool busy)
    {
        ApplyCursorRecursive(this, busy ? Cursors.AppStarting : Cursors.Default);
        if (!busy)
        {
            pictureBox.Cursor = Cursors.Cross;
            dxPanel.Cursor = Cursors.Cross;
        }
    }

    private static void ApplyCursorRecursive(Control root, Cursor cursor)
    {
        root.Cursor = cursor;
        foreach (Control child in root.Controls)
            ApplyCursorRecursive(child, cursor);
    }

    // Upscales the preview bitmap to full resolution (bilinear).
    private static Bitmap Upscale(Bitmap small, int fullW, int fullH)
    {
        var up = new Bitmap(fullW, fullH);
        using (var g = Graphics.FromImage(up))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(small, 0, 0, fullW, fullH);
        }
        small.Dispose();
        return up;
    }

    // Redraws: realtime if DirectX engine, otherwise bitmap render (optional preview).
    private void InvalidateView(bool preview = false)
    {
        if (_engine == RenderEngine.DirectX && DxMandelbrot.IsReady)
            _dxDirty = true;
        else
            RenderAsync(preview);
    }

    private void ApplyEngineVisibility()
    {
        bool dx = _engine == RenderEngine.DirectX && DxMandelbrot.IsReady;
        dxPanel.Visible = dx;
        pictureBox.Visible = !dx;
        bool gpuEngine = _engine != RenderEngine.Cpu && _engine != RenderEngine.Rust; // the GPU dropdown is shown only with a GPU engine
        lblGpu.Visible = gpuEngine;
        cmbGpu.Visible = gpuEngine;
        bool prec = _engine != RenderEngine.DirectX; // precision choice for CPU and CUDA (DirectX is always float)
        radPrec32.Enabled = prec;
        radPrec64.Enabled = prec;
        if (dx)
            _dxDirty = true;
        else
            RenderAsync();
    }

    private void DxTimer_Tick(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (_engine != RenderEngine.DirectX || !DxMandelbrot.IsReady) return;
        if (dxPanel.Width <= 0 || dxPanel.Height <= 0) return;
        try
        {
            DxMandelbrot.Resize(dxPanel.Width, dxPanel.Height);
            if (!_dxDirty) return;
            _dxDirty = false;
            DxMandelbrot.Render(_centerX, _centerY, _scale, dxPanel.Width, dxPanel.Height, MaxIter, AaFactor, ActivePalette, _jcx, _jcy, _julia);
            UpdateDxStatus();
        }
        catch (Exception ex)
        {
            FallbackToCpu("DirectX: " + ex.Message);
        }
    }

    private void UpdateDxStatus()
    {
        string warn = GpuMandelbrot.WantsDouble(_scale) ? " [beyond float!]" : "";
        string juliaLabel = _julia ? $" | Julia c={_jcx:+0.000000;-0.000000} {_jcy:+0.000000;-0.000000}i" : "";
        lblStatus.Text = $"Center {_centerX:+0.000000;-0.000000} {_centerY:+0.000000;-0.000000}i | width {_scale:E2} | iter {MaxIter}{(chkIterAuto.Checked ? " (auto)" : "")} | {ActivePalette} | engine DirectX (float){warn}{juliaLabel}";
    }

    private void FallbackToCpu(string reason)
    {
        DxMandelbrot.Dispose();
        radioDx.Enabled = false;
        if (_engine == RenderEngine.DirectX)
        {
            _engine = RenderEngine.Cpu;
            radioCpu.Checked = true;
        }
        ApplyEngineVisibility();
        lblStatus.Text = reason + " — falling back to CPU";
    }

    // ---------- Coordinates ----------

    private (double cx, double cy) PixelToComplex(Point p)
    {
        Size vs = ActiveViewSize;
        int w = vs.Width, h = vs.Height;
        double pixelSize = _scale / w;
        double cx = _centerX + (p.X - w * 0.5) * pixelSize;
        double cy = _centerY + (p.Y - h * 0.5) * pixelSize;
        return (cx, cy);
    }

    private void ZoomAt(Point p, double factor)
    {
        // Zoom centered on the mouse point (factor < 1 = zoom in).
        PushHistory();
        var (cx, cy) = PixelToComplex(p);
        _centerX = cx + (_centerX - cx) * factor;
        _centerY = cy + (_centerY - cy) * factor;
        _scale *= factor;
        InvalidateView();
    }

    // Active view dimensions (DirectX panel or bitmap pictureBox).
    private Size ActiveViewSize =>
        (_engine == RenderEngine.DirectX && DxMandelbrot.IsReady) ? dxPanel.Size : pictureBox.Size;

    private void PanBy(Point from, Point to)
    {
        // Moves the view following the mouse: the point under the cursor stays under the cursor.
        double pixelSize = _scale / Math.Max(1, ActiveViewSize.Width);
        _centerX += (from.X - to.X) * pixelSize;
        _centerY += (from.Y - to.Y) * pixelSize;
    }

    private void Reset()
    {
        PushHistory();
        _jcx = DefaultJcx; // also reset Julia c (the mode stays)
        _jcy = DefaultJcy;
        _centerX = StartCenterX;
        _centerY = StartCenterY;
        _scale = StartScale;
        InvalidateView();
    }

    private void SavePng()
    {
        Bitmap? captured = null;
        try
        {
            Bitmap? src = (_engine == RenderEngine.DirectX && DxMandelbrot.IsReady)
                ? (captured = DxMandelbrot.Capture()) // from the DirectX backbuffer
                : _fractal;
            if (src == null) return;
            using var dlg = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png",
                FileName = "mandelbrot.png"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                src.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                lblStatus.Text = $"Saved to {dlg.FileName}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed:\n{ex.Message}",
                "Save PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            captured?.Dispose();
        }
    }

    // ---------- Zones (saving/loading view as JSON) ----------

    // Saved view: center, complex width, iterations and Julia mode
    // (Julia fields have defaults: old files load as Mandelbrot).
    private sealed record ViewZone(double CenterX, double CenterY, double Scale, int MaxIter,
        bool Julia = false, double Jcx = DefaultJcx, double Jcy = DefaultJcy);

    private void SaveZone()
    {
        var zone = new ViewZone(_centerX, _centerY, _scale, MaxIter);
        using var dlg = new SaveFileDialog
        {
            Filter = "Mandelbrot Zone (*.json)|*.json",
            FileName = "zone.json",
            DefaultExt = "json"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string json = JsonSerializer.Serialize(zone, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, json);
        lblStatus.Text = $"Zone saved to {dlg.FileName}";
    }

    private void LoadZone()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Mandelbrot Zone (*.json)|*.json",
            DefaultExt = "json"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string json;
        try
        {
            json = File.ReadAllText(dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WarnZone("Load zone", ex.Message);
            return;
        }
        if (!TryParseZone(json, out var zone, out string error) || zone == null)
        {
            WarnZone("Load zone", error);
            return;
        }
        PushHistory();
        ApplyZone(zone);
        lblStatus.Text = $"Zone loaded from {dlg.FileName}";
    }

    private static bool TryParseZone(string json, out ViewZone? zone, out string error)
    {
        zone = null;
        error = "";
        try
        {
            zone = JsonSerializer.Deserialize<ViewZone>(json);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        if (zone == null || !double.IsFinite(zone.CenterX) || !double.IsFinite(zone.CenterY)
            || !double.IsFinite(zone.Scale) || zone.Scale <= 0)
        {
            error = "The file does not contain a valid zone.";
            return false;
        }
        return true;
    }

    private void WarnZone(string title, string message) =>
        MessageBox.Show(this, $"Cannot load zone:\n{message}",
            title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // Applies a zone (history, favorites, file): explicit iterations.
    private void ApplyZone(ViewZone zone)
    {
        _centerX = zone.CenterX;
        _centerY = zone.CenterY;
        _scale = zone.Scale;
        _julia = zone.Julia;
        _jcx = zone.Jcx;
        _jcy = zone.Jcy;
        _juliaItem.Checked = _julia;
        chkIterAuto.Checked = false; // the zone saves explicit iterations
        numIter.Value = Math.Clamp(zone.MaxIter, (int)numIter.Minimum, (int)numIter.Maximum);
        InvalidateView();
    }

    // ---------- History and favorites ----------

    private ViewZone CurrentZone() => new(_centerX, _centerY, _scale, MaxIter, _julia, _jcx, _jcy);

    // Records the current view before a committed change (max 200).
    private void PushHistory()
    {
        _backZones.Push(CurrentZone());
        _forwardZones.Clear();
        while (_backZones.Count > 200)
        {
            var arr = _backZones.ToArray();
            _backZones.Clear();
            foreach (var z in arr[..^1].Reverse())
                _backZones.Push(z);
        }
        UpdateHistoryMenu();
    }

    private void GoBack()
    {
        if (_backZones.Count == 0) return;
        _forwardZones.Push(CurrentZone());
        ApplyZone(_backZones.Pop());
        UpdateHistoryMenu();
    }

    private void GoForward()
    {
        if (_forwardZones.Count == 0) return;
        _backZones.Push(CurrentZone());
        ApplyZone(_forwardZones.Pop());
        UpdateHistoryMenu();
    }

    // Toggles Julia mode (CheckOnClick has already updated the check).
    private void ToggleJulia()
    {
        PushHistory();
        _julia = _juliaItem.Checked;
        InvalidateView();
    }

    private void UpdateHistoryMenu()
    {
        if (_backItem == null) return;
        _backItem.Enabled = _backZones.Count > 0;
        _forwardItem.Enabled = _forwardZones.Count > 0;
    }

    private static string ZonesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MandelbrotViewer", "zone");

    // View menu: history + favorites (built in code because
    // the favorites list is dynamic).
    private void BuildViewMenu()
    {
        var viewMenu = new ToolStripMenuItem("&View");
        _backItem = new ToolStripMenuItem("Back", null, (s, e) => GoBack())
        {
            ShortcutKeys = Keys.Alt | Keys.Left,
            ToolTipText = "Go to previous view",
        };
        _forwardItem = new ToolStripMenuItem("Forward", null, (s, e) => GoForward())
        {
            ShortcutKeys = Keys.Alt | Keys.Right,
            ToolTipText = "Go to next view",
        };
        _juliaItem = new ToolStripMenuItem("&Julia mode", null, (s, e) => ToggleJulia())
        {
            ShortcutKeys = Keys.Control | Keys.J,
            ToolTipText = "Julia set with fixed c (click = sets c, rest unchanged)",
            Checked = _julia,
            CheckOnClick = true,
        };
        var addFav = new ToolStripMenuItem("Save favorite zone…", null, (s, e) => SaveFavorite())
        {
            ShortcutKeys = Keys.Control | Keys.D,
            ToolTipText = "Save current view to favorites (JSON file)",
        };
        _favoritesMenu = new ToolStripMenuItem("Favorite zones")
        {
            ToolTipText = "Go to a saved favorite zone",
        };
        _favoritesMenu.DropDownOpening += (s, e) => RebuildFavoritesMenu();
        _removeFavMenu = new ToolStripMenuItem("Delete favorite")
        {
            ToolTipText = "Delete a favorite zone file",
        };
        _removeFavMenu.DropDownOpening += (s, e) => RebuildRemoveFavMenu();
        viewMenu.DropDownItems.AddRange(new ToolStripItem[] {
            _backItem, _forwardItem, new ToolStripSeparator(),
            _juliaItem, new ToolStripSeparator(),
            addFav, _favoritesMenu, _removeFavMenu });
        menuStrip.Items.Insert(1, viewMenu);
        UpdateHistoryMenu();
    }

    private void SaveFavorite()
    {
        try
        {
            Directory.CreateDirectory(ZonesDir);
            using var dlg = new SaveFileDialog
            {
                Filter = "Mandelbrot Zone (*.json)|*.json",
                DefaultExt = "json",
                InitialDirectory = ZonesDir,
                FileName = "favorite.json",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (!Path.GetFullPath(dlg.FileName).StartsWith(
                    Path.GetFullPath(ZonesDir), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Choose a folder inside:\n" + ZonesDir,
                    "Favorite zone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string json = JsonSerializer.Serialize(CurrentZone(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            lblStatus.Text = $"Favorite saved: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Cannot save favorite:\n{ex.Message}",
                "Favorite zone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string[] FavoriteFiles()
    {
        try
        {
            if (!Directory.Exists(ZonesDir)) return Array.Empty<string>();
            return Directory.GetFiles(ZonesDir, "*.json")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private void RebuildFavoritesMenu()
    {
        _favoritesMenu.DropDownItems.Clear();
        string[] files = FavoriteFiles();
        if (files.Length == 0)
        {
            _favoritesMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var item = new ToolStripMenuItem(name, null, (s, e) => LoadFavoriteFile(file))
            {
                ToolTipText = file,
            };
            _favoritesMenu.DropDownItems.Add(item);
        }
    }

    private void RebuildRemoveFavMenu()
    {
        _removeFavMenu.DropDownItems.Clear();
        string[] files = FavoriteFiles();
        if (files.Length == 0)
        {
            _removeFavMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string captured = file;
            _removeFavMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (s, e) =>
            {
                try
                {
                    File.Delete(captured);
                    lblStatus.Text = $"Favorite deleted: {name}";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(this, $"Cannot delete:\n{ex.Message}",
                        "Favorite zone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }));
        }
    }

    private void LoadFavoriteFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WarnZone("Favorite zone", ex.Message);
            return;
        }
        if (!TryParseZone(json, out var zone, out string error) || zone == null)
        {
            WarnZone("Favorite zone", error);
            return;
        }
        PushHistory();
        ApplyZone(zone);
        lblStatus.Text = $"Favorite: {Path.GetFileNameWithoutExtension(path)}";
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"Mandelbrot Set Viewer v{AppVersion.Display}\n" +
            $"Engine: {EngineDescription()}\n\n" +
            "Explore the fractal z = z² + c with smooth coloring.\n\n" +
            "Left click: zoom in 2x at point\n" +
            "Right click: zoom out 2x at point\n" +
            "Wheel: zoom at cursor\n" +
            "Drag: pan the view\n" +
            "R: reset | S: save PNG | +/-: iterations | Arrows: pan\n" +
            "Alt+Left/Right: view history | View menu: favorite zones (Ctrl+D)\n" +
            "Ctrl+J: Julia mode (click = sets c, zoom/pan unchanged)\n" +
            "Palette from dropdown (Fire, Ice, Thermal, Ocean, Violet, Desert, Forest)\n" +
            "Auto iterations: checkbox, grow with zoom level\n" +
            "AA: 1x off, 2x/4x/8x with nearby pixel averaging\n" +
            "Engines: CPU, Rust (SIMD via mandel_ffi.dll), CUDA (compute) or DirectX (realtime, float)\n" +
            "GPU: choose video card from dropdown (Auto = most powerful)\n\n" +
            "File menu: save/load zone as JSON.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string EngineDescription() => _engine switch
    {
        RenderEngine.DirectX when DxMandelbrot.IsReady => "DirectX realtime (float)",
        RenderEngine.Cuda when GpuMandelbrot.IsReady => $"CUDA ({GpuMandelbrot.DeviceShortName})",
        RenderEngine.Rust when RustMandelbrot.IsAvailable => "Rust SIMD (mandel-ffi)",
        _ => "CPU multicore",
    };

    // Applies saved settings (without rendering: Shown handles that).
    private void LoadSettings()
    {
        _settings = AppSettings.Load();
        // The view is not persisted: always starts from the full set.
        numIter.Value = Math.Clamp(_settings.MaxIter, (int)numIter.Minimum, (int)numIter.Maximum);
        chkIterAuto.Checked = _settings.IterAuto;
        cmbPalette.SelectedIndex = Math.Clamp(_settings.Palette, 0, cmbPalette.Items.Count - 1);
        cmbAA.SelectedIndex = Math.Clamp(_settings.AaIndex, 0, cmbAA.Items.Count - 1);
        radPrec32.Checked = _settings.Single; // 32-bit if Single, otherwise stays 64-bit
        _julia = _settings.Julia;
        _jcx = double.IsFinite(_settings.Jcx) ? _settings.Jcx : DefaultJcx;
        _jcy = double.IsFinite(_settings.Jcy) ? _settings.Jcy : DefaultJcy;
        _gpuSelection = string.IsNullOrEmpty(_settings.Gpu) ? null : _settings.Gpu;

        var r = new Rectangle(_settings.WinX, _settings.WinY, _settings.WinW, _settings.WinH);
        if (r.Width > 0 && r.Height > 0 && Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(r)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
        }
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveSettings()
    {
        try
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.IterAuto = chkIterAuto.Checked;
            _settings.MaxIter = (int)numIter.Value;
            _settings.Palette = cmbPalette.SelectedIndex;
            _settings.AaIndex = cmbAA.SelectedIndex;
            _settings.Single = radPrec32.Checked; // true = 32-bit (float)
            _settings.Julia = _julia;
            _settings.Jcx = _jcx;
            _settings.Jcy = _jcy;
            _settings.Engine = _engine.ToString();
            _settings.Gpu = _gpuSelection ?? "";
            _settings.WinX = r.X;
            _settings.WinY = r.Y;
            _settings.WinW = r.Width;
            _settings.WinH = r.Height;
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            _settings.Save();
        }
        catch
        {
            // Never block closing because of settings.
        }
    }

    // ---------- UI Events ----------

    private void BtnReset_Click(object? sender, EventArgs e) => Reset();

    private void SaveImageItem_Click(object? sender, EventArgs e) => SavePng(); // File menu → "Save image..." (Ctrl+Shift+S)

    private void ExportShotItem_Click(object? sender, EventArgs e)
    {
        bool useCuda = _engine == RenderEngine.Cuda && GpuMandelbrot.IsReady;
        using var dlg = new ExportForm(_centerX, _centerY, _scale, MaxIter, ActivePalette,
            AaFactor, _engine, useCuda, UseDoublePrecision, ActiveViewSize, _julia, _jcx, _jcy,
            presetDefault: 0); // screenshot: starts from current view, free preset and AA
        dlg.ShowDialog(this);
    }

    private void VideoItem_Click(object? sender, EventArgs e)
    {
        bool useCuda = _engine == RenderEngine.Cuda && GpuMandelbrot.IsReady;
        using var dlg = new ZoomVideoForm(_centerX, _centerY, _scale, ActivePalette, AaFactor,
            _engine, useCuda, UseDoublePrecision, ActiveViewSize, _julia, _jcx, _jcy);
        dlg.ShowDialog(this);
    }

    private async void RealTimeItem_Click(object? sender, EventArgs e)
    {
        if (_realTimeActive) { _realTimeCts?.Cancel(); return; }

        if (StartScale / _scale < 2.0)
        {
            MessageBox.Show(this,
                "Already at the full set: frame a zone first.",
                "Back to set", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _realTimeActive = true;
        _realTimeCts = new CancellationTokenSource();
        var token = _realTimeCts.Token;
        const int frames = 120;
        const int fps = 30;
        int frameMs = 1000 / fps;
        double startCx = _centerX, startCy = _centerY, startScale = _scale;
        int savedAa = cmbAA.SelectedIndex;
        cmbAA.SelectedIndex = 0;
        realTimeItem.Text = "Stop (Esc)";
        lblStatus.Text = "Going back to set (AA 1x)... (Esc to stop)";

        try
        {
            for (int i = 0; i < frames; i++)
            {
                token.ThrowIfCancellationRequested();
                double t = frames > 1 ? i / (double)(frames - 1) : 1.0;
                double te = 1.0 - Math.Pow(1.0 - t, 3.0);
                double scale = startScale * Math.Pow(StartScale / startScale, te);
                double span = StartScale - startScale;
                double u = span > 0 ? (scale - startScale) / span : 1.0;
                _centerX = startCx + (StartCenterX - startCx) * u;
                _centerY = startCy + (StartCenterY - startCy) * u;
                _scale = scale;
                InvalidateView();
                lblStatus.Text = $"Back to set {i + 1}/{frames}... (Esc to stop)";
                if (i < frames - 1)
                    await Task.Delay(frameMs, token);
            }
            lblStatus.Text = "Back to set: completed.";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Back to set: interrupted.";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            _realTimeCts?.Dispose();
            _realTimeCts = null;
            _realTimeActive = false;
            cmbAA.SelectedIndex = savedAa;
            realTimeItem.Text = "Back to set (&RealTime)";
        }
    }

    private void SaveZoneItem_Click(object? sender, EventArgs e) => SaveZone();

    private void LoadZoneItem_Click(object? sender, EventArgs e) => LoadZone();

    private void AboutItem_Click(object? sender, EventArgs e) => ShowAbout();

    private void LogItem_Click(object? sender, EventArgs e)
    {
        var dlg = new LogForm(BuildDiagnosticLog()) { Owner = this };
        dlg.ShowDialog(this);
    }

    private string BuildDiagnosticLog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Mandelbrot Set Viewer   v{AppVersion.Full}");
        sb.AppendLine($"Opened at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Runtime .NET: {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine("=== Engine ===");
        sb.AppendLine($"Selected: {RenderEngineInfo.DisplayName(_engine)}");
        sb.AppendLine($"In use:   {EngineDescription()}");
        sb.AppendLine();
        sb.AppendLine("=== DirectX (D3D11) ===");
        sb.AppendLine($"Ready:        {(DxMandelbrot.IsReady ? "yes" : "NO")}");
        sb.AppendLine($"Card in use: {(DxMandelbrot.AdapterName.Length > 0 ? DxMandelbrot.AdapterName : "(none)")}");
        sb.AppendLine($"Last error:   {(DxMandelbrot.LastError.Length > 0 ? DxMandelbrot.LastError : "(none)")}");
        sb.AppendLine($"DXGI cards:   {JoinOrNone(DxMandelbrot.AdapterNames())}");
        sb.AppendLine($"DXGI enum error: {(DxMandelbrot.EnumerationError.Length > 0 ? DxMandelbrot.EnumerationError : "(none)")}");
        sb.AppendLine();
        sb.AppendLine("=== CUDA (native) ===");
        sb.AppendLine($"Ready:         {(GpuMandelbrot.IsReady ? "yes" : "NO")}");
        sb.AppendLine($"Device in use: {(GpuMandelbrot.DeviceName.Length > 0 ? GpuMandelbrot.DeviceName : "(none)")}");
        sb.AppendLine($"Details:       {(GpuMandelbrot.DeviceDetails.Length > 0 ? GpuMandelbrot.DeviceDetails : "(none)")}");
        sb.AppendLine($"Last error:    {(GpuMandelbrot.LastError.Length > 0 ? GpuMandelbrot.LastError : "(none)")}");
        sb.AppendLine($"Device CUDA:    {JoinOrNone(GpuMandelbrot.DeviceNames())}");
        sb.AppendLine();
        sb.AppendLine("=== GPU selection ===");
        sb.AppendLine($"Selected card: {(_gpuSelection ?? "Auto")}");
        sb.AppendLine($"Precision (CPU/CUDA): {(UseDoublePrecision ? "64-bit (double)" : "32-bit (float)")}");
        sb.AppendLine();
        sb.AppendLine("=== Saved settings ===");
        sb.AppendLine($"Engine:     {_settings.Engine}");
        sb.AppendLine($"GPU:        {(_settings.Gpu.Length > 0 ? _settings.Gpu : "Auto")}");
        sb.AppendLine($"Palette:    {_settings.Palette}    AA: {_settings.AaIndex}    Iterations: {_settings.MaxIter} (auto = {_settings.IterAuto})");
        sb.AppendLine($"Precision (Single): {_settings.Single}");
        sb.AppendLine();
        sb.AppendLine("=== Event log ===");
        sb.Append(AppLog.GetText());
        return sb.ToString();
    }

    private static string JoinOrNone(IReadOnlyList<string> items)
        => items.Count > 0 ? string.Join(",  ", items) : "(none)";

    private void BenchmarkItem_Click(object? sender, EventArgs e)
    {
        bool pauseDirectX = _engine == RenderEngine.DirectX && DxMandelbrot.IsReady;
        if (pauseDirectX)
        {
            _dxTimer.Stop();
            // During the benchmark the swapchain shows the gray sample frame:
            // hides the DX panel to avoid confusing the main view.
            dxPanel.Visible = false;
        }
        try
        {
            using var dlg = new BenchmarkForm(_engine, UseDoublePrecision);
            dlg.ShowDialog(this);
        }
        finally
        {
            if (pauseDirectX)
            {
                dxPanel.Visible = true;
                _dxDirty = true;
                _dxTimer.Start();
            }
        }
    }

    private void NumIter_ValueChanged(object? sender, EventArgs e)
    {
        if (!chkIterAuto.Checked) InvalidateView(); // in auto iterations are decided by zoom
    }

    private void ChkIterAuto_CheckedChanged(object? sender, EventArgs e)
    {
        numIter.Enabled = !chkIterAuto.Checked;
        InvalidateView();
    }

    private void CmbAA_SelectedIndexChanged(object? sender, EventArgs e) => InvalidateView();

    private void CmbPalette_SelectedIndexChanged(object? sender, EventArgs e) => InvalidateView();

    private void PrecRadio_CheckedChanged(object? sender, EventArgs e) => InvalidateView();

    // Label of the GPU selected in the dropdown ("Auto" if index 0).
    private string GpuLabel() => cmbGpu.SelectedIndex > 0 ? cmbGpu.SelectedItem?.ToString() ?? "Auto" : "Auto";

    private void CmbGpu_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbGpu.SelectedIndex < 0) return;
        _gpuSelection = cmbGpu.SelectedIndex > 0 ? cmbGpu.SelectedItem!.ToString() : null;
        string name = GpuLabel();

        if (_engine == RenderEngine.DirectX)
        {
            if (DxMandelbrot.TryInitialize(dxPanel.Handle, Math.Max(1, dxPanel.Width), Math.Max(1, dxPanel.Height), _gpuSelection))
            {
                _dxDirty = true;
                lblStatus.Text = $"GPU: {name} (DirectX)";
            }
            else
            {
                FallbackToCpu($"DirectX su '{name}': {DxMandelbrot.LastError}");
            }
        }
        else if (_engine == RenderEngine.Cuda)
        {
            if (GpuMandelbrot.TryInitialize(_gpuSelection))
            {
                InvalidateView();
                lblStatus.Text = $"GPU: {name} (CUDA {GpuMandelbrot.DeviceShortName})";
            }
            else
            {
                _engine = RenderEngine.Cpu;
                radioCpu.Checked = true;
                ApplyEngineVisibility();
                lblStatus.Text = $"CUDA on '{name}': {GpuMandelbrot.LastError} — switching to CPU";
            }
        }
        else
        {
            // CPU/Rust engine: selection remembered, applied when engine changes.
            lblStatus.Text = $"GPU: {name} ({RenderEngineInfo.DisplayName(_engine)} engine, will apply on engine change)";
        }
    }

    private void EngineRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton r || !r.Checked) return;
        _engine = r == radioCuda ? RenderEngine.Cuda
            : r == radioDx ? RenderEngine.DirectX
            : r == radioRust ? RenderEngine.Rust
            : RenderEngine.Cpu;

        // Rust DLL missing: fall back to CPU immediately.
        if (_engine == RenderEngine.Rust && !RustMandelbrot.IsAvailable)
        {
            _engine = RenderEngine.Cpu;
            radioCpu.Checked = true;
            ApplyEngineVisibility();
            lblStatus.Text = "Rust not available: " + RustMandelbrot.LastError;
            return;
        }

        // GPU engine not yet initialized: initialize now on the selected card.
        if (_engine == RenderEngine.Cuda && !GpuMandelbrot.IsReady)
        {
            Cursor = Cursors.WaitCursor;
            bool ok = GpuMandelbrot.TryInitialize(_gpuSelection);
            Cursor = Cursors.Default;
            if (!ok)
            {
                _engine = RenderEngine.Cpu;
                radioCpu.Checked = true;
                ApplyEngineVisibility();
                lblStatus.Text = "CUDA not available: " + GpuMandelbrot.LastError;
                return;
            }
        }
        if (_engine == RenderEngine.DirectX && !DxMandelbrot.IsReady)
        {
            if (!DxMandelbrot.TryInitialize(dxPanel.Handle, Math.Max(1, dxPanel.Width), Math.Max(1, dxPanel.Height), _gpuSelection))
            {
                radioDx.Enabled = false;
                _engine = RenderEngine.Cpu;
                radioCpu.Checked = true;
                ApplyEngineVisibility();
                lblStatus.Text = "DirectX not available: " + DxMandelbrot.LastError;
                return;
            }
        }

        if (!RenderEngineInfo.IsAvailable(_engine)) _engine = RenderEngine.Cpu;
        ApplyEngineVisibility();
    }

    private void ExitItem_Click(object? sender, EventArgs e) => Close();

    private void PictureBox_Resize(object? sender, EventArgs e)
    {
        if (_suspendRender || IsDisposed) return;
        RenderAsync();
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        ((Control?)sender)?.Focus();
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            _dragging = true;
            _dragButton = e.Button;
            _downPos = _lastPos = e.Location;
            _moved = false;
            _dragStartZone = CurrentZone();
        }
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (!_moved && Distance(_downPos, e.Location) < DragThresholdPx) return; // still a possible click

        _moved = true;
        PanBy(_lastPos, e.Location);
        _lastPos = e.Location;

        // Throttle: avoids queuing a render for every pixel of movement.
        int now = Environment.TickCount;
        if (now - _lastPanTick >= PanThrottleMs)
        {
            _lastPanTick = now;
            InvalidateView(preview: true); // bitmap: preview; DirectX: full realtime frame
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != _dragButton) return;
        _dragging = false;

        if (!_moved)
        {
            // Click without drag: in Julia sets c at point, otherwise zoom.
            if (_julia && e.Button == MouseButtons.Left)
            {
                PushHistory();
                (_jcx, _jcy) = PixelToComplex(e.Location);
                InvalidateView();
            }
            else if (e.Button == MouseButtons.Left)
                ZoomAt(e.Location, 0.5); // zoom in 2x
            else
                ZoomAt(e.Location, 2.0); // zoom out 2x
        }
        else
        {
            if (_dragStartZone != null)
            {
                _backZones.Push(_dragStartZone); // pan is committed: can go back
                _forwardZones.Clear();
                UpdateHistoryMenu();
            }
            InvalidateView(); // ensures rendering of the final pan position
        }
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void PictureBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.7 : 1.43; // wheel up = zoom in, centered on mouse
        ZoomAt(e.Location, factor);
    }

    private void PictureBox_MouseEnter(object? sender, EventArgs e)
    {
        // The wheel arrives at the focused control: gets focus on mouse enter.
        if (sender is Control c && !c.Focused) c.Focus();
    }

    private void MandelbrotForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && _realTimeActive) { _realTimeCts?.Cancel(); return; }
        if (e.Control || e.Alt) return; // Ctrl+/Alt+ combinations belong to menus
        if (e.KeyCode == Keys.R) Reset();
        else if (e.KeyCode == Keys.S) SavePng();
        else if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
            numIter.Value = Math.Min(numIter.Maximum, numIter.Value + 50);
        else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
            numIter.Value = Math.Max(numIter.Minimum, numIter.Value - 50);
        else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            // Keyboard pan (not when adjusting iterations, which
            // already uses up/down arrows): 1/10 of the view, Shift = fine step 1/100.
            if (numIter.Focused) return;
            PushHistory();
            double step = (e.Shift ? 0.01 : 0.1) * _scale;
            if (e.KeyCode == Keys.Left) _centerX -= step;
            else if (e.KeyCode == Keys.Right) _centerX += step;
            else if (e.KeyCode == Keys.Up) _centerY -= step;
            else _centerY += step;
            e.Handled = true;
            InvalidateView();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        _dxTimer.Stop();
        _renderCts?.Cancel();
        _realTimeCts?.Cancel();
        DxMandelbrot.Dispose();
        GpuMandelbrot.Dispose();
        base.OnFormClosing(e);
    }
}
