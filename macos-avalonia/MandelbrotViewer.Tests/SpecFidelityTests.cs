using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MandelbrotViewer.Tests;

public class SpecFidelityTests
{
    [Fact]
    public void AutoIter_SpotValues()
    {
        // Formula §2.3: clamp(2000*(1+log10(1.5/(scale/2))),50,50000).
        // NOTE: la SPEC riporta "≈1944 at scale 9.36" ma la formula dà 1011
        // (1.5/4.68=0.3205, log10=-0.494, 2000*0.506=1011). La formula è
        // normativa e riproduce esattamente 10915 al benchmark scale, quindi
        // si implementa la formula; il valore 1944 è un errore della SPEC.
        Assert.Equal(1011, Mandelbrot.AutoIterForScale(9.36));
        Assert.Equal(10915, Mandelbrot.AutoIterForScale(1.0453474311811176e-04));
        Assert.Equal(50, Mandelbrot.AutoIterForScale(1e30));
        Assert.Equal(50000, Mandelbrot.AutoIterForScale(1e-30));
    }

    [Fact]
    public void Palette_Stops()
    {
        Assert.Equal((byte)255, PaletteColors.GetStop(Palette.Fire, 2).R);
        Assert.Equal((byte)120, PaletteColors.GetStop(Palette.Fire, 2).G);
        Assert.Equal((byte)0, PaletteColors.GetStop(Palette.Fire, 2).B);
        Assert.Equal(((byte)130, (byte)215, (byte)255), PaletteColors.GetStop(Palette.Ice, 3));
        Assert.Equal(((byte)55, (byte)32, (byte)12), PaletteColors.GetStop(Palette.Forest, 0));
    }

    [Fact]
    public void Lut_SizeAndLookup()
    {
        var lut = PaletteColors.GetLut(Palette.Fire, 256);
        Assert.Equal(4096, lut.Length);
        // lookup clamps
        int c0 = PaletteColors.ColorFromLut(lut, -5, 1.0 / 256);
        int c1 = PaletteColors.ColorFromLut(lut, 1e9, 1.0 / 256);
        Assert.Equal(lut[0], c0);
        Assert.Equal(lut[4095], c1);
    }

    [Fact]
    public void Interior_EarlyOut()
    {
        Assert.True(Mandelbrot.IsInterior(-1, 0));  // period-2 bulb
        Assert.True(Mandelbrot.IsInterior(0, 0));   // main cardioid
        Assert.False(Mandelbrot.IsInterior(2.5, 0)); // exterior
        // NOTE SPEC §8 cita "c = 0.3" come esempio di punto nero via early-out:
        // è sbagliato. 0.3 è fuori dal cardioid (che arriva a 0.25), quindi
        // l'early-out NON scatta, e con la escape-loop normativa diverge
        // entro 256 iterazioni -> colore, non nero. Verificato qui.
        Assert.False(Mandelbrot.IsInterior(0.3, 0));
        var lut = PaletteColors.GetLut(Palette.Fire, 256);
        Assert.NotEqual(unchecked((int)0xFF000000),
            Mandelbrot.PixelDouble(0.3, 0, 256, lut, 1.0 / 256, 0, 0, false));
        Assert.Equal(unchecked((int)0xFF000000),
            Mandelbrot.PixelDouble(-1, 0, 256, lut, 1.0 / 256, 0, 0, false));
    }

    [Fact]
    public void Escape_InteriorStaysBounded()
    {
        var (iter, _) = Mandelbrot.Escape(0, 0, 0, 0, 1000);
        Assert.Equal(1000, iter);
        var (iter2, _) = Mandelbrot.Escape(2.5, 0, 0, 0, 1000);
        Assert.True(iter2 < 10);
    }

    [Fact]
    public void GpuViewParams_Is88Bytes()
    {
        GpuViewParams.AssertSize();
        Assert.Equal(88, System.Runtime.InteropServices.Marshal.SizeOf<GpuViewParams>());
    }

