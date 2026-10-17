using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace MandelbrotViewer.Views;

// High-res PNG export dialog (§6.2): presets, custom 320-16384, AA, precision,
// tiled 512x512 render on the active engine, PNG save.
public sealed class ExportForm : Window
{
    private readonly ComboBox _preset = new() { Width = 220 };
    private readonly TextBox _cw = new() { Width = 80, Text = "1152" };
    private readonly TextBox _ch = new() { Width = 80, Text = "720" };
    private readonly ComboBox _aa = new() { Width = 120 };
    private readonly RadioButton _p32 = new() { Content = "32", GroupName = "expPrec" };
    private readonly RadioButton _p64 = new() { Content = "64", GroupName = "expPrec", IsChecked = true };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock _progress = new() { Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button _render;
    private CancellationTokenSource? _cts;

    private readonly double _cx, _cy, _scale;
    private readonly int _maxIter, _viewAa;
    private readonly Palette _palette;
    private readonly bool _julia, _useDouble;
    private readonly double _jcx, _jcy;
    private readonly GpuEngine? _gpu;

    public ExportForm(Window owner, double cx, double cy, double scale, int maxIter,
        Palette palette, int viewAa, bool julia, double jcx, double jcy, bool useDouble,
        int viewW, int viewH, bool screenshotEntry, GpuEngine? gpu = null)
    {
        _gpu = gpu is not null && gpu.IsReady ? gpu : null;
        _cx = cx; _cy = cy; _scale = scale; _maxIter = maxIter; _palette = palette;
        _viewAa = viewAa; _julia = julia; _jcx = jcx; _jcy = jcy; _useDouble = useDouble;
        Title = "Export PNG";
        Width = 480; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var presets = new[]
        {
            "Current view", "Full HD 1920x1080", "2K 2560x1440", "4K 3840x2160",
            "8K 7680x4320", "Double 4K 7680x2160", "Custom…"
        };
        _preset.ItemsSource = presets;
        _preset.SelectedIndex = screenshotEntry ? 0 : 3;
        _preset.SelectionChanged += (_, _) => { ApplyPreset(viewW, viewH); UpdateInfo(); };
        _aa.ItemsSource = new[] { "As view", "1x", "2x", "4x", "8x" };
        _aa.SelectedIndex = 0;
        _aa.SelectionChanged += (_, _) => UpdateInfo();
        _cw.TextChanged += (_, _) => UpdateInfo();
        _ch.TextChanged += (_, _) => UpdateInfo();
        if (!useDouble) { _p32.IsChecked = true; }
        if (_gpu is not null) { _p32.IsChecked = true; _p64.IsEnabled = false; }
        _render = new Button { Content = "Render & save…", Width = 140, Margin = new Thickness(8) };
        _render.Click += (_, _) => OnRender();
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(8) };
        cancel.Click += (_, _) => { _cts?.Cancel(); };
        Content = new StackPanel
        {
            Children =
            {
                Row("Preset:", _preset),
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8,4,8,4),
                    Children = { new TextBlock { Text = "Custom W×H:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) }, _cw, new TextBlock { Text = " x ", VerticalAlignment = VerticalAlignment.Center }, _ch } },
                Row("AA:", _aa),
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8,4,8,4),
                    Children = { new TextBlock { Text = "Precision:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) }, _p32, _p64 } },
                _info, _progress,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _render, cancel } }
            }
        };
        ApplyPreset(viewW, viewH);
        UpdateInfo();
    }

