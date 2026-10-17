using System.IO.Compression;

namespace MandelbrotViewer;

// Minimal PNG writer (8-bit RGBA, non-interlaced): lets the CLI encode renders
// without a UI toolkit (WriteableBitmap needs a platform backend).
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrc();

    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] tb = System.Text.Encoding.ASCII.GetBytes(type);
        WriteU32(s, (uint)data.Length);
        s.Write(tb, 0, 4);
        s.Write(data, 0, data.Length);
        WriteU32(s, Crc(tb, data));
    }

    private static void WriteU32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    // pixels: row-major 32-bit ARGB (alpha ignored, written opaque).
    public static void WriteArgb(string path, int[] pixels, int w, int h)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        byte[] ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        byte[] row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0); // filter None
            for (int x = 0; x < w; x++)
            {
                int c = pixels[y * w + x];
                row[x * 4] = (byte)((c >> 16) & 0xFF);
                row[x * 4 + 1] = (byte)((c >> 8) & 0xFF);
                row[x * 4 + 2] = (byte)(c & 0xFF);
                row[x * 4 + 3] = 0xFF;
            }
            raw.Write(row, 0, row.Length);
        }
        raw.Position = 0;
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            raw.CopyTo(z);
        Chunk(fs, "IDAT", comp.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }
}
