using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Daevas.Game.Data;
using Daevas.Gears;
using Raylib_cs;

namespace Daevas.Game;

/// A Gears of War: E-Day map read from the player's install: every static mesh its World Partition cells
/// place (FastGeo components and instances), baked into per-texture chunks in game space, with colliders.
sealed class CityData
{
    public readonly List<CityBatch> Batches = new();
    public readonly Dictionary<string, GearsTexture> Textures = new();   // small first: the full size streams in after
    public readonly List<BoundingBox> Colliders = new();
    public readonly List<BoundingBox> Mounds = new();
    /// Every piece up to roof height near the play area: the camera keeps out of these (walking only uses Colliders).
    public readonly List<BoundingBox> CameraBoxes = new();
    /// The low triangles of big buildings (too big for a box): the camera ray tests these, found by 4 m cell.
    public readonly Dictionary<(int, int), List<(Vector3 a, Vector3 b, Vector3 c)>> CameraTris = new();
    public int Placements, Meshes, Triangles;
    public float[]? Ground; public int GroundN; public float GroundHalf;
    public readonly List<Vector3> Shopfronts = new();
    /// How far the district's standing pieces reach, by angle (Abyss.Bins), and the 2 m ground cells they stand on.
    public readonly float[] Reach = new float[96];
    public readonly HashSet<(int, int)> Occupied = new();
    /// Foundations under raised ground floors: filled down into the ground so no gap shows under a building.
    public readonly List<BoundingBox> Plinths = new();
    public int SlabPlinths, BlockPlinths, ColumnPlinths;
    /// Stairs and ramps: walked up as slopes (which way they rise, from the mesh).
    public readonly List<Ramp> Ramps = new();
}

/// A flight of stairs or a ramp: its box, and the side it rises toward (+X, -X, +Z or -Z).
public readonly record struct Ramp(BoundingBox Box, int Axis, int Sign)
{
    /// The walking height at a point inside it (or float.MinValue outside).
    public float HeightAt(Vector3 p)
    {
        if (p.X < Box.Min.X || p.X > Box.Max.X || p.Z < Box.Min.Z || p.Z > Box.Max.Z) return float.MinValue;
        var t = Axis == 0 ? (p.X - Box.Min.X) / MathF.Max(0.01f, Box.Max.X - Box.Min.X) : (p.Z - Box.Min.Z) / MathF.Max(0.01f, Box.Max.Z - Box.Min.Z);
        if (Sign < 0) t = 1 - t;
        return Box.Min.Y + (Box.Max.Y - Box.Min.Y) * Math.Clamp(t, 0, 1);
    }
}

sealed class CityBatch
{
    public string? Texture;
    public bool Window; // untextured window interiors are drawn dark
    public bool Glass;  // shop windows and panes: dark tinted glass, so the empty buildings behind don't show
    public readonly List<float> Pos = new(), Nrm = new(), Uv = new();
    public readonly List<ushort> Idx = new();
    public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
    public int Verts => Pos.Count / 3;
}

sealed class CityChunk
{
    public Model Model;
    public Vector3 Centre;
    public float Radius;
    public bool Far;   // the distant ring, streamed in after the start
    public int Alpha;  // Lit.Opaque, Cutout or Blended (by the texture's name)
}

/// A Siege district to fight in: which World Partition cells to read, where its centre is and its street height.
sealed record CityMap(string Id, string Name, string Map, string[] Cells, float CenterX, float CenterY, float GroundZ, string Unlock);

sealed class City
{
    /// Texture streaming: the city starts with textures this small, and the full size streams in, nearest first.
    public const int StreamStart = 128;
    /// Each texture path's GPU texture and the chunks that use it (for swapping in the full size).
    public readonly Dictionary<string, (Texture2D tex, List<int> chunks)> TextureUse = new();
    public int Streamed;

    /// Swaps in a texture's full-size version (main thread) and frees the small one.
    public unsafe void Upgrade(string path, Texture2D full)
    {
        if (!TextureUse.TryGetValue(path, out var use)) { Raylib.UnloadTexture(full); return; }
        foreach (var i in use.chunks) Chunks[i].Model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = full;
        Raylib.UnloadTexture(use.tex);
        TextureUse[path] = (full, use.chunks);
        Streamed++;
    }

    /// The texture paths, nearest to `start` first.
    public List<string> StreamOrder(Vector3 start) =>
        TextureUse.OrderBy(kv => kv.Value.chunks.Min(i => Vector3.Distance(Chunks[i].Centre with { Y = 0 }, start with { Y = 0 }) - Chunks[i].Radius)).Select(kv => kv.Key).ToList();

