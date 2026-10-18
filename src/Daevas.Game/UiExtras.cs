using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Extra screens and HUD pieces: the minimap and full map, the objective tracker, loading tips, title embers,
/// credits, patch notes, first-time hints and the screenshot flash.
static class UiExtras
{
    // ---------------------------------------------------------------- maps

    static Texture2D _navTex;
    static World? _navFor;
    static int _navStamp;
    public static bool BigMap;

    /// The street layout from the world's walking grid: open street light, walls dark.
    static void EnsureNav(World w)
    {
        var (open, n) = w.NavGrid;
        if (open == null) return;
        var stamp = open.Count(o => o) * 31 + n;
        if (_navFor == w && _navStamp == stamp && _navTex.Id != 0) return;
        if (_navTex.Id != 0) Raylib.UnloadTexture(_navTex);
        var img = Raylib.GenImageColor(n, n, new Color(0, 0, 0, 0));
        for (var x = 0; x < n; x++)
            for (var z = 0; z < n; z++)
                Raylib.ImageDrawPixel(ref img, x, z, open[x * n + z] ? new Color(150, 145, 135, 200) : new Color(30, 30, 34, 200));
        _navTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        _navFor = w; _navStamp = stamp;
    }

    static Vector2 ToMap(World w, Vector3 p, Rectangle r, float metres, Vector3 centre) =>
        new(r.X + r.Width / 2 + (p.X - centre.X) / metres * r.Width, r.Y + r.Height / 2 + (p.Z - centre.Z) / metres * r.Height);

    static void DrawMarks(World w, Rectangle r, float metres, Vector3 centre, float scale)
    {
        bool In(Vector2 v) => v.X > r.X && v.Y > r.Y && v.X < r.X + r.Width && v.Y < r.Y + r.Height;
        void Dot(Vector3 p, float size, Color c) { var v = ToMap(w, p, r, metres, centre); if (In(v)) Raylib.DrawCircleV(v, size * scale, c); }
        foreach (var s in w.WindStreams) Dot(s, 3, new Color(150, 255, 220, 230));
        foreach (var c in w.Chests.Where(c => !c.Opened)) Dot(c.Pos, 4, new Color(255, 210, 90, 255));
        if (w.Van is { } van) Dot(van.Pos, 5, new Color(90, 150, 255, 255));
        foreach (var e in w.Enemies) Dot(e.Pos, World.IsBoss(e) ? 6 : e.Elite ? 3.5f : 2.5f, World.IsBoss(e) ? new Color(255, 60, 200, 255) : new Color(255, 70, 50, 255));
        foreach (var a in w.Allies) Dot(a.Pos, 2.5f, new Color(130, 255, 160, 255));
        // the hero: an arrow the way you face
        var pv = ToMap(w, w.P.Pos, r, metres, centre);
        var f = new Vector2(w.P.Forward.X, w.P.Forward.Z);
        var side = new Vector2(-f.Y, f.X);
        Raylib.DrawTriangle(pv + f * 7 * scale, pv - f * 4 * scale - side * 4 * scale, pv - f * 4 * scale + side * 4 * scale, new Color(255, 230, 140, 255));
        Raylib.DrawTriangle(pv + f * 7 * scale, pv - f * 4 * scale + side * 4 * scale, pv - f * 4 * scale - side * 4 * scale, new Color(255, 230, 140, 255));
    }

