using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CUE4Parse;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.FastGeoStreaming;
using CUE4Parse.UE4.Assets.Objects.Unversioned;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using OffiUtils;

namespace Daevas.Gears;

/// One mesh read from the player's Gears of War: E-Day install, in game units (centimetres, Z up).
public sealed class GearsMesh
{
    public required string Name;
    public required float[] Positions;  // x,y,z
    public required float[] Normals;    // x,y,z
    public required float[] Uvs;        // u,v
    public required uint[] Indices;
    public bool FromNanite;
    public (short Material, int FirstIndex, int Triangles)[] Sections = [];
    public string[] Materials = [];
}

public sealed class GearsSkinnedMesh
{
    public required string Name;
    public required float[] Positions, Normals, Uvs;   // centimetres, Z up (bind pose)
    public required uint[] Indices;
    public required ushort[] BoneIndices;               // 4 per vertex, skeleton bone index
    public required float[] BoneWeights;                // 4 per vertex
    public required string[] BoneNames;
    public required int[] BoneParents;
    public required float[] RefPose;                    // per bone: rotation xyzw, translation xyz, scale xyz (parent space)
    public required (short Material, int BaseIndex, int Triangles)[] Sections;
    public required string[] Materials;
    public int[] LodVertexCounts = [];
}

public sealed class GearsTexture
{
    public required string Name;
    public required int Width, Height;
    public required byte[] Rgba;
}

/// Reads the player's own Gears of War: E-Day archives. Nothing is copied out of the install;
/// meshes and textures are decoded into memory while the game runs.
public sealed class GearsInstall : IDisposable
{
    readonly DefaultFileProvider _provider;
    public string ContentRoot { get; }
    public int FileCount => _provider.Files.Count;
    /// Identifies the installed game build (its archives' sizes and dates), for caches made from it.
    public string Stamp { get; private set; } = "";
    public string? LastError { get; private set; }

