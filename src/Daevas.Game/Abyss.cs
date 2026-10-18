using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// The district floats in AION's Abyss: its streets are the top of an island torn out of Sera, ringed with Gears cliff
/// faces hanging from the edge, with a rock underside that narrows to a point far below. Around it, other islands drift
/// in the violet void (the sky shader and the lit shader's height fog make the void). Replaces the flat plain and the
/// painted skyline that used to show past the district's edge.
static unsafe class Abyss
{
    static Model _top, _under, _unit;
    static bool _built;
    static readonly List<(int kind, Matrix4x4 transform, Vector3 at)> _rim = new();
    static readonly List<(Matrix4x4 transform, Vector3 at)> _islands = new();
    static readonly List<(int kind, Matrix4x4 transform, Vector3 at)> _islandCliffs = new();
    static List<PropModel> _cliffs = new();
    static List<PropModel> _plants = new();
    static Model _soil;
    static bool _soilBuilt;
    /// Trees, bushes and grass: which plant, its transform, where, and its tint (the Abyss's lavender leaves).
    static readonly List<(int plant, Matrix4x4 transform, Vector3 at, Color tint)> _flora = new();
    static Model _crystal;
    static readonly List<Model> _plinths = new();
    static readonly List<Model> _contact = new();
    static Model _rebar;
    /// The torn edge: broken chunks lying along the rim and bent rebar sticking out over the void.
    static readonly List<(Matrix4x4 transform, Vector3 at, bool rebar)> _lip = new();
    /// The torn underside: pipes and cables hanging from below the rim.
    static readonly List<(Matrix4x4 transform, Vector3 at)> _hanging = new();
    /// A ruined citadel on a far island (Gears cliffs stretched into spires), and where the Abyss core glows.
    static readonly List<(int kind, Matrix4x4 transform, Vector3 at)> _citadel = new();
    static readonly Vector3 CitadelAt = new(-520, 40, -610), CoreAt = new(180, -460, -400);
    static readonly List<(Matrix4x4 transform, Vector3 at)> _crystals = new();
    public static float Radius;
    public static bool Ready => _built;

    static float Wave(float a, int seed) =>
        0.55f * MathF.Sin(a * 3 + seed) + 0.3f * MathF.Sin(a * 7.3f + seed * 1.7f) + 0.15f * MathF.Sin(a * 13.1f + seed * 2.3f);

    static float Hash(int i) { var x = (uint) i * 2654435761u; x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; return (x & 0xffff) / 65535f; }

    const int Bins = 96;
    static readonly float[] _profile = new float[Bins];

    /// The island's edge at an angle: just past the district's last buildings that way (ragged).
    static float RimAt(float a)
    {
        var f = (a / MathF.Tau % 1 + 1) % 1 * Bins;
        var i = (int) f; var t = f - i;
        var r = _profile[i % Bins] * (1 - t) + _profile[(i + 1) % Bins] * t;
        return r * (1 + 0.03f * Wave(a, 3));
    }

    /// Measures the district's footprint by angle: how far its buildings reach each way.
    static void Measure(City city)
    {
        var reach = new float[Bins];
        Array.Fill(reach, Sheets.CityPlayHalf * 1.15f + 4);
        // every standing piece's own footprint (measured as the city was built); the chunks only as a fallback
        var measured = city.Reach.Any(r => r > 0);
        for (var i = 0; i < Bins; i++) reach[i] = MathF.Max(reach[i], city.Reach[i]);
        foreach (var c in measured ? [] : city.Chunks)
        {
            if (c.Far || c.Radius > 40) continue; // a batch spread across the district says nothing about its edge
            var d = MathF.Sqrt(c.Centre.X * c.Centre.X + c.Centre.Z * c.Centre.Z);
            var outer = d + c.Radius * 0.8f;
            if (d < 1 || outer < reach[0] * 0.5f) continue;
            var a = MathF.Atan2(c.Centre.Z, c.Centre.X);
            var half = MathF.Atan2(c.Radius, d);
            for (var b = (int) MathF.Floor((a - half) / MathF.Tau * Bins); b <= (int) MathF.Ceiling((a + half) / MathF.Tau * Bins); b++)
            {
                var k = (b % Bins + Bins) % Bins;
                reach[k] = MathF.Max(reach[k], outer);
            }
        }
        // smooth it (a rolling maximum, then an average) and keep a margin past the last walls
        for (var i = 0; i < Bins; i++)
        {
            var m = 0f;
            for (var k = -2; k <= 2; k++) m = MathF.Max(m, reach[(i + k + Bins) % Bins]);
            _profile[i] = m;
        }
        var smooth = new float[Bins];
        for (var i = 0; i < Bins; i++)
        {
            var sum = 0f;
            for (var k = -2; k <= 2; k++) sum += _profile[(i + k + Bins) % Bins];
            smooth[i] = MathF.Min(Sheets.CityDrawRadius + 30, MathF.Max(_profile[i], sum / 5) + 6); // never inside a building's reach
        }
        Array.Copy(smooth, _profile, Bins);
        Radius = _profile.Max();
    }

