using System.Buffers.Binary;
using System.IO.Compression;

namespace MandelbrotViewer;

// Minimal PNG writer (8-bit RGBA, non-interlaced): lets the CLI encode renders
// without a UI toolkit (WriteableBitmap needs a platform backend).
public static class PngWriter
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static void Chunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> u4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(u4, (uint)data.Length);
        s.Write(u4);
        s.Write(type);
        s.Write(data);
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(u4, c ^ 0xFFFFFFFFu);
        s.Write(u4);
    }

    // pixels: row-major 32-bit ARGB (alpha ignored, written opaque).
    public static void WriteArgb(string path, int[] pixels, int w, int h)
    {
        using var fs = File.Create(path);
        fs.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], h);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(fs, "IHDR"u8, ihdr);
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] row = new byte[1 + w * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int c = pixels[y * w + x], o = 1 + x * 4;
                    row[o] = (byte)(c >> 16); row[o + 1] = (byte)(c >> 8); row[o + 2] = (byte)c; row[o + 3] = 0xFF;
                }
                z.Write(row);
            }
        }
        Chunk(fs, "IDAT"u8, comp.ToArray());
        Chunk(fs, "IEND"u8, []);
    }
}
