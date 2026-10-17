namespace MandelbrotViewer;

static class Program
{
    // The main entry point for the application.
    [STAThread]
    static void Main(string[] args)
    {
        // Diagnostics and benchmarks without UI: --diag-dx [card] | --diag-gpu |
        // --bench-dx [card] | --bench-cuda [device] | --bench-cpu [float] [--csv file]
        // (see Diagnostics.cs). `--csv file` appends one row per run to the CSV.
        if (args.Length > 0 && args[0] == "--diag-dx")
        {
            Diagnostics.DiagDx(args.Length > 1 ? args[1] : null);
            return;
        }
        if (args.Length > 0 && args[0] == "--bench-dx")
        {
            var (device, csv) = ParseBenchArgs(args);
            Diagnostics.BenchDx(device, runs: 3, budget: BenchmarkStandard.Budget, csvPath: csv);
            return;
        }
        if (args.Length > 0 && args[0] == "--bench-cuda")
        {
            var (device, csv) = ParseBenchArgs(args);
            Diagnostics.BenchCuda(device, runs: 3, budget: BenchmarkStandard.Budget, csvPath: csv);
            return;
        }
        if (args.Length > 0 && args[0] == "--bench-cpu")
        {
            var (mode, csv) = ParseBenchArgs(args);
            bool useFloat = string.Equals(mode, "float", StringComparison.OrdinalIgnoreCase);
            Diagnostics.BenchCpu(runs: 3, budget: BenchmarkStandard.Budget, csvPath: csv, useFloat: useFloat);
            return;
        }
        if (args.Length > 0 && args[0] == "--diag-gpu")
        {
            Diagnostics.DiagGpu();
            return;
        }
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MandelbrotForm());
    }

    // App icon (embedded app.ico) on the given window; optional,
    // never blocking.
    internal static void ApplyIcon(Form form)
    {
        try
        {
            using var s = typeof(Program).Assembly
                .GetManifestResourceStream("MandelbrotViewer.app.ico");
            if (s != null) form.Icon = new Icon(s);
        }
        catch
        {
            // It starts anyway without the icon.
        }
    }

    // Arguments of the --bench-* commands: first positional = card/device
    // (--bench-cpu: precision mode "float", default double), `--csv file`
    // anywhere after the flag. Returns (device, csvPath).
    private static (string? Device, string? Csv) ParseBenchArgs(string[] args)
    {
        string? device = null, csv = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--csv" && i + 1 < args.Length) csv = args[++i];
            else if (device == null) device = args[i];
        }
        return (device, csv);
    }
}
