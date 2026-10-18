using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Daeva wings as real, lit geometry, after AION 2's: great white angel wings from the upper back, the arm rising to
/// the wrist above the head and sweeping far out, hung with long slender pointed feathers whose ragged tips reach
/// the knees, under layers of shorter coverts. Silver-white, the class colour only a breath at the tips.
/// One dynamic mesh, refilled each frame and drawn with the world's lit shader (sun, shadows, fog).
static unsafe class Wings
{
    // (feathers, from..to along the arm, length root..tip, half width, angle from straight down root..tip (deg),
    //  layer: + toward the back, how much class colour at the tip)
    static readonly (int n, float from, float to, float len0, float len1, float hw, float ang0, float ang1, float layer, float tint)[] Rows =
    {
        (14, 0.58f, 1.00f, 1.05f, 1.55f, 0.16f, 40, 100, 0.000f, 0.35f), // primaries: the long fan swept out from the wrist
        (18, 0.04f, 0.66f, 0.80f, 1.45f, 0.19f, 4, 32, 0.010f, 0.30f),   // secondaries: hanging from the rising arm
        (16, 0.00f, 0.32f, 0.70f, 0.95f, 0.17f, 0, 10, 0.020f, 0.20f),   // tertials, down to the hips by the body
        (20, 0.04f, 0.98f, 0.48f, 0.72f, 0.16f, 10, 86, 0.030f, 0.12f),  // greater coverts
        (18, 0.02f, 0.94f, 0.30f, 0.42f, 0.14f, 15, 92, 0.040f, 0.06f),  // median coverts
        (16, 0.00f, 0.90f, 0.16f, 0.24f, 0.12f, 25, 98, 0.050f, 0.00f),  // lesser coverts along the arm's top edge
    };
    // folded, each row's length (m): the primaries make the long point, the coverts the rounded top
    static readonly float[] FoldLen = { 1.0f, 0.68f, 0.55f, 0.40f, 0.26f, 0.15f };
    const int PerSide = 102, Quads = PerSide * 2;
    /// The whole wing's size: AION 2's span would hide the hero from the over-the-shoulder camera.
    public static float Size => 0.7f * Settings.S.WingSize / 100f;

    static Mesh _mesh;
    static Model _model;
    static bool _ready;

    static void Ensure()
    {
        if (_ready) return;
        _ready = true;
        _mesh = new Mesh(Quads * 4, Quads * 2);
        _mesh.AllocVertices(); _mesh.AllocNormals(); _mesh.AllocTexCoords(); _mesh.AllocColors(); _mesh.AllocIndices();
        var idx = _mesh.IndicesAs<ushort>();
        for (var q = 0; q < Quads; q++)
        {
            idx[q * 6] = (ushort) (q * 4); idx[q * 6 + 1] = (ushort) (q * 4 + 1); idx[q * 6 + 2] = (ushort) (q * 4 + 2);
            idx[q * 6 + 3] = (ushort) (q * 4); idx[q * 6 + 4] = (ushort) (q * 4 + 2); idx[q * 6 + 5] = (ushort) (q * 4 + 3);
        }
        Raylib.UploadMesh(ref _mesh, true);
        _model = Lit.Make(_mesh);
        _model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = MakeFeather();
    }

    static float Hash(int i) { var x = (uint) i * 2654435761u; x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; return (x & 0xffff) / 65535f; }

    /// A long flight feather: a fine shaft, soft vanes, a pointed tip, wispy barbs splitting along the edges.
    static Texture2D MakeFeather()
    {
        const int W = 64, H = 256;
        var img = Raylib.GenImageColor(W, H, new Color(0, 0, 0, 0));
        for (var y = 0; y < H; y++)
        {
            var v = y / (float) (H - 1);                                           // 0 = tip, 1 = root
            var half = MathF.Pow(MathF.Min(1, v / 0.32f), 0.6f);                  // a soft point at the tip
            if (v > 0.9f) half *= 1 - (v - 0.9f) * 6;                               // pinched at the quill
            for (var x = 0; x < W; x++)
            {
                var u = (x + 0.5f) / W * 2 - 1;                                    // -1..1 across
                var au = MathF.Abs(u);
                // barbs run down and out from the shaft: the edge frays and splits along them
                var barb = v * 70 - au * 9 + (u < 0 ? 0.37f : 0);
                var fray = 0.92f + 0.08f * MathF.Sin(barb * 2.1f);
                var edge = half * fray - au;
                if (edge <= 0) continue;
                var a = Math.Clamp(edge * 7, 0, 1) * Math.Clamp(v * 9, 0, 1);
                var shaft = au < 0.04f ? 0.93f : 1f;
                var streak = 0.97f + 0.03f * MathF.Sin(barb * 3.1f);
                var shade = (0.82f + 0.18f * (1 - au)) * shaft * streak * (0.9f + 0.1f * (1 - v));
                var c = (byte) Math.Clamp(255 * shade, 0, 255);
                Raylib.ImageDrawPixel(ref img, x, y, new Color(c, c, (byte) Math.Min(255, c + 5), (byte) (255 * a)));
            }
        }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        return tex;
    }