    GearsInstall(string paks, string root)
    {
        ContentRoot = root;
        try
        {
            var toc = Directory.GetFiles(paks, "*.utoc").OrderBy(f => f).Select(f => new FileInfo(f)).Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}");
            Stamp = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|", toc))))[..16];
        }
        catch { Stamp = "unknown"; }
        // case-insensitive: game patches rename folders' case (Locust/Wretch became Locust/wretch in the October 2026 update)
        _provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_GearsofWarEDay), StringComparer.OrdinalIgnoreCase);
        _provider.Initialize();
        _provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));
        _provider.PostMount();
    }

    /// Finds the install from a folder Melty passes (the game folder or anything inside it).
    public static string? FindPaks(string gameDir)
    {
        foreach (var c in new[] { gameDir, Path.Combine(gameDir, "Content") })
        {
            var p = Path.Combine(c, "FairlightConcept", "Content", "Paks");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    public static GearsInstall Open(string gameDir)
    {
        var paks = FindPaks(gameDir) ?? throw new DirectoryNotFoundException($"Gears of War: E-Day archives not found under {gameDir}");
        InstallDecoders();
        return new GearsInstall(paks, gameDir);
    }

    static bool _decodersReady;
    static void InstallDecoders()
    {
        if (_decodersReady) return;
        _decodersReady = true;
        Serilog.Log.Logger = Serilog.Core.Logger.None;
        var dec = new DecompressorBuilder()
            .AddRange(Compression.Decompressor, true)
            .Add(CompressionAlgorithm.Oodle, Ooz.TryDecompress, replace: true)
            .Build();
        typeof(Compression).GetField("_decompressor", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, dec);
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        Globals.AllowMissingMappings = true;
        Globals.MappinglessFilter = o => o is UStaticMesh or UTexture2D or USkeletalMesh or USkeleton or UAnimSequence or UFastGeoContainer;
        var lodModels = typeof(USkinnedAsset).GetProperty(nameof(USkinnedAsset.LODModels))!;
        Globals.MappinglessReset = o => { if (o is USkeletalMesh) lodModels.SetValue(o, null); };
        Globals.MappinglessPrecheck = (o, ar) => o is not USkeletalMesh || LooksLikeSkeletalMeshStart(ar);
        Globals.MappinglessAcceptPartial = o => o is USkeletalMesh sk && SkeletalLodIsSane(sk);
        var renderData = typeof(UStaticMesh).GetProperty(nameof(UStaticMesh.RenderData))!;
        var refSkeleton = typeof(USkinnedAsset).GetProperty(nameof(USkinnedAsset.ReferenceSkeleton))!;
        Globals.MappinglessCustom = (o, ar, start, end) =>
        {
            switch (o)
            {
                case UStaticMesh sm:
                    if (FindLods(ar, start, end) is { } lods) renderData.SetValue(sm, new FStaticMeshRenderData { LODs = lods });
                    // keep the property bytes: material references in them give the material slot order
                    var head = (FAssetArchive) ar.Clone();
                    head.Position = start;
                    sm.CustomGameData = head.ReadBytes((int) Math.Min(end - start, 1 << 20));
                    return true;
                case USkeleton skeleton:
                    if (FindBoneArray(ar, start, end) is { } rs) skeleton.CustomGameData = rs;
                    return true;
                case UFastGeoContainer fg:
                    fg.ComponentClusters = ReadFastGeo(ar, start, end) ?? [];
                    return true;
                case UAnimSequence anim:
                    if (FindAclStream(ar, start, end) is { } acl) anim.CustomGameData = acl;
                    return true;
                case USkeletalMesh sk:
                    if (FindSkeletal(ar, start, end) is { } found)
                    {
                        refSkeleton.SetValue(sk, found.skel);
                        lodModels.SetValue(sk, found.lods);
                        sk.CustomGameData = found.mats;
                    }
                    return true;
                default:
                    return false;
            }
        };
    }

    // A cooked skeletal mesh's ReferenceSkeleton starts with its bone array: [count][root: FName, parent -1]
    // [next: FName, parent 0]... After it come bCooked, a Gears-specific block and the LOD render data.
    // A USkeleton's reference skeleton: the same bone-array pattern as a skeletal mesh's.
    static FReferenceSkeleton? FindBoneArray(FAssetArchive ar, long start, long end)
    {
        var probe = (FAssetArchive) ar.Clone();
        for (var p = start; p + 32 < end; p++)
        {
            probe.Position = p;
            var count = probe.Read<int>();
            if (count is < 2 or > 4096) continue;
            var n0 = probe.Read<int>(); var k0 = probe.Read<int>(); var parent0 = probe.Read<int>();
            if (parent0 != -1 || n0 < 0 || k0 is < 0 or > 64) continue;
            var n1 = probe.Read<int>(); var k1 = probe.Read<int>(); var parent1 = probe.Read<int>();
            if (parent1 != 0 || n1 < 0 || k1 is < 0 or > 64) continue;
            try
            {
                var a = (FAssetArchive) ar.Clone();
                a.Position = p;
                var skel = new FReferenceSkeleton(a);
                if (skel.FinalRefBonePose.Length == skel.FinalRefBoneInfo.Length) return skel;
            }
            catch { /* keep looking */ }
        }
        return null;
    }

    // A cooked ACL animation: [track count][bone index per track] ... [byte count][bulk flag 0][ACL buffer].
    // The ACL buffer is found by its tracks-header tag (0xAC11AC11), the track table by walking back.
    static (int[] tracks, byte[] acl)? FindAclStream(FAssetArchive ar, long start, long end)
    {
        var probe = (FAssetArchive) ar.Clone();
        for (var p = start + 8; p + 32 < end; p++)
        {
            probe.Position = p + 8;
            if (probe.Read<uint>() != 0xAC11AC11u) continue;
            probe.Position = p;
            var size = probe.Read<uint>();
            if (size < 32 || p + size > end) continue;
            probe.Position = p - 8;
            if (probe.Read<int>() != size || probe.Read<int>() != 0) continue;
            probe.Position = p + 16;
            var numTracks = probe.Read<int>();
            if (numTracks is < 1 or > 4096) continue;
            for (var q = p - 12 - numTracks * 4; q >= Math.Max(start, p - 12 - numTracks * 4 - 65536); q--)
            {
                probe.Position = q;
                if (probe.Read<int>() != numTracks) continue;
                var map = new int[numTracks];
                var ok = true;
                for (var i = 0; i < numTracks && ok; i++) { map[i] = probe.Read<int>(); ok = map[i] is >= 0 and < 4096; }
                if (!ok) continue;
                probe.Position = p;
                return (map, probe.ReadBytes((int) size));
            }
        }
        return null;
    }

    // The material array ends exactly where the reference skeleton begins: walk back until one parses to there.
    static FSkeletalMaterial[] FindSkeletalMaterials(FAssetArchive ar, long start, long skeletonAt)
    {
        var probe = (FAssetArchive) ar.Clone();
        for (var q = skeletonAt - 8; q >= Math.Max(start, skeletonAt - 16384); q--)
        {
            probe.Position = q;
            var count = probe.Read<int>();
            if (count is < 1 or > 64) continue;
            try
            {
                var a = (FAssetArchive) ar.Clone();
                a.Position = q;
                var mats = a.ReadArray(() => new FSkeletalMaterial(a));
                if (a.Position == skeletonAt && mats.All(m => m.MaterialInterface?.ResolvedObject != null)) return mats;
            }
            catch { /* keep walking back */ }
        }
        return [];
    }

    static (FReferenceSkeleton skel, FStaticLODModel[] lods, FSkeletalMaterial[] mats)? FindSkeletal(FAssetArchive ar, long start, long end)
    {
        var probe = (FAssetArchive) ar.Clone();
        for (var p = start; p + 32 < end; p++)
        {
            probe.Position = p;
            var count = probe.Read<int>();
            if (count is < 2 or > 4096) continue;
            var n0 = probe.Read<int>(); var k0 = probe.Read<int>(); var parent0 = probe.Read<int>();
            if (parent0 != -1 || n0 < 0 || k0 is < 0 or > 64) continue;
            var n1 = probe.Read<int>(); var k1 = probe.Read<int>(); var parent1 = probe.Read<int>();
            if (parent1 != 0 || n1 < 0 || k1 is < 0 or > 64) continue;
            foreach (var vertexColors in new[] { false, true })
            {
                try
                {
                    var a = (FAssetArchive) ar.Clone();
                    a.Position = p;
                    var skel = new FReferenceSkeleton(a);
                    if (skel.FinalRefBonePose.Length != skel.FinalRefBoneInfo.Length) break;
                    a.ReadBoolean(); // bCooked
                    if (a.Versions["SkeletalMesh.KeepMobileMinLODSettingOnDesktop"]) a.Read<int>();
                    a.SkipBulkArrayData();
                    a.SkipArray<uint>();
                    var extra = a.Read<int>();
                    for (var i = 0; i < extra; i++) { a.Position += 8; a.SkipArray<uint>(); a.Position += 4; }
                    a.Position += 32;
                    var lods = new FStaticLODModel[a.Read<int>()];
                    if (lods.Length is < 1 or > 8) break;
                    for (var i = 0; i < lods.Length; i++)
                    {
                        lods[i] = new FStaticLODModel();
                        lods[i].SerializeRenderItem(a, vertexColors);
                    }
                    var lod = lods.FirstOrDefault(l => !l.SkipLod);
                    var nv = lod?.VertexBufferGPUSkin?.GetVertexCount() ?? 0;
                    if (lod != null && nv > 3 && lod.Indices!.Buffer!.All(x => x < nv)) return (skel, lods, FindSkeletalMaterials(ar, start, p));
                }
                catch { /* not here */ }
            }
        }
        return null;
    }

    // After the unknown properties a cooked USkeletalMesh continues with: ObjectGuid flag (+guid),
    // FStripDataFlags, ImportedBounds (7 doubles: origin, extent, radius), then the material array.
    static bool LooksLikeSkeletalMeshStart(FAssetArchive ar)
    {
        var hasGuid = ar.Read<int>();
        if (hasGuid == 1) ar.Position += 16; else if (hasGuid != 0) return false;
        var g = ar.Read<byte>(); var cls = ar.Read<byte>();
        if (g > 3 || cls > 3) return false;
        for (var i = 0; i < 3; i++) { var o = ar.Read<double>(); if (!double.IsFinite(o) || Math.Abs(o) > 1e5) return false; }
        for (var i = 0; i < 3; i++) { var e = ar.Read<double>(); if (!double.IsFinite(e) || e < 0 || e > 1e5) return false; }
        var r = ar.Read<double>();
        if (!double.IsFinite(r) || r <= 1 || r > 2e5) return false;
        var mats = ar.Read<int>();
        return mats is >= 1 and <= 64;
    }

    static bool SkeletalLodIsSane(USkeletalMesh sk)
    {
        var lod = sk.LODModels?.FirstOrDefault(l => !l.SkipLod);
        if (lod == null || sk.ReferenceSkeleton == null) return false;
        var n = lod.VertexBufferGPUSkin?.GetVertexCount() ?? 0;
        var idx = lod.Indices!.Buffer!;
        if (n < 3 || idx.Length < 3 || idx.Length % 3 != 0) return false;
        for (var i = 0; i < idx.Length; i++) if (idx[i] >= n) return false;
        return true;
    }

    // Without the property schema we can't walk the mesh export field by field, so look for its LOD array
    // directly: [LOD count][per LOD: strip flags, sections, bounds, ... vertex/index buffers (inline or bulk)].
    // A candidate is kept when CUE4Parse reads every LOD and LOD0 holds a consistent triangle list.
    static FStaticMeshLODResources[]? FindLods(FAssetArchive ar, long start, long end)
    {
        var probe = (FAssetArchive) ar.Clone();
        for (var p = start; p + 16 < end; p++)
        {
            probe.Position = p;
            var count = probe.Read<int>();
            if (count is < 1 or > 8) continue;
            probe.Position += 2; // strip flags
            var sections = probe.Read<int>();
            if (sections is < 1 or > 64) continue;
            var material = probe.Read<int>();
            if (material is < 0 or > 64) continue;
            try
            {
                var attempt = (FAssetArchive) ar.Clone();
                attempt.Position = p + 4;
                var lods = new FStaticMeshLODResources[count];
                for (var i = 0; i < count; i++) lods[i] = new FStaticMeshLODResources(attempt);
                if (LodIsSane(lods[0])) return lods;
            }
            catch { /* not the LOD array */ }
        }
        return null;
    }

    static bool LodIsSane(FStaticMeshLODResources lod)
    {
        var verts = lod.PositionVertexBuffer?.Verts;
        var idx = lod.IndexBuffer;
        if (verts == null || idx == null || verts.Length < 3 || idx.Length < 3 || idx.Length % 3 != 0) return false;
        if (lod.VertexBuffer?.UV is not { } uv || uv.Length != verts.Length) return false;
        for (var i = 0; i < idx.Length; i++) if (idx[i] >= verts.Length) return false;
        var tris = lod.Sections.Sum(s => (long) s.NumTriangles);
        return tris * 3 == idx.Length;
    }

    public IEnumerable<string> Find(string prefix, string? contains = null) =>
        _provider.Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                                        && (contains == null || k.Contains(contains, StringComparison.OrdinalIgnoreCase)));

    public bool Exists(string path) => _provider.Files.ContainsKey(path);

    /// A static mesh at a level of detail (0 = full; asking past the last LOD gives the last one).
    public GearsMesh? LoadMesh(string path, int lodIndex = 0)
    {
        var pkg = _provider.LoadPackage(path);
        var sm = pkg.GetExports().OfType<UStaticMesh>().FirstOrDefault();
        if (sm == null) { LastError = "no StaticMesh export"; return null; }
        return FromStatic(pkg, sm, lodIndex);
    }

    /// A map cell's FastGeo container: the property block holds only the Assets array (object references the
    /// components point into); it's found as a count followed by that many valid package indices, then the
    /// native cluster data is read with CUE4Parse's own FastGeo types.
    static FFastGeoComponentCluster[]? ReadFastGeo(FAssetArchive ar, long start, long end)
    {
        var a = (FAssetArchive) ar.Clone();
        a.Position = start;
        try { _ = new FUnversionedHeader(a); } catch (Exception e) { if (Globals.MappinglessDebug) Console.WriteLine("  fastgeo header: " + e.Message); return null; }
        var owner = ar.Owner!;
        // The properties are object-reference arrays ([count][count package indices], Assets among them);
        // the native cluster data follows the last one.
        int[]? ReadRefs()
        {
            if (a.Position + 4 > end) return null;
            var n = a.Read<int>();
            if (n < 0 || n > 50000 || a.Position + n * 4L > end) return null;
            var raw = new int[n];
            for (var i = 0; i < n; i++)
            {
                var v = a.Read<int>();
                if (!(v < 0 ? -v - 1 < owner.ImportMapLength : v > 0 && v - 1 < owner.ExportMapLength)) return null;
                raw[i] = v;
            }
            return raw;
        }
        var arrays = new List<(int[] refs, long after)>();
        while (arrays.Count < 4)
        {
            var at = a.Position;
            var refs = ReadRefs();
            if (refs == null) { a.Position = at; break; }
            arrays.Add((refs, a.Position));
        }
        if (Globals.MappinglessDebug) Console.WriteLine($"  fastgeo: {arrays.Count} reference arrays ({string.Join(", ", arrays.Select(x => x.refs.Length))})");
        foreach (var (refs, _) in arrays)
        {
            var assets = refs.Select(v => new FPackageIndex(a, v)).ToArray();
            for (var k = arrays.Count - 1; k >= 0; k--)
            {
                try
                {
                    a.Position = arrays[k].after;
                    var fg = new FFastGeoArchive(a, assets);
                    var clusters = fg.ReadArray(() => new FFastGeoComponentCluster(fg));
                    if (clusters.Length > 0 && clusters.Sum(c => c.StaticMeshComponents.Length + c.InstancedStaticMeshComponents.Length) > 0)
                    {
                        if (Globals.MappinglessDebug)
                        {
                            Console.WriteLine($"  fastgeo: {clusters.Length} clusters with Assets = {refs.Length} refs, data at +{arrays[k].after - start}, ended at +{fg.Position - start} of {end - start}");
                            foreach (var cl in clusters)
                            {
                                Console.WriteLine($"    cluster {cl.Name}: {cl.StaticMeshComponents.Length} SM, {cl.InstancedStaticMeshComponents.Length} ISM, {cl.SkinnedMeshComponents.Length} SK");
                                foreach (var c in cl.InstancedStaticMeshComponents.Take(3))
                                {
                                    var d = c.GearsInstanceData ?? [];
                                    var es = c.GearsInstanceElementSize;
                                    Console.WriteLine($"      ISM elem {es} x{(es > 0 ? d.Length / es : 0)} world t=({c.WorldTransform.Translation.X:0},{c.WorldTransform.Translation.Y:0},{c.WorldTransform.Translation.Z:0})");
                                    for (var e = 0; e < Math.Min(2, es > 0 ? d.Length / es : 0); e++)
                                        Console.WriteLine("        f: " + string.Join(" ", Enumerable.Range(0, es / 4).Select(i => BitConverter.ToSingle(d, e * es + i * 4).ToString("0.###"))));
                                }
                            }
                        }
                        return clusters;
                    }
                }
                catch (Exception e) { if (Globals.MappinglessDebug) Console.WriteLine($"  fastgeo try assets={refs.Length} data@{arrays[k].after - start}: {e.GetType().Name} {e.Message.Split((char) 10)[0]} pos {a.Position - start} | {string.Join(" < ", (e.StackTrace ?? "").Split((char) 10).Take(4).Select(l => l.Trim().Split(" in ")[0].Replace("at CUE4Parse.UE4.Assets.Exports.FastGeoStreaming.", "")))}"); }
            }
        }
        return null;
    }

    /// Every static mesh placed in a map cell (FastGeo static and instanced components), in world space.
    public List<GearsPlacement> LoadPlacements(string cellPath)
    {
        var pkg = _provider.LoadPackage(cellPath);
        var list = new List<GearsPlacement>();
        static string? PathOf(FPackageIndex? i)
        {
            var name = i?.ResolvedObject?.GetPathName();
            if (name == null || !name.StartsWith("/Game/")) return null;
            var dot = name.LastIndexOf('.');
            return "FairlightConcept/Content" + name[5..(dot > 0 ? dot : name.Length)] + ".uasset";
        }
        foreach (var fg in pkg.GetExports().OfType<UFastGeoContainer>())
            foreach (var cl in fg.ComponentClusters ?? [])
            {
                foreach (var c in cl.StaticMeshComponents)
                    if (PathOf(c.SceneProxyDesc?.StaticMeshSceneProxyDesc?.StaticMesh) is { } mesh)
                        list.Add(new GearsPlacement { Mesh = mesh, World = ToMatrix(c.WorldTransform), Bounds = c.WorldBounds });
                foreach (var c in cl.InstancedStaticMeshComponents)
                {
                    if (PathOf(c.SceneProxyDesc?.StaticMeshSceneProxyDesc?.StaticMesh) is not { } mesh) continue;
                    var comp = ToMatrix(c.WorldTransform);
                    foreach (var inst in c.PerInstanceSMData ?? [])
                        list.Add(new GearsPlacement { Mesh = mesh, World = ToMatrix(inst.TransformData) * comp, Bounds = c.WorldBounds });
                    // Gears keeps each instance as a 3x3 rotation-scale matrix (rows) and a translation, relative to the component
                    if (c.GearsInstanceData is { } d && c.GearsInstanceElementSize == 48)
                        for (var o = 0; o + 48 <= d.Length; o += 48)
                        {
                            float F(int i) => BitConverter.ToSingle(d, o + i * 4);
                            var local = new System.Numerics.Matrix4x4(F(0), F(1), F(2), 0, F(3), F(4), F(5), 0, F(6), F(7), F(8), 0, F(9), F(10), F(11), 1);
                            list.Add(new GearsPlacement { Mesh = mesh, World = local * comp, Bounds = c.WorldBounds });
                        }
                }
            }
        return list;
    }

    static System.Numerics.Matrix4x4 ToMatrix(CUE4Parse.UE4.Objects.Core.Math.FTransform t) =>
        System.Numerics.Matrix4x4.CreateScale((float) t.Scale3D.X, (float) t.Scale3D.Y, (float) t.Scale3D.Z)
        * System.Numerics.Matrix4x4.CreateFromQuaternion(new System.Numerics.Quaternion((float) t.Rotation.X, (float) t.Rotation.Y, (float) t.Rotation.Z, (float) t.Rotation.W))
        * System.Numerics.Matrix4x4.CreateTranslation((float) t.Translation.X, (float) t.Translation.Y, (float) t.Translation.Z);

    /// Class and name of every export in a package (map cells hold many).
    public List<(string cls, string name)> Exports(string path)
    {
        var pkg = _provider.LoadPackage(path);
        var list = new List<(string, string)>();
        foreach (var e in pkg.GetExports()) list.Add((e.ExportType, e.Name));
        return list;
    }

    /// Every static mesh a package holds (an HLOD cell keeps its merged meshes inside the map package).
    public List<GearsMesh> LoadAllMeshes(string path)
    {
        var pkg = _provider.LoadPackage(path);
        var list = new List<GearsMesh>();
        foreach (var sm in pkg.GetExports().OfType<UStaticMesh>())
            if (FromStatic(pkg, sm) is { } m) list.Add(m);
        return list;
    }

    GearsMesh? FromStatic(CUE4Parse.UE4.Assets.IPackage pkg, UStaticMesh sm, int lodIndex = 0)
    {
        var sane = sm.RenderData?.LODs?.Where(l => l != null && LodIsSane(l)).ToArray() ?? [];
        var lod = sane.Length == 0 ? sm.RenderData?.LODs?.FirstOrDefault() : sane[Math.Clamp(lodIndex, 0, sane.Length - 1)];
        if (lod == null || !LodIsSane(lod)) { LastError = "LOD buffers not found"; return null; }
        var verts = lod.PositionVertexBuffer!.Verts;
        var items = lod.VertexBuffer!.UV;
        var n = verts.Length;
        var pos = new float[n * 3]; var nrm = new float[n * 3]; var uv = new float[n * 2];
        for (var i = 0; i < n; i++)
        {
            pos[i * 3] = verts[i].X; pos[i * 3 + 1] = verts[i].Y; pos[i * 3 + 2] = verts[i].Z;
            var tz = items[i].Normal[^1];
            nrm[i * 3] = tz.X; nrm[i * 3 + 1] = tz.Y; nrm[i * 3 + 2] = tz.Z;
            uv[i * 2] = items[i].UV[0].U; uv[i * 2 + 1] = items[i].UV[0].V;
        }
        var idx = new uint[lod.IndexBuffer!.Length];
        for (var i = 0; i < idx.Length; i++) idx[i] = (uint) lod.IndexBuffer[i];
        // Material slots: the package's material imports, ordered by where each is first referenced
        // in the mesh's own data (StaticMaterials comes first among the properties).
        var materials = new List<string>();
        if (sm.CustomGameData is byte[] raw)
        {
            var mats = new List<(int first, string path)>();
            for (var i = 0; i < pkg.ImportMapLength; i++)
            {
                string? name;
                try { name = new FPackageIndex(pkg, -(i + 1)).ResolvedObject?.GetPathName(); } catch { continue; }
                if (name == null || !name.Contains("/Materials/", StringComparison.OrdinalIgnoreCase) && !name.Contains("/Material", StringComparison.OrdinalIgnoreCase)) continue;
                var needle = BitConverter.GetBytes(-(i + 1));
                var at = raw.AsSpan().IndexOf(needle);
                mats.Add((at < 0 ? int.MaxValue : at, name));
            }
            materials = mats.OrderBy(m => m.first).Select(m => m.path).ToList();
        }
        var sections = lod.Sections.Select(s => ((short) s.MaterialIndex, s.FirstIndex, s.NumTriangles)).ToArray();
        return new GearsMesh { Name = sm.Name, Positions = pos, Normals = nrm, Uvs = uv, Indices = idx, Sections = sections, Materials = materials.ToArray() };
    }

    /// A character mesh with its skeleton and per-vertex skin weights (4 influences, skeleton bone indices).
    public GearsSkinnedMesh? LoadSkinned(string path, int lodIndex = 0)
    {
        var pkg = _provider.LoadPackage(path);
        var sk = pkg.GetExports().OfType<USkeletalMesh>().FirstOrDefault();
        if (sk == null) { LastError = "no SkeletalMesh export"; return null; }
        if (!SkeletalLodIsSane(sk)) { LastError = "skeletal LOD not found"; return null; }
        var usable = sk.LODModels!.Where(l => !l.SkipLod && l.VertexBufferGPUSkin?.GetVertexCount() > 0).ToArray();
        var lod = usable[Math.Clamp(lodIndex, 0, usable.Length - 1)];
        var vb = lod.VertexBufferGPUSkin;
        var n = vb.GetVertexCount();
        var pos = new float[n * 3]; var nrm = new float[n * 3]; var uv = new float[n * 2];
        var bones = new ushort[n * 4]; var weights = new float[n * 4];
        var section = 0;
        for (var i = 0; i < n; i++)
        {
            while (section + 1 < lod.Sections.Length && i >= lod.Sections[section].BaseVertexIndex + lod.Sections[section].NumVertices) section++;
            var map = lod.Sections[section].BoneMap;
            FSkelMeshVertexBase v = vb.bUseFullPrecisionUVs ? vb.VertsFloat[i] : vb.VertsHalf[i];
            pos[i * 3] = v.Pos.X; pos[i * 3 + 1] = v.Pos.Y; pos[i * 3 + 2] = v.Pos.Z;
            var tz = v.Normal[2];
            nrm[i * 3] = tz.X; nrm[i * 3 + 1] = tz.Y; nrm[i * 3 + 2] = tz.Z;
            uv[i * 2] = v.UVs[0].U; uv[i * 2 + 1] = v.UVs[0].V;
            if (v.Infs == null) { weights[i * 4] = 1; continue; }
            // keep the 4 heaviest influences, renormalised
            var order = Enumerable.Range(0, v.Infs.BoneWeight.Length).OrderByDescending(k => v.Infs.BoneWeight[k]).Take(4).ToArray();
            float sum = 0;
            foreach (var k in order) sum += v.Infs.BoneWeight[k];
            for (var j = 0; j < order.Length; j++)
            {
                var k = order[j];
                var bi = v.Infs.BoneIndex[k];
                bones[i * 4 + j] = bi < map.Length ? map[bi] : (ushort) 0;
                weights[i * 4 + j] = sum > 0 ? v.Infs.BoneWeight[k] / sum : (j == 0 ? 1 : 0);
            }
        }
        var refSkel = sk.ReferenceSkeleton;
        var nb = refSkel.FinalRefBoneInfo.Length;
        var names = new string[nb]; var parents = new int[nb]; var pose = new float[nb * 10];
        for (var b = 0; b < nb; b++)
        {
            names[b] = refSkel.FinalRefBoneInfo[b].Name.Text;
            parents[b] = refSkel.FinalRefBoneInfo[b].ParentIndex;
            var t = refSkel.FinalRefBonePose[b];
            pose[b * 10] = (float) t.Rotation.X; pose[b * 10 + 1] = (float) t.Rotation.Y; pose[b * 10 + 2] = (float) t.Rotation.Z; pose[b * 10 + 3] = (float) t.Rotation.W;
            pose[b * 10 + 4] = (float) t.Translation.X; pose[b * 10 + 5] = (float) t.Translation.Y; pose[b * 10 + 6] = (float) t.Translation.Z;
            pose[b * 10 + 7] = (float) t.Scale3D.X; pose[b * 10 + 8] = (float) t.Scale3D.Y; pose[b * 10 + 9] = (float) t.Scale3D.Z;
        }
        var buf = lod.Indices!.Buffer!;
        var sections = lod.Sections.Select(s => (s.MaterialIndex, s.BaseIndex, s.NumTriangles)).ToArray();
        var materials = (sk.CustomGameData as FSkeletalMaterial[] ?? []).Select(m => m.MaterialInterface?.ResolvedObject?.GetPathName() ?? "").ToArray();
        return new GearsSkinnedMesh
        {
            Name = sk.Name, Positions = pos, Normals = nrm, Uvs = uv, Indices = (uint[]) buf.Clone(),
            BoneIndices = bones, BoneWeights = weights, BoneNames = names, BoneParents = parents, RefPose = pose,
            Sections = sections, Materials = materials, LodVertexCounts = usable.Select(l => l.VertexBufferGPUSkin.GetVertexCount()).ToArray(),
        };
    }

    /// A USkeleton's bones in its own order (the order animation tracks refer to).
    public GearsSkeleton? LoadSkeleton(string path)
    {
        var pkg = _provider.LoadPackage(path);
        var skel = pkg.GetExports().OfType<USkeleton>().FirstOrDefault();
        if (skel?.CustomGameData is not FReferenceSkeleton rs) { LastError = "skeleton bones not found"; return null; }
        var n = rs.FinalRefBoneInfo.Length;
        var pose = new float[n * 10];
        for (var b = 0; b < n; b++)
        {
            var t = rs.FinalRefBonePose[b];
            pose[b * 10] = (float) t.Rotation.X; pose[b * 10 + 1] = (float) t.Rotation.Y; pose[b * 10 + 2] = (float) t.Rotation.Z; pose[b * 10 + 3] = (float) t.Rotation.W;
            pose[b * 10 + 4] = (float) t.Translation.X; pose[b * 10 + 5] = (float) t.Translation.Y; pose[b * 10 + 6] = (float) t.Translation.Z;
            pose[b * 10 + 7] = (float) t.Scale3D.X; pose[b * 10 + 8] = (float) t.Scale3D.Y; pose[b * 10 + 9] = (float) t.Scale3D.Z;
        }
        return new GearsSkeleton { Name = skel.Name, BoneNames = rs.FinalRefBoneInfo.Select(i => i.Name.Text).ToArray(), RefPose = pose };
    }

    /// An ACL-compressed Gears animation, decoded to local bone transforms for every sample.
    public unsafe GearsAnim? LoadAnim(string path, GearsSkeleton skeleton)
    {
        var pkg = _provider.LoadPackage(path);
        var anim = pkg.GetExports().OfType<UAnimSequence>().FirstOrDefault();
        if (anim?.CustomGameData is not ValueTuple<int[], byte[]> data) { LastError = "ACL stream not found"; return null; }
        var (map, bytes) = data;
        if (map.Any(b => b >= skeleton.BoneNames.Length)) { LastError = "tracks don't match the skeleton"; return null; }
        using var tracks = new CUE4Parse.ACL.CompressedTracks(bytes);
        var header = tracks.GetTracksHeader();
        var samples = (int) header.NumSamples;
        var keys = new float[map.Length * samples * 10];
        fixed (float* refPose = skeleton.RefPose) fixed (int* trackMap = map) fixed (float* outKeys = keys)
            AclNative.nReadACLData(tracks.Handle, refPose, trackMap, outKeys);
        return new GearsAnim { Name = anim.Name, TrackBones = map, NumSamples = samples, SampleRate = header.SampleRate, Keys = keys };
    }

    /// The raw bytes of a file in the archives (for example a Wwise media .ubulk, which is the .wem itself).
    public byte[]? ReadRaw(string path) => _provider.Files.TryGetValue(path, out var f) ? f.Read() : null;

    /// Every sound a Wwise event plays: its media are imported by the event package and stored as
    /// .wem files (RIFF, 16-bit PCM in this game) in each media package's .ubulk.
    /// vgmstream-cli.exe (built with Vorbis only), used to turn Wwise Vorbis media into WAV; null = PCM media only.
    public static string? WemDecoder;
    /// Where decoded Wwise Vorbis media are kept (made from the player's own install, never shipped).
    public static string SoundCache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GearsOfAion", "sound-cache");

    public List<GearsSound> LoadEventSounds(string eventPath, int limit = int.MaxValue)
    {
        var list = new List<GearsSound>();
        foreach (var imp in Imports(eventPath).Where(i => i.StartsWith("/Game/WwiseAudio/Media/", StringComparison.OrdinalIgnoreCase)))
        {
            if (list.Count >= limit) break;
            var raw = ReadRaw("FairlightConcept/Content" + imp[5..imp.LastIndexOf('.')] + ".ubulk");
            if (raw == null || raw.Length < 44 || raw[0] != 'R' || raw[1] != 'I') continue;
            var name = imp[(imp.LastIndexOf('/') + 1)..imp.LastIndexOf('.')];
            var pcm = ParseWav(raw);
            if (pcm == null && ReadFormatTag(raw) == 0xFFFF) pcm = DecodeVorbis(name, raw);
            if (pcm == null) continue;
            var (channels, rate, samples) = pcm.Value;
            double sq = 0; long rough = 0;
            for (var i = 0; i < samples.Length; i++) { sq += (double) samples[i] * samples[i]; if (i >= channels) rough += Math.Abs(samples[i] - samples[i - channels]); }
            long mag = 0; foreach (var s in samples) mag += Math.Abs((int) s);
            list.Add(new GearsSound
            {
                Name = name, Channels = channels, Rate = rate, Samples = samples,
                Rms = (float) Math.Sqrt(sq / Math.Max(1, samples.Length)), Roughness = mag > 0 ? (float) rough / mag : 0,
            });
        }
        return list;
    }

    static int ReadFormatTag(byte[] raw) => raw.Length >= 22 && raw[12] == 'f' && raw[13] == 'm' ? BitConverter.ToUInt16(raw, 20) : -1;

    /// 16-bit PCM from a RIFF file; null for anything else.
    static (int channels, int rate, short[] samples)? ParseWav(byte[] raw)
    {
        int channels = 0, rate = 0, bits = 0, format = 0; byte[]? data = null;
        for (var o = 12; o + 8 <= raw.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(raw, o, 4);
            var len = BitConverter.ToInt32(raw, o + 4);
            if (len < 0 || o + 8 + len > raw.Length) break;
            if (id == "fmt ") { format = BitConverter.ToUInt16(raw, o + 8); channels = BitConverter.ToUInt16(raw, o + 10); rate = BitConverter.ToInt32(raw, o + 12); bits = BitConverter.ToUInt16(raw, o + 22); }
            if (id == "data") data = raw.AsSpan(o + 8, len).ToArray();
            o += 8 + len + (len & 1);
        }
        if (data == null || format is not (1 or 0xFFFE) || bits != 16 || channels is < 1 or > 8 || rate <= 0) return null;
        var samples = new short[data.Length / 2];
        Buffer.BlockCopy(data, 0, samples, 0, samples.Length * 2);
        if (channels <= 2) return (channels, rate, samples);
        // surround and quad beds: fold down to stereo (even channels left, odd right)
        var frames = samples.Length / channels;
        var stereo = new short[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            int l = 0, r = 0, nl = 0, nr = 0;
            for (var c = 0; c < channels; c++) { if (c % 2 == 0) { l += samples[f * channels + c]; nl++; } else { r += samples[f * channels + c]; nr++; } }
            stereo[f * 2] = (short) (l / Math.Max(1, nl)); stereo[f * 2 + 1] = (short) (r / Math.Max(1, nr));
        }
        return (2, rate, stereo);
    }

    /// Wwise Vorbis media: the bulk holds the .wem (RIFF size + 8 bytes) followed by padding; vgmstream decodes it once into the cache.
    static (int channels, int rate, short[] samples)? DecodeVorbis(string name, byte[] raw)
    {
        try
        {
            var wav = System.IO.Path.Combine(SoundCache, name + ".wav");
            if (!File.Exists(wav))
            {
                if (WemDecoder == null || !File.Exists(WemDecoder)) return null;
                var size = BitConverter.ToInt32(raw, 4) + 8;
                if (size <= 12 || size > raw.Length) return null;
                Directory.CreateDirectory(SoundCache);
                var wem = System.IO.Path.Combine(SoundCache, name + ".wem");
                var tmp = wav + ".part";
                File.WriteAllBytes(wem, raw.AsSpan(0, size).ToArray());
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(WemDecoder) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(tmp); psi.ArgumentList.Add(wem);
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    proc.StandardOutput.ReadToEndAsync(); proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(15000)) { try { proc.Kill(); } catch { } return null; }
                    if (proc.ExitCode != 0 || !File.Exists(tmp)) return null;
                    File.Move(tmp, wav, true);
                }
                finally
                {
                    try { File.Delete(wem); } catch { }
                    try { File.Delete(tmp); } catch { }
                }
            }
            return ParseWav(File.ReadAllBytes(wav));
        }
        catch { return null; }
    }

    /// Names and paths of everything a package imports, in import order.
    public List<string> Imports(string path)
    {
        var pkg = _provider.LoadPackage(path);
        var list = new List<string>();
        for (var i = 0; i < pkg.ImportMapLength; i++)
        {
            try { list.Add(new FPackageIndex(pkg, -(i + 1)).ResolvedObject?.GetPathName() ?? "?"); }
            catch (Exception e) { list.Add("! " + e.Message); }
        }
        return list;
    }

    Dictionary<string, string>? _albedoIndex;

    /// The colour texture that goes with a material, by Gears' naming: <material>_d (trailing digits dropped).
    public string? FindAlbedo(string materialPath, int udimTile = 1001)
    {
        if (string.IsNullOrEmpty(materialPath)) return null;
        _albedoIndex ??= _provider.Files.Keys
            .Where(k => k.EndsWith("_d.uasset", StringComparison.OrdinalIgnoreCase) && (k.Contains("/Characters/", StringComparison.OrdinalIgnoreCase) || k.Contains("/Weapons/", StringComparison.OrdinalIgnoreCase) || k.Contains("/Environments/", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(k => System.IO.Path.GetFileNameWithoutExtension(k)[..^2].ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var name = materialPath[(materialPath.LastIndexOf('.') + 1)..].ToLowerInvariant();
        foreach (var candidate in new[] { name, name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), name.Replace("_base", ""), name.Replace("wpn_", ""), name.Replace("wpn_", "cog_") })
            if (_albedoIndex.TryGetValue(candidate, out var path)) return path;
        // Instances and world-space variants: drop _mic / _ws / _<x>pm_01a style suffixes, then try sibling letters.
        var stem = System.Text.RegularExpressions.Regex.Replace(name, @"(_mic|_ws|_[a-z]*pm_\d+[a-z]?|_leaks)$", "");
        foreach (var candidate in new[] { stem, stem.TrimEnd('a', 'b', 'c', 'd', 'e') + "a", stem.TrimEnd('a', 'b', 'c', 'd', 'e') + "b" })
            if (_albedoIndex.TryGetValue(candidate, out var path2)) return path2;
        // Skin and heads use UDIM tiles named <character>_mat_<tile>_d.
        var at = name.IndexOf("_mat", StringComparison.Ordinal);
        if (at > 0 && _albedoIndex.TryGetValue($"{name[..at]}_mat_{udimTile}", out var tile)) return tile;
        // The material's own textures: the first colour (_d) texture it, or a parent material, imports.
        if (TextureFromImports(materialPath, 2) is { } imported) return imported;
        // Last resort: the closest texture whose name starts with the material's stem.
        var prefix = _albedoIndex.Keys.Where(k => k.StartsWith(stem, StringComparison.Ordinal)).OrderBy(k => k.Length).FirstOrDefault();
        if (prefix != null) return _albedoIndex[prefix];
        return null;
    }

    string? TextureFromImports(string objectPath, int depth)
    {
        if (!objectPath.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)) return null;
        var dot = objectPath.LastIndexOf('.');
        var pkg = "FairlightConcept/Content" + objectPath[5..(dot > 0 ? dot : objectPath.Length)] + ".uasset";
        List<string> imports;
        try { if (!_provider.Files.ContainsKey(pkg)) return null; imports = Imports(pkg); }
        catch { return null; }
        foreach (var i in imports)
        {
            var d = i.LastIndexOf('.');
            var name = d > 0 ? i[(d + 1)..] : i;
            if (i.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_d", StringComparison.OrdinalIgnoreCase))
            {
                var tex = "FairlightConcept/Content" + i[5..(d > 0 ? d : i.Length)] + ".uasset";
                if (_provider.Files.ContainsKey(tex)) return tex;
            }
        }
        if (depth > 0)
            foreach (var i in imports.Where(i => i.Contains("/Materials/", StringComparison.OrdinalIgnoreCase) && !i.Contains("/Functions/", StringComparison.OrdinalIgnoreCase)))
                if (TextureFromImports(i, depth - 1) is { } t) return t;
        return null;
    }

    public GearsTexture? LoadTexture(string path, int maxSize = 1024)
    {
        var pkg = _provider.LoadPackage(path);
        var t = pkg.GetExports().OfType<UTexture2D>().FirstOrDefault();
        var tex = t?.Decode(maxSize);
        if (t == null || tex == null) return null;
        using var bmp = tex.ToSkBitmap();
        using var rgba = bmp.Copy(SkiaSharp.SKColorType.Rgba8888);
        return new GearsTexture { Name = t.Name, Width = rgba.Width, Height = rgba.Height, Rgba = rgba.Bytes };
    }

    public void Dispose() => _provider.Dispose();
}

/// One Wwise media sound (16-bit PCM, interleaved).
/// One placed mesh from a map cell: package path and world transform (Gears units: cm, Z up).
public sealed class GearsPlacement
{
    public required string Mesh;
    public System.Numerics.Matrix4x4 World;
    public CUE4Parse.UE4.Objects.Core.Math.FBoxSphereBounds Bounds;
}

public sealed class GearsSound
{
    public required string Name;
    public int Channels, Rate;
    public required short[] Samples;
    public float Rms, Roughness;
    public float Duration => Samples.Length / (float) Math.Max(1, Channels) / Math.Max(1, Rate);
}

public sealed class GearsSkeleton
{
    public required string Name;
    public required string[] BoneNames;
    public required float[] RefPose;     // per bone: rotation xyzw, translation xyz, scale xyz
}

public sealed class GearsAnim
{
    public required string Name;
    public required int[] TrackBones;    // skeleton bone index per track
    public required int NumSamples;
    public float SampleRate;
    public required float[] Keys;        // [track * NumSamples + sample] * 10: rotation xyzw, translation xyz, scale xyz
    public float Duration => NumSamples > 1 ? (NumSamples - 1) / MathF.Max(1, SampleRate) : 0;
}

static class AclNative
{
    // CUE4Parse-Natives (ACL by Nicholas Frechette, MIT) built with clang from CUE4Parse-Natives/ACL.
    [DllImport("CUE4Parse-Natives")] public static extern unsafe void nReadACLData(IntPtr tracks, float* refPoses, int* trackToBone, float* outAtoms);
}

static class Ooz
{
    [DllImport("ooz.dll")] static extern unsafe int OozDecompress(byte* src, int srcLen, byte* dst, int dstLen);

    public static unsafe bool TryDecompress(ReadOnlySpan<byte> src, Span<byte> dst, out int written)
    {
        fixed (byte* s = src) fixed (byte* d = dst)
            written = OozDecompress(s, src.Length, d, dst.Length);
        return written == dst.Length;
    }
}