    public static void DrawMinimap(World w, Hud hud)
    {
        EnsureNav(w);
        int sw = Ui.W;
        var r = new Rectangle(sw - 216, 200, 196, 196);
        Raylib.DrawRectangleRec(r, new Color(10, 10, 14, 200));
        const float metres = 70;
        if (_navTex.Id != 0)
        {
            var (_, n) = w.NavGrid;
            var cell = w.ArenaHalf * 2 / n;
            var c = new Vector2((w.P.Pos.X + w.ArenaHalf) / cell, (w.P.Pos.Z + w.ArenaHalf) / cell);
            var half = metres / 2 / cell;
            Raylib.DrawTexturePro(_navTex, new Rectangle(c.X - half, c.Y - half, half * 2, half * 2), r, Vector2.Zero, 0, Color.White);
        }
        Ui.Scissor(r.X, r.Y, r.Width, r.Height);
        DrawEdge(w, r, metres, w.P.Pos);
        DrawMarks(w, r, metres, w.P.Pos, 1);
        Raylib.EndScissorMode();
        Raylib.DrawRectangleLinesEx(r, 1, new Color(120, 110, 90, 220));
        hud.Text(Pad.Active ? "map: View+LB" : $"map: {Keys.Name(Keys.Key("map"))}", r.X + 4, r.Y + r.Height + 2, 13, new Color(180, 175, 165, 255));
    }

    /// The district's edge on a map: the void beyond it violet, the line where flight stops.
    static void DrawEdge(World w, Rectangle r, float metres, Vector3 centre)
    {
        var scale = r.Width / metres;
        float X(float x) => r.X + r.Width / 2 + (x - centre.X) * scale;
        float Y(float z) => r.Y + r.Height / 2 + (z - centre.Z) * scale;
        float x0 = X(-w.ArenaHalf), x1 = X(w.ArenaHalf), y0 = Y(-w.ArenaHalf), y1 = Y(w.ArenaHalf);
        var voidCol = new Color(46, 22, 70, 230);
        if (x0 > r.X) Raylib.DrawRectangleRec(new Rectangle(r.X, r.Y, x0 - r.X, r.Height), voidCol);
        if (x1 < r.X + r.Width) Raylib.DrawRectangleRec(new Rectangle(x1, r.Y, r.X + r.Width - x1, r.Height), voidCol);
        if (y0 > r.Y) Raylib.DrawRectangleRec(new Rectangle(MathF.Max(r.X, x0), r.Y, MathF.Min(x1, r.X + r.Width) - MathF.Max(r.X, x0), y0 - r.Y), voidCol);
        if (y1 < r.Y + r.Height) Raylib.DrawRectangleRec(new Rectangle(MathF.Max(r.X, x0), y1, MathF.Min(x1, r.X + r.Width) - MathF.Max(r.X, x0), r.Y + r.Height - y1), voidCol);
        Raylib.DrawRectangleLinesEx(new Rectangle(x0, y0, x1 - x0, y1 - y0), 2, new Color(200, 150, 255, 230));
    }

