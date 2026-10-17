using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MandelbrotViewer.Views;

// Standard benchmark window (§6.1).
public sealed class BenchmarkForm : Window
{
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 0) };
    private readonly Avalonia.Controls.Image _preview = new() { Width = 320, Height = 180, Margin = new Thickness(8), Stretch = Stretch.Fill };
    private readonly TextBlock _progress = new() { Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock _result = new() { FontWeight = FontWeight.Bold, Margin = new Thickness(8, 4, 8, 0) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) };
    private readonly PerfChart _chart = new() { Height = 260, Margin = new Thickness(8) };
    private readonly Button _start;
    private readonly Button _csv;
    private readonly bool _useFloat;
    private readonly string _precisionLabel;
    private readonly GpuEngine? _gpu;
    private CancellationTokenSource? _cts;
    private (int Frames, double Seconds, double Mpixel)? _last;

    public BenchmarkForm(bool useFloat, GpuEngine? gpu = null)
    {
        _useFloat = useFloat;
        _gpu = gpu is not null && gpu.IsReady ? gpu : null;
        _precisionLabel = _gpu is not null ? "32-bit" : (useFloat ? "32-bit" : "64-bit");
        Title = "Standard benchmark";
        Width = 640; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _info.Text = $"{(_gpu is not null ? "GPU " + _gpu.DeviceShortName : "CPU " + Diagnostics.CpuName())} — zone 960x540 1x AA = 960x540 elementary samples " +
            $"without averaging, {BenchmarkStandard.Iterations} max iterations, scale {BenchmarkStandard.Scale:e}, " +
            $"precision {(_gpu is not null ? "GPU" : "CPU")} {_precisionLabel} — minimum duration {BenchmarkStandard.BudgetSeconds} seconds (GPU offscreen, no presentation).";
        _start = new Button { Content = "Start", Width = 100, Margin = new Thickness(8, 8, 4, 8) };
        _csv = new Button { Content = "Export CSV…", Width = 120, Margin = new Thickness(4, 8, 8, 8), IsEnabled = false };
        _start.Click += (_, _) => OnStartOrCancel();
        _csv.Click += (_, _) => OnCsv();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _start, _csv } };
        Content = new StackPanel
        {
            Children = { _info, _preview, _progress, _result, _detail, _chart, buttons }
        };
        Opened += (_, _) => OnStartOrCancel();
    }

    private void OnStartOrCancel()
    {
        if (_cts is not null) { _cts.Cancel(); return; }
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        _start.Content = "Cancel";
        _csv.IsEnabled = false;
        _result.Text = ""; _detail.Text = ""; _progress.Text = "0%";
        try
        {
            // Colored preview first (Fire, AA 1x) (§6.1).
            int pw = 320, ph = 180;
            int[] px;
            if (_gpu is not null)
            {
                px = await Task.Run(() =>
                    _gpu.RenderPreviewToImage(pw, ph, BenchmarkStandard.CenterX, BenchmarkStandard.CenterY,
                        BenchmarkStandard.Scale, BenchmarkStandard.Iterations, Palette.Fire, 1,
                        false, 0, 0, pw, ph, 0, 0, useDouble: false)
                    ?? throw new InvalidOperationException("GPU preview failed."));
            }
            else
            {
                px = await Task.Run(() =>
                {
                    var buf = new int[pw * ph];
                    Mandelbrot.RenderInto(buf, pw, ph, BenchmarkStandard.CenterX, BenchmarkStandard.CenterY,
                        BenchmarkStandard.Scale, BenchmarkStandard.Iterations, Palette.Fire, 1,
                        _cts.Token, 0, 0, pw, ph, 0, 0, false, !_useFloat);
                    return buf;
                });
            }
            var bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96), PixelFormat.Bgra8888);
            using (var fb = bmp.Lock())
                unsafe { new Span<int>(px).CopyTo(new Span<int>((void*)fb.Address, px.Length)); }
            _preview.Source = bmp;

            var progress = new Progress<BenchmarkProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                double pct = Math.Min(100, 100 * p.Seconds / BenchmarkStandard.BudgetSeconds);
                string rate = p.Seconds >= 0.2 && p.SamplesPerSecond > 0 ? $"  |  {BenchmarkProgress.FormatRate(p.SamplesPerSecond)}" : "";
                _progress.Text = $"{pct:0}%{rate}";
            }));
            var (_, secs, frames) = await Task.Run(() =>
            {
                if (_gpu is not null)
                {
                    var (s, f) = _gpu.Benchmark(BenchmarkStandard.W, BenchmarkStandard.H,
                        BenchmarkStandard.Iterations, _cts.Token,
                        sec => ((IProgress<BenchmarkProgress>)progress).Report(
                            new BenchmarkProgress(0, 0, sec, 0)));
                    return (0L, s, f);
                }
                if (_useFloat)
                    return Mandelbrot.BenchmarkCpuFloat(BenchmarkStandard.W, BenchmarkStandard.H,
                        BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                        BenchmarkStandard.Iterations, _cts.Token,
                        p => ((IProgress<BenchmarkProgress>)progress).Report(p));
                return Mandelbrot.BenchmarkCpu(BenchmarkStandard.W, BenchmarkStandard.H,
                    BenchmarkStandard.CenterX, BenchmarkStandard.CenterY, BenchmarkStandard.Scale,
                    BenchmarkStandard.Iterations, _cts.Token,
                    p => ((IProgress<BenchmarkProgress>)progress).Report(p));
            });
            double mpixel = frames * (double)BenchmarkStandard.SamplesPerFrame / Math.Max(secs, 1e-9) / 1e6;
            _result.Text = $"{mpixel:0.#} Mpixel/s";
            string devName = _gpu is not null ? _gpu.DeviceShortName : Diagnostics.CpuName();
            _detail.Text = $"{frames} frame ({frames / Math.Max(secs, 1e-9):0.#} frame/s) 960x540 AA1x in {secs:0.00} s on {devName}";
            _last = (frames, secs, mpixel);
            _chart.SetResult($"Risultato {mpixel:0.#}", mpixel);
            _csv.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            _detail.Text = "Cancelled.";
            AppLog.Write("Benchmark CPU: cancelled");
        }
        catch (Exception ex)
        {
            _detail.Text = ex.Message;
            AppLog.Write($"Benchmark CPU: {ex.Message}");
            _chart.InvalidateVisual();
        }
        finally
        {
            _cts?.Dispose(); _cts = null;
            _start.Content = "Start";
        }
    }

    private async void OnCsv()
    {
        if (_last is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Append benchmark CSV",
            SuggestedFileName = "benchmark.csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;
        BenchmarkCsv.AppendRow(file.Path.LocalPath, BenchmarkCsv.FormatRow(DateTime.UtcNow,
            _gpu is not null ? "GPU" : "CPU",
            _gpu is not null ? _gpu.DeviceShortName : Diagnostics.CpuName(),
            _precisionLabel, 1, _last.Value.Frames, _last.Value.Seconds, _last.Value.Mpixel));
    }

    // Custom bar chart (§6.1): margins L132 R56 T22 B24, sorted desc, max*1.15.
    private sealed class PerfChart : Control
    {
        private readonly List<(string Label, double Value, Color Color)> _refs = new()
        {
            ("GPU 5070 Ti 32-bit", 310.7, Color.FromRgb(226,126,34)),
            ("GPU 4070 SUPER 32-bit", 250.1, Color.FromRgb(226,126,34)),
            ("GPU 5070 Ti 64-bit", 6.7, Color.FromRgb(226,126,34)),
            ("GPU 4070 SUPER 64-bit", 5.3, Color.FromRgb(226,126,34)),
            ("GPU 5070 Ti", 338, Color.FromRgb(226,126,34)),
            ("GPU 4070 SUPER", 262.4, Color.FromRgb(226,126,34)),
            ("GPU AMD Radeon", 5.2, Color.FromRgb(226,126,34)),
            ("CPU 9900X", 9.7, Color.FromRgb(112,128,144)),
            ("CPU 9900X float", 15.3, Color.FromRgb(112,128,144)),
        };
        private (string Label, double Value)? _current;

        public void SetResult(string label, double value)
        {
            _current = (label, value);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            var bars = _refs.Select(r => (r.Label, r.Value, r.Color)).ToList();
            if (_current.HasValue)
                bars.Add((_current.Value.Label, _current.Value.Value, Color.FromRgb(36, 113, 163)));
            bars.Sort((a, b) => b.Value.CompareTo(a.Value));
            double max = bars.Count == 0 ? 1 : bars.Max(b => b.Value) * 1.15;
            const double L = 132, R = 56, T = 22, B = 24;
            double w = Math.Max(0, Bounds.Width - L - R);
            double rowH = bars.Count == 0 ? 0 : Math.Max(8, Math.Min(200.0 / Math.Max(1, bars.Count), (Bounds.Height - T - B) / Math.Max(1, bars.Count)));
            var title = new FormattedText("Confronto prestazioni (MPixel/s)", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 11, Brushes.Black);
            ctx.DrawText(title, new Point(L, 4));
            double y = T;
            foreach (var (label, value, color) in bars)
            {
                double bh = Math.Min(22, rowH * 0.62);
                double bw = max <= 0 ? 0 : w * value / max;
                ctx.FillRectangle(new SolidColorBrush(color), new Rect(L, y + (rowH - bh) / 2, Math.Max(1, bw), bh));
                var lt = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.Black);
                ctx.DrawText(lt, new Point(4, y + (rowH - 12) / 2));
                var vt = new FormattedText($"{value:0.#}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.Black);
                ctx.DrawText(vt, new Point(L + bw + 4, y + (rowH - 12) / 2));
                y += rowH;
            }
            // two gridlines at 1/3 and 2/3 of max
            var pen = new Pen(Brushes.LightGray, 1);
            foreach (double f in new[] { 1.0 / 3, 2.0 / 3 })
            {
                double gx = L + w * f;
                ctx.DrawLine(pen, new Point(gx, T), new Point(gx, y));
            }
        }
    }
}
