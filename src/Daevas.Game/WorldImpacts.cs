using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Impacts that match what they hit (sparks off metal wrecks, concrete chips and dust off walls, grit off the street)
/// and the marks they leave: bullet pockmarks that stay on walls and wrecks for the rest of the run.
sealed partial class World
{
    readonly List<(Vector3 at, Vector3 normal, float size, float shade)> _pocks = new();

    /// What is at a point: "metal" (a wreck), "ground" or "concrete".
    string SurfaceAt(Vector3 at)
    {
        if (at.Y < 0.25f) return "ground";
        foreach (var (prop, pos, _) in _street)
            if (WreckName.IsMatch(prop.Def.Mesh) && Vector2.Distance(new(pos.X, pos.Z), new(at.X, at.Z)) < MathF.Max(prop.Size.X, prop.Size.Z) * 0.6f) return "metal";
        return "concrete";
    }

    /// A shot stopped by something solid at `at` (inside `box`, if any): the hit, and a mark that stays.
    void Impact(Vector3 at, BoundingBox? box)
    {
        switch (SurfaceAt(at))
        {
            case "metal":
                Bits.Emit(at, 7, new Color(255, 225, 150, 255), 5f, 0.03f, 0.35f, 9f, glow: true);
                Bits.Emit(at, 2, new Color(90, 88, 92, 150), 0.6f, 0.12f, 0.5f, -0.3f, grow: 0.4f);
                break;
            case "ground":
                Bits.Emit(at with { Y = 0.05f }, 6, new Color(96, 84, 70, 200), 2.2f, 0.07f, 0.6f, 9f);
                Bits.Emit(at with { Y = 0.1f }, 2, new Color(120, 108, 92, 120), 0.5f, 0.3f, 0.9f, -0.2f, grow: 0.6f);
                Mark(at, 0.12f, new Color(30, 26, 22, 120), 400);
                return;
            default:
                Bits.Emit(at, 6, new Color(150, 142, 132, 220), 3f, 0.05f, 0.5f, 9f);
                Bits.Emit(at, 3, new Color(170, 160, 148, 140), 0.7f, 0.22f, 0.9f, -0.2f, grow: 0.7f);
                break;
        }
        if (box is not { } b) return;
        // the face it struck: the box side nearest the point
        float dx0 = at.X - b.Min.X, dx1 = b.Max.X - at.X, dz0 = at.Z - b.Min.Z, dz1 = b.Max.Z - at.Z, dy1 = b.Max.Y - at.Y;
        var m = MathF.Min(MathF.Min(MathF.Min(dx0, dx1), MathF.Min(dz0, dz1)), dy1);
        var n = m == dx0 ? -Vector3.UnitX : m == dx1 ? Vector3.UnitX : m == dz0 ? -Vector3.UnitZ : m == dz1 ? Vector3.UnitZ : Vector3.UnitY;
        var onFace = n.X != 0 ? at with { X = n.X < 0 ? b.Min.X : b.Max.X } : n.Z != 0 ? at with { Z = n.Z < 0 ? b.Min.Z : b.Max.Z } : at with { Y = b.Max.Y };
        _pocks.Add((onFace + n * 0.02f, n, 0.06f + (float) _rng.NextDouble() * 0.06f, 0.6f + 0.4f * (float) _rng.NextDouble()));
        if (_pocks.Count > 600) _pocks.RemoveAt(0);
    }

    void DrawPocks()
    {
        if (_pocks.Count == 0) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Rlgl.Begin(DrawMode.Triangles);
        foreach (var (at, n, size, shade) in _pocks)
        {
            if (Vector3.DistanceSquared(at, P.Pos) > 50 * 50) continue;
            var right = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY)) * size;
            var up = Vector3.Cross(right, n);
            var c = (byte) (18 + 20 * (1 - shade));
            void V(Vector3 p, byte a) { Rlgl.Color4ub(c, c, c, a); Rlgl.Vertex3f(p.X, p.Y, p.Z); }
            // a little star of a chip: a dark core and a lighter rim, as a fan of 6 triangles
            for (var k = 0; k < 6; k++)
            {
                var a0 = k * MathF.Tau / 6; var a1 = (k + 1) * MathF.Tau / 6;
                var r0 = 1 + 0.35f * MathF.Sin(k * 2.3f + size * 50); var r1 = 1 + 0.35f * MathF.Sin((k + 1) * 2.3f + size * 50);
                V(at, (byte) (210 * shade));
                V(at + (right * MathF.Cos(a0) + up * MathF.Sin(a0)) * r0, 0);
                V(at + (right * MathF.Cos(a1) + up * MathF.Sin(a1)) * r1, 0);
            }
        }
        Rlgl.End();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }
}
