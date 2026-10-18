using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The options screen (O from the title, class select or pause; Start+Y... on a pad: Y in the pause menu).
/// Up/down picks a row, left/right changes it; changes apply and save at once.
static class Options
{
    public static bool Open, KeysOpen, PadOpen;
    static int _row, _keyRow, _scroll;
    static bool _waitingKey;
    public static bool Muted;

    static readonly int[] FrameCaps = { 0, 30, 60, 120, 144, 240 };

    sealed record Row(string Label, Func<string> Value, Action<int> Change);

    static readonly Row[] Rows =
    {
        new("Delete all saved data ...", () => _wipeAsk > 0 ? "Press again to confirm" : "Enter", _ => Wipe()),
        new("Key bindings ...", () => "Enter", _ => KeysOpen = true),
        new("Gamepad buttons ...", () => "Enter", _ => PadOpen = true),
        new("Game speed", () => $"{Settings.S.GameSpeed}%", d => Settings.S.GameSpeed = Math.Clamp(Settings.S.GameSpeed + d * 20, 60, 100)),
        new("Colour-blind filter", () => new[] { "Off", "Deuteranopia", "Protanopia", "Tritanopia" }[Settings.S.ColourBlind], d => Settings.S.ColourBlind = (Settings.S.ColourBlind + d + 4) % 4),
        new("Gamepad look curve", () => new[] { "Linear", "Smooth", "Sharp" }[Settings.S.LookCurve], d => Settings.S.LookCurve = (Settings.S.LookCurve + d + 3) % 3),
        new("Gamepad aim assist", () => OnOff(Settings.S.AimAssist), _ => Settings.S.AimAssist = !Settings.S.AimAssist),
        new("Mouse-only controls", () => OnOff(Settings.S.MouseOnly), _ => Settings.S.MouseOnly = !Settings.S.MouseOnly),
        new("Auto-evade red circles", () => OnOff(Settings.S.AutoEvade), _ => Settings.S.AutoEvade = !Settings.S.AutoEvade),
        new("Simple hotbar (4 slots)", () => OnOff(Settings.S.SimpleHotbar), _ => Settings.S.SimpleHotbar = !Settings.S.SimpleHotbar),
        new("Camera smoothing", () => OnOff(Settings.S.CameraSmoothing), _ => Settings.S.CameraSmoothing = !Settings.S.CameraSmoothing),
        new("Lock-on camera", () => OnOff(Settings.S.LockOnCamera), _ => Settings.S.LockOnCamera = !Settings.S.LockOnCamera),
        new("COG squadmate", () => OnOff(Settings.S.Squadmate), _ => Settings.S.Squadmate = !Settings.S.Squadmate),
        new("Sound captions", () => OnOff(Settings.S.Captions), _ => Settings.S.Captions = !Settings.S.Captions),
        new("Difficulty", () => new[] { "Easy", "Normal", "Hard", "Daeva (very hard)" }[Settings.S.Difficulty], d => Settings.S.Difficulty = (Settings.S.Difficulty + d + 4) % 4),
        new("Master volume", () => $"{Settings.S.MasterVolume}%", d => Settings.S.MasterVolume += d * 10),
        new("Music volume", () => $"{Settings.S.MusicVolume}%", d => Settings.S.MusicVolume = Math.Clamp(Settings.S.MusicVolume + d * 10, 0, 100)),
        new("Ambience volume", () => $"{Settings.S.AmbienceVolume}%", d => Settings.S.AmbienceVolume = Math.Clamp(Settings.S.AmbienceVolume + d * 10, 0, 100)),
        new("Effects volume", () => $"{Settings.S.EffectsVolume}%", d => Settings.S.EffectsVolume += d * 10),
        new("Mouse sensitivity", () => $"{Settings.S.MouseSensitivity:0.0}x", d => Settings.S.MouseSensitivity = MathF.Round(Settings.S.MouseSensitivity + d * 0.1f, 1)),
        new("Controller look speed", () => $"{Settings.S.PadLookSpeed:0.0}x", d => Settings.S.PadLookSpeed = MathF.Round(Settings.S.PadLookSpeed + d * 0.1f, 1)),
        new("Invert look", () => OnOff(Settings.S.InvertLook), _ => Settings.S.InvertLook = !Settings.S.InvertLook),
        new("Field of view", () => $"{Settings.S.Fov}", d => Settings.S.Fov += d * 2),
        new("Camera distance", () => $"{Settings.S.CameraDistance:0.0}x", d => Settings.S.CameraDistance = MathF.Round(Settings.S.CameraDistance + d * 0.1f, 1)),
        new("Controller rumble", () => OnOff(Settings.S.Rumble), _ => Settings.S.Rumble = !Settings.S.Rumble),
        new("Subtitle size", () => $"{Settings.S.CaptionSize}%", d => Settings.S.CaptionSize = Math.Clamp(Settings.S.CaptionSize + d * 25, 75, 200)),
        new("Wing size", () => $"{Settings.S.WingSize}%", d => Settings.S.WingSize = Math.Clamp(Settings.S.WingSize + d * 10, 50, 130)),
        new("Wings", () => Settings.S.WingStyle == 0 ? "Elyos white" : "Asmodian dark", _ => Settings.S.WingStyle = 1 - Settings.S.WingStyle),
        new("Camera shoulder", () => Settings.S.LeftShoulder ? "Left" : "Right", _ => Settings.S.LeftShoulder = !Settings.S.LeftShoulder),
        new("Camera shake", () => $"{Settings.S.CameraShake}%", d => Settings.S.CameraShake += d * 10),
        new("Reduce flashing", () => OnOff(Settings.S.ReduceFlashing), _ => Settings.S.ReduceFlashing = !Settings.S.ReduceFlashing),
        new("Graphics preset", () => new[] { "Low", "Medium", "High" }[Settings.S.Preset], d =>
        {
            Settings.S.Preset = (Settings.S.Preset + d + 3) % 3;
            // the preset sets the heavy options too
            Settings.S.Shadows = Settings.S.Preset > 0; Settings.S.Bloom = Settings.S.Preset > 0;
        }),
        new("Monitor", () => $"{Settings.S.Monitor + 1} of {Math.Max(1, Raylib.GetMonitorCount())}", d => Settings.S.Monitor = (Settings.S.Monitor + d + Math.Max(1, Raylib.GetMonitorCount())) % Math.Max(1, Raylib.GetMonitorCount())),
        new("Bloom and colour grade", () => OnOff(Settings.S.Bloom), _ => Settings.S.Bloom = !Settings.S.Bloom),
        new("Colour grade", () => new[] { "Gears (warm)", "Abyss (violet)", "Neutral" }[Settings.S.Grade], d => Settings.S.Grade = (Settings.S.Grade + d + 3) % 3),
        new("Sun shadows", () => OnOff(Settings.S.Shadows), _ => Settings.S.Shadows = !Settings.S.Shadows),
        new("Damage numbers", () => OnOff(Settings.S.DamageNumbers), _ => Settings.S.DamageNumbers = !Settings.S.DamageNumbers),
        new("Auto-attack locked target", () => OnOff(Settings.S.AutoAttack), _ => Settings.S.AutoAttack = !Settings.S.AutoAttack),
        new("Auto-target skills", () => OnOff(Settings.S.AutoTarget), _ => Settings.S.AutoTarget = !Settings.S.AutoTarget),
        new("Sprint", () => Settings.S.HoldToSprint ? "Hold" : "Toggle", _ => Settings.S.HoldToSprint = !Settings.S.HoldToSprint),
        new("Interface size", () => $"{Settings.S.UiScale}%", d => Settings.S.UiScale += d * 5),
        new("Fullscreen", () => OnOff(Settings.S.Fullscreen), _ => Settings.S.Fullscreen = !Settings.S.Fullscreen),
        new("Frame rate", () => Settings.S.FrameCap == 0 ? "VSync" : $"{Settings.S.FrameCap} fps",
            d => Settings.S.FrameCap = FrameCaps[(Array.IndexOf(FrameCaps, Settings.S.FrameCap) + d + FrameCaps.Length) % FrameCaps.Length]),
    };