    /// The three Siege districts of E-Day (the street centre and height of the two newer ones were surveyed with the probe tool).
    public static readonly CityMap[] Maps =
    {
        new("downtown", Sheets.CityName, Sheets.CityMap, Sheets.CityCells, Sheets.CityCenterX, Sheets.CityCenterY, Sheets.CityGroundZ, ""),
        new("industrial", "Industrial", "Fairlight/Maps/Fairlight_World/Fairlight_Siege_Industrial_WP", Grid(-2, 0, -10, -8), -70, -1072, 192.7f, "wave-3"),
        new("historic", "Historic", "Fairlight/Maps/Fairlight_World/Fairlight_Siege_Historic_WP", Grid(-4, -2, 7, 9), -370, 1095, 300.85f, "win"),
    };
    static string[] Grid(int x0, int x1, int y0, int y1)
    {
        var list = new List<string>();
        for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++) list.Add($"MainGrid_L0_X{x}_Y{y}");
        return list.ToArray();
    }
    /// The district being built or played.
    public static CityMap Current = Maps[0];
    public static bool Unlocked(CityMap m) => m.Unlock == "" || Profile.P.Achievements.Contains(m.Unlock) || Environment.GetEnvironmentVariable("DAEVAS_MAP") == m.Id;

    public readonly List<CityChunk> Chunks = new();
    public readonly List<BoundingBox> Colliders = new();
    public readonly List<BoundingBox> Mounds = new();
    public readonly List<BoundingBox> CameraBoxes = new();
    public Dictionary<(int, int), List<(Vector3 a, Vector3 b, Vector3 c)>> CameraTris = new();
    public float[] Reach = new float[96];
    public HashSet<(int, int)> Occupied = new();
    public List<BoundingBox> Plinths = new();
    public List<Ramp> Ramps = new();

    /// Whether a piece of the district stands on the ground near a point (within `margin` metres).
    public bool OccupiedNear(float x, float z, float margin)
    {
        int x0 = (int) MathF.Floor((x - margin) / 2), x1 = (int) MathF.Floor((x + margin) / 2);
        int z0 = (int) MathF.Floor((z - margin) / 2), z1 = (int) MathF.Floor((z + margin) / 2);
        for (var i = x0; i <= x1; i++) for (var k = z0; k <= z1; k++) if (Occupied.Contains((i, k))) return true;
        return false;
    }

    float[]? _ground; int _groundN; float _groundHalf;
    /// Open shopfronts near the fight (you can walk into the shops behind them).
    public readonly List<Vector3> Shopfronts = new();


    /// The real street surface under x/z (sloped streets, sidewalks, plazas, stairs), 0 off the measured area.
    /// Never below 0: the drawn street (the floor under the city) is there, covering pits and holes.
    public float GroundAt(float x, float z) => MathF.Max(0, RawGroundAt(x, z));

    float RawGroundAt(float x, float z)
    {
        if (_ground == null) return 0;
        var gx = x + _groundHalf - 0.5f; var gz = z + _groundHalf - 0.5f;
        int x0 = (int) MathF.Floor(gx), z0 = (int) MathF.Floor(gz);
        if (x0 < 0 || z0 < 0 || x0 + 1 >= _groundN || z0 + 1 >= _groundN) return 0;
        float fx = gx - x0, fz = gz - z0;
        float H(int ix, int iz) { var h = _ground[ix * _groundN + iz]; return float.IsNaN(h) ? 0 : h; }
        return (H(x0, z0) * (1 - fx) + H(x0 + 1, z0) * fx) * (1 - fz) + (H(x0, z0 + 1) * (1 - fx) + H(x0 + 1, z0 + 1) * fx) * fz;
    }
    public const float TriCell = 4;
    /// Where the street lamps' bulbs are (placed by the world at the start of a run).
    public readonly List<Vector3> Lamps = new();
    public int Triangles;

    /// Game space: metres, y up, the map's centre point at the origin and its street level at y = 0.
    static Vector3 ToGame(Vector3 ue) => new(ue.X / 100 - Current.CenterX, ue.Z / 100 - Current.GroundZ, ue.Y / 100 - Current.CenterY);

    /// The cells around the district's own: the distant ring that streams in after the start.
    public static string[] RingCells(CityMap map)
    {
        var have = new HashSet<(int, int)>();
        var suffixes = new HashSet<string> { "" };
        foreach (var c in map.Cells)
        {
            var m = Regex.Match(c, @"^MainGrid_L0_X(-?\d+)_Y(-?\d+)(_.*)?$");
            if (!m.Success) continue;
            have.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
            if (m.Groups[3].Success) suffixes.Add(m.Groups[3].Value);
        }
        if (have.Count == 0) return [];
        var ring = new List<string>();
        for (var x = have.Min(h => h.Item1) - 1; x <= have.Max(h => h.Item1) + 1; x++)
            for (var y = have.Min(h => h.Item2) - 1; y <= have.Max(h => h.Item2) + 1; y++)
                if (!have.Contains((x, y)))
                    foreach (var sfx in suffixes) ring.Add($"MainGrid_L0_X{x}_Y{y}{sfx}");
        return ring.ToArray();
    }

    /// Builds the district (or, `distant`, the ring of cells around it: big pieces only, nothing to walk into,
    /// between `near` and `far` metres out).
    public static CityData Build(GearsInstall gears, Action<string> status, string[]? cells = null, float near = 0, float far = 0, bool distant = false)
    {
        var data = new CityData();
        var map = Current;
        cells ??= map.Cells;
        if (far <= 0) far = Sheets.CityDrawRadius;
        var mapName = map.Map[(map.Map.LastIndexOf('/') + 1)..];
        // the cells are read in parallel (each is its own map file), then put back in order
        var perCell = new List<GearsPlacement>[cells.Length];
        var cellsDone = 0;
        System.Threading.Tasks.Parallel.For(0, cells.Length, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
        {
            var path = $"FairlightConcept/Content/{map.Map}/_Generated_/{mapName}_{cells[i]}.umap";
            try { perCell[i] = CachedPlacements(gears, path, cells[i]); }
            catch (Exception e) { if (!distant) Log.Info($"  city cell {cells[i]} failed: {e.Message}"); perCell[i] = new(); }
            status($"Reading the streets of {map.Name} ({System.Threading.Interlocked.Increment(ref cellsDone)}/{cells.Length})");
        });
        var all = perCell.SelectMany(c => c).ToList();
        var skip = new Regex(Sheets.CitySkip, RegexOptions.IgnoreCase);
        var keep = all.Where(p => !skip.IsMatch(p.Mesh))
            .Select(p => (p, at: ToGame(p.World.Translation)))
            .Where(x => MathF.Max(MathF.Abs(x.at.X), MathF.Abs(x.at.Z)) is var d && d < far && d >= near && x.at.Y > -20 && x.at.Y < 120)
            .ToList();
        data.Placements = keep.Count;
        if (Environment.GetEnvironmentVariable("DAEVAS_FLOATCHECK") == "1")
        {
            // pieces standing high with nothing under them (what looks like a floating building)
            var low = keep.Where(x => x.at.Y < 12).Select(x => x.at).ToList();
            var floating = keep.Where(x => x.at.Y > 15 && !low.Any(l => MathF.Abs(l.X - x.at.X) < 8 && MathF.Abs(l.Z - x.at.Z) < 8)).ToList();
            foreach (var g in floating.GroupBy(x => ((int) (x.at.X / 20), (int) (x.at.Z / 20))).OrderByDescending(g => g.Count()).Take(8))
                Log.Info($"  floating{(distant ? " (ring)" : "")}: {g.Count()} pieces near {g.First().at.X:0},{g.First().at.Y:0},{g.First().at.Z:0}: {string.Join(", ", g.Select(x => x.p.Mesh[(x.p.Mesh.LastIndexOf('/') + 1)..]).Distinct().Take(3))}");
        }
        if (Environment.GetEnvironmentVariable("DAEVAS_ROADCHECK") == "1")
            foreach (var g in keep.Where(x => x.p.Mesh.Contains("SplinePolygon", StringComparison.OrdinalIgnoreCase)).GroupBy(x => System.Text.RegularExpressions.Regex.Replace(x.p.Mesh[(x.p.Mesh.LastIndexOf('/') + 1)..], @"\d+", "#")).OrderByDescending(g => g.Count()).Take(15))
                Log.Info($"  spline surface {g.Key}: x{g.Count()}, y {g.Min(x => x.at.Y):0.0}..{g.Max(x => x.at.Y):0.0}");
        Log.Info($"  city {map.Name}{(distant ? " (distant ring)" : "")}: {all.Count} placements in {cells.Length} cells, {keep.Count} kept");

        // the buildings are read in parallel (the first start of a district is mostly this)
        var unique = keep.Select(x => x.p.Mesh).Distinct().ToList();
        var read = new System.Collections.Concurrent.ConcurrentDictionary<string, GearsMesh?>();
        var done = 0;
        System.Threading.Tasks.Parallel.ForEach(unique, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, path =>
        {
            GearsMesh? m;
            try { m = gears.LoadMesh(path, Sheets.CityLod); }
            catch { m = null; }
            read[path] = m;
            var n = System.Threading.Interlocked.Increment(ref done);
            if (n % 25 == 0) status($"Reading Gears buildings ({n}/{unique.Count})");
        });
        var meshes = new Dictionary<string, GearsMesh?>(read);
        data.Meshes = meshes.Count(m => m.Value != null);

        var albedo = new Dictionary<string, string?>();
        var bounds = new Dictionary<GearsMesh, (Vector3 min, Vector3 max)>();
        var open = new Dictionary<(string?, int, int), CityBatch>();
        var chunk = Sheets.CityChunk;
        status("Building the city");
        // Only what stands on the street: pieces are taken from the ground up, and one that starts high above
        // everything already standing under it (part of a building whose lower floors aren't in these cells) is left out.
        (Vector3 min, Vector3 max) GameBox(GearsPlacement pl, GearsMesh gm)
        {
            if (!bounds.TryGetValue(gm, out var lb))
            {
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                for (var i = 0; i < gm.Positions.Length; i += 3) { var v = new Vector3(gm.Positions[i], gm.Positions[i + 1], gm.Positions[i + 2]); mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                bounds[gm] = lb = (mn, mx);
            }
            var a = new Vector3(float.MaxValue); var z = new Vector3(float.MinValue);
            for (var c = 0; c < 8; c++)
            {
                var g = ToGame(Vector3.Transform(new Vector3((c & 1) == 0 ? lb.min.X : lb.max.X, (c & 2) == 0 ? lb.min.Y : lb.max.Y, (c & 4) == 0 ? lb.min.Z : lb.max.Z), pl.World));
                a = Vector3.Min(a, g); z = Vector3.Max(z, g);
            }
            return (a, z);
        }
        const float SupportCell = 4;
        var support = new Dictionary<(int, int), float>();
        var unsupported = new HashSet<int>();
        var order = Enumerable.Range(0, keep.Count).Where(i => meshes[keep[i].p.Mesh] is { Positions.Length: > 0 })
            .Select(i => (i, box: GameBox(keep[i].p, meshes[keep[i].p.Mesh]!))).OrderBy(x => x.box.Item1.Y).ToList();
        foreach (var (i, (bmin, bmax)) in order)
        {
            int x0 = (int) MathF.Floor(bmin.X / SupportCell), x1 = (int) MathF.Floor(bmax.X / SupportCell);
            int z0 = (int) MathF.Floor(bmin.Z / SupportCell), z1 = (int) MathF.Floor(bmax.Z / SupportCell);
            if (bmin.Y > 8)
            {
                var under = float.MinValue;
                for (var cx = x0; cx <= x1; cx++) for (var cz = z0; cz <= z1; cz++) if (support.TryGetValue((cx, cz), out var t)) under = MathF.Max(under, t);
                if (bmin.Y > under + 3) { unsupported.Add(i); continue; }
            }
            for (var cx = x0; cx <= x1; cx++) for (var cz = z0; cz <= z1; cz++)
                support[(cx, cz)] = support.TryGetValue((cx, cz), out var t2) ? MathF.Max(t2, bmax.Y) : bmax.Y;
        }
        if (unsupported.Count > 0) Log.Info($"  city {map.Name}{(distant ? " (distant ring)" : "")}: {unsupported.Count} pieces with nothing under them left out");
        var standing = new List<(Vector3 min, Vector3 max, bool hangs)>();
        var missing = new Dictionary<string, int>();
        var index = -1;
        foreach (var (p, at) in keep)
        {
            index++;
            if (unsupported.Contains(index)) continue;
            if (meshes[p.Mesh] is not { } m || m.Positions.Length == 0) continue;
            var world = p.World;
            // collider: the placed mesh's box, for pieces that stand on the street and are tall enough to block
            if (!bounds.TryGetValue(m, out var b))
            {
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                for (var i = 0; i < m.Positions.Length; i += 3) { var v = new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]); mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                bounds[m] = b = (mn, mx);
            }
            var gmin = new Vector3(float.MaxValue); var gmax = new Vector3(float.MinValue);
            for (var c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? b.min.X : b.max.X, (c & 2) == 0 ? b.min.Y : b.max.Y, (c & 4) == 0 ? b.min.Z : b.max.Z);
                var g = ToGame(Vector3.Transform(corner, world));
                gmin = Vector3.Min(gmin, g); gmax = Vector3.Max(gmax, g);
            }
            var size = gmax - gmin;
            // dev: DAEVAS_PIECESAT=x,z lists the pieces over that spot (to find why something floats)
            if (_piecesAt is { } pa && !distant && pa.X > gmin.X && pa.X < gmax.X && pa.Y > gmin.Z && pa.Y < gmax.Z)
                Log.Info($"  piece over {pa.X:0},{pa.Y:0}: {p.Mesh[(p.Mesh.LastIndexOf('/') + 1)..]} y {gmin.Y:0.0}..{gmax.Y:0.0}, {size.X:0}x{size.Z:0} m, at {at.X:0},{at.Z:0}");
            if (!distant && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 40)
                standing.Add((gmin, gmax, Overhead.IsMatch(p.Mesh[(p.Mesh.LastIndexOf('/') + 1)..]) || IsPile(p.Mesh)));
            if (!distant && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityDrawRadius + 20)
            {
                // a ground floor standing a little above the street: a foundation under it, so you can't see beneath
                // ...and a whole building block whose ground storey is missing from the map (it hangs a few metres up):
                // a solid weathered ground storey under it, that you can't walk into
                var file = p.Mesh[(p.Mesh.LastIndexOf('/') + 1)..]; // (folder names like "LevelDesign" would match "sign")
                var low = gmin.Y > 0.05f && gmin.Y < 1.6f && size.Y > 1.5f && MathF.Max(size.X, size.Z) > 2;
                var block = gmin.Y >= 1.6f && gmin.Y < 7 && size.Y > 4 && size.X * size.Z > 25 && !Overhead.IsMatch(file);
                // a raised plaza deck (in Gears it rests on terrain the game doesn't load): solid ground under it
                var slab = size.Y < 1.2f && size.X * size.Z > 60 && gmin.Y > 0.4f && gmin.Y < 8 && !Overhead.IsMatch(file);
                if ((low || block || slab) && !p.Mesh.Contains("storefront", StringComparison.OrdinalIgnoreCase) && !IsPile(p.Mesh))
                {
                    var top = slab ? gmax.Y - 0.03f : gmin.Y + 0.12f;
                    var plinth = new BoundingBox(new Vector3(gmin.X + 0.05f, -0.5f, gmin.Z + 0.05f), new Vector3(gmax.X - 0.05f, top, gmax.Z - 0.05f));
                    data.Plinths.Add(plinth);
                    if (slab) data.SlabPlinths++; else if (block) data.BlockPlinths++;
                    if ((block || slab) && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 15) data.Colliders.Add(plinth with { Min = plinth.Min with { Y = 0 } });
                }
            }
            // the district's footprint: everything standing on the ground (not flat road surfaces) marks how far the
            // island must reach that way, and the ground it covers (the Abyss keeps trees out of buildings)
            if (!distant && gmin.Y < 6 && size.Y > 1.5f && size.X * size.Z < 6000 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityDrawRadius + 20)
            {
                for (var ix = (int) MathF.Floor(gmin.X / 2); ix <= (int) MathF.Floor(gmax.X / 2); ix++)
                    for (var iz = (int) MathF.Floor(gmin.Z / 2); iz <= (int) MathF.Floor(gmax.Z / 2); iz++)
                    {
                        data.Occupied.Add((ix, iz));
                        var cx = ix * 2 + 1f; var cz = iz * 2 + 1f;
                        var bin = (int) ((MathF.Atan2(cz, cx) / MathF.Tau % 1 + 1) % 1 * data.Reach.Length) % data.Reach.Length;
                        data.Reach[bin] = MathF.Max(data.Reach[bin], MathF.Min(Sheets.CityDrawRadius + 25, MathF.Sqrt(cx * cx + cz * cz) + 1.5f));
                    }
            }
            // far away only the bigger pieces show (but not so few that a tower's lower floors go missing)
            if (distant && MathF.Max(size.X, MathF.Max(size.Y, size.Z)) < (MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < 200 ? 2.5f : 6)) continue;
            // shopfronts are open: you can walk in (they're drawn, but don't block or push the camera)
            var shopfront = p.Mesh.Contains("storefront", StringComparison.OrdinalIgnoreCase);
            if (shopfront && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf) data.Shopfronts.Add((gmin + gmax) / 2);
            // a shopfront's sill still stands: vault it to get in (Space)
            if (!distant && shopfront && gmin.Y < 2.6f && size.Y > 1.0f && size.X * size.Z < 900 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 15)
                data.Colliders.Add(new BoundingBox(gmin, gmax with { Y = MathF.Min(gmax.Y, MathF.Max(0, gmin.Y) + 0.9f) }));
            if (!distant && !shopfront && StairName.IsMatch(p.Mesh[(p.Mesh.LastIndexOf('/') + 1)..]) && gmin.Y < 2.6f && size.Y > 0.4f && size.Y < 8 && size.X * size.Z < 900 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 15)
            {
                // which way it climbs: the mesh's vertices, higher toward one end of its longer side
                var axis = size.X >= size.Z ? 0 : 2;
                float lowSum = 0, highSum = 0; int lowN = 0, highN = 0;
                for (var i = 0; i < m.Positions.Length; i += 9)
                {
                    var g = ToGame(Vector3.Transform(new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]), world));
                    var along = axis == 0 ? (g.X - gmin.X) / MathF.Max(0.01f, size.X) : (g.Z - gmin.Z) / MathF.Max(0.01f, size.Z);
                    if (along < 0.5f) { lowSum += g.Y; lowN++; } else { highSum += g.Y; highN++; }
                }
                var sign = (highN == 0 ? 0 : highSum / highN) >= (lowN == 0 ? 0 : lowSum / lowN) ? 1 : -1;
                data.Ramps.Add(new Ramp(new BoundingBox(gmin, gmax), axis == 0 ? 0 : 1, sign));
            }
            else if (!distant && !shopfront && IsPile(p.Mesh) && gmin.Y < 2.6f && size.Y > 0.3f && size.X * size.Z < 900 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 15)
                data.Mounds.Add(new BoundingBox(gmin, gmax));
            else if (!distant && !shopfront && size.Y > 1.0f && gmin.Y < 2.6f && gmax.Y > 0.9f && size.X * size.Z < 900 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 15)
                data.Colliders.AddRange(ShapedColliders(m, world, gmin, gmax));
            if (distant || shopfront) { }
            else if (gmin.Y < 12 && size.Y > 0.25f && size.X * size.Z < 900 && MathF.Max(MathF.Abs(at.X), MathF.Abs(at.Z)) < Sheets.CityPlayHalf + 25)
                data.CameraBoxes.Add(new BoundingBox(gmin, gmax));
            else if (gmin.Y < 12 && size.X * size.Z >= 900 && gmax.X > -Sheets.CityPlayHalf - 25 && gmin.X < Sheets.CityPlayHalf + 25 && gmax.Z > -Sheets.CityPlayHalf - 25 && gmin.Z < Sheets.CityPlayHalf + 25)
                AddCameraTris(data, m, world);

            if (Environment.GetEnvironmentVariable("DAEVAS_ROADCHECK") == "1" && !distant && gmin.X < 3 && gmax.X > -3 && gmin.Z < 3 && gmax.Z > -3 && gmin.Y < 0.5f && gmax.Y > -0.5f)
                Log.Info($"  roadcheck: {p.Mesh[(p.Mesh.LastIndexOf('/') + 1)..]} y {gmin.Y:0.00}..{gmax.Y:0.00}, materials {string.Join(" | ", m.Materials.Select(mt => mt[(mt.LastIndexOf('/') + 1)..] + " -> " + (gears.FindAlbedo(mt) is { } al ? al[(al.LastIndexOf('/') + 1)..] : "none")))}");
            var key = ((int) MathF.Floor(at.X / chunk), (int) MathF.Floor(at.Z / chunk));
            var mirrored = world.GetDeterminant() < 0;
            foreach (var s in m.Sections)
            {
                var mat = s.Material >= 0 && s.Material < m.Materials.Length ? m.Materials[s.Material] : null;
                string? tex = null;
                if (mat != null && !albedo.TryGetValue(mat, out tex)) { albedo[mat] = tex = gears.FindAlbedo(mat) ?? FallbackAlbedo(mat); if (tex == null && !distant) missing[mat[(mat.LastIndexOf('/') + 1)..]] = missing.GetValueOrDefault(mat[(mat.LastIndexOf('/') + 1)..]) + 1; }
                var used = new Dictionary<uint, ushort>();
                var count = s.Triangles * 3;
                if (!open.TryGetValue((tex, key.Item1, key.Item2), out var batch) || batch.Verts + count > 60000)
                {
                    if (batch != null) data.Batches.Add(batch);
                    open[(tex, key.Item1, key.Item2)] = batch = new CityBatch { Texture = tex, Window = tex == null && mat != null && mat.Contains("Window", StringComparison.OrdinalIgnoreCase), Glass = mat != null && mat.Contains("glass", StringComparison.OrdinalIgnoreCase) };
                }
                for (var k = 0; k + 2 < count; k += 3)
                {
                    // UE's front faces wind clockwise; swapping Y/Z mirrors them counter-clockwise, as raylib wants,
                    // unless the piece is itself placed mirrored (negative scale), which flips them back
                    foreach (var corner in mirrored ? [0, 2, 1] : new[] { 0, 1, 2 })
                    {
                        var src = m.Indices[s.FirstIndex + k + corner];
                        if (!used.TryGetValue(src, out var dst))
                        {
                            var v = ToGame(Vector3.Transform(new Vector3(m.Positions[src * 3], m.Positions[src * 3 + 1], m.Positions[src * 3 + 2]), world));
                            var n = Vector3.TransformNormal(new Vector3(m.Normals[src * 3], m.Normals[src * 3 + 1], m.Normals[src * 3 + 2]), world);
                            n = n.LengthSquared() > 1e-8f ? Vector3.Normalize(new Vector3(n.X, n.Z, n.Y)) : Vector3.UnitY;
                            dst = (ushort) batch.Verts;
                            batch.Pos.Add(v.X); batch.Pos.Add(v.Y); batch.Pos.Add(v.Z);
                            batch.Nrm.Add(n.X); batch.Nrm.Add(n.Y); batch.Nrm.Add(n.Z);
                            batch.Uv.Add(m.Uvs[src * 2]); batch.Uv.Add(m.Uvs[src * 2 + 1]);
                            batch.Min = Vector3.Min(batch.Min, v); batch.Max = Vector3.Max(batch.Max, v);
                            used[src] = dst;
                        }
                        batch.Idx.Add(dst);
                    }
                }
                data.Triangles += s.Triangles;
            }
        }
        if (Environment.GetEnvironmentVariable("DAEVAS_CITYMAP") == "1") WriteMap(keep.Select(x => x.p).ToList(), meshes, bounds);
        data.Batches.AddRange(open.Values);
        data.Batches.RemoveAll(b => b.Idx.Count == 0);
        if (!distant) BuildGround(data);

        var textures = data.Batches.Select(b => b.Texture).Where(t => t != null).Distinct().ToList();
        for (var i = 0; i < textures.Count; i++)
        {
            if (i % 10 == 0) status($"Reading Gears textures ({i}/{textures.Count})");
            try { if (gears.LoadTexture(textures[i]!, StreamStart) is { } t) data.Textures[textures[i]!] = t; }
            catch { /* untextured */ }
        }
        if (data.Shopfronts.Count > 0) Log.Info($"  open shopfronts in the play area: {data.Shopfronts.Count}");
        if (!distant) FillFloatingColumns(data, standing);
        if (missing.Count > 0) Log.Info($"  city: materials with no Gears colour texture found: {string.Join(", ", missing.OrderByDescending(k => k.Value).Take(14).Select(k => k.Key))}");
        Log.Info($"  city: {data.Meshes} meshes, {data.Triangles:N0} triangles in {data.Batches.Count} batches, {data.Textures.Count}/{textures.Count} textures, {data.Colliders.Count} colliders, {data.Mounds.Count} walkable piles, {data.Ramps.Count} stairs and ramps, {data.Plinths.Count} foundations ({data.BlockPlinths} building blocks, {data.SlabPlinths} raised plazas, {data.ColumnPlinths} floating strips), {data.CameraBoxes.Count} camera boxes, {data.CameraTris.Values.Sum(l => l.Count):N0} camera triangles");
        return data;
    }

    const string CacheVersion = "daevas-city-2"; // 2: front faces wind the right way

    /// The street surface: for each metre of the play area, the lowest walkable (upward-facing) surface near street
    /// level, from the Gears geometry itself (roads, sidewalks, plazas, stairs). NaN where there is none.
    static void BuildGround(CityData data)
    {
        var half = Sheets.CityPlayHalf + 10;
        var n = (int) (half * 2);
        var g = new float[n * n];
        Array.Fill(g, float.NaN);
        foreach (var b in data.Batches)
            for (var t = 0; t + 2 < b.Idx.Count; t += 3)
            {
                Vector3 V(int k) => new(b.Pos[b.Idx[t + k] * 3], b.Pos[b.Idx[t + k] * 3 + 1], b.Pos[b.Idx[t + k] * 3 + 2]);
                var (a, c, d) = (V(0), V(1), V(2));
                var nrm = Vector3.Cross(c - a, d - a);
                if (nrm.LengthSquared() < 1e-8f || MathF.Abs(Vector3.Normalize(nrm).Y) < 0.85f) continue; // walkable: nearly level
                var top = MathF.Max(a.Y, MathF.Max(c.Y, d.Y));
                if (top > 3.5f || MathF.Min(a.Y, MathF.Min(c.Y, d.Y)) < -4f) continue;          // street level, not roofs or basements
                int x0 = (int) MathF.Floor(MathF.Min(a.X, MathF.Min(c.X, d.X)) + half), x1 = (int) MathF.Ceiling(MathF.Max(a.X, MathF.Max(c.X, d.X)) + half);
                int z0 = (int) MathF.Floor(MathF.Min(a.Z, MathF.Min(c.Z, d.Z)) + half), z1 = (int) MathF.Ceiling(MathF.Max(a.Z, MathF.Max(c.Z, d.Z)) + half);
                for (var x = Math.Max(0, x0); x <= Math.Min(n - 1, x1); x++)
                    for (var z = Math.Max(0, z0); z <= Math.Min(n - 1, z1); z++)
                    {
                        // the cell centre inside the triangle (in x/z): its height there
                        float px = x + 0.5f - half, pz = z + 0.5f - half;
                        var den = (c.Z - d.Z) * (a.X - d.X) + (d.X - c.X) * (a.Z - d.Z);
                        if (MathF.Abs(den) < 1e-6f) continue;
                        var w1 = ((c.Z - d.Z) * (px - d.X) + (d.X - c.X) * (pz - d.Z)) / den;
                        var w2 = ((d.Z - a.Z) * (px - d.X) + (a.X - d.X) * (pz - d.Z)) / den;
                        var w3 = 1 - w1 - w2;
                        if (w1 < -0.01f || w2 < -0.01f || w3 < -0.01f) continue;
                        var h = w1 * a.Y + w2 * c.Y + w3 * d.Y;
                        ref var cell = ref g[x * n + z];
                        if (float.IsNaN(cell) || h < cell) cell = h;
                    }
            }
        var sm = (float[]) g.Clone();
        Span<float> win = stackalloc float[9];
        for (var x = 1; x < n - 1; x++)
            for (var z = 1; z < n - 1; z++)
            {
                var k = 0;
                for (var dx = -1; dx <= 1; dx++) for (var dz = -1; dz <= 1; dz++) { var h = g[(x + dx) * n + z + dz]; if (!float.IsNaN(h)) win[k++] = h; }
                if (k < 5) continue;
                var part = win[..k]; part.Sort();
                sm[x * n + z] = part[k / 2];
            }
        g = sm;
        data.Ground = g; data.GroundN = n; data.GroundHalf = half;
        var have = g.Where(h => !float.IsNaN(h)).ToArray();
        if (have.Length > 0)
        {
            var mean = have.Average();
            // where most of the street really is (10 cm buckets)
            var hist = have.GroupBy(h => MathF.Round(h * 10) / 10).OrderByDescending(gr => gr.Count()).Take(6).Select(gr => $"{gr.Key:0.0}m:{gr.Count()}");
            Log.Info($"  street height histogram: {string.Join(" ", hist)}");
            Log.Info($"  street surface: {have.Length}/{g.Length} m², heights {have.Min():0.00}..{have.Max():0.00} m, mean {mean:0.00}, {have.Count(h => MathF.Abs(h) > 0.3f)} m² off the street plane by 0.3 m or more");
        }
    }

    /// A cell's placements, cached on this PC after the first read (reading a cell takes a few seconds).
    static List<GearsPlacement> CachedPlacements(GearsInstall gears, string path, string cell)
    {
        var dir = System.IO.Path.Combine(Paths.DataDir, "city-cache");
        var file = System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileNameWithoutExtension(path)}.bin");
        try
        {
            if (System.IO.File.Exists(file))
            {
                using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(file));
                if (r.ReadString() == CacheVersion && r.ReadString() == gears.Stamp)
                {
                    var meshes = new string[r.ReadInt32()];
                    for (var i = 0; i < meshes.Length; i++) meshes[i] = r.ReadString();
                    var list = new List<GearsPlacement>(r.ReadInt32());
                    for (var n = list.Capacity; n > 0; n--)
                    {
                        var m = meshes[r.ReadInt32()];
                        var f = new float[16];
                        for (var k = 0; k < 16; k++) f[k] = r.ReadSingle();
                        list.Add(new GearsPlacement { Mesh = m, World = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]) });
                    }
                    return list;
                }
            }
        }
        catch (Exception e) { Log.Info($"  city cache for {cell} unreadable: {e.Message}"); }
        var read = gears.LoadPlacements(path);
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(file));
            w.Write(CacheVersion); w.Write(gears.Stamp);
            var names = read.Select(p => p.Mesh).Distinct().ToList();
            var index = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
            w.Write(names.Count); foreach (var n in names) w.Write(n);
            w.Write(read.Count);
            foreach (var p in read)
            {
                w.Write(index[p.Mesh]);
                var m = p.World;
                foreach (var v in new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 }) w.Write(v);
            }
        }
        catch (Exception e) { Log.Info($"  city cache for {cell} not written: {e.Message}"); }
        return read;
    }

    /// Dev: an ASCII map (2 m cells, 240 m square) of everything standing at street level, to choose the play area.
    static void WriteMap(List<GearsPlacement> placements, Dictionary<string, GearsMesh?> meshes, Dictionary<GearsMesh, (Vector3 min, Vector3 max)> bounds)
    {
        const int n = 120; const float cell = 2f;
        var grid = new char[n, n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) grid[x, y] = '.';
        foreach (var p in placements)
        {
            if (meshes[p.Mesh] is not { } m || !bounds.TryGetValue(m, out var b)) continue;
            var gmin = new Vector3(float.MaxValue); var gmax = new Vector3(float.MinValue);
            for (var c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? b.min.X : b.max.X, (c & 2) == 0 ? b.min.Y : b.max.Y, (c & 4) == 0 ? b.min.Z : b.max.Z);
                var g = ToGame(Vector3.Transform(corner, p.World));
                gmin = Vector3.Min(gmin, g); gmax = Vector3.Max(gmax, g);
            }
            if (gmax.Y < 0.5f || gmin.Y > 2.5f || gmax.Y - gmin.Y < 0.8f) continue;
            for (var x = (int) ((gmin.X + n) / cell); x <= (int) ((gmax.X + n) / cell); x++)
                for (var y = (int) ((gmin.Z + n) / cell); y <= (int) ((gmax.Z + n) / cell); y++)
                    if (x >= 0 && y >= 0 && x < n && y < n) grid[x, y] = (gmax.X - gmin.X) * (gmax.Z - gmin.Z) > 900 ? 'B' : '#';
        }
        var lines = new List<string> { "city map: x = -120..120 m left to right, z = -120..120 m top to bottom, 2 m per char, + = origin" };
        for (var y = 0; y < n; y++)
        {
            var row = new char[n];
            for (var x = 0; x < n; x++) row[x] = x == n / 2 && y == n / 2 ? '+' : grid[x, y];
            lines.Add(new string(row));
        }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Paths.LogDir, "citymap.txt"), lines);
    }

    /// GPU side (main thread).
    /// Puts a build's batches on the GPU as chunks (the district itself, or its distant ring).
    readonly Dictionary<string, Texture2D> _farTextures = new();

    public unsafe void AddBatches(CityData data, Func<GearsTexture, Texture2D> makeTexture, bool far)
    {
        var gpu = far ? _farTextures : new Dictionary<string, Texture2D>();
        var untextured = 0;
        foreach (var b in data.Batches)
        {
            var mesh = new Mesh(b.Verts, b.Idx.Count / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
            var pos = mesh.VerticesAs<float>(); var nrm = mesh.NormalsAs<float>(); var uv = mesh.TexCoordsAs<float>(); var idx = mesh.IndicesAs<ushort>();
            for (var i = 0; i < b.Pos.Count; i++) { pos[i] = b.Pos[i]; nrm[i] = b.Nrm[i]; }
            for (var i = 0; i < b.Uv.Count; i++) uv[i] = b.Uv[i];
            for (var i = 0; i < b.Idx.Count; i++) idx[i] = b.Idx[i];
            Raylib.UploadMesh(ref mesh, false);
            // drawn from the GPU only: raylib's CPU copy would hold hundreds of MB (the indices stay, see GameAssets.DropCpuCopy)
            Raylib.MemFree(mesh.Vertices); mesh.Vertices = null;
            Raylib.MemFree(mesh.Normals); mesh.Normals = null;
            Raylib.MemFree(mesh.TexCoords); mesh.TexCoords = null;
            if (!far) Responsive.Tick();
            var model = Lit.Make(mesh);
            if (b.Texture != null && data.Textures.TryGetValue(b.Texture, out var t))
            {
                // the distant ring shares the district's textures where it can, and keeps its small ones (no streaming)
                Texture2D tex;
                if (far && TextureUse.TryGetValue(b.Texture, out var shared)) tex = shared.tex;
                else if (!gpu.TryGetValue(b.Texture, out tex)) gpu[b.Texture] = tex = makeTexture(t);
                model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
                if (!far)
                {
                    if (!TextureUse.TryGetValue(b.Texture, out var use)) TextureUse[b.Texture] = use = (tex, new List<int>());
                    use.chunks.Add(Chunks.Count);
                }
            }
            else
            {
                // no Gears albedo found: weathered concrete rather than a flat colour
                if (b.Window) model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(34, 38, 44, 255);
                else
                {
                    if (_grime.Id == 0) _grime = MakeGrime();
                    model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = _grime;
                    model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(170, 164, 156, 255);
                    untextured++;
                }
            }
            Chunks.Add(new CityChunk { Model = model, Centre = (b.Min + b.Max) / 2, Radius = Vector3.Distance(b.Min, b.Max) / 2, Far = far, Alpha = b.Glass ? Lit.Glass : b.Texture != null && data.Textures.TryGetValue(b.Texture, out var at) ? Lit.AlphaFor(b.Texture, at.Rgba) : Lit.Opaque });
        }
        if (!far) Log.Info($"  city: {data.Batches.Count} chunks, {untextured} untextured batches given weathered concrete; alpha: {Chunks.Count(c => c.Alpha == Lit.Cutout)} cut out, {Chunks.Count(c => c.Alpha == Lit.Blended)} blended, {Chunks.Count(c => c.Alpha == Lit.Glass)} glass");
    }

    public static unsafe City Upload(CityData data, Func<GearsTexture, Texture2D> makeTexture)
    {
        var city = new City { Triangles = data.Triangles };
        city.Colliders.AddRange(data.Colliders);
        city.Mounds.AddRange(data.Mounds);
        city.Reach = data.Reach; city.Occupied = data.Occupied; city.Plinths = data.Plinths; city.Ramps = data.Ramps;
        city.CameraBoxes.AddRange(data.CameraBoxes);
        city.CameraTris = data.CameraTris;
        city._ground = data.Ground; city._groundN = data.GroundN; city._groundHalf = data.GroundHalf;
        city.Shopfronts.AddRange(data.Shopfronts);

        city.AddBatches(data, makeTexture, false);
        return city;
    }

    /// Files a big piece's triangles below roof height into the 4 m camera cells they cover.
    static void AddCameraTris(CityData data, GearsMesh m, Matrix4x4 world)
    {
        var lim = Sheets.CityPlayHalf + 25;
        foreach (var s in m.Sections)
            for (var k = 0; k + 2 < s.Triangles * 3; k += 3)
            {
                Vector3 V(int corner) { var i = (int) m.Indices[s.FirstIndex + k + corner]; return ToGame(Vector3.Transform(new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]), world)); }
                var a = V(0); var b = V(1); var c = V(2);
                var mn = Vector3.Min(a, Vector3.Min(b, c)); var mx = Vector3.Max(a, Vector3.Max(b, c));
                if (mn.Y > 12 || mx.X < -lim || mn.X > lim || mx.Z < -lim || mn.Z > lim) continue;
                int x0 = (int) MathF.Floor(MathF.Max(mn.X, -lim) / City.TriCell), x1 = (int) MathF.Floor(MathF.Min(mx.X, lim) / City.TriCell);
                int z0 = (int) MathF.Floor(MathF.Max(mn.Z, -lim) / City.TriCell), z1 = (int) MathF.Floor(MathF.Min(mx.Z, lim) / City.TriCell);
                for (var x = x0; x <= x1; x++)
                    for (var z = z0; z <= z1; z++)
                    {
                        if (!data.CameraTris.TryGetValue((x, z), out var list)) data.CameraTris[(x, z)] = list = new();
                        list.Add((a, b, c));
                    }
            }
    }

    static Texture2D _grime;

    /// A tileable weathered-concrete texture: blotchy value noise, fine grain and dark streaks running down.
    static Texture2D MakeGrime()
    {
        const int n = 256;
        var rng = new Random(7);
        var grid = new float[16, 16];
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) grid[x, y] = (float) rng.NextDouble();
        float Noise(float x, float y, int cells)
        {
            var fx = x * cells; var fy = y * cells;
            int x0 = (int) fx, y0 = (int) fy;
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float G(int gx, int gy) => grid[(gx % cells + cells) % cells * (16 / cells), (gy % cells + cells) % cells * (16 / cells)];
            return float.Lerp(float.Lerp(G(x0, y0), G(x0 + 1, y0), tx), float.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), tx), ty);
        }
        var streaks = new float[n];
        for (var x = 0; x < n; x++) streaks[x] = (float) rng.NextDouble() < 0.08 ? 0.75f + 0.2f * (float) rng.NextDouble() : 1;
        var img = Raylib.GenImageColor(n, n, Color.White);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                float u = x / (float) n, v = y / (float) n;
                var blot = Noise(u, v, 4) * 0.5f + Noise(u, v, 8) * 0.3f + Noise(u, v, 16) * 0.2f;
                var grain = 0.92f + 0.08f * (float) rng.NextDouble();
                var streak = float.Lerp(1, streaks[x], 0.4f + 0.6f * v);
                var g = Math.Clamp((0.62f + 0.45f * (blot - 0.5f)) * grain * streak, 0, 1);
                Raylib.ImageDrawPixel(ref img, x, y, new Color((int) (g * 255), (int) (g * 250), (int) (g * 242), 255));
            }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
        return tex;
    }

    public int DrawnChunks;
    public static float DetailRadius = 1;   // the graphics preset shrinks how far small pieces are drawn

    /// Every chunk once (the loading warm-up: the driver only really uploads a mesh or texture on its first draw).
    public void DrawAll()
    {
        foreach (var c in Chunks) Raylib.DrawModel(c.Model, Vector3.Zero, 1, Color.White);
    }

    public void Draw(Vector3 eye, Vector3 forward, bool far = true)
    {
        // the view frustum's side planes (a generous cone around the view direction)
        var fov = 1.25f; // half-angle in radians, wide enough for the widest FOV setting and ultrawide screens
        var cosF = MathF.Cos(fov); var sinF = MathF.Sin(fov);
        DrawnChunks = 0;
        // Gears walls, fences and panels are often single-sided (two-sided materials in UE): drawn from both sides here,
        // so nothing turns see-through from behind (the lit shader flips the normal for back faces)
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        foreach (var c in Chunks)
        {
            var to = c.Centre - eye;
            var dist = to.Length();
            if (c.Far ? !far || dist - c.Radius > 320 * DetailRadius : dist - c.Radius > (Sheets.CityDrawRadius + 30) * DetailRadius) continue;
            if (dist > c.Radius && Vector3.Dot(to, forward) < -c.Radius) continue; // behind the camera
            // outside the view cone (sphere test against the cone)
            if (dist > c.Radius)
            {
                var along = Vector3.Dot(to, forward);
                var across = MathF.Sqrt(MathF.Max(0, dist * dist - along * along));
                if (across * cosF - along * sinF > c.Radius) continue;
            }
            // small pieces far away aren't worth drawing
            if (!c.Far && c.Radius < 4 && dist > 70 * DetailRadius) continue;
            Lit.AlphaMode(c.Alpha);
            Raylib.DrawModel(c.Model, Vector3.Zero, 1, Color.White);
            DrawnChunks++;
        }
        Lit.AlphaMode(Lit.Opaque);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }

    static readonly Vector2? _piecesAt = Environment.GetEnvironmentVariable("DAEVAS_PIECESAT")?.Split(',') is [var px, var pz]
        ? new Vector2(float.Parse(px, System.Globalization.CultureInfo.InvariantCulture), float.Parse(pz, System.Globalization.CultureInfo.InvariantCulture)) : null;

    /// Building pieces stacked on nothing (in Gears they stand on terrain the game doesn't load): every 2 m square
    /// near the fight where pieces rise high but the lowest solid one starts above head height, with nothing at street
    /// level, gets a solid foundation under it (merged into strips). Overhead pieces (balconies, awnings, fire escapes,
    /// bridges...) don't count: they're meant to hang.
    static void FillFloatingColumns(CityData data, List<(Vector3 min, Vector3 max, bool hangs)> pieces)
    {
        const float Cell = 2;
        var lowest = new Dictionary<(int, int), float>(); var highest = new Dictionary<(int, int), float>(); var grounded = new HashSet<(int, int)>();
        foreach (var (min, max, hangs) in pieces)
        {
            if (hangs || max.Y - min.Y < 0.3f) continue;
            for (var ix = (int) MathF.Floor((min.X + 0.3f) / Cell); ix <= (int) MathF.Floor((max.X - 0.3f) / Cell); ix++)
                for (var iz = (int) MathF.Floor((min.Z + 0.3f) / Cell); iz <= (int) MathF.Floor((max.Z - 0.3f) / Cell); iz++)
                {
                    var k = (ix, iz);
                    if (min.Y < 0.6f) grounded.Add(k);
                    lowest[k] = lowest.TryGetValue(k, out var lo) ? MathF.Min(lo, min.Y) : min.Y;
                    highest[k] = highest.TryGetValue(k, out var hi) ? MathF.Max(hi, max.Y) : max.Y;
                }
        }
        var floating = new Dictionary<(int, int), float>();
        foreach (var (k, lo) in lowest)
            if (!grounded.Contains(k) && lo > 1.2f && lo < 12 && highest[k] - lo > 3) floating[k] = lo;
        // merge runs along x with about the same floor into strips
        var done = new HashSet<(int, int)>(); var strips = 0;
        foreach (var (k, lo) in floating.OrderBy(f => f.Key.Item2).ThenBy(f => f.Key.Item1))
        {
            if (done.Contains(k)) continue;
            var (x, z) = k; var x1 = x; var top = lo;
            while (floating.TryGetValue((x1 + 1, z), out var next) && MathF.Abs(next - lo) < 0.5f && !done.Contains((x1 + 1, z))) { x1++; top = MathF.Min(top, next); }
            for (var i = x; i <= x1; i++) done.Add((i, z));
            var box = new BoundingBox(new Vector3(x * Cell, -0.5f, z * Cell), new Vector3((x1 + 1) * Cell, top + 0.1f, (z + 1) * Cell));
            data.Plinths.Add(box);
            var centre = (box.Min + box.Max) / 2;
            if (MathF.Max(MathF.Abs(centre.X), MathF.Abs(centre.Z)) < Sheets.CityPlayHalf + 15) data.Colliders.Add(box with { Min = box.Min with { Y = 0 } });
            strips++;
        }
        data.ColumnPlinths = strips;
        if (floating.Count > 0) Log.Info($"  city: {floating.Count} floating 2 m squares under building pieces, filled with {strips} foundation strips");
    }

    /// A piece's collision: one box when it fills its footprint; for open shapes (an L of wall, a wall on the slant, a
    /// U of barriers) the 1 m cells its geometry actually covers, merged into strips.
    static List<BoundingBox> ShapedColliders(GearsMesh m, Matrix4x4 world, Vector3 gmin, Vector3 gmax)
    {
        var size = gmax - gmin;
        if (size.X * size.Z < 16 || MathF.Min(size.X, size.Z) < 3) return [new BoundingBox(gmin, gmax)];
        int nx = (int) MathF.Ceiling(size.X), nz = (int) MathF.Ceiling(size.Z);
        var cells = new bool[nx, nz];
        for (var i = 0; i < m.Positions.Length; i += 3)
        {
            var g = ToGame(Vector3.Transform(new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]), world));
            if (g.Y > gmin.Y + 2.6f) continue; // only what a hero would walk into
            cells[Math.Clamp((int) (g.X - gmin.X), 0, nx - 1), Math.Clamp((int) (g.Z - gmin.Z), 0, nz - 1)] = true;
        }
        var filled = 0; foreach (var c in cells) if (c) filled++;
        if (filled > nx * nz * 0.6f) return [new BoundingBox(gmin, gmax)];
        var boxes = new List<BoundingBox>();
        for (var z = 0; z < nz; z++)
            for (var x = 0; x < nx; x++)
            {
                if (!cells[x, z]) continue;
                var x1 = x; while (x1 + 1 < nx && cells[x1 + 1, z]) x1++;
                boxes.Add(new BoundingBox(new Vector3(gmin.X + x, gmin.Y, gmin.Z + z), new Vector3(MathF.Min(gmax.X, gmin.X + x1 + 1), gmax.Y, MathF.Min(gmax.Z, gmin.Z + z + 1))));
                x = x1;
            }
        return boxes.Count > 0 ? boxes : [new BoundingBox(gmin, gmax)];
    }

    /// Materials the install names no colour texture for: a Gears texture of the same stuff.
    static string? FallbackAlbedo(string material)
    {
        var name = material[(material.LastIndexOf('.') + 1)..].ToLowerInvariant();
        const string Tiling = "FairlightConcept/Content/Fairlight/Environments/Tiling/Textures/";
        const string Rock = "FairlightConcept/Content/Fairlight/Environments/Sets/TechTest/Props/Rock/Textures/";
        if (name.Contains("concrete")) return Tiling + "concrete_dirty_01a_d.uasset";
        if (name.Contains("seawall") || name.Contains("rock") || name.Contains("cliff")) return Rock + "massive_nordic_coastal_cliff_01a_d.uasset";
        if (name.Contains("darkergrey") || name.Contains("grey")) return Tiling + "concrete_wall_09a_d.uasset";
        return null;
    }

    /// Stairs and ramps (walked up, not climbed over).
    static readonly System.Text.RegularExpressions.Regex StairName = new(@"stair|steps|ramp", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// Pieces meant to hang overhead (no ground storey belongs under them).
    static readonly System.Text.RegularExpressions.Regex Overhead = new(@"overhang|awning|canopy|bridge|balcon|sign|pipe|duct|hvac|roof|fire_?escape|scaffold|catwalk|crane|light|lamp|wire|cable|antenna|billboard|walkway|stair|ladder", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// Weathered concrete (for untextured pieces and the ground storeys filled in under raised buildings).
    public static Texture2D Grime { get { if (_grime.Id == 0) _grime = MakeGrime(); return _grime; } }

    /// Rubble, debris and dirt piles: walked over as mounds rather than walls.
    public static bool IsPile(string mesh) => PileName.IsMatch(mesh[(mesh.LastIndexOf('/') + 1)..]);
    static readonly System.Text.RegularExpressions.Regex PileName = new(@"rubble|debris|pile|dirt|gravel|mound|scree|sandbags?_?pile", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
}
