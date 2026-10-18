using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Small camera-facing particles: blood and dust (blended) and spell sparks (glowing, additive).
sealed class Particles
{
    struct Bit
    {
        public Vector3 Pos, Vel;
        public float Life, Max, Size, Gravity, Grow;
        public Color Col;
        public bool Glow;
    }

    const int Cap = 9000;
    /// The graphics preset scales how many particles can be alive at once.
    public static float CapScale = 1;
    public int Count => _bits.Count;
    readonly List<Bit> _bits = new();
    readonly Random _rng = new(5);
    static Texture2D _dot;

    /// A soft round dot every particle is drawn with.
    static Texture2D Dot()
    {
        if (_dot.Id != 0) return _dot;
        var img = Raylib.GenImageGradientRadial(32, 32, 0.15f, Color.White, new Color(255, 255, 255, 0));
        _dot = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_dot, TextureFilter.Bilinear);
        return _dot;
    }

    /// A burst of `count` particles from `at`, flung at up to `speed` m/s (mostly upward), plus an optional push.
    public void Emit(Vector3 at, int count, Color col, float speed, float size, float life, float gravity = 9.8f, bool glow = false, Vector3? push = null, float grow = 0)
    {
        for (var i = 0; i < count && _bits.Count < Cap * CapScale; i++)
        {
            var dir = new Vector3(_rng.NextSingle() * 2 - 1, _rng.NextSingle() * 1.2f - 0.2f, _rng.NextSingle() * 2 - 1);
            if (dir.LengthSquared() < 1e-4f) dir = Vector3.UnitY;
            dir = Vector3.Normalize(dir);
            var l = life * (0.6f + 0.4f * _rng.NextSingle());
            _bits.Add(new Bit
            {
                Pos = at, Vel = dir * speed * (0.35f + 0.65f * _rng.NextSingle()) + (push ?? Vector3.Zero),
                Life = l, Max = l, Size = size * (0.7f + 0.6f * _rng.NextSingle()), Gravity = gravity, Col = col, Glow = glow, Grow = grow,
            });
        }
    }

    public void Update(float dt)
    {
        for (var i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            b.Life -= dt;
            if (b.Life <= 0) { _bits.RemoveAt(i); continue; }
            b.Vel.Y -= b.Gravity * dt;
            b.Size += b.Grow * dt;
            if (b.Grow > 0) b.Vel *= 1 - 0.4f * dt; // smoke slows as it spreads
            b.Pos += b.Vel * dt;
            if (b.Pos.Y < 0.02f) { b.Pos.Y = 0.02f; b.Vel *= 0.25f; } // settle on the street
            _bits[i] = b;
        }
    }

    public void Draw(Camera3D cam)
    {
        if (_bits.Count == 0) return;
        var fwd = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        foreach (var glowPass in new[] { false, true })
        {
            if (glowPass) Raylib.BeginBlendMode(BlendMode.Additive);
            Rlgl.SetTexture(Dot().Id);
            Rlgl.Begin(DrawMode.Quads);
            foreach (var b in _bits)
            {
                if (b.Glow != glowPass) continue;
                var k = b.Life / b.Max;
                var s = b.Glow ? b.Size * (0.4f + 0.6f * k) : b.Size;
                // fade out puffs right in front of the lens, so smoke never greys out the whole view
                var depth = Vector3.Dot(b.Pos - cam.Position, fwd);
                if (depth < 0.05f) continue;
                var near = Math.Clamp((depth - s * 0.3f) / (s + 1.5f), 0, 1);
                var a = (byte) Math.Clamp(b.Col.A * near * MathF.Min(1, k * 2.5f) * (b.Grow > 0 ? MathF.Min(1, (1 - k) * 6) : 1), 0, 255);
                Rlgl.Color4ub(b.Col.R, b.Col.G, b.Col.B, a);
                var r = right * s; var u = up * s;
                var p = b.Pos;
                Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(p.X - r.X - u.X, p.Y - r.Y - u.Y, p.Z - r.Z - u.Z);
                Rlgl.TexCoord2f(1, 0); Rlgl.Vertex3f(p.X + r.X - u.X, p.Y + r.Y - u.Y, p.Z + r.Z - u.Z);
                Rlgl.TexCoord2f(1, 1); Rlgl.Vertex3f(p.X + r.X + u.X, p.Y + r.Y + u.Y, p.Z + r.Z + u.Z);
                Rlgl.TexCoord2f(0, 1); Rlgl.Vertex3f(p.X - r.X + u.X, p.Y - r.Y + u.Y, p.Z - r.Z + u.Z);
            }
            Rlgl.End();
            Rlgl.SetTexture(0);
            Rlgl.DrawRenderBatchActive();
            if (glowPass) Raylib.EndBlendMode();
        }
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
