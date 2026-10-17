using Avalonia;
using System;

namespace MandelbrotViewer;

sealed class Program
{
    // Entry point. Parses CLI commands (--diag-gpu, --bench-gpu, --bench-cpu;
    // --bench-cpu takes "float" mode, all --bench-* accept an optional
    // device/card positional and "--csv file"). Applies the embedded app icon
    // to any form (§1.3).
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--diag-gpu" || args[0] == "--bench-gpu" || args[0] == "--bench-cpu" || args[0] == "--render-png" || args[0] == "--bench-check"))
            return Diagnostics.RunCli(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