    /// Builds the island under the district and the islands around it (once; the ground texture follows the district).
    public static void Build(GameAssets assets, Texture2D ground, float groundTile)
    {
        _cliffs = assets.AbyssCliffModels;
        var rock = assets.AbyssRock;
        // each district has its own shape: the island follows it (only the meshes are freed: the shader is shared)
        if (_built) { Raylib.UnloadMesh(_top.Meshes[0]); Raylib.UnloadMesh(_under.Meshes[0]); }
        if (assets.City != null) Measure(assets.City);
        _top = Lit.Make(TopMesh(groundTile));
        _under = Lit.Make(RockMesh(RimAt, 0, 270, 96, 16, 1, Radius));
        if (!_built) _unit = Lit.Make(RockMesh(a => 1 + 0.12f * Wave(a, 9), 1, 1.9f, 40, 8, 7, 1));
        Place();
        _home.Clear(); _falling.Clear();
        if (_soilBuilt) Raylib.UnloadMesh(_soil.Meshes[0]);
        _soil = Lit.Make(SoilMesh());
        _soilBuilt = true;
        _plants = assets.AbyssPlantModels;
        Plant(assets.City);
        foreach (var m in _plinths) Raylib.UnloadMesh(m.Meshes[0]);
        _plinths.Clear();
        if (assets.City != null)
            for (var i = 0; i < assets.City.Plinths.Count; i += 3000)
            {
                var m = Lit.Make(PlinthMesh(assets.City.Plinths, i, Math.Min(3000, assets.City.Plinths.Count - i)));
                m.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = assets.AbyssWall.Id != 0 ? assets.AbyssWall : City.Grime;
                m.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = assets.AbyssWall.Id != 0 ? Color.White : new Color(150, 144, 136, 255);
                _plinths.Add(m);
            }
        foreach (var m in _contact) Raylib.UnloadMesh(m.Meshes[0]);
        _contact.Clear();
        if (assets.City != null) foreach (var mesh in ContactMeshes(assets.City)) _contact.Add(Lit.Make(mesh));
        if (_crystal.MeshCount == 0) _crystal = Lit.Make(CrystalMesh());
        if (_rebar.MeshCount == 0) { _rebar = Lit.Make(RebarMesh()); _rebar.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(70, 52, 44, 255); }
        Lip(assets.City);
        Landmarks();
        Crystals(assets.City);
        _built = true;
        _top.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = ground;
        if (assets.AbyssSoil.Id != 0) _soil.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = assets.AbyssSoil;
        else _soil.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(78, 72, 64, 255);
        foreach (var m in new[] { _under, _unit })
        {
            if (rock.Id != 0) m.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = rock;
            else m.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(92, 88, 96, 255);
        }
        Log.Info($"abyss: island {_profile.Min():0}-{Radius:0} m out, {_rim.Count} cliffs on its rim, {_islands.Count} islands around ({_cliffs.Count} Gears cliff meshes), {_flora.Count} trees, bushes and grass ({_plants.Count} Gears plants), {_crystals.Count} crystal clusters, {assets.City?.Plinths.Count ?? 0} foundations and ground storeys under raised buildings, contact shadows in {_contact.Count} meshes");
    }

    static Matrix4x4 Turn(float sx, float sy, float sz, bool hang, float yaw)
    {
        var t = Raymath.MatrixScale(sx, sy, sz);
        if (hang) t = Raymath.MatrixMultiply(t, Raymath.MatrixRotateX(MathF.PI)); // upside down: it hangs from the edge
        return Raymath.MatrixMultiply(t, Raymath.MatrixRotateY(yaw));
    }

    static void Place()
    {
        _rim.Clear(); _islands.Clear(); _islandCliffs.Clear();
        var kinds = _cliffs.Count;
        if (kinds > 0)
        {
            // cliff faces all round the rim, hanging 35-60 m below the street
            var n = (int) (MathF.Tau * Radius / 14);
            for (var i = 0; i < n; i++)
            {
                var a = (i + Hash(i) * 0.5f) * MathF.Tau / n;
                var r = RimAt(a) - 2.5f;
                var k = (int) (Hash(i + 100) * kinds) % kinds;
                var size = _cliffs[k].Size;
                var across = 19 / MathF.Max(1, MathF.Max(size.X, size.Z));
                var drop = (35 + 25 * Hash(i + 200)) / MathF.Max(1, size.Y);
                _rim.Add((k, Turn(across, drop, across, true, Hash(i + 300) * MathF.Tau), new Vector3(MathF.Cos(a) * r, -0.6f, MathF.Sin(a) * r))); // tops tucked under the street
            }
        }
        // islands drifting in the void: above and below, near and far
        for (var i = 0; i < 26; i++)
        {
            var a = Hash(i + 500) * MathF.Tau;
            var d = 300 + 560 * Hash(i + 600);
            var y = -230 + 360 * Hash(i + 700);
            var size = IslandSize(i);
            var at = new Vector3(MathF.Cos(a) * d, y, MathF.Sin(a) * d);
            _islands.Add((Turn(size, size, size, false, Hash(i + 1000) * MathF.Tau), at));
            if (kinds == 0) continue;
            for (var c = 0; c < 1 + (int) (Hash(i + 1100) * 2.5f); c++)
            {
                var k = (int) (Hash(i * 7 + c + 1200) * kinds) % kinds;
                var cs = _cliffs[k].Size;
                var s = size * (0.45f + 0.3f * Hash(i * 3 + c + 1300)) / MathF.Max(1, MathF.Max(cs.X, cs.Z));
                var off = new Vector3((Hash(i * 5 + c + 1400) - 0.5f) * size * 0.8f, -size * 0.05f, (Hash(i * 5 + c + 1500) - 0.5f) * size * 0.8f);
                _islandCliffs.Add((k, Turn(s, s * (0.8f + 0.6f * Hash(i + c + 1600)), s, false, Hash(i * 11 + c) * MathF.Tau), at + off));
            }
        }
    }

    // ---------------------------------------------------------------- meshes

