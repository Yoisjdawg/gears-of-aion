using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Daevas.Game.Data;
using Daevas.Gears;
using Raylib_cs;

namespace Daevas.Game;

/// A Gears of War: E-Day gun as decoded on the loading thread, before it goes to the GPU.
sealed class GunData
{
    public required WeaponRow Def;
    public required List<GearsMesh> Parts;
    public GearsTexture? Texture;
    public GearsSkinnedMesh? Full;           // the full Gears gun model, when the sheet names one
    public string?[] FullTextures = [];
}

/// A Gears character on the GPU: one model per section for every baked frame of every clip.
sealed class CharacterModel
{
    public required CharacterRow Def;
    public required Model[] Sections;                          // the bind pose, skinned on the GPU
    public required Texture2D Bones;                           // one row of bone matrices per frame
    public required Dictionary<Clip, (int Start, int Count)> Rows;
    public required Dictionary<Clip, List<Vector3>> Hand;
    public required Dictionary<Clip, float> Duration;
    public float Height;
    public int Triangles;

    public bool Has(Clip clip) => Rows.ContainsKey(clip);

    /// The two frames either side of t (0..1 of the clip) and how far between them.
    public (int a, int b, float f) RowsAt(Clip clip, float t)
    {
        var (start, n) = Rows[clip];
        if (n <= 1) return (start, start, 0);
        if (BakedCharacter.Once(clip))
        {
            var u = Math.Clamp(t, 0, 1) * (n - 1);
            var i = Math.Min((int) u, n - 2);
            return (start + i, start + i + 1, Math.Clamp(u - i, 0, 1));
        }
        var v = t * n;
        v -= MathF.Floor(v / n) * n;
        var k = Math.Clamp((int) v, 0, n - 1);
        return (start + k, start + (k + 1) % n, v - k);
    }

    /// Draws the character posed at t of the clip; `blend` cross-fades it out of the clip it was in before.
    public void Draw(Clip clip, float t, Matrix4x4 transform, Vector3 pos, Color tint, AnimBlend? blend = null)
    {
        var (a, b, f) = RowsAt(clip, t);
        var (c, g) = blend?.Step(this, clip, f < 0.5f ? a : b) ?? (0, 0f);
        if (!GameAssets.CpuSkinCheck) Lit.Skin(Bones, a, b, f, c, g);
        foreach (var model in Sections)
        {
            var m = model;
            m.Transform = transform;
            Raylib.DrawModel(m, pos, 1, tint);
        }
        Lit.NoSkin();
    }

    public Vector3 HandAt(Clip clip, float t)
    {
        var h = Hand[clip];
        var i = (int) (t * h.Count) % h.Count;
        return h[i < 0 ? i + h.Count : i];
    }
}

/// Cross-fades one drawn character from the clip it was in into the next (keep one per character).
sealed class AnimBlend
{
    const float Fade = 0.16f;
    CharacterModel? _model;
    Clip _clip;
    int _last, _from;
    double _since = -1;

    public (int row, float weight) Step(CharacterModel cm, Clip clip, int row)
    {
        var now = Raylib.GetTime();
        if (cm != _model) { _model = cm; _clip = clip; _since = -1; }
        else if (clip != _clip) { _from = _last; _since = now; _clip = clip; }
        _last = row;
        if (_since < 0) return (0, 0);
        var k = (float) ((now - _since) / Fade);
        if (k >= 1) { _since = -1; return (0, 0); }
        return (_from, 1 - k * k * (3 - 2 * k));
    }
}

/// One Gears environment piece placed in the arena: centred on x/z, standing on y = 0, facing +z.
sealed class PropModel
{
    public required ArenaRow Def;
    public required Model[] Models;
    public int[] Alpha = [];    // each model's Lit alpha mode
    public Vector3 Size;        // metres before yaw
}

/// A Locust gun (a Gears skeletal mesh in its bind pose), barrel along +x, centred.
sealed class HeldGunModel
{
    public required Model[] Models;
    public Vector3 Size;
}

sealed class GunModel
{
    public required WeaponRow Def;
    public required Model[] Models;
    public Vector3 Size;      // metres, x = length along the barrel
    public int Triangles;
}

/// Everything the mashup reads from the player's two installs.
sealed class GameAssets
{
    public readonly Dictionary<string, GunModel> Guns = new();
    public readonly Dictionary<string, CharacterModel> Characters = new();
    public readonly Dictionary<string, HeldGunModel> HeldGuns = new();
    public readonly List<PropModel> Props = new();
    public Texture2D Ground;
    public bool HasGround;
    readonly List<(ArenaRow def, GearsMesh mesh, string?[] textures)> _propData = new();
    GearsTexture? _groundData;
    readonly List<GearsTexture> _decalData = new();
    readonly List<(string, GearsTexture)> _groundDecalData = new();
    /// Gears street decals (cracks, patches, manholes, road markings...) laid on the streets.
    public readonly List<(string name, Texture2D tex)> GroundDecals = new();
    /// name, size in metres (width, length), how many per district
    public static readonly (string name, Vector2 size, int count)[] GroundDecalKinds =
    {
        ("asphalt_crack_decal_01a", new(3, 3), 14), ("asphalt_crack_decal_02a", new(3, 3), 14), ("asphalt_crackseal_01a", new(2.5f, 4), 10), ("asphalt_crackseal_02a", new(2.5f, 4), 10),
        ("asphalt_patch_01a", new(3, 3), 8), ("asphalt_patch_02a", new(3, 2.5f), 8), ("asphalt_patch_03a", new(2.5f, 3), 8), ("road_patch_01a", new(3, 4), 6),
        ("asphalt_pothole_decal_01a", new(1.6f, 1.6f), 10), ("asphalt_pothole_decal_02a", new(1.8f, 1.8f), 10), ("asphalt_pothole_decal_03a", new(1.4f, 1.4f), 8),
        ("burn_decal_01a", new(3.5f, 3.5f), 6), ("burn_decal_03a", new(3, 3), 6), ("debris_rubble_decal_v01_01a", new(3, 3), 10), ("debris_rubble_decal_v03_01a", new(2.5f, 2.5f), 10),
        ("oil_stain_mega_decal_01a", new(3, 3), 6), ("manhole_decal_v01_01a", new(1.2f, 1.2f), 6), ("round_manhole_cover_mega_decal_01a", new(1.3f, 1.3f), 4),
        ("tyre_skid_straight_deferred_decal_01a", new(0.8f, 5), 6), ("road_marking_line_dashed_01a", new(0.25f, 3), 18), ("road_marking_line_straight_damaged_01a", new(0.25f, 6), 10),
        ("road_marking_crosswalk_01a", new(3, 5), 4), ("road_marking_arrow_straight_01a", new(1, 3), 3), ("decal_puddle_concrete_01a_a", new(2.5f, 2.5f), 8),
    };
    /// Gears graffiti and wall decals, put up on the district's walls.
    public readonly List<Texture2D> Decals = new();
    static readonly string[] DecalNames =
    {
        "graffiti_good_cog_v01_decal_01a_d", "graffiti_tag_decal_01a_d", "graffiti_tag_decal_01c_d", "graffiti_tag_decal_01e_d", "graffiti_tag_decal_01g_d",
        "graffiti_wall_decals_v01_01a_d", "graffiti_wall_decals_v01_03a_d", "graffiti_wall_decals_v01_05a_d", "graffiti_wall_decals_v01_09a_d", "graffiti_wall_decals_v01_12a_d",
        "graffiti_wall_decals_v02_02a_d", "graffiti_wall_decals_v02_07a_d", "graffiti_wall_decals_v02_12a_d", "graffiti_wall_decals_v02_19a_d", "graffiti_wall_decals_v02_25a_d",
        "graffiti_wall_decals_v02_31a_d", "painted_sign_decal_glass_v01_01a_d", "amberstone_sign_decal_v01_01a_d",
    };
    /// The Gears map the fights happen in (null = the fort arena from the arena sheet).
    public City? City;
    CityData? _cityData;
    Task? _cityReload;
    public string CityId = "downtown";

