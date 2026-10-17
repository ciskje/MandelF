using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using MandelbrotViewer.Views;

namespace MandelbrotViewer.Tests;

// Headless UI structure tests (§5.2 layout, menus, tooltips).
public class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Layout_ControlsPresent()
    {
        var w = new MainWindow();
        w.Show();
        Assert.StartsWith("Mandelbrot Set Viewer v", w.Title);

        var buttons = w.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Contains(buttons, b => (string?)b.Content == "↩ Home");
        Assert.Contains(buttons, b => (string?)b.Content == "Reset");
        Assert.Contains(buttons, b => (string?)b.Content == "Benchmark");

        var combos = w.GetLogicalDescendants().OfType<ComboBox>().ToList();
        Assert.True(combos.Count >= 3); // palette, AA, GPU

        var radios = w.GetLogicalDescendants().OfType<RadioButton>().ToList();
        Assert.Contains(radios, r => (string?)r.Content == "CPU");
        Assert.Contains(radios, r => (string?)r.Content == "GPU");
        Assert.Contains(radios, r => (string?)r.Content == "32");
        Assert.Contains(radios, r => (string?)r.Content == "64");

        var menus = w.GetLogicalDescendants().OfType<Menu>().ToList();
        Assert.Single(menus);
        var tops = menus[0].GetLogicalChildren().OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        Assert.Contains("_File", tops);
        Assert.Contains("_View", tops);
        Assert.Contains("_Generate", tops);
        Assert.Contains("_Help", tops);

        // tooltips required (§5.2)
        var home = buttons.First(b => (string?)b.Content == "↩ Home");
        Assert.Equal("Back to set with RealTime animation (Ctrl+Shift+R)", ToolTip.GetTip(home));

        // GPU radio disabled until Metal backend (§4.9)
        var gpu = radios.First(r => (string?)r.Content == "GPU");
        Assert.False(gpu.IsEnabled);

        // hint label present
        var labels = w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(labels, t => t.Contains("Drag/arrows: pan"));

        w.Close();
    }

    [AvaloniaFact]
    public void Menus_FileItemsPresent()
    {
        var w = new MainWindow();
        w.Show();
        var items = w.GetLogicalDescendants().OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        Assert.Contains("Load _zone...", items);
        Assert.Contains("Save _zone...", items);
        Assert.Contains("Save _image as...", items);
        Assert.Contains("Bench_mark...", items);
        Assert.Contains("_Julia mode", items);
        Assert.Contains("_Screenshot...", items);
        Assert.Contains("_Zoom video...", items);
        Assert.Contains("Back to set (_RealTime)", items);
        Assert.Contains("_About...", items);
        Assert.Contains("Show _log / diagnostics...", items);
        w.Close();
    }
}

public class ViewSizingRegressionTests
{
    [AvaloniaFact]
    public async Task ViewHasSize()
    {
        var w = new MainWindow();
        w.Show();
        await Task.Delay(500);
        var img = w.GetLogicalDescendants().OfType<Avalonia.Controls.Image>().First();
        var border = w.GetLogicalDescendants().OfType<Avalonia.Controls.Border>().FirstOrDefault();
        System.Console.WriteLine($"IMG bounds={img.Bounds} visible={img.IsVisible} attached={Avalonia.Controls.TopLevel.GetTopLevel(img) != null}");
        // Regression: sourceless Image arranges at 0x0 and would deadlock rendering.
        Assert.True(img.Bounds.Width > 100 && img.Bounds.Height > 100);
        if (border is not null)
            System.Console.WriteLine($"BORDER bounds={border.Bounds}");
        System.Console.WriteLine($"WIN client={w.ClientSize} bounds={w.Bounds}");
        w.Close();
    }
}
