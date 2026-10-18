using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The Gears of Aion splash: dusk over a burning city in crimson and gold, light breaking behind the logo, white
/// Daeva feathers drifting down through rising embers, and the credits. Shown while the game loads, and exported
/// with --splash for sharing.
static class Splash
{
    static float H(int i) { var s = MathF.Sin(i * 12.9898f + 78.233f) * 43758.547f; return s - MathF.Floor(s); }
    static Color C(int r, int g, int b, float a) => new((byte) r, (byte) g, (byte) b, (byte) Math.Clamp(a * 255, 0, 255));

    /// The whole splash over (0,0)-(w,h), `t` seconds since it appeared (the logo fades in over the first one and a half).
    /// The title's live 3D island is drawn behind (then only the logo and words go on top).
    public static bool SceneBehind;

    public static void Draw(float w, float h, float t)
    {
        var center = new Vector2(w / 2, h * 0.40f);
        var size = h * 0.105f;
        if (SceneBehind)
        {
            // the 3D island is behind: dim it a little, and more under the words, so they read
            Raylib.DrawRectangle(0, 0, (int) w + 1, (int) h + 1, new Color(0, 0, 0, 80));
            Raylib.DrawRectangleGradientV(0, (int) (h * 0.5f), (int) w, (int) (h * 0.5f) + 1, new Color(0, 0, 0, 0), new Color(0, 0, 0, 200));
        }
        else Backdrop(w, h, t, center);
        // the logo, rising into the light
        var appear = Math.Clamp(t / 1.5f, 0, 1); appear = appear * appear * (3 - 2 * appear);
        Logo.Draw(center.X, center.Y + (1 - appear) * h * 0.02f, size * (0.94f + 0.06f * appear), appear, MathF.Sin(t * 1.3f) * appear);
        var sub = Math.Clamp((t - 1.0f) / 0.8f, 0, 1);
        Text("AION 2  x  Gears of War: E-Day", w / 2, center.Y + size * 2.25f, h * 0.032f, C(240, 236, 228, sub));
        Credits(w, h, sub);
    }

    public static void Credits(float w, float h, float a)
    {
        if (Brand.Aion.Id != 0 || Brand.Gears.Id != 0) Brand.DrawLockup(w / 2, h * 0.875f, h * 0.05f, a);
        else Text("Made by yoisjdawg", w / 2, h * 0.925f, h * 0.022f, C(255, 214, 120, a));
        Text("AION 2 © NCSOFT    Gears of War: E-Day © Microsoft / The Coalition    Fan-made: reads your own installs of both games",
            w / 2, h * 0.955f, h * 0.016f, C(200, 190, 175, a * 0.9f));
    }

