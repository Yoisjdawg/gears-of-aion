using System;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The Gears of Aion logo, drawn from scratch: a heavy gunmetal cog (the Gears) borne up by a pair of feathered Daeva wings
/// (AION), a gold star at its hub, and the name beneath. Used on the title and loading screens, and exported with
/// --logo for the app icon and the capsule art.
static class Logo
{
    static Font _font;
    static bool _fontReady;

    static readonly Color Ink = new(18, 14, 12, 255);
    static readonly Color Gold = new(255, 214, 120, 255);

    internal static Font TitleFont()
    {
        if (_fontReady) return _font;
        _fontReady = true;
        var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeuib.ttf");
        var glyphs = new int[96]; // printable ASCII and the copyright sign
        for (var i = 0; i < 95; i++) glyphs[i] = 32 + i;
        glyphs[95] = 0xA9;
        _font = File.Exists(f) ? Raylib.LoadFontEx(f, 160, glyphs, glyphs.Length) : Raylib.GetFontDefault();
        Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
        return _font;
    }

    /// The logo centred on (cx, cy) (the cog's hub), `size` the cog's radius in pixels. `flap` gently beats the
    /// wings (title screen), `name` adds GEARS OF AION beneath.
    public static void Draw(float cx, float cy, float size, float alpha = 1, float flap = 0, bool name = true, float raise = 0.58f)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        var c = new Vector2(cx, cy);
        Wing(c, size, -1, flap, alpha, raise);
        Wing(c, size, 1, flap, alpha, raise);
        Cog(c, size, alpha);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        if (name) Name("GEARS OF AION", cx, cy + size * 1.30f, size * 0.78f, alpha);
    }

    static Color A(Color c, float alpha) => new(c.R, c.G, c.B, (byte) Math.Clamp(c.A * alpha, 0, 255));

    static Color Mix(Color a, Color b, float t) => new(
        (byte) (a.R + (b.R - a.R) * t), (byte) (a.G + (b.G - a.G) * t), (byte) (a.B + (b.B - a.B) * t), (byte) (a.A + (b.A - a.A) * t));

    // ---------------------------------------------------------------- the wings

    // (feathers, from..to along the arm, length root..tip, half width, start..end angle from straight down, colour at the tip)
    static readonly (int n, float from, float to, float len0, float len1, float hw, float ang0, float ang1, float tint)[] Rows =
    {
        (9, 0.55f, 1.00f, 1.55f, 2.15f, 0.16f, 64, 148, 1.00f), // primaries, fanning out and up at the tip
        (10, 0.05f, 0.62f, 1.10f, 1.50f, 0.16f, 16, 58, 0.80f), // secondaries hanging below the arm
        (12, 0.04f, 0.96f, 0.58f, 0.80f, 0.13f, 22, 112, 0.45f), // greater coverts
        (10, 0.02f, 0.88f, 0.30f, 0.42f, 0.12f, 30, 116, 0.15f), // lesser coverts along the arm
    };

    static void Wing(Vector2 c, float size, int side, float flap, float alpha, float raise)
    {
        var s = side;
        var root = c + new Vector2(s * size * 0.55f, -size * 0.25f);
        var span = size * 2.15f;
        var lift = raise + flap * 0.18f; // radians the arm rises from level
        // the wing arm: rises from behind the cog to the wrist and on out to the tip
        Vector2 Arm(float t)
        {
            var dir = new Vector2(s * MathF.Cos(lift), -MathF.Sin(lift));
            return root + dir * span * t + new Vector2(0, -MathF.Sin(MathF.PI * t * 0.9f) * size * 0.55f);
        }
        var silver = new Color(236, 238, 246, 255);
        var deep = new Color(196, 160, 82, 255);
        // back rows first so the coverts lie over the flight feathers
        foreach (var row in Rows)
            for (var i = row.n - 1; i >= 0; i--)
            {
                var f = row.n == 1 ? 0 : i / (float) (row.n - 1);
                var t = row.from + (row.to - row.from) * f;
                var ang = (row.ang0 + (row.ang1 - row.ang0) * f) * MathF.PI / 180 - flap * 0.12f * t;
                var dir = new Vector2(s * MathF.Sin(ang), MathF.Cos(ang));
                var len = (row.len0 + (row.len1 - row.len0) * f) * size;
                var tipCol = Mix(silver, Gold, row.tint * (0.4f + 0.6f * t));
                Feather(Arm(t), dir, len, row.hw * size, A(Mix(silver, deep, 0.15f), alpha), A(tipCol, alpha), A(Ink, alpha));
            }
        // the arm's leading edge, a soft bright bone line
        for (var k = 0; k < 16; k++)
        {
            var a0 = Arm(k / 16f * 0.92f); var a1 = Arm((k + 1) / 16f * 0.92f);
            Raylib.DrawLineEx(a0, a1, size * 0.07f * (1 - k / 22f), A(Ink, alpha));
            Raylib.DrawLineEx(a0, a1, size * 0.04f * (1 - k / 22f), A(silver, alpha));
        }
    }

    const int N = 10;
    static readonly Vector2[] _l = new Vector2[N + 1], _r = new Vector2[N + 1];

    /// A feather: narrow quill, broad vane, rounded tip, an ink outline and a shaft down the middle.
    internal static void Feather(Vector2 b, Vector2 dir, float len, float hw, Color baseCol, Color tipCol, Color outline)
    {
        var across = new Vector2(-dir.Y, dir.X);
        var l = _l; var r = _r;
        void Edges(float grow)
        {
            for (var k = 0; k <= N; k++)
            {
                var v = k / (float) N;
                var w = (0.35f + 0.65f * MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1, v * 0.62f + 0.08f)), 0.7f)) * hw;
                if (v > 0.8f) w *= MathF.Sqrt(MathF.Max(0, 1 - (v - 0.8f) / 0.2f * 0.96f)); // rounded tip
                var p = b + dir * (len * v + grow * (v > 0.99f ? 1 : 0));
                l[k] = p - across * (w + grow); r[k] = p + across * (w + grow);
            }
        }
        Edges(MathF.Max(1.2f, hw * 0.16f));
        Strip(l, r, outline, outline);
        Edges(0);
        Strip(l, r, baseCol, tipCol);
        Raylib.DrawLineEx(b + dir * len * 0.04f, b + dir * len * 0.9f, MathF.Max(1, hw * 0.12f), Mix(baseCol, outline, 0.35f));
    }

    static void Strip(ReadOnlySpan<Vector2> l, ReadOnlySpan<Vector2> r, Color c0, Color c1)
    {
        Rlgl.Begin(DrawMode.Triangles);
        for (var k = 0; k + 1 < l.Length; k++)
        {
            var ca = Mix(c0, c1, k / (float) (l.Length - 1)); var cb = Mix(c0, c1, (k + 1) / (float) (l.Length - 1));
            V(l[k], ca); V(r[k], ca); V(r[k + 1], cb);
            V(l[k], ca); V(r[k + 1], cb); V(l[k + 1], cb);
        }
        Rlgl.End();
    }

    internal static void V(Vector2 p, Color c) { Rlgl.Color4ub(c.R, c.G, c.B, c.A); Rlgl.Vertex2f(p.X, p.Y); }

    // ---------------------------------------------------------------- the cog

    /// After the Gears cog: a heavy ring of broad square teeth in weathered gunmetal, lit from the top left, a
    /// crimson glow round its hollow heart.
    static void Cog(Vector2 c, float r, float alpha)
    {
        const int Teeth = 10;
        var ink = A(Ink, alpha);
        var light = Vector2.Normalize(new Vector2(-0.55f, -0.85f));
        var dark = new Color(40, 40, 46, 255); var bright = new Color(205, 207, 214, 255);
        Color Steel(Vector2 d, float lift) => A(Mix(dark, bright, Math.Clamp((Vector2.Dot(d, light) * 0.5f + 0.5f) * 0.8f + lift, 0, 1)), alpha);
        Vector2 Dir(float a) => new(MathF.Sin(a), -MathF.Cos(a));
        void Tooth(float a, float grow, bool fill)
        {
            var d = Dir(a); var p = new Vector2(-d.Y, d.X);
            float inner = 0.80f * r, outer = 1.17f * r + grow, w0 = 0.205f * r + grow, w1 = 0.175f * r + grow;
            Color c0 = fill ? Steel(d, -0.15f) : ink, c1 = fill ? Steel(d, 0.12f) : ink;
            Rlgl.Begin(DrawMode.Triangles);
            V(c + d * inner - p * w0, c0); V(c + d * inner + p * w0, c0); V(c + d * outer + p * w1, c1);
            V(c + d * inner - p * w0, c0); V(c + d * outer + p * w1, c1); V(c + d * outer - p * w1, c1);
            Rlgl.End();
            if (fill)
            {
                // the tooth's top edge catches the light
                var lit = Vector2.Dot(d, light);
                if (lit > -0.2f) Raylib.DrawLineEx(c + d * (outer - r * 0.03f) - p * w1 * 0.85f, c + d * (outer - r * 0.03f) + p * w1 * 0.85f, r * 0.035f, A(new Color(235, 236, 240, (int) (255 * Math.Clamp(lit + 0.3f, 0, 1))), alpha));
            }
        }
        for (var i = 0; i < Teeth; i++) Tooth(i * MathF.Tau / Teeth, r * 0.05f, false);
        Raylib.DrawCircleV(c, r * 0.95f, ink);
        for (var i = 0; i < Teeth; i++) Tooth(i * MathF.Tau / Teeth, 0, true);
        // the rim: bright on its lit outer edge, falling into shadow toward the hole (a bevel)
        Band(c, r * 0.58f, r * 0.90f, d => Steel(-d, -0.1f), d => Steel(d, 0.08f));
        Band(c, r * 0.71f, r * 0.735f, _ => A(new Color(28, 26, 28, 200), alpha), _ => A(new Color(28, 26, 28, 200), alpha));
        // battle wear: scratches across the steel
        for (var i = 0; i < 9; i++)
        {
            var a = Hash(i * 3.1f) * MathF.Tau; var rr = r * (0.62f + 0.24f * Hash(i * 7.7f));
            var d = Dir(a); var p = new Vector2(-d.Y, d.X);
            var at = c + d * rr; var len = r * (0.06f + 0.10f * Hash(i * 1.9f));
            Raylib.DrawLineEx(at - p * len, at + p * len + d * len * 0.4f, MathF.Max(1, r * 0.012f), A(new Color(30, 28, 30, 170), alpha));
        }
        // the hollow heart, glowing crimson at its edge
        Raylib.DrawCircleV(c, r * 0.58f, ink);
        Disc(c, r * 0.545f, A(new Color(22, 16, 16, 255), alpha), A(new Color(150, 20, 16, 255), alpha));
        Disc(c, r * 0.43f, A(new Color(30, 22, 22, 255), alpha), A(new Color(30, 22, 22, 0), alpha));
        Star(c, r * 0.42f, r * 0.12f, A(Gold, alpha), ink);
    }

    static float Hash(float x) { var s = MathF.Sin(x * 12.9898f + 4.1f) * 43758.547f; return s - MathF.Floor(s); }

    static void Band(Vector2 c, float r0, float r1, Func<Vector2, Color> inner, Func<Vector2, Color> outer)
    {
        const int N = 72;
        Rlgl.Begin(DrawMode.Triangles);
        for (var i = 0; i < N; i++)
        {
            var a0 = i * MathF.Tau / N; var a1 = (i + 1) * MathF.Tau / N;
            var d0 = new Vector2(MathF.Sin(a0), -MathF.Cos(a0)); var d1 = new Vector2(MathF.Sin(a1), -MathF.Cos(a1));
            V(c + d0 * r0, inner(d0)); V(c + d0 * r1, outer(d0)); V(c + d1 * r1, outer(d1));
            V(c + d0 * r0, inner(d0)); V(c + d1 * r1, outer(d1)); V(c + d1 * r0, inner(d1));
        }
        Rlgl.End();
    }
    internal static void Disc(Vector2 c, float r, Color inner, Color outer)
    {
        Rlgl.Begin(DrawMode.Triangles);
        for (var i = 0; i < 48; i++)
        {
            var a0 = i * MathF.Tau / 48; var a1 = (i + 1) * MathF.Tau / 48;
            V(c, inner); V(c + new Vector2(MathF.Sin(a0), -MathF.Cos(a0)) * r, outer); V(c + new Vector2(MathF.Sin(a1), -MathF.Cos(a1)) * r, outer);
        }
        Rlgl.End();
    }

    /// AION's light at the hub: a four-pointed gold star.
    static void Star(Vector2 c, float rOut, float rIn, Color fill, Color ink)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var g = pass == 0 ? rIn * 0.35f : 0;
            var col = pass == 0 ? ink : fill;
            var mid = pass == 0 ? ink : new Color((byte) 255, (byte) 246, (byte) 214, fill.A);
            Rlgl.Begin(DrawMode.Triangles);
            for (var i = 0; i < 8; i++)
            {
                float Rad(int k) => (k % 2 == 0 ? rOut : rIn) + g;
                var a0 = i * MathF.Tau / 8; var a1 = (i + 1) * MathF.Tau / 8;
                V(c, mid);
                V(c + new Vector2(MathF.Sin(a0), -MathF.Cos(a0)) * Rad(i), col);
                V(c + new Vector2(MathF.Sin(a1), -MathF.Cos(a1)) * Rad(i + 1), col);
            }
            Rlgl.End();
        }
    }

    // ---------------------------------------------------------------- the name

    static void Name(string s, float cx, float y, float size, float alpha)
    {
        var font = TitleFont();
        var spacing = size * 0.06f;
        var w = Raylib.MeasureTextEx(font, s, size, spacing).X;
        var p = new Vector2(cx - w / 2, y);
        var o = MathF.Max(1.5f, size / 14);
        for (var k = 0; k < 16; k++)
        {
            var a = k * MathF.Tau / 16;
            Raylib.DrawTextEx(font, s, p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * o, size, spacing, A(Ink, alpha * 0.9f));
        }
        Raylib.DrawTextEx(font, s, p + new Vector2(0, size * 0.02f), size, spacing, A(new Color(150, 96, 30, 255), alpha));
        Raylib.DrawTextEx(font, s, p, size, spacing, A(Gold, alpha));
    }

    /// --logo <png> [px] [noname]: draws the logo alone on a clear square and saves it.
    public static void Export(string path, int px, bool name)
    {
        var rt = Raylib.LoadRenderTexture(px, px);
        Raylib.BeginTextureMode(rt);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Rlgl.SetBlendFactorsSeparate(0x0302, 0x0303, 1, 0x0303, 0x8006, 0x8006); // keep the alpha right on a clear target
        Raylib.BeginBlendMode(BlendMode.CustomSeparate);
        if (name) Draw(px / 2f, px * 0.56f, px * 0.112f);
        else Draw(px / 2f, px * 0.68f, px * 0.138f, name: false, raise: 0.95f); // wings raised high: fills a square icon
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        var img = Raylib.LoadImageFromTexture(rt.Texture);
        Raylib.ImageFlipVertical(ref img);
        Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
        Raylib.UnloadRenderTexture(rt);
        Log.Info("logo saved to " + path);
    }
}
