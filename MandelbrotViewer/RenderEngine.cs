namespace MandelbrotViewer;

// Fractal rendering engine: `Cpu` (multicore C#), `Cuda` (v2.22 native nvcc)
// and `DirectX` (v2.0, realtime shader).
public enum RenderEngine
{
    Cpu,
    Cuda,
    DirectX
}

public static class RenderEngineInfo
{
    public static bool IsAvailable(RenderEngine engine) => engine switch
    {
        RenderEngine.Cpu => true,
        RenderEngine.Cuda => GpuMandelbrot.IsReady,
        RenderEngine.DirectX => DxMandelbrot.IsReady,
        _ => false,
    };

    public static string DisplayName(RenderEngine engine) => engine switch
    {
        RenderEngine.Cuda => "CUDA",
        RenderEngine.DirectX => "DirectX",
        _ => "CPU",
    };
}