    /// The street on top: a ragged disc, the district's ground texture tiled across it.
    static Mesh TopMesh(float tile)
    {
        const int M = 96;
        var mesh = new Mesh(M + 1, M);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
        var pos = mesh.VerticesAs<Vector3>(); var nrm = mesh.NormalsAs<Vector3>(); var uv = mesh.TexCoordsAs<Vector2>(); var idx = mesh.IndicesAs<ushort>();
        pos[0] = Vector3.Zero;
        for (var j = 0; j < M; j++)
        {
            var a = j * MathF.Tau / M;
            pos[j + 1] = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * RimAt(a);
        }
        for (var i = 0; i <= M; i++) { nrm[i] = Vector3.UnitY; uv[i] = new Vector2(pos[i].X, pos[i].Z) / tile; }
        for (var j = 0; j < M; j++) Tri(pos, idx, j, 0, j + 1, (j + 1) % M + 1, Vector3.UnitY);
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// A rock body under a rim (radius by angle), from `top` down `depth`, narrowing to a ragged point; with a rocky
    /// cap on top when `cap` is 1 (the floating islands). `scale` sets the texture's tiling (metres per repeat 40).
    static Mesh RockMesh(Func<float, float> rim, int cap, float depth, int M, int K, int seed, float scale)
    {
        var verts = (K + 1) * M + 1 + (cap == 1 ? 1 : 0);
        var tris = K * M * 2 - M + (cap == 1 ? M : 0);
        var mesh = new Mesh(verts, tris);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
        var pos = mesh.VerticesAs<Vector3>(); var nrm = mesh.NormalsAs<Vector3>(); var uv = mesh.TexCoordsAs<Vector2>(); var idx = mesh.IndicesAs<ushort>();
        var tile = 40 / MathF.Max(0.01f, scale);
        for (var k = 0; k <= K; k++)
        {
            var t = k / (float) K;
            for (var j = 0; j < M; j++)
            {
                var a = j * MathF.Tau / M;
                var jag = 1 + (k == 0 ? 0 : 0.16f * (Hash(seed * 1000 + k * M + j) - 0.5f) + 0.1f * Wave(a * 2 + k, seed));
                var r = rim(a) * MathF.Pow(1 - t, 0.75f) * 0.98f * jag;
                var y = -depth * (t + 0.04f * (k == 0 ? 0 : Hash(seed * 77 + k * M + j)));
                pos[k * M + j] = new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
                nrm[k * M + j] = Vector3.Normalize(new Vector3(MathF.Cos(a), -0.45f, MathF.Sin(a)));
                uv[k * M + j] = new Vector2(a * rim(a) / tile, y / tile);
            }
        }
        var tip = (K + 1) * M;
        pos[tip] = new Vector3(0, -depth * 1.04f, 0); nrm[tip] = -Vector3.UnitY; uv[tip] = new Vector2(0, -depth / tile);
        var n = 0;
        for (var k = 0; k < K - 1; k++)
            for (var j = 0; j < M; j++)
            {
                int a = k * M + j, b = k * M + (j + 1) % M, c = (k + 1) * M + (j + 1) % M, d = (k + 1) * M + j;
                var outward = new Vector3(pos[a].X, 0, pos[a].Z);
                Tri(pos, idx, n++, a, b, c, outward);
                Tri(pos, idx, n++, a, c, d, outward);
            }
        for (var j = 0; j < M; j++) Tri(pos, idx, n++, (K - 1) * M + j, (K - 1) * M + (j + 1) % M, tip, -Vector3.UnitY);
        if (cap == 1)
        {
            var centre = tip + 1;
            pos[centre] = Vector3.Zero; nrm[centre] = Vector3.UnitY; uv[centre] = Vector2.Zero;
            for (var j = 0; j < M; j++) Tri(pos, idx, n++, centre, j, (j + 1) % M, Vector3.UnitY);
        }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// Rocky soil along the rim, fading in over the street (blended by vertex alpha): the ground the plants grow in.
    static Mesh SoilMesh()
    {
        const int M = 96;
        float[] inset = [18, 8, 0.6f]; byte[] alpha = [0, 255, 255];
        var mesh = new Mesh(M * 3, M * 4);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocColors(); mesh.AllocIndices();
        var pos = mesh.VerticesAs<Vector3>(); var nrm = mesh.NormalsAs<Vector3>(); var uv = mesh.TexCoordsAs<Vector2>(); var col = mesh.ColorsAs<Color>(); var idx = mesh.IndicesAs<ushort>();
        for (var k = 0; k < 3; k++)
            for (var j = 0; j < M; j++)
            {
                var a = j * MathF.Tau / M;
                var r = RimAt(a) - inset[k] * (0.8f + 0.4f * Hash(j * 3 + k));
                var i = k * M + j;
                pos[i] = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
                nrm[i] = Vector3.UnitY; uv[i] = new Vector2(pos[i].X, pos[i].Z) / 9f;
                col[i] = new Color((byte) 255, (byte) 255, (byte) 255, alpha[k]);
            }
        var n = 0;
        for (var k = 0; k < 2; k++)
            for (var j = 0; j < M; j++)
            {
                int a = k * M + j, b = k * M + (j + 1) % M, c = (k + 1) * M + (j + 1) % M, d = (k + 1) * M + j;
                Tri(pos, idx, n++, a, b, c, Vector3.UnitY);
                Tri(pos, idx, n++, a, c, d, Vector3.UnitY);
            }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// Plants on the island's free ground near the rim (never in a building, never on the battlefield) and on the
    /// floating islands. Trees are Gears spruces, pines and hornbeams; leaves take the Abyss's lavender light.
    static void Plant(City? city)
    {
        _flora.Clear();
        if (_plants.Count == 0) return;
        int[] Kind(string role) => Enumerable.Range(0, _plants.Count).Where(i => _plants[i].Def.Role == role).ToArray();
        var trees = Kind("tree"); var small = Kind("small"); var bushes = Kind("bush"); var grass = Kind("grass");
        var leaf = new Color(232, 226, 240, 255); var blade = new Color(150, 156, 128, 255); var dead = new Color(176, 164, 186, 255);
        void Add(int[] kinds, int seed, Vector3 at, float height, Color tint)
        {
            if (kinds.Length == 0) return;
            var p = kinds[(int) (Hash(seed) * kinds.Length) % kinds.Length];
            var s = height / MathF.Max(0.2f, _plants[p].Size.Y);
            _flora.Add((p, Turn(s, s, s, false, Hash(seed + 1) * MathF.Tau), at, tint));
        }
        var half = Sheets.CityPlayHalf + 4;
        for (var i = 0; i < 900 && _flora.Count < 160; i++)
        {
            var a = Hash(i * 3 + 7000) * MathF.Tau;
            var r = RimAt(a) - (1.5f + 13 * Hash(i * 3 + 7001));
            var at = new Vector3(MathF.Cos(a) * r, -0.03f, MathF.Sin(a) * r);
            if (MathF.Abs(at.X) < half && MathF.Abs(at.Z) < half) continue;          // the battlefield stays clear
            var roll = Hash(i * 3 + 7002);
            var room = roll < 0.3f ? 3.5f : roll < 0.45f ? 2.5f : 1.2f;
            if (city != null && city.OccupiedNear(at.X, at.Z, room)) continue;       // never inside a building
            if (roll < 0.3f) Add(trees, i + 7100, at, 13 + 8 * Hash(i + 7200), leaf);
            else if (roll < 0.45f) Add(small, i + 7100, at, 7 + 5 * Hash(i + 7200), dead);
            else if (roll < 0.75f) Add(bushes, i + 7100, at, 1.2f + 1.4f * Hash(i + 7200), dead);
            else Add(grass, i + 7100, at, 0.5f + 0.5f * Hash(i + 7200), blade);
        }
        // the floating islands: a few trees and grass on each top
        for (var k = 0; k < _islands.Count; k++)
        {
            var (_, at) = _islands[k];
            var size = IslandSize(k);
            for (var j = 0; j < 2 + (int) (Hash(k + 8000) * 4); j++)
            {
                var a = Hash(k * 13 + j + 8100) * MathF.Tau; var r = size * 0.65f * MathF.Sqrt(Hash(k * 13 + j + 8200));
                var spot = at + new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
                if (j % 3 == 2) Add(grass, k * 31 + j, spot, 0.6f * MathF.Max(1, size / 12), blade);
                else if (Hash(k + j + 8300) < 0.7f) Add(trees, k * 31 + j, spot, MathF.Min(22, size * (0.35f + 0.3f * Hash(k * 7 + j))), leaf);
                else Add(small, k * 31 + j, spot, MathF.Min(14, size * 0.3f), dead);
            }
        }
    }

    /// An Abyss crystal cluster: a few tall six-sided shards leaning out from one root, deep violet at the base and
    /// pale lavender to teal at the tips (vertex colours; drawn glowing).
    static Mesh CrystalMesh()
    {
        const int Shards = 6, Sides = 6;
        var verts = Shards * (Sides * 2 + 1); var tris = Shards * Sides * 3;
        var mesh = new Mesh(verts, tris);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocColors(); mesh.AllocIndices();
        var pos = mesh.VerticesAs<Vector3>(); var nrm = mesh.NormalsAs<Vector3>(); var uv = mesh.TexCoordsAs<Vector2>(); var col = mesh.ColorsAs<Color>(); var idx = mesh.IndicesAs<ushort>();
        int v = 0, n = 0;
        for (var s = 0; s < Shards; s++)
        {
            var lean = s == 0 ? 0 : 0.25f + 0.45f * Hash(s + 40);
            var turn = s * MathF.Tau / (Shards - 1) + Hash(s + 50);
            var axis = Vector3.Normalize(new Vector3(MathF.Cos(turn) * lean, 1, MathF.Sin(turn) * lean));
            var side = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitZ)); var side2 = Vector3.Cross(axis, side);
            var len = s == 0 ? 1 : 0.45f + 0.4f * Hash(s + 60); var rad = (s == 0 ? 0.16f : 0.1f) * (0.8f + 0.4f * Hash(s + 70));
            var root = v;
            for (var k = 0; k < Sides; k++)
            {
                var a = k * MathF.Tau / Sides;
                var d = side * MathF.Cos(a) + side2 * MathF.Sin(a);
                pos[v] = d * rad * 0.7f - axis * 0.05f; pos[v + Sides] = axis * len * 0.82f + d * rad;
                nrm[v] = nrm[v + Sides] = d;
                col[v] = new Color((byte) 70, (byte) 34, (byte) 120, (byte) 255); col[v + Sides] = new Color((byte) 196, (byte) 160, (byte) 255, (byte) 255);
                uv[v] = uv[v + Sides] = Vector2.Zero;
                v++;
            }
            v += Sides;
            var tip = v++;
            pos[tip] = axis * len; nrm[tip] = axis; col[tip] = new Color((byte) 170, (byte) 250, (byte) 255, (byte) 255); uv[tip] = Vector2.Zero;
            for (var k = 0; k < Sides; k++)
            {
                int a0 = root + k, a1 = root + (k + 1) % Sides, b0 = a0 + Sides, b1 = a1 + Sides;
                var outward = pos[a0] + pos[a1];
                Tri(pos, idx, n++, a0, a1, b1, outward); Tri(pos, idx, n++, a0, b1, b0, outward);
                Tri(pos, idx, n++, b0, b1, tip, outward + axis);
            }
        }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// Crystal clusters along the rim, hanging from the cliffs below it, and on the floating islands.
    static void Crystals(City? city)
    {
        _crystals.Clear();
        var half = Sheets.CityPlayHalf + 4;
        for (var i = 0; i < 600 && _crystals.Count < 46; i++)
        {
            var a = Hash(i * 5 + 9000) * MathF.Tau;
            var r = RimAt(a) - (1 + 9 * Hash(i * 5 + 9001));
            var at = new Vector3(MathF.Cos(a) * r, -0.2f, MathF.Sin(a) * r);
            if (MathF.Abs(at.X) < half && MathF.Abs(at.Z) < half) continue;
            if (city != null && city.OccupiedNear(at.X, at.Z, 2)) continue;
            var s = 1.6f + 3.4f * Hash(i * 5 + 9002) * Hash(i * 5 + 9003);
            _crystals.Add((Turn(s, s, s, false, Hash(i + 9100) * MathF.Tau), at));
        }
        for (var i = 0; i < 24; i++)
        {
            // under the rim, pointing down into the void
            var a = Hash(i + 9200) * MathF.Tau; var r = RimAt(a) - 3;
            var s = 3 + 5 * Hash(i + 9300);
            _crystals.Add((Turn(s, s, s, true, Hash(i + 9400) * MathF.Tau), new Vector3(MathF.Cos(a) * r, -8 - 25 * Hash(i + 9500), MathF.Sin(a) * r)));
        }
        for (var k = 0; k < _islands.Count; k++)
        {
            var (_, at) = _islands[k]; var size = IslandSize(k);
            var s = size * (0.12f + 0.12f * Hash(k + 9600));
            _crystals.Add((Turn(s, s, s, false, Hash(k + 9700) * MathF.Tau), at + new Vector3((Hash(k + 9800) - 0.5f) * size, 0, (Hash(k + 9900) - 0.5f) * size)));
        }
    }

    /// Boxes (four sides and a top) for the foundations, textured like the street around them.
    static Mesh PlinthMesh(List<BoundingBox> boxes, int first, int count)
    {
        var pos = new Vector3[count * 20]; var nrm = new Vector3[count * 20]; var uv = new Vector2[count * 20]; var idx = new ushort[count * 30];
        int v = 0, n = 0;
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool top)
        {
            var o = v;
            foreach (var q in new[] { a, b, c, d })
            {
                pos[v] = q; nrm[v] = normal;
                uv[v] = top ? new Vector2(q.X, q.Z) / 4f : new Vector2((normal.X != 0 ? q.Z : q.X) / 4f, -q.Y / 4f);
                v++;
            }
            Tri(pos, idx, n++, o, o + 1, o + 2, normal); Tri(pos, idx, n++, o, o + 2, o + 3, normal);
        }
        for (var i = first; i < first + count; i++)
        {
            var (lo, hi) = (boxes[i].Min, boxes[i].Max);
            Face(new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z), -Vector3.UnitZ, false);
            Face(new(lo.X, lo.Y, hi.Z), new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z), Vector3.UnitZ, false);
            Face(new(lo.X, lo.Y, lo.Z), new(lo.X, lo.Y, hi.Z), new(lo.X, hi.Y, hi.Z), new(lo.X, hi.Y, lo.Z), -Vector3.UnitX, false);
            Face(new(hi.X, lo.Y, lo.Z), new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(hi.X, hi.Y, lo.Z), Vector3.UnitX, false);
            Face(new(lo.X, hi.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z), Vector3.UnitY, true);
        }
        var mesh = new Mesh(count * 20, count * 10);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
        pos.CopyTo(mesh.VerticesAs<Vector3>()); nrm.CopyTo(mesh.NormalsAs<Vector3>()); uv.CopyTo(mesh.TexCoordsAs<Vector2>()); idx.CopyTo(mesh.IndicesAs<ushort>());
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// Contact shadows: a soft dark band on the ground along the foot of every building (where the occupied ground
    /// meets the open), so walls sit on the street instead of seeming to hover over a bright sidewalk.
    static List<Mesh> ContactMeshes(City city)
    {
        var quads = new List<(Vector3 a, Vector3 b, Vector3 outward)>();
        foreach (var (x, z) in city.Occupied)
        {
            void Edge(int dx, int dz)
            {
                if (city.Occupied.Contains((x + dx, z + dz))) return;
                // the cell edge facing the open neighbour, its two ends
                var c = new Vector3(x * 2 + 1, 0, z * 2 + 1);
                var o = new Vector3(dx, 0, dz);
                var along = new Vector3(-dz, 0, dx);
                quads.Add((c + o - along, c + o + along, o));
            }
            Edge(1, 0); Edge(-1, 0); Edge(0, 1); Edge(0, -1);
        }
        // kerbs and steps in the street itself: a soft shade on the lower side of every rise
        var reach = (int) (Sheets.CityPlayHalf + 20);
        for (var x = -reach; x < reach; x++)
            for (var z = -reach; z < reach; z++)
            {
                var h = city.GroundAt(x + 0.5f, z + 0.5f);
                foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
                {
                    var h2 = city.GroundAt(x + 0.5f + dx, z + 0.5f + dz);
                    if (MathF.Abs(h2 - h) < 0.08f || MathF.Abs(h2 - h) > 1.2f) continue;
                    // the edge between the two cells, the shade spreading over the lower one
                    var low = h2 < h ? 1 : -1;
                    var mid = new Vector3(x + 0.5f + dx * 0.5f, 0, z + 0.5f + dz * 0.5f);
                    var o = new Vector3(dx * low, 0, dz * low) * 0.5f;
                    var along = new Vector3(-dz, 0, dx) * 0.5f;
                    quads.Add((mid - along, mid + along, o * 2));
                }
            }
        var meshes = new List<Mesh>();
        const int PerMesh = 14000;
        for (var first = 0; first < quads.Count; first += PerMesh)
        {
            var count = Math.Min(PerMesh, quads.Count - first);
            var pos = new Vector3[count * 4]; var nrm = new Vector3[count * 4]; var uv = new Vector2[count * 4]; var col = new Color[count * 4]; var idx = new ushort[count * 6];
            for (var q = 0; q < count; q++)
            {
                var (a, b, o) = quads[first + q];
                var v = q * 4;
                Vector3 At(Vector3 p, float lift) => p with { Y = city.GroundAt(p.X, p.Z) + lift };
                pos[v] = At(a, 0.05f); pos[v + 1] = At(b, 0.05f); pos[v + 2] = At(b + o * 1.6f, 0.05f); pos[v + 3] = At(a + o * 1.6f, 0.05f);
                for (var k = 0; k < 4; k++) { nrm[v + k] = Vector3.UnitY; uv[v + k] = Vector2.Zero; }
                col[v] = col[v + 1] = new Color((byte) 0, (byte) 0, (byte) 0, (byte) 150);
                col[v + 2] = col[v + 3] = new Color((byte) 0, (byte) 0, (byte) 0, (byte) 0);
                Tri(pos, idx, q * 2, v, v + 1, v + 2, Vector3.UnitY); Tri(pos, idx, q * 2 + 1, v, v + 2, v + 3, Vector3.UnitY);
            }
            var mesh = new Mesh(count * 4, count * 2);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocColors(); mesh.AllocIndices();
            pos.CopyTo(mesh.VerticesAs<Vector3>()); nrm.CopyTo(mesh.NormalsAs<Vector3>()); uv.CopyTo(mesh.TexCoordsAs<Vector2>()); col.CopyTo(mesh.ColorsAs<Color>()); idx.CopyTo(mesh.IndicesAs<ushort>());
            Raylib.UploadMesh(ref mesh, false);
            meshes.Add(mesh);
        }
        return meshes;
    }

