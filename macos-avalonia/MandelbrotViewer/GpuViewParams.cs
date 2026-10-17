using System.Runtime.InteropServices;

namespace MandelbrotViewer;

// GpuViewParams: blittable struct, field order + 8-byte packing, exactly 88 bytes (§4.2).
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GpuViewParams
{
    public double CenterX;
    public double CenterY;
    public double PixelSize;
    public double TopY;
    public int W;
    public int H;
    public int MaxIter;
    public int Supersample;
    public int JuliaOn;
    public int Pad;
    public double Jcx;
    public double Jcy;
    public int FullW;
    public int FullH;
    public int OffsetX;
    public int OffsetY;

    public const int ExpectedSize = 88;

    public static void AssertSize()
    {
        int size = Marshal.SizeOf<GpuViewParams>();
        if (size != ExpectedSize)
            throw new InvalidOperationException($"GpuViewParams size {size} != {ExpectedSize}.");
    }
}
