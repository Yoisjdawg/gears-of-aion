using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Gears graffiti and painted signs on the district's walls: put up where a street meets a flat building wall.
sealed partial class World
{
    readonly List<(Vector3 at, float yaw, float w, float h, int tex)> _decals = new();
    static Model _decalModel;
    static bool _decalReady;

    /// The first building wall (real Gears triangles, not prop boxes) along a ray: distance and the wall's normal.
    (float dist, Vector3 normal)? WallRay(Vector3 from, Vector3 dir, float len, bool deepOk = false)
    {
        if (_assets.City == null) return null;
        var ray = new Ray(from, dir);
        var end = from + dir * len;
        float near = len; Vector3 normal = default; var hit = false;
        int x0 = (int) MathF.Floor((MathF.Min(from.X, end.X) - 0.3f) / City.TriCell), x1 = (int) MathF.Floor((MathF.Max(from.X, end.X) + 0.3f) / City.TriCell);
        int z0 = (int) MathF.Floor((MathF.Min(from.Z, end.Z) - 0.3f) / City.TriCell), z1 = (int) MathF.Floor((MathF.Max(from.Z, end.Z) + 0.3f) / City.TriCell);
        for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
                if (_assets.City.CameraTris.TryGetValue((x, z), out var tris))
                    foreach (var (a, b, c) in tris)
                    {
                        var h = Raylib.GetRayCollisionTriangle(ray, a, b, c);
                        if (h.Hit && h.Distance > 0 && h.Distance < near) { near = h.Distance; normal = h.Normal; hit = true; }
                    }
        // the boxes of building pieces (tall and wide, so not cars or lampposts)
        foreach (var box in _assets.City.CameraBoxes)
        {
            if (box.Max.Y - box.Min.Y < 4 || MathF.Max(box.Max.X - box.Min.X, box.Max.Z - box.Min.Z) < 3) continue;
            if (box.Max.X < MathF.Min(from.X, end.X) - 0.5f || box.Min.X > MathF.Max(from.X, end.X) + 0.5f) continue;
            if (box.Max.Z < MathF.Min(from.Z, end.Z) - 0.5f || box.Min.Z > MathF.Max(from.Z, end.Z) + 0.5f) continue;
            var h = Raylib.GetRayCollisionBox(ray, box);
            if (!h.Hit || h.Distance <= 0 || h.Distance >= near) continue;
            // only thin pieces (wall panels): a deep box's face can float in front of doorways and recesses
            var depth = MathF.Abs(h.Normal.X) > 0.5f ? box.Max.X - box.Min.X : box.Max.Z - box.Min.Z;
            if (depth > 0.8f && !deepOk) continue;
            near = h.Distance; normal = h.Normal; hit = true;
        }
        return hit ? (near, normal) : null;
    }

