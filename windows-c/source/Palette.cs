using System.Runtime.CompilerServices;

namespace MandelbrotViewer;

// Color palettes available in the viewer (the order is
// the dropdown index and is persisted in settings.json: add only at the end).
public enum Palette
{
    Fire,
    Ice,
    Thermal,
    Ocean,
    Violet,
    Desert,
    Forest
}

    // Palette gradients and iteration-based color interpolation.
    // Single source of stops for the CPU and DirectX paths; the CUDA kernel samples
    // the same table entries uploaded to the device (GpuMandelbrot.EnsureLut),
    // DirectX samples them from a texture in the Graded function of the HLSL shader
    // (DxMandelbrot.cs). The three implementations MUST stay aligned:
    // same entries and same mapping t = (nu/maxIter)^0.35 (gamma, like the
    // Python reference: smooth iteration + tone curve).
internal static class PaletteColors
{
    // 7 palettes × 5 stops at t = 0, .25, .5, .75, 1.
    private static readonly byte[,,] Stops = new byte[7, 5, 3]
    {
        { {0,0,0}, {170,20,0}, {255,120,0}, {255,220,90}, {255,255,225} },       // Fire
        { {0,0,0}, {0,35,110}, {0,130,220}, {130,215,255}, {240,255,255} },       // Ice
        { {0,0,0}, {75,10,105}, {195,25,85}, {250,135,30}, {252,250,180} },       // Thermal
        { {0,0,0}, {0,40,95}, {0,120,175}, {85,200,225}, {235,250,255} },         // Ocean
        { {60,0,80}, {210,0,190}, {120,40,220}, {0,200,230}, {210,255,255} },     // Violet
        { {0,0,0}, {95,50,10}, {185,110,40}, {235,190,110}, {255,245,220} },      // Desert
        { {55,32,12}, {115,78,32}, {95,125,45}, {60,170,70}, {205,235,175} },     // Forest
    };

    internal const int LutSize = 4096;

    private static readonly object LutGate = new();
    private static int[]? _cachedLut;
    private static Palette _cachedPalette = (Palette)(-1);

    // Returns the cached fast color table for the render hot loop.
    internal static int[] GetLut(Palette palette)
    {
        lock (LutGate)
        {
            if (_cachedLut != null && _cachedPalette == palette)
                return _cachedLut;
            int p = (int)palette;
            var lut = new int[LutSize];
            for (int i = 0; i < LutSize; i++)
            {
                double raw = (i + 0.5) / LutSize;
                double segment = Math.Pow(raw, 0.35) * 4.0;
                int seg = segment >= 3.0 ? 3 : (int)segment;
                double f = segment - seg;
                int r = (int)(Stops[p, seg, 0] + f * (Stops[p, seg + 1, 0] - Stops[p, seg, 0]));
                int g = (int)(Stops[p, seg, 1] + f * (Stops[p, seg + 1, 1] - Stops[p, seg, 1]));
                int b = (int)(Stops[p, seg, 2] + f * (Stops[p, seg + 1, 2] - Stops[p, seg, 2]));
                lut[i] = unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
            }
            _cachedLut = lut;
            _cachedPalette = palette;
            return lut;
        }
    }

    // Maps a smoothed iteration value through the fast table with linear
    // interpolation between adjacent entries (no visible banding).
    // Param lut (int[]): Input: table from GetLut (same palette/maxIter as the view).
    // Param smooth (double): Input: smoothed escape value nu in [0, maxIter].
    // Param invMaxIter (double): Input: 1.0/maxIter, precomputed by the caller.
    // Returns (int): Output: packed ARGB color interpolated from the table.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ColorFromLut(int[] lut, double smooth, double invMaxIter)
    {
        double pos = smooth * invMaxIter * lut.Length - 0.5;
        if (pos <= 0.0) return lut[0];
        if (pos >= lut.Length - 1) return lut[lut.Length - 1];
        int i0 = (int)pos;
        double f = pos - i0;
        int c0 = lut[i0], c1 = lut[i0 + 1];
        double r = ((c0 >> 16) & 0xFF) + f * ((((c1 >> 16) & 0xFF) - ((c0 >> 16) & 0xFF)));
        double g = ((c0 >> 8) & 0xFF) + f * ((((c1 >> 8) & 0xFF) - ((c0 >> 8) & 0xFF)));
        double b = (c0 & 0xFF) + f * (((c1 & 0xFF) - (c0 & 0xFF)));
        return unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
    }
}
