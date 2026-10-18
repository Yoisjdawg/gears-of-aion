using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Daevas.Gears;

// Dev tool: probe the player's Gears of War: E-Day install through Daevas.Gears.
// Probe <gameDir> <outDir> list | mesh <prefix>... | tex <prefix>...
static class Program
{
    static void Main(string[] args)
    {
        var sw = Stopwatch.StartNew();
        using var gears = GearsInstall.Open(args[0]);
        var outDir = args[1];
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"mounted {gears.FileCount} files in {sw.ElapsedMilliseconds} ms");
        var mode = args[2];
        if (mode == "media")
        {
            // media <event path>: the WEM media an event imports, with size and codec
            foreach (var ev in args.Skip(3))
                foreach (var imp in gears.Imports(ev).Where(i => i.StartsWith("/Game/WwiseAudio/Media/")))
                {
                    var file = "FairlightConcept/Content" + imp[5..imp.LastIndexOf('.')] + ".ubulk";
                    var raw = gears.ReadRaw(file);
                    if (raw == null) { Console.WriteLine($"  {imp}: no ubulk"); continue; }
                    var riff = System.Text.Encoding.ASCII.GetString(raw, 0, 4);
                    var fmt = raw.Length > 22 ? BitConverter.ToUInt16(raw, 20) : 0;
                    var ch = raw.Length > 24 ? BitConverter.ToUInt16(raw, 22) : 0;
                    var rate = raw.Length > 28 ? BitConverter.ToInt32(raw, 24) : 0;
                    var byteRate = BitConverter.ToInt32(raw, 28); var bits = BitConverter.ToUInt16(raw, 34);
                    var chunks = new List<string>();
                    for (var o = 12; o + 8 <= raw.Length && chunks.Count < 8;) { var id = System.Text.Encoding.ASCII.GetString(raw, o, 4); var len = BitConverter.ToInt32(raw, o + 4); chunks.Add($"{id}:{len}"); if (len < 0) break; o += 8 + len + (len & 1); }
                    Console.WriteLine($"  {imp[(imp.LastIndexOf('/') + 1)..]}: {raw.Length} bytes {riff} fmt=0x{fmt:X4} ch={ch} rate={rate} byteRate={byteRate} bits={bits} chunks=[{string.Join(" ", chunks)}] sub={BitConverter.ToString(raw, 44, 4)}");
                    File.WriteAllBytes(Path.Combine(outDir, imp[(imp.LastIndexOf('/') + 1)..imp.LastIndexOf('.')] + ".wem"), raw);
                }
            return;
        }
        if (mode == "sounds")
        {
            // sounds <event path>...: every recording an event plays, as WAV files (a sound browser)
            foreach (var ev in args.Skip(3))
            {
                var sounds = gears.LoadEventSounds(ev);
                Console.WriteLine($"{ev}: {sounds.Count} recordings");
                foreach (var snd in sounds)
                {
                    var path = Path.Combine(outDir, snd.Name + ".wav");
                    using var fs = File.Create(path);
                    using var bw = new BinaryWriter(fs);
                    var bytes = snd.Samples.Length * 2;
                    bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    bw.Write(16); bw.Write((short) 1); bw.Write((short) snd.Channels); bw.Write(snd.Rate); bw.Write(snd.Rate * snd.Channels * 2); bw.Write((short) (snd.Channels * 2)); bw.Write((short) 16);
                    bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
                    foreach (var v in snd.Samples) bw.Write(v);
                    Console.WriteLine($"  {snd.Name}: {snd.Duration:0.00}s {snd.Channels}ch {snd.Rate} Hz");
                }
            }
            return;
        }
        if (mode == "survey")
        {
            // survey <list file of cell packages>: per cell, placements and where they are (metres)
            var lines = new List<string>();
            foreach (var path in File.ReadAllLines(args[3]).Where(l => l.Trim() != ""))
            {
                try
                {
                    var pl = gears.LoadPlacements(path.Trim());
                    if (pl.Count == 0) { lines.Add($"{path.Trim()}	0"); continue; }
                    var xs = pl.Select(p => p.World.Translation.X / 100).OrderBy(v => v).ToArray();
                    var ys = pl.Select(p => p.World.Translation.Y / 100).OrderBy(v => v).ToArray();
                    var zs = pl.Select(p => p.World.Translation.Z / 100).OrderBy(v => v).ToArray();
                    lines.Add($"{path.Trim()}	{pl.Count}	{pl.Select(p => p.Mesh).Distinct().Count()}	{xs[xs.Length / 2]:0}	{ys[ys.Length / 2]:0}	{zs[zs.Length / 10]:0}	{zs[zs.Length / 2]:0}	{xs[0]:0}..{xs[^1]:0}	{ys[0]:0}..{ys[^1]:0}");
                }
                catch (Exception e) { lines.Add($"{path.Trim()}	error {e.Message}"); }
            }
            File.WriteAllLines(Path.Combine(outDir, "survey.tsv"), lines);
            Console.WriteLine($"surveyed {lines.Count} cells");
            return;
        }
        if (mode == "ground")
        {
            // ground <cx m> <cy m> <radius m> <cells...>: heights of street-level pieces near a point, and the most used meshes there
            float cx = float.Parse(args[3]), cy = float.Parse(args[4]), r = float.Parse(args[5]);
            var near = args.Skip(6).SelectMany(gears.LoadPlacements)
                .Where(p => MathF.Sqrt(MathF.Pow(p.World.Translation.X / 100 - cx, 2) + MathF.Pow(p.World.Translation.Y / 100 - cy, 2)) < r).ToList();
            Console.WriteLine($"{near.Count} placements within {r} m");
            var street = near.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.Mesh, "sidewalk|road|street|curb|plaza|asphalt|ground|floor_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            foreach (var g in street.GroupBy(p => MathF.Round(p.World.Translation.Z / 100)).OrderByDescending(g => g.Count()).Take(8))
                Console.WriteLine($"  street z {g.Key} m: {g.Count()}");
            // lowest placement in each 8 m square: where buildings meet the street
            foreach (var g in near.GroupBy(p => (MathF.Floor((p.World.Translation.X / 100 - cx) / 8), MathF.Floor((p.World.Translation.Y / 100 - cy) / 8))).OrderBy(g => g.Key))
                Console.Write($"[{g.Key.Item1 * 8},{g.Key.Item2 * 8}]={g.Min(p => p.World.Translation.Z / 100):0} ");
            Console.WriteLine();
            foreach (var g in near.GroupBy(p => p.Mesh).OrderByDescending(g => g.Count()).Take(25))
                Console.WriteLine($"  x{g.Count()} {g.Key[(g.Key.IndexOf("/Fairlight/") + 11)..]}");
            return;
        }
        if (mode == "fastgeo")
        {
            // fastgeo <cell package with extension>...: the meshes a map cell places, and where
            foreach (var path in args.Skip(3))
            {
                var pl = gears.LoadPlacements(path);
                Console.WriteLine($"{path[(path.LastIndexOf('/') + 1)..]}: {pl.Count} placements, {pl.Select(p => p.Mesh).Distinct().Count()} meshes");
                foreach (var g in pl.GroupBy(p => p.Mesh).OrderByDescending(g => g.Count()).Take(6))
                {
                    var t = g.First().World.Translation;
                    Console.WriteLine($"  x{g.Count()} {g.Key[(g.Key.IndexOf("/Fairlight/") + 11)..]} first at ({t.X / 100:0},{t.Y / 100:0},{t.Z / 100:0}) m");
                }
            }
            return;
        }
        if (mode == "cell")
        {
            // cell <map package with extension>: exports by class, then every static mesh inside
            foreach (var path in args.Skip(3))
            {
                var ex = gears.Exports(path);
                Console.WriteLine($"{path}: {ex.Count} exports: " + string.Join(", ", ex.GroupBy(e => e.cls).Select(g => $"{g.Key} x{g.Count()}")));
                foreach (var m in gears.LoadAllMeshes(path))
                {
                    float mnx = float.MaxValue, mny = float.MaxValue, mnz = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue, mxz = float.MinValue;
                    for (var q = 0; q < m.Positions.Length; q += 3) { mnx = Math.Min(mnx, m.Positions[q]); mxx = Math.Max(mxx, m.Positions[q]); mny = Math.Min(mny, m.Positions[q + 1]); mxy = Math.Max(mxy, m.Positions[q + 1]); mnz = Math.Min(mnz, m.Positions[q + 2]); mxz = Math.Max(mxz, m.Positions[q + 2]); }
                    Console.WriteLine($"  mesh {m.Name}: {m.Positions.Length / 3} verts {m.Indices.Length / 3} tris, x {mnx / 100:0}..{mxx / 100:0} y {mny / 100:0}..{mxy / 100:0} z {mnz / 100:0}..{mxz / 100:0} m, {m.Materials.Length} materials");
                    WriteObj(m, Path.Combine(outDir, m.Name + ".obj"));
                }
            }
            return;
        }
        if (mode == "imports")
        {
            foreach (var path in args.Skip(3))
                Console.WriteLine($"{path}:\n  " + string.Join("\n  ", gears.Imports(path)));
            return;
        }
        if (mode == "anim")
        {
            // anim <skeleton path> <anim path>...
            var skel = gears.LoadSkeleton(args[3]);
            Console.WriteLine(skel == null ? $"skeleton: {gears.LastError}" : $"skeleton {skel.Name}: {skel.BoneNames.Length} bones");
            if (skel == null) return;
            foreach (var a in args.Skip(4))
            {
                sw.Restart();
                try
                {
                    var anim = gears.LoadAnim(a, skel);
                    if (anim == null) { Console.WriteLine($"  -- {a}: {gears.LastError}"); continue; }
                    var k = anim.Keys;
                    var first = anim.TrackBones.Length > 1 ? 1 : 0;
                    var mid = anim.NumSamples / 2;
                    string Q(int track, int s) { var o = (track * anim.NumSamples + s) * 10; return $"q=({k[o]:0.00},{k[o + 1]:0.00},{k[o + 2]:0.00},{k[o + 3]:0.00}) t=({k[o + 4]:0.0},{k[o + 5]:0.0},{k[o + 6]:0.0})"; }
                    Console.WriteLine($"  anim {anim.Name}: {anim.TrackBones.Length} tracks {anim.NumSamples} samples @ {anim.SampleRate} Hz ({anim.Duration:0.00}s) [{sw.ElapsedMilliseconds} ms]");
                    Console.WriteLine($"     {skel.BoneNames[anim.TrackBones[first]]} s0 {Q(first, 0)} | mid {Q(first, mid)}");
                }
                catch (Exception ex) { Console.WriteLine($"!! {a}: {ex.GetType().Name} {ex.Message}"); }
            }
            return;
        }
        foreach (var prefix in args.Skip(3))
        {
            if (mode == "list") { File.WriteAllLines(Path.Combine(outDir, "files.txt"), gears.Find(prefix)); continue; }
            foreach (var f in gears.Find(prefix))
            {
                sw.Restart();
                try
                {
                    if (mode == "mesh")
                    {
                        var m = gears.LoadMesh(f);
                        var b = CUE4Parse.Globals.LastMappinglessBytes;
                        if (b != null) Console.WriteLine("     accepted: " + Convert.ToHexString(b));
                        Console.WriteLine("     last error: " + CUE4Parse.Globals.LastMappinglessError);
                        if (m == null) { Console.WriteLine($"  -- {f}: no mesh: {gears.LastError} [{sw.ElapsedMilliseconds} ms]"); continue; }
                        WriteObj(m, Path.Combine(outDir, m.Name + ".obj"));
                        Console.WriteLine($"  mesh {m.Name}: {m.Positions.Length / 3} verts {m.Indices.Length / 3} tris nanite={m.FromNanite} [{sw.ElapsedMilliseconds} ms]");
                        float mnx = float.MaxValue, mny = float.MaxValue, mnz = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue, mxz = float.MinValue;
                        for (var q = 0; q < m.Positions.Length; q += 3) { mnx = Math.Min(mnx, m.Positions[q]); mxx = Math.Max(mxx, m.Positions[q]); mny = Math.Min(mny, m.Positions[q + 1]); mxy = Math.Max(mxy, m.Positions[q + 1]); mnz = Math.Min(mnz, m.Positions[q + 2]); mxz = Math.Max(mxz, m.Positions[q + 2]); }
                        Console.WriteLine($"     size {(mxx - mnx) / 100:0.0} x {(mxy - mny) / 100:0.0} x {(mxz - mnz) / 100:0.0} m (z from {mnz / 100:0.0})");
                        Console.WriteLine("     sections " + string.Join(", ", m.Sections.Select(s => $"{s.Material}:{s.Triangles}")) + " | mats " + string.Join(", ", m.Materials.Select(x => x[(x.LastIndexOf('.') + 1)..] + "->" + (gears.FindAlbedo(x) is { } a ? Path.GetFileNameWithoutExtension(a) : "?"))));
                    }
                    else if (mode == "skin")
                    {
                        var m = gears.LoadSkinned(f);
                        if (m == null) { Console.WriteLine($"  -- {f}: {gears.LastError} [{sw.ElapsedMilliseconds} ms]"); continue; }
                        File.WriteAllLines(Path.Combine(outDir, m.Name + ".bones.txt"), m.BoneNames.Select((b, k) => $"{k} {b} parent={m.BoneParents[k]} t=({m.RefPose[k * 10 + 4]:0.0},{m.RefPose[k * 10 + 5]:0.0},{m.RefPose[k * 10 + 6]:0.0})"));
                        WriteObj(new GearsMesh { Name = m.Name, Positions = m.Positions, Normals = m.Normals, Uvs = m.Uvs, Indices = m.Indices }, Path.Combine(outDir, m.Name + ".obj"));
                        Console.WriteLine($"  skin {m.Name}: {m.Positions.Length / 3} verts {m.Indices.Length / 3} tris {m.BoneNames.Length} bones lods=[{string.Join(",", m.LodVertexCounts)}] sections={m.Sections.Length} mats=[{string.Join(", ", m.Materials.Select(x => x[(x.LastIndexOf('.') + 1)..] + "->" + (gears.FindAlbedo(x) is { } a ? Path.GetFileNameWithoutExtension(a) : "?")))}] secMats=[{string.Join(",", m.Sections.Select(s => s.Material))}] [{sw.ElapsedMilliseconds} ms]");
                    }
                    else if (mode == "tex")
                    {
                        var t = gears.LoadTexture(f, 512);
                        if (t == null) { Console.WriteLine($"  -- {f}: no texture"); continue; }
                        using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(t.Width, t.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul));
                        System.Runtime.InteropServices.Marshal.Copy(t.Rgba, 0, bmp.GetPixels(), t.Rgba.Length);
                        using var fs = File.Create(Path.Combine(outDir, t.Name + ".png"));
                        bmp.Encode(fs, SkiaSharp.SKEncodedImageFormat.Png, 90);
                        Console.WriteLine($"  tex {t.Name} {t.Width}x{t.Height} [{sw.ElapsedMilliseconds} ms]");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"!! {f}: {ex.GetType().Name} {ex.Message}"); }
            }
        }
    }

    static void WriteObj(GearsMesh m, string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        for (var i = 0; i < m.Positions.Length; i += 3) sb.Append(ci, $"v {m.Positions[i]} {m.Positions[i + 2]} {m.Positions[i + 1]}\n");
        for (var i = 0; i < m.Uvs.Length; i += 2) sb.Append(ci, $"vt {m.Uvs[i]} {1 - m.Uvs[i + 1]}\n");
        for (var i = 0; i + 2 < m.Indices.Length; i += 3)
        {
            uint a = m.Indices[i] + 1, b = m.Indices[i + 1] + 1, c = m.Indices[i + 2] + 1;
            sb.Append($"f {a}/{a} {c}/{c} {b}/{b}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
