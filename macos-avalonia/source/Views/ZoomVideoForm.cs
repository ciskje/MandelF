using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace MandelbrotViewer.Views;

// Zoom video dialog (§6.3): current view → full set, ease-out log, auto iter
// per frame, ffmpeg H.264 MP4 or PNG fallback sequence.
public sealed class ZoomVideoForm : Window
{
    private readonly ComboBox _frames = new() { Width = 120 };
    private readonly ComboBox _fps = new() { Width = 120 };
    private readonly ComboBox _aa = new() { Width = 120 };
    private readonly Avalonia.Controls.ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Margin = new Thickness(8) };
    private readonly TextBlock _label = new() { Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button _go;
    private readonly Button _openFolder = new() { Content = "Open folder", Width = 110, Margin = new Thickness(8), IsEnabled = false };
    private CancellationTokenSource? _cts;
    private string? _lastFolder;

    private readonly double _sx, _sy, _startScale;
    private readonly int _maxIter, _viewAa;
    private readonly Palette _palette;
    private readonly bool _julia, _useDouble;
    private readonly double _jcx, _jcy;
    private readonly int _viewW, _viewH;
    private readonly GpuEngine? _gpu;

    public ZoomVideoForm(Window owner, double cx, double cy, double startScale, int maxIter,
        Palette palette, int viewAa, bool julia, double jcx, double jcy, bool useDouble, int viewW, int viewH,
        GpuEngine? gpu = null)
    {
        _sx = cx; _sy = cy; _startScale = startScale; _maxIter = maxIter; _palette = palette;
        _viewAa = viewAa; _julia = julia; _jcx = jcx; _jcy = jcy; _useDouble = useDouble;
        _viewW = Math.Max(320, viewW); _viewH = Math.Max(240, viewH);
        _gpu = gpu is not null && gpu.IsReady ? gpu : null;
        Title = "Zoom video";
        Width = 440; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _frames.ItemsSource = new[] { "60", "120", "240", "480" };
        _frames.SelectedIndex = 1;
        _fps.ItemsSource = new[] { "24", "30", "60" };
        _fps.SelectedIndex = 1;
        _aa.ItemsSource = new[] { "As view", "1x", "2x", "4x", "8x" };
        _aa.SelectedIndex = 0;
        _go = new Button { Content = "Render video…", Width = 140, Margin = new Thickness(8) };
        _go.Click += (_, _) => OnGo();
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(8) };
        cancel.Click += (_, _) => _cts?.Cancel();
        _openFolder.Click += (_, _) => OpenFolder();
        Content = new StackPanel
        {
            Children =
            {
                Row("Frames:", _frames), Row("FPS:", _fps), Row("AA:", _aa),
                _bar, _label,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _go, cancel, _openFolder } }
            }
        };
    }

    private static StackPanel Row(string label, Control c) => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4),
        Children = { new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center }, c }
    };

    private static string? FindFfmpeg()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string cand = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(cand)) return cand;
            }
            catch { }
        }
        return null;
    }

    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(_lastFolder)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{_lastFolder}\"")
            { UseShellExecute = false };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { AppLog.Write($"Open folder: {ex.Message}"); }
    }

    private async void OnGo()
    {
        int n = int.Parse((string)_frames.SelectedItem!);
        int fps = int.Parse((string)_fps.SelectedItem!);
        int k = _aa.SelectedIndex <= 0 ? _viewAa : 1 << (_aa.SelectedIndex - 1);
        _cts = new CancellationTokenSource();
        _go.IsEnabled = false;
        string tmp = Path.Combine(Path.GetTempPath(), $"MandelC#-video-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(tmp);
        try
        {
            for (int i = 0; i < n; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var (cx, cy, scale) = MainWindow.BackToSetFrame(_sx, _sy, _startScale, i, n);
                int iter = Mandelbrot.AutoIterForScale(scale);
                _label.Text = $"Frame {i + 1}/{n}…";
                _bar.Value = 100.0 * i / n;
                int w = _viewW, h = _viewH;
                int[] buf = await Task.Run(() =>
                {
                    if (_gpu is not null)
                        return _gpu.RenderTile(w, h, cx, cy, scale, iter, _palette, k,
                            _cts.Token, 0, 0, w, h, _jcx, _jcy, _julia, useDouble: false);
                    var b = new int[w * h];
                    Mandelbrot.RenderInto(b, w, h, cx, cy, scale, iter, _palette, k,
                        _cts.Token, 0, 0, w, h, _jcx, _jcy, _julia, _useDouble);
                    return b;
                });
                var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888);
                using (var fb = bmp.Lock())
                    unsafe { new Span<int>(buf).CopyTo(new Span<int>((void*)fb.Address, buf.Length)); }
                await using (var s = File.Create(Path.Combine(tmp, $"f{i:0000}.png")))
                    bmp.Save(s);
            }
            string? ffmpeg = FindFfmpeg();
            if (ffmpeg is null)
            {
                _label.Text = $"ffmpeg not found — PNG sequence kept in {tmp}";
                _lastFolder = tmp;
                _openFolder.IsEnabled = true;
                tmp = ""; // keep folder (it IS the result, §6.3)
                return;
            }
            var outFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save MP4",
                SuggestedFileName = "mandelbrot-zoom.mp4",
                FileTypeChoices = new[] { new FilePickerFileType("MP4") { Patterns = new[] { "*.mp4" } } }
            });
            if (outFile is null) { _label.Text = "Cancelled."; return; }
            string args = $"-y -framerate {fps} -i f%04d.png -frames:v {n} -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p -crf 18 \"{outFile.Path.LocalPath}\"";
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
            {
                WorkingDirectory = tmp, RedirectStandardError = true, UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) throw new InvalidOperationException("Could not start ffmpeg.");
            // Drain stderr asynchronously during wait (pipe fills → deadlock, §6.3).
            var errTask = proc.StandardError.ReadToEndAsync();
            await Task.Run(() =>
            {
                while (!proc.WaitForExit(500))
                    _cts.Token.ThrowIfCancellationRequested();
            }, _cts.Token);
            string err = await errTask;
            if (proc.ExitCode != 0)
            {
                var last10 = string.Join("\n", err.Split('\n').TakeLast(10));
                throw new InvalidOperationException($"ffmpeg exit {proc.ExitCode}:\n{last10}");
            }
            _label.Text = "Done.";
            Close();
        }
        catch (OperationCanceledException)
        {
            _label.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _label.Text = ex.Message.Split('\n')[0];
            await Dialogs.Warn(this, ex.Message);
            AppLog.Write($"Video: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose(); _cts = null;
            _go.IsEnabled = true;
            _bar.Value = 0;
            try { if (!string.IsNullOrEmpty(tmp) && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }
}