    public static void DrawBigMap(World w, Hud hud)
    {
        if (!BigMap) return;
        EnsureNav(w);
        int sw = Ui.W, sh = Ui.H;
        var size = MathF.Min(sw, sh) - 160;
        var r = new Rectangle(sw / 2f - size / 2, sh / 2f - size / 2 + 20, size, size);
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(6, 6, 10, 200));
        if (_navTex.Id != 0) Raylib.DrawTexturePro(_navTex, new Rectangle(0, 0, _navTex.Width, _navTex.Height), r, Vector2.Zero, 0, Color.White);
        DrawMarks(w, r, w.ArenaHalf * 2, Vector3.Zero, 1.6f);
        foreach (var (pos, id) in w.Landmarks)
        {
            var v = ToMap(w, pos, r, w.ArenaHalf * 2, Vector3.Zero);
            Raylib.DrawCircleV(v, 5, new Color(255, 230, 160, 255));
            hud.Text(Story.Landmarks[id].name, v.X + 8, v.Y - 8, 15, new Color(255, 230, 160, 255));
        }
        foreach (var (pos, _) in w.LorePages) Raylib.DrawCircleV(ToMap(w, pos, r, w.ArenaHalf * 2, Vector3.Zero), 4, new Color(230, 210, 170, 255));
        // spawn-point editor: left click adds a Locust spawn point, right click removes the nearest
        var m = Ui.Mouse;
        foreach (var sp in Profile.P.SpawnPoints) Raylib.DrawCircleLinesV(ToMap(w, new Vector3(sp[0], 0, sp[1]), r, w.ArenaHalf * 2, Vector3.Zero), 6, new Color(255, 80, 60, 255));
        if (Raylib.CheckCollisionPointRec(m, r))
        {
            var world = new Vector3((m.X - r.X - r.Width / 2) / r.Width * w.ArenaHalf * 2, 0, (m.Y - r.Y - r.Height / 2) / r.Height * w.ArenaHalf * 2);
            if (Inp.IsMouseButtonPressed(MouseButton.Left)) { Profile.P.SpawnPoints.Add(new[] { world.X, world.Z }); Profile.Save(); }
            if (Inp.IsMouseButtonPressed(MouseButton.Right) && Profile.P.SpawnPoints.Count > 0)
            {
                Profile.P.SpawnPoints.Remove(Profile.P.SpawnPoints.OrderBy(sp => (sp[0] - world.X) * (sp[0] - world.X) + (sp[1] - world.Z) * (sp[1] - world.Z)).First());
                Profile.Save();
            }
        }
        hud.TextCentered($"Spawn editor: left click adds a Locust spawn point, right click removes one ({Profile.P.SpawnPoints.Count} set; none = anywhere)", sw / 2f, r.Y + r.Height + 32, 15, new Color(255, 160, 140, 255));
        Raylib.DrawRectangleLinesEx(r, 2, new Color(160, 140, 100, 255));
        hud.TextCentered("SIEGE DOWNTOWN", sw / 2f, r.Y - 50, 32, new Color(255, 214, 110, 255));
        hud.TextCentered("red: Locust   pink: boss   gold: war chest   blue: ammo van   teal: wind stream   green: spirits", sw / 2f, r.Y + r.Height + 10, 17, new Color(200, 200, 210, 255));
    }

    // ---------------------------------------------------------------- objectives

    public static void DrawObjectives(World w, Hud hud)
    {
        var lines = new List<string>();
        if (w.Mode == GameMode.Tutorial && w.TutorialStep < World.TutorialSteps.Length) lines.Add("Finish the tutorial steps");
        else if (w.Mode == GameMode.Practice) lines.Add("Practice on the training targets");
        else if (w.WaveActive) lines.Add($"Clear {w.WaveLabel.ToLowerInvariant()} ({w.Enemies.Count(e => e.Hp > 0)} Locust left)");
        else if (!w.Won && !w.Lost) lines.Add("Get ready: the next wave is coming");
        if (w.Boss != null) lines.Add($"Defeat {w.Boss.Def.Name}");
        if (w.Van is { Hp: > 0 } van2) lines.Add(van2.Survivors ? "Keep the stranded squad alive" : "Keep the ammo van standing");
        if (w.Chests.Any(c => !c.Opened)) lines.Add("Open the war chest");
        if (w.Enemies.Any(e => e.Downed > 0)) lines.Add("Execute the downed Locust");
        if (w.StigmaPoints > 0) lines.Add("Choose a Stigma");
        if (w.LorePages.Count > 0) lines.Add($"Lore pages nearby ({w.LorePages.Count})");
        var y = 218f;
        hud.Text("OBJECTIVES", 22, y, 14, new Color(255, 214, 110, 255));
        foreach (var l in lines) { y += 20; hud.Text("- " + l, 22, y, 16, new Color(225, 220, 210, 255)); }
    }

    // ---------------------------------------------------------------- loading tips and the title

    static readonly string[] Tips =
    {
        "Land a skill, then press the same key again for its chain.",
        "Red circles on the ground fill up, then hit. Evade out with Space.",
        "A dodged hit opens a counter-attack: your next blow lands 50% harder.",
        "Each Locust has a weakness. Lock on to see it in the target frame.",
        "Big Locust drop better gear. Press I between waves to equip it.",
        "Wind streams refill your wings. Look for the swirling teal columns.",
        "Downed Drones can be executed with E for bonus XP and a little HP.",
        "Kill the Spore Matron first: it heals every Locust around it.",
        "Sandbags (B) between waves give you cover where you need it.",
        "Your ultimate fills as you fight. Press Q to transform.",
        "Stigmas at levels 5, 10 and 15 change how your Daeva plays.",
        "Snipers aim a red laser at you first. Move, or break their line of sight.",
        "Your district was torn out of Sera and hangs in the Abyss. Mind the edge: past it there is only the void.",
        "The violet crystals along the rim glow brighter at night.",
        "Fly too far and the Siege lines turn you back: the fight stays in the district.",
        "Too much wing in the way? Options has a wing size setting, and Asmodian dark wings.",
    };
    public static string Tip => Tips[(int) (Raylib.GetTime() / 5) % Tips.Length];

    static readonly List<(Vector2 pos, float speed, float size)> _embers = new();
    public static void DrawEmbers()
    {
        int sw = Ui.W, sh = Ui.H;
        var dt = Raylib.GetFrameTime();
        while (_embers.Count < 80) _embers.Add((new Vector2(Random.Shared.NextSingle() * sw, sh + Random.Shared.NextSingle() * sh), 20 + Random.Shared.NextSingle() * 60, 1 + Random.Shared.NextSingle() * 2.5f));
        for (var i = 0; i < _embers.Count; i++)
        {
            var (p, s, z) = _embers[i];
            p.Y -= s * dt; p.X += MathF.Sin((float) Raylib.GetTime() + i) * 12 * dt;
            if (p.Y < -10) p = new Vector2(Random.Shared.NextSingle() * sw, sh + 10);
            _embers[i] = (p, s, z);
            var a = (int) (200 * Math.Clamp(p.Y / sh, 0, 1));
            Raylib.DrawCircleV(p, z, new Color(255, 150, 60, a));
            Raylib.DrawCircleV(p, z * 3, new Color(255, 120, 40, a / 6));
        }
    }

    // ---------------------------------------------------------------- credits and patch notes

    public static int Overlay;   // 0 none, 1 credits, 2 patch notes

    public static void DrawOverlay(Hud hud)
    {
        if (Overlay == 0) return;
        int sw = Ui.W, sh = Ui.H;
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(6, 6, 10, 230));
        if (Overlay == 1)
        {
            hud.TextCentered("CREDITS", sw / 2f, sh * 0.25f, 56, new Color(255, 214, 110, 255));
            Brand.DrawLockup(sw / 2f, sh * 0.25f + 92, 70);
            hud.TextCentered("Made by yoisjdawg", sw / 2f, sh * 0.25f + 196, 28, Color.White);
            hud.TextCentered("AION 2 © NCSOFT", sw / 2f, sh * 0.25f + 246, 24, new Color(210, 205, 195, 255));
            hud.TextCentered("Gears of War: E-Day © Microsoft / The Coalition", sw / 2f, sh * 0.25f + 282, 24, new Color(210, 205, 195, 255));
            hud.TextCentered("Both logos are read from your own installs of the two games.", sw / 2f, sh * 0.25f + 324, 18, new Color(170, 165, 158, 255));
        }
        else
        {
            hud.TextCentered("WHAT'S NEW", sw / 2f, 70, 48, new Color(255, 214, 110, 255));
            var y = 140f;
            foreach (var l in PatchNotes) { hud.TextCentered(l, sw / 2f, y, 19, l.StartsWith("v") ? new Color(255, 214, 110, 255) : Color.White); y += 26; }
        }
        hud.TextCentered(Pad.Active ? "B: back" : "Esc: back", sw / 2f, sh - 50, 20, new Color(200, 200, 210, 255));
    }

    static readonly string[] PatchNotes =
    {
        "v0.8.0",
        "Eight-wave campaign with radio from COG Control, an intro and an ending",
        "Bosses: Vrak, the Corpser, Vrak the Scorched and the Brumak, with phases and telegraphed attacks",
        "New Locust: snipers, shotgunners, grenadiers, Flooders, Spore Matrons, Seeders, Brood Shifters, elites",
        "Game modes: Endless Horde, Siege Defence, Boss Rush, Time Attack, Daily Challenge, Practice, Tutorial",
        "Loot and gear: rarities, enchanting to +15, manastones, sets, a vendor, crafting, achievements, titles",
        "AION depth: elements, chains for every class, stances, Wrath, runes, Stigmas, ultimates and Transformation",
        "Flight: gliding, wind streams, air dash, dive strikes, wing skins",
        "Weather and time of day: rain, razorhail, dust storms, thunderstorms, night with street lamps",
        "A shader sky, sun shafts, bloom, point lights from fires and spells, feathered wings",
        "Options: key and gamepad rebinding, difficulty, colour-blind filters, accessibility assists",
    };

    // ---------------------------------------------------------------- first-time hints

    public static string Hint = "";
    public static float HintTime;

    public static void ShowHint(string id, string text)
    {
        if (Profile.P.Hints.Contains(id)) return;
        Profile.P.Hints.Add(id);
        Hint = text; HintTime = 7;
    }

    public static void DrawHint(Hud hud)
    {
        if (HintTime <= 0) return;
        HintTime -= Raylib.GetFrameTime();
        int sh = Ui.H;
        var wdt = hud.Measure(Hint, 18).X + 40;
        var a = (int) (230 * Math.Clamp(HintTime, 0, 1));
        Raylib.DrawRectangle(16, (int) (sh * 0.42f), (int) wdt, 54, new Color(14, 20, 30, a));
        Raylib.DrawRectangle(16, (int) (sh * 0.42f), 4, 54, new Color(255, 214, 110, a));
        hud.Text("TIP", 30, sh * 0.42f + 6, 14, new Color(255, 214, 110, a));
        hud.Text(Hint, 30, sh * 0.42f + 26, 18, new Color(255, 255, 255, a));
    }

    public static void CheckHints(World w)
    {
        if (Profile.P.Bag.Count > 0) ShowHint("bag", $"You found gear! Press {Keys.Name(Keys.Key("bag"))} to open your bag and equip it.");
        if (w.Chests.Any(c => !c.Opened)) ShowHint("chest", "A war chest! Walk up to it to open it.");
        if (w.Enemies.Any(e => e.Downed > 0)) ShowHint("downed", $"A Locust is down. Get close and press {Keys.Name(Keys.Key("execute"))} to execute it.");
        if (w.StigmaPoints > 0) ShowHint("stigma", $"A Stigma is ready. Press {Keys.Name(Keys.Key("stigma"))} to choose one.");
        if (w.Ultimate >= 100) ShowHint("ultimate", $"Your ultimate is ready. Press {Keys.Name(Keys.Key("ultimate"))} to transform.");
        if (w.Telegraphs.Count > 0) ShowHint("telegraph", "Red circles fill up, then hit. Get out, or evade through them.");
        if (!w.WaveActive && w.Wave > 0 && !w.Won) ShowHint("breather", $"Between waves: {Keys.Name(Keys.Key("bag"))} for the vendor, {Keys.Name(Keys.Key("sandbags"))} for sandbags.");
        if (w.Boss != null) ShowHint("boss", "A boss! It shrugs off stuns, but hard hits still interrupt its wind-ups.");
    }

    // ---------------------------------------------------------------- screenshot flash

    public static float Flash;
    public static void DrawFlash()
    {
        if (Flash <= 0) return;
        Flash -= Raylib.GetFrameTime() * 3;
        Raylib.DrawRectangle(0, 0, Ui.W, Ui.H, new Color(255, 255, 255, (int) (120 * Math.Clamp(Flash, 0, 1))));
    }
}
