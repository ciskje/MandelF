using System.Runtime.InteropServices;
using System.Text;

namespace MandelbrotViewer;

// Minimal CUDA driver API binding over nvcuda.dll (no NuGet wrapper).
// Covers exactly what the Mandelbrot backend needs: init, device query,
// one context, PTX module + kernels, device memory, launches, sync.
// All entry points are raw; Check converts the result code to an exception.
// Param op (string): Input: operation name used in the exception message.
internal static class CudaNative
{
    private const string Dll = "nvcuda.dll";

    // Device attributes queried (CUdevice_attribute values).
    internal const int AttrWarpSize = 10;
    internal const int AttrMultiprocessorCount = 16;
    internal const int AttrMaxThreadsPerMultiprocessor = 39;
    internal const int AttrClockRate = 13;
    internal const int AttrComputeCapabilityMajor = 75;
    internal const int AttrComputeCapabilityMinor = 76;

    [DllImport(Dll)]
    private static extern int cuInit(uint flags);

    [DllImport(Dll)]
    private static extern int cuDriverGetVersion(out int version);

    [DllImport(Dll)]
    private static extern int cuDeviceGetCount(out int count);

    [DllImport(Dll)]
    private static extern int cuDeviceGet(out int device, int ordinal);

    [DllImport(Dll)]
    private static extern int cuDeviceGetName(StringBuilder name, int len, int device);

    [DllImport(Dll)]
    private static extern int cuDeviceTotalMem_v2(out UIntPtr bytes, int device);

    [DllImport(Dll)]
    private static extern int cuDeviceGetAttribute(out int value, int attrib, int device);

    [DllImport(Dll)]
    private static extern int cuCtxCreate_v2(out IntPtr ctx, uint flags, int device);

    [DllImport(Dll)]
    private static extern int cuCtxDestroy_v2(IntPtr ctx);

    [DllImport(Dll)]
    private static extern int cuCtxSetCurrent(IntPtr ctx);

    [DllImport(Dll)]
    private static extern int cuCtxSynchronize();

    [DllImport(Dll)]
    private static extern int cuModuleLoadDataEx(out IntPtr module, IntPtr image,
        uint numOptions, IntPtr options, IntPtr optionValues);

    [DllImport(Dll)]
    private static extern int cuModuleUnload(IntPtr module);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    private static extern int cuModuleGetFunction(out IntPtr func, IntPtr module, string name);

    [DllImport(Dll)]
    private static extern int cuMemAlloc_v2(out ulong dptr, UIntPtr bytesize);

    [DllImport(Dll)]
    private static extern int cuMemFree_v2(ulong dptr);

    [DllImport(Dll)]
    private static extern int cuMemcpyHtoD_v2(ulong dstDevice, IntPtr srcHost, UIntPtr byteCount);

    [DllImport(Dll)]
    private static extern int cuMemcpyDtoH_v2(IntPtr dstHost, ulong srcDevice, UIntPtr byteCount);

    [DllImport(Dll)]
    private static extern int cuLaunchKernel(IntPtr func,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes, IntPtr stream, IntPtr kernelParams, IntPtr extra);

    [DllImport(Dll)]
    private static extern int cuGetErrorString(int error, out IntPtr str);

    [DllImport(Dll)]
    private static extern int cuOccupancyMaxActiveBlocksPerMultiprocessor(
        out int numBlocks, IntPtr func, int blockSize, UIntPtr dynamicSMemSize);

    // Throws InvalidOperationException when the CUDA result is not success.
    // Param rc (int): Input: raw CUresult code. Param op (string): Input: operation label.
    internal static void Check(int rc, string op)
    {
        if (rc == 0)
            return;
        string detail = Describe(rc);
        throw new InvalidOperationException($"CUDA {op} failed ({rc}): {detail}");
    }

    // Human-readable CUDA error text; never throws.
    // Param rc (int): Input: raw CUresult code. Returns (string): Output: driver message or fallback.
    internal static string Describe(int rc)
    {
        try
        {
            if (cuGetErrorString(rc, out IntPtr p) == 0 && p != IntPtr.Zero)
                return Marshal.PtrToStringAnsi(p) ?? $"error {rc}";
        }
        catch
        {
            // Fall through to the numeric fallback.
        }
        return $"error {rc}";
    }

