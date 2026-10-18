using System;
using System.Collections.Generic;
using System.Linq;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// The custom wave editor (Custom Waves mode): how many of each Locust per wave, and the boss of the fifth.
static class CustomEditor
{
    public static bool Open;
    static int _row;
    static string[] Types => Sheets.Enemies.Where(e => e.Special != "boss" && e.Id is not ("dummy" or "nemacyst")).Select(e => e.Id).ToArray();
    static readonly string[] Bosses = { "none", "vrak", "corpser", "vrak-scorched", "brumak" };

    /// Returns true when the player starts (go on to class select).
    public static bool Update()
    {
        var types = Types;
        var n = types.Length + 2;
        var (pdx, pdy) = Pad.MenuNav();
        if (Inp.IsKeyPressed(KeyboardKey.Down) || pdy > 0) _row = (_row + 1) % n;
        if (Inp.IsKeyPressed(KeyboardKey.Up) || pdy < 0) _row = (_row + n - 1) % n;
        var d = Inp.IsKeyPressed(KeyboardKey.Right) || pdx > 0 ? 1 : Inp.IsKeyPressed(KeyboardKey.Left) || pdx < 0 ? -1 : 0;
        var c = Profile.P.CustomWave;
        if (d != 0 && _row < types.Length) { c[types[_row]] = Math.Clamp(c.GetValueOrDefault(types[_row]) + d, 0, 20); Sfx.Play(Sfx.Click); }
        if (d != 0 && _row == types.Length) { Profile.P.CustomBoss = Bosses[(Array.IndexOf(Bosses, Profile.P.CustomBoss) + d + Bosses.Length) % Bosses.Length]; Sfx.Play(Sfx.Click); }
        if ((Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Confirm) && _row == n - 1)
        {
            if (c.Values.Sum() == 0) c["wretch"] = 4;
            Profile.Save(); Open = false; return true;
        }
        if (Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Cancel) { Profile.Save(); Open = false; }
        return false;
    }

    public static void Draw(Hud hud)
    {
        int sw = Ui.W, sh = Ui.H;
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(8, 8, 12, 230));
        hud.TextCentered("CUSTOM WAVES", sw / 2f, 50, 44, new Color(255, 214, 110, 255));
        var types = Types;
        for (var i = 0; i < types.Length + 2; i++)
        {
            var y = 110 + i * 34f;
            var sel = i == _row;
            Raylib.DrawRectangle(sw / 2 - 320, (int) y, 640, 30, sel ? new Color(70, 60, 40, 230) : new Color(30, 30, 36, 200));
            string label, value;
            if (i < types.Length) { label = Sheets.Enemies.First(e => e.Id == types[i]).Name; value = $"<  {Profile.P.CustomWave.GetValueOrDefault(types[i])}  >"; }
            else if (i == types.Length) { label = "Boss of wave 5"; value = $"<  {(Profile.P.CustomBoss == "none" ? "none" : Sheets.Enemies.First(e => e.Id == Profile.P.CustomBoss).Name)}  >"; }
            else { label = "Start"; value = "Enter"; }
            hud.Text(label, sw / 2f - 308, y + 5, 19, i == types.Length + 1 ? new Color(255, 214, 110, 255) : Color.White);
            hud.Text(value, sw / 2f + 308 - hud.Measure(value, 19).X, y + 5, 19, sel ? new Color(255, 225, 150, 255) : new Color(200, 200, 210, 255));
        }
        hud.TextCentered("Up/Down: choose   Left/Right: how many   Enter on Start: fight   Esc: back", sw / 2f, sh - 40, 18, new Color(200, 200, 210, 255));
    }
}