    /// The scene without the logo: sky, light, embers, falling feathers and brass, vignette (menus draw over it, dimmed).
    public static void Backdrop(float w, float h, float t, Vector2 center, float dim = 0)
    {
        // sky: night above, the city's fires below
        Raylib.DrawRectangleGradientV(0, 0, (int) w, (int) (h * 0.55f) + 1, C(10, 10, 20, 1), C(46, 24, 30, 1));
        Raylib.DrawRectangleGradientV(0, (int) (h * 0.55f), (int) w, (int) (h * 0.45f) + 1, C(46, 24, 30, 1), C(70, 16, 10, 1));
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Raylib.BeginBlendMode(BlendMode.Additive);
        // light breaking behind the logo: a soft glow and slow-turning rays
        Logo.Disc(center, h * 0.62f, C(255, 196, 110, 0.20f), C(255, 120, 60, 0));
        Logo.Disc(center, h * 0.26f, C(255, 230, 170, 0.30f), C(255, 190, 110, 0));
        Rlgl.Begin(DrawMode.Triangles);
        for (var i = 0; i < 18; i++)
        {
            var a = i * MathF.Tau / 18 + t * 0.05f + H(i) * 0.2f;
            var spread = 0.035f + 0.03f * H(i + 40);
            var len = h * (0.75f + 0.5f * H(i + 80));
            var a0 = new Vector2(MathF.Cos(a - spread), MathF.Sin(a - spread)); var a1 = new Vector2(MathF.Cos(a + spread), MathF.Sin(a + spread));
            var glow = 0.07f + 0.05f * MathF.Sin(t * 0.7f + i * 1.7f);
            Logo.V(center, C(255, 214, 140, glow)); Logo.V(center + a0 * len, C(255, 160, 80, 0)); Logo.V(center + a1 * len, C(255, 160, 80, 0));
        }
        Rlgl.End();
        // embers rising off the burning city
        for (var i = 0; i < 70; i++)
        {
            var speed = h * (0.04f + 0.08f * H(i + 300));
            var y = h - ((t * speed + H(i + 200) * h * 1.2f) % (h * 1.2f));
            var x = H(i + 100) * w + MathF.Sin(t * (0.6f + H(i)) + i) * h * 0.02f;
            var flicker = 0.5f + 0.5f * MathF.Sin(t * (3 + 4 * H(i + 7)) + i);
            var r = h * (0.0016f + 0.0026f * H(i + 9));
            var fade = Math.Clamp(y / h * 1.4f, 0, 1);
            Raylib.DrawCircleV(new Vector2(x, y), r * 3, C(255, 110, 40, 0.10f * flicker * fade));
            Raylib.DrawCircleV(new Vector2(x, y), r, C(255, 190, 110, 0.85f * flicker * fade));
        }
        Raylib.EndBlendMode();
        // white Daeva feathers drifting down and Gears brass tumbling with them, streaming in from the top
        for (var i = 0; i < 18; i++)
        {
            var far = H(i + 500);                                  // 0 near .. 1 far
            var y = Fall(t, h * (0.04f + 0.05f * (1 - far)), H(i + 600) * 4.5f, h);
            if (y < -h * 0.1f) continue;
            var x = H(i + 700) * w + MathF.Sin(t * 0.8f + i * 2.1f) * h * 0.05f;
            var ang = MathF.Sin(t * 0.9f + i) * 0.9f + H(i + 800) * MathF.Tau;
            var len = h * (0.05f + 0.05f * (1 - far));
            var dir = new Vector2(MathF.Sin(ang), MathF.Cos(ang));
            var a = 0.35f + 0.5f * (1 - far);
            Logo.Feather(new Vector2(x, y) - dir * len / 2, dir, len, len * 0.085f, C(244, 244, 250, a), C(255, 240, 210, a), C(30, 24, 22, a * 0.25f));
        }
        for (var i = 0; i < 22; i++)
        {
            var far = H(i + 900);
            var y = Fall(t, h * (0.16f + 0.14f * (1 - far)), H(i + 950) * 3.5f, h);
            if (y < -h * 0.1f) continue;
            var x = H(i + 1000) * w;
            var spin = t * (2.5f + 4 * H(i + 1050)) * (i % 2 == 0 ? 1 : -1) + H(i + 1100) * MathF.Tau;
            Bullet(new Vector2(x, y), spin, h * (0.036f + 0.026f * (1 - far)), i % 3 == 0, 0.55f + 0.45f * (1 - far));
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        // a vignette to hold the eye on the middle
        Raylib.DrawRectangleGradientV(0, 0, (int) w, (int) (h * 0.22f), C(0, 0, 0, 0.55f), C(0, 0, 0, 0));
        Raylib.DrawRectangleGradientV(0, (int) (h * 0.78f), (int) w, (int) (h * 0.22f) + 1, C(0, 0, 0, 0), C(0, 0, 0, 0.6f));
        Raylib.DrawRectangleGradientH(0, 0, (int) (w * 0.18f), (int) h, C(0, 0, 0, 0.5f), C(0, 0, 0, 0));
        Raylib.DrawRectangleGradientH((int) (w * 0.82f), 0, (int) (w * 0.18f) + 1, (int) h, C(0, 0, 0, 0), C(0, 0, 0, 0.5f));
        if (dim > 0) Raylib.DrawRectangle(0, 0, (int) w + 1, (int) h + 1, C(0, 0, 0, dim));
    }

    /// Where a falling thing is: above the screen until its turn (`delay` s), then falling at `speed` px/s, round again.
    static float Fall(float t, float speed, float delay, float h)
    {
        var v = (t - delay) * speed;
        return v < 0 ? -h : v % (h * 1.25f) - h * 0.12f;
    }

    /// A spent brass casing (or a whole round, copper-tipped), tumbling: `spin` its angle, `len` its length.
    static void Bullet(Vector2 c, float spin, float len, bool live, float a)
    {
        var d = new Vector2(MathF.Cos(spin), MathF.Sin(spin)); var p = new Vector2(-d.Y, d.X);
        var hw = len * 0.2f;
        var back = c - d * len * 0.5f; var neck = c + d * len * (live ? 0.12f : 0.3f); var mouth = c + d * len * (live ? 0.2f : 0.5f);
        var ink = C(24, 16, 10, a);
        Color brassLit = C(255, 222, 140, a), brass = C(206, 156, 64, a), brassDark = C(112, 76, 28, a);
        Rlgl.Begin(DrawMode.Triangles);
        void Quad(Vector2 q0, Vector2 q1, Vector2 q2, Vector2 q3, Color c0, Color c1, Color c2, Color c3)
        { Logo.V(q0, c0); Logo.V(q1, c1); Logo.V(q2, c2); Logo.V(q0, c0); Logo.V(q2, c2); Logo.V(q3, c3); }
        var o = len * 0.06f;
        // outline, then the case lit down one side, a shoulder taper to the neck
        Quad(back - p * (hw + o) - d * o, back + p * (hw + o) - d * o, mouth + p * (hw * 0.75f + o) + d * o, mouth - p * (hw * 0.75f + o) + d * o, ink, ink, ink, ink);
        Quad(back - p * hw, back, neck, neck - p * hw, brassLit, brass, brass, brassLit);
        Quad(back, back + p * hw, neck + p * hw, neck, brass, brassDark, brassDark, brass);
        Quad(neck - p * hw, neck + p * hw, mouth + p * hw * 0.75f, mouth - p * hw * 0.75f, brassLit, brassDark, brassDark, brassLit);
        // the rim and primer at the base
        Quad(back - p * hw * 1.1f, back + p * hw * 1.1f, back + p * hw * 1.1f + d * len * 0.07f, back - p * hw * 1.1f + d * len * 0.07f, brassDark, brassDark, brassDark, brassDark);
        if (live)
        {
            // the bullet: a copper ogive to a point
            var tip = c + d * len * 0.62f;
            Color cop = C(214, 110, 60, a), copDark = C(120, 52, 26, a);
            Logo.V(mouth - p * (hw * 0.75f + o), ink); Logo.V(mouth + p * (hw * 0.75f + o), ink); Logo.V(tip + d * o, ink);
            Logo.V(mouth - p * hw * 0.75f, cop); Logo.V(mouth, cop); Logo.V(tip, cop);
            Logo.V(mouth, copDark); Logo.V(mouth + p * hw * 0.75f, copDark); Logo.V(tip, copDark);
        }
        Rlgl.End();
    }

    static void Text(string s, float cx, float y, float size, Color c)
    {
        var font = Logo.TitleFont();
        var m = Raylib.MeasureTextEx(font, s, size, size * 0.04f);
        var p = new Vector2(cx - m.X / 2, y);
        Raylib.DrawTextEx(font, s, p + new Vector2(0, size * 0.06f), size, size * 0.04f, new Color((byte) 0, (byte) 0, (byte) 0, (byte) (c.A * 0.8f)));
        Raylib.DrawTextEx(font, s, p, size, size * 0.04f, c);
    }

    /// --splash <png> [--splash-size WxH]: a still of the splash, the logo fully in.
    public static void Export(string path, int w, int h)
    {
        var rt = Raylib.LoadRenderTexture(w, h);
        Raylib.BeginTextureMode(rt);
        Raylib.ClearBackground(Color.Black);
        Draw(w, h, 6f);
        Raylib.EndTextureMode();
        var img = Raylib.LoadImageFromTexture(rt.Texture);
        Raylib.ImageFlipVertical(ref img);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8);
        Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
        Raylib.UnloadRenderTexture(rt);
        Log.Info("splash saved to " + path);
    }
}
