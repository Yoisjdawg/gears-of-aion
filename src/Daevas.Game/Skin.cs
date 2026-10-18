using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Daevas.Gears;

namespace Daevas.Game;

public enum Clip { Idle, Walk, Attack, Aim, Spawn, Death, Evade, Cast, Hit, Land, Jump, Sprint, StrafeL, StrafeR, Idle2, Victory, GetUp }

/// One section of one part in its bind pose (Gears space, centimetres, Z up), skinned on the GPU.
sealed class BakedSection
{
    public required float[] Positions, Normals, Uvs;
    public required ushort[] Indices;
    public required int Texture;            // index into BakedCharacter.TexturePaths (-1 = none)
    public required float[] BoneIds;        // 4 per vertex: index into the character's bone palette
    public required byte[] Weights;         // 4 per vertex, 0..255
}

/// A Gears character: its bind-pose mesh, and its skeleton posed for every frame of every clip
/// (one row of bone matrices per frame, in game space), ready to upload.
sealed class BakedCharacter
{
    public required CharacterRow Def;
    public readonly List<BakedSection> Bind = new();
    public readonly Dictionary<Clip, (int Start, int Count)> Rows = new();       // clip -> its frames' rows
    public readonly List<float[]> BoneRows = new();                              // per frame: 3 vec4 per palette bone
    public int Palette;                                                          // bones in the palette
    public readonly Dictionary<Clip, List<Vector3>> Hand = new();                // clip -> frame -> hand position
    public readonly List<string> TexturePaths = new();
    public float Height;
    public readonly Dictionary<Clip, float> Duration = new();                     // seconds per clip cycle
    public readonly HashSet<Clip> FromGears = new();                              // clips that are real Gears animations

    public static readonly Dictionary<Clip, int> FrameCounts = new()
    {
        [Clip.Idle] = 8, [Clip.Walk] = 16, [Clip.Attack] = 8, [Clip.Aim] = 1, [Clip.Spawn] = 0, [Clip.Death] = 0, [Clip.Evade] = 0,
        // these only exist as Gears animations
        [Clip.Cast] = 0, [Clip.Hit] = 0, [Clip.Land] = 0, [Clip.Jump] = 0, [Clip.Sprint] = 0, [Clip.StrafeL] = 0, [Clip.StrafeR] = 0,
        [Clip.Idle2] = 0, [Clip.Victory] = 0, [Clip.GetUp] = 0,
    };
    /// Clips that play once (the rest loop).
    public static bool Once(Clip c) => c is Clip.Attack or Clip.Spawn or Clip.Death or Clip.Evade or Clip.Cast or Clip.Hit or Clip.Land or Clip.Jump or Clip.Idle2 or Clip.Victory or Clip.GetUp;
}

/// Poses Gears skeletons procedurally (walk, idle, attack, gun-carry) and skins the meshes on the CPU.
static class Skin
{
    sealed class Skel
    {
        public required GearsSkinnedMesh Mesh;
        public required Matrix4x4[] Local, RefComp, InvBind;
        public required Dictionary<string, int> Index;
    }

    /// A Gears animation for one clip, read against the skeleton it was made for.
    public sealed class ClipSource
    {
        public required GearsAnim Anim;
        public required GearsSkeleton Skeleton;
    }