    void PlaceDecals()
    {
        if (_assets.City == null || _assets.Decals.Count == 0) return;
        var cb = _assets.City.CameraBoxes;
        Log.Info($"graffiti walls: {cb.Count} camera boxes ({cb.Count(b => b.Max.Y - b.Min.Y >= 4)} tall), {_assets.City.CameraTris.Count} tri cells, box y {(cb.Count > 0 ? cb.Min(b => b.Min.Y) : 0):0.0}..{(cb.Count > 0 ? cb.Max(b => b.Max.Y) : 0):0.0}, arena half {ArenaHalf:0}");
        int noWall = 0, notUpright = 0, notFlat = 0;
        // strict first (real wall triangles and thin panels); boxy districts with few of those also take deep boxes
        foreach (var deepOk in new[] { false, true })
        {
            if (deepOk && _decals.Count >= 6) break;
            for (var tries = 0; tries < 3000 && _decals.Count < 30; tries++)
            {
                // a random open spot on the streets (the walking map isn't built yet this early)
                var spot = new Vector3(((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 4), 0, ((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 4));
                if (Blocked(spot, 0.8f)) continue;
                var a = _rng.Next(16) * MathF.Tau / 16;
                var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
                var from = spot + Vector3.UnitY * 2f;
                if (WallRay(from, dir, 14f, deepOk) is not { } wall || wall.dist < 1f) { noWall++; continue; }
                // an upright wall facing the street (the triangle's normal may point either way)
                var n = Vector3.Normalize(wall.normal with { Y = 0 });
                if (Vector3.Dot(n, dir) > 0) n = -n;
                if (MathF.Abs(wall.normal.Y) > 0.2f || Vector3.Dot(n, -dir) < 0.7f) { notUpright++; continue; }
                // and flat: rays a metre to each side, up and down hit it at nearly the same distance
                var side = new Vector3(dir.Z, 0, -dir.X);
                var flat = true;
                foreach (var off in new[] { side * 1.1f, -side * 1.1f, Vector3.UnitY * 1.1f, -Vector3.UnitY * 1.0f })
                    if (WallRay(from + off, dir, 15f, deepOk) is not { } w2 || MathF.Abs(w2.dist - wall.dist) > 0.15f) { flat = false; break; }
                if (!flat) { notFlat++; continue; }
                var at = from + dir * wall.dist + n * 0.04f;
                if (_decals.Any(x => Vector3.Distance(x.at, at) < 7)) continue;
                var tex = _rng.Next(_assets.Decals.Count);
                var t = _assets.Decals[tex];
                var width = 1.6f + (float) _rng.NextDouble() * 1.2f;
                var height = width * t.Height / MathF.Max(1, t.Width);
                if (height > 2.0f) { height = 2.0f; width = height * t.Width / MathF.Max(1, t.Height); }
                at.Y = 1.0f + height / 2 + (float) _rng.NextDouble() * 0.5f;
                _decals.Add((at, MathF.Atan2(n.X, n.Z), width, height, tex));
            }
        }
        if (Environment.GetEnvironmentVariable("DAEVAS_SHOPSHOT") is { } shop && _assets.City is { Shopfronts.Count: > 0 } sc)
        {
            // dev: stand in a shopfront (DAEVAS_SHOPSHOT=n picks the n-th nearest to the start), facing in
            var pick = sc.Shopfronts.OrderBy(f => Dist2(f, P.Pos)).Skip(int.TryParse(shop, out var k) ? k : 0).First();
            var open = Enumerable.Range(0, 16).Select(i => i * MathF.Tau / 16).OrderByDescending(a => CameraRay(pick with { Y = 1.6f }, new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), 30)).First();
            P.Pos = pick with { Y = 0 } + new Vector3(MathF.Sin(open), 0, MathF.Cos(open)) * 2.5f;
            P.Yaw = open + MathF.PI; // back toward the shop
            Log.Info($"shopshot at {pick}");
        }
        if (Environment.GetEnvironmentVariable("DAEVAS_DECALSHOT") == "1" && _decals.Count > 0)
        {
            // dev: start in front of the first one, facing it
            var (at, yaw, _, _, _) = _decals[0];
            var n = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
            P.Pos = (at + n * 5) with { Y = 0 };
            P.Yaw = MathF.Atan2(-n.X, -n.Z);
        }
        Log.Info($"graffiti: {_decals.Count} on the walls (tries without a wall {noWall}, not upright {notUpright}, not flat {notFlat})");
    }

    /// Dev (capsule art): the direction with the longest clear view from the hero that still leaves room
    /// for a camera on the other side.
    public float OpenestYaw()
    {
        float Ray(float a) => CameraRay(P.Pos + Vector3.UnitY * 2, new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), 120);
        var best = 0f; var far = 0f;
        for (var i = 0; i < 32; i++)
        {
            var a = i * MathF.Tau / 32;
            var d = Ray(a);
            if (d > far && Ray(a + MathF.PI) > 6.5f) { far = d; best = a; }
        }
        return best;
    }

