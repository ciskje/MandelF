namespace MandelbrotViewer;

// Standard benchmark parameters, identical for all engines so the results
// are comparable: fixed area at high iterations, 960x540 with AA 1x meant as
// a grid of elementary samples without averaging (960x540 pixels per frame), only
// counting the escape iterations (no coloring nor downsampling).
internal static class BenchmarkStandard
{
    public const int Width = 960;
    public const int Height = 540;
    public const int Aa = 1;                    // grid = Width*AA x Height*AA
    // Benchmark iterations, computed with the auto formula at the test scale.
    public static int MaxIter => Mandelbrot.AutoIterForScale(Scale);
    public const double CenterX = -0.7499302568795561;
    public const double CenterY = -0.015139113925433963;
    public const double Scale = 0.00010453474311811176; // half = 5.226737155905588e-05
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    // Elementary samples per frame (Width*AA x Height*AA).
    public static long PixelsPerFrame => (long)Width * Height * Aa * Aa;

    // The final yardstick: elementary samples per second.
    public static double PixelsPerSecond(int frames, double seconds) =>
        seconds > 0 ? frames * PixelsPerFrame / seconds : 0;
}