    static string OnOff(bool b) => b ? "On" : "Off";
    static int _wipeAsk;

    /// A clean slate: the profile, settings and the caches built from your installs (logs stay for troubleshooting).
    static void Wipe()
    {
        if (++_wipeAsk < 2) return;
        _wipeAsk = 0;
        var dir = Paths.DataDir;
        foreach (var f in new[] { "profile.json", "settings.json" }) try { System.IO.File.Delete(System.IO.Path.Combine(dir, f)); } catch { }
        foreach (var d in new[] { "city-cache", "sound-cache" }) try { System.IO.Directory.Delete(System.IO.Path.Combine(dir, d), true); } catch { }
        Profile.P = new(); Settings.S = new();
        Log.Info("all saved data deleted");
    }

    static string Help(string row) => row switch
    {
        "Key bindings ..." => "Change any keyboard key.",
        "Gamepad buttons ..." => "Move evade, flight, sprint and execute to other face buttons.",
        "Game speed" => "Slow the whole game down if it's too fast to follow.",
        "Colour-blind filter" => "Shifts colours so reds, greens and blues stay easy to tell apart.",
        "Gamepad look curve" => "Smooth and Sharp make small stick movements finer.",
        "Gamepad aim assist" => "Your aim drifts onto a Locust close to the crosshair.",
        "Mouse-only controls" => "Right mouse walks, the wheel picks a skill, middle click casts it, side buttons evade and fly.",
        "Auto-evade red circles" => "Rolls you out of a red circle at the last moment (once every 6 seconds).",
        "Simple hotbar (4 slots)" => "Shows four big slots; keys 5-8 still work.",
        "Camera smoothing" => "The camera eases after you instead of snapping.",
        "Lock-on camera" => "The camera turns to keep your locked target in view.",
        "Sound captions" => "Shows who made a sound: Locust screams, gunfire, emergence.",
        "Difficulty" => "How hard Locust hit and how much HP they have. Harder scores more.",
        "Reduce flashing" => "Softens glows and turns off bloom and pulsing.",
        "Auto-attack locked target" => "Keeps basic-attacking your locked target.",
        "Auto-target skills" => "Skills turn to the nearest Locust when you aren't aiming at one.",
        "Interface size" => "Scales the HUD.",
        _ => "",
    };

