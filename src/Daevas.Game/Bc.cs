using System;
using System.Threading.Tasks;

namespace Daevas.Game;

/// A small, fast block-compression encoder: textures go to the GPU as DXT1 (opaque, 1/8 the memory) or DXT5
/// (with alpha, 1/4), with their whole mip chain built here (the GPU can't make mips for compressed textures).
static class Bc
{
    /// The whole mip chain, compressed, one level after another (as raylib wants it), and the level count.
    public static (byte[] data, int mips) Encode(byte[] rgba, int w, int h, bool alpha)
    {
        var levels = new System.Collections.Generic.List<byte[]>();
        var cur = rgba; int cw = w, ch = h;
        while (true)
        {
            levels.Add(EncodeLevel(cur, cw, ch, alpha));
            if (cw == 1 && ch == 1) break;
            (cur, cw, ch) = Half(cur, cw, ch);
        }
        var total = 0;
        foreach (var l in levels) total += l.Length;
        var all = new byte[total];
        var at = 0;
        foreach (var l in levels) { l.CopyTo(all, at); at += l.Length; }
        return (all, levels.Count);
    }

    /// True when any pixel is noticeably see-through.
    public static bool HasAlpha(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4) if (rgba[i] < 250) return true;
        return false;
    }

    static (byte[], int, int) Half(byte[] src, int w, int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
                for (var c = 0; c < 4; c++)
                {
                    int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                    dst[(y * nw + x) * 4 + c] = (byte) ((src[(y0 * w + x0) * 4 + c] + src[(y0 * w + x1) * 4 + c] + src[(y1 * w + x0) * 4 + c] + src[(y1 * w + x1) * 4 + c] + 2) / 4);
                }
        return (dst, nw, nh);
    }

    static byte[] EncodeLevel(byte[] px, int w, int h, bool alpha)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4, size = alpha ? 16 : 8;
        var outp = new byte[bw * bh * size];
        Parallel.For(0, bh, by =>
        {
            Span<byte> block = stackalloc byte[64];
            for (var bx = 0; bx < bw; bx++)
            {
                for (var i = 0; i < 16; i++)
                {
                    int x = Math.Min(w - 1, bx * 4 + (i & 3)), y = Math.Min(h - 1, by * 4 + (i >> 2));
                    var s = (y * w + x) * 4;
                    block[i * 4] = px[s]; block[i * 4 + 1] = px[s + 1]; block[i * 4 + 2] = px[s + 2]; block[i * 4 + 3] = px[s + 3];
                }
                var o = (by * bw + bx) * size;
                if (alpha) { AlphaBlock(block, outp.AsSpan(o, 8)); ColorBlock(block, outp.AsSpan(o + 8, 8)); }
                else ColorBlock(block, outp.AsSpan(o, 8));
            }
        });
        return outp;
    }

    static ushort To565(int r, int g, int b) => (ushort) (((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | (b * 31 + 127) / 255);

    static (int r, int g, int b) From565(ushort c) => (((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31);

    /// Colour: the block's bounding box (inset a little) as the two end colours, each pixel to the nearest of four.
    static void ColorBlock(ReadOnlySpan<byte> b, Span<byte> o)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        for (var i = 0; i < 16; i++)
        {
            int r = b[i * 4], g = b[i * 4 + 1], bl = b[i * 4 + 2];
            if (r < minR) minR = r; if (g < minG) minG = g; if (bl < minB) minB = bl;
            if (r > maxR) maxR = r; if (g > maxG) maxG = g; if (bl > maxB) maxB = bl;
        }
        int insR = (maxR - minR) >> 4, insG = (maxG - minG) >> 4, insB = (maxB - minB) >> 4;
        minR += insR; minG += insG; minB += insB; maxR -= insR; maxG -= insG; maxB -= insB;
        var c0 = To565(maxR, maxG, maxB);
        var c1 = To565(minR, minG, minB);
        if (c0 < c1) (c0, c1) = (c1, c0);
        o[0] = (byte) c0; o[1] = (byte) (c0 >> 8); o[2] = (byte) c1; o[3] = (byte) (c1 >> 8);
        uint indices = 0;
        if (c0 != c1)
        {
            var (r0, g0, b0) = From565(c0);
            var (r1, g1, b1) = From565(c1);
            Span<int> pr = stackalloc int[4]; Span<int> pg = stackalloc int[4]; Span<int> pb = stackalloc int[4];
            pr[0] = r0; pg[0] = g0; pb[0] = b0;
            pr[1] = r1; pg[1] = g1; pb[1] = b1;
            pr[2] = (2 * r0 + r1) / 3; pg[2] = (2 * g0 + g1) / 3; pb[2] = (2 * b0 + b1) / 3;
            pr[3] = (r0 + 2 * r1) / 3; pg[3] = (g0 + 2 * g1) / 3; pb[3] = (b0 + 2 * b1) / 3;
            for (var i = 0; i < 16; i++)
            {
                int r = b[i * 4], g = b[i * 4 + 1], bl = b[i * 4 + 2];
                int best = 0, bestD = int.MaxValue;
                for (var k = 0; k < 4; k++)
                {
                    int dr = r - pr[k], dg = g - pg[k], db = bl - pb[k];
                    var d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (d < bestD) { bestD = d; best = k; }
                }
                indices |= (uint) best << (i * 2);
            }
        }
        o[4] = (byte) indices; o[5] = (byte) (indices >> 8); o[6] = (byte) (indices >> 16); o[7] = (byte) (indices >> 24);
    }

    /// Alpha (DXT5): the block's lowest and highest alpha, each pixel to the nearest of eight steps between them.
    static void AlphaBlock(ReadOnlySpan<byte> b, Span<byte> o)
    {
        int lo = 255, hi = 0;
        for (var i = 0; i < 16; i++) { int a = b[i * 4 + 3]; if (a < lo) lo = a; if (a > hi) hi = a; }
        o[0] = (byte) hi; o[1] = (byte) lo;
        ulong bits = 0;
        if (hi != lo)
        {
            Span<int> pal = stackalloc int[8];
            pal[0] = hi; pal[1] = lo;
            for (var k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * hi + k * lo) / 7;
            for (var i = 0; i < 16; i++)
            {
                int a = b[i * 4 + 3], best = 0, bestD = int.MaxValue;
                for (var k = 0; k < 8; k++) { var d = Math.Abs(a - pal[k]); if (d < bestD) { bestD = d; best = k; } }
                bits |= (ulong) best << (i * 3);
            }
        }
        for (var k = 0; k < 6; k++) o[2 + k] = (byte) (bits >> (k * 8));
    }
}
