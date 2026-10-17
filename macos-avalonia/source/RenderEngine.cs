namespace MandelbrotViewer;

public enum RenderEngine
{
    Cpu,
    Gpu
}

public static class RenderEngineInfo
{
    public static bool IsAvailable(RenderEngine engine, Func<bool>? gpuReady = null) =>
        engine == RenderEngine.Cpu || (gpuReady?.Invoke() ?? false);

    public static string DisplayName(RenderEngine engine) =>
        engine == RenderEngine.Cpu ? "CPU" : "GPU";
}