    // Initializes the driver (idempotent). Must run before any other call.
    internal static void Init() => Check(cuInit(0), "cuInit");

    // Driver version as major*1000 + minor*10 (e.g. 12030 = 12.3).
    // Returns (int): Output: driver version, 0 when the query fails.
    internal static int DriverVersion()
    {
        try
        {
            return cuDriverGetVersion(out int v) == 0 ? v : 0;
        }
        catch
        {
            return 0;
        }
    }

    // Number of CUDA-capable devices.
    // Returns (int): Output: device count (0 when none).
    internal static int DeviceCount()
    {
        Check(cuDeviceGetCount(out int n), "cuDeviceGetCount");
        return Math.Max(0, n);
    }

    // Handle of the ordinal-th device.
    // Param ordinal (int): Input: zero-based device index. Returns (int): Output: device handle.
    internal static int GetDevice(int ordinal)
    {
        Check(cuDeviceGet(out int dev, ordinal), "cuDeviceGet");
        return dev;
    }

    // Display name of a device (e.g. "NVIDIA GeForce RTX 5070 Ti").
    // Param device (int): Input: device handle. Returns (string): Output: device name.
    internal static string GetDeviceName(int device)
    {
        var sb = new StringBuilder(256);
        Check(cuDeviceGetName(sb, sb.Capacity, device), "cuDeviceGetName");
        return sb.ToString();
    }

    // Total device memory in bytes.
    // Param device (int): Input: device handle. Returns (ulong): Output: bytes of VRAM.
    internal static ulong GetTotalMem(int device)
    {
        Check(cuDeviceTotalMem_v2(out UIntPtr bytes, device), "cuDeviceTotalMem");
        return bytes.ToUInt64();
    }

    // Integer device attribute (SM count, warp size, clocks, compute capability...).
    // Param device (int): Input: device handle. Param attrib (int): Input: CUdevice_attribute value.
    // Returns (int): Output: attribute value.
    internal static int GetAttribute(int device, int attrib)
    {
        Check(cuDeviceGetAttribute(out int v, attrib, device), "cuDeviceGetAttribute");
        return v;
    }

    // Creates a context on the device and makes it current on this thread.
    // Param device (int): Input: device handle. Returns (IntPtr): Output: context handle.
    internal static IntPtr CreateContext(int device)
    {
        Check(cuCtxCreate_v2(out IntPtr ctx, 0, device), "cuCtxCreate");
        return ctx;
    }

    // Destroys a context. Param ctx (IntPtr): Input: context handle.
    internal static void DestroyContext(IntPtr ctx)
    {
        if (ctx != IntPtr.Zero)
            cuCtxDestroy_v2(ctx);
    }

    // Makes the context current on the calling thread (contexts are used
    // from several worker threads, all serialized by the render gate).
    // Param ctx (IntPtr): Input: context handle.
    internal static void SetCurrent(IntPtr ctx) => Check(cuCtxSetCurrent(ctx), "cuCtxSetCurrent");

    // Blocks until all work on the context completes.
    internal static void Synchronize() => Check(cuCtxSynchronize(), "cuCtxSynchronize");

    // Loads a PTX image (null-terminated bytes) as a module.
    // Param ptxBytes (byte[]): Input: PTX text plus trailing zero byte. Returns (IntPtr): Output: module handle.
    internal static unsafe IntPtr LoadModule(byte[] ptxBytes)
    {
        IntPtr mod;
        fixed (byte* p = ptxBytes)
            Check(cuModuleLoadDataEx(out mod, (IntPtr)p, 0, IntPtr.Zero, IntPtr.Zero), "cuModuleLoadDataEx");
        return mod;
    }

    // Unloads a module. Param module (IntPtr): Input: module handle.
    internal static void UnloadModule(IntPtr module)
    {
        if (module != IntPtr.Zero)
            cuModuleUnload(module);
    }

    // Kernel function handle from a loaded module.
    // Param module (IntPtr): Input: module handle. Param name (string): Input: entry name (e.g. "FloatKernel").
    // Returns (IntPtr): Output: function handle.
    internal static IntPtr GetFunction(IntPtr module, string name)
    {
        Check(cuModuleGetFunction(out IntPtr f, module, name), $"cuModuleGetFunction({name})");
        return f;
    }