    private static StackPanel Row(string label, Control c) => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4),
        Children = { new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center }, c }
    };

    private void ApplyPreset(int viewW, int viewH)
    {
        int vw = Math.Max(320, viewW), vh = Math.Max(240, viewH);
        (int, int)? fixed_ = _preset.SelectedIndex switch
        {
            0 => (vw, vh),
            1 => (1920, 1080),
            2 => (2560, 1440),
            3 => (3840, 2160),
            4 => (7680, 4320),
            5 => (7680, 2160),
            _ => null
        };
        if (fixed_.HasValue) { _cw.Text = fixed_.Value.Item1.ToString(); _ch.Text = fixed_.Value.Item2.ToString(); }
        _cw.IsEnabled = _ch.IsEnabled = _preset.SelectedIndex == 6;
    }

    private int AaFactor()
    {
        if (_aa.SelectedIndex <= 0) return _viewAa;
        return 1 << (_aa.SelectedIndex - 1);
    }

    private bool TrySize(out int w, out int h)
    {
        w = h = 0;
        return int.TryParse(_cw.Text, out w) && int.TryParse(_ch.Text, out h)
            && w >= 320 && w <= 16384 && h >= 320 && h <= 16384;
    }

    private void UpdateInfo()
    {
        int k = AaFactor();
        if (!TrySize(out int w, out int h))
        {
            _info.Text = "Custom dimensions: integers 320…16384 px.";
            return;
        }
        string mode = _julia ? $"Julia c={_jcx} {_jcy}i" : "Mandelbrot";
        string eng = _gpu is not null ? $"GPU-float {_gpu.DeviceShortName}" : $"CPU-{(_p64.IsChecked == true ? "double" : "float")}";
        _info.Text = $"{eng}, {mode}, {_palette}, iter {_maxIter}: {w}×{h} ({w * h / 1e6:0.#} MPixel), " +
            $"AA{k}x — {w * k * h * k / 1e6:0.#} MPixel of samples.";
    }

    private async void OnRender()
    {
        if (!TrySize(out int w, out int h))
        {
            await Dialogs.Warn(this, "Custom dimensions: integers 320…16384 px.");
            return;
        }
        int k = AaFactor();
        bool useDouble = _p64.IsChecked == true;
        _cts = new CancellationTokenSource();
        _render.IsEnabled = false;
        try
        {
            int[] result = new int[w * h];
            const int tile = 512;
            int tiles = ((w + tile - 1) / tile) * ((h + tile - 1) / tile);
            int done = 0;
            for (int oy = 0; oy < h; oy += tile)
                for (int ox = 0; ox < w; ox += tile)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    int tw = Math.Min(tile, w - ox), th = Math.Min(tile, h - oy);
                    _progress.Text = $"Rendering {w}×{h} AA{k}x… tile {done + 1}/{tiles}";
                    int[] tb = await Task.Run(() =>
                    {
                        if (_gpu is not null)
                            return _gpu.RenderTile(tw, th, _cx, _cy, _scale, _maxIter, _palette, k,
                                _cts.Token, ox, oy, w, h, _jcx, _jcy, _julia, useDouble: false);
                        var buf = new int[tw * th];
                        Mandelbrot.RenderInto(buf, tw, th, _cx, _cy, _scale, _maxIter, _palette, k,
                            _cts.Token, ox, oy, w, h, _jcx, _jcy, _julia, useDouble);
                        return buf;
                    });
                    for (int y = 0; y < th; y++)
                        Array.Copy(tb, y * tw, result, (oy + y) * w + ox, tw);
                    done++;
                }
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888);
            using (var fb = bmp.Lock())
                unsafe { new Span<int>(result).CopyTo(new Span<int>((void*)fb.Address, result.Length)); }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save PNG",
                SuggestedFileName = "mandelbrot.png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
            });
            if (file is not null)
            {
                await using var s = await file.OpenWriteAsync();
                bmp.Save(s);
            }
            Close();
        }
        catch (OperationCanceledException) { _progress.Text = "Cancelled."; }
        catch (Exception ex)
        {
            await Dialogs.Warn(this, ex.Message);
            AppLog.Write($"Export: {ex.Message}");
        }
        finally { _cts?.Dispose(); _cts = null; _render.IsEnabled = true; }
    }
}
