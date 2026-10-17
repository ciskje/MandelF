namespace MandelbrotViewer;

// Standard benchmark parameters shared by GUI and CLI (§6.1).
public static class BenchmarkStandard
{
    public const int W = 960;
    public const int H = 540;
    public const int Aa = 1;
    public const int SamplesPerFrame = W * H * Aa * Aa; // 518400
    public const double CenterX = -0.7499302568795561;
    public const double CenterY = -0.015139113925433963;
    public const double Scale = 1.0453474311811176e-04;
    public const int BudgetSeconds = 8;

    public static int Iterations => Mandelbrot.AutoIterForScale(Scale); // 10915
}

// Progress record + 1 s report interval (§3.3).
public sealed record BenchmarkProgress(int Frames, long TotalIters, double Seconds, double SamplesPerSecond)
{
    public const double ReportIntervalSeconds = 1.0;

    public static string FormatRate(double samplesPerSecond)
    {
        if (samplesPerSecond >= 1e9) return $"{samplesPerSecond / 1e9:0.#} Gpixel/s";
        if (samplesPerSecond >= 1e6) return $"{samplesPerSecond / 1e6:0.#} Mpixel/s";
        if (samplesPerSecond >= 1e3) return $"{samplesPerSecond / 1e3:0.#} kpixel/s";
        return $"{samplesPerSecond:0} pixel/s";
    }
}