    /// Both wings at the shoulders. `spread` 0..1 (folded against the back..open), `flap` the beat angle, `tint` the class colour.
    public static void Draw(Vector3 shoulders, Vector3 fwd, Vector3 right, float spread, float flap, Color tint, float alpha, float sway = 0)
    {
        Ensure();
        var open = spread * spread * (3 - 2 * Math.Clamp(spread, 0, 1)); // eased: folded against the back .. spread wide
        var pos = _mesh.VerticesAs<Vector3>(); var nrm = _mesh.NormalsAs<Vector3>();
        var uv = _mesh.TexCoordsAs<Vector2>(); var col = _mesh.ColorsAs<Color>();
        var up = Vector3.UnitY;
        // Elyos wings are white; Asmodian wings are dark, with a violet sheen toward the tips
        var dark = Settings.S.WingStyle == 1;
        var white = dark ? new Vector3(58, 52, 70) : new Vector3(240, 241, 246);
        var shadow = dark ? new Vector3(30, 26, 38) : new Vector3(196, 198, 210);
        if (dark) tint = new Color((byte) 150, (byte) 90, (byte) 220, (byte) 255);
        var tintV = Vector3.Lerp(white, new Vector3(tint.R, tint.G, tint.B), 0.75f);
        var a = (byte) Math.Clamp(255 * alpha, 0, 255);
        Color Shade(Vector3 c) => new((byte) c.X, (byte) c.Y, (byte) c.Z, a);
        var q = 0;
        for (var side = -1; side <= 1; side += 2)
        {
            var root = shoulders + right * side * 0.12f - fwd * 0.08f;
            // the wing's plane: out from the spine and up, tilted back a little; the beat swings it about the shoulder
            var beat = flap * 0.9f;
            var outDir = Vector3.Normalize(right * side * MathF.Cos(beat) + up * MathF.Sin(beat) - fwd * 0.18f);
            var upDir = Vector3.Normalize(up * MathF.Cos(beat) - right * side * MathF.Sin(beat));
            var back = -fwd;
            var sideDir = right * side;
            // spread: the arm curves rising out to the wrist, high above the shoulder
            Vector3 Arm(float t)
            {
                var o = 1.6f * t;
                var h = MathF.Sin(MathF.Min(1, t / 0.75f) * MathF.PI / 2) * 0.8f - MathF.Max(0, t - 0.75f) * 0.2f;
                return root + outDir * o + upDir * h + back * 0.18f * t;
            }
            var ri = 0;
            foreach (var row in Rows)
            {
                for (var i = 0; i < row.n; i++)
                {
                    var f = row.n == 1 ? 0 : i / (float) (row.n - 1);
                    var seed = side * 1000 + ri * 100 + i;
                    var t = row.from + (row.to - row.from) * f;
                    var ang = (row.ang0 + (row.ang1 - row.ang0) * f + (Hash(seed) - 0.5f) * 6) * MathF.PI / 180 + flap * 0.25f * t;
                    var len = (row.len0 + (row.len1 - row.len0) * f) * (0.82f + 0.36f * Hash(seed + 7)) * Size;
                    var bOpen = root + (Arm(t) + back * row.layer - root) * Size;
                    var dirOpen = Vector3.Normalize(-upDir * MathF.Cos(ang) + outDir * MathF.Sin(ang) + back * 0.12f);
                    // folded: the wrist a rounded hump above the shoulder blade, the coverts over it, the long primaries
                    // laid down and back to a point (a slim teardrop either side of the spine)
                    // (seen side-on, as in a drawing of a folded wing: edge-on to the camera behind, so the hero stays in view)
                    var bFold = root + sideDir * (0.06f + row.layer * 0.8f) + up * (0.18f + 0.14f * MathF.Sin(MathF.PI * MathF.Min(1, t * 1.2f))) + back * (-0.06f + 0.18f * t);
                    var dirFold = Vector3.Normalize(-up + back * (0.10f + 0.16f * t * (ri == 0 ? 1.5f : 1) + 0.12f * sway * t) + sideDir * (0.07f + 0.05f * sway * side));
                    var lenFold = FoldLen[ri] * (0.9f + 0.2f * Hash(seed + 3)) * (ri == 0 ? 0.85f + 0.3f * f : 1);
                    var b = Vector3.Lerp(bFold, bOpen, open);
                    var dir = Vector3.Normalize(Vector3.Lerp(dirFold, dirOpen, open));
                    len = lenFold + (len - lenFold) * open;
                    var tip = b + dir * len;
                    // the feather's width, in the wing's plane: across the back when spread, fore and aft when folded
                    var across = Vector3.Normalize(Vector3.Lerp(Vector3.Cross(dirFold, right), Vector3.Cross(dirOpen, fwd), open));
                    var hw = row.hw * Size * (0.6f + 0.4f * open);
                    var n = Vector3.Normalize(Vector3.Cross(across, dir));
                    var o = q * 4;
                    pos[o] = b - across * hw * 0.45f; pos[o + 1] = b + across * hw * 0.45f; // narrower at the quill
                    pos[o + 2] = tip + across * hw; pos[o + 3] = tip - across * hw;
                    for (var k = 0; k < 4; k++) nrm[o + k] = n;
                    uv[o] = new Vector2(0, 1); uv[o + 1] = new Vector2(1, 1); uv[o + 2] = new Vector2(1, 0); uv[o + 3] = new Vector2(0, 0);
                    // under-layers a touch greyer, so the rows read; the class colour only at the long tips
                    var root3 = Vector3.Lerp(white, shadow, 0.6f - row.layer * 10);
                    col[o] = col[o + 1] = Shade(root3);
                    col[o + 2] = col[o + 3] = Shade(Vector3.Lerp(white, tintV, row.tint * (0.4f + 0.6f * t)));
                    q++;
                }
                ri++;
            }
        }
        Raylib.UpdateMeshBuffer(_mesh, 0, pos, 0);
        Raylib.UpdateMeshBuffer(_mesh, 2, nrm, 0);
        Raylib.UpdateMeshBuffer(_mesh, 3, col, 0);
        Raylib.UpdateMeshBuffer(_mesh, 1, uv, 0);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling(); // seen from behind and in front
        Lit.AlphaMode(Lit.Blended);
        Raylib.DrawModel(_model, Vector3.Zero, 1, Color.White);
        Lit.AlphaMode(Lit.Opaque);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }
}