    // Allocates device memory. Param bytes (ulong): Input: size in bytes. Returns (ulong): Output: device pointer.
    internal static ulong Alloc(ulong bytes)
    {
        Check(cuMemAlloc_v2(out ulong p, (UIntPtr)bytes), "cuMemAlloc");
        return p;
    }

    // Frees device memory. Param dptr (ulong): Input: device pointer (0 is ignored).
    internal static void Free(ulong dptr)
    {
        if (dptr != 0)
            cuMemFree_v2(dptr);
    }

    // Copies host ints to the device. Param dst (ulong): Input: device pointer. Param src (int[]): Input: host data.
    internal static unsafe void CopyHtoD(ulong dst, int[] src)
    {
        fixed (int* p = src)
            Check(cuMemcpyHtoD_v2(dst, (IntPtr)p, (UIntPtr)(src.Length * 4L)), "cuMemcpyHtoD");
    }

    // Copies device ints to the host. Param dst (int[]): Output: host destination. Param src (ulong): Input: device pointer.
    internal static unsafe void CopyDtoH(int[] dst, ulong src)
    {
        fixed (int* p = dst)
            Check(cuMemcpyDtoH_v2((IntPtr)p, src, (UIntPtr)(dst.Length * 4L)), "cuMemcpyDtoH");
    }

    // Launches a render kernel on a 2D grid: (pixels, view, lut). Thread (x, y)
    // maps directly to output pixel (x, y), so the device needs no index division.
    // Param func (IntPtr): Input: kernel handle. Param gridX/gridY (uint): Input: block counts per axis.
    // Param blockX/blockY (uint): Input: threads per block per axis. Param dPixels (ulong): Input: device output buffer.
    // Param view (GpuViewParams): Input: view parameters passed by value.
    // Param dLut (ulong): Input: device palette table.
    internal static unsafe void LaunchRender(IntPtr func, uint gridX, uint gridY, uint blockX, uint blockY,
        ulong dPixels, in GpuViewParams view, ulong dLut)
    {
        ulong pix = dPixels, lut = dLut;
        GpuViewParams v = view;
        IntPtr* args = stackalloc IntPtr[3];
        args[0] = (IntPtr)(&pix);
        args[1] = (IntPtr)(&v);
        args[2] = (IntPtr)(&lut);
        Check(cuLaunchKernel(func, gridX, gridY, 1, blockX, blockY, 1, 0, IntPtr.Zero, (IntPtr)args, IntPtr.Zero),
            "cuLaunchKernel(render)");
    }

    // Launches a benchmark kernel on a 2D grid: (iters, view). Thread (x, y)
    // maps directly to sample (x, y), so the device needs no index division.
    // Param func (IntPtr): Input: kernel handle. Param gridX/gridY (uint): Input: block counts per axis.
    // Param blockX/blockY (uint): Input: threads per block per axis. Param dIters (ulong): Input: device output buffer.
    // Param view (GpuViewParams): Input: view parameters passed by value.
    internal static unsafe void LaunchBench(IntPtr func, uint gridX, uint gridY, uint blockX, uint blockY,
        ulong dIters, in GpuViewParams view)
    {
        ulong it = dIters;
        GpuViewParams v = view;
        IntPtr* args = stackalloc IntPtr[2];
        args[0] = (IntPtr)(&it);
        args[1] = (IntPtr)(&v);
        Check(cuLaunchKernel(func, gridX, gridY, 1, blockX, blockY, 1, 0, IntPtr.Zero, (IntPtr)args, IntPtr.Zero),
            "cuLaunchKernel(bench)");
    }

    // Resident blocks per SM for the given function and block size (occupancy
    // query used to account waves: residentWarps = blocks * block/32).
    // Param func (IntPtr): Input: kernel handle. Param blockSize (int): Input: threads per block.
    // Returns (int): Output: max active blocks per multiprocessor (0 on query failure).
    internal static int ActiveBlocksPerSm(IntPtr func, int blockSize)
    {
        try
        {
            if (cuOccupancyMaxActiveBlocksPerMultiprocessor(
                    out int n, func, blockSize, UIntPtr.Zero) == 0)
                return Math.Max(0, n);
        }
        catch
        {
            // Fall through to the fallback below.
        }
        return 0;
    }
}
