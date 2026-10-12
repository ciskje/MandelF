namespace MandelbrotViewer;

// Fractal rendering engine: `Cpu` (multicore C#), `Rust` (SIMD via
// mandel-ffi.dll, same formulas), `Cuda` (v1.4 ILGPU, v2.22 native nvcc)
// and `DirectX` (v2.0, realtime shader).
public enum RenderEngine
{
    Cpu,
    Rust,
    Cuda,
    DirectX
}

public static class RenderEngineInfo
{
    public static bool IsAvailable(RenderEngine engine) => engine switch
    {
        RenderEngine.Cpu => true,
        RenderEngine.Rust => RustMandelbrot.IsAvailable,
        RenderEngine.Cuda => GpuMandelbrot.IsReady,
        RenderEngine.DirectX => DxMandelbrot.IsReady,
        _ => false,
    };

    public static string DisplayName(RenderEngine engine) => engine switch
    {
        RenderEngine.Rust => "Rust",
        RenderEngine.Cuda => "CUDA",
        RenderEngine.DirectX => "DirectX",
        _ => "CPU",
    };
}