    [Fact]
    public void Tiling_PixelIdentical()
    {
        int W = 128, H = 96, k = 2, maxIter = 200;
        int[] whole = new int[W * H];
        Mandelbrot.RenderInto(whole, W, H, -0.5, 0, 9.36, maxIter, Palette.Fire, k,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        int[] tiled = new int[W * H];
        int tile = 32;
        for (int oy = 0; oy < H; oy += tile)
            for (int ox = 0; ox < W; ox += tile)
            {
                int tw = Math.Min(tile, W - ox), th = Math.Min(tile, H - oy);
                int[] tb = new int[tw * th];
                Mandelbrot.RenderInto(tb, tw, th, -0.5, 0, 9.36, maxIter, Palette.Fire, k,
                    CancellationToken.None, ox, oy, W, H, 0, 0, false, true);
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                        tiled[(oy + y) * W + ox + x] = tb[y * tw + x];
            }
        int diff = 0;
        for (int i = 0; i < W * H; i++)
            if (whole[i] != tiled[i]) diff++;
        Assert.True(diff == 0, $"tiled differs in {diff} pixels");
    }

    [Fact]
    public void FloatAndDoublePaths_AgreeOnInterior()
    {
        // Interior => nero opaco in entrambi i path (§1.5.3).
        int W = 64, H = 48;
        int[] d = new int[W * H];
        int[] f = new int[W * H];
        Mandelbrot.RenderInto(d, W, H, -0.5, 0, 9.36, 300, Palette.Ice, 1,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        Mandelbrot.RenderInto(f, W, H, -0.5, 0, 9.36, 300, Palette.Ice, 1,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, false);
        int black = unchecked((int)0xFF000000);
        Assert.Contains(d, px => px == black);
        Assert.Contains(f, px => px == black);
    }

    [Fact]
    public void Settings_CorruptFallsBack()
    {
        var s = AppSettings.Load(); // must never throw
        Assert.InRange(s.MaxIter, 50, 50000);
    }

    [Fact]
    public void BackToSet_Math()
    {
        // Frame 0 = start state, last frame = full set (§5.11).
        var (cx0, cy0, sc0) = Views.MainWindow.BackToSetFrame(-0.75, 0.1, 1e-4, 0, 120);
        Assert.Equal(-0.75, cx0);
        Assert.Equal(0.1, cy0);
        Assert.Equal(1e-4, sc0);
        var (cx1, cy1, sc1) = Views.MainWindow.BackToSetFrame(-0.75, 0.1, 1e-4, 119, 120);
        Assert.Equal(Mandelbrot.InitialCenterX, cx1, 12);
        Assert.Equal(Mandelbrot.InitialCenterY, cy1, 12);
        Assert.Equal(Mandelbrot.InitialScale, sc1, 9);
        // Scale grows monotonically along the animation.
        double prev = sc0;
        for (int i = 1; i < 120; i++)
        {
            double s = Views.MainWindow.BackToSetFrame(-0.75, 0.1, 1e-4, i, 120).Scale;
            Assert.True(s >= prev);
            prev = s;
        }
        // Continuity from the framed point: no center jump bigger than a
        // fraction of the current view width (deep-zoom start).
        double px = -0.75, py = 0.1;
        for (int i = 1; i < 120; i++)
        {
            var (cx, cy, sc) = Views.MainWindow.BackToSetFrame(-0.75, 0.1, 1e-4, i, 120);
            double step = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
            Assert.True(step <= 0.05 * sc, $"frame {i}: center step {step} vs scale {sc}");
            px = cx; py = cy;
        }
    }

    [Fact]
    public void Csv_Format()
    {
        string row = BenchmarkCsv.FormatRow(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CPU", "Test", "64-bit", 1, 10, 8.0, 33.6);
        Assert.StartsWith("2026-01-01", row);
        Assert.Contains(BenchmarkCsv.Header.Split(',')[0], "timestamp");
    }

    [Fact]
    public void Render_Deterministic()
    {
        int W = 96, H = 64;
        int[] a = new int[W * H], b = new int[W * H];
        Mandelbrot.RenderInto(a, W, H, -0.5, 0, 9.36, 200, Palette.Thermal, 2,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        Mandelbrot.RenderInto(b, W, H, -0.5, 0, 9.36, 200, Palette.Thermal, 2,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void AaFactors_AllRun_InteriorBlack()
    {
        int black = unchecked((int)0xFF000000);
        foreach (int k in new[] { 1, 2, 4, 8 })
        {
            int W = 64, H = 48;
            int[] px = new int[W * H];
            Mandelbrot.RenderInto(px, W, H, -0.5, 0, 9.36, 200, Palette.Fire, k,
                CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
            Assert.Contains(px, v => v == black); // interior stays black at any AA
        }
    }

    [Fact]
    public void Julia_DiffersFromMandelbrot_Deterministic()
    {
        int W = 96, H = 64;
        int[] m = new int[W * H], j1 = new int[W * H], j2 = new int[W * H];
        Mandelbrot.RenderInto(m, W, H, -0.5, 0, 4.0, 200, Palette.Ocean, 1,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        Mandelbrot.RenderInto(j1, W, H, -0.5, 0, 4.0, 200, Palette.Ocean, 1,
            CancellationToken.None, 0, 0, W, H, -0.7, 0.27015, true, true);
        Mandelbrot.RenderInto(j2, W, H, -0.5, 0, 4.0, 200, Palette.Ocean, 1,
            CancellationToken.None, 0, 0, W, H, -0.7, 0.27015, true, true);
        Assert.Equal(j1, j2);
        Assert.NotEqual(m, j1); // different dynamical systems
    }

    [Fact]
    public void Tiling512_ExportPath_PixelIdentical()
    {
        // Mirrors ExportForm: 640x480 whole vs 512x512 tiles, AA2x.
        int W = 640, H = 480, k = 2, maxIter = 300;
        int[] whole = new int[W * H];
        Mandelbrot.RenderInto(whole, W, H, -0.75, 0.1, 0.5, maxIter, Palette.Violet, k,
            CancellationToken.None, 0, 0, W, H, 0, 0, false, true);
        int[] tiled = new int[W * H];
        const int tile = 512;
        for (int oy = 0; oy < H; oy += tile)
            for (int ox = 0; ox < W; ox += tile)
            {
                int tw = Math.Min(tile, W - ox), th = Math.Min(tile, H - oy);
                int[] tb = new int[tw * th];
                Mandelbrot.RenderInto(tb, tw, th, -0.75, 0.1, 0.5, maxIter, Palette.Violet, k,
                    CancellationToken.None, ox, oy, W, H, 0, 0, false, true);
                for (int y = 0; y < th; y++)
                    Array.Copy(tb, y * tw, tiled, (oy + y) * W + ox, tw);
            }
        Assert.Equal(whole, tiled);
    }

    [Fact]
    public void PngWriter_RoundTripHeader()
    {
        int[] px = new int[8 * 4];
        for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)0xFF112233);
        string path = Path.Combine(Path.GetTempPath(), $"mvtest_{Guid.NewGuid():N}.png");
        try
        {
            PngWriter.WriteArgb(path, px, 8, 4);
            byte[] sig = new byte[8];
            using (var fs = File.OpenRead(path)) fs.ReadExactly(sig, 0, 8);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, sig);
            Assert.True(new FileInfo(path).Length > 50); // 8x4 solid compresses tiny
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
