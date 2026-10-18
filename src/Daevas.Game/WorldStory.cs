using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Story in the street: lore pages to collect, named landmarks, and the Quartermaster at the supply station.
sealed partial class World
{
    public readonly List<(Vector3 pos, int page)> LorePages = new();
    public readonly List<(Vector3 pos, int id)> Landmarks = new();
    readonly HashSet<int> _visited = new();

    void PlaceStoryBits()
    {
        if (Mode is GameMode.Practice or GameMode.Tutorial) return;
        var missing = Enumerable.Range(0, Story.Lore.Length).Where(i => !Profile.P.LorePages.Contains(i)).OrderBy(_ => _rng.Next()).Take(3).ToList();
        foreach (var page in missing) if (ReachableSpot(10) is { } at) LorePages.Add((at, page));
        foreach (var id in Enumerable.Range(0, Story.Landmarks.Length).OrderBy(_ => _rng.Next()).Take(3))
            if (ReachableSpot(12) is { } at) Landmarks.Add((at, id));
    }

    void UpdateStory()
    {
        for (var i = LorePages.Count - 1; i >= 0; i--)
        {
            var (pos, page) = LorePages[i];
            if (Dist2(pos, P.Pos) > 1.8f) continue;
            LorePages.RemoveAt(i);
            if (!Profile.P.LorePages.Contains(page)) Profile.P.LorePages.Add(page);
            Profile.Save();
            Banner($"LORE PAGE {Profile.P.LorePages.Count}/{Story.Lore.Length}\n{Story.Lore[page].title}");
            Say("Lore page found - read it in Bag > Codex", new Color(220, 200, 150, 255));
            Sfx.PlayGears("ui", "reward");
        }
        foreach (var (pos, id) in Landmarks)
            if (!_visited.Contains(id) && Dist2(pos, P.Pos) < 6)
            {
                _visited.Add(id);
                var (name, about) = Story.Landmarks[id];
                QueueRadio(new[] { ("Landmark: " + name, about) });
            }
    }

    public bool NearQuartermaster => Station is { } s && Dist2(s + new Vector3(1.6f, 0, 0), P.Pos) < 2.6f && !WaveActive && !Won && !Lost;

    void DrawStory()
    {
        foreach (var (pos, _) in LorePages)
        {
            var bob = MathF.Sin(TimeAlive * 2.5f + pos.X) * 0.12f;
            Raylib.DrawModelEx(_block, pos + new Vector3(0, 0.9f + bob, 0), Vector3.UnitY, TimeAlive * 60 % 360, new Vector3(0.35f, 0.45f, 0.03f), new Color(225, 210, 170, 255));
            Glow.Sprite(pos + new Vector3(0, 0.9f + bob, 0), 0.7f, new Color(255, 220, 140, 110));
            Glow.Decal(pos, 0.8f, new Color(255, 220, 140, 120), ring: true);
        }
        // the Quartermaster beside the supply station: a gold "!" when the board has something to claim
        if (Station is { } st && _assets.Characters.TryGetValue("carmine", out var qm))
        {
            var at = st + new Vector3(1.6f, 0, 0);
            var face = P.Pos - at;
            DrawCharacter(qm, Clip.Aim, (float) Raylib.GetTime() / MathF.Max(0.1f, qm.Duration[Clip.Aim]), at, MathF.Atan2(face.X, face.Z), 0, new Color(200, 205, 190, 255));
            if (!ShadowPass && Progression.Board().Any(m => !m.Claimed && m.Progress >= m.Target))
                Glow.Sprite(at + new Vector3(0, 2.4f + MathF.Sin(TimeAlive * 3) * 0.1f, 0), 0.35f, new Color(255, 214, 90, 255));
        }
    }
}