    static void Change(int d)
    {
        Rows[_row].Change(d);
        Settings.Apply(Muted);
        Settings.Save();
        Sfx.Play(Sfx.Click);
    }

    /// Returns true while the screen is open (it takes the input).
    public static bool Update()
    {
        if (!Open) return false;
        if (KeysOpen || PadOpen) { UpdateBindings(); return true; }
        var (pdx, pdy) = Pad.MenuNav();
        if (Inp.IsKeyPressed(KeyboardKey.Down) || Inp.IsKeyPressed(KeyboardKey.S) || pdy > 0) { _row = (_row + 1) % Rows.Length; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Up) || Inp.IsKeyPressed(KeyboardKey.W) || pdy < 0) { _row = (_row + Rows.Length - 1) % Rows.Length; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Right) || Inp.IsKeyPressed(KeyboardKey.D) || pdx > 0) Change(1);
        if (Inp.IsKeyPressed(KeyboardKey.Left) || Inp.IsKeyPressed(KeyboardKey.A) || pdx < 0) Change(-1);
        // mouse: click a row's arrows
        var m = Ui.Mouse;
        if (_row < _scroll) _scroll = _row;
        if (_row >= _scroll + Visible) _scroll = _row - Visible + 1;
        for (var i = 0; i < Rows.Length; i++)
        {
            var r = RowRect(i);
            if (!Shown(i) || !Raylib.CheckCollisionPointRec(m, r)) continue;
            if (Inp.GetMouseDelta() != Vector2.Zero) _row = i;
            if (Inp.IsMouseButtonPressed(MouseButton.Left)) { _row = i; Change(m.X < r.X + r.Width * 0.75f ? -1 : 1); }
            if (Inp.IsMouseButtonPressed(MouseButton.Right)) { _row = i; Change(-1); }
        }
        if ((Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Confirm) && Rows[_row].Label.EndsWith("...")) { Rows[_row].Change(1); return true; }
        if (Inp.IsKeyPressed(KeyboardKey.Escape) || Inp.IsKeyPressed(KeyboardKey.O) || Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Cancel || Pad.Back) { Open = false; Sfx.Play(Sfx.Click); }
        return true;
    }

    const int Visible = 16;
    static Rectangle RowRect(int i)
    {
        int sw = Ui.W, sh = Ui.H;
        var h = MathF.Min(38, (sh - 260) / (float) Visible);
        return new Rectangle(sw / 2f - 360, 140 + (i - _scroll) * h, 720, h - 4);
    }
    static bool Shown(int i) => i >= _scroll && i < _scroll + Visible;

    static void UpdateBindings()
    {
        var n = KeysOpen ? Keys.Actions.Length : Keys.PadActions.Length;
        if (_waitingKey)
        {
            var k = (KeyboardKey) Raylib.GetKeyPressed();
            if (k == KeyboardKey.Escape) _waitingKey = false;
            else if (k != KeyboardKey.Null) { Keys.Bind(Keys.Actions[_keyRow].id, k); _waitingKey = false; Sfx.Play(Sfx.Click); }
            return;
        }
        var (pdx, pdy) = Pad.MenuNav();
        if (Inp.IsKeyPressed(KeyboardKey.Down) || pdy > 0) _keyRow = (_keyRow + 1) % n;
        if (Inp.IsKeyPressed(KeyboardKey.Up) || pdy < 0) _keyRow = (_keyRow + n - 1) % n;
        _keyRow = Math.Clamp(_keyRow, 0, n - 1);
        if (KeysOpen && (Inp.IsKeyPressed(KeyboardKey.Enter) || Inp.IsMouseButtonPressed(MouseButton.Left))) _waitingKey = true;
        if (PadOpen && (pdx != 0 || Inp.IsKeyPressed(KeyboardKey.Right) || Inp.IsKeyPressed(KeyboardKey.Left) || Pad.Confirm))
        { Keys.CyclePad(Keys.PadActions[_keyRow].id, pdx < 0 || Inp.IsKeyPressed(KeyboardKey.Left) ? -1 : 1); Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Backspace)) { Settings.S.KeyBinds.Clear(); Settings.S.PadBinds.Clear(); Settings.Save(); }
        if (Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Cancel) { KeysOpen = PadOpen = false; _keyRow = 0; }
    }

    static void DrawBindings(Hud hud)
    {
        int sw = Ui.W, sh = Ui.H;
        hud.TextCentered(KeysOpen ? "KEY BINDINGS" : "GAMEPAD BUTTONS", sw / 2f, 70, 44, new Color(255, 214, 110, 255));
        var n = KeysOpen ? Keys.Actions.Length : Keys.PadActions.Length;
        var cols = KeysOpen ? 2 : 1;
        var perCol = (n + cols - 1) / cols;
        for (var i = 0; i < n; i++)
        {
            var x = sw / 2f - (cols == 2 ? 620 : 300) + (i / perCol) * 640; var y = 140 + (i % perCol) * 34f;
            var sel = i == _keyRow;
            Raylib.DrawRectangle((int) x, (int) y, 600, 30, sel ? new Color(70, 60, 40, 230) : new Color(30, 30, 36, 200));
            var label = KeysOpen ? Keys.Actions[i].label : Keys.PadActions[i].label;
            var value = KeysOpen ? (sel && _waitingKey ? "press a key..." : Keys.Name(Keys.Key(Keys.Actions[i].id))) : Keys.PadName(Keys.Pad(Keys.PadActions[i].id));
            hud.Text(label, x + 12, y + 5, 19, Color.White);
            hud.Text(value, x + 600 - 12 - hud.Measure(value, 19).X, y + 5, 19, sel ? new Color(255, 225, 150, 255) : new Color(200, 200, 210, 255));
        }
        hud.TextCentered(KeysOpen ? "Up/Down: choose   Enter or click: rebind   Backspace: defaults   Esc: back" : "Up/Down: choose   Left/Right: change button   Backspace: defaults   B/Esc: back", sw / 2f, sh - 50, 20, new Color(200, 200, 210, 255));
    }

    public static void Draw(Hud hud)
    {
        if (!Open) return;
        int sw = Ui.W, sh = Ui.H;
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(8, 8, 12, 225));
        if (KeysOpen || PadOpen) { DrawBindings(hud); return; }
        hud.TextCentered("OPTIONS", sw / 2f, 70, 48, new Color(255, 214, 110, 255));
        for (var i = 0; i < Rows.Length; i++)
        {
            if (!Shown(i)) continue;
            var r = RowRect(i);
            var sel = i == _row;
            Raylib.DrawRectangleRec(r, sel ? new Color(70, 60, 40, 220) : new Color(30, 30, 36, 200));
            if (sel) Raylib.DrawRectangleLinesEx(r, 2, new Color(255, 214, 110, 255));
            var ts = MathF.Min(22, r.Height * 0.62f);
            hud.Text(Rows[i].Label, r.X + 16, r.Y + (r.Height - ts) / 2, ts, Color.White);
            var v = $"<  {Rows[i].Value()}  >";
            var size = hud.Measure(v, ts);
            hud.Text(v, r.X + r.Width - 16 - size.X, r.Y + (r.Height - ts) / 2, ts, sel ? new Color(255, 225, 150, 255) : new Color(200, 200, 210, 255));
        }
        hud.TextCentered(Help(Rows[_row].Label), sw / 2f, sh - 80, 18, new Color(255, 225, 160, 255));
        hud.TextCentered(Pad.Active ? "D-pad: choose and change    B: back" : "Up/Down: choose    Left/Right or click: change    Esc: back",
            sw / 2f, sh - 50, 20, new Color(200, 200, 210, 255));
    }
}
