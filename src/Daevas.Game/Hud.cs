using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// 2D overlay: AION-style hotbar and bars, Gears weapon slots, damage numbers, banners.
sealed class Hud
{
    readonly GameAssets _a;
    readonly Dictionary<string, RenderTexture2D> _gunIcons = new();
    public string Toast = "";
    public float ToastTime;

    public Hud(GameAssets a) => _a = a;

    /// Locust markers: red, or (with a colour-blind filter on) amber, which every kind of colour blindness tells
    /// apart from the blue and teal of friends and the green of health.
    public static Color EnemyColour => Settings.S.ColourBlind > 0 ? new Color(255, 176, 0, 255) : new Color(220, 60, 50, 255);

    // ---------------------------------------------------------------- text

    public void Text(string s, float x, float y, float size, Color c, bool outline = true, float spacing = 1)
    {
        s = Lang.T(s);
        if (outline)
        {
            var o = MathF.Max(1, size / 16);
            var shadow = new Color(0, 0, 0, (int) (c.A * 0.85f));
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) Raylib.DrawTextEx(_a.Font, s, new Vector2(x + dx * o, y + dy * o), size, spacing, shadow);
        }
        Raylib.DrawTextEx(_a.Font, s, new Vector2(x, y), size, spacing, c);
    }

    public Vector2 Measure(string s, float size, float spacing = 1) => Raylib.MeasureTextEx(_a.Font, s, size, spacing);

    public void TextCentered(string s, float cx, float y, float size, Color c, bool outline = true)
    {
        s = Lang.T(s);
        var w = Measure(s, size).X;
        Text(s, cx - w / 2, y, size, c, outline);
    }

    // ---------------------------------------------------------------- gun icons (the real Gears models)

    public void BuildGunIcons()
    {
        foreach (var (id, gm) in _a.Guns)
        {
            var rt = Raylib.LoadRenderTexture(256, 112);
            var cam = new Camera3D(new Vector3(0, 0.05f, -2.4f), Vector3.Zero, Vector3.UnitY, gm.Size.X / 2.286f * 1.08f, CameraProjection.Orthographic);
            Raylib.BeginTextureMode(rt);
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Raylib.BeginMode3D(cam);
            Lit.SetEye(cam.Position);
            foreach (var model in gm.Models) { var m = model; m.Transform = Raymath.MatrixRotateY(MathF.PI); Raylib.DrawModel(m, Vector3.Zero, 1, Color.White); }
            Raylib.EndMode3D();
            Raylib.EndTextureMode();
            Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
            _gunIcons[id] = rt;
        }
    }

    public void DrawGunIcon(string id, Rectangle dst, Color tint)
    {
        if (!_gunIcons.TryGetValue(id, out var rt)) return;
        // render textures are stored upside down
        Raylib.DrawTexturePro(rt.Texture, new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height), dst, Vector2.Zero, 0, tint);
    }

    // ---------------------------------------------------------------- in game

    Texture2D _vignette;
    float _scale = 1;   // interface size while the panels draw
    int W => Ui.W;   // panels and menus are laid out on the virtual screen
    int H => Ui.H;

    public void Draw(World w, Camera3D cam)
    {
        // the vignette and the labels over the Locust are placed in real pixels
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var p = w.P;

        // hurt and low HP: the screen edges run red, pulsing harder as HP drops
        var low = Math.Clamp(1 - p.Hp / (p.MaxHp * 0.4f), 0, 1);
        var hurt = Math.Clamp(p.HurtFlash / 0.25f, 0, 1);
        if ((low > 0 || hurt > 0) && !w.Won)
        {
            if (_vignette.Id == 0)
            {
                var img = Raylib.GenImageGradientRadial(256, 256, 0.45f, new Color(120, 0, 0, 0), new Color(120, 0, 0, 255));
                _vignette = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
                Raylib.SetTextureFilter(_vignette, TextureFilter.Bilinear);
            }
            var pulse = low > 0 && !Settings.S.ReduceFlashing ? 0.75f + 0.25f * MathF.Sin((float) Raylib.GetTime() * (4 + 4 * low)) : 1;
            var a = Math.Clamp(low * 0.6f * pulse + hurt * (Settings.S.ReduceFlashing ? 0.2f : 0.4f), 0, 0.75f);
            Raylib.DrawTexturePro(_vignette, new Rectangle(0, 0, 256, 256), new Rectangle(0, 0, sw, sh), Vector2.Zero, 0, new Color(255, 255, 255, (int) (a * 255)));
        }

        // damage numbers, AION style: pop in, float up, fade
        foreach (var n in w.Numbers)
        {
            var sp = Raylib.GetWorldToScreen(n.Pos, cam);
            if (Vector3.Dot(n.Pos - cam.Position, cam.Target - cam.Position) <= 0) continue;
            var k = n.Age / Sheets.DamageNumbersLifetime;
            var pop = n.Age < 0.12f ? 1.6f - n.Age / 0.12f * 0.6f : 1f;
            if (!Settings.S.DamageNumbers && n.Text.Length > 0 && char.IsDigit(n.Text[0])) continue;
            var size = 30 * n.Scale * pop * Ui.Scale;
            var col = World.Fade(n.Col, k < 0.65f ? 1 : 1 - (k - 0.65f) / 0.35f);
            TextCentered(n.Text, sp.X + n.Drift.X * k, sp.Y, size, col);
            // AION crits: a small CRITICAL tag over the big gold number
            if (n.Scale >= Sheets.DamageNumbersCritScale && char.IsDigit(n.Text[0])) TextCentered("CRITICAL", sp.X + n.Drift.X * k, sp.Y - 16 * Ui.Scale, 13 * Ui.Scale, World.Fade(new Color(255, 160, 40, 255), k < 0.65f ? 1 : 1 - (k - 0.65f) / 0.35f));
        }

        // the last Locust of a long wave: a marker on each (at the screen edge when out of view)
        if (w.ShowStragglers)
            foreach (var e in w.Enemies)
            {
                var top = e.Pos + new Vector3(0, e.Def.Size + 0.8f, 0);
                var ahead = Vector3.Dot(top - cam.Position, cam.Target - cam.Position) > 0;
                var sp = Raylib.GetWorldToScreen(top, cam);
                if (!ahead) sp = new Vector2(sw - sp.X, sh - sp.Y);
                var m = 40 * Ui.Scale;
                var edge = !ahead || sp.X < m || sp.Y < m || sp.X > sw - m || sp.Y > sh - m;
                sp = new Vector2(Math.Clamp(sp.X, m, sw - m), Math.Clamp(sp.Y, m, sh - m));
                var pulse = 0.75f + 0.25f * MathF.Sin((float) Raylib.GetTime() * 5);
                var r = 11 * Ui.Scale;
                Raylib.DrawPoly(sp, 4, r, 45, new Color(230, 70, 50, (int) (230 * pulse)));
                Raylib.DrawPolyLines(sp, 4, r + 2, 45, new Color(255, 220, 200, 220));
                TextCentered($"{Vector3.Distance(w.P.Pos, e.Pos):0} m" + (edge ? "" : $"  {e.Def.Name}"), sp.X, sp.Y + r + 4, 15 * Ui.Scale, new Color(255, 225, 210, 230));
            }

        // enemy HP bars
        foreach (var e in w.Enemies)
        {
            var top = e.Pos + new Vector3(0, e.Def.Size + 0.25f, 0);
            if (Vector3.Dot(top - cam.Position, cam.Target - cam.Position) <= 0) continue;
            var sp = Raylib.GetWorldToScreen(top, cam);
            var dist = Vector3.Distance(cam.Position, top);
            var bw = Math.Clamp(900 / dist, 30, 110) * Ui.Scale;
            Raylib.DrawRectangle((int) (sp.X - bw / 2) - 1, (int) sp.Y - 1, (int) bw + 2, 8, new Color(0, 0, 0, 180));
            Raylib.DrawRectangle((int) (sp.X - bw / 2), (int) sp.Y, (int) (bw * Math.Clamp(e.Hp / e.MaxHp, 0, 1)), 6, EnemyColour);
            if (World.IsBoss(e)) continue; // bosses get the big bar at the top
            var title = e.Elite ? "Elite " : "";
            if (dist < 22) TextCentered($"Lv {w.EnemyLevel(e)}  {title}{e.Def.Name}" + (e.Stun > 0 ? "  (stunned)" : ""), sp.X, sp.Y - 20 * Ui.Scale, 16 * Ui.Scale, e.Elite ? new Color(255, 210, 110, 255) : new Color(235, 225, 210, 255));
            if (dist < 22 && e.Def.Title != "none") TextCentered(e.Def.Title, sp.X, sp.Y - 38 * Ui.Scale, 14 * Ui.Scale, new Color(255, 200, 120, 255));
        }

        // the panels draw at the chosen interface size
        _scale = Ui.Scale;
        Rlgl.DrawRenderBatchActive();
        Rlgl.PushMatrix();
        Rlgl.Scalef(_scale, _scale, 1);
        sw = W; sh = H;

        // crosshair
        var cx = sw / 2f; var cy = sh / 2f;
        var hit = w.AimHit(w.Gun.Def.Range).enemy != null;
        var cc = hit ? new Color(255, 80, 60, 255) : new Color(255, 255, 255, 220);
        var gap = 6 + w.Gun.Def.SpreadDegrees * 2 + p.Recoil * 6;
        Raylib.DrawRectangle((int) (cx - gap - 10), (int) cy - 1, 10, 2, cc);
        Raylib.DrawRectangle((int) (cx + gap), (int) cy - 1, 10, 2, cc);
        Raylib.DrawRectangle((int) cx - 1, (int) (cy - gap - 10), 2, 10, cc);
        Raylib.DrawRectangle((int) cx - 1, (int) (cy + gap), 2, 10, cc);

        if (Sheets.GunsEnabled) DrawActiveReload(w, cx, cy);
        DrawCastBar(w, cx);
        DrawTargetFrame(w, cx);
        DrawEvade(w);
        DrawCharacterFrame(w);
        DrawHotbar(w);
        if (Sheets.GunsEnabled) DrawWeapons(w);
        DrawXpBar(w);

        // wave and banners
        var waveText = w.Mode == GameMode.Practice ? "Practice range  -  Esc to leave" : w.Mode == GameMode.Tutorial && w.TutorialStep < World.TutorialSteps.Length ? "Tutorial" : w.Won ? "Arena cleared" : w.WaveActive ? $"{w.WaveLabel}  -  {w.Enemies.Count} enemies" + (w.Modifier != "none" ? $"  -  {w.Modifier}" : "") :
            $"Next wave in {MathF.Ceiling(MathF.Max(0, w.Breather))}   ({(Pad.Active ? "Start" : "Enter")} to start now)";
        TextCentered(waveText, cx, 18, 22, new Color(230, 220, 200, 255));
        if (w.BannerTime > 0 && w.Banners.Count > 0)
        {
            var a = MathF.Min(1, w.BannerTime / 0.5f);
            var lines = w.Banners[^1].Split('\n');
            TextCentered(lines[0], cx, sh * 0.24f, 52, World.Fade(new Color(255, 214, 110, 255), a));
            for (var i = 1; i < lines.Length; i++) TextCentered(lines[i], cx, sh * 0.24f + 60 + i * 30 - 30, 26, World.Fade(Color.White, a));
        }
        if (ToastTime > 0) TextCentered(Toast, cx, sh - 230, 22, World.Fade(new Color(255, 120, 100, 255), MathF.Min(1, ToastTime * 2)));

        DrawFeed(w);
        DrawPotions(w);
        DrawBossBar(w);
        DrawCombat(w);
        DrawModeInfo(w);
        DrawUltimate(w);
        Captions.Update(Raylib.GetFrameTime());
        var cs = Settings.S.CaptionSize / 100f;
        for (var i = 0; i < Captions.Lines.Count; i++) TextCentered(Captions.Lines[i].text, W / 2f, H - 300 - i * 22 * cs, 17 * cs, new Color(230, 230, 235, (int) (255 * System.MathF.Min(1, Captions.Lines[i].time))));
        Rlgl.DrawRenderBatchActive();
        Rlgl.PopMatrix();
        _scale = 1;
    }

    /// Combo counter, execution prompt, stun and break-free, the damage meter and the combat log (L).
    void DrawCombat(World w)
    {
        if (w.KryllNear) TextCentered(w.DarkTime < World.KryllGrace ? $"The Kryll are gathering! Get into the light ({World.KryllGrace - w.DarkTime:0.0})" : "KRYLL! Get into the light", W / 2f, H * 0.3f, 30, new Color(255, 120, 100, (int) (180 + 75 * MathF.Sin((float) Raylib.GetTime() * 8))));
        if (w.Squad is { } sq)
        {
            var name = Sheets.Characters.First(c => c.Id == sq.Character).Name;
            Text(sq.Down > 0 ? $"{name}: DOWN" : name, 26, 186, 14, sq.Down > 0 ? new Color(255, 120, 100, 255) : new Color(140, 190, 255, 255));
            Raylib.DrawRectangle(26, 202, 120, 5, new Color(30, 30, 36, 220));
            Raylib.DrawRectangle(26, 202, (int) (120 * Math.Clamp(sq.Hp / sq.MaxHp, 0, 1)), 5, new Color(90, 150, 255, 255));
        }
        if (w.Combo >= 3)
        {
            var pop = 1 + MathF.Max(0, w.ComboTimer - 2.2f) * 1.5f;
            Text($"{w.Combo}", W - 190, H * 0.36f, 44 * pop, new Color(255, 214, 110, (int) (255 * MathF.Min(1, w.ComboTimer))));
            Text("COMBO", W - 186 + Measure($"{w.Combo}", 44 * pop).X, H * 0.36f + 18, 18, new Color(255, 214, 110, (int) (255 * MathF.Min(1, w.ComboTimer))));
        }
        if (w.ExecuteTarget != null) TextCentered(Pad.Active ? "X: EXECUTE" : "E: EXECUTE", W / 2f, H * 0.62f, 26, new Color(255, 110, 90, 255));
        if (w.PlayerStun > 0)
            TextCentered($"{w.PlayerStunLabel.ToUpperInvariant()}  -  " + (w.BreakFreeCd > 0 ? $"break free in {w.BreakFreeCd:0}s" : Pad.Active ? "B: break free" : "Space: break free"), W / 2f, H * 0.58f, 24, new Color(255, 170, 120, 255));
        if (w.WaveActive && w.Dps > 0) Text($"DPS {w.Dps:0}", W - 130, 168, 18, new Color(220, 210, 190, 255));
        if (!w.ShowLog) return;
        var lines = w.CombatLog.TakeLast(14).ToList();
        var y = H * 0.30f;
        Raylib.DrawRectangle(14, (int) y - 8, 430, 26 + lines.Count * 20 + 130, new Color(10, 10, 14, 190));
        Text("Combat log (L)", 24, y, 16, new Color(255, 214, 110, 255));
        y += 22;
        foreach (var (time, text, col) in lines) { Text($"{(int) time / 60}:{(int) time % 60:00}  {text}", 24, y, 14, col); y += 20; }
        y += 8;
        Text("Damage this run", 24, y, 16, new Color(255, 214, 110, 255));
        y += 22;
        var total = MathF.Max(1, w.DamageBySource.Values.Sum());
        foreach (var (src, dmg) in w.DamageBySource.OrderByDescending(kv => kv.Value).Take(5).Select(kv => (kv.Key, kv.Value)))
        {
            Raylib.DrawRectangle(24, (int) y + 2, (int) (300 * dmg / total), 14, new Color(150, 60, 50, 200));
            Text($"{src}  {dmg:N0}  ({dmg / total * 100:0}%)", 28, y, 14, Color.White);
            y += 20;
        }
    }

    /// The ultimate meter right of the hotbar, Transformation time, and a Stigma waiting to be chosen.
    void DrawUltimate(World w)
    {
        var cx = W / 2f + 640; var cy = H - 112f;
        var full = w.Ultimate >= 100;
        Raylib.DrawCircle((int) cx, (int) cy, 30, new Color(16, 14, 22, 230));
        Raylib.DrawCircleSector(new Vector2(cx, cy), 27, -90, -90 + 360 * w.Ultimate / 100f, 40, full ? new Color(255, 214, 110, 255) : new Color(150, 110, 60, 255));
        Raylib.DrawCircle((int) cx, (int) cy, 20, new Color(16, 14, 22, 255));
        if (w.Transform > 0) TextCentered($"{w.Transform:0}", cx, cy - 11, 22, new Color(255, 230, 150, 255));
        else TextCentered(full ? "!" : $"{w.Ultimate:0}", cx, cy - 10, full ? 22 : 16, full ? new Color(255, 214, 110, 255) : Color.White);
        TextCentered(Pad.Active ? "LT+RS" : "Q", cx, cy + 32, 14, new Color(200, 200, 210, 255));
        if (full && w.Transform <= 0) TextCentered(World.UltimateOf(w.P.Cls.Id).name, cx, cy - 52, 15, new Color(255, 214, 110, 255));
        if (w.InCover) TextCentered("IN COVER", W / 2f, H - 262, 18, new Color(170, 210, 255, 230));
        if (w.StigmaPoints > 0 && !w.StigmaOpen) TextCentered(Pad.Active ? "Stigma ready: LT+X" : "Stigma ready: press K", W / 2f, H - 240, 20, new Color(210, 160, 255, 255));
    }

    /// Mode extras: score and clock, the ammo van's HP, the tutorial's next step and the radio.
    void DrawModeInfo(World w)
    {
        var t = TimeSpan.FromSeconds(w.RunClock);
        if (w.Mode != GameMode.Practice && w.Mode != GameMode.Tutorial)
        {
            var big = w.Mode == GameMode.TimeAttack;
            var clock = $"{(int) t.TotalMinutes}:{t.Seconds:00}" + (big ? $".{t.Milliseconds / 100}" : "");
            Text($"Score {w.Score:N0}", W - 200, 120, 18, new Color(255, 214, 110, 255));
            Text(clock, W - 200, 142, big ? 24 : 18, Color.White);
        }
        if (w.Van is { } v)
        {
            var bw = 320f; var x = W / 2f - bw / 2; var y = H - 200f;
            TextCentered(v.Survivors ? "STRANDED COG SQUAD" : "COG AMMO VAN", W / 2f, y - 20, 16, new Color(140, 190, 255, 255));
            Raylib.DrawRectangle((int) x - 2, (int) y - 2, (int) bw + 4, 16, new Color(0, 0, 0, 200));
            Raylib.DrawRectangle((int) x, (int) y, (int) (bw * Math.Clamp(v.Hp / v.MaxHp, 0, 1)), 12, new Color(90, 150, 255, 255));
        }
        if (w.Mode == GameMode.Tutorial && w.TutorialStep < World.TutorialSteps.Length)
        {
            var (text, padText) = World.TutorialSteps[w.TutorialStep];
            Raylib.DrawRectangle(W / 2 - 360, (int) (H * 0.2f) - 10, 720, 70, new Color(10, 10, 14, 200));
            TextCentered($"Step {w.TutorialStep + 1} of {World.TutorialSteps.Length}", W / 2f, H * 0.2f - 4, 16, new Color(255, 214, 110, 255));
            TextCentered(Pad.Active ? padText : text, W / 2f, H * 0.2f + 20, 24, Color.White);
        }
        if (w.RadioNow is { } r)
        {
            var y = H - 290f;
            var width = MathF.Max(380, Measure(r.line, 18).X + 30);
            Raylib.DrawRectangle(16, (int) y, (int) width, 58, new Color(10, 14, 20, 210));
            Raylib.DrawRectangle(16, (int) y, 4, 58, new Color(90, 160, 255, 255));
            Text(r.who.ToUpperInvariant(), 28, y + 6, 15, new Color(140, 190, 255, 255));
            Text(r.line, 28, y + 28, 18, Color.White);
        }
    }

    /// The boss's name, title, level and HP with its phase marks, across the top.
    void DrawBossBar(World w)
    {
        if (w.Boss is not { } b) return;
        var bw = MathF.Min(760, W - 200f); var x = W / 2f - bw / 2; var y = 158f;
        TextCentered($"Lv {w.EnemyLevel(b)}  {b.Def.Name}  -  {b.Def.Title}" + (b.Enraged ? "  (BERSERK)" : ""), W / 2f, y - 26, 22, b.Enraged ? new Color(255, 90, 70, 255) : new Color(255, 214, 140, 255));
        Raylib.DrawRectangle((int) x - 2, (int) y - 2, (int) bw + 4, 22, new Color(0, 0, 0, 200));
        var frac = Math.Clamp(b.Hp / b.MaxHp, 0, 1);
        Raylib.DrawRectangle((int) x, (int) y, (int) (bw * frac), 18, b.Phase switch { 0 => new Color(200, 50, 40, 255), 1 => new Color(230, 110, 30, 255), _ => new Color(255, 40, 90, 255) });
        foreach (var m in new[] { 0.33f, 0.66f }) Raylib.DrawRectangle((int) (x + bw * m), (int) y, 2, 18, new Color(255, 255, 255, 160));
        TextCentered($"{(int) MathF.Max(0, b.Hp):N0} / {(int) b.MaxHp:N0}", W / 2f, y + 1, 15, Color.White);
    }

    /// Loot, achievements and wave bonuses, newest at the bottom, fading out.
    void DrawFeed(World w)
    {
        var y = H - 190f;
        for (var i = w.Feed.Count - 1; i >= 0; i--)
        {
            var (text, col, time) = w.Feed[i];
            var s = Measure(text, 18);
            Text(text, W - 30 - s.X, y, 18, World.Fade(col, MathF.Min(1, time)));
            y -= 24;
        }
    }

    /// The three potion slots left of the hotbar: count and cooldown.
    void DrawPotions(World w)
    {
        var kinds = new[] { ("hp-potion", Pad.Active ? "LB" : "Z", new Color(200, 60, 60, 255)), ("mp-potion", Pad.Active ? "RB" : "X", new Color(60, 110, 220, 255)), ("serum", Pad.Active ? "LT+LB" : "V", new Color(90, 200, 170, 255)) };
        var x = W / 2f - 640; var y = H - 140f;
        foreach (var (id, key, col) in kinds)
        {
            var n = Profile.Mat(id);
            var r = new Rectangle(x, y, 44, 44);
            Raylib.DrawRectangleRec(r, new Color(20, 20, 26, 220));
            Raylib.DrawCircle((int) (x + 22), (int) (y + 24), 13, n > 0 ? col : new Color(70, 70, 76, 255));
            Raylib.DrawRectangle((int) (x + 18), (int) (y + 6), 8, 8, n > 0 ? new Color(200, 190, 170, 255) : new Color(90, 90, 96, 255));
            var cd = w.PotionCd.GetValueOrDefault(id);
            if (cd > 0) Raylib.DrawRectangle((int) x, (int) y, 44, (int) (44 * cd / 8), new Color(0, 0, 0, 160));
            Text(n.ToString(), x + 30, y + 26, 16, Color.White);
            TextCentered(key, x + 22, y + 46, 14, new Color(200, 200, 210, 255));
            x += 50;
        }
        if (!w.WaveActive && !w.Won && !w.Lost) TextCentered(Pad.Active ? "Pause, LB: bag and vendor   RS: sandbags" : $"I: bag and vendor   B: sandbags ({World.SandbagCost} Kinah)", W / 2f, 46, 18, new Color(255, 214, 110, 255));
    }

    void DrawCharacterFrame(World w)
    {
        var p = w.P;
        var col = World.Hex(p.Cls.Color);
        Raylib.DrawRectangleRounded(new Rectangle(16, 16, 360, 112), 0.15f, 6, new Color(12, 12, 16, 190));
        Raylib.DrawCircle(64, 72, 38, col);
        Raylib.DrawCircle(64, 72, 33, new Color(20, 18, 24, 255));
        TextCentered(p.Level.ToString(), 64, 50, 40, col);
        Text(p.Cls.Name, 112, 22, 24, Color.White);
        Text(w.LevelRow.Title, 112 + Measure(p.Cls.Name, 24).X + 10, 27, 17, new Color(200, 190, 170, 255));
        Bar(112, 56, 250, 18, p.Hp / p.MaxHp, new Color(196, 40, 40, 255), $"{(int) p.Hp} / {(int) p.MaxHp}");
        if (p.Shield > 0) Raylib.DrawRectangle(112, 56, (int) (250 * MathF.Min(1, p.Shield / p.MaxHp)), 4, new Color(120, 180, 255, 255));
        Bar(112, 80, 250, 14, p.Mp / p.MaxMp, new Color(50, 110, 220, 255), $"{(int) p.Mp} / {(int) p.MaxMp}");
        if (p.Cls.Id == "gladiator") Raylib.DrawRectangle(112, 95, (int) (250 * w.Wrath / 100f), 3, w.Wrath >= 100 ? new Color(255, 70, 40, 255) : new Color(200, 90, 50, 255));
        // AION buff and debuff icons: a letter, a ring that runs down, seconds left
        var icons = new List<(string tag, float left, float total, Color col, bool bad)>();
        if (p.BuffTime > 0) icons.Add(($"+{(int) p.BuffPct}", p.BuffTime, 12, new Color(255, 200, 90, 255), false));
        if (p.Shield > 0) icons.Add(("SH", p.ShieldTime, 10, new Color(140, 190, 255, 255), false));
        if (p.AuraTime > 0) icons.Add(("SP", p.AuraTime, 20, new Color(255, 150, 60, 255), false));
        if (p.GuardTime > 0) icons.Add(("GD", p.GuardTime, 10, new Color(200, 210, 230, 255), false));
        if (p.HasteTime > 0) icons.Add(("HS", p.HasteTime, 10, new Color(140, 240, 210, 255), false));
        if (p.RegenTime > 0) icons.Add(("RG", p.RegenTime, 12, new Color(110, 230, 120, 255), false));
        if (p.Stealth > 0) icons.Add(("HD", p.Stealth, 8, new Color(170, 150, 220, 255), false));
        if (p.Salvation > 0) icons.Add(("SV", p.Salvation, 30, new Color(255, 235, 160, 255), false));
        if (w.CounterTime > 0) icons.Add(("CT", w.CounterTime, 1.5f, new Color(140, 220, 255, 255), false));
        if (w.PoisonTime > 0) icons.Add(("PO", w.PoisonTime, 4, new Color(140, 220, 60, 255), true));
        if (w.PlayerStun > 0) icons.Add(("ST", w.PlayerStun, 1.4f, new Color(255, 150, 110, 255), true));
        if (w.StanceOn) icons.Add(("SN", 1, 1, World.Hex(p.Cls.Color), false));
        if (w.CoatTime > 0) icons.Add(("PC", w.CoatTime, 12, new Color(140, 220, 60, 255), false));
        if (p.Cls.Id == "ranger" && w.StillTime > 1) icons.Add(("FO", 1, 1, new Color(230, 220, 160, 255), false));
        if (w.CommandTime > 0) icons.Add(("CM", w.CommandTime, 6, new Color(255, 200, 120, 255), false));
        var x = 112f;
        foreach (var (tag, left, total, ic, bad) in icons)
        {
            var r = new Rectangle(x, 98, 24, 24);
            Raylib.DrawRectangleRec(r, bad ? new Color(70, 20, 20, 230) : new Color(20, 26, 34, 230));
            Raylib.DrawRectangleLinesEx(r, 1, ic);
            var k = Math.Clamp(left / total, 0, 1);
            Raylib.DrawRectangle((int) x, (int) (98 + 24 * (1 - k)), 3, (int) (24 * k), ic);
            TextCentered(tag, x + 13, 100, 12, ic);
            TextCentered($"{MathF.Ceiling(left)}", x + 13, 111, 10, Color.White);
            x += 28;
        }
        // AION flight gauge
        Raylib.DrawRectangleRounded(new Rectangle(16, 134, 360, 30), 0.3f, 6, new Color(12, 12, 16, 170));
        Text(p.Flying ? "FLYING" : w.Gliding ? "GLIDING" : "FLIGHT", 26, 140, 15, p.Flying || w.Gliding ? new Color(170, 230, 255, 255) : new Color(200, 190, 170, 255));
        if (w.Flights == 0 && w.TimeAlive > 15 && !w.WaveActive) TextCentered(Pad.Active ? "Y: take flight with your Daeva wings" : "F: take flight with your Daeva wings", W / 2f, H * 0.66f, 22, new Color(170, 230, 255, 255));
        if (p.Flying) Text(Pad.Active ? "B dash   LT+A dive" : "Space dash   R dive   Shift boost", 26, 166, 14, new Color(170, 210, 230, 255));
        Bar(112, 141, 250, 14, p.FlightTime / Sheets.FlightSeconds, new Color(90, 200, 255, 255), $"{p.FlightTime:0.0}s   {(Pad.Active ? "Y" : "F")}");
        if (w.FlightNote != "") { Toast = w.FlightNote; ToastTime = 1.2f; w.FlightNote = ""; }
    }

    void Bar(float x, float y, float w, float h, float frac, Color c, string label)
    {
        Raylib.DrawRectangle((int) x, (int) y, (int) w, (int) h, new Color(30, 30, 36, 255));
        Raylib.DrawRectangleGradientV((int) x, (int) y, (int) (w * Math.Clamp(frac, 0, 1)), (int) h, c, World.Fade(c, 0.55f));
        Raylib.DrawRectangleLines((int) x, (int) y, (int) w, (int) h, new Color(0, 0, 0, 255));
        TextCentered(label, x + w / 2, y + h / 2 - h * 0.45f, h * 0.9f, Color.White);
    }

    void DrawHotbar(World w)
    {
        int sw = W, sh = H;
        var slots = Settings.S.SimpleHotbar ? 4 : 8;
        var size = (int) (Sheets.HotbarSlotSize * (slots == 4 ? 1.35f : 1)); var gap = Sheets.HotbarSlotGap;
        var total = slots * size + (slots - 1) * gap;
        var x0 = sw / 2f - total / 2f; var y0 = sh - size - 72;
        var col = World.Hex(w.P.Cls.Color);
        Raylib.DrawRectangleRounded(new Rectangle(x0 - 10, y0 - 10, total + 20, size + 20), 0.2f, 6, new Color(12, 12, 16, 190));
        for (var i = 0; i < slots; i++)
        {
            var s = w.ActiveSkill(i);
            var chain = s.ChainFrom != "none";
            var r = new Rectangle(x0 + i * (size + gap), y0, size, size);
            var unlocked = w.Unlocked(s);
            var cd = w.P.Cooldowns.TryGetValue(s.Id, out var c) ? c : 0;
            var mpShort = w.P.Mp < s.MpCost;
            var top = unlocked ? col : new Color(60, 60, 64, 255);
            Raylib.DrawRectangleGradientV((int) r.X, (int) r.Y, size, size, top, new Color(20, 18, 26, 255));
            DrawKindGlyph(s.Kind, r, unlocked ? Color.White : new Color(110, 110, 110, 255));
            if (unlocked && cd > 0)
            {
                // AION-style clockwise cooldown sweep with seconds left
                var frac = cd / s.Cooldown;
                Ui.Scissor(r.X, r.Y, size, size);
                Raylib.DrawCircleSector(new Vector2(r.X + size / 2f, r.Y + size / 2f), size * 0.72f, -90, -90 + 360 * frac, 32, new Color(0, 0, 0, 170));
                Raylib.EndScissorMode();
                TextCentered(cd >= 1 ? ((int) MathF.Ceiling(cd)).ToString() : cd.ToString("0.0"), r.X + size / 2f, r.Y + size / 2f - 14, 28, Color.White);
            }
            if (unlocked && mpShort) Raylib.DrawRectangleRec(r, new Color(20, 40, 120, 120));
            if (!unlocked) TextCentered($"Lv {s.UnlockLevel}", r.X + size / 2f, r.Y + size / 2f - 10, 20, new Color(220, 220, 220, 255));
            Raylib.DrawRectangleLinesEx(r, 2, unlocked && cd <= 0 && !mpShort ? new Color(255, 225, 150, 255) : new Color(0, 0, 0, 255));
            if (chain && w.P.Chains[i] is { } offer)
            {
                // AION chain skill: a pulsing gold frame, a CHAIN tag and the time left to use it
                var pulse = 0.6f + 0.4f * MathF.Sin((float) Raylib.GetTime() * 12);
                Raylib.DrawRectangleLinesEx(new Rectangle(r.X - 3, r.Y - 3, size + 6, size + 6), 3, World.Fade(new Color(255, 205, 60, 255), pulse));
                TextCentered("CHAIN", r.X + size / 2f, r.Y - 22, 14, new Color(255, 215, 90, 255));
                Raylib.DrawRectangle((int) r.X, (int) (r.Y + size - 4), (int) (size * Math.Clamp(offer.Time / Sheets.ChainSkillsWindow, 0, 1)), 4, new Color(255, 205, 60, 255));
            }
            Text(Pad.Active ? Pad.SkillLabels[i] : (i + 1).ToString(), r.X + 4, r.Y + 2, Pad.Active ? 13 : 18, Color.White);
            if (unlocked)
            {
                for (var k = 0; k < w.Rank(s); k++) Raylib.DrawRectangle((int) r.X + size - 8 - k * 7, (int) r.Y + 5, 5, 5, new Color(255, 210, 80, 255));
                Text(s.MpCost.ToString(), r.X + size - 4 - Measure(s.MpCost.ToString(), 15).X, r.Y + size - 18, 15, new Color(130, 170, 255, 255));
            }
            var name = s.Name.Length > 11 ? s.Name[..10] + "." : s.Name;
            TextCentered(name, r.X + size / 2f, r.Y + size + 4, 14, unlocked ? new Color(235, 230, 220, 255) : new Color(130, 130, 130, 255));
        }
    }

    /// The skill book (shown while paused): every hotbar skill with its chain links, what it does and what it costs.
    /// Where skill book slot i sits (for clicks): two columns, four rows.
    public static Rectangle SkillBookRect(int i, float top) => new(Ui.W / 2f - 560 + (i % 2) * 560, top + (i / 2) * 92, 520, 84);

    static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

    public void DrawSkillBook(World w, float top, int picked = -1)
    {
        int sw = W;
        var col = World.Hex(w.P.Cls.Color);
        float x0 = sw / 2f - 560, y = top, colW = 560;
        var mouse = Ui.Mouse;
        SkillRow? hover = null;
        for (var i = 0; i < 8; i++)
        {
            var s = w.Skill(i);
            var x = x0 + (i % 2) * colW;
            if (i % 2 == 0 && i > 0) y += 92;
            var unlocked = w.Unlocked(s);
            var r = new Rectangle(x, y, 44, 44);
            var area = SkillBookRect(i, top);
            if (Raylib.CheckCollisionPointRec(mouse, area)) { hover = s; Raylib.DrawRectangleLinesEx(area, 1, new Color(120, 110, 90, 200)); }
            if (picked == i) Raylib.DrawRectangleLinesEx(area, 2, new Color(255, 214, 110, 255));
            Raylib.DrawRectangleGradientV((int) r.X, (int) r.Y, 44, 44, unlocked ? col : new Color(60, 60, 64, 255), new Color(20, 18, 26, 255));
            DrawKindGlyph(s.Kind, r, unlocked ? Color.White : new Color(110, 110, 110, 255));
            Text($"{i + 1}  {s.Name}", x + 54, y - 2, 18, unlocked ? Color.White : new Color(150, 150, 150, 255));
            var facts = unlocked ? $"Rank {Roman(w.Rank(s))}   {s.MpCost} MP   {s.Cooldown:0.#}s cooldown   {s.Element}" : $"unlocks at level {s.UnlockLevel}";
            if (s.CastTime > 0) facts += $"   {s.CastTime:0.#}s cast";
            if (s.Cc != "none") facts += $"   {s.Cc} {s.CcTime:0.#}s";
            Text(facts, x + 54, y + 19, 14, new Color(200, 190, 170, 255));
            Text(s.Description, x + 54, y + 36, 14, new Color(225, 220, 210, 255));
            // its chain links
            var link = Sheets.Skills.FirstOrDefault(k => k.ChainFrom == s.Id);
            var cx = x + 54f;
            while (link != null)
            {
                var label = $"> {link.Name}" + (w.Unlocked(link) ? "" : $" (lv {link.UnlockLevel})");
                Text(label, cx, y + 54, 14, w.Unlocked(link) ? new Color(255, 215, 90, 255) : new Color(150, 140, 110, 255));
                cx += Measure(label, 14).X + 14;
                link = Sheets.Skills.FirstOrDefault(k => k.ChainFrom == link.Id);
            }
        }
        y += 100;
        TextCentered("Passive: " + World.PassiveOf(w.P.Cls.Id), sw / 2f, y, 17, new Color(200, 205, 225, 255));
        if (w.Stigmas.Count > 0) TextCentered("Stigmas: " + string.Join(", ", w.Stigmas.Select(id => World.StigmaTiers.SelectMany(t => t).First(d => d.Id == id).Name)), sw / 2f, y + 24, 17, new Color(210, 170, 255, 255));
        TextCentered(picked >= 0 ? "Now click the slot to swap it with" : "Click two skills to swap their hotbar slots", sw / 2f, y + 48, 16, new Color(170, 165, 155, 255));
        if (hover != null) DrawTooltip(w, hover, mouse);
    }

    /// A skill's tooltip at the mouse: rank, numbers, element, crowd control and its chains.
    void DrawTooltip(World w, SkillRow s, Vector2 at)
    {
        var lines = new List<(string, Color)>
        {
            (s.Name, new Color(255, 214, 110, 255)),
            (w.Unlocked(s) ? $"Rank {Roman(w.Rank(s))}  -  power {w.SkillPower(s):0}" : $"Unlocks at level {s.UnlockLevel}", Color.White),
            ($"{s.Kind}  -  {s.Element}" + (s.Range > 0 ? $"  -  {s.Range:0} m" : "") + (s.Radius > 0 ? $"  -  radius {s.Radius:0.#} m" : ""), new Color(200, 200, 210, 255)),
            ($"{s.MpCost} MP  -  {s.Cooldown:0.#}s cooldown" + (s.CastTime > 0 ? $"  -  {s.CastTime:0.#}s cast" : "  -  instant"), new Color(200, 200, 210, 255)),
        };
        if (s.Cc != "none") lines.Add(($"{s.Cc} for {s.CcTime:0.#}s", new Color(205, 170, 255, 255)));
        lines.Add((s.Description, new Color(230, 225, 215, 255)));
        var wdt = lines.Max(l => Measure(l.Item1, 16).X) + 24;
        var x = MathF.Min(at.X + 18, W - wdt - 10); var y = MathF.Min(at.Y + 18, H - lines.Count * 22 - 20);
        Raylib.DrawRectangle((int) x, (int) y, (int) wdt, lines.Count * 22 + 14, new Color(12, 12, 18, 235));
        Raylib.DrawRectangleLinesEx(new Rectangle(x, y, wdt, lines.Count * 22 + 14), 1, new Color(160, 140, 100, 255));
        for (var i = 0; i < lines.Count; i++) Text(lines[i].Item1, x + 12, y + 8 + i * 22, 16, lines[i].Item2);
    }

    public static Rectangle StigmaCard(int i) => new(Ui.W / 2f - 540 + i * 370, Ui.H * 0.32f, 340, 220);

    public void DrawStigma(World w, int pick)
    {
        Raylib.DrawRectangle(0, 0, Ui.W, Ui.H, new Color(6, 4, 12, 200));
        var tier = World.StigmaTiers[Math.Min(w.Stigmas.Count, World.StigmaTiers.Length - 1)];
        TextCentered("CHOOSE A STIGMA", Ui.W / 2f, Ui.H * 0.18f, 52, new Color(210, 160, 255, 255));
        for (var i = 0; i < tier.Length; i++)
        {
            var r = StigmaCard(i);
            Raylib.DrawRectangleRounded(r, 0.1f, 6, i == pick ? new Color(50, 30, 70, 240) : new Color(20, 16, 28, 230));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.1f, 6, i == pick ? 3 : 1, i == pick ? new Color(210, 160, 255, 255) : new Color(90, 80, 110, 255));
            TextCentered($"{i + 1}", r.X + r.Width / 2, r.Y + 16, 30, new Color(210, 160, 255, 255));
            TextCentered(tier[i].Name, r.X + r.Width / 2, r.Y + 80, 24, Color.White);
            TextCentered(tier[i].About, r.X + r.Width / 2, r.Y + 130, 18, new Color(220, 210, 235, 255));
        }
        TextCentered(Pad.Active ? "D-pad: choose   A: take it   B: later" : "1-3, arrows or click: choose   Enter: take it   Esc: later", Ui.W / 2f, Ui.H * 0.32f + 260, 20, new Color(200, 190, 210, 255));
    }

    void DrawKindGlyph(string kind, Rectangle r, Color c)
    {
        var cx = r.X + r.Width / 2; var cy = r.Y + r.Height / 2; var s = r.Width * 0.28f;
        switch (kind)
        {
            case "strike": Raylib.DrawLineEx(new Vector2(cx - s, cy + s), new Vector2(cx + s, cy - s), 5, c); Raylib.DrawLineEx(new Vector2(cx - s * 0.6f, cy - s * 0.1f), new Vector2(cx + s * 0.1f, cy + s * 0.6f), 4, c); break;
            case "shot": Raylib.DrawLineEx(new Vector2(cx - s, cy), new Vector2(cx + s, cy), 4, c); Raylib.DrawTriangle(new Vector2(cx + s * 1.2f, cy), new Vector2(cx + s * 0.5f, cy - s * 0.45f), new Vector2(cx + s * 0.5f, cy + s * 0.45f), c); break;
            case "nova": Raylib.DrawRing(new Vector2(cx, cy), s * 0.6f, s * 0.85f, 0, 360, 24, c); Raylib.DrawCircle((int) cx, (int) cy, s * 0.3f, c); break;
            case "blast": Raylib.DrawCircle((int) cx, (int) cy, s * 0.8f, c); Raylib.DrawRing(new Vector2(cx, cy), s * 0.95f, s * 1.1f, 0, 360, 24, c); break;
            case "dot": for (var i = 0; i < 3; i++) Raylib.DrawCircle((int) (cx - s * 0.6f + i * s * 0.6f), (int) (cy + (i % 2) * s * 0.4f - s * 0.2f), s * 0.25f, c); break;
            case "stun": Raylib.DrawPoly(new Vector2(cx, cy), 5, s, 0, c); break;
            case "dash": for (var i = 0; i < 3; i++) Raylib.DrawTriangle(new Vector2(cx - s + i * s * 0.7f + s * 0.6f, cy), new Vector2(cx - s + i * s * 0.7f, cy - s * 0.6f), new Vector2(cx - s + i * s * 0.7f, cy + s * 0.6f), c); break;
            case "heal": Raylib.DrawRectangle((int) (cx - s * 0.25f), (int) (cy - s), (int) (s * 0.5f), (int) (s * 2), c); Raylib.DrawRectangle((int) (cx - s), (int) (cy - s * 0.25f), (int) (s * 2), (int) (s * 0.5f), c); break;
            case "buff": Raylib.DrawTriangle(new Vector2(cx, cy - s), new Vector2(cx - s, cy + s * 0.2f), new Vector2(cx + s, cy + s * 0.2f), c); Raylib.DrawRectangle((int) (cx - s * 0.3f), (int) (cy + s * 0.2f), (int) (s * 0.6f), (int) (s * 0.8f), c); break;
            case "shield": Raylib.DrawRing(new Vector2(cx, cy), s * 0.75f, s, 200, 340, 16, c); Raylib.DrawRectangle((int) (cx - s * 0.7f), (int) (cy - s * 0.5f), (int) (s * 1.4f), (int) (s * 0.9f), c); break;
            case "aura": Raylib.DrawCircle((int) cx, (int) (cy - s * 0.2f), s * 0.45f, c); Raylib.DrawTriangle(new Vector2(cx, cy - s * 1.1f), new Vector2(cx - s * 0.45f, cy - s * 0.3f), new Vector2(cx + s * 0.45f, cy - s * 0.3f), c); break;
        }
    }

    /// Gears-style Active Reload bar under the crosshair: gold = fast reload, white = perfect (bonus damage).
    void DrawActiveReload(World w, float cx, float cy)
    {
        var g = w.Gun;
        const float bw = 280, bh = 12;
        var x = cx - bw / 2; var y = cy + 70;
        if (g.Reload > 0)
        {
            Raylib.DrawRectangle((int) x - 2, (int) y - 2, (int) bw + 4, (int) bh + 4, new Color(0, 0, 0, 170));
            if (g.Jammed)
                Raylib.DrawRectangle((int) x, (int) y, (int) (bw * g.Progress), (int) bh, new Color(200, 50, 40, 230));
            else
            {
                var start = Sheets.ActiveReloadZoneStart;
                Raylib.DrawRectangle((int) x, (int) y, (int) bw, (int) bh, new Color(70, 64, 58, 220));
                Raylib.DrawRectangle((int) (x + bw * start), (int) y, (int) (bw * Sheets.ActiveReloadGoodWidth), (int) bh, new Color(230, 170, 60, 255));
                Raylib.DrawRectangle((int) (x + bw * start), (int) y, (int) MathF.Max(3, bw * Sheets.ActiveReloadPerfectWidth), (int) bh, new Color(255, 250, 235, 255));
            }
            var mx = x + bw * Math.Clamp(g.Progress, 0, 1);
            Raylib.DrawRectangle((int) mx - 2, (int) y - 6, 4, (int) bh + 12, g.Tried ? new Color(160, 160, 160, 255) : Color.White);
            if (!g.Tried) TextCentered(Pad.Active ? "X  Active Reload" : "R  Active Reload", cx, y + bh + 14, 15, new Color(230, 220, 200, 220));
        }
        if (w.ReloadTextTime > 0 && !(g.Reload > 0 && !g.Tried))
        {
            var col = w.ReloadText == "JAMMED" ? new Color(255, 90, 70, 255) : w.ReloadText.StartsWith("PERFECT") ? new Color(255, 250, 235, 255) : new Color(255, 200, 90, 255);
            TextCentered(w.ReloadText, cx, y + bh / 2, 24, World.Fade(col, MathF.Min(1, w.ReloadTextTime * 2)));
        }
        if (w.P.ActiveBonus > 0)
            TextCentered($"Active Reload  +{Sheets.ActiveReloadBonusDamagePct}%  {MathF.Ceiling(w.P.ActiveBonus)}s", cx, y + bh + 36, 16, new Color(255, 140, 80, 230));
    }

    /// AION-style target frame: name, HP with percent, and what's affecting it.
    void DrawTargetFrame(World w, float cx)
    {
        var t = w.FrameTarget;
        if (t == null || t.Hp <= 0) return;
        const float fw = 380, fh = 62;
        var x = cx - fw / 2; var y = 50f;
        var locked = t == w.Locked;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, fw, fh), 0.18f, 6, new Color(12, 10, 14, 200));
        if (locked) Raylib.DrawRectangleRoundedLinesEx(new Rectangle(x, y, fw, fh), 0.18f, 6, 2, new Color(255, 80, 60, 230));
        Text($"Lv {w.EnemyLevel(t)}  {t.Def.Name}", x + 12, y + 6, 18, new Color(255, 120, 100, 255));
        var tag = locked ? "LOCKED" : Pad.Active ? "RS to lock" : "Tab to lock";
        Text(tag, x + fw - 12 - Measure(tag, 13).X, y + 9, 13, locked ? new Color(255, 120, 100, 255) : new Color(170, 160, 150, 255));
        var frac = Math.Clamp(t.Hp / t.MaxHp, 0, 1);
        Raylib.DrawRectangle((int) x + 12, (int) y + 30, (int) fw - 24, 14, new Color(50, 20, 20, 255));
        Raylib.DrawRectangle((int) x + 12, (int) y + 30, (int) ((fw - 24) * frac), 14, new Color(210, 50, 40, 255));
        TextCentered($"{(int) t.Hp:N0} / {(int) t.MaxHp:N0}  ({frac * 100:0}%)", cx, y + 29, 13, Color.White);
        var status = new List<string>();
        if (t.Emerge >= 0) status.Add("Emerging");
        if (t.Stun > 0) status.Add($"Stunned {t.Stun:0.0}s");
        if (t.Dots.Count > 0) status.Add(t.Dots.Count > 1 ? $"Burning x{t.Dots.Count}" : "Burning");
        if (t.Root > 0) status.Add("Rooted");
        if (t.Slow > 0) status.Add("Slowed");
        if (t.Silence > 0) status.Add($"Silenced {t.Silence:0.0}s");
        if (t.Disarm > 0) status.Add($"Disarmed {t.Disarm:0.0}s");
        if (t.Downed > 0) status.Add("DOWN - execute it");
        if (t.Elite) status.Add("Elite (rallying)");
        if (t.InCover) status.Add("In cover");
        if (t.Stagger > 0.05f) status.Add($"Stagger {t.Stagger * 100:0}%");
        if (t.Def.Weak != "none") status.Add($"Weak: {t.Def.Weak}");
        if (status.Count > 0) Text(string.Join("   ", status), x + 12, y + fh - 17, 13, new Color(255, 200, 110, 255));
    }

    /// Evasion pip beside the hotbar, with its cooldown.
    void DrawEvade(World w)
    {
        int sw = W, sh = H;
        var size = Sheets.HotbarSlotSize; var gap = Sheets.HotbarSlotGap;
        var total = 8 * size + 7 * gap;
        var r = new Rectangle(sw / 2f - total / 2f - 10 - 16 - 50, sh - size - 72 + (size - 50) / 2f, 50, 50);
        var ready = w.P.EvadeCd <= 0;
        Raylib.DrawRectangleRounded(r, 0.25f, 6, ready ? new Color(60, 90, 120, 230) : new Color(30, 30, 36, 220));
        TextCentered("EVADE", r.X + 25, r.Y + 17, 13, ready ? Color.White : new Color(140, 140, 140, 255));
        if (!ready) Raylib.DrawRectangle((int) r.X + 4, (int) (r.Y + 44), (int) (42 * (1 - Math.Clamp(w.P.EvadeCd / Sheets.EvasionCooldown, 0, 1))), 3, new Color(170, 220, 255, 255));
        TextCentered(Pad.Active ? "B" : "Space", r.X + 25, r.Y + 52, 13, new Color(200, 190, 170, 255));
    }

    /// AION cast bar above the hotbar.
    void DrawCastBar(World w, float cx)
    {
        if (w.P.Casting is not { } c) return;
        int sh = H;
        const float bw = 320, bh = 16;
        var y = sh - Sheets.HotbarSlotSize - 72 - 58;
        var k = Math.Clamp(c.Time / c.Skill.CastTime, 0, 1);
        Raylib.DrawRectangle((int) (cx - bw / 2) - 2, (int) y - 2, (int) bw + 4, (int) bh + 4, new Color(0, 0, 0, 190));
        Raylib.DrawRectangleGradientH((int) (cx - bw / 2), (int) y, (int) (bw * k), (int) bh, World.Fade(World.Hex(w.P.Cls.Color), 0.7f), World.Hex(w.P.Cls.Color));
        TextCentered($"{c.Skill.Name}  {MathF.Max(0, c.Skill.CastTime - c.Time):0.0}s", cx, y - 1, 15, Color.White);
    }

    void DrawWeapons(World w)
    {
        int sw = W, sh = H;
        var bw = 168; var bh = 74;
        var x0 = sw - bw - 20; var y0 = sh - (bh + 6) * w.P.Guns.Length - 30;
        for (var i = 0; i < w.P.Guns.Length; i++)
        {
            var g = w.P.Guns[i];
            var sel = i == w.P.Gun;
            var r = new Rectangle(x0 - (sel ? 16 : 0), y0 + i * (bh + 6), bw + (sel ? 16 : 0), bh);
            Raylib.DrawRectangleRounded(r, 0.15f, 6, sel ? new Color(40, 30, 22, 220) : new Color(12, 12, 16, 160));
            if (sel) Raylib.DrawRectangleRoundedLinesEx(r, 0.15f, 6, 2, new Color(255, 190, 90, 255));
            DrawGunIcon(g.Def.Id, new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, r.Height - 22), sel ? Color.White : new Color(170, 170, 170, 255));
            Text(Pad.Active ? g.Def.Name : $"{g.Def.Key}  {g.Def.Name}", r.X + 8, r.Y + bh - 22, 16, sel ? Color.White : new Color(170, 170, 170, 255));
            var ammo = g.Reload > 0 ? "RELOAD" : $"{g.Ammo}/{g.Def.Clip}";
            Text(ammo, r.X + r.Width - 8 - Measure(ammo, 16).X, r.Y + bh - 22, 16, g.Ammo == 0 || g.Reload > 0 ? new Color(255, 120, 90, 255) : new Color(255, 220, 160, 255));
            if (g.Reload > 0) Raylib.DrawRectangle((int) r.X + 6, (int) (r.Y + bh - 4), (int) ((r.Width - 12) * g.Progress), 3, g.Jammed ? new Color(220, 60, 50, 255) : new Color(255, 190, 90, 255));
        }
        Text("Gears of War: E-Day arsenal", x0, y0 - 24, 15, new Color(190, 180, 160, 255));
    }

    void DrawXpBar(World w)
    {
        int sw = W, sh = H;
        var need = w.LevelRow.XpToNext;
        var frac = need > 0 ? w.P.Xp / (float) need : 1f;
        Raylib.DrawRectangle(0, sh - 14, sw, 14, new Color(10, 10, 14, 220));
        Raylib.DrawRectangleGradientH(0, sh - 12, (int) (sw * frac), 10, new Color(120, 70, 220, 255), new Color(200, 140, 255, 255));
        for (var i = 1; i < 10; i++) Raylib.DrawRectangle(sw * i / 10, sh - 12, 1, 10, new Color(0, 0, 0, 160));
        var label = need > 0 ? $"Lv {w.P.Level}   XP {w.P.Xp} / {need}  ({frac * 100:0.0}%)" : $"Lv {w.P.Level}   MAX";
        TextCentered(label, sw / 2f, sh - 34, 17, new Color(220, 200, 255, 255));
    }
}