    /// A tuft of bent reinforcing bars: a few thin rods from one root, kinked partway along.
    static Mesh RebarMesh()
    {
        const int Rods = 4, Segs = 3;
        var quads = Rods * Segs * 4;
        var pos = new Vector3[quads * 4]; var nrm = new Vector3[quads * 4]; var uv = new Vector2[quads * 4]; var idx = new ushort[quads * 6];
        int v = 0, n = 0;
        for (var r = 0; r < Rods; r++)
        {
            var dir = Vector3.Normalize(new Vector3(1, 0.15f + 0.35f * Hash(r + 30000), (Hash(r + 30100) - 0.5f) * 0.8f));
            var bend = Vector3.Normalize(new Vector3(0.3f, -1, (Hash(r + 30200) - 0.5f)));
            var p0 = new Vector3(0, 0, (r - Rods / 2f) * 0.18f);
            for (var g = 0; g < Segs; g++)
            {
                var d = Vector3.Normalize(Vector3.Lerp(dir, bend, g / (float) Segs * (0.4f + 0.5f * Hash(r + 30300))));
                var p1 = p0 + d * (0.5f + 0.3f * Hash(r * 7 + g + 30400));
                var side = Vector3.Normalize(Vector3.Cross(d, Vector3.UnitY)) * 0.025f; var up = Vector3.Normalize(Vector3.Cross(side, d)) * 0.025f;
                foreach (var o in new[] { side, up, -side, -up })
                {
                    var o2 = o == side ? up : o == up ? -side : o == -side ? -up : side;
                    pos[v] = p0 + o; pos[v + 1] = p1 + o; pos[v + 2] = p1 + o2; pos[v + 3] = p0 + o2;
                    var nn = Vector3.Normalize(o + o2);
                    for (var k = 0; k < 4; k++) { nrm[v + k] = nn; uv[v + k] = Vector2.Zero; }
                    Tri(pos, idx, n++, v, v + 1, v + 2, nn); Tri(pos, idx, n++, v, v + 2, v + 3, nn);
                    v += 4;
                }
                p0 = p1;
            }
        }
        var mesh = new Mesh(quads * 4, quads * 2);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
        pos.CopyTo(mesh.VerticesAs<Vector3>()); nrm.CopyTo(mesh.NormalsAs<Vector3>()); uv.CopyTo(mesh.TexCoordsAs<Vector2>()); idx.CopyTo(mesh.IndicesAs<ushort>());
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// Broken chunks along the rim (flattened rocks) and rebar jutting out over the edge, every few metres.
    static void Lip(City? city)
    {
        _lip.Clear();
        var n = (int) (MathF.Tau * Radius / 3.2f);
        for (var i = 0; i < n; i++)
        {
            var a = (i + Hash(i + 31000) * 0.6f) * MathF.Tau / n;
            var r = RimAt(a);
            var outward = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            if (Hash(i + 31100) < 0.28f)
            {
                // rebar where a slab sheared off: rooted just inside the edge, pointing out over the void
                var at = outward * (r - 0.3f) + Vector3.UnitY * -0.15f;
                var yaw = -a + (Hash(i + 31200) - 0.5f) * 0.6f;
                _lip.Add((Raymath.MatrixMultiply(Raymath.MatrixScale(1.3f, 1.3f, 1.3f), Raymath.MatrixRotateY(yaw)), at, true));
            }
            else
            {
                var at = outward * (r - 0.8f - 2.5f * Hash(i + 31300)) + Vector3.UnitY * -0.1f;
                if (city != null && city.OccupiedNear(at.X, at.Z, 0.8f)) continue;
                var sz = 0.5f + 1.1f * Hash(i + 31400);
                _lip.Add((Raymath.MatrixMultiply(Raymath.MatrixScale(sz * 1.4f, sz * 0.35f, sz), Raymath.MatrixRotateY(Hash(i + 31500) * MathF.Tau)), at, false));
            }
        }
    }

    /// Torn pipes and cables hanging under the rim, and the far landmarks of the Abyss.
    static void Landmarks()
    {
        _hanging.Clear(); _citadel.Clear();
        for (var i = 0; i < 40; i++)
        {
            var a = Hash(i + 32000) * MathF.Tau;
            var r = RimAt(a) - 6 - 20 * Hash(i + 32100);
            var len = 4 + 10 * Hash(i + 32200);
            // the rebar tuft turned to hang straight down, stretched long
            var t = Raymath.MatrixMultiply(Raymath.MatrixScale(len, 1.6f, 1.6f), Raymath.MatrixRotateZ(-MathF.PI / 2 + (Hash(i + 32300) - 0.5f) * 0.3f));
            t = Raymath.MatrixMultiply(t, Raymath.MatrixRotateY(Hash(i + 32400) * MathF.Tau));
            _hanging.Add((t, new Vector3(MathF.Cos(a) * r, -18 - 30 * Hash(i + 32500) * (1 - (Radius - r) / Radius), MathF.Sin(a) * r)));
        }
        if (_cliffs.Count == 0) return;
        for (var i = 0; i < 7; i++)
        {
            var k = (int) (Hash(i + 33000) * _cliffs.Count) % _cliffs.Count;
            var size = _cliffs[k].Size;
            var across = (18 + 14 * Hash(i + 33100)) / MathF.Max(1, MathF.Max(size.X, size.Z));
            var tall = (60 + 70 * Hash(i + 33200)) / MathF.Max(1, size.Y);
            var off = new Vector3((Hash(i + 33300) - 0.5f) * 70, 0, (Hash(i + 33400) - 0.5f) * 70);
            _citadel.Add((k, Turn(across, tall, across, false, Hash(i + 33500) * MathF.Tau), CitadelAt + off));
        }
    }

    static float IslandSize(int i) => 14 + 55 * Hash(i + 800) * Hash(i + 900);

    /// Writes triangle `n`, wound so its front faces `outward` (the lit shader lights front faces as they are).
    static void Tri(Span<Vector3> pos, Span<ushort> idx, int n, int a, int b, int c, Vector3 outward)
    {
        var face = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
        if (Vector3.Dot(face, outward) < 0) (b, c) = (c, b);
        idx[n * 3] = (ushort) a; idx[n * 3 + 1] = (ushort) b; idx[n * 3 + 2] = (ushort) c;
    }

    // ---------------------------------------------------------------- drawing

    static float _time;

    /// Where a floating island has drifted to (it bobs and sways slowly); things on it go with it.
    static Vector3 Drift(int island) =>
        new(MathF.Sin(_time * 0.05f + island * 2.1f) * 3, MathF.Sin(_time * 0.11f + island * 1.3f) * 2.5f, MathF.Cos(_time * 0.04f + island * 0.7f) * 3);

    /// The island a thing out in the void stands on (or -1: the district's own island).
    static int IslandOf(Vector3 at)
    {
        if (at.X * at.X + at.Z * at.Z < (Radius + 40) * (Radius + 40) && at.Y > -60) return -1;
        var best = -1; var bestD = float.MaxValue;
        for (var k = 0; k < _islands.Count; k++)
        {
            var d = Vector3.DistanceSquared(_islands[k].at, at);
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    static readonly Dictionary<Vector3, int> _home = new();
    static Vector3 Moved(Vector3 at)
    {
        if (!_home.TryGetValue(at, out var k)) _home[at] = k = IslandOf(at);
        return k < 0 ? at : at + Drift(k);
    }

    // ---------------------------------------------------------------- motes, crumbling rim

    /// Chunks of the rim breaking off and tumbling into the void: where, when, how big, spin.
    static readonly List<(Vector3 start, float born, float size, float spin)> _falling = new();
    static float _nextChunk = 3;

    /// Once a frame: the clock for the drift, motes and falling chunks (`near` = the hero, so chunks fall where seen).
    public static void Update(float dt, Vector3 near)
    {
        if (!_built) return;
        _time += dt;
        if ((_nextChunk -= dt) <= 0)
        {
            _nextChunk = 2.5f + 5 * Hash((int) (_time * 10));
            var a = MathF.Atan2(near.Z, near.X) + (Hash((int) (_time * 13)) - 0.5f) * 1.6f;
            var r = RimAt(a) - 1;
            _falling.Add((new Vector3(MathF.Cos(a) * r, -0.5f, MathF.Sin(a) * r), _time, 0.6f + 1.6f * Hash((int) (_time * 17)), Hash((int) (_time * 19)) * 4 - 2));
        }
        _falling.RemoveAll(f => _time - f.born > 9);
    }

    /// Aether motes rising out of the void past the rim, and the falling chunks' dust (glowing, after the scene).
    public static void DrawGlows()
    {
        if (!_built) return;
        // the Abyss core: a vast violet glow deep in the void
        var pulse = 0.85f + 0.15f * MathF.Sin(_time * 0.4f);
        Glow.Sprite(CoreAt, 200 * pulse, new Color((byte) 150, (byte) 70, (byte) 240, (byte) 140));
        Glow.Sprite(CoreAt, 80 * pulse, new Color((byte) 230, (byte) 190, (byte) 255, (byte) 200));
        for (var i = 0; i < 140; i++)
        {
            var a = Hash(i + 20000) * MathF.Tau;
            var r = RimAt(a) + 4 + 30 * Hash(i + 20100);
            var cycle = 18 + 10 * Hash(i + 20200);
            var t = ((_time + Hash(i + 20300) * cycle) % cycle) / cycle;           // 0..1 through its rise
            var at = new Vector3(MathF.Cos(a) * r + MathF.Sin(_time * 0.3f + i) * 2, -70 + 110 * t, MathF.Sin(a) * r + MathF.Cos(_time * 0.27f + i) * 2);
            var fade = MathF.Sin(t * MathF.PI);
            var teal = Hash(i + 20400) < 0.35f;
            Glow.Sprite(at, 0.9f + 1.2f * Hash(i + 20500), teal ? new Color((byte) 120, (byte) 230, (byte) 240, (byte) (150 * fade)) : new Color((byte) 200, (byte) 150, (byte) 255, (byte) (170 * fade)));
        }
        foreach (var (start, born, size, _) in _falling)
        {
            var t = _time - born;
            var at = start + new Vector3(0, -4.9f * t * t, 0) + Vector3.Normalize(start with { Y = 0 }) * t * 1.2f;
            Glow.Sprite(at + Vector3.UnitY * size, size * 2.2f, new Color((byte) 150, (byte) 130, (byte) 120, (byte) Math.Clamp(90 - t * 10, 0, 90)));
        }
    }

    public static void Draw()
    {
        if (!_built) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Lit.AlphaMode(Lit.Opaque);
        Raylib.DrawModel(_top, new Vector3(0, -0.04f, 0), 1, Color.White);
        Raylib.DrawModel(_under, new Vector3(0, -0.3f, 0), 1, Color.White);
        foreach (var m in _plinths) Raylib.DrawModel(m, Vector3.Zero, 1, Color.White);
        foreach (var (kind, transform, at) in _rim) DrawCliff(kind, transform, at);
        foreach (var (transform, at, rebar) in _lip) { var m = rebar ? _rebar : _unit; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White); }
        foreach (var (transform, at) in _hanging) { var m = _rebar; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White); }
        { var m = _unit; m.Transform = Turn(95, 120, 95, false, 0.4f); Raylib.DrawModel(m, CitadelAt, 1, Color.White); }
        foreach (var (kind, transform, at) in _citadel) DrawCliff(kind, transform, at);
        for (var k = 0; k < _islands.Count; k++) { var (transform, at) = _islands[k]; var m = _unit; m.Transform = transform; Raylib.DrawModel(m, at + Drift(k), 1, Color.White); }
        foreach (var (kind, transform, at) in _islandCliffs) DrawCliff(kind, transform, Moved(at));
        // the chunks breaking off the rim
        foreach (var (start, born, size, spin) in _falling)
        {
            var t = _time - born;
            var at = start + new Vector3(0, -4.9f * t * t, 0) + Vector3.Normalize(start with { Y = 0 }) * t * 1.2f;
            var m = _unit;
            m.Transform = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Raymath.MatrixScale(size, size, size), Raymath.MatrixRotateX(spin * t)), Raymath.MatrixRotateY(spin * 0.7f * t));
            Raylib.DrawModel(m, at, 1, Color.White);
        }
        Lit.AlphaMode(Lit.Blended);
        Raylib.DrawModel(_soil, new Vector3(0, -0.02f, 0), 1, Color.White);
        Rlgl.DisableDepthMask(); // flat on the street, like the ground decals
        foreach (var m in _contact) Raylib.DrawModel(m, Vector3.Zero, 1, Color.White);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        foreach (var (plant, transform, at, tint) in _flora)
        {
            var p = _plants[plant];
            for (var k = 0; k < p.Models.Length; k++)
            {
                Lit.AlphaMode(k < p.Alpha.Length ? p.Alpha[k] : Lit.Opaque); // leaves and grass blades cut out
                var m = p.Models[k]; m.Transform = transform; Raylib.DrawModel(m, Moved(at), 1, tint);
            }
        }
        Lit.AlphaMode(Lit.Glow);
        foreach (var (transform, at) in _crystals) { var m = _crystal; m.Transform = transform; Raylib.DrawModel(m, Moved(at), 1, Color.White); }
        Lit.AlphaMode(Lit.Opaque);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }

    /// One of the Abyss crystal clusters anywhere (the aether shards on the rooftops).
    public static void DrawCrystal(Vector3 at, float scale, float spin)
    {
        if (_crystal.MeshCount == 0) return;
        Rlgl.DrawRenderBatchActive();
        Lit.AlphaMode(Lit.Glow);
        var m = _crystal; m.Transform = Turn(scale, scale, scale, false, spin);
        Raylib.DrawModel(m, at, 1, Color.White);
        Lit.AlphaMode(Lit.Opaque);
    }

    static void DrawCliff(int kind, Matrix4x4 transform, Vector3 at)
    {
        if (kind >= _cliffs.Count) return;
        foreach (var model in _cliffs[kind].Models) { var m = model; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White); }
    }
}
