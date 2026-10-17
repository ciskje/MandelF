using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MandelbrotViewer.Views;

public sealed class DelegateCommand : ICommand
{
    private readonly Action _run;
    public DelegateCommand(Action run) => _run = run;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _run();
    public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public static class Dialogs
{
    public static async Task Warn(Window parent, string message, string title = "Mandelbrot Set Viewer")
    {
        var tb = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Avalonia.Thickness(12) };
        var ok = new Button { Content = "OK", Width = 90, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 0, 12) };
        var dlg = new Window
        {
            Title = title, Width = 460, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Children = { tb, ok } }
        };
        ok.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(parent);
    }

    public static KeyGesture G(string renderShortcut)
    {
        // Platform mapping (§1.6): reference bindings use Ctrl; on macOS use Cmd.
        if (OperatingSystem.IsMacOS() && renderShortcut.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
            renderShortcut = "Meta+" + renderShortcut["Ctrl+".Length..];
        return KeyGesture.Parse(renderShortcut);
    }
}
