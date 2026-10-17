using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace MandelbrotViewer.Views;

// Main window (§5): navigation, mouse/keyboard, history, favorites, settings,
// engine switching, back-to-set animation, diagnostic text builder.
public partial class MainWindow : Window
{
    // ---- view state (§2.2, §5) ----
    private double _cx = Mandelbrot.InitialCenterX;
    private double _cy = Mandelbrot.InitialCenterY;
    private double _scale = Mandelbrot.InitialScale;
    private int _maxIter = Mandelbrot.DefaultIter;
    private bool _auto;
    private int _palIdx;
    private int _aaIdx;
    private bool _useDouble = true;
    private bool _julia;
    private double _jcx = Mandelbrot.DefaultJcx;
    private double _jcy = Mandelbrot.DefaultJcy;
    private RenderEngine _engine = RenderEngine.Cpu;
    private string _gpuSel = "";
    private readonly GpuEngine _gpu = new();

    private readonly List<ViewZone> _back = new();
    private readonly Stack<ViewZone> _forward = new();

    // ---- controls ----
    private readonly Button _btnHome = new() { Content = "↩ Home", Width = 70 };
    private readonly Button _btnReset = new() { Content = "Reset", Width = 60 };
    private readonly Button _btnBench = new() { Content = "Benchmark", Width = 100 };
    private readonly NumericUpDown _numIter = new() { Width = 140, MinWidth = 140, Minimum = 50, Maximum = 50000, Increment = 50, Value = 256 };
    private readonly CheckBox _chkAuto = new() { Content = "Auto" };
    private readonly ComboBox _cmbPal = new() { Width = 110 };
    private readonly ComboBox _cmbAa = new() { Width = 80, MinWidth = 80 };
    private readonly RadioButton _rCpu = new() { Content = "CPU", GroupName = "eng", IsChecked = true };
    private readonly RadioButton _rGpu = new() { Content = "GPU", GroupName = "eng", IsEnabled = false };
    private readonly RadioButton _r32 = new() { Content = "32", GroupName = "prec" };
    private readonly RadioButton _r64 = new() { Content = "64", GroupName = "prec", IsChecked = true };
    private readonly TextBlock _lblGpu = new() { Text = "GPU:", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _cmbGpu = new() { Width = 200 };
    private readonly Avalonia.Controls.Image _view = new()
    {
        Stretch = Stretch.Fill,
        Cursor = new Cursor(StandardCursorType.Cross), Focusable = true,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
    };
    private readonly TextBlock _status = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private MenuItem _mBack = new(), _mFwd = new(), _mJulia = new(), _mFav = new(), _mDelFav = new(), _mAnim = new();
    private MenuItem _juliaItem = new();

    private WriteableBitmap? _shown;
    private CancellationTokenSource? _renderCts;
    private int _renderSeq;
    private CancellationTokenSource? _animCts;
    private bool _isDown;
    private Point _downPx;
    private Point _pressPx;
    private ViewZone? _dragStartZone;
    private int _lastPreviewTick;
    private AppSettings _saved = new();
    private readonly DispatcherTimer _gpuTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _gpuDirty;
    private bool _gpuPreview;
    private readonly DispatcherTimer _resizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public MainWindow()
    {
        InitializeComponent();
        ClientSize = new Size(1152, 720);
        Title = $"Mandelbrot Set Viewer v{AppVersion.Display}";
        BuildLayout();
        WireEvents();
        LoadSettings();
        _gpuTimer.Tick += (_, _) =>
        {
            if (_engine == RenderEngine.Gpu && _gpuDirty)
            {
                _gpuDirty = false;
                RequestRender(_gpuPreview);
            }
        };
        _gpuTimer.Start();
        // Resize: preview immediately (stretch during the drag), full quality
        // 300 ms after the last SizeChanged (release).
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); InvalidateView(false); };
        Opened += (_, _) =>
        {
            RequestRender(preview: false);
            // New-install default is CPU until the GPU engine initializes on
            // first show (§5.10). Probe on the UI (main) thread: GLFW requires
            // main-thread init; a temp hidden context costs ~0.1 s.
            try
            {
                if (GpuEngine.AdapterNames().Length > 0)
                    _rGpu.IsEnabled = true;
            }
            catch { }
        };
        Closing += (_, _) => OnClosing();
        ArmFrameDump();
        ArmAnimDebug();
    }

    // Debug hook: MV_ANIM=1 → after 3 s zoom to a deep zone and start the
    // back-to-set animation, dumping frames to /tmp/mv-anim-N.png/.txt.
    private void ArmAnimDebug()
    {
        if (Environment.GetEnvironmentVariable("MV_ANIM") != "1") return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _cx = BenchmarkStandard.CenterX;
                    _cy = BenchmarkStandard.CenterY;
                    _scale = 0.01;
                    _auto = true;
                    _chkAuto.IsChecked = true;
                    OnAnimateOrStop();
                });
                for (int n = 0; n < 8; n++)
                {
                    await Task.Delay(2000);
                    int m = n;
                    await Dispatcher.UIThread.InvokeAsync(
                        () => DumpFrameTo($"/tmp/mv-anim-{m}.png", $"/tmp/mv-anim-{m}.txt"));
                }
            }
            catch { }
        });
    }

    // Diagnostic backdoor: launch with MV_DUMP=1 to write the currently
    // displayed frame to /tmp/mv-frame.png (+ engine/status to /tmp/mv-frame.txt)
    // ~12 s after startup.
    private void ArmFrameDump()
    {
        if (Environment.GetEnvironmentVariable("MV_DUMP") != "1") return;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        t.Tick += (_, _) => { t.Stop(); DumpFrame(); };
        t.Start();
    }

    private void DumpFrame()
    {
        try
        {
            Dispatcher.UIThread.Post(() => DumpFrameTo("/tmp/mv-frame.png", "/tmp/mv-frame.txt"));
        }
        catch { }
    }

    private void DumpFrameTo(string pngPath, string txtPath)
    {
        try
        {
            File.WriteAllText(txtPath,
                $"engine={_engine} useDouble={_useDouble} gpuReady={_gpu.IsReady} " +
                $"gpuErr={_gpu.LastError} status={_status.Text} shown={(_shown is not null ? $"{_shown.PixelSize.Width}x{_shown.PixelSize.Height}" : "null")} " +
                $"bounds={_view.Bounds.Width:0}x{_view.Bounds.Height:0} seq={_renderSeq} " +
                $"gpuRadio={_rGpu.IsEnabled} client={ClientSize.Width:0}x{ClientSize.Height:0}");
            if (_shown is not null)
            {
                int w = _shown.PixelSize.Width, h = _shown.PixelSize.Height;
                int[] px = new int[w * h];
                using (var fb = _shown.Lock())
                    unsafe { new Span<int>((void*)fb.Address, px.Length).CopyTo(px); }
                PngWriter.WriteArgb(pngPath, px, w, h);
            }
        }
        catch (Exception ex) { try { File.WriteAllText(txtPath, "dump failed: " + ex.Message); } catch { } }
    }

    // ---- layout (§5.2) ----
    private void BuildLayout()
    {
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);

        var row0 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4, 8, 2) };
        row0.Children.Add(_btnHome);
        row0.Children.Add(_btnReset);
        row0.Children.Add(_btnBench);
        row0.Children.Add(new TextBlock { Text = "Iterations:", VerticalAlignment = VerticalAlignment.Center });
        row0.Children.Add(_numIter);
        row0.Children.Add(_chkAuto);
        row0.Children.Add(new TextBlock { Text = "Palette:", VerticalAlignment = VerticalAlignment.Center });
        _cmbPal.ItemsSource = new[] { "Fire", "Ice", "Thermal", "Ocean", "Violet", "Desert", "Forest" };
        _cmbPal.SelectedIndex = 0;
        row0.Children.Add(_cmbPal);
        row0.Children.Add(new TextBlock { Text = "AA:", VerticalAlignment = VerticalAlignment.Center });
        _cmbAa.ItemsSource = new[] { "1x", "2x", "4x", "8x" };
        _cmbAa.SelectedIndex = 0;
        row0.Children.Add(_cmbAa);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 2, 8, 4) };
        row1.Children.Add(new TextBlock { Text = "Engine:", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_rCpu);
        row1.Children.Add(_rGpu);
        row1.Children.Add(new TextBlock { Text = "Precision:", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_r32);
        row1.Children.Add(_r64);
        row1.Children.Add(_lblGpu);
        _cmbGpu.ItemsSource = new[] { "Auto" };
        _cmbGpu.SelectedIndex = 0;
        row1.Children.Add(_cmbGpu);
        var toolbar = new StackPanel { Children = { row0, row1 } };
        DockPanel.SetDock(toolbar, Dock.Top);

        var hint = new TextBlock
        {
            Text = "Left/Right click: zoom | Wheel: zoom | Drag/arrows: pan | R: reset | S: save",
            Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(8, 0, 0, 0)
        };
        var statusBar = new DockPanel { LastChildFill = true, Margin = new Thickness(4) };
        statusBar.Children.Add(hint);
        DockPanel.SetDock(hint, Dock.Right);
        statusBar.Children.Add(_status);
        DockPanel.SetDock(statusBar, Dock.Bottom);

        RenderOptions.SetBitmapInterpolationMode(_view, BitmapInterpolationMode.HighQuality);
        // The Image arranges at 0x0 while Source is null (Avalonia ArrangeOverride),
        // which would deadlock rendering (no size → no render → no Source):
        // assign a 1x1 placeholder so layout sizes the view immediately.
        _view.Source = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888);
        // NOTE: a Border wrapping the Image leaves the Image at 0x0 (Decorator
        // arranges the sourceless Image at desired size); a Grid stretches it.
        var viewHost = new Grid { Background = Brushes.Black, Children = { _view } };
        Content = new DockPanel { LastChildFill = true, Children = { menu, toolbar, statusBar, viewHost } };
        ApplyEngineVisibility();
        ApplyTooltips();
    }

    private void ApplyTooltips()
    {
        ToolTip.SetTip(_btnHome, "Back to set with RealTime animation (Ctrl+Shift+R)");
        ToolTip.SetTip(_btnReset, "Restore the initial set view (R key)");
        ToolTip.SetTip(_btnBench, "Open the standard benchmark (Ctrl+B)");
        ToolTip.SetTip(_numIter, "Maximum iterations per pixel (+/- keys)");
        ToolTip.SetTip(_chkAuto, "Automatic iterations: grow with zoom level");
        ToolTip.SetTip(_cmbPal, "Fractal color palette (Fire, Ice, Thermal, Ocean, Violet, Desert, Forest)");
        ToolTip.SetTip(_cmbAa, "Antialiasing: 1x disabled, 2x/4x/8x average of nearby pixels");
        ToolTip.SetTip(_cmbGpu, "Video card to use (Auto = the most powerful)");
        ToolTip.SetTip(_r32, "CPU/GPU precision 32-bit (float): faster, less precise.");
        ToolTip.SetTip(_r64, "CPU/GPU precision 64-bit (double): more precise, slower.");
        ToolTip.SetTip(_rCpu, "CPU multicore engine (always available)");
        ToolTip.SetTip(_rGpu, "GPU engine: real-time GPU rendering (float/double, uses the GPU available on this machine)");
    }

    private Menu BuildMenu()
    {
        MenuItem mi(string h, string gesture, string tip, Action onClick)
        {
            var m = new MenuItem { Header = h };
            if (!string.IsNullOrEmpty(gesture)) m.InputGesture = Dialogs.G(gesture);
            ToolTip.SetTip(m, tip);
            m.Click += (_, _) => onClick();
            return m;
        }
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(mi("Load _zone...", "Ctrl+O", "Reload the view saved in a JSON file (Ctrl+O)", () => _ = OnLoadZone()));
        file.Items.Add(mi("Save _zone...", "Ctrl+S", "Save the current view (center, scale, iterations) to a JSON file (Ctrl+S)", () => _ = OnSaveZone()));
        file.Items.Add(new Separator());
        file.Items.Add(mi("Save _image as...", "Ctrl+Shift+S", "Save the current image as PNG (Ctrl+Shift+S)", () => _ = OnSavePng()));
        file.Items.Add(new Separator());
        file.Items.Add(mi("Bench_mark...", "Ctrl+B", "Standard benchmark: 8s at high iterations (Ctrl+B)", () => _ = OnBenchmark()));
        file.Items.Add(new Separator());
        file.Items.Add(mi("E_xit", "", "", () => Close()));

        var view = new MenuItem { Header = "_View" };
        _mBack = mi("Back", "Alt+Left", "", OnBack);
        _mFwd = mi("Forward", "Alt+Right", "", OnForward);
        view.Items.Add(_mBack);
        view.Items.Add(_mFwd);
        view.Items.Add(new Separator());
        _juliaItem = new MenuItem { Header = "_Julia mode", ToggleType = MenuItemToggleType.CheckBox };
        _juliaItem.InputGesture = Dialogs.G("Ctrl+J");
        ToolTip.SetTip(_juliaItem, "");
        _juliaItem.Click += (_, _) => OnJuliaToggle();
        view.Items.Add(_juliaItem);
        view.Items.Add(new Separator());
        view.Items.Add(mi("Save favorite zone…", "Ctrl+D", "", () => _ = OnSaveFavorite()));
        _mFav = new MenuItem { Header = "Favorite zones" };
        _mFav.SubmenuOpened += (_, _) => RebuildFavorites(_mFav, open: true);
        view.Items.Add(_mFav);
        _mDelFav = new MenuItem { Header = "Delete favorite" };
        _mDelFav.SubmenuOpened += (_, _) => RebuildFavorites(_mDelFav, open: false);
        view.Items.Add(_mDelFav);

        var gen = new MenuItem { Header = "_Generate" };
        ToolTip.SetTip(gen, "Generate output from the view: PNG screenshot or MP4 zoom video");
        gen.Items.Add(mi("_Screenshot...", "Ctrl+Shift+E", "Screenshot at view resolution with optional AA (Ctrl+Shift+E)", () => _ = OnScreenshot()));
        gen.Items.Add(mi("_Zoom video...", "Ctrl+Shift+V", "MP4 zoom video with optional AA (Ctrl+Shift+V, requires ffmpeg)", () => _ = OnVideo()));
        _mAnim = mi("Back to set (_RealTime)", "Ctrl+Shift+R", "Back to set with RealTime animation (Esc to stop, Ctrl+Shift+R)", () => OnAnimateOrStop());
        gen.Items.Add(_mAnim);

        var help = new MenuItem { Header = "_Help" };
        var about = mi("_About...", "F1", "About MandelC# (F1)", () => _ = new AboutForm().ShowDialog(this));
        help.Items.Add(about);
        help.Items.Add(new Separator());
        help.Items.Add(mi("Show _log / diagnostics...", "", "", () => _ = OnShowLog()));

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(view);
        menu.Items.Add(gen);
        menu.Items.Add(help);
        return menu;
    }

    // ---- events ----
    private void WireEvents()
    {
        _btnHome.Click += (_, _) => OnAnimateOrStop();
        _btnReset.Click += (_, _) => OnReset();
        _btnBench.Click += (_, _) => _ = OnBenchmark();
        _numIter.ValueChanged += (_, _) => { _maxIter = (int)Math.Clamp(_numIter.Value ?? 256, 50, 50000); InvalidateView(false); };
        _chkAuto.IsCheckedChanged += (_, _) => { _auto = _chkAuto.IsChecked == true; InvalidateView(false); };
        _cmbPal.SelectionChanged += (_, _) => { _palIdx = Math.Max(0, _cmbPal.SelectedIndex); InvalidateView(false); };
        _cmbAa.SelectionChanged += (_, _) => { _aaIdx = Math.Clamp(_cmbAa.SelectedIndex, 0, 3); InvalidateView(false); };
        _rCpu.IsCheckedChanged += (_, _) => { if (_rCpu.IsChecked == true) SwitchEngine(RenderEngine.Cpu); };
        _rGpu.IsCheckedChanged += (_, _) => { if (_rGpu.IsChecked == true) SwitchEngine(RenderEngine.Gpu); };
        _r32.IsCheckedChanged += (_, _) => { if (_r32.IsChecked == true) { _useDouble = false; InvalidateView(false); } };
        _r64.IsCheckedChanged += (_, _) => { if (_r64.IsChecked == true) { _useDouble = true; InvalidateView(false); } };
        _cmbGpu.SelectionChanged += (_, _) =>
        {
            _gpuSel = _cmbGpu.SelectedIndex <= 0 ? "" : (string?)_cmbGpu.SelectedItem ?? "";
            if (_engine == RenderEngine.Gpu) SwitchEngine(RenderEngine.Gpu);
        };

        _view.PointerEntered += (_, _) => _view.Focus();
        _view.PointerPressed += OnPointerPressed;
        _view.PointerMoved += OnPointerMoved;
        _view.PointerReleased += OnPointerReleased;
        _view.PointerWheelChanged += OnWheel;
        _view.SizeChanged += (_, _) => { InvalidateView(preview: true); _resizeDebounce.Stop(); _resizeDebounce.Start(); };
        KeyDown += OnKeyDown;
    }

    // ---- settings (§5.12, §7.1) ----
    private void LoadSettings()
    {
        _saved = AppSettings.Load();
        _maxIter = _saved.MaxIter;
        _numIter.Value = _maxIter;
        _auto = _saved.IterAuto;
        _chkAuto.IsChecked = _auto;
        _palIdx = Math.Clamp(_saved.Palette, 0, 6);
        _cmbPal.SelectedIndex = _palIdx;
        _aaIdx = Math.Clamp(_saved.AaIndex, 0, 3);
        _cmbAa.SelectedIndex = _aaIdx;
        _useDouble = !_saved.Single;
        _r32.IsChecked = !_useDouble;
        _r64.IsChecked = _useDouble;
        _julia = _saved.Julia;
        _juliaItem.IsChecked = _julia;
        _jcx = double.IsFinite(_saved.Jcx) ? _saved.Jcx : Mandelbrot.DefaultJcx;
        _jcy = double.IsFinite(_saved.Jcy) ? _saved.Jcy : Mandelbrot.DefaultJcy;
        _gpuSel = _saved.Gpu ?? "";
        // Window bounds when intersecting a screen (§5.1).
        if (_saved.WinW > 200 && _saved.WinH > 150)
        {
            var r = new PixelRect(_saved.WinX, _saved.WinY, _saved.WinW, _saved.WinH);
            if (Screens.All.Any(s => s.Bounds.Intersects(r)))
            {
                Position = new PixelPoint(_saved.WinX, _saved.WinY);
                ClientSize = new Size(_saved.WinW, _saved.WinH);
            }
            if (_saved.Maximized) WindowState = WindowState.Maximized;
        }
        // Saved engine applied only if available (§7.1); GPU init fails → CPU.
        if (_saved.Engine == "Gpu" && TryInitGpu())
        {
            _engine = RenderEngine.Gpu;
            _rGpu.IsEnabled = true;
            _rGpu.IsChecked = true;
            _useDouble = false; // float-only GPU backend
            _r32.IsChecked = true;
        }
        else
        {
            _engine = RenderEngine.Cpu;
            _rCpu.IsChecked = true;
        }
        ApplyEngineVisibility();
        UpdateHistoryMenus();
    }

    private void OnClosing()
    {
        try
        {
            _saved.IterAuto = _auto;
            _saved.MaxIter = _maxIter;
            _saved.Palette = _palIdx;
            _saved.AaIndex = _aaIdx;
            _saved.Engine = _engine.ToString();
            _saved.Gpu = _gpuSel;
            _saved.Single = !_useDouble;
            _saved.Julia = _julia;
            _saved.Jcx = _jcx; _saved.Jcy = _jcy;
            _saved.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _saved.WinX = Position.X; _saved.WinY = Position.Y;
                _saved.WinW = (int)ClientSize.Width; _saved.WinH = (int)ClientSize.Height;
            }
            _saved.Save();
        }
        catch { }
        _renderCts?.Cancel();
        _animCts?.Cancel();
        _gpu.Dispose();
    }

    // ---- engine switching (§5.10) ----
    private bool TryInitGpu()
    {
        try
        {
            using var cur = new Cursor(StandardCursorType.Wait);
            Cursor = cur;
            bool ok = _gpu.TryInitialize(string.IsNullOrEmpty(_gpuSel) ? null : _gpuSel);
            Cursor = null;
            return ok;
        }
        catch { return false; }
    }

    private void SwitchEngine(RenderEngine eng)
    {
        if (eng == RenderEngine.Gpu)
        {
            if (!TryInitGpu())
            {
                _engine = RenderEngine.Cpu;
                _rGpu.IsEnabled = false;
                _rGpu.IsChecked = false;
                _rCpu.IsChecked = true;
                _status.Text = $"{_gpu.LastError} — switching to CPU";
                AppLog.Write($"GPU init failed: {_gpu.LastError}");
                ApplyEngineVisibility();
                InvalidateView(false);
                return;
            }
            _engine = RenderEngine.Gpu;
            _rGpu.IsEnabled = true;
            // Float-only GPU on this platform (§6.2): force 32-bit.
            if (_useDouble)
            {
                _useDouble = false;
                _r32.IsChecked = true;
                _status.Text = $"GPU on '{_gpu.DeviceShortName}' (32-bit only)";
            }
            else
                _status.Text = $"GPU on '{_gpu.DeviceShortName}'";
        }
        else
        {
            _engine = RenderEngine.Cpu;
        }
        ApplyEngineVisibility();
        InvalidateView(false);
    }

    private void ApplyEngineVisibility()
    {
        bool gpu = _engine == RenderEngine.Gpu;
        _lblGpu.IsVisible = gpu;
        _cmbGpu.IsVisible = gpu;
        _r64.IsEnabled = !gpu; // GPU backend is 32-bit only on this platform
        ToolTip.SetTip(_r64, gpu
            ? "64-bit (double): unavailable with the GPU engine on this platform."
            : "CPU/GPU precision 64-bit (double): more precise, slower.");
        if (gpu && _cmbGpu.ItemCount <= 1)
        {
            var names = new List<string> { "Auto" };
            names.AddRange(GpuEngine.AdapterNames().Distinct().OrderBy(n => n));
            _cmbGpu.ItemsSource = names;
            _cmbGpu.SelectedIndex = string.IsNullOrEmpty(_gpuSel) ? 0 : Math.Max(0, names.IndexOf(_gpuSel));
        }
    }

    // ---- render pipeline ----
    private int EffIter() => _auto ? Mandelbrot.AutoIterForScale(_scale) : _maxIter;
    private int AaFactor() => 1 << _aaIdx;

    // All interactions invalidate; CPU renders immediately, GPU sets the dirty
    // flag consumed by the 16 ms timer (§4.7: renders only when dirty).
    private void InvalidateView(bool preview)
    {
        if (_engine == RenderEngine.Gpu)
        {
            _gpuPreview = preview;
            _gpuDirty = true;
        }
        else
            RequestRender(preview);
    }

    private void RequestRender(bool preview)
    {
        double vw = Math.Max(1, _view.Bounds.Width);
        double vh = Math.Max(1, _view.Bounds.Height);
        if (vw < 2 || vh < 2) return;
        int seq = ++_renderSeq;
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        double cx = _cx, cy = _cy, scale = _scale;
        int maxIter = EffIter();
        var pal = (Palette)_palIdx;
        int k = preview ? 1 : AaFactor();
        int w = preview ? Math.Max(1, (int)vw / 2) : (int)vw;
        int h = preview ? Math.Max(1, (int)vh / 2) : (int)vh;
        bool julia = _julia;
        double jcx = _jcx, jcy = _jcy;
        bool useDouble = _useDouble;
        bool useGpu = _engine == RenderEngine.Gpu;

        Dispatcher.UIThread.Post(() =>
        {
            Cursor = new Cursor(StandardCursorType.AppStarting);
            _status.Text = $"Computing {(preview ? "preview " : "")}{w}x{h}, iter={maxIter}...";
        });
        _ = Task.Run(() =>
        {
            try
            {
                int[] buf;
                if (useGpu)
                {
                    var (px, _) = _gpu.Render(w, h, cx, cy, scale, maxIter, pal, k,
                        cts.Token, julia, jcx, jcy, useDouble: false);
                    buf = px;
                }
                else
                {
                    buf = new int[w * h];
                    Mandelbrot.RenderInto(buf, w, h, cx, cy, scale, maxIter, pal, k,
                        cts.Token, 0, 0, w, h, jcx, jcy, julia, useDouble);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (seq != _renderSeq) return; // superseded: no cursor/image touch (§5.10)
                    var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888);
                    using (var fb = bmp.Lock())
                        unsafe { new Span<int>(buf).CopyTo(new Span<int>((void*)fb.Address, buf.Length)); }
                    _shown = bmp;
                    _view.Source = bmp;
                    if (useGpu) _gpu.NotePresented(buf, w, h); // Capture() source (§4.1)
                    _status.Text = StatusText(cx, cy, scale, maxIter, pal, k, preview, julia, jcx, jcy);
                    Cursor = null;
                    _view.Cursor = new Cursor(StandardCursorType.Cross);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (seq != _renderSeq) return;
                    if (useGpu) FallbackToCpu(ex.Message);
                    else
                    {
                        _status.Text = ex.Message;
                        Cursor = null;
                        AppLog.Write($"Render: {ex.Message}");
                    }
                });
            }
        });
    }

    // Runtime render failure → dispose engine, CPU fallback, readable status (§4.9).
    private void FallbackToCpu(string reason)
    {
        try { _gpu.Dispose(); } catch { }
        _engine = RenderEngine.Cpu;
        _rGpu.IsEnabled = false;
        _rGpu.IsChecked = false;
        _rCpu.IsChecked = true;
        _status.Text = $"{reason} — falling back to CPU";
        AppLog.Write($"GPU render failed: {reason}");
        ApplyEngineVisibility();
        InvalidateView(false);
    }

    private string StatusText(double cx, double cy, double scale, int maxIter, Palette pal, int k,
        bool preview, bool julia, double jcx, double jcy)
    {
        string eng = _engine == RenderEngine.Cpu
            ? $"CPU-{(_useDouble ? "double" : "float")}"
            : $"GPU-{(_useDouble ? "double" : "float")} {_gpu.DeviceShortName}";
        string s = $"Center {cx:+0.000000;-0.000000} {cy:+0.000000;-0.000000}i | width {scale:E2} | " +
            $"iter {maxIter}{(_auto ? " (auto)" : "")} | {pal} AA{k}x | engine {eng}";
        if (julia) s += $" | Julia c={jcx} {jcy}i";
        if (preview) s += " (preview)";
        if (_engine == RenderEngine.Gpu && scale < 1e-3) s += " [beyond float!]";
        return s;
    }

    // ---- mouse (§5.5) ----
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetPosition(_view);
        if (e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed ||
            e.GetCurrentPoint(_view).Properties.IsRightButtonPressed)
        {
            _isDown = true;
            _downPx = pt;
            _pressPx = pt;
            _dragStartZone = CurrentZone();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDown) return;
        var pt = e.GetPosition(_view);
        double dist = Math.Sqrt(Math.Pow(pt.X - _downPx.X, 2) + Math.Pow(pt.Y - _downPx.Y, 2));
        if (dist < 5) return; // drag vs click threshold (§5.5)
        var (ax, ay) = ComplexFromPixel(_downPx.X, _downPx.Y);
        var (bx, by) = ComplexFromPixel(pt.X, pt.Y);
        double vw = Math.Max(1, _view.Bounds.Width);
        _cx += (ax - bx);
        _cy += (ay - by);
        _downPx = pt;
        _ = ax; _ = vw;
        if (Environment.TickCount - _lastPreviewTick >= 80) // throttle 80 ms
        {
            _lastPreviewTick = Environment.TickCount;
            InvalidateView(preview: true);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDown) return;
        _isDown = false;
        var pt = e.GetPosition(_view);
        // Click vs drag: distanza totale dal press (§5.5 soglia 5px).
        double total = Math.Sqrt(Math.Pow(pt.X - _pressPx.X, 2) + Math.Pow(pt.Y - _pressPx.Y, 2));
        if (_dragStartZone is not null && total < 5 && e.InitialPressMouseButton is MouseButton btn)
        {
            var (px, py) = ComplexFromPixel(pt.X, pt.Y);
            if (_julia && btn == MouseButton.Left)
            {
                PushHistory();
                _jcx = px; _jcy = py; // Julia c-set (§5.5)
            }
            else if (btn == MouseButton.Left)
            {
                PushHistory();
                var (cx, cy, sc) = Mandelbrot.Zoom(_cx, _cy, _scale, px, py, 0.5);
                _cx = cx; _cy = cy; _scale = sc;
            }
            else if (btn == MouseButton.Right)
            {
                PushHistory();
                var (cx, cy, sc) = Mandelbrot.Zoom(_cx, _cy, _scale, px, py, 2.0);
                _cx = cx; _cy = cy; _scale = sc;
            }
            InvalidateView(false);
        }
        else if (_dragStartZone is not null)
        {
            // committed pan (§5.8): dragStartZone → back, forward cleared
            _back.Add(_dragStartZone);
            TrimHistory();
            _forward.Clear();
            UpdateHistoryMenus();
            InvalidateView(false);
        }
        _dragStartZone = null;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var pt = e.GetPosition(_view);
        var (px, py) = ComplexFromPixel(pt.X, pt.Y);
        double f = e.Delta.Y > 0 ? 0.7 : 1.43;
        PushHistory();
        var (cx, cy, sc) = Mandelbrot.Zoom(_cx, _cy, _scale, px, py, f);
        _cx = cx; _cy = cy; _scale = sc;
        InvalidateView(false);
    }

    private (double Px, double Py) ComplexFromPixel(double xp, double yp)
    {
        double w = Math.Max(1, _view.Bounds.Width);
        double h = Math.Max(1, _view.Bounds.Height);
        double px = _cx + (xp - w / 2) * (_scale / w);
        double py = _cy - (_scale * h / w) / 2 + yp * (_scale / w);
        return (px, py);
    }

    // ---- keyboard (§5.6) ----
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (ctrl || alt)
        {
            // menus own Ctrl/Alt combos (§5.6)
            if (alt && e.Key == Key.Left) { OnBack(); e.Handled = true; }
            else if (alt && e.Key == Key.Right) { OnForward(); e.Handled = true; }
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                if (_animCts is not null) { _animCts.Cancel(); e.Handled = true; }
                break;
            case Key.R:
                OnReset(); e.Handled = true;
                break;
            case Key.S:
                _ = OnSavePng(); e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                _maxIter = Math.Clamp(_maxIter + 50, 50, 50000);
                _numIter.Value = _maxIter;
                InvalidateView(false);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                _maxIter = Math.Clamp(_maxIter - 50, 50, 50000);
                _numIter.Value = _maxIter;
                InvalidateView(false);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                if (_numIter.IsFocused) return; // arrows adjust numeric there (§5.6)
                double step = _scale * ((e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1.0 / 100 : 1.0 / 10));
                PushHistory();
                if (e.Key == Key.Left) _cx -= step;
                else if (e.Key == Key.Right) _cx += step;
                else if (e.Key == Key.Up) _cy -= step;
                else _cy += step;
                InvalidateView(false);
                e.Handled = true;
                break;
        }
    }

    // ---- history (§5.8) ----
    private ViewZone CurrentZone() => new(_cx, _cy, _scale, EffIter(), _julia, _jcx, _jcy);

    private void PushHistory()
    {
        _back.Add(CurrentZone());
        TrimHistory();
        _forward.Clear();
        UpdateHistoryMenus();
    }

    private void TrimHistory()
    {
        while (_back.Count > 200)
            _back.RemoveAt(0); // drop oldest, preserve order
    }

    private void UpdateHistoryMenus()
    {
        _mBack.IsEnabled = _back.Count > 0;
        _mFwd.IsEnabled = _forward.Count > 0;
    }

    private void ApplyZone(ViewZone z)
    {
        _cx = z.CenterX; _cy = z.CenterY; _scale = z.Scale;
        _maxIter = Math.Clamp(z.MaxIter, 50, 50000);
        _numIter.Value = _maxIter;
        _auto = false;
        _chkAuto.IsChecked = false;
        _julia = z.Julia;
        _juliaItem.IsChecked = _julia;
        _jcx = z.Jcx; _jcy = z.Jcy;
        InvalidateView(false);
    }

    private void OnBack()
    {
        if (_back.Count == 0) return;
        _forward.Push(CurrentZone());
        ApplyZone(_back[^1]);
        _back.RemoveAt(_back.Count - 1);
        UpdateHistoryMenus();
    }

    private void OnForward()
    {
        if (_forward.Count == 0) return;
        _back.Add(CurrentZone());
        TrimHistory();
        ApplyZone(_forward.Pop());
        UpdateHistoryMenus();
    }

    private void OnReset()
    {
        PushHistory();
        _cx = Mandelbrot.InitialCenterX;
        _cy = Mandelbrot.InitialCenterY;
        _scale = Mandelbrot.InitialScale;
        if (_julia) { _jcx = Mandelbrot.DefaultJcx; _jcy = Mandelbrot.DefaultJcy; }
        InvalidateView(false);
    }

    private void OnJuliaToggle()
    {
        PushHistory();
        _julia = _juliaItem.IsChecked == true;
        InvalidateView(false);
    }

    // ---- PNG (§5.7) ----
    private async Task OnSavePng()
    {
        if (_shown is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save image",
            SuggestedFileName = "mandelbrot.png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
        });
        if (file is null) return;
        try
        {
            await using var s = await file.OpenWriteAsync();
            _shown.Save(s);
        }
        catch (Exception ex) { await Dialogs.Warn(this, ex.Message); }
    }

    // ---- zones & favorites (§5.9) ----
    private async Task OnSaveZone()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save zone",
            SuggestedFileName = "zone.json",
            FileTypeChoices = new[] { new FilePickerFileType("Mandelbrot Zone") { Patterns = new[] { "*.json" } } }
        });
        if (file is null) return;
        AppSettings.SaveZone(file.Path.LocalPath, CurrentZone());
    }

    private async Task OnLoadZone()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load zone",
            FileTypeFilter = new[] { new FilePickerFileType("Mandelbrot Zone") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0) return;
        try
        {
            var z = AppSettings.LoadZone(files[0].Path.LocalPath);
            if (z is null) return;
            PushHistory();
            ApplyZone(z);
        }
        catch (Exception ex) { await Dialogs.Warn(this, ex.Message); }
    }

    private void RebuildFavorites(MenuItem parent, bool open)
    {
        parent.Items.Clear();
        string dir = AppSettings.ZonesDir();
        string[] files;
        try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>(); }
        catch (Exception ex) { _ = Dialogs.Warn(this, ex.Message); return; }
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        if (files.Length == 0)
        {
            parent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }
        foreach (var f in files)
        {
            var item = new MenuItem { Header = Path.GetFileName(f) };
            string path = f;
            item.Click += (_, _) =>
            {
                if (open) _ = OpenFavorite(path);
                else _ = DeleteFavorite(path);
            };
            parent.Items.Add(item);
        }
    }

    private async Task OpenFavorite(string path)
    {
        try
        {
            var z = AppSettings.LoadZone(path);
            if (z is null) return;
            PushHistory();
            ApplyZone(z);
        }
        catch (Exception ex) { await Dialogs.Warn(this, ex.Message); }
    }

    private async Task DeleteFavorite(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { await Dialogs.Warn(this, ex.Message); }
    }

    private async Task OnSaveFavorite()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save favorite zone",
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(AppSettings.ZonesDir()),
            SuggestedFileName = "favorite.json",
            FileTypeChoices = new[] { new FilePickerFileType("Mandelbrot Zone") { Patterns = new[] { "*.json" } } }
        });
        if (file is null) return;
        // Must stay inside the zone folder (§5.9).
        string zones = Path.GetFullPath(AppSettings.ZonesDir()) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(file.Path.LocalPath);
        if (!full.StartsWith(zones, StringComparison.OrdinalIgnoreCase))
        {
            await Dialogs.Warn(this, "Favorites must be saved inside the zone folder.");
            return;
        }
        try { AppSettings.SaveZone(full, CurrentZone()); }
        catch (Exception ex) { await Dialogs.Warn(this, ex.Message); }
    }

    // ---- generate ----
    private async Task OnBenchmark()
    {
        var dlg = new BenchmarkForm(!_useDouble, _engine == RenderEngine.Gpu ? _gpu : null);
        await dlg.ShowDialog(this);
    }

    private async Task OnScreenshot()
    {
        var dlg = new ExportForm(this, _cx, _cy, _scale, EffIter(), (Palette)_palIdx, AaFactor(),
            _julia, _jcx, _jcy, _useDouble, (int)_view.Bounds.Width, (int)_view.Bounds.Height, screenshotEntry: true,
            _engine == RenderEngine.Gpu ? _gpu : null);
        await dlg.ShowDialog(this);
    }

    private async Task OnVideo()
    {
        if (_scale <= 0 || Mandelbrot.InitialScale / _scale < 2.0)
        {
            await Dialogs.Warn(this, "You are already at the full set: frame a region first...");
            return;
        }
        var dlg = new ZoomVideoForm(this, _cx, _cy, _scale, EffIter(), (Palette)_palIdx, AaFactor(),
            _julia, _jcx, _jcy, _useDouble, (int)_view.Bounds.Width, (int)_view.Bounds.Height,
            _engine == RenderEngine.Gpu ? _gpu : null);
        await dlg.ShowDialog(this);
    }

    private async Task OnShowLog()
    {
        var dlg = new LogForm(Diagnostics.BuildDiagnosticLog(_engine.ToString(), _engine.ToString(), _gpu, _saved));
        await dlg.ShowDialog(this);
    }

    // ---- back to set (§5.11) ----
    private void OnAnimateOrStop()
    {
        if (_animCts is not null) { _animCts.Cancel(); return; }
        _ = RunBackToSet();
    }

    // Frame interpolation (§5.11): logarithmic eased scale, center following
    // the SCALE linearly toward (-0.5, 0) — i.e. a straight line in
    // (center, scale) space from the current view to the full set. Linearity
    // in the eased parameter instead would fling the framed region dozens of
    // screen widths away within the first frames (center moves while the
    // scale is still tiny). Pure for testing.
    public static (double Cx, double Cy, double Scale) BackToSetFrame(
        double sx, double sy, double startScale, int i, int frames)
    {
        double S = Mandelbrot.InitialScale;
        double t = (double)i / (frames - 1);
        double te = 1 - Math.Pow(1 - t, 3);
        double scale = startScale * Math.Pow(S / startScale, te);
        double u = (scale - S) / (startScale - S); // 1 at start → 0 at full set
        double cx = Mandelbrot.InitialCenterX + (sx - Mandelbrot.InitialCenterX) * u;
        double cy = Mandelbrot.InitialCenterY + (sy - Mandelbrot.InitialCenterY) * u;
        return (cx, cy, scale);
    }

    // Renders one animation frame and WAITS for it (every frame is displayed
    // in order; fire-and-forget would starve slow frames: each new render
    // cancels the previous one, so deep-zoom frames would never appear).
    private async Task<bool> RenderFrameAndWaitAsync(double cx, double cy, double scale,
        int maxIter, Palette pal, bool julia, double jcx, double jcy, bool useGpu,
        CancellationToken ct)
    {
        double vw = Math.Max(1, _view.Bounds.Width);
        double vh = Math.Max(1, _view.Bounds.Height);
        if (vw < 2 || vh < 2) return true;
        int w = Math.Max(1, (int)vw / 2), h = Math.Max(1, (int)vh / 2);
        try
        {
            int[] buf;
            if (useGpu)
                buf = (await Task.Run(
                    () => _gpu.Render(w, h, cx, cy, scale, maxIter, pal, 1,
                        ct, julia, jcx, jcy, useDouble: false), ct)).Pixels;
            else
            {
                var b = new int[w * h];
                bool dbl = _useDouble;
                await Task.Run(() => Mandelbrot.RenderInto(b, w, h, cx, cy, scale, maxIter,
                    pal, 1, ct, 0, 0, w, h, jcx, jcy, julia, dbl), ct);
                buf = b;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888);
                using (var fb = bmp.Lock())
                    unsafe { new Span<int>(buf).CopyTo(new Span<int>((void*)fb.Address, buf.Length)); }
                _shown = bmp;
                _view.Source = bmp;
                if (useGpu) _gpu.NotePresented(buf, w, h);
            });
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            if (useGpu) FallbackToCpu(ex.Message);
            else AppLog.Write($"Animation frame: {ex.Message}");
            return false;
        }
    }

    private async Task RunBackToSet()
    {
        if (Mandelbrot.InitialScale / _scale < 2.0)
        {
            await Dialogs.Warn(this, "Already at the full set: frame a zone first.");
            return;
        }
        const int frames = 120;
        double startScale = _scale, sx = _cx, sy = _cy;
        int savedAa = _aaIdx;
        bool useGpu = _engine == RenderEngine.Gpu;
        _renderCts?.Cancel(); // stop any in-flight full render first
        _animCts = new CancellationTokenSource();
        var ct = _animCts.Token;
        _mAnim.Header = "Stop (Esc)";
        Cursor = new Cursor(StandardCursorType.AppStarting);
        try
        {
            for (int i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (cx, cy, sc) = BackToSetFrame(sx, sy, startScale, i, frames);
                _cx = cx; _cy = cy; _scale = sc;
                _aaIdx = 0; // forced 1x during the animation (§5.11)
                _status.Text = $"Back to set {i + 1}/{frames}... (Esc to stop)";
                int maxIter = EffIter();
                var pal = (Palette)_palIdx;
                if (!await RenderFrameAndWaitAsync(cx, cy, sc, maxIter, pal, _julia,
                        _jcx, _jcy, useGpu && _engine == RenderEngine.Gpu, ct))
                    break;
                await Task.Delay(33, ct); // ~30 fps
            }
            _status.Text = "Back to set complete.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Back to set interrupted.";
        }
        finally
        {
            _animCts?.Dispose();
            _animCts = null;
            _mAnim.Header = "Back to set (_RealTime)";
            _aaIdx = savedAa;
            Cursor = null;
            InvalidateView(false);
        }
    }
}
