using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MandelbrotViewer.Views;

// Read-only log/diagnostics dialog (§6.4): monospace 9.75pt, 720x480 min
// 440x320, centered on parent, Copy + Close buttons.
public sealed class LogForm : Window
{
    public LogForm(string content)
    {
        Title = "Log / diagnostics";
        Width = 720; Height = 480; MinWidth = 440; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var box = new TextBox
        {
            Text = content, IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo,Consolas,Monospace"), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        var copy = new Button { Content = "Copy to clipboard", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", Width = 90 };
        copy.Click += async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null) await clip.SetTextAsync(box.Text ?? "");
        };
        close.Click += (_, _) => Close();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8), Children = { copy, close }
        };
        Content = new DockPanel
        {
            Children = { bar, box }
        };
        DockPanel.SetDock(bar, Dock.Bottom);
        box.KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}

// About dialog (§6.5).
public sealed class AboutForm : Window
{
    public AboutForm()
    {
        Title = "About Mandelbrot Set Viewer";
        Width = 560; Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string text =
            $"Mandelbrot Set Viewer v{AppVersion.Display}\n" +
            "Interactive Mandelbrot/Julia explorer. CPU multicore engine (SIMD, 32/64-bit); " +
            "GPU engine via the platform GPU API when available.\n\n" +
            "Left click: zoom in 2x | Right click: zoom out 2x | Wheel: zoom 0.7/1.43 | " +
            "Drag: pan | R: reset | S: save PNG | +/-: iterations | Arrows: pan (Shift fine) | " +
            "Alt+Left/Right: history | Ctrl+D: favorite | Ctrl+J: Julia mode\n\n" +
            "Palettes: Fire, Ice, Thermal, Ocean, Violet, Desert, Forest. " +
            "Auto iterations grow with zoom. AA 1x/2x/4x/8x. GPU Auto = most powerful card. " +
            "File menu: load/save zones (JSON).";
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(12) };
        var ok = new Button { Content = "OK", Width = 90, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 0, 12) };
        ok.Click += (_, _) => Close();
        Content = new StackPanel { Children = { tb, ok } };
    }
}
