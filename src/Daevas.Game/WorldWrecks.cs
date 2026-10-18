using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Wrecks block by their real shape: a wreck's footprint cut into slices along its length, each as tall as the
/// wreck is there (a low bonnet or a flattened roof can be climbed; the cab stays tall).
sealed partial class World
{
    static readonly Dictionary<PropModel, (float from, float to, float lo, float hi, float top)[]> _slices = new();
    static readonly System.Text.RegularExpressions.Regex WreckName = new(@"vehicle|car|truck|bus|armadillo|semi|van|tank", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// The prop's slices in its own space (metres, base at 0): along its longer side, the cross-extent and top of each.
    static unsafe (float from, float to, float lo, float hi, float top)[] Slices(PropModel prop, int count)
    {
        if (_slices.TryGetValue(prop, out var cached)) return cached;
        var alongX = prop.Size.X >= prop.Size.Z;
        var half = (alongX ? prop.Size.X : prop.Size.Z) / 2;
        var result = new (float from, float to, float lo, float hi, float top)[count];
        for (var i = 0; i < count; i++) result[i] = (-half + i * 2 * half / count, -half + (i + 1) * 2 * half / count, float.MaxValue, float.MinValue, 0);
        foreach (var model in prop.Models)
        {
            var mesh = model.Meshes[0];
            if (mesh.Vertices == null) continue;
            foreach (var v in mesh.VerticesAs<Vector3>())
            {
                var a = alongX ? v.X : v.Z; var c = alongX ? v.Z : v.X;
                var i = Math.Clamp((int) ((a + half) / (2 * half) * count), 0, count - 1);
                var s = result[i];
                result[i] = (s.from, s.to, MathF.Min(s.lo, c), MathF.Max(s.hi, c), MathF.Max(s.top, v.Y));
            }
        }
        for (var i = 0; i < count; i++) if (result[i].lo > result[i].hi) result[i] = (result[i].from, result[i].to, 0, 0, 0);
        _slices[prop] = result;
        return result;
    }

    /// Boxes for a prop placed at `at`, turned `yaw` degrees (a multiple of 90): sliced for wrecks, one box otherwise.
    List<BoundingBox> PropBoxes(PropModel prop, Vector3 at, float yaw, BoundingBox whole)
    {
        if (!WreckName.IsMatch(prop.Def.Mesh[(prop.Def.Mesh.LastIndexOf('/') + 1)..]) || MathF.Max(prop.Size.X, prop.Size.Z) < 2.5f) return [whole];
        var boxes = new List<BoundingBox>();
        var alongX = prop.Size.X >= prop.Size.Z;
        var turn = Raymath.MatrixRotateY(yaw * MathF.PI / 180); // as the wreck is drawn
        foreach (var (from, to, lo, hi, top) in Slices(prop, 4))
        {
            if (top < 0.3f) continue;
            // the slice's corners in the prop's space, turned and placed
            Vector3[] corners = alongX
                ? [new(from, 0, lo), new(to, 0, lo), new(to, 0, hi), new(from, 0, hi)]
                : [new(lo, 0, from), new(hi, 0, from), new(hi, 0, to), new(lo, 0, to)];
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var c in corners)
            {
                var w = at + Raymath.Vector3Transform(c, turn);
                min = Vector3.Min(min, w); max = Vector3.Max(max, w);
            }
            boxes.Add(new BoundingBox(min with { Y = at.Y }, max with { Y = at.Y + top }));
        }
        return boxes.Count > 0 ? boxes : [whole];
    }
}
