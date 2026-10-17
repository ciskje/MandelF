namespace MandelbrotViewer;

// Benchmark progress: seconds, accumulated iterations and completed frames.
// TotalIters is meaningful only for the CPU benchmark: CUDA and DirectX measure in
// frames (the per-frame work is fixed), so they report TotalIters = 0.
public record struct BenchmarkProgress(double ElapsedSeconds, long TotalIters, int Frames)
{
    // Minimum interval between two UI updates (each Invoke steals time from the test).
    public static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);
}