    readonly List<(Vector3 at, float yaw, Vector2 size, Texture2D tex)> _groundDecals = new();

    /// Gears street decals on the open street: cracks, patches, potholes, burns, rubble, oil, manholes and road
    /// markings (the lines and crossings run along the street they're on).
    void PlaceGroundDecals()
    {
        if (_assets.City == null || _assets.GroundDecals.Count == 0) return;
        foreach (var (name, tex) in _assets.GroundDecals)
        {
            var kind = GameAssets.GroundDecalKinds.First(k => k.name == name);
            var line = name.StartsWith("road_marking") || name.StartsWith("tyre_skid");
            for (int placed = 0, tries = 0; placed < kind.count && tries < kind.count * 30; tries++)
            {
                var at = new Vector3(((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 3), 0, ((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 3));
                if (Blocked(at, MathF.Max(kind.size.X, kind.size.Y) * 0.4f) || GroundAt(at) > 0.2f) continue; // on the road itself
                float yaw;
                if (line)
                {
                    // along the street: the direction with the longest clear run
                    float best = 0; yaw = 0;
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * MathF.PI / 8;
                        var run = CameraRay(at + Vector3.UnitY * 0.5f, new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), 40) + CameraRay(at + Vector3.UnitY * 0.5f, new Vector3(-MathF.Sin(a), 0, -MathF.Cos(a)), 40);
                        if (run > best) { best = run; yaw = a; }
                    }
                }
                else yaw = (float) _rng.NextDouble() * MathF.Tau;
                var scale = 0.85f + (float) _rng.NextDouble() * 0.35f;
                _groundDecals.Add((at, yaw, kind.size * scale, tex));
                placed++;
            }
        }
        Log.Info($"street decals: {_groundDecals.Count} on the streets");
    }

    unsafe void DrawGroundDecals()
    {
        Lit.AlphaMode(Lit.Blended);
        try
        {
        if (_groundDecals.Count == 0 || ShadowPass) return;
        if (!_decalReady) { _decalModel = Lit.Make(Raylib.GenMeshPlane(1, 1, 1, 1)); _decalReady = true; }
        Rlgl.DisableDepthMask(); // flat on the street: they don't hide each other
        for (var i = 0; i < _groundDecals.Count; i++)
        {
            var d = _groundDecals[i];
            if (Vector3.DistanceSquared(d.at, P.Pos) > 70 * 70) continue;
            _decalModel.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = d.tex;
            var model = _decalModel;
            model.Transform = Raymath.MatrixMultiply(Raymath.MatrixScale(d.size.X, 1, d.size.Y), Raymath.MatrixRotateY(d.yaw));
            Raylib.DrawModel(model, d.at with { Y = 0.015f }, 1, new Color(235, 235, 235, 255));
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        }
        finally { Lit.AlphaMode(Lit.Opaque); }
    }

    unsafe void DrawDecals()
    {
        Lit.AlphaMode(Lit.Blended);
        try
        {
        if (_decals.Count == 0 || ShadowPass) return;
        if (!_decalReady) { _decalModel = Lit.Make(Raylib.GenMeshPlane(1, 1, 1, 1)); _decalReady = true; }
        foreach (var d in _decals)
        {
            if (Vector3.DistanceSquared(d.at, P.Pos) > 80 * 80) continue;
            _decalModel.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = _assets.Decals[d.tex];
            var model = _decalModel;
            // the plane faces +Y: stand it up (+Y to +Z, texture top up), then turn it to face the street
            model.Transform = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Raymath.MatrixScale(d.w, 1, d.h), Raymath.MatrixRotateX(MathF.PI / 2)), Raymath.MatrixRotateY(d.yaw));
            Raylib.DrawModel(model, d.at, 1, new Color(230, 230, 230, 255));
        }
        }
        finally { Lit.AlphaMode(Lit.Opaque); }
    }
}
