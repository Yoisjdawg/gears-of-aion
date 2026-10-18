using System;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Soft glowing sprites for spell effects: camera-facing glows, glowing beams and flat decals on the street,
/// all additive, from two small textures made at start (a soft dot and a soft ring).
static class Glow
{
    static Texture2D _dot, _ring, _feather;
    static Camera3D _cam;

    static Texture2D Make(Func<float, float> alphaAtRadius)
    {
        const int n = 64;
        var img = Raylib.GenImageColor(n, n, new Color(255, 255, 255, 0));
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n * 2 - 1; var dy = (y + 0.5f) / n * 2 - 1;
                var a = Math.Clamp(alphaAtRadius(MathF.Sqrt(dx * dx + dy * dy)), 0, 1);
                Raylib.ImageDrawPixel(ref img, x, y, new Color(255, 255, 255, (int) (a * 255)));
            }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        return tex;
    }

    static void Ensure()
    {
        if (_dot.Id != 0) return;
        _dot = Make(r => r >= 1 ? 0 : MathF.Pow(1 - r, 2.2f));                           // soft round glow
        _ring = Make(r => MathF.Exp(-MathF.Pow((r - 0.82f) / 0.09f, 2)) + 0.12f * MathF.Max(0, 1 - r)); // thin ring, faint fill
        _feather = MakeFeather();
    }

    /// A feather, root at the bottom (v = 0) and tip at the top: a quill down the middle, soft barbs either side
    /// and a rounded tip; brighter along the quill.
    static Texture2D MakeFeather()
    {
        const int w = 32, h = 128;
        var img = Raylib.GenImageColor(w, h, new Color(255, 255, 255, 0));
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w * 2 - 1;   // -1..1 across
                var v = (y + 0.5f) / h;           // 0 root .. 1 tip
                var half = 0.92f * MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1, v * 0.62f + 0.06f)), 0.7f) * MathF.Min(1, (1 - v) * 7 + 0.15f);
                var edge = Math.Clamp((half - MathF.Abs(u)) * 9, 0, 1);
                var barbs = 0.82f + 0.18f * MathF.Sin(v * 70 - MathF.Abs(u) * 24);
                var quill = MathF.Exp(-u * u * 260);
                var a = edge * barbs * (0.75f + 0.25f * v) + quill * 0.3f * MathF.Min(1, v * 4);
                var shade = (byte) Math.Clamp(255 * (0.82f + 0.18f * quill + 0.1f * (1 - MathF.Abs(u))), 0, 255);
                Raylib.ImageDrawPixel(ref img, x, h - 1 - y, new Color(shade, shade, shade, (byte) (Math.Clamp(a, 0, 1) * 255)));
            }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        return tex;
    }

    /// One wing feather from `root` to `tip`, `halfWidth` metres either side along `side` (unit), shaded from
    /// `rootCol` to `tipCol`; `additive` draws it as a glow instead of a solid feather.
    public static void Feather(Vector3 root, Vector3 tip, Vector3 side, float halfWidth, Color rootCol, Color tipCol, bool additive)
    {
        Ensure();
        var s = side * halfWidth;
        void Draw()
        {
            Rlgl.SetTexture(_feather.Id);
            Rlgl.Begin(DrawMode.Quads);
            Rlgl.Color4ub(rootCol.R, rootCol.G, rootCol.B, rootCol.A);
            Rlgl.TexCoord2f(0, 1); Rlgl.Vertex3f(root.X - s.X, root.Y - s.Y, root.Z - s.Z);
            Rlgl.TexCoord2f(1, 1); Rlgl.Vertex3f(root.X + s.X, root.Y + s.Y, root.Z + s.Z);
            Rlgl.Color4ub(tipCol.R, tipCol.G, tipCol.B, tipCol.A);
            Rlgl.TexCoord2f(1, 0); Rlgl.Vertex3f(tip.X + s.X, tip.Y + s.Y, tip.Z + s.Z);
            Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(tip.X - s.X, tip.Y - s.Y, tip.Z - s.Z);
            Rlgl.End();
            Rlgl.SetTexture(0);
        }
        if (additive) { Additive(() => Emit(_feather.Id, root - s, root + s, tip + s, tip - s, Color.White, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0))); _queue[^1] = _queue[^1] with { Col = tipCol }; return; }
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        Draw();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    /// Call at the start of each 3D pass so billboards face this camera.
    public static void Begin(Camera3D cam) { Ensure(); _cam = cam; }

    static void Quad(Texture2D tex, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col) =>
        Emit(tex.Id, a, b, c, d, col, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

    /// Additive quads wait here and go to the GPU together in Flush (one batch instead of one per glow).
    struct Queued { public uint Tex; public Vector3 A, B, C, D; public Color Col; public Vector2 UvA, UvB, UvC, UvD; }
    static readonly System.Collections.Generic.List<Queued> _queue = new();
    public static int LastFlushCount;

    static void Additive(Action draw)
    {
        _capture = true;
        draw();
        _capture = false;
    }
    static bool _capture;

    static void Emit(uint tex, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        if (_capture) { _queue.Add(new Queued { Tex = tex, A = a, B = b, C = c, D = d, Col = col, UvA = ua, UvB = ub, UvC = uc, UvD = ud }); return; }
        Rlgl.SetTexture(tex);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(col.R, col.G, col.B, col.A);
        Rlgl.TexCoord2f(ua.X, ua.Y); Rlgl.Vertex3f(a.X, a.Y, a.Z);
        Rlgl.TexCoord2f(ub.X, ub.Y); Rlgl.Vertex3f(b.X, b.Y, b.Z);
        Rlgl.TexCoord2f(uc.X, uc.Y); Rlgl.Vertex3f(c.X, c.Y, c.Z);
        Rlgl.TexCoord2f(ud.X, ud.Y); Rlgl.Vertex3f(d.X, d.Y, d.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// Draws every queued glow in one additive pass (call once at the end of the 3D scene).
    public static void Flush()
    {
        LastFlushCount = _queue.Count;
        if (_queue.Count == 0) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        Raylib.BeginBlendMode(BlendMode.Additive);
        // grouped by texture so the batch only breaks when the texture changes
        foreach (var group in _queue.GroupBy(q => q.Tex))
        {
            Rlgl.SetTexture(group.Key);
            Rlgl.Begin(DrawMode.Quads);
            foreach (var q in group)
            {
                Rlgl.Color4ub(q.Col.R, q.Col.G, q.Col.B, q.Col.A);
                Rlgl.TexCoord2f(q.UvA.X, q.UvA.Y); Rlgl.Vertex3f(q.A.X, q.A.Y, q.A.Z);
                Rlgl.TexCoord2f(q.UvB.X, q.UvB.Y); Rlgl.Vertex3f(q.B.X, q.B.Y, q.B.Z);
                Rlgl.TexCoord2f(q.UvC.X, q.UvC.Y); Rlgl.Vertex3f(q.C.X, q.C.Y, q.C.Z);
                Rlgl.TexCoord2f(q.UvD.X, q.UvD.Y); Rlgl.Vertex3f(q.D.X, q.D.Y, q.D.Z);
            }
            Rlgl.End();
        }
        Rlgl.SetTexture(0);
        Rlgl.DrawRenderBatchActive();
        Raylib.EndBlendMode();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
        _queue.Clear();
    }

    /// A glowing sprite facing the camera (`size` = radius in metres).
    /// Softer glows when the player asks for less flashing.
    static Color Calm(Color c) => Settings.S.ReduceFlashing ? c with { A = (byte) (c.A * 0.55f) } : c;

    public static void Sprite(Vector3 at, float size, Color col)
    {
        col = Calm(col);
        if (size <= 0.001f || col.A == 0) return;
        var fwd = Vector3.Normalize(_cam.Target - _cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY)) * size;
        var up = Vector3.Normalize(Vector3.Cross(right, fwd)) * size;
        Additive(() => Quad(_dot, at - right - up, at + right - up, at + right + up, at - right + up, col));
    }

    /// A glowing beam from a to b, `width` metres wide, turned to face the camera.
    public static void Beam(Vector3 a, Vector3 b, float width, Color col)
    {
        col = Calm(col);
        var axis = b - a;
        if (axis.LengthSquared() < 1e-6f || width <= 0.001f) return;
        var mid = (a + b) / 2;
        var side = Vector3.Cross(Vector3.Normalize(axis), Vector3.Normalize(_cam.Position - mid));
        if (side.LengthSquared() < 1e-6f) return;
        side = Vector3.Normalize(side) * width;
        // the dot texture across the width gives soft edges; its middle row runs along the length
        Additive(() => Emit(_dot.Id, a - side, a + side, b + side, b - side, col, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f)));
        Sprite(a, width * 2, col);
        Sprite(b, width * 3, col);
    }

    /// A dark mark on the street (blended, not glowing): scorches, blood, footprints. Turned by `yaw`.
    /// The street height under a point (set by the world): marks and rings at street level sit on it.
    public static Func<Vector3, float>? Ground;
    static float OnGround(Vector3 c) => c.Y < 0.3f && Ground != null ? MathF.Max(c.Y, Ground(c)) : c.Y;

    public static void Mark(Vector3 centre, float radius, Color col, float yaw)
    {
        if (radius <= 0.01f || col.A == 0) return;
        Ensure();
        var y = OnGround(centre) + 0.04f;
        var c = MathF.Cos(yaw) * radius; var s = MathF.Sin(yaw) * radius;
        var a = new Vector3(centre.X - c + s, y, centre.Z - s - c);
        var b = new Vector3(centre.X + c + s, y, centre.Z + s - c);
        var d = new Vector3(centre.X + c - s, y, centre.Z + s + c);
        var e = new Vector3(centre.X - c - s, y, centre.Z - s + c);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Quad(_dot, a, b, d, e, col);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    /// A flat glow or ring lying on the street at `centre`.
    public static void Decal(Vector3 centre, float radius, Color col, bool ring)
    {
        col = Calm(col);
        if (radius <= 0.01f) return;
        var y = OnGround(centre) + 0.06f;
        var a = new Vector3(centre.X - radius, y, centre.Z - radius);
        var b = new Vector3(centre.X + radius, y, centre.Z - radius);
        var c = new Vector3(centre.X + radius, y, centre.Z + radius);
        var d = new Vector3(centre.X - radius, y, centre.Z + radius);
        Additive(() => Quad(ring ? _ring : _dot, a, b, c, d, col));
    }
}