    /// Builds another district in the background (the old one stays until the new one is ready).
    public bool SwitchCity(CityMap map)
    {
        if (City != null && CityId == map.Id && _cityReload == null) return true;
        if (_cityReload == null && _gears != null)
        {
            Status = $"Building the {map.Name} district";
            var gears = _gears;
            _cityReload = Task.Run(() =>
            {
                lock (gears)
                {
                    City.Current = map;
                    try { _cityData = City.Build(gears, st => Status = st); }
                    catch (Exception e) { Log.Info($"  city {map.Name} failed: {e.Message}"); _cityData = null; }
                }
            });
        }
        if (_cityReload is not { IsCompleted: true }) return false;
        _cityReload = null;
        if (_cityData is { Batches.Count: > 0 })
        {
            City = City.Upload(_cityData, MakeTexture);
            CityId = City.Current.Id;
            CityId = map.Id;
            Log.Info($"  city {map.Name} on the GPU: {City.Chunks.Count} chunks");
            StartTextureStream();
            // (the distant ring of real cells is off: the district floats in the Abyss, see Abyss.cs)
            WarmUp();
        }
        _cityData = null;
        return true;
    }
    static readonly string[] AbyssCliffs = ["massive_nordic_coastal_cliff_01a", "coastal_cliff_nordic_mega_01a", "cliff_coastal_nordic_mega_01b", "huge_nordic_coastal_cliff_01a", "cliff_coastal_nordic_mega_02a"];
    readonly List<(ArenaRow def, GearsMesh mesh, string?[] tex)> _abyssData = new();
    GearsTexture? _abyssRockData;
    /// The Abyss around the district: Gears cliff faces and their rock (see Abyss).
    public readonly List<PropModel> AbyssCliffModels = new();
    public Texture2D AbyssRock;
    // (Gears foliage keeps its leaf masks where the game can't read them, so only plants that need none: the red pine,
    // whose needles carry their own mask, deadwood, twiggy burnt bushes and blade-geometry grass)
    static readonly (string path, string kind)[] AbyssPlants =
    [
        ("Trees_Speedtree/Mesh/tree_pine_red_v01_01c", "tree"), ("Trees_Megascans/Mesh/tree_dead_v01_05a", "small"),
        ("Bush/Mesh/bush_burn_v01_01a", "bush"), ("Bush/Mesh/bush_broom_dead_v01_01a", "bush"),
        ("Grass/Mesh/grass_v01_cluster_03a", "grass"), ("Grass/Mesh/SM_FieldGrass_01", "grass"),
    ];
    readonly List<(ArenaRow def, GearsMesh mesh, string?[] tex)> _abyssPlantData = new();
    GearsTexture? _abyssSoilData, _abyssWallData;
    volatile GearsTexture? _brandData;

    /// A decoded Gears texture as a raylib image (RGBA, uncompressed: for UI art drawn with its own alpha).
    static unsafe Image ImageOf(GearsTexture t)
    {
        var img = Raylib.GenImageColor(t.Width, t.Height, Color.Blank);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        new Span<byte>(t.Rgba).CopyTo(new Span<byte>(img.Data, t.Width * t.Height * 4));
        return img;
    }
    /// Gears trees, bushes and grass for the island (Def.Role: tree, small, bush, grass), and the rocky soil under them.
    public readonly List<PropModel> AbyssPlantModels = new();
    public Texture2D AbyssSoil;
    /// A retaining wall for the raised plazas and ground storeys filled in under the city's floating pieces.
    public Texture2D AbyssWall;
    readonly List<(SoundRow def, List<GearsSound> main, List<GearsSound> thump)> _soundData = new();
    readonly System.Collections.Concurrent.ConcurrentQueue<(SoundRow, List<GearsSound>, List<GearsSound>)> _lateSounds = new();

    /// Hands sounds decoded in the background to the audio device (main thread, one a frame).
    public bool LateSoundsDone => _lateDone && _lateSounds.IsEmpty;
    volatile bool _lateDone;

    public void PumpLateSounds()
    {
        if (_lateSounds.TryDequeue(out var s)) Sfx.AddGears(s.Item1, s.Item2, s.Item3);
    }

    /// Keeps the first `seconds` of a recording (0 = all of it), fading out over the last quarter second.
    static GearsSound Trim(GearsSound s, float seconds)
    {
        if (seconds <= 0 || s.Duration <= seconds) return s;
        var frames = (int) (seconds * s.Rate);
        var fade = Math.Min(frames, s.Rate / 4);
        var samples = new short[frames * s.Channels];
        for (var f = 0; f < frames; f++)
        {
            var g = f < frames - fade ? 1f : (frames - f) / (float) fade;
            for (var c = 0; c < s.Channels; c++) samples[f * s.Channels + c] = (short) (s.Samples[f * s.Channels + c] * g);
        }
        return new GearsSound { Name = s.Name, Channels = s.Channels, Rate = s.Rate, Samples = samples, Rms = s.Rms, Roughness = s.Roughness };
    }
    readonly List<(string id, GearsSkinnedMesh mesh, string?[] textures)> _heldGunData = new();
    /// Each class's hand weapon (while the guns are off), keyed by class id.
    public readonly Dictionary<string, HeldGunModel> HandWeapons = new();
    readonly List<(string cls, float[] pos, float[] nrm, float[] uv, uint[] idx, (short Material, int First, int Triangles)[] sections, string?[] tex)> _handData = new();
    readonly Dictionary<string, GearsTexture> _charTextures = new();
    List<BakedCharacter> _baked = new();
    readonly object _pending = new();
    readonly Dictionary<string, GearsSkeleton?> _skeletons = new();
    readonly Dictionary<string, Texture2D> _gpuTextures = new();
    GearsInstall? _gears;
    Task? _heroLoading;
    string? _heroLoadingId;
    string? _firstHero;
    public Texture2D Splash;
    public bool HasSplash;
    public Font Font;
    public string Status = "Starting";
    public readonly List<string> Errors = new();

    Task<List<GunData>>? _loading;

    const string WeaponsRoot = "FairlightConcept/Content/Fairlight/Weapons/";

    /// Starts reading a hero the player picked; true once it is on the GPU.
    public bool RequestCharacter(string id)
    {
        if (Characters.ContainsKey(id)) return true;
        if (_heroLoading == null && _gears != null && _loading == null)
        {
            var row = Sheets.Characters.First(c => c.Id == id);
            _heroLoadingId = id;
            Status = $"Reading {row.Name} from Gears of War: E-Day";
            _heroLoading = Task.Run(() => { lock (_gears) LoadCharacter(_gears, row); });
        }
        if (_heroLoading is { IsCompleted: true })
        {
            if (_heroLoading.IsFaulted) { Log.Info("hero load failed: " + _heroLoading.Exception!.GetBaseException().Message); Errors.Add(_heroLoading.Exception!.GetBaseException().Message); }
            _heroLoading = null;
            UploadPendingCharacters();
            if (!Characters.ContainsKey(_heroLoadingId!)) return true; // failed: fall back to whatever can be drawn
        }
        return Characters.ContainsKey(id);
    }

    RenderTexture2D _warmTarget;