    public static BakedCharacter Bake(CharacterRow def, List<GearsSkinnedMesh> parts, Func<string, int, string?> findAlbedo, Dictionary<Clip, ClipSource> clips)
    {
        var baked = new BakedCharacter { Def = def };
        var skels = parts.Select(Prepare).ToList();
        var main = skels[0];

        // Character frame in UE component space: up is +Z, forward from heel to toe.
        var fwd = Dir(main, "b_foot_L", "b_ball_L") ?? Dir(main, "b_foot_R", "b_ball_R") ?? new Vector3(0, 1, 0);
        fwd.Z = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector3.Normalize(fwd) : new Vector3(0, 1, 0);
        var up = Vector3.UnitZ;
        var side = Vector3.Normalize(Vector3.Cross(up, fwd));
        // Game space: (x, z, y) * 0.01, then turn so the character faces +Z.
        var gameFwd = new Vector3(fwd.X, 0, fwd.Y);
        var turn = MathF.Atan2(gameFwd.X, gameFwd.Z);
        var toGame = Matrix4x4.CreateRotationY(-turn);

        // Textures per section (UDIM tile from the section's UVs).
        var texIndex = new Dictionary<string, int>();
        var sectionTex = new List<int[]>();
        var sectionTile = new List<float[]>();
        foreach (var s in skels)
        {
            var m = s.Mesh;
            var tex = new int[m.Sections.Length];
            var tiles = new float[m.Sections.Length];
            for (var k = 0; k < m.Sections.Length; k++)
            {
                var (mat, baseIndex, tris) = m.Sections[k];
                double su = 0; var cnt = 0;
                for (var i = baseIndex; i < baseIndex + tris * 3 && i < m.Indices.Length; i += 7) { su += m.Uvs[m.Indices[i] * 2]; cnt++; }
                var tile = cnt > 0 ? MathF.Floor((float) (su / cnt)) : 0;
                tiles[k] = tile;
                var matPath = mat >= 0 && mat < m.Materials.Length ? m.Materials[mat] : "";
                var path = findAlbedo(matPath, 1001 + (int) tile);
                if (path == null) { tex[k] = -1; continue; }
                if (!texIndex.TryGetValue(path, out var ti)) { ti = baked.TexturePaths.Count; baked.TexturePaths.Add(path); texIndex[path] = ti; }
                tex[k] = ti;
            }
            sectionTex.Add(tex);
            sectionTile.Add(tiles);
        }

        // Calibrate each swing so a positive angle means what the pose code expects (legs and spine;
        // arms are aimed at target directions instead, which works from any rest pose).
        float Sign(Skel s, string bone, string child, Vector3 axis, Vector3 want)
        {
            if (!s.Index.TryGetValue(bone, out var b) || !s.Index.TryGetValue(child, out var c)) return 1;
            var p = s.RefComp[b].Translation; var q = s.RefComp[c].Translation;
            var r = Vector3.Transform(q - p, Quaternion.CreateFromAxisAngle(axis, 0.2f));
            return Vector3.Dot(r - (q - p), want) >= 0 ? 1 : -1;
        }
        var thighFwd = Sign(main, "b_thigh_L", "b_calf_L", side, fwd);
        var spineFwd = Sign(main, "b_spine_01", "b_spine_03", side, fwd);
        var kneeL = Sign(main, "b_calf_L", "b_foot_L", side, -fwd);
        var kneeR = Sign(main, "b_calf_R", "b_foot_R", side, -fwd);
        // which way is out for each arm
        var outL = Dir(main, "b_upperarm_L", "b_forearm_L") is { } al && Vector3.Dot(al, side) < 0 ? -side : side;
        var outR = -outL;

        Dictionary<string, PoseOp> Pose(Clip clip, float t)
        {
            var d = new Dictionary<string, PoseOp>();
            void Rot(string b, Vector3 axis, float deg) => d[b] = new PoseOp { Axis = axis, Deg = deg };
            void Aim(string b, string child, Vector3 dir) => d[b] = new PoseOp { Child = child, Target = Vector3.Normalize(dir) };
            var stride = def.Stride;
            var hunch = def.Gait == "hunched" ? 28f : 4f;
            var gun = def.HandBone != "none";
            var ph = t * MathF.Tau;
            var s = clip == Clip.Walk ? MathF.Sin(ph) : 0;
            Rot("b_spine_01", side, spineFwd * hunch);
            // relaxed arms hang down and swing opposite the legs
            Aim("b_upperarm_L", "b_forearm_L", -up + outL * 0.22f - fwd * 0.5f * s * stride / 30);
            Aim("b_forearm_L", "b_hand_L", -up + fwd * 0.3f + outL * 0.05f);
            Aim("b_upperarm_R", "b_forearm_R", -up + outR * 0.22f + fwd * 0.5f * s * stride / 30);
            Aim("b_forearm_R", "b_hand_R", -up + fwd * 0.3f + outR * 0.05f);
            switch (clip)
            {
                case Clip.Walk:
                    Rot("b_thigh_L", side, thighFwd * stride * s);
                    Rot("b_thigh_R", side, thighFwd * -stride * s);
                    Rot("b_calf_L", side, kneeL * stride * 1.3f * MathF.Max(0, -MathF.Cos(ph)));
                    Rot("b_calf_R", side, kneeR * stride * 1.3f * MathF.Max(0, MathF.Cos(ph)));
                    Rot("b_spine_02", up, 4 * s);
                    break;
                case Clip.Idle:
                    Rot("b_spine_02", side, spineFwd * 2.5f * MathF.Sin(ph));
                    Rot("b_head", side, spineFwd * 2f * MathF.Sin(ph + 1));
                    break;
                case Clip.Attack:
                {
                    // wind up (arm raised high) then strike forward and down
                    var k = t < 0.4f ? t / 0.4f : 1 - MathF.Min(1, (t - 0.4f) / 0.25f);
                    var outA = def.AttackArm == "L" ? outL : outR;
                    var raised = up * 1.0f + outA * 0.35f - fwd * 0.3f;
                    var struck = -up * 0.4f + fwd * 1.0f + outA * 0.1f;
                    var u = def.AttackArm == "L" ? "L" : "R";
                    Aim($"b_upperarm_{u}", $"b_forearm_{u}", Vector3.Lerp(struck, raised, k));
                    Aim($"b_forearm_{u}", $"b_hand_{u}", Vector3.Lerp(fwd - up * 0.3f, up + fwd * 0.4f, k));
                    Rot("b_spine_02", up, (def.AttackArm == "L" ? 1 : -1) * 20 * (k - 0.5f));
                    Rot("b_spine_03", side, spineFwd * 12 * (1 - k));
                    break;
                }
            }
            if (gun && clip != Clip.Attack)
            {
                // carry the gun at the hip-to-chest line: right hand on the grip, left hand on the foregrip
                Aim("b_upperarm_R", "b_forearm_R", -up * 0.75f + fwd * 0.35f + outR * 0.3f);
                Aim("b_forearm_R", "b_hand_R", fwd * 1.0f + up * 0.15f - outR * 0.35f);
                Aim("b_upperarm_L", "b_forearm_L", -up * 0.55f + fwd * 0.6f + outL * 0.2f);
                Aim("b_forearm_L", "b_hand_L", fwd * 0.8f + up * 0.2f - outL * 0.75f);
            }
            return d;
        }

        // Only the bones that move vertices go to the GPU: one palette across all the parts.
        var palette = new List<int[]>();
        foreach (var sk in skels)
        {
            var map = Enumerable.Repeat(-1, sk.Local.Length).ToArray();
            var m = sk.Mesh;
            for (var i = 0; i < m.BoneWeights.Length; i++)
                if (m.BoneWeights[i] > 0 && map[m.BoneIndices[i]] < 0) map[m.BoneIndices[i]] = baked.Palette++;
            palette.Add(map);
        }
        for (var si = 0; si < skels.Count; si++) baked.Bind.AddRange(BindSections(skels[si], palette[si], sectionTex[si], sectionTile[si]));
        // Gears space to game space: (x, z, y) * 0.01, turned to face +Z.
        var swap = new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1) * Matrix4x4.CreateScale(0.01f) * toGame;

