namespace MandelbrotViewer;

// Enum order = dropdown index = persisted index; add only at end (§2.4).
public enum Palette
{
    Fire = 0,
    Ice = 1,
    Thermal = 2,
    Ocean = 3,
    Violet = 4,
    Desert = 5,
    Forest = 6
}

public static class PaletteColors
{
    public const int LutSize = 4096;
    private const double Gamma = 0.35;

    // 7 palettes × 5 stops at t = 0, .25, .5, .75, 1 (§2.4).
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

    private static readonly object Gate = new();
    private static readonly Dictionary<(Palette, int), int[]> Cache = new();

    public static (byte R, byte G, byte B) GetStop(Palette palette, int stopIndex) =>
        (Stops[(int)palette, stopIndex, 0], Stops[(int)palette, stopIndex, 1], Stops[(int)palette, stopIndex, 2]);

    // Single source of truth, shared by all engines (§2.4). Do not mutate result.
    public static int[] GetLut(Palette palette, int maxIter)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue((palette, maxIter), out var lut))
                return lut;
            int[] table = new int[LutSize];
            for (int i = 0; i < LutSize; i++)
            {
                double raw = (i + 0.5) / LutSize;
                double t = Math.Pow(raw, Gamma);
                double segment = t * 4.0;
                int seg = Math.Min(3, (int)segment);
                double f = segment - seg;
                int r0 = Stops[(int)palette, seg, 0], r1 = Stops[(int)palette, seg + 1, 0];
                int g0 = Stops[(int)palette, seg, 1], g1 = Stops[(int)palette, seg + 1, 1];
                int b0 = Stops[(int)palette, seg, 2], b1 = Stops[(int)palette, seg + 1, 2];
                int r = (int)(r0 + f * (r1 - r0));
                int g = (int)(g0 + f * (g1 - g0));
                int b = (int)(b0 + f * (b1 - b0));
                table[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }
            Cache[(palette, maxIter)] = table;
            return table;
        }
    }

    // Lookup mirroring GPU LutColor (§2.4). Lerp in double, truncate.
    public static int ColorFromLut(int[] lut, double smooth, double invMaxIter)
    {
        double pos = smooth * invMaxIter * LutSize - 0.5;
        if (pos <= 0) return lut[0];
        if (pos >= LutSize - 1) return lut[LutSize - 1];
        int i0 = (int)pos;
        double f = pos - i0;
        int c0 = lut[i0], c1 = lut[i0 + 1];
        int r0 = (c0 >> 16) & 0xFF, g0 = (c0 >> 8) & 0xFF, b0 = c0 & 0xFF;
        int r1 = (c1 >> 16) & 0xFF, g1 = (c1 >> 8) & 0xFF, b1 = c1 & 0xFF;
        int r = (int)(r0 + f * (r1 - r0));
        int g = (int)(g0 + f * (g1 - g0));
        int b = (int)(b0 + f * (b1 - b0));
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
}