    /// Draws the city's chunks from `first` on once off screen (the driver uploads on first draw).
    void WarmChunks(int first)
    {
        if (City == null) return;
        if (_warmTarget.Id == 0) _warmTarget = Raylib.LoadRenderTexture(16, 16);
        Raylib.BeginTextureMode(_warmTarget);
        Raylib.BeginMode3D(new Camera3D(new Vector3(0, 1, 4), Vector3.Zero, Vector3.UnitY, 45, CameraProjection.Perspective));
        for (var i = first; i < City.Chunks.Count; i++) Raylib.DrawModel(City.Chunks[i].Model, Vector3.Zero, 1, Color.White);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    /// Draws every character clip, gun and hand weapon once off screen, so the driver has set them all up
    /// before the first wave (no hitch the first time a new Locust or skill pose appears).
    /// After the first load: the decoded character textures are all on the GPU now (a hero picked later reads any
    /// it lacks again), and the loading garbage goes back to Windows.
    public void ReleaseLoadingCaches()
    {
        long bytes;
        lock (_pending) { bytes = _charTextures.Values.Sum(t => (long) t.Rgba.Length); _charTextures.Clear(); }
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        Log.Memory($"after loading (freed {bytes / 1048576} MB of character textures)");
    }

    public void WarmUp()
    {
        var t0 = Environment.TickCount64;
        var rt = Raylib.LoadRenderTexture(64, 64);
        Raylib.BeginTextureMode(rt);
        Raylib.BeginMode3D(new Camera3D(new Vector3(0, 1, 4), new Vector3(0, 1, 0), Vector3.UnitY, 45, CameraProjection.Perspective));
        foreach (var c in Characters.Values)
            foreach (var clip in c.Rows.Keys) c.Draw(clip, 0.5f, Matrix4x4.Identity, Vector3.Zero, Color.White);
        foreach (var g in HeldGuns.Values.Concat(HandWeapons.Values))
            foreach (var m in g.Models) Raylib.DrawModel(m, Vector3.Zero, 1, Color.White);
        City?.DrawAll();
        foreach (var p in Props)
            foreach (var m in p.Models) Raylib.DrawModel(m, Vector3.Zero, 1, Color.White);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        Raylib.UnloadRenderTexture(rt);
        Rlgl.DrawRenderBatchActive();
        Log.Info($"warm-up: {Characters.Count} characters, {HeldGuns.Count + HandWeapons.Count} weapons, {City?.Chunks.Count ?? 0} city chunks, {Props.Count} props drawn once in {Environment.TickCount64 - t0} ms");
    }

    // ---------------------------------------------------------------- texture streaming

    readonly System.Collections.Concurrent.ConcurrentQueue<(City city, string path, GearsTexture tex)> _streamed = new();
    int _streamGen;

    /// The streamed city textures' size for the graphics preset: Low keeps memory down.
    static int TextureBudget => Settings.S.Preset switch { 0 => Math.Min(256, Sheets.CityTextureSize), _ => Sheets.CityTextureSize };

    /// Reads the city's full-size textures in the background, nearest the start first (the city starts with small ones).
    void StartTextureStream()
    {
        if (City is not { } city || _gears is not { } gears) return;
        var gen = ++_streamGen;
        _streamDone = false; _compacted = false;
        var order = city.StreamOrder(Vector3.Zero);
        var t0 = Environment.TickCount64;
        Task.Run(() =>
        {
            foreach (var path in order)
            {
                if (gen != _streamGen) return; // another district took over
                GearsTexture? full = null;
                try { lock (gears) full = gears.LoadTexture(path, TextureBudget); } catch { }
                if (full != null) _streamed.Enqueue((city, path, full));
            }
            Log.Info($"  texture streaming: {order.Count} full-size city textures read in {(Environment.TickCount64 - t0) / 1000.0:0.0} s");
            Log.Memory("after texture streaming");
            _streamDone = true;
        });
    }

    // ---------------------------------------------------------------- the distant ring of cells

    CityData? _farData;
    City? _farFor;

    /// Reads the cells around the district in the background (big pieces only) to replace the painted skyline.
    void StartFarRing()
    {
        if (City is not { } city || _gears is not { } gears) return;
        var gen = _streamGen;
        var ring = City.RingCells(City.Current);
        if (ring.Length == 0) return;
        var t0 = Environment.TickCount64;
        Task.Run(() =>
        {
            CityData? far = null;
            try { lock (gears) far = City.Build(gears, _ => { }, ring, Sheets.CityDrawRadius, 300, distant: true); }
            catch (Exception e) { Log.Info("distant ring failed: " + e.Message); }
            if (far == null || gen != _streamGen) return;
            Log.Info($"  distant ring: {ring.Length} cells read in {(Environment.TickCount64 - t0) / 1000.0:0.0} s");
            Log.Memory("after reading the distant ring");
            _farFor = city; _farData = far;
        });
    }

    /// Call once a frame: puts a couple of streamed textures on the GPU.
    static readonly bool _memLog = Environment.GetEnvironmentVariable("DAEVAS_MEMLOG") == "1";
    long _memNext;
    volatile bool _streamDone;
    bool _compacted;

    public void PumpTextureStream()
    {
        if (_brandData != null) { Brand.Gears = Raylib.LoadTextureFromImage(ImageOf(_brandData)); Raylib.SetTextureFilter(Brand.Gears, TextureFilter.Bilinear); _brandData = null; }
        // dev: DAEVAS_MEMLOG=1 logs memory every 5 s (with what the asset caches hold)
        if (_memLog && Environment.TickCount64 > _memNext)
        {
            _memNext = Environment.TickCount64 + 5000;
            long ct; lock (_pending) ct = _charTextures.Values.Sum(t => (long) t.Rgba.Length);
            Log.Memory($"(char textures {ct / 1048576} MB, city chunks {City?.Chunks.Count ?? 0})");
        }
        if (_farData is { } fd && _farFor == City && City != null)
        {
            // the distant ring goes up a few dozen chunks a frame, each drawn once off screen (no hitch when it appears)
            var part = new CityData();
            foreach (var kv in fd.Textures) part.Textures[kv.Key] = kv.Value;
            var take = Math.Min(24, fd.Batches.Count);
            part.Batches.AddRange(fd.Batches.GetRange(0, take));
            fd.Batches.RemoveRange(0, take);
            var first = City.Chunks.Count;
            City.AddBatches(part, MakeTexture, true);
            WarmChunks(first);
            if (fd.Batches.Count == 0) { _farData = null; Log.Memory("after uploading the distant ring"); }
        }
        // uploads by time, not count: up to about 2 ms a frame, so a frame never hitches on a big texture
        var budget = System.Diagnostics.Stopwatch.StartNew();
        while (budget.Elapsed.TotalMilliseconds < 2 && _streamed.TryDequeue(out var s))
            if (s.city == City) s.city.Upgrade(s.path, MakeTexture(s.tex));
        // everything streamed and on the GPU: hand the loading garbage back to Windows, once
        if (_streamDone && _streamed.IsEmpty && _farData == null && !_compacted)
        {
            _compacted = true;
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            Log.Memory("once the city was all streamed");
        }
    }

    void UploadPendingCharacters()
    {
        List<BakedCharacter> baked; List<(string id, GearsSkinnedMesh mesh, string?[] textures)> guns;
        lock (_pending) { baked = _baked.ToList(); _baked.Clear(); guns = _heldGunData.ToList(); _heldGunData.Clear(); }
        foreach (var b in baked) UploadCharacter(b, _gpuTextures);
        foreach (var (id, gun, texPaths) in guns) UploadHeldGun(id, gun, texPaths, _gpuTextures);
        List<(string cls, float[] pos, float[] nrm, float[] uv, uint[] idx, (short Material, int First, int Triangles)[] sections, string?[] tex)> hands;
        lock (_pending) { hands = _handData.ToList(); _handData.Clear(); }
        foreach (var h in hands)
        {
            foreach (var tp in h.tex)
                if (tp != null && !_gpuTextures.ContainsKey(tp) && _charTextures.TryGetValue(tp, out var ct)) _gpuTextures[tp] = MakeTexture(ct);
            HandWeapons[h.cls] = BuildHand(h.pos, h.nrm, h.uv, h.idx, h.sections, h.tex);
        }
    }

    public void Begin(string? firstHero = null)
    {
        _firstHero = firstHero;
        LoadFont();
        LoadSplash();
        _loading = Task.Run(LoadGunsFromGears);
    }

    /// True once the guns are on the GPU (or failed).
    public bool Poll()
    {
        if (_loading is not { IsCompleted: true }) return false;
        if (_loading.IsFaulted) { Errors.Add("Gears of War: E-Day: " + _loading.Exception!.GetBaseException().Message); Log.Info("loading failed: " + _loading.Exception!.GetBaseException()); _loading = null; return true; }
        var textures = _gpuTextures;
        foreach (var g in _loading.Result)
        {
            if (g.Full != null)
            {
                var (models, size) = BuildRigid(g.Full, g.FullTextures, textures);
                Guns[g.Def.Id] = new GunModel { Def = g.Def, Models = models, Size = size, Triangles = g.Full.Indices.Length / 3 };
            }
            else Upload(g);
        }
        Log.Info($"Gears guns ready: {string.Join(", ", Guns.Values.Select(g => $"{g.Def.Name} ({g.Triangles} tris)"))}");
        UploadPendingCharacters();
        foreach (var (def, mesh, texPaths) in _propData) UploadProp(def, mesh, texPaths, textures);
        if (_cityData != null)
        {
            City = City.Upload(_cityData, MakeTexture);
            Log.Info($"  city on the GPU: {City.Chunks.Count} chunks, {City.Triangles:N0} triangles");
            Log.Memory("after the city went to the GPU");
            _cityData = null;
            StartTextureStream();
            // (the distant ring of real cells is off: the district floats in the Abyss, see Abyss.cs)
        }
        if (_groundData != null) { Ground = MakeTexture(_groundData); HasGround = Ground.Id != 0; }
        foreach (var (def, mesh, texPaths) in _abyssData) AbyssCliffModels.Add(BuildProp(def, mesh, texPaths, textures));
        _abyssData.Clear();
        foreach (var (def, mesh, texPaths) in _abyssPlantData) AbyssPlantModels.Add(BuildProp(def, mesh, texPaths, textures));
        _abyssPlantData.Clear();
        if (_abyssSoilData != null) { AbyssSoil = MakeTexture(_abyssSoilData); _abyssSoilData = null; }
        if (_abyssWallData != null) { AbyssWall = MakeTexture(_abyssWallData); _abyssWallData = null; }
        if (_abyssRockData != null) { AbyssRock = MakeTexture(_abyssRockData); _abyssRockData = null; }
        foreach (var d in _decalData) { var t = MakeTexture(d); if (t.Id != 0) Decals.Add(t); }
        foreach (var (name, d) in _groundDecalData) { var t = MakeTexture(d); if (t.Id != 0) GroundDecals.Add((name, t)); }
        _groundDecalData.Clear();
        _decalData.Clear();
        foreach (var (def, main, thump) in _soundData) Sfx.AddGears(def, main, thump);
        // music, ambience and the hero's sounds keep loading in the background while you play
        var lateGears = _gears;
        if (lateGears != null)
            Task.Run(() =>
            {
                foreach (var so in Sheets.Sounds.Where(LateSound))
                {
                    (SoundRow, List<GearsSound>, List<GearsSound>)? d;
                    lock (lateGears) d = DecodeSound(lateGears, so);
                    if (d is { } ok) _lateSounds.Enqueue(ok);
                }
                _lateDone = true;
            });
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        Log.Info($"textures on the GPU: {TextureBytes / 1048576} MB ({TextureBytesRaw / 1048576} MB uncompressed)");
        Log.Info($"memory after characters: managed {GC.GetTotalMemory(false) / 1048576} MB, process {Environment.WorkingSet / 1048576} MB, frames {Characters.Values.Sum(c => c.Bones.Height)}, bones {Characters.Values.Sum(c => c.Bones.Width / 3)}");
        Log.Info($"Gears characters ready: {string.Join(", ", Characters.Values.Select(c => $"{c.Def.Name} ({c.Triangles} tris, {c.Height:0.00} m)"))}");
        Log.Memory("when the characters were ready");
        _loading = null;
        return true;
    }

    void LoadFont()
    {
        var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeuib.ttf");
        var glyphs = new int[96]; // printable ASCII and the copyright sign for the credits
        for (var i = 0; i < 95; i++) glyphs[i] = 32 + i;
        glyphs[95] = 0xA9;
        Font = File.Exists(f) ? Raylib.LoadFontEx(f, Raylib.GetScreenHeight() > 1300 ? 144 : 72, glyphs, glyphs.Length) : Raylib.GetFontDefault();
        Raylib.SetTextureFilter(Font.Texture, TextureFilter.Bilinear);
    }

    void LoadSplash()
    {
        if (Paths.Aion == null) { Errors.Add("AION 2 was not found on this PC."); return; }
        var p = Path.Combine(Paths.Aion, Paths.AionSplash);
        if (!File.Exists(p)) { Errors.Add("AION 2's Splash.bmp is missing: " + p); return; }
        var img = Raylib.LoadImage(p);
        Splash = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(Splash, TextureFilter.Bilinear);
        HasSplash = Splash.Id != 0;
        Log.Info($"AION 2 splash read from {p} ({Splash.Width}x{Splash.Height})");
        Brand.CutAionLogo(p);
    }

    List<GunData> LoadGunsFromGears()
    {
        if (Paths.Gears == null) throw new DirectoryNotFoundException("Gears of War: E-Day was not found on this PC.");
        Status = "Opening Gears of War: E-Day";
        var t0 = Environment.TickCount64;
        GearsInstall.WemDecoder = Path.Combine(AppContext.BaseDirectory, "vgmstream", "vgmstream-cli.exe");
        var gears = _gears = GearsInstall.Open(Paths.Gears);
        // the Gears of War: E-Day logo for the lockup (shown as soon as it's read)
        try { _brandData = gears.LoadTexture("FairlightConcept/Content/Fairlight/Interface/Art/Brand/T_UI_logo_gears_eday.uasset", 1024); } catch { }
        Log.Info($"Gears archives mounted: {gears.FileCount} files in {Environment.TickCount64 - t0} ms from {Paths.Gears}");
        var result = new List<GunData>();
        foreach (var w in Sheets.GunsEnabled ? Sheets.Weapons : [])
        {
            Status = $"Reading the {w.Name} from Gears of War: E-Day";
            var parts = new List<GearsMesh>();
            foreach (var p in w.Parts)
            {
                var path = $"{WeaponsRoot}{w.PartsFolder}/{p}_SM01.uasset";
                var m = gears.LoadMesh(path);
                if (m == null) Log.Info($"  {w.Id}: part {p} not read ({gears.LastError})");
                else parts.Add(m);
            }
            var tex = gears.LoadTexture($"{WeaponsRoot}{w.Texture}.uasset", 1024);
            GearsSkinnedMesh? full = null;
            string?[] fullTex = [];
            if (w.Model != "parts")
            {
                full = gears.LoadSkinned($"{WeaponsRoot}{w.Model}.uasset");
                if (full == null) Log.Info($"  {w.Id}: full model not read ({gears.LastError}), using parts");
                else
                {
                    var fallback = $"{WeaponsRoot}{w.Texture}.uasset";
                    fullTex = full.Sections.Select(s => (s.Material >= 0 && s.Material < full.Materials.Length ? gears.FindAlbedo(full.Materials[s.Material]) : null) ?? fallback).ToArray();
                    foreach (var tp in fullTex.Distinct())
                        if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 1024) is { } gt) _charTextures[tp] = gt;
                }
            }
            Log.Info($"  {w.Id}: {parts.Count}/{w.Parts.Length} parts, texture {(tex == null ? "missing" : $"{tex.Width}x{tex.Height}")}");
            if (parts.Count == 0) throw new InvalidDataException($"No parts of the {w.Name} could be read.");
            result.Add(new GunData { Def = w, Parts = parts, Texture = tex, Full = full, FullTextures = fullTex });
        }
        // Enemies load now; heroes load when their class deploys (see RequestCharacter).
        if (!Sheets.GunsEnabled)
            foreach (var cls in Sheets.Classes.Where(c => c.HandWeapon != "none"))
            {
                Status = $"Reading the {cls.Name}'s weapon from Gears of War: E-Day";
                LoadHandWeapon(gears, cls);
            }
        foreach (var ch in Sheets.Characters)
            if (Sheets.Enemies.Any(e => e.Character == ch.Id) || ch.Id is "streetlamp" or "trafficlight" or "dom" or "marcus" or "carmine" || Environment.GetEnvironmentVariable("DAEVAS_ALL_CHARACTERS") == "1" || ch.Id == _firstHero)
                LoadCharacter(gears, ch);
        if (Sheets.CityEnabled)
        {
            var tc = Environment.TickCount64;
            City.Current = City.Maps.FirstOrDefault(m => m.Id == Profile.P.Map && City.Unlocked(m)) ?? City.Maps[0];
            try { _cityData = City.Build(gears, st => Status = st); }
            catch (Exception e) { Log.Info($"  city failed: {e.Message}"); }
            if (_cityData is { Batches.Count: 0 }) _cityData = null;
            Log.Info($"  city {(_cityData == null ? "not built, using the fort arena" : "built")} in {Environment.TickCount64 - tc} ms");
        }
        foreach (var a in Sheets.Arena.Where(r => (r.Role == "street") == (_cityData != null)))
        {
            Status = "Building the arena from Gears of War: E-Day";
            var m = gears.LoadMesh($"FairlightConcept/Content/Fairlight/Environments/{a.Mesh}.uasset");
            if (m == null) { Log.Info($"  arena {a.Id}: not read ({gears.LastError})"); continue; }
            var texPaths = m.Sections.Select(s => s.Material >= 0 && s.Material < m.Materials.Length ? gears.FindAlbedo(m.Materials[s.Material]) : null).ToArray();
            foreach (var tp in texPaths.Distinct())
                if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 1024) is { } pt) _charTextures[tp] = pt;
            _propData.Add((a, m, texPaths));
        }
        foreach (var so in Sheets.Sounds.Where(x => !LateSound(x)))
        {
            Status = "Reading Gears of War: E-Day sounds";
            if (DecodeSound(gears, so) is { } d) _soundData.Add(d);
        }
        _groundData = gears.LoadTexture($"FairlightConcept/Content/Fairlight/Environments/{(_cityData != null ? Sheets.CityGroundTexture : Sheets.ArenaGroundTexture)}.uasset", 1024);
        Log.Info($"  arena: {_propData.Count} pieces, ground {(_groundData == null ? "missing" : "ok")}");
        Status = "Reading Gears graffiti";
        foreach (var name in DecalNames)
            if (gears.LoadTexture($"FairlightConcept/Content/Fairlight/Environments/Decals/Textures/{name}.uasset", 512) is { } dt) _decalData.Add(dt);
        Log.Info($"  graffiti: {_decalData.Count}/{DecalNames.Length} decals");
        Status = "Reading Gears street decals";
        foreach (var (name, _, _) in GroundDecalKinds)
            if (gears.LoadTexture($"FairlightConcept/Content/Fairlight/Environments/Decals/Textures/{name}_d.uasset", 512) is { } gt) _groundDecalData.Add((name, gt));
        Log.Info($"  street decals: {_groundDecalData.Count}/{GroundDecalKinds.Length}");
        if (_cityData != null)
        {
            // the Abyss around the district: Gears cliff faces for its rim and the floating islands, and their rock
            Status = "Raising the district into the Abyss";
            foreach (var name in AbyssCliffs)
            {
                var m = gears.LoadMesh($"FairlightConcept/Content/Fairlight/Environments/Sets/TechTest/Props/Rock/Mesh/{name}.uasset");
                if (m == null) { Log.Info($"  abyss {name}: not read ({gears.LastError})"); continue; }
                var texPaths = m.Sections.Select(x => x.Material >= 0 && x.Material < m.Materials.Length ? gears.FindAlbedo(m.Materials[x.Material]) : null).ToArray();
                foreach (var tp in texPaths.Distinct())
                    if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 1024) is { } ct) _charTextures[tp] = ct;
                _abyssData.Add((new ArenaRow("abyss-" + name, name, 0, 0, 0, "abyss", 1), m, texPaths));
            }
            // Gears trees, bushes and grass for the island's edges and the floating islands
            foreach (var (path, kind) in AbyssPlants)
            {
                var m = gears.LoadMesh($"FairlightConcept/Content/Fairlight/Environments/Vegetation/{path}.uasset");
                if (m == null) { Log.Info($"  abyss plant {path}: not read ({gears.LastError})"); continue; }
                var texPaths = m.Sections.Select(x => x.Material >= 0 && x.Material < m.Materials.Length ? gears.FindAlbedo(m.Materials[x.Material]) : null).ToArray();
                foreach (var tp in texPaths.Distinct())
                    if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 1024) is { } ct) _charTextures[tp] = ct;
                _abyssPlantData.Add((new ArenaRow("plant-" + path[(path.LastIndexOf('/') + 1)..], path, 0, 0, 0, kind, 1), m, texPaths));
            }
            _abyssWallData = gears.LoadTexture("FairlightConcept/Content/Fairlight/Environments/Tiling/Textures/canal_wall_v01_01a_d.uasset", 1024);
            _abyssSoilData = gears.LoadTexture("FairlightConcept/Content/Fairlight/Environments/Tiling/Textures/rocky_soil_ground_mega_01a_d.uasset", 1024);
            _abyssRockData = gears.LoadTexture("FairlightConcept/Content/Fairlight/Environments/Sets/TechTest/Props/Rock/Textures/massive_nordic_coastal_cliff_01a_d.uasset", 1024);
            Log.Info($"  abyss: {_abyssData.Count}/{AbyssCliffs.Length} cliffs, rock {(_abyssRockData == null ? "missing" : "ok")}");
        }
        Status = "Gears guns, characters and arena ready";
        return result;
    }

    static bool LateSound(SoundRow so) => so.Owner is "music" or "ambience" or "hero" or "ui" or "world";

    /// Decodes one sound row's recordings from the install.
    (SoundRow, List<GearsSound>, List<GearsSound>)? DecodeSound(GearsInstall gears, SoundRow so)
    {
        try
        {
            var all = gears.LoadEventSounds($"FairlightConcept/Content/WwiseAudio/Events/Fairlight_Events/{so.Event}.uasset", LateSound(so) ? 12 : int.MaxValue);
            List<GearsSound> main = all, thump = new();
            if (so.Pick == "shot")
            {
                // a full shot (clean, not too short) plus a short low thump layer; mechanical clicks are left out
                main = all.Where(s => s.Duration >= 0.3f && s.Roughness < 0.5f).ToList();
                thump = all.Where(s => s.Duration < 0.35f && s.Roughness < 0.05f).ToList();
                if (main.Count == 0) main = all;
            }
            main = main.Take(so.Owner == "music" ? 1 : so.Owner == "ambience" ? 3 : 8).Select(m => Trim(m, so.MaxLength)).ToList(); thump = thump.Take(4).ToList();
            Log.Info($"  sound {so.Id}: {all.Count} recordings, using {main.Count} main ({string.Join(", ", main.Select(m => $"{m.Duration:0.00}s"))}) + {thump.Count} thumps");
            return (so, main, thump);
        }
        catch (Exception e) { Log.Info($"  sound {so.Id} failed: {e.Message}"); }
        return null;
    }

    /// Reads, animates and bakes one Gears character (and its held gun) on the calling thread.
    void LoadCharacter(GearsInstall gears, CharacterRow ch)
    {
        Status = $"Reading {ch.Name} from Gears of War: E-Day";
        var t1 = Environment.TickCount64;
        var parts = new List<GearsSkinnedMesh>();
        foreach (var p in ch.Parts)
        {
            var m = gears.LoadSkinned($"FairlightConcept/Content/Fairlight/{(p.StartsWith("AnimProps/") ? "" : "Characters/")}{p}.uasset", ch.Lod);
            if (m == null) Log.Info($"  {ch.Id}: part {p} not read ({gears.LastError})");
            else parts.Add(m);
        }
        if (parts.Count == 0) throw new InvalidDataException($"{ch.Name} could not be read.");
        Status = $"Animating {ch.Name}";
        var clips = new Dictionary<Clip, Skin.ClipSource>();
        var skel = _skeletons.TryGetValue(ch.AnimSkeleton, out var cached) ? cached
            : _skeletons[ch.AnimSkeleton] = gears.LoadSkeleton($"FairlightConcept/Content/Fairlight/{(ch.AnimSkeleton.StartsWith("AnimProps/") ? "" : "Characters/")}{ch.AnimSkeleton}.uasset");
        if (skel == null) Log.Info($"  {ch.Id}: skeleton {ch.AnimSkeleton} not read ({gears.LastError}), using procedural motion");
        var wanted = new List<(Clip, string)> { (Clip.Idle, ch.IdleAnim), (Clip.Walk, ch.WalkAnim), (Clip.Attack, ch.AttackAnim), (Clip.Aim, ch.IdleAnim), (Clip.Spawn, ch.SpawnAnim), (Clip.Death, ch.DeathAnim), (Clip.Evade, ch.EvadeAnim) };
        if (ch.ExtraAnims != "none")
            foreach (var pair in ch.ExtraAnims.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && Enum.TryParse<Clip>(kv[0], true, out var extra))
                {
                    wanted.RemoveAll(w => w.Item1 == extra);
                    wanted.Add((extra, kv[1]));
                }
            }
        foreach (var (clip, path) in wanted)
        {
            if (skel == null || path == "none") continue;
            try
            {
                var anim = gears.LoadAnim($"FairlightConcept/Content/Fairlight/Animation/Gameplay/{path}.uasset", skel);
                if (anim != null) clips[clip] = new Skin.ClipSource { Anim = anim, Skeleton = skel };
                else Log.Info($"  {ch.Id}: {clip} animation not read ({gears.LastError})");
            }
            catch (Exception e) { Log.Info($"  {ch.Id}: {clip} animation failed ({e.Message})"); }
        }
        var baked = Skin.Bake(ch, parts, gears.FindAlbedo, clips);
        var size = ch.Id == "marcus" ? 1024 : 512;
        foreach (var path in baked.TexturePaths)
            if (!_charTextures.ContainsKey(path) && gears.LoadTexture(path, size) is { } tex) _charTextures[path] = tex;
        lock (_pending) _baked.Add(baked);
        var gun = ch.HeldGun == "none" ? null : gears.LoadSkinned($"FairlightConcept/Content/Fairlight/Weapons/{ch.HeldGun}.uasset");
        if (ch.HeldGun != "none") Log.Info($"  {ch.Id}: held gun {ch.HeldGun} {(gun == null ? "not read: " + gears.LastError : $"{gun.Indices.Length / 3} tris")}");
        if (gun != null)
        {
            var texPaths = gun.Sections.Select(s => s.Material >= 0 && s.Material < gun.Materials.Length ? gears.FindAlbedo(gun.Materials[s.Material]) : null).ToArray();
            foreach (var tp in texPaths)
                if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 512) is { } gt) _charTextures[tp] = gt;
            lock (_pending) _heldGunData.Add((ch.Id, gun, texPaths));
        }
        Log.Info($"  {ch.Id}: {parts.Count}/{ch.Parts.Length} parts, {baked.TexturePaths.Count} textures, Gears animations: {string.Join(" ", baked.FromGears.Select(c => $"{c}({baked.Duration[c]:0.0}s)"))}, {Environment.TickCount64 - t1} ms");
    }

    static readonly bool RawTextures = Environment.GetEnvironmentVariable("DAEVAS_RAWTEX") == "1";
    /// Texture memory on the GPU (with mips), and what it would be uncompressed.
    public static long TextureBytes, TextureBytesRaw;

    unsafe Texture2D MakeTexture(GearsTexture t)
    {
        Responsive.Tick();
        TextureBytesRaw += t.Rgba.Length * 4L / 3;
        if (!RawTextures && t.Width >= 4 && t.Height >= 4)
        {
            // block-compressed, with the mip chain built on the CPU
            var alpha = Bc.HasAlpha(t.Rgba);
            var (data, mips) = Bc.Encode(t.Rgba, t.Width, t.Height, alpha);
            var mem = (byte*) Raylib.MemAlloc((uint) data.Length);
            Marshal.Copy(data, 0, (IntPtr) mem, data.Length);
            var cimg = new Image { Data = mem, Width = t.Width, Height = t.Height, Mipmaps = mips, Format = alpha ? PixelFormat.CompressedDxt5Rgba : PixelFormat.CompressedDxt1Rgb };
            var ctex = Raylib.LoadTextureFromImage(cimg);
            Raylib.UnloadImage(cimg);
            if (ctex.Id != 0)
            {
                Raylib.SetTextureFilter(ctex, TextureFilter.Trilinear);
                Raylib.SetTextureWrap(ctex, TextureWrap.Repeat);
                TextureBytes += data.Length;
                return ctex;
            }
        }
        TextureBytes += t.Rgba.Length * 4L / 3;
        var bytes = (byte*) Raylib.MemAlloc((uint) t.Rgba.Length);
        Marshal.Copy(t.Rgba, 0, (IntPtr) bytes, t.Rgba.Length);
        var img = new Image { Data = bytes, Width = t.Width, Height = t.Height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
        return tex;
    }

    void UploadProp(ArenaRow def, GearsMesh g, string?[] texPaths, Dictionary<string, Texture2D> textures) =>
        Props.Add(BuildProp(def, g, texPaths, textures));

    /// A static Gears mesh on the GPU as one or more models (origin at its base centre, metres, y up).
    unsafe PropModel BuildProp(ArenaRow def, GearsMesh g, string?[] texPaths, Dictionary<string, Texture2D> textures)
    {
        var n = g.Positions.Length / 3;
        var pts = new Vector3[n];
        for (var i = 0; i < n; i++) pts[i] = new Vector3(g.Positions[i * 3], g.Positions[i * 3 + 2], g.Positions[i * 3 + 1]) * 0.01f;
        var min = pts.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var max = pts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var shift = new Vector3((min.X + max.X) / 2, min.Y, (min.Z + max.Z) / 2);
        var models = new List<Model>();
        var sections = g.Sections.Length > 0 ? g.Sections : [((short) 0, 0, g.Indices.Length / 3)];
        int kept = 0, total = 0, grey = 0;
        var alphas = new List<int>();
        for (var k = 0; k < sections.Length; k++)
        {
            var (_, first, tris) = sections[k];
            if (tris <= 0) continue;
            total += tris;
            // a section can hold more vertices than one 16-bit mesh: it goes up in as many pieces as it needs
            for (var t = 0; t < tris;)
            {
            var remap = new Dictionary<uint, ushort>();
            var ps = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var idx = new List<ushort>();
            for (; t < tris && remap.Count < 64990; t++)
                for (var c = 0; c < 3; c++)
                {
                    var vi = g.Indices[first + t * 3 + c];
                    if (!remap.TryGetValue(vi, out var ni))
                    {
                        ni = (ushort) remap.Count; remap[vi] = ni;
                        ps.Add(pts[vi] - shift);
                        ns.Add(new Vector3(g.Normals[vi * 3], g.Normals[vi * 3 + 2], g.Normals[vi * 3 + 1]));
                        us.Add(new Vector2(g.Uvs[vi * 2], g.Uvs[vi * 2 + 1]));
                    }
                    idx.Add(ni);
                }
            if (idx.Count < 3) continue;
            var mesh = new Mesh(ps.Count, idx.Count / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
            ps.ToArray().AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
            ns.ToArray().AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
            us.ToArray().AsSpan().CopyTo(mesh.TexCoordsAs<Vector2>());
            idx.ToArray().AsSpan().CopyTo(mesh.IndicesAs<ushort>());
            Raylib.UploadMesh(ref mesh, false);
            var model = Lit.Make(mesh);
            var tp = k < texPaths.Length ? texPaths[k] : null;
            if (tp != null && (textures.TryGetValue(tp, out var tex) || (_charTextures.TryGetValue(tp, out var data) && (textures[tp] = tex = MakeTexture(data)).Id != 0)))
                model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
            else { model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(110, 104, 96, 255); grey++; }
            models.Add(model);
            // props are solid things: only glass may be see-through; any other mostly-clear mask is cut out crisply
            var mode = tp != null && _charTextures.TryGetValue(tp, out var td) ? Lit.AlphaFor(tp, td.Rgba) : Lit.Opaque;
            if (mode == Lit.Blended && !tp!.Contains("glass", StringComparison.OrdinalIgnoreCase)) mode = Lit.Cutout;
            alphas.Add(mode);
            kept += idx.Count / 3;
            }
        }
        Log.Info($"  prop {def.Id}: {kept}/{total} triangles in {models.Count} pieces (alpha {string.Join("", alphas)}{(def.Role is "tree" or "small" or "bush" or "grass" ? " " + string.Join(",", texPaths.Select(t => t == null ? "-" : t[(t.LastIndexOf('/') + 1)..])) + " | " + string.Join(",", g.Materials.Select(m => m[(m.LastIndexOf('.') + 1)..])) : "")}), {sections.Length - texPaths.Count(t => t != null)} of {sections.Length} sections without a Gears texture" +
                 (grey > 0 ? $" ({string.Join(", ", sections.Select((x, i) => (x, i)).Where(q => q.i >= texPaths.Length || texPaths[q.i] == null).Select(q => q.x.Material >= 0 && q.x.Material < g.Materials.Length ? g.Materials[q.x.Material][(g.Materials[q.x.Material].LastIndexOf('.') + 1)..] : "?").Distinct())})" : ""));
        return new PropModel { Def = def, Models = models.ToArray(), Alpha = alphas.ToArray(), Size = max - min };
    }

    /// Reads a class's hand weapon (static or skeletal) on the loading thread.
    void LoadHandWeapon(GearsInstall gears, ClassRow cls)
    {
        var path = $"FairlightConcept/Content/Fairlight/Weapons/{cls.HandWeapon}.uasset";
        try
        {
            float[] pos, nrm, uv; uint[] idx; (short, int, int)[] sections; string[] mats;
            if (gears.LoadMesh(path) is { } m) { pos = m.Positions; nrm = m.Normals; uv = m.Uvs; idx = m.Indices; sections = m.Sections; mats = m.Materials; }
            else if (gears.LoadSkinned(path, cls.WeaponLod) is { } k) { pos = k.Positions; nrm = k.Normals; uv = k.Uvs; idx = k.Indices; sections = k.Sections.Select(s => (s.Material, s.BaseIndex, s.Triangles)).ToArray(); mats = k.Materials; }
            else { Log.Info($"  {cls.Id}: hand weapon {cls.HandWeapon} not read ({gears.LastError})"); return; }
            var tex = sections.Select(s => s.Item1 >= 0 && s.Item1 < mats.Length ? gears.FindAlbedo(mats[s.Item1]) : null).ToArray();
            foreach (var tp in tex)
                if (tp != null && !_charTextures.ContainsKey(tp) && gears.LoadTexture(tp, 512) is { } t) _charTextures[tp] = t;
            lock (_pending) _handData.Add((cls.Id, pos, nrm, uv, idx, sections, tex));
            Log.Info($"  {cls.Id}: hand weapon {cls.HandWeapon[(cls.HandWeapon.LastIndexOf('/') + 1)..]}, {idx.Length / 3} tris");
        }
        catch (Exception e) { Log.Info($"  {cls.Id}: hand weapon failed ({e.Message})"); }
    }

    /// A hand weapon as rigid models: grip at the origin (the mesh's own pivot), the long axis turned onto +x, head first.
    unsafe HeldGunModel BuildHand(float[] posCm, float[] nrmIn, float[] uvIn, uint[] indices, (short Material, int First, int Triangles)[] sections, string?[] texPaths)
    {
        var n = posCm.Length / 3;
        var pts = new Vector3[n];
        for (var i = 0; i < n; i++) pts[i] = new Vector3(posCm[i * 3], posCm[i * 3 + 2], posCm[i * 3 + 1]) * 0.01f;
        var min = pts.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var max = pts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var size = max - min;
        // rotations only (no mirroring), so the winding stays as the guns have it
        Func<Vector3, Vector3> turn = size.Y >= size.X && size.Y >= size.Z ? v => new Vector3(v.Y, -v.X, v.Z)
            : size.Z > size.X ? v => new Vector3(v.Z, v.Y, -v.X) : v => v;
        var tmin = pts.Select(turn).Aggregate(new Vector3(float.MaxValue), Vector3.Min); var tmax = pts.Select(turn).Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var flip = -tmin.X > tmax.X; // the longer side from the grip is the head
        Vector3 Fix(Vector3 v) { v = turn(v); return flip ? new Vector3(-v.X, -v.Y, v.Z) : v; }
        var models = new List<Model>();
        for (var k = 0; k < sections.Length; k++)
        {
            var (_, baseIndex, tris) = sections[k];
            var remap = new Dictionary<uint, ushort>();
            var ps = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var idx = new List<ushort>();
            for (var t = 0; t < tris; t++)
                for (var c = 0; c < 3; c++)
                {
                    var vi = indices[baseIndex + t * 3 + c];
                    if (!remap.TryGetValue(vi, out var ni))
                    {
                        ni = (ushort) remap.Count; remap[vi] = ni;
                        ps.Add(Fix(pts[vi]));
                        ns.Add(Fix(new Vector3(nrmIn[vi * 3], nrmIn[vi * 3 + 2], nrmIn[vi * 3 + 1])));
                        us.Add(new Vector2(uvIn[vi * 2], uvIn[vi * 2 + 1]));
                    }
                    idx.Add(ni);
                }
            if (idx.Count < 3 || remap.Count >= 65535) continue;
            var mesh = new Mesh(ps.Count, idx.Count / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
            ps.ToArray().AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
            ns.ToArray().AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
            us.ToArray().AsSpan().CopyTo(mesh.TexCoordsAs<Vector2>());
            idx.ToArray().AsSpan().CopyTo(mesh.IndicesAs<ushort>());
            Raylib.UploadMesh(ref mesh, false);
            var model = Lit.Make(mesh);
            if (texPaths[k] is { } tp && _gpuTextures.TryGetValue(tp, out var tex)) model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
            else model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(120, 116, 110, 255);
            models.Add(model);
        }
        var tsize = new Vector3(MathF.Max(MathF.Abs(tmin.X), MathF.Abs(tmax.X)), size.Y, size.Z);
        return new HeldGunModel { Models = models.ToArray(), Size = tsize };
    }

    void UploadHeldGun(string id, GearsSkinnedMesh g, string?[] texPaths, Dictionary<string, Texture2D> textures)
    {
        var (models, size) = BuildRigid(g, texPaths, textures);
        HeldGuns[id] = new HeldGunModel { Models = models, Size = size };
    }

    /// A Gears skeletal gun in its bind pose as rigid models, one per section.
    unsafe (Model[] models, Vector3 size) BuildRigid(GearsSkinnedMesh g, string?[] texPaths, Dictionary<string, Texture2D> textures)
    {
        // Game space (x, z, y) in metres; put the long horizontal axis along +x and centre it.
        var n = g.Positions.Length / 3;
        var pts = new Vector3[n];
        for (var i = 0; i < n; i++) pts[i] = new Vector3(g.Positions[i * 3], g.Positions[i * 3 + 2], g.Positions[i * 3 + 1]) * 0.01f;
        var min = pts.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var max = pts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var size = max - min; var centre = (min + max) / 2;
        var swap = size.Z > size.X;   // barrel along z: turn it onto x
        Vector3 Fix(Vector3 v) { v -= centre; return swap ? new Vector3(v.Z, v.Y, -v.X) : v; }
        var models = new List<Model>();
        for (var k = 0; k < g.Sections.Length; k++)
        {
            var (_, baseIndex, tris) = g.Sections[k];
            var remap = new Dictionary<uint, ushort>();
            var ps = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var idx = new List<ushort>();
            for (var t = 0; t < tris; t++)
                for (var c = 0; c < 3; c++)
                {
                    var vi = g.Indices[baseIndex + t * 3 + c];
                    if (!remap.TryGetValue(vi, out var ni))
                    {
                        ni = (ushort) remap.Count; remap[vi] = ni;
                        ps.Add(Fix(pts[vi]));
                        var nn = new Vector3(g.Normals[vi * 3], g.Normals[vi * 3 + 2], g.Normals[vi * 3 + 1]);
                        ns.Add(swap ? new Vector3(nn.Z, nn.Y, -nn.X) : nn);
                        us.Add(new Vector2(g.Uvs[vi * 2], g.Uvs[vi * 2 + 1]));
                    }
                    idx.Add(ni);
                }
            if (idx.Count < 3 || remap.Count >= 65535) continue;
            var mesh = new Mesh(ps.Count, idx.Count / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
            ps.ToArray().AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
            ns.ToArray().AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
            us.ToArray().AsSpan().CopyTo(mesh.TexCoordsAs<Vector2>());
            idx.ToArray().AsSpan().CopyTo(mesh.IndicesAs<ushort>());
            Raylib.UploadMesh(ref mesh, false);
            var model = Lit.Make(mesh);
            var tp = texPaths[k];
            if (tp != null && (textures.TryGetValue(tp, out var tex) || (_charTextures.TryGetValue(tp, out var data) && (textures[tp] = tex = MakeTexture(data)).Id != 0)))
                model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
            models.Add(model);
        }
        return (models.ToArray(), swap ? new Vector3(size.Z, size.Y, size.X) : size);
    }

    public static readonly bool CpuSkinCheck = Environment.GetEnvironmentVariable("DAEVAS_CPUSKIN") == "1";

    unsafe void UploadCharacter(BakedCharacter b, Dictionary<string, Texture2D> textures)
    {
        var sections = b.Bind.Select(s =>
        {
            var nv = s.Positions.Length / 3;
            var mesh = new Mesh(nv, s.Indices.Length / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices(); mesh.AllocTangents(); mesh.AllocColors();
            s.Positions.AsSpan().CopyTo(MemoryMarshal.Cast<Vector3, float>(mesh.VerticesAs<Vector3>()));
            s.Normals.AsSpan().CopyTo(MemoryMarshal.Cast<Vector3, float>(mesh.NormalsAs<Vector3>()));
            s.Uvs.AsSpan().CopyTo(MemoryMarshal.Cast<Vector2, float>(mesh.TexCoordsAs<Vector2>()));
            s.Indices.AsSpan().CopyTo(mesh.IndicesAs<ushort>());
            s.BoneIds.AsSpan().CopyTo(new Span<float>(mesh.Tangents, nv * 4));
            s.Weights.AsSpan().CopyTo(new Span<byte>(mesh.Colors, nv * 4));
            if (CpuSkinCheck)
            {
                // dev: pose the first idle frame here instead of on the GPU
                var row = b.BoneRows[b.Rows[Clip.Idle].Start];
                for (var i = 0; i < nv; i++)
                {
                    Vector3 p = default, q = default;
                    var v = new Vector3(s.Positions[i * 3], s.Positions[i * 3 + 1], s.Positions[i * 3 + 2]);
                    var nn = new Vector3(s.Normals[i * 3], s.Normals[i * 3 + 1], s.Normals[i * 3 + 2]);
                    for (var k = 0; k < 4; k++)
                    {
                        var w = s.Weights[i * 4 + k] / 255f;
                        if (w <= 0) continue;
                        var o = (int) s.BoneIds[i * 4 + k] * 12;
                        Vector3 R(int r) => new(row[o + r * 4], row[o + r * 4 + 1], row[o + r * 4 + 2]);
                        float T(int r) => row[o + r * 4 + 3];
                        p += w * new Vector3(Vector3.Dot(R(0), v) + T(0), Vector3.Dot(R(1), v) + T(1), Vector3.Dot(R(2), v) + T(2));
                        q += w * new Vector3(Vector3.Dot(R(0), nn), Vector3.Dot(R(1), nn), Vector3.Dot(R(2), nn));
                    }
                    mesh.VerticesAs<Vector3>()[i] = p;
                    mesh.NormalsAs<Vector3>()[i] = q.LengthSquared() > 0 ? Vector3.Normalize(q) : Vector3.UnitY;
                }
            }
            Raylib.UploadMesh(ref mesh, false);
            DropCpuCopy(ref mesh);
            var model = Lit.Make(mesh);
            if (s.Texture >= 0)
            {
                var path = b.TexturePaths[s.Texture];
                if (!textures.TryGetValue(path, out var tex) && _charTextures.TryGetValue(path, out var data))
                    textures[path] = tex = MakeTexture(data);
                if (tex.Id != 0) model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
            }
            else model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Color = new Color(90, 84, 80, 255);
            return model;
        }).ToArray();
        // the bone texture: a row per frame, three RGBA32F texels per palette bone
        var width = Math.Max(1, b.Palette * 3);
        var rows = b.BoneRows.Count;
        var data = new float[width * 4 * rows];
        for (var r = 0; r < rows; r++) b.BoneRows[r].AsSpan().CopyTo(data.AsSpan(r * width * 4));
        uint id;
        fixed (float* d = data) id = Rlgl.LoadTexture(d, width, rows, PixelFormat.UncompressedR32G32B32A32, 1);
        var bones = new Texture2D { Id = id, Width = width, Height = rows, Mipmaps = 1, Format = PixelFormat.UncompressedR32G32B32A32 };
        if (id == 0) Log.Info($"  {b.Def.Id}: bone texture {width}x{rows} could not be made");
        if (Environment.GetEnvironmentVariable("DAEVAS_BONECHECK") == "1" && id != 0)
        {
            var back = (float*) Rlgl.ReadTexturePixels(id, width, rows, PixelFormat.UncompressedR32G32B32A32);
            float worst = 0;
            for (var i = 0; i < data.Length; i++) worst = MathF.Max(worst, MathF.Abs(back[i] - data[i]));
            Log.Info($"  {b.Def.Id}: bone texture {width}x{rows} read back, worst difference {worst}, sample {data[0]} {data[3]} vs {back[0]} {back[3]}");
            Raylib.MemFree(back);
        }
        Characters[b.Def.Id] = new CharacterModel
        {
            Def = b.Def, Sections = sections, Bones = bones, Rows = new(b.Rows), Hand = b.Hand, Duration = b.Duration, Height = b.Height,
            Triangles = b.Bind.Sum(s => s.Indices.Length / 3),
        };
    }

    // Characters are drawn from the GPU only, so raylib's CPU copy of the vertices would just sit in RAM.
    // The indices stay: raylib draws a mesh without them as a plain triangle list.
    static unsafe void DropCpuCopy(ref Mesh m)
    {
        Raylib.MemFree(m.Vertices); m.Vertices = null;
        Raylib.MemFree(m.Normals); m.Normals = null;
        Raylib.MemFree(m.TexCoords); m.TexCoords = null;
        Raylib.MemFree(m.Tangents); m.Tangents = null;
        Raylib.MemFree(m.Colors); m.Colors = null;
    }

    unsafe void Upload(GunData g)
    {
        // Merge the parts into one mesh. Gears is centimetres with Z up and X along the barrel;
        // here x = barrel, y = up, z = right, in metres. Swapping Y/Z mirrors, which turns UE's clockwise
        // front faces counter-clockwise, as raylib wants them: the winding stays.
        var verts = g.Parts.Sum(p => p.Positions.Length / 3);
        var tris = g.Parts.Sum(p => p.Indices.Length / 3);
        var mesh = new Mesh(verts, tris);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
        var pos = mesh.VerticesAs<Vector3>(); var nrm = mesh.NormalsAs<Vector3>();
        var uv = mesh.TexCoordsAs<Vector2>(); var idx = mesh.IndicesAs<ushort>();
        int v0 = 0, i0 = 0;
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var p in g.Parts)
        {
            var n = p.Positions.Length / 3;
            for (var i = 0; i < n; i++)
            {
                var v = new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 2], p.Positions[i * 3 + 1]) * 0.01f;
                pos[v0 + i] = v;
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                nrm[v0 + i] = Vector3.Normalize(new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 2], p.Normals[i * 3 + 1]));
                uv[v0 + i] = new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]);
            }
            for (var k = 0; k + 2 < p.Indices.Length; k += 3)
            {
                idx[i0++] = (ushort) (v0 + p.Indices[k]);
                idx[i0++] = (ushort) (v0 + p.Indices[k + 1]);
                idx[i0++] = (ushort) (v0 + p.Indices[k + 2]);
            }
            v0 += n;
        }
        // Centre the gun on its bounding box so it can be held anywhere.
        var centre = (min + max) / 2;
        for (var i = 0; i < verts; i++) pos[i] -= centre;
        Raylib.UploadMesh(ref mesh, false);
        var model = Raylib.LoadModelFromMesh(mesh);
        Lit.Use(ref model);
        if (g.Texture is { } t)
        {
            var bytes = (byte*) Raylib.MemAlloc((uint) t.Rgba.Length);
            Marshal.Copy(t.Rgba, 0, (IntPtr) bytes, t.Rgba.Length);
            var img = new Image { Data = bytes, Width = t.Width, Height = t.Height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.GenTextureMipmaps(ref tex);
            Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
            model.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = tex;
        }
        Guns[g.Def.Id] = new GunModel { Def = g.Def, Models = [model], Size = max - min, Triangles = tris };
    }
}