        foreach (var clip in BakedCharacter.FrameCounts.Keys)
        {
            var frames = new List<float[]>();
            var hands = new List<Vector3>();
            clips.TryGetValue(clip, out var src);
            // emerging and dying only exist as Gears animations (otherwise the game uses its own effects)
            if (src == null && BakedCharacter.FrameCounts[clip] == 0) continue;
            var once = BakedCharacter.Once(clip);
            // more frames for smoother motion (big, rarely seen clips stay leaner)
            // (a frame is only a row of bone matrices now, and the GPU blends between them)
            var n = src == null ? BakedCharacter.FrameCounts[clip] : Math.Clamp((int) MathF.Round(src.Anim.Duration * (clip == Clip.Attack ? 30 : once ? 24 : 20)), 8, clip is Clip.Spawn or Clip.Death ? 48 : 60);
            baked.Duration[clip] = src?.Anim.Duration ?? (clip == Clip.Walk ? 0.9f : clip == Clip.Attack ? 0.7f : 3f);
            if (src != null) baked.FromGears.Add(clip);
            for (var f = 0; f < n; f++)
            {
                var pose = src == null ? Pose(clip, f / (float) n) : null;
                var animPose = src == null ? null : SampleAnim(src, once ? f / (float) (n - 1) : f / (float) n);
                var row = new float[baked.Palette * 12];
                for (var si = 0; si < skels.Count; si++)
                {
                    var skel = skels[si];
                    var comp = animPose != null ? FkAnim(skel, animPose, src!) : Fk(skel, pose!);
                    var skinM = new Matrix4x4[comp.Length];
                    for (var b = 0; b < comp.Length; b++) skinM[b] = skel.InvBind[b] * comp[b];
                    var map = palette[si];
                    for (var b = 0; b < comp.Length; b++)
                    {
                        if (map[b] < 0) continue;
                        var g = skinM[b] * swap;
                        var o = map[b] * 12;
                        row[o] = g.M11; row[o + 1] = g.M21; row[o + 2] = g.M31; row[o + 3] = g.M41;
                        row[o + 4] = g.M12; row[o + 5] = g.M22; row[o + 6] = g.M32; row[o + 7] = g.M42;
                        row[o + 8] = g.M13; row[o + 9] = g.M23; row[o + 10] = g.M33; row[o + 11] = g.M43;
                    }
                    if (si == 0)
                    {
                        var hb = def.HandBone != "none" && skel.Index.TryGetValue(def.HandBone, out var h) ? h : -1;
                        hands.Add(hb >= 0 ? ToGame(comp[hb].Translation, toGame) : Vector3.Zero);
                    }
                }
                frames.Add(row);
            }
            baked.Rows[clip] = (baked.BoneRows.Count, frames.Count);
            baked.BoneRows.AddRange(frames);
            baked.Hand[clip] = hands;
        }
        if (Environment.GetEnvironmentVariable("DAEVAS_MESHCHECK") == "1") MeshCheck(def, skels, baked);
        // standing height: the first idle frame, skinned here once
        var idle = baked.BoneRows[baked.Rows[Clip.Idle].Start];
        var top = float.MinValue;
        foreach (var bs in baked.Bind)
            for (var i = 0; i < bs.Positions.Length / 3; i++)
            {
                float y = 0;
                for (var k = 0; k < 4; k++)
                {
                    var w = bs.Weights[i * 4 + k] / 255f;
                    if (w <= 0) continue;
                    var o = (int) bs.BoneIds[i * 4 + k] * 12 + 4;
                    y += w * (idle[o] * bs.Positions[i * 3] + idle[o + 1] * bs.Positions[i * 3 + 1] + idle[o + 2] * bs.Positions[i * 3 + 2] + idle[o + 3]);
                }
                top = MathF.Max(top, y);
            }
        baked.Height = top > float.MinValue ? top : 1.8f;
        return baked;
    }

    /// Local rotation (and translation) of every animated bone at time t (0..1 of the clip), by bone name.
    static Dictionary<string, (Quaternion rot, Vector3 pos)> SampleAnim(ClipSource src, float t)
    {
        var a = src.Anim;
        var s = Math.Clamp((int) MathF.Round(t * (a.NumSamples - 1)), 0, a.NumSamples - 1);
        var d = new Dictionary<string, (Quaternion, Vector3)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < a.TrackBones.Length; i++)
        {
            var o = (i * a.NumSamples + s) * 10;
            var k = a.Keys;
            d[src.Skeleton.BoneNames[a.TrackBones[i]]] = (Quaternion.Normalize(new Quaternion(k[o], k[o + 1], k[o + 2], k[o + 3])), new Vector3(k[o + 4], k[o + 5], k[o + 6]));
        }
        return d;
    }

    /// FK with Gears animation rotations applied by bone name. Bone lengths stay the mesh's own (so one
    /// animation fits differently proportioned characters); the pelvis takes the animation's height,
    /// scaled to the mesh, and the body is kept centred over the origin (no root drift).
    static Matrix4x4[] FkAnim(Skel s, Dictionary<string, (Quaternion rot, Vector3 pos)> pose, ClipSource src)
    {
        var n = s.Local.Length;
        var comp = new Matrix4x4[n];
        var pelvis = s.Index.TryGetValue("b_pelvis", out var pb) ? pb : -1;
        var skelPelvis = Array.FindIndex(src.Skeleton.BoneNames, b => b.Equals("b_pelvis", StringComparison.OrdinalIgnoreCase));
        var meshPelvisLen = pelvis >= 0 ? s.Local[pelvis].Translation.Length() : 1;
        var animPelvisLen = skelPelvis >= 0 ? new Vector3(src.Skeleton.RefPose[skelPelvis * 10 + 4], src.Skeleton.RefPose[skelPelvis * 10 + 5], src.Skeleton.RefPose[skelPelvis * 10 + 6]).Length() : 1;
        for (var b = 0; b < n; b++)
        {
            var local = s.Local[b];
            if (pose.TryGetValue(s.Mesh.BoneNames[b], out var k))
            {
                var p = s.Mesh.RefPose.AsSpan(b * 10, 10);
                var trans = new Vector3(p[4], p[5], p[6]);
                if (b == pelvis && animPelvisLen > 1e-3f) trans = k.pos * (meshPelvisLen / animPelvisLen);
                local = Matrix4x4.CreateScale(p[7], p[8], p[9]) * Matrix4x4.CreateFromQuaternion(k.rot) * Matrix4x4.CreateTranslation(trans);
            }
            var parent = s.Mesh.BoneParents[b];
            comp[b] = parent >= 0 ? local * comp[parent] : local;
        }
        if (pelvis >= 0)
        {
            var drift = comp[pelvis].Translation - s.RefComp[pelvis].Translation;
            drift.Z = 0;
            var fix = Matrix4x4.CreateTranslation(-drift);
            for (var b = 0; b < n; b++) comp[b] *= fix;
        }
        return comp;
    }

    static Vector3? Dir(Skel s, string a, string b) =>
        s.Index.TryGetValue(a, out var i) && s.Index.TryGetValue(b, out var j) ? s.RefComp[j].Translation - s.RefComp[i].Translation : null;

    static Vector3 ToGame(Vector3 ue, Matrix4x4 toGame) => Vector3.Transform(new Vector3(ue.X, ue.Z, ue.Y) * 0.01f, toGame);

    static Skel Prepare(GearsSkinnedMesh m)
    {
        var n = m.BoneNames.Length;
        var local = new Matrix4x4[n]; var comp = new Matrix4x4[n]; var inv = new Matrix4x4[n];
        for (var b = 0; b < n; b++)
        {
            var p = m.RefPose.AsSpan(b * 10, 10);
            local[b] = Matrix4x4.CreateScale(p[7], p[8], p[9]) * Matrix4x4.CreateFromQuaternion(new Quaternion(p[0], p[1], p[2], p[3])) * Matrix4x4.CreateTranslation(p[4], p[5], p[6]);
            comp[b] = m.BoneParents[b] >= 0 ? local[b] * comp[m.BoneParents[b]] : local[b];
            Matrix4x4.Invert(comp[b], out inv[b]);
        }
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var b = 0; b < n; b++) index.TryAdd(m.BoneNames[b], b);
        return new Skel { Mesh = m, Local = local, RefComp = comp, InvBind = inv, Index = index };
    }

    sealed class PoseOp
    {
        public Vector3 Axis;
        public float Deg;
        public string? Child;
        public Vector3 Target;
    }

    static Matrix4x4[] Fk(Skel s, Dictionary<string, PoseOp> pose)
    {
        var n = s.Local.Length;
        var comp = new Matrix4x4[n];
        for (var b = 0; b < n; b++)
        {
            var parent = s.Mesh.BoneParents[b];
            comp[b] = parent >= 0 ? s.Local[b] * comp[parent] : s.Local[b];
            if (!pose.TryGetValue(s.Mesh.BoneNames[b], out var op)) continue;
            var pivot = comp[b].Translation;
            Matrix4x4 rot;
            if (op.Child != null)
            {
                if (!s.Index.TryGetValue(op.Child, out var c)) continue;
                // child position through any bones in between (twist bones and the like)
                var chain = Matrix4x4.Identity;
                var k = c;
                while (k >= 0 && k != b) { chain = chain * s.Local[k]; k = s.Mesh.BoneParents[k]; }
                if (k != b) continue;
                var cur = (chain * comp[b]).Translation - pivot;
                if (cur.LengthSquared() < 1e-6f) continue;
                cur = Vector3.Normalize(cur);
                var axis = Vector3.Cross(cur, op.Target);
                var sin = axis.Length();
                if (sin < 1e-5f) continue;
                rot = Matrix4x4.CreateFromAxisAngle(axis / sin, MathF.Atan2(sin, Vector3.Dot(cur, op.Target)));
            }
            else
            {
                if (op.Deg == 0) continue;
                rot = Matrix4x4.CreateFromAxisAngle(op.Axis, op.Deg * MathF.PI / 180);
            }
            comp[b] = comp[b] * Matrix4x4.CreateTranslation(-pivot) * rot * Matrix4x4.CreateTranslation(pivot);
        }
        return comp;
    }

    /// Dev: how well the Gears normals match the triangles, and how much the first idle frame stretches edges.
    static void MeshCheck(CharacterRow def, List<Skel> skels, BakedCharacter baked)
    {
        var m = skels[0].Mesh;
        var nv = m.Positions.Length / 3;
        var acc = new Vector3[nv];
        Vector3 P(uint i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
        for (var t = 0; t + 2 < m.Indices.Length; t += 3)
        {
            var (a, b, c) = (m.Indices[t], m.Indices[t + 1], m.Indices[t + 2]);
            var fn = Vector3.Cross(P(b) - P(a), P(c) - P(a));
            acc[a] += fn; acc[b] += fn; acc[c] += fn;
        }
        int agree = 0, opposite = 0, other = 0, zero = 0;
        for (var i = 0; i < nv; i++)
        {
            var g = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            if (g.LengthSquared() < 1e-6f || acc[i].LengthSquared() < 1e-12f) { zero++; continue; }
            var d = Vector3.Dot(Vector3.Normalize(g), Vector3.Normalize(acc[i]));
            if (d > 0.6f) agree++; else if (d < -0.6f) opposite++; else other++;
        }
        var wsum = 0; var multi = 0;
        for (var i = 0; i < nv; i++) { var k = 0; for (var j = 0; j < 4; j++) if (m.BoneWeights[i * 4 + j] > 0) k++; wsum += k; if (k > 1) multi++; }
        // edge stretch in the first idle frame (first part's sections only)
        var row = baked.BoneRows[baked.Rows[Clip.Idle].Start];
        Vector3 Sk(BakedSection bs, int i)
        {
            var v = new Vector3(bs.Positions[i * 3], bs.Positions[i * 3 + 1], bs.Positions[i * 3 + 2]);
            Vector3 p = default;
            for (var k = 0; k < 4; k++)
            {
                var w = bs.Weights[i * 4 + k] / 255f;
                if (w <= 0) continue;
                var o = (int) bs.BoneIds[i * 4 + k] * 12;
                p += w * new Vector3(row[o] * v.X + row[o + 1] * v.Y + row[o + 2] * v.Z + row[o + 3], row[o + 4] * v.X + row[o + 5] * v.Y + row[o + 6] * v.Z + row[o + 7], row[o + 8] * v.X + row[o + 9] * v.Y + row[o + 10] * v.Z + row[o + 11]);
            }
            return p;
        }
        int edges = 0, stretched = 0;
        foreach (var bs in baked.Bind)
            for (var t = 0; t + 2 < bs.Indices.Length; t += 3)
                for (var e = 0; e < 3; e++)
                {
                    int i = bs.Indices[t + e], j = bs.Indices[t + (e + 1) % 3];
                    var bind = (new Vector3(bs.Positions[i * 3], bs.Positions[i * 3 + 1], bs.Positions[i * 3 + 2]) - new Vector3(bs.Positions[j * 3], bs.Positions[j * 3 + 1], bs.Positions[j * 3 + 2])).Length() * 0.01f;
                    var posed = (Sk(bs, i) - Sk(bs, j)).Length();
                    edges++;
                    if (bind > 1e-4f && (posed / bind > 2f || posed / bind < 0.5f)) stretched++;
                }
        int ba = 0, bo = 0, bt = 0;
        foreach (var bs in baked.Bind)
        {
            var acc2 = new Vector3[bs.Positions.Length / 3];
            Vector3 Q(int i) => new(bs.Positions[i * 3], bs.Positions[i * 3 + 1], bs.Positions[i * 3 + 2]);
            for (var t = 0; t + 2 < bs.Indices.Length; t += 3)
            {
                var fn = Vector3.Cross(Q(bs.Indices[t + 1]) - Q(bs.Indices[t]), Q(bs.Indices[t + 2]) - Q(bs.Indices[t]));
                acc2[bs.Indices[t]] += fn; acc2[bs.Indices[t + 1]] += fn; acc2[bs.Indices[t + 2]] += fn;
            }
            for (var i = 0; i < acc2.Length; i++)
            {
                var g = new Vector3(bs.Normals[i * 3], bs.Normals[i * 3 + 1], bs.Normals[i * 3 + 2]);
                if (g.LengthSquared() < 1e-6f || acc2[i].LengthSquared() < 1e-12f) continue;
                var d = Vector3.Dot(Vector3.Normalize(g), Vector3.Normalize(acc2[i]));
                bt++; if (d > 0.6f) ba++; else if (d < -0.6f) bo++;
            }
        }
        Log.Info($"  meshcheck {def.Id}: sections agree {ba * 100 / Math.Max(1, bt)}% opposite {bo * 100 / Math.Max(1, bt)}%");
        Log.Info($"  meshcheck {def.Id}: normals agree {agree * 100 / Math.Max(1, nv)}% opposite {opposite * 100 / Math.Max(1, nv)}% other {other * 100 / Math.Max(1, nv)}% zero {zero}; influences avg {wsum / (float) Math.Max(1, nv):0.00}, multi {multi * 100 / Math.Max(1, nv)}%; idle edges stretched {stretched * 1000 / Math.Max(1, edges) / 10f}%");
    }

    /// The part's sections in their bind pose, with each vertex's bones as palette indices.
    static IEnumerable<BakedSection> BindSections(Skel s, int[] palette, int[] tex, float[] tiles)
    {
        var m = s.Mesh;
        for (var k = 0; k < m.Sections.Length; k++)
        {
            var (_, baseIndex, tris) = m.Sections[k];
            var remap = new Dictionary<uint, ushort>();
            var idx = new List<ushort>(tris * 3);
            var ps = new List<float>(); var ns = new List<float>(); var us = new List<float>();
            var ids = new List<float>(); var ws = new List<byte>();
            for (var t = 0; t < tris; t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    // UE's front faces wind clockwise; the (x, z, y) mirror into game space makes them counter-clockwise,
                    // so the winding stays as it is (the Gears normals point the other way to UE's cross product)
                    var vi = m.Indices[baseIndex + t * 3 + c];
                    if (!remap.TryGetValue(vi, out var ni))
                    {
                        if (remap.Count >= 65535) break;
                        ni = (ushort) remap.Count;
                        remap[vi] = ni;
                        ps.Add(m.Positions[vi * 3]); ps.Add(m.Positions[vi * 3 + 1]); ps.Add(m.Positions[vi * 3 + 2]);
                        ns.Add(m.Normals[vi * 3]); ns.Add(m.Normals[vi * 3 + 1]); ns.Add(m.Normals[vi * 3 + 2]);
                        us.Add(m.Uvs[vi * 2] - tiles[k]); us.Add(m.Uvs[vi * 2 + 1]);
                        for (var b = 0; b < 4; b++)
                        {
                            var w = m.BoneWeights[vi * 4 + b];
                            var pb = w > 0 ? palette[m.BoneIndices[vi * 4 + b]] : -1;
                            ids.Add(pb < 0 ? 0 : pb);
                            ws.Add(pb < 0 ? (byte) 0 : (byte) Math.Clamp((int) MathF.Round(w * 255), 0, 255));
                        }
                    }
                    idx.Add(ni);
                }
            }
            if (idx.Count < 3) continue;
            yield return new BakedSection { Positions = ps.ToArray(), Normals = ns.ToArray(), Uvs = us.ToArray(), Indices = idx.ToArray(), Texture = tex[k], BoneIds = ids.ToArray(), Weights = ws.ToArray() };
        }
    }
}
