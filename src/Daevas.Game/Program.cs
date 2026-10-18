using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

enum Screen { Loading, Title, ModeSelect, ClassSelect, Deploying, Playing, Viewer }

/// Gears of Aion: AION-style classes, skills and levels in a cover-shooter arena armed with
/// the real guns from the player's own Gears of War: E-Day install, under AION 2's own splash art.
static class Program
{
    static GameAssets _assets = null!;
    static Hud _hud = null!;
    static World? _world;
    static Screen _screen = Screen.Loading;
    static int _pick;
    static TestMode? _test;
    static Random _rng = new();
    static float _autoTimer, _testClock;
    static int _autoSlot;
    static float _autoBlind;
    static bool _autoPlay;
    static string? _shotPath;
    static float _shotAfter = -1;
    static (float yaw, float height, float dist)? _overview; // dev: --overview <yaw> <height> <dist>
    static readonly (Vector3 eye, Vector3 at)? _devCam = Environment.GetEnvironmentVariable("DAEVAS_CAM")?.Split(',') is { Length: 6 } c
        ? (new Vector3(F(c[0]), F(c[1]), F(c[2])), new Vector3(F(c[3]), F(c[4]), F(c[5]))) : null;
    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    static readonly (Vector3 eye, Vector3 at)[]? _camPath = Environment.GetEnvironmentVariable("DAEVAS_CAMPATH")?.Split(';')
        .Select(p => p.Split(',')).Where(c => c.Length == 6)
        .Select(c => (new Vector3(F(c[0]), F(c[1]), F(c[2])), new Vector3(F(c[3]), F(c[4]), F(c[5])))).ToArray();
    static string? _shotWhen;
    static Recorder? _recorder;              // dev: --record <file.mp4> --record-start <s> --record-seconds <s>
    static float _recordStart = 8, _recordSeconds = 20, _recordClock, _clipYaw;
    static Vector3 _clipEye;
    static int _jumpWave, _jumpLevel;
    static float? _forcePitch;               // dev (shots): hold the camera pitch, in radians        // dev: --wave <n> --level <n> // dev: reload | emerge | death - wait for that moment before the shot
    static int _shotHold = -1; // frames the world stays paused before the shot (hidden windows can lag a few frames)
    static Screen _shotScreen = Screen.Playing, _afterLoading = Screen.Title;
    static Clip _viewerClip = Clip.Idle;
    static float _viewerYaw;

    [STAThread]
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr FindWindow(string? cls, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    static long _memAt;
    static bool _memWarned;

    /// Every 30 s a memory line in the log; past 6 GB, once, a report of what the game holds (so a climb can be traced).
    static void WatchMemory()
    {
        if (Environment.TickCount64 < _memAt) return;
        _memAt = Environment.TickCount64 + 30000;
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        var gb = p.PrivateMemorySize64 / 1073741824.0;
        Log.Memory($"on {_screen}");
        if (gb > 6 && !_memWarned)
        {
            _memWarned = true;
            var w = _world;
            Log.Info($"MEMORY HIGH ({gb:0.0} GB): screen {_screen}, city chunks {_assets.City?.Chunks.Count ?? 0}, characters {_assets.Characters.Count}, " +
                     (w == null ? "no run" : $"hero at {w.P.Pos}, flying {w.P.Flying}, enemies {w.Enemies.Count}, dying {w.Dying.Count}, effects {w.Effects.Count}"));
        }
    }

    static void ShowRunningCopy()
    {
        var h = FindWindow(null, "Gears of Aion");
        if (h == IntPtr.Zero) return;
        ShowWindow(h, 9); // restore if minimised
        SetForegroundWindow(h);
    }

    static void Main(string[] args)
    {
        // one copy at a time: a second launch (say, clicking again while it loads) brings the first to the front
        // instead of loading the game twice over. Dev and test runs (--background, --logo...) don't take part.
        var player = !args.Any(a => a is "--background" or "--logo" or "--splash") && Environment.GetEnvironmentVariable("DAEVAS_TEST") == null;
        var first = true;
        using var single = player ? new System.Threading.Mutex(true, "GearsOfAion-one-copy", out first) : null;
        if (!first) { Paths.MoveOldData(); Log.Open(); Log.Info("already running: showing that copy"); ShowRunningCopy(); return; }
        Paths.MoveOldData();
        Log.Open();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log.Crash(ex); };
        Paths.Resolve(args);
        if (player) LastCrash.Check();
        Log.Info($"Gears of Aion 0.9.0  gears={Paths.Gears}  aion={Paths.Aion}");
        _test = TestMode.Detect();
        _autoPlay = _test != null || args.Contains("--autoplay");
        var ovi = Array.IndexOf(args, "--overview");
        if (ovi >= 0 && ovi + 3 < args.Length)
            _overview = (float.Parse(args[ovi + 1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[ovi + 2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[ovi + 3], System.Globalization.CultureInfo.InvariantCulture));
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (Arg("--record-start") is { } rs) _recordStart = float.Parse(rs, inv);
        _recordWhen = Arg("--record-when");
        if (Arg("--fake-input") is { } fi) Inp.Fake(int.Parse(fi));          // dev: a scripted test player
        if (Arg("--quit-after") is { } qa) _quitAfter = float.Parse(qa, inv); // dev: leave after this many seconds
        _noHud = args.Contains("--no-hud"); // dev: clean frames for screenshots and capsule art
        _capsule = args.Contains("--capsule");
        if (Arg("--record-seconds") is { } rsec) _recordSeconds = float.Parse(rsec, inv);
        if (Arg("--wave") is { } wv) _jumpWave = int.Parse(wv);
        if (Arg("--pitch") is { } pt) _forcePitch = float.Parse(pt, inv);
        if (Arg("--level") is { } lv) _jumpLevel = int.Parse(lv);
        var recordTo = Arg("--record");
        var when = Array.IndexOf(args, "--shot-when");
        if (when >= 0 && when + 1 < args.Length) _shotWhen = args[when + 1];
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shot") _shotPath = args[i + 1];
            if (args[i] == "--shot-after") _shotAfter = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            if (args[i] == "--shot-screen") _shotScreen = Enum.Parse<Screen>(args[i + 1], true);
            if (args[i] == "--overlay") UiExtras.Overlay = int.Parse(args[i + 1]); // dev: open the credits (1) or notes (2)
            if (args[i] == "--start-screen") _afterLoading = Enum.Parse<Screen>(args[i + 1], true);
            if (args[i] == "--viewer") { _viewerClip = Enum.Parse<Clip>(args[i + 1], true); _afterLoading = Screen.Viewer; }
            if (args[i] == "--viewer-yaw") _viewerYaw = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            if (args[i] == "--class") _pick = Math.Max(0, Array.FindIndex(Sheets.Classes, c => c.Id == args[i + 1]));
        }
        if (_test != null) _pick = Math.Abs(_test.Seed) % Sheets.Classes.Length;

        var background = args.Contains("--background") || Environment.GetEnvironmentVariable("DAEVAS_BACKGROUND") == "1";
        var flags = ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint;
        if (background) flags = (flags & ~ConfigFlags.VSyncHint) | ConfigFlags.UnfocusedWindow | ConfigFlags.HiddenWindow;
        // dev: DAEVAS_HIDE=1 runs exactly as a player's launch (settings, pad, profile, vsync) but in a hidden window
        if (Environment.GetEnvironmentVariable("DAEVAS_HIDE") == "1") flags |= ConfigFlags.UnfocusedWindow | ConfigFlags.HiddenWindow;
        Raylib.SetConfigFlags(flags);
        Pad.Disabled = background;
        // dev: --size WxH opens the window at that size (to check 4K and ultrawide layouts)
        var size = Array.IndexOf(args, "--size") is var si and >= 0 && si + 1 < args.Length ? args[si + 1].Split('x') : null;
        Raylib.InitWindow(size != null ? int.Parse(size[0]) : 1600, size != null ? int.Parse(size[1]) : 900, "Gears of Aion");
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(144);
        Settings.Load();
        if (_test == null) SheetMods.Load(); // Melty's test runs always play the sheets as shipped
        Profile.Load();
        Lang.Load(args.Contains("--dump-strings"));
        _benchmark = args.Contains("--benchmark");
        _soak = args.Contains("--soak");
        Options.Open = args.Contains("--options"); // dev: open on the options screen
        Options.Muted = background || args.Contains("--mute") || Environment.GetEnvironmentVariable("DAEVAS_MUTE") == "1";
        if (!background) Settings.Apply(Options.Muted);
        if (Arg("--preset") is { } preset)
        {
            // dev: --preset low|medium|high, as the options screen sets it (for benchmarks)
            Settings.S.Preset = preset switch { "low" => 0, "medium" => 1, _ => 2 };
            Settings.S.Shadows = Settings.S.Bloom = Settings.S.Preset > 0;
            City.DetailRadius = Settings.S.Preset switch { 0 => 0.6f, 1 => 0.8f, _ => 1f };
            Particles.CapScale = Settings.S.Preset switch { 0 => 0.4f, 1 => 0.7f, _ => 1f };
        }
        Lit.Load();
        Sky.Load();
        Post.Load();
        Sfx.Load();
        if (background || args.Contains("--mute") || Environment.GetEnvironmentVariable("DAEVAS_MUTE") == "1") Raylib.SetMasterVolume(0);
        _assets = new GameAssets();
        _hud = new Hud(_assets);
        if (Arg("--logo") is { } logoPath)
        {
            // dev: --logo <png> [px] [--logo-plain]: the logo alone, for the app icon and the capsule art
            Logo.Export(logoPath, int.TryParse(Arg("--logo-size"), out var lpx) ? lpx : 1024, !args.Contains("--logo-plain"));
            Raylib.CloseWindow();
            return;
        }
        if (Arg("--splash") is { } splashPath)
        {
            // dev: --splash <png> [--splash-size WxH]: a still of the splash screen
            var ss = (Arg("--splash-size") ?? "1920x1080").Split('x');
            Splash.Export(splashPath, int.Parse(ss[0]), int.Parse(ss[1]));
            Raylib.CloseWindow();
            return;
        }
        _assets.Begin(_autoPlay ? Sheets.Classes[_pick].Character : null);
        if (_test != null && _assets.HasSplash) _test.Pass("aion-splash-loaded");

        if (recordTo != null) _recorder = new Recorder(recordTo, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        var clock = 0f;
        while (!Raylib.WindowShouldClose() && !_quit)
        {
            var dt = _recorder != null ? 1 / 30f : MathF.Min(Raylib.GetFrameTime(), 1 / 20f); // clips step at a steady 30 fps
            Inp.Hold = _screen != Screen.Playing;
            Responsive.Enabled = _screen is Screen.Loading or Screen.Deploying;
            Inp.BeginFrame(ref dt);
            clock += dt;
            if (_quitAfter > 0 && clock > _quitAfter) _quit = true;
            if (Inp.IsKeyPressed(KeyboardKey.F11)) { Settings.S.Fullscreen = !Settings.S.Fullscreen; Settings.Apply(Options.Muted); Settings.Save(); }
            if (Inp.IsKeyPressed(KeyboardKey.F9)) { Options.Muted = !Options.Muted; Settings.Apply(Options.Muted); }
            Pad.Poll();
            if (_shotHold < 0) Update(dt);
            if (_benchmark && _screen == Screen.Playing && _assets.LateSoundsDone)
            {
                // a 30 s orbit over the city, timed by the wall clock (after 3 s to settle)
                if (_benchStart == 0) _benchStart = Raylib.GetTime();
                var elapsed = (float) (Raylib.GetTime() - _benchStart);
                _overview = (elapsed * 12, 45, 70);
                if (elapsed > 3) _benchFrames.Add(Raylib.GetFrameTime());
                if (elapsed > 3 && Raylib.GetFrameTime() > 0.04f) Log.Info($"benchmark: slow frame {Raylib.GetFrameTime() * 1000:0} ms at {elapsed:0.00} s, GC gen2 {GC.CollectionCount(2)}, chunks {_assets.City?.DrawnChunks}");
                if (elapsed > 33)
                {
                    var fps = _benchFrames.Count / (elapsed - 3);
                    var worst = _benchFrames.OrderByDescending(f => f).Take(Math.Max(1, _benchFrames.Count / 100)).Average();
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm}  avg {fps:0} fps  1% low {1 / worst:0} fps  frames {_benchFrames.Count}  preset {Settings.S.Preset}";
                    File.AppendAllText(Path.Combine(Paths.LogDir, "benchmark.txt"), line + Environment.NewLine);
                    Log.Info("benchmark " + line);
                    break;
                }
            }
            // menu music outside a run; the run picks its own
            if (_screen is Screen.Title or Screen.ModeSelect or Screen.ClassSelect or Screen.Deploying) { Audio.Cue("music", "music", "menu", 0.5f); Audio.Cue("ambience", "", "", 0); Audio.Cue("battle", "", "", 0); Audio.Cue("rain", "", "", 0); Audio.Cue("wind", "", "", 0); Audio.Muffle = 0; Audio.Echo = 0; }
            Audio.Update(dt);
            _assets.PumpLateSounds();
            _assets.PumpTextureStream();
            WatchMemory();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(24, 22, 26, 255));
            Draw();
            Ui.Begin();
            Options.Draw(_hud);
            if (Inp.IsKeyPressed(KeyboardKey.F3)) _showPerf = !_showPerf;
            if (Inp.IsKeyPressed(KeyboardKey.F8)) _showErrors = !_showErrors;
            if (_showPerf) DrawPerf();
            if (Inp.IsKeyPressed(KeyboardKey.F1)) _showControls = !_showControls;
            if (_showControls) DrawControls();
            if (_showErrors) DrawErrors();
            Ui.End();
            // shots are read before the buffer swap, so they show this frame
            Rlgl.DrawRenderBatchActive();
            if (Inp.IsKeyPressed(KeyboardKey.F12))
            {
                var file = Path.Combine(Paths.LogDir, $"shot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                SaveShot(file);
                UiExtras.Flash = 1; _hud.Toast = "Screenshot saved: " + file; _hud.ToastTime = 2.5f;
            }
            if (_shotPath != null && _shotHold < 0 && _shotAfter >= 0 && clock >= _shotAfter && _screen == _shotScreen && ShotMoment())
                _shotHold = 20;
            if (_recorder != null && _screen == Screen.Playing && (_recordClock += dt) >= _recordStart && RecordMoment())
            {
                // clip time counts from the start of play, not from loading
                _recorder.Frame();
                if (_recordClock >= _recordStart + _recordSeconds) { _recorder.Dispose(); _recorder = null; Log.Info("clip saved"); break; }
            }
            if (_shotHold >= 0 && _shotHold-- == 0)
            {
                Log.Info($"fps {Raylib.GetFPS()}");
                SaveShot(_shotPath!);
                _shotPath = null;
            }
            Raylib.EndDrawing();
        }
        Log.Launch($"closed after {clock:0}s");
        if (_world != null && _screen == Screen.Playing) Replay.StopRecording(_world); // a run left mid-way is still a replay
        if (Replay.Watching != null) Replay.Close(_world);
        Lang.Save();
        Raylib.CloseWindow();
    }

    /// --record-when <moment>: the clip starts the first time that moment happens (after --record-start).
    static bool RecordMoment()
    {
        if (_recordWhen == null || _recordStarted) return true;
        var keep = _shotWhen;
        _shotWhen = _recordWhen;
        _recordStarted = ShotMoment();
        _shotWhen = keep;
        if (_recordStarted) { _recordStart = _recordClock; Log.Info($"clip starts at {_recordClock:0.0} s ({_recordWhen})"); }
        else if (_recordClock > _recordStart + 240) { _recordStarted = true; _recordStart = _recordClock; Log.Info($"clip: no {_recordWhen} moment in 4 minutes, recording anyway"); }
        return _recordStarted;
    }
    static string? _recordWhen;
    static bool _quit;
    static float _quitAfter = -1;
    static int _replayHold;

    /// Starts watching a recorded run (from --replay or the title screen).
    static void WatchReplay(string? path)
    {
        if (path == null || Replay.Open(path) is not { } h) { _hud.Toast = "No replay to watch yet"; _hud.ToastTime = 3; return; }
        _pick = Math.Max(0, Array.FindIndex(Sheets.Classes, c => c.Id == h.Class));
        _jumpWave = h.JumpWave; _jumpLevel = h.JumpLevel;
        StartGame();
    }
    static bool _noHud, _capsule, _capsuleAimed;
    static bool _recordStarted;

    static bool ShotMoment()
    {
        if (_shotWhen == null) return true;
        if (_world is not { } w) return false;
        bool InView(Enemy e) { var to = Vector3.Normalize(e.Pos - w.P.Pos); return Vector3.Dot(to, w.AimDir() with { Y = 0 }) > 0.85f && Vector3.Distance(e.Pos, w.P.Pos) < 18; }
        return _shotWhen switch
        {
            "reload" => w.Gun.Reload > 0 && w.Gun.Progress > 0.2f,
            "emerge" => w.Enemies.Any(e => e.Emerge > 0.8f && InView(e)),
            "death" => w.Dying.Any(e => e.Dead > 0.35f && InView(e)),
            "chain" => w.P.Chains.Any(c => c is { Time: > 2 }) && w.Locked != null && InView(w.Locked),
            "evade" => w.P.EvadeTime > 0 && w.P.EvadeTime < Sheets.EvasionSeconds * 0.6f,
            "cast" => w.P.Casting is { } c && c.Time > c.Skill.CastTime * 0.5f && w.Enemies.Any(InView),
            "pet" => w.Allies.Count > 0 && w.Enemies.Any(InView) && w.Numbers.Count > 2,
            "ticker" => w.Enemies.Any(e => e.Def.Id == "ticker" && e.Emerge < 0 && InView(e) && Vector3.Distance(e.Pos, w.P.Pos) < 9),
            "transform" => w.Transform > 10 && w.Enemies.Any(InView),
            "boss" => w.Boss is { } boss && w.BossIntro <= 0 && InView(boss) && Vector3.Distance(boss.Pos, w.P.Pos) < 16,
            "telegraph" => w.Telegraphs.Count > 0 && w.Enemies.Any(InView),
            "flight" => w.P.Flying && w.P.Pos.Y > Sheets.FlightHeight - 0.2f && w.Enemies.Count > 0,
            _ => true,
        };
    }

    static void SaveShot(string path)
    {
        var img = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
        Log.Info("screenshot " + path + (_world is { } sw ? $" hero at {sw.P.Pos} eye {sw.CameraEye()} close {sw.CameraClose} box {sw.LastCameraBox.Min} {sw.LastCameraBox.Max}" : ""));
    }

    // ---------------------------------------------------------------- update

    static void Update(float dt)
    {
        if (_test == null && SheetMods.Poll(dt)) { _hud.Toast = SheetMods.Status == "" ? "Sheet mods removed" : SheetMods.Status + " (reloaded)"; _hud.ToastTime = 3; }
        _hud.ToastTime -= dt;
        if (_screen != Screen.Loading && Options.Update()) return;
        if ((Inp.IsKeyPressed(KeyboardKey.O) || Pad.Pressed(GamepadButton.RightFaceUp)) && _screen is Screen.Title or Screen.ClassSelect) { Options.Open = true; Sfx.Play(Sfx.Click); return; }
        switch (_screen)
        {
            case Screen.Loading:
                if (_assets.Poll())
                {
                    _assets.WarmUp();
                    if (_assets.City != null) Abyss.Build(_assets, _assets.Ground, Sheets.CityGroundTileMetres); // (for the title's live island)
                    _hud.BuildGunIcons();
                    _assets.ReleaseLoadingCaches();
                    var all = !Sheets.GunsEnabled || Sheets.Weapons.All(w => _assets.Guns.ContainsKey(w.Id));
                    if (_test != null && Sheets.GunsEnabled) { if (all) _test.Pass("gears-guns-loaded"); else _test.Fail("gears-guns-loaded", string.Join("; ", _assets.Errors)); }
                    var chars = Sheets.Enemies.All(e => _assets.Characters.ContainsKey(e.Character)) && (!_autoPlay || _assets.Characters.ContainsKey(Sheets.Classes[_pick].Character));
                    if (_test != null) { if (chars) _test.Pass("gears-characters-loaded"); else _test.Fail("gears-characters-loaded", string.Join("; ", _assets.Errors)); }
                    var arena = (_assets.City != null || _assets.Props.Count == Sheets.Arena.Length) && _assets.HasGround;
                    if (_test != null) { if (arena) _test.Pass("gears-arena-loaded"); else _test.Fail("gears-arena-loaded", $"{_assets.Props.Count}/{Sheets.Arena.Length} pieces, ground {_assets.HasGround}"); }
                    _screen = _assets.Errors.Count > 0 && _assets.Characters.Count == 0 ? Screen.Loading : _autoPlay ? Screen.Playing : _afterLoading;
                    if (_screen == Screen.ClassSelect && Environment.GetCommandLineArgs().Contains("--deploy")) StartGame();
                    if (_screen == Screen.Playing) StartGame();
                    if (Arg2("--replay") is { } rp) WatchReplay(rp);
                    if (_world != null && Environment.GetCommandLineArgs().Contains("--movetest")) Log.Info("movetest: " + _world.MoveSelfTest());
                }
                break;
            case Screen.Title:
                if (UiExtras.Overlay != 0) { if (Inp.IsKeyPressed(KeyboardKey.Escape) || Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Cancel || Pad.Confirm) UiExtras.Overlay = 0; break; }
                if (Inp.IsKeyPressed(KeyboardKey.C) || Pad.Pressed(GamepadButton.RightFaceLeft)) { UiExtras.Overlay = 1; break; }
                if (Inp.IsKeyPressed(KeyboardKey.N) || Pad.Pressed(GamepadButton.LeftTrigger1)) { UiExtras.Overlay = 2; break; }
                if (LastCrash.File != null)
                {
                    // the last run crashed: say so once; L opens the log folder, anything else carries on
                    if (Inp.IsKeyPressed(KeyboardKey.L)) { LastCrash.OpenFolder(); LastCrash.Seen(); break; }
                    if (Inp.IsKeyPressed(KeyboardKey.S) || Pad.Pressed(GamepadButton.RightFaceUp))
                    {
                        // safe mode: the low preset, no shadows or bloom, then carry on
                        Settings.S.Preset = 0; Settings.S.Shadows = false; Settings.S.Bloom = false;
                        City.DetailRadius = 0.6f; Particles.CapScale = 0.4f;
                        Settings.Save(); LastCrash.Seen(); _hud.Toast = "Safe mode: Low graphics (Options to change)"; _hud.ToastTime = 3;
                        break;
                    }
                    if (Inp.IsKeyPressed(KeyboardKey.Enter) || Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Confirm || Pad.Cancel) { LastCrash.Seen(); break; }
                    break;
                }
                if (Inp.IsKeyPressed(KeyboardKey.R)) { WatchReplay(Replay.Latest()); break; }
                if (Inp.IsKeyPressed(KeyboardKey.Enter) || Inp.IsMouseButtonPressed(MouseButton.Left) || Pad.Confirm) { _screen = Screen.ModeSelect; Sfx.Play(Sfx.Click); }
                break;
            case Screen.ModeSelect:
                UpdateModeSelect();
                break;
            case Screen.ClassSelect:
                UpdateClassSelect();
                break;
            case Screen.Deploying:
                if (_assets.RequestCharacter(Progression.HeroFor(Sheets.Classes[_pick])) && _assets.SwitchCity(SelectedMap)) StartGame();
                break;
            case Screen.Playing:
                UpdatePlaying(dt);
                break;
        }
    }

    static void UpdateClassSelect()
    {
        var n = Sheets.Classes.Length;
        var (pdx, pdy) = Pad.MenuNav();
        if (pdx != 0) { _pick = (_pick + n + pdx) % n; Sfx.Play(Sfx.Click); }
        if (pdy != 0) { _pick = (_pick + 4) % n; Sfx.Play(Sfx.Click); }
        if (Pad.Confirm) { StartGame(); return; }
        if (Pad.Cancel) { _screen = Screen.ModeSelect; return; }
        if (Inp.IsKeyPressed(KeyboardKey.H) || Pad.Pressed(GamepadButton.RightFaceUp))
        {
            var cls = Sheets.Classes[_pick];
            var heroes = Progression.HeroesFor(cls);
            var cur = heroes.IndexOf(Progression.HeroFor(cls));
            Profile.P.HeroLooks[cls.Id] = heroes[(cur + 1) % heroes.Count];
            Profile.Save();
            Sfx.Play(Sfx.Click);
        }
        if (Inp.IsKeyPressed(KeyboardKey.Right) || Inp.IsKeyPressed(KeyboardKey.D)) { _pick = (_pick + 1) % n; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Left) || Inp.IsKeyPressed(KeyboardKey.A)) { _pick = (_pick + n - 1) % n; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Down) || Inp.IsKeyPressed(KeyboardKey.S)) { _pick = (_pick + 4) % n; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Up) || Inp.IsKeyPressed(KeyboardKey.W)) { _pick = (_pick + 4) % n; Sfx.Play(Sfx.Click); }
        var m = Ui.Mouse;
        for (var i = 0; i < n; i++)
            if (Raylib.CheckCollisionPointRec(m, ClassCard(i)))
            {
                if (Inp.GetMouseDelta() != Vector2.Zero) _pick = i;
                if (Inp.IsMouseButtonPressed(MouseButton.Left)) { _pick = i; StartGame(); return; }
            }
        if (Inp.IsKeyPressed(KeyboardKey.Enter)) StartGame();
        if (Inp.IsKeyPressed(KeyboardKey.Escape)) _screen = Screen.ModeSelect;
    }

    static Rectangle ModeCard(int i)
    {
        int sw = Ui.W, sh = Ui.H;
        var h = MathF.Min(70, (sh - 260) / (float) Modes.All.Length);
        return new Rectangle(sw / 2f - 620, 150 + i * h, 560, h - 8);
    }

    static void UpdateModeSelect()
    {
        if (CustomEditor.Open) { if (CustomEditor.Update()) { _mode = GameMode.Custom; _screen = Screen.ClassSelect; } return; }
        var n = Modes.All.Length;
        var (pdx, pdy) = Pad.MenuNav();
        if (Inp.IsKeyPressed(KeyboardKey.Down) || Inp.IsKeyPressed(KeyboardKey.S) || pdy > 0) { _modePick = (_modePick + 1) % n; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Up) || Inp.IsKeyPressed(KeyboardKey.W) || pdy < 0) { _modePick = (_modePick + n - 1) % n; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.H) || Pad.Pressed(GamepadButton.RightFaceLeft)) { _hardcore = !_hardcore; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.M) || Pad.Pressed(GamepadButton.RightFaceUp))
        {
            var open = City.Maps.Where(City.Unlocked).ToList();
            Profile.P.Map = open[(open.FindIndex(m => m.Id == SelectedMap.Id) + 1) % open.Count].Id;
            Profile.Save();
            Sfx.Play(Sfx.Click);
        }
        var m = Ui.Mouse;
        var go = Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Confirm;
        for (var i = 0; i < n; i++)
            if (Raylib.CheckCollisionPointRec(m, ModeCard(i)))
            {
                if (Inp.GetMouseDelta() != Vector2.Zero) _modePick = i;
                if (Inp.IsMouseButtonPressed(MouseButton.Left)) { _modePick = i; go = true; }
            }
        if (go && Modes.All[_modePick].mode == GameMode.Custom) { CustomEditor.Open = true; Sfx.Play(Sfx.Click); return; }
        if (go) { _mode = Modes.All[_modePick].mode; _screen = Screen.ClassSelect; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Cancel) _screen = Screen.Title;
    }

    static void DrawModeSelect()
    {
        int sw = Ui.W, sh = Ui.H;
        Splash.Backdrop(sw, sh, (float) Raylib.GetTime(), new Vector2(sw / 2f, sh * 0.4f), 0.55f);
        _hud.TextCentered("CHOOSE A MODE", sw / 2f, 60, 54, new Color(255, 214, 120, 255));
        for (var i = 0; i < Modes.All.Length; i++)
        {
            var r = ModeCard(i);
            var sel = i == _modePick;
            Raylib.DrawRectangleRounded(r, 0.12f, 6, sel ? new Color(46, 38, 26, 235) : new Color(14, 14, 18, 200));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.12f, 6, sel ? 3 : 1, sel ? new Color(255, 214, 110, 255) : new Color(80, 76, 70, 255));
            _hud.Text(Modes.All[i].name, r.X + 20, r.Y + r.Height / 2 - 14, 28, sel ? Color.White : new Color(215, 208, 195, 255));
        }
        var (mode, name, about) = Modes.All[_modePick];
        var x = sw / 2f - 20; var y = 160f;
        _hud.Text(name, x, y, 36, new Color(255, 214, 110, 255));
        y += 50;
        foreach (var line in Wrap(about, 46)) { _hud.Text(line, x, y, 22, Color.White); y += 30; }
        y += 16;
        var key = mode == GameMode.Daily ? Modes.DailyKey : mode.ToString() + (_hardcore ? "-hardcore" : "");
        _hud.Text(mode == GameMode.Daily ? $"Today's board ({DateTime.Now:d MMM})" : "Your best runs" + (_hardcore ? " (Hardcore)" : ""), x, y, 22, new Color(255, 214, 110, 255));
        y += 32;
        if (Profile.P.Leaderboards.TryGetValue(key, out var board) && board.Count > 0)
            foreach (var (e, rank) in board.Take(6).Select((e, k) => (e, k + 1)))
            {
                _hud.Text($"#{rank}  {e.Score:N0}   {e.Class}, wave {e.Wave}, {e.Seconds / 60}:{e.Seconds % 60:00}   {e.Date}", x, y, 18, new Color(220, 214, 200, 255));
                y += 26;
            }
        else { _hud.Text("No runs yet.", x, y, 18, new Color(170, 165, 160, 255)); y += 26; }
        y += 20;
        _hud.Text($"Hardcore: {(_hardcore ? "ON  - one life, no potions or Salvation, 1.5x loot and score" : "off")}", x, y, 20, _hardcore ? new Color(255, 110, 90, 255) : new Color(190, 185, 175, 255));
        y += 34;
        _hud.Text($"District: {SelectedMap.Name}   ({(Pad.Active ? "Y" : "M")} to change)", x, y, 20, new Color(150, 200, 255, 255));
        y += 26;
        foreach (var m in City.Maps.Where(m => !City.Unlocked(m)))
        {
            var need = Profile.AchievementList.FirstOrDefault(a => a.Id == m.Unlock);
            _hud.Text($"{m.Name}: locked - {need?.About ?? m.Unlock}", x, y, 16, new Color(150, 150, 160, 255)); y += 22;
        }
        if (CustomEditor.Open) { CustomEditor.Draw(_hud); return; }
        _hud.TextCentered(Pad.Active ? "D-pad: choose   A: next   X: Hardcore   B: back" : "Up/Down or mouse: choose   Enter or click: next   H: Hardcore   Esc: back", sw / 2f, sh - 50, 20, new Color(200, 190, 170, 255));
    }

    static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line != "") { yield return line; line = ""; }
            line = line == "" ? word : line + " " + word;
        }
        if (line != "") yield return line;
    }

    static bool _paused, _sprintToggle, _photo, _showPerf, _showErrors, _benchmark, _showControls, _soak;
    static float _soakTimer;

    /// F1: every control on one card (keyboard bindings as set, and the gamepad layout).
    static void DrawControls()
    {
        int sw = Ui.W, sh = Ui.H;
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(6, 6, 10, 225));
        _hud.TextCentered("CONTROLS", sw / 2f, 40, 44, new Color(255, 214, 110, 255));
        var half = (Keys.Actions.Length + 1) / 2;
        for (var i = 0; i < Keys.Actions.Length; i++)
        {
            var (id, label, _) = Keys.Actions[i];
            var x = sw / 2f - 560 + (i / half) * 560; var y = 100 + (i % half) * 26f;
            _hud.Text(label, x, y, 18, Color.White);
            _hud.Text(Keys.Name(Keys.Key(id)), x + 330, y, 18, new Color(255, 225, 150, 255));
        }
        var pad = new[]
        {
            "Gamepad: left stick move, right stick look, RT attack, A sprint, B evade, Y fly, X execute",
            "D-pad skills 1-4, LT + D-pad skills 5-8, RS lock (between waves: sandbags), LB/RB potions",
            "LT + RS ultimate, LT + A dive, LT + Y stance, LT + B command spirits, LT + X Stigma, View pause",
            "Moving into low cover, evade vaults it or climbs onto it (crates, ledges); stand still against cover to take less from that side",
            "Mouse: left click attack, wheel zoom, F1 this card, F3 performance, F8 problems, F9 mute, F10 photo, F11 fullscreen, F12 screenshot",
        };
        for (var i = 0; i < pad.Length; i++) _hud.TextCentered(pad[i], sw / 2f, 110 + half * 26 + 20 + i * 26, 17, new Color(200, 210, 230, 255));
        _hud.TextCentered("Change keys in Options > Key bindings.   F1 to close", sw / 2f, sh - 40, 18, new Color(200, 200, 210, 255));
    }
    static double _benchStart;
    static readonly List<float> _benchFrames = new();
    static int _previewElement;

    /// F3: frame time, what's on screen and memory (and a warning when memory runs high).
    /// Pause > F: a feedback note with this run's details, opened next to the logs so it can be sent with them.
    static void WriteFeedback(World w)
    {
        try
        {
            var file = Path.Combine(Paths.LogDir, $"feedback-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllLines(file, new[]
            {
                "Gears of Aion feedback", "", "What happened / what you'd like:", "", "", "---",
                $"class {w.P.Cls.Name}, mode {w.Mode}, {w.WaveLabel}, level {w.P.Level}, district {City.Current.Name}",
                $"settings: preset {Settings.S.Preset}, difficulty {Settings.S.Difficulty}", "log: latest.log in this folder",
            });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", file) { UseShellExecute = true });
            _hud.Toast = "Feedback note opened - it's saved in the logs folder"; _hud.ToastTime = 3;
        }
        catch (Exception e) { Log.Info("feedback note failed: " + e.Message); }
    }

    static void DrawPerf()
    {
        var mem = GC.GetTotalMemory(false) / 1048576;
        var ws = Environment.WorkingSet / 1048576;
        var w = _world;
        var lines = new[]
        {
            $"{Raylib.GetFPS()} fps   {Raylib.GetFrameTime() * 1000:0.0} ms",
            $"city chunks drawn {_assets.City?.DrawnChunks ?? 0}/{_assets.City?.Chunks.Count ?? 0}   glows {Glow.LastFlushCount}",
            w == null ? "" : $"Locust {w.Enemies.Count}   effects {w.Effects.Count}   particles {w.Bits.Count}   telegraphs {w.Telegraphs.Count}",
            $"memory: managed {mem} MB, process {ws} MB" + (ws > 6000 ? "   HIGH: try the Low graphics preset" : ""),
        };
        Raylib.DrawRectangle(8, 8, 520, lines.Length * 20 + 10, new Color(0, 0, 0, 190));
        for (var i = 0; i < lines.Length; i++) _hud.Text(lines[i], 16, 12 + i * 20, 16, ws > 6000 && i == 3 ? new Color(255, 120, 100, 255) : new Color(170, 255, 170, 255));
    }

    /// F8: recent problems from the log (files that didn't read, animations that failed).
    static void DrawErrors()
    {
        int sw = Ui.W, sh = Ui.H;
        string[] lines;
        lock (Log.Problems) lines = Log.Problems.TakeLast(24).ToArray();
        Raylib.DrawRectangle(40, 80, sw - 80, sh - 160, new Color(10, 6, 6, 220));
        _hud.Text($"PROBLEMS ({lines.Length})   F8 to close   full log: {Paths.LogDir}", 56, 92, 18, new Color(255, 160, 140, 255));
        for (var i = 0; i < lines.Length; i++) _hud.Text(lines[i].Length > 160 ? lines[i][..160] : lines[i], 56, 124 + i * 22, 15, new Color(240, 220, 210, 255));
        if (lines.Length == 0) _hud.Text("Nothing has gone wrong.", 56, 124, 16, Color.White);
    }
    static Vector3 _photoEye;
    static float _photoYaw, _photoPitch;
    public static int _mouseSkill;
    static Vector3 _camEye;
    static float _deathOrbit;
    static int _stigmaPick, _bookPick = -1;
    static CityMap SelectedMap => (Replay.Watching is { } rw ? City.Maps.FirstOrDefault(m => m.Id == rw.Map) : null)
        ?? City.Maps.FirstOrDefault(m => m.Id == Profile.P.Map && City.Unlocked(m)) ?? City.Maps[0];
    static GameMode _mode = GameMode.Campaign;
    static bool _hardcore;
    static int _modePick;

    static string? Arg2(string name) { var a = Environment.GetCommandLineArgs(); var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

    static void StartGame()
    {
        _paused = false;
        var devRun = Environment.GetCommandLineArgs().Any(a => a is "--background" or "--no-hud" or "--shot" or "--record");
        if (Profile.P.Records.Values.Sum(r => r.Runs) == 0 && !_autoPlay && Replay.Watching == null && !devRun) _showControls = true; // the first run shows the controls (F1 hides them)
        var cls = Sheets.Classes[_pick];
        // the class's Gears hero is read from the install the first time it deploys
        var heroId = Progression.HeroFor(cls);
        if (!_assets.RequestCharacter(heroId) || (!_autoPlay && !_assets.SwitchCity(SelectedMap))) { _screen = Screen.Deploying; return; }
        var seed = Replay.Watching?.Seed ?? _test?.Seed ?? Environment.TickCount;
        _world = new World(cls, _assets, seed);
        _world.HeroId = heroId;
        _world.Milestone += m => { _test?.Pass(m); Log.Info("milestone " + m); };
        var modeArg = Array.IndexOf(Environment.GetCommandLineArgs(), "--mode");
        var mode = Replay.Watching is { } rw ? Enum.Parse<GameMode>(rw.Mode) : modeArg > 0 ? Enum.Parse<GameMode>(Environment.GetCommandLineArgs()[modeArg + 1], true) : _autoPlay ? GameMode.Campaign : _mode;
        var hardcore = Replay.Watching?.Hardcore ?? (_hardcore && !_autoPlay);
        _world.SetMode(mode, hardcore);
        _world.AutoStigma = _autoPlay;
        if (_jumpWave > 0 || _jumpLevel > 0) _world.Jump(Math.Max(1, _jumpWave), Math.Max(1, _jumpLevel));
        _screen = Screen.Playing;
        // every played run is recorded (a replay is the seed, the start and each frame's input)
        if (!_autoPlay && _test == null && Replay.Watching == null)
            Replay.StartRecording(new Replay.Header
            {
                Seed = seed, Class = cls.Id, Hero = heroId, Mode = mode.ToString(), Hardcore = hardcore, Map = SelectedMap.Id,
                JumpWave = _jumpWave, JumpLevel = _jumpLevel, Settings = Settings.Snapshot(), Profile = Profile.Snapshot(), Started = DateTime.Now.ToString("s"),
            });
        _testClock = 0;
        if (!_autoPlay) Raylib.DisableCursor();
        Log.Info($"start as {cls.Name}");
        if (_autoPlay) _world.SkipBreather();
        var argv = Environment.GetCommandLineArgs();
        if (argv.Contains("--dev-loot"))
        {
            // dev (shots): a bag of mixed gear and materials
            var rng = new Random(5);
            Profile.P.Kinah = 4200;
            for (var i = 0; i < 14; i++) Profile.P.Bag.Add(Profile.MakeItem(rng, cls.Id, 6, (Rarity) (i % 6)));
            foreach (var m in new[] { "chitin", "imulsion", "enchant", "hp-potion", "mp-potion", "serum" }) Profile.AddMat(m, 7);
            Profile.Equip(cls.Id, Profile.P.Bag.First(i => i.Slot != "weapon" || i.Class == cls.Id));
            Profile.P.Achievements.AddRange(new[] { "first-kill", "kills-100", "wave-3" });
            _world.ApplyGear();
        }
        if (argv.Contains("--bag")) BagScreen.Open = true;
        if (Array.IndexOf(argv, "--bag-tab") is var bt and > 0) BagScreen.OpenAt(int.Parse(argv[bt + 1]));
    }

    static void UpdatePlaying(float dt)
    {
        var w = _world!;
        if (w.Won || w.Lost) Replay.RunEnded(w);
        if (Replay.Watching != null && (Inp.PlaybackEnded || ((w.Won || w.Lost) && _replayHold++ > 120)))
        {
            var verdict = Replay.Close(w);
            _hud.Toast = "Replay over: " + verdict; _hud.ToastTime = 4;
            _replayHold = 0;
            if (Environment.GetCommandLineArgs().Contains("--replay")) { _quit = true; return; }
            _screen = Screen.Title; Raylib.EnableCursor();
            return;
        }
        if (_capsule)
        {
            // dev: dusk, wings out, facing away from the longest open street
            Env.Set("Clear", 18.3f); Env.Hour = 18.3f;
            w.P.Flying = true; w.P.FlightTime = Sheets.FlightSeconds;
            if (!_capsuleAimed) { w.P.Yaw = w.OpenestYaw() + MathF.PI; _capsuleAimed = true; }
        }
        if ((w.Won || w.Lost) && _soak && (_soakTimer += dt) > 3)
        {
            _soakTimer = 0;
            _pick = (_pick + 1) % Sheets.Classes.Length;
            Log.Info($"soak: next run as {Sheets.Classes[_pick].Name}, managed memory {GC.GetTotalMemory(false) / 1048576} MB, process {Environment.WorkingSet / 1048576} MB");
            StartGame();
            return;
        }
        if (w.Won || w.Lost)
        {
            // end screen: retry the same class, or choose again
            if (Inp.IsKeyPressed(KeyboardKey.R) || Pad.Pressed(GamepadButton.RightFaceLeft)) { StartGame(); return; }
            if (Inp.IsKeyPressed(KeyboardKey.Enter) || Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Confirm || Pad.Back) { Raylib.EnableCursor(); _screen = Screen.ClassSelect; return; }
        }
        if (Keys.Pressed("photo") && !w.Won && !w.Lost)
        {
            _photo = !_photo;
            if (_photo) { _photoEye = w.CameraEye(); _photoYaw = w.P.Yaw; _photoPitch = w.P.Pitch; }
        }
        if (_photo)
        {
            // photo mode: the world holds still and the camera flies free
            var pmd = Inp.GetMouseDelta();
            _photoYaw -= pmd.X * 0.003f; _photoPitch = Math.Clamp(_photoPitch - pmd.Y * 0.003f, -1.4f, 1.4f);
            var f = new Vector3(MathF.Sin(_photoYaw), 0, MathF.Cos(_photoYaw));
            var r = new Vector3(-f.Z, 0, f.X);
            var sp = 8 * Raylib.GetFrameTime();
            if (Keys.Down("forward")) _photoEye += f * sp;
            if (Keys.Down("back")) _photoEye -= f * sp;
            if (Keys.Down("left")) _photoEye += r * sp;
            if (Keys.Down("right")) _photoEye -= r * sp;
            if (Inp.IsKeyDown(KeyboardKey.Space)) _photoEye.Y += sp;
            if (Inp.IsKeyDown(KeyboardKey.LeftControl)) _photoEye.Y = MathF.Max(0.3f, _photoEye.Y - sp);
            if (Inp.IsKeyPressed(KeyboardKey.Escape)) _photo = false;
            return;
        }
        if (Keys.Pressed("map")) UiExtras.BigMap = !UiExtras.BigMap;
        if (!_autoPlay) UiExtras.CheckHints(w);
        if (w.StigmaOpen)
        {
            // choosing a Stigma: Left/Right or 1-3, Enter or click; Esc closes
            var (pdx, _) = Pad.MenuNav();
            if (Inp.IsKeyPressed(KeyboardKey.Right) || pdx > 0) _stigmaPick = (_stigmaPick + 1) % 3;
            if (Inp.IsKeyPressed(KeyboardKey.Left) || pdx < 0) _stigmaPick = (_stigmaPick + 2) % 3;
            for (var i = 0; i < 3; i++) if (Inp.IsKeyPressed(KeyboardKey.One + i)) { _stigmaPick = i; }
            var mouse = Ui.Mouse;
            var clicked = -1;
            for (var i = 0; i < 3; i++) if (Raylib.CheckCollisionPointRec(mouse, Hud.StigmaCard(i)) && Inp.IsMouseButtonPressed(MouseButton.Left)) clicked = i;
            if (clicked >= 0) _stigmaPick = clicked;
            if (clicked >= 0 || Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Confirm) { w.PickStigma(_stigmaPick); if (!_autoPlay && !_paused) Raylib.DisableCursor(); }
            else if (Inp.IsKeyPressed(KeyboardKey.Escape) || Pad.Cancel) { w.StigmaOpen = false; if (!_autoPlay && !_paused) Raylib.DisableCursor(); }
            return;
        }
        if (BagScreen.Open)
        {
            BagScreen.Update(w);
            if (!BagScreen.Open && !_autoPlay && !_paused) Raylib.DisableCursor();
            return;
        }
        if (!w.Won && !w.Lost && !_paused && Keys.Pressed("bag")) { BagScreen.Open = true; Raylib.EnableCursor(); Sfx.Play(Sfx.Click); return; }
        if (!w.Won && !w.Lost && (Inp.IsKeyPressed(KeyboardKey.Escape) || Inp.IsKeyPressed(KeyboardKey.P) || Pad.Back))
        {
            _paused = !_paused;
            if (_paused) Raylib.EnableCursor(); else if (!_autoPlay) Raylib.DisableCursor();
            return;
        }
        if (_paused)
        {
            var bookTop = Ui.H * 0.08f + 160;
            for (var i = 0; i < 8; i++)
                if (Inp.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Ui.Mouse, Hud.SkillBookRect(i, bookTop)))
                {
                    if (_bookPick < 0) _bookPick = i;
                    else { if (_bookPick != i) w.SwapSlots(_bookPick, i); _bookPick = -1; }
                    Sfx.Play(Sfx.Click);
                }
            if (Inp.IsKeyPressed(KeyboardKey.O) || Pad.Pressed(GamepadButton.RightFaceUp)) { Options.Open = true; Sfx.Play(Sfx.Click); }
            else if (Inp.IsKeyPressed(KeyboardKey.I) || Pad.Pressed(GamepadButton.LeftTrigger1)) { BagScreen.Open = true; Sfx.Play(Sfx.Click); }
            else if (Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Confirm) { _paused = false; if (!_autoPlay) Raylib.DisableCursor(); }
            else if (Inp.IsKeyPressed(KeyboardKey.R) || Pad.Pressed(GamepadButton.RightFaceLeft)) { _paused = false; StartGame(); }
            else if (Inp.IsKeyPressed(KeyboardKey.C) || Pad.Pressed(GamepadButton.RightFaceRight)) { _paused = false; _screen = Screen.ClassSelect; }
            else if (Inp.IsKeyPressed(KeyboardKey.F)) WriteFeedback(w);
            return;
        }
        var md = Inp.GetMouseDelta();
        var sens = Sheets.MovementMouseSensitivity * Settings.S.MouseSensitivity;
        var inv = Settings.S.InvertLook ? -1 : 1;
        w.P.Yaw -= md.X * sens;
        w.P.Pitch = Math.Clamp(w.P.Pitch - md.Y * sens * inv, -1.1f, 0.9f);
        var zoom = Sheets.GunsEnabled || Settings.S.MouseOnly ? 0 : Inp.GetMouseWheelMove(); // with the guns off the wheel zooms the camera
        if (zoom != 0) { Settings.S.CameraDistance = Math.Clamp(Settings.S.CameraDistance - zoom * 0.1f, 0.6f, 1.8f); Settings.Save(); }
        var move = new Vector2((Keys.Down("right") ? 1 : 0) - (Keys.Down("left") ? 1 : 0),
                               (Keys.Down("forward") ? 1 : 0) - (Keys.Down("back") ? 1 : 0));
        var look = Pad.Look;
        // the look curve: smooth and sharp make small stick moves finer
        float Curve(float v) => Settings.S.LookCurve switch { 1 => MathF.Sign(v) * v * v, 2 => MathF.Sign(v) * MathF.Abs(v * v * v), _ => v };
        look = new Vector2(Curve(look.X), Curve(look.Y));
        if (Pad.Active && Settings.S.AimAssist && look.LengthSquared() < 0.5f && w.Locked == null)
        {
            // aim assist: drift onto a Locust close to the crosshair
            var aim = w.AimDir();
            var best = w.Enemies.Where(e => e.Hp > 0 && w.PlayerSees(e)).Select(e => (e, d: Vector3.Normalize(e.Centre - w.CameraEye()))).Where(x => Vector3.Dot(x.d, aim) > 0.985f).OrderByDescending(x => Vector3.Dot(x.d, aim)).FirstOrDefault();
            if (best.e != null)
            {
                var want = MathF.Atan2(best.d.X, best.d.Z);
                w.P.Yaw += WrapAngle(want - w.P.Yaw) * MathF.Min(1, dt * 3);
            }
        }
        if (Settings.S.LockOnCamera && w.Locked is { Hp: > 0 } lk)
        {
            var to = lk.Pos - w.P.Pos;
            w.P.Yaw += WrapAngle(MathF.Atan2(to.X, to.Z) - w.P.Yaw) * MathF.Min(1, dt * 4);
        }
        w.P.Yaw -= look.X * 3.2f * dt * Settings.S.PadLookSpeed;
        w.P.Pitch = Math.Clamp(w.P.Pitch - look.Y * 2.2f * dt * Settings.S.PadLookSpeed * inv, -1.1f, 0.9f);
        var pm = Pad.Move;
        if (pm.LengthSquared() > 0) move = pm;
        if (Settings.S.MouseOnly)
        {
            // right mouse walks forward, the wheel picks a skill, middle click casts it, the side buttons evade and fly
            if (Inp.IsMouseButtonDown(MouseButton.Right)) move.Y = 1;
            var wheel = Inp.GetMouseWheelMove();
            if (wheel != 0) _mouseSkill = (_mouseSkill + (wheel < 0 ? 1 : 7)) % 8;
            if (Inp.IsMouseButtonPressed(MouseButton.Middle)) Cast(w, _mouseSkill);
            if (Inp.IsMouseButtonPressed(MouseButton.Side)) w.Evade(move);
            if (Inp.IsMouseButtonPressed(MouseButton.Extra)) w.ToggleFlight();
        }
        if (!Settings.S.HoldToSprint && (Keys.Pressed("sprint") || Pad.Pressed(GamepadButton.LeftThumb))) _sprintToggle = !_sprintToggle;
        var sprint = Settings.S.HoldToSprint ? Keys.Down("sprint") || Pad.Sprint : _sprintToggle;
        var padFire = w.Gun.Def.Automatic ? Pad.FireDown : Pad.FirePressed();

        if (!w.Lost && !w.Won)
        {
            if (!Sheets.GunsEnabled) { if (Pad.FireDown || Inp.IsMouseButtonDown(MouseButton.Left)) w.BasicAttack(); }
            else if (padFire || (w.Gun.Def.Automatic ? Inp.IsMouseButtonDown(MouseButton.Left) : Inp.IsMouseButtonPressed(MouseButton.Left))) w.Fire();
            if (Sheets.GunsEnabled && (Inp.IsKeyPressed(KeyboardKey.R) || Pad.Reload)) w.ReloadPressed();
            if (Keys.Pressed("evade") || Pad.Evade) w.Evade(move);
            if (Keys.Pressed("lock") || Pad.Lock) w.CycleLock();
            if (Keys.Pressed("flight") || Pad.Flight) w.ToggleFlight();
            if (Keys.Pressed("execute") || (!Sheets.GunsEnabled && Pad.Execute))
            {
                if (w.NearQuartermaster && w.ExecuteTarget == null) { BagScreen.OpenAt(4); Raylib.EnableCursor(); return; }
                if (w.NearStation && w.ExecuteTarget == null) { BagScreen.OpenAt(3); Raylib.EnableCursor(); return; }
                w.ExecuteDowned();
            }
            if (Keys.Pressed("log")) w.ShowLog = !w.ShowLog;
            if (w.Mode == GameMode.Practice && Inp.IsKeyPressed(KeyboardKey.F7))
            {
                // effect previewer: each press shows the next element's impact on the targets
                var els = new[] { "physical", "fire", "ice", "lightning", "holy", "shadow", "wind", "earth", "spirit", "poison", "arcane" };
                var el = els[_previewElement++ % els.Length];
                w.PreviewElement(el);
                _hud.Toast = "Effect preview: " + el; _hud.ToastTime = 1.5f;
            }
            if ((!Sheets.GunsEnabled && Keys.Pressed("ultimate")) || Pad.Ultimate) { var ult = w.UseUltimate(); if (ult != "") { _hud.Toast = ult; _hud.ToastTime = 1.2f; } }
            if ((Keys.Pressed("stigma") || Pad.StigmaKey) && w.StigmaPoints > 0) { w.StigmaOpen = true; Raylib.EnableCursor(); }
            if ((!Sheets.GunsEnabled && Keys.Pressed("dive")) || Pad.Dive) { var dv = w.Dive(); if (dv != "") { _hud.Toast = dv; _hud.ToastTime = 1.2f; } }
            if (Keys.Pressed("stance") || Pad.Stance) { _hud.Toast = w.ToggleStance(); _hud.ToastTime = 1.4f; }
            if (Keys.Pressed("command") || Pad.Command) { _hud.Toast = w.CommandPets(); _hud.ToastTime = 1.4f; }
            if (Keys.Pressed("sandbags") || (!w.WaveActive && !w.Won && Pad.Lock)) { _hud.Toast = w.Fortify(); _hud.ToastTime = 1.6f; }
            w.AutoAttack();
            // potions: Z life, X mana, V wind serum (pad: LB, RB, LT+LB)
            var padAlt = Pad.Available && Inp.GetGamepadAxisMovement(0, GamepadAxis.LeftTrigger) > 0.3f;
            string? potion = Keys.Pressed("hp") ? "hp-potion" : Keys.Pressed("mp") ? "mp-potion" : Keys.Pressed("serum") ? "serum" : null;
            if (!Sheets.GunsEnabled && Pad.Pressed(GamepadButton.LeftTrigger1)) potion = padAlt ? "serum" : "hp-potion";
            if (!Sheets.GunsEnabled && Pad.Pressed(GamepadButton.RightTrigger1)) potion = "mp-potion";
            if (potion != null && w.UsePotion(potion) is { Length: > 0 } why) { _hud.Toast = why; _hud.ToastTime = 1.5f; }
            if (Sheets.GunsEnabled && (Inp.IsKeyPressed(KeyboardKey.Q) || Pad.NextGun)) w.SwitchGun((w.P.Gun + 1) % w.P.Guns.Length);
            if (Sheets.GunsEnabled && Pad.PrevGun) w.SwitchGun((w.P.Gun + w.P.Guns.Length - 1) % w.P.Guns.Length);
            var wheel = Inp.GetMouseWheelMove();
            if (Sheets.GunsEnabled && wheel != 0) w.SwitchGun((w.P.Gun + (wheel < 0 ? 1 : w.P.Guns.Length - 1)) % w.P.Guns.Length);
            for (var i = 0; i < w.P.Guns.Length; i++)
                if (Enum.TryParse<KeyboardKey>(w.P.Guns[i].Def.Key, out var k) && Inp.IsKeyPressed(k)) w.SwitchGun(i);
            for (var i = 0; i < 8; i++)
                if (Keys.Pressed($"skill{i + 1}") || Pad.Skill(i)) Cast(w, i);
            if (Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Start) w.SkipBreather();
        }
        if (_autoPlay) AutoPlay(w, dt, ref move);
        // a boss's defeat plays out in slow motion
        if (w.SlowMo > 0) { w.SlowMo -= dt; dt *= 0.35f; }
        dt *= Settings.S.GameSpeed / 100f;
        w.Update(dt, move, sprint);
    }

    static void Cast(World w, int slot)
    {
        var why = w.TryCast(slot);
        if (why != "") { _hud.Toast = $"{w.ActiveSkill(slot).Name}: {why}"; _hud.ToastTime = 1.2f; }
    }

    /// Test runs (and --autoplay) play themselves: face the nearest enemy, shoot, use every ready skill.
    static void AutoPlay(World w, float dt, ref Vector2 move)
    {
        _testClock += dt;
        if (w.Won || w.Lost) return;
        // the nearest enemy in sight (enemies path to the player, so wait for the rest)
        var target = w.Enemies.Where(e => w.PlayerSees(e)).OrderBy(e => Vector3.Distance(e.Pos, w.P.Pos)).FirstOrDefault();
        _autoBlind = target == null ? _autoBlind + dt : 0;
        if (target == null && _autoBlind > 6) target = w.Enemies.OrderBy(e => Vector3.Distance(e.Pos, w.P.Pos)).FirstOrDefault(); // go hunting
        if (target == null && w.Enemies.Count > 0) { move = Vector2.Zero; return; }
        if (target == null) { w.SkipBreather(); return; }
        var eye = w.CameraEye();
        var to = target.Centre - eye;
        var wantYaw = MathF.Atan2(to.X, to.Z);
        var wantPitch = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1, 1));
        w.P.Yaw += Math.Clamp(WrapAngle(wantYaw - w.P.Yaw), -6 * dt, 6 * dt);
        w.P.Pitch += Math.Clamp(wantPitch - w.P.Pitch, -4 * dt, 4 * dt);
        if (_forcePitch is { } fp) w.P.Pitch = fp;
        var dist = Vector3.Distance(target.Pos, w.P.Pos);
        // melee classes close in so their swings (and chains) land; the rest keep their distance
        var chainRoot = Enumerable.Range(0, 4).Select(w.Skill).FirstOrDefault(k => Sheets.Skills.Any(c => c.ChainFrom == k.Id)) ?? w.Skill(0);
        var melee = chainRoot.Kind is "strike" or "stun" or "dash" || (!Sheets.GunsEnabled && w.P.Cls.BasicKind == "melee");
        var (near, far) = melee ? (1.8f, 2.6f) : (5f, 12f);
        move = new Vector2(MathF.Sin(_testClock * 0.8f) * (melee ? 0.2f : 0.6f), dist > far ? 1 : dist < near ? -0.7f : 0);
        if (dist > far && !w.PlayerSees(target))
        {
            // out of sight: walk round the buildings to it
            var d = w.HeroPathDir(target.Pos);
            move = new Vector2(Vector3.Dot(d, w.P.Right), Vector3.Dot(d, w.P.Forward));
        }
        if (w.P.Casting != null) move = Vector2.Zero; // stand still so casts finish
        if (w.KryllNear && w.P.Hp < w.P.MaxHp * 0.5f && w.NearestLight() is { } light && Vector3.Distance(light with { Y = 0 }, w.P.Pos with { Y = 0 }) > 1.5f)
        {
            // at night, fight from the light: head for the nearest lamp or fire, round the buildings (still facing the target)
            var d = w.HeroPathDir(light with { Y = 0 });
            move = new Vector2(Vector3.Dot(d, w.P.Right), Vector3.Dot(d, w.P.Forward));
        }
        if (w.Ultimate >= 100 && target != null && Vector3.Distance(target.Pos, w.P.Pos) < 7) w.UseUltimate();
        if (MathF.Abs(WrapAngle(wantYaw - w.P.Yaw)) < 0.12f && (Sheets.GunsEnabled || w.P.Cls.BasicKind != "melee" || dist < w.P.Cls.BasicRange + 0.8f)) w.Fire();
        if ((_autoTimer -= dt) <= 0)
        {
            _autoTimer = 0.6f;
            _autoSlot++;
            for (var k = 0; k < 8; k++)
            {
                var i = (_autoSlot + k) % 8; // take turns so every skill gets used, not just the first chain
                var s = w.ActiveSkill(i);
                if (!w.Unlocked(s) || w.P.Mp < s.MpCost || (w.P.Cooldowns.TryGetValue(s.Id, out var cd) && cd > 0)) continue;
                if (s.Kind is "strike" or "stun" && dist > s.Range + target.Radius) continue;
                if (s.Kind is "nova" or "dash" && dist > s.Range + s.Radius + 2) continue;
                if (w.TryCast(i) == "") break;
            }
        }
        if (w.Gun.Ammo == 0) w.Reload();
        if (w.P.Hp < w.P.MaxHp * 0.4f) w.UsePotion("hp-potion"); // as a player would
        // AION target lock on the nearest enemy, and an evasion roll away from shots and swings
        if (w.Locked == null) w.CycleLock();
        var danger = w.Shots.Any(s => Vector3.Distance(s.Pos, w.P.Pos) < 5 && Vector3.Dot(s.Vel, w.P.Pos - s.Pos) > 0)
                     || w.Enemies.Any(e => !e.Def.Ranged && e.Emerge < 0 && Vector3.Distance(e.Pos, w.P.Pos) < e.Def.AttackRange + 0.5f && e.AttackCd < 0.25f)
                     || (_testClock % 8 < dt && w.Enemies.Any(e => Vector3.Distance(e.Pos, w.P.Pos) < 10));
        if (danger) w.Evade(new Vector2(MathF.Sin(_testClock) > 0 ? 1 : -1, -0.3f));
        // AION flight: take off now and then, land after a few seconds
        if (!w.P.Flying && w.P.FlightTime >= Sheets.FlightSeconds - 0.01f && _testClock % 20 < dt) w.ToggleFlight();
        else if (w.P.Flying && w.P.FlightTime < Sheets.FlightSeconds - 6) w.ToggleFlight();
        // Active Reload: aim for the middle of the perfect zone (test runs need one perfect reload)
        if (w.Gun.Reload > 0 && !w.Gun.Tried && w.Gun.Progress >= Sheets.ActiveReloadZoneStart + Sheets.ActiveReloadPerfectWidth * 0.5f) w.ReloadPressed();
    }

    static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    // ---------------------------------------------------------------- draw

    /// A slow orbit round the island: the sky, the city, the Abyss and its motes.
    static void DrawTitleScene()
    {
        var t = (float) Raylib.GetTime() * 0.035f;
        var eye = new Vector3(MathF.Cos(t) * 230, 75 + 15 * MathF.Sin(t * 0.7f), MathF.Sin(t) * 230);
        var cam = new Camera3D(eye, new Vector3(0, -10, 0), Vector3.UnitY, 50, CameraProjection.Perspective);
        Sky.Draw(cam);
        Raylib.BeginMode3D(cam);
        Lit.SetEye(cam.Position);
        Lit.SetEnv();
        Lit.ClearLights(); Lit.SendLights();
        Glow.Begin(cam);
        Abyss.Update(Raylib.GetFrameTime(), Vector3.Zero);
        _assets.City!.Draw(cam.Position, Vector3.Normalize(cam.Target - cam.Position));
        Abyss.Draw();
        Abyss.DrawGlows();
        Glow.Flush();
        Raylib.EndMode3D();
    }

    static void Draw()
    {
        // the 3D screens start virtual 2D themselves, after their world-anchored labels
        var menu = _screen is not (Screen.Playing or Screen.Viewer);
        // the title: the district's island drifting in the Abyss, live in 3D, behind the logo
        Splash.SceneBehind = _screen == Screen.Title && _assets.City != null && Abyss.Ready && UiExtras.Overlay == 0;
        if (Splash.SceneBehind) DrawTitleScene();
        if (menu) Ui.Begin();
        DrawScreen();
        if (menu) Ui.End();
    }

    static void DrawScreen()
    {
        int sw = Ui.W, sh = Ui.H;
        switch (_screen)
        {
            case Screen.Loading:
                Splash.Draw(sw, sh, (float) Raylib.GetTime());
                if (_assets.Errors.Count > 0 && _assets.Characters.Count == 0)
                    for (var i = 0; i < _assets.Errors.Count; i++) _hud.TextCentered(_assets.Errors[i], sw / 2f, sh * 0.66f + i * 30, 22, new Color(255, 120, 100, 255));
                else
                {
                    _hud.TextCentered(_assets.Status + new string('.', (int) (Raylib.GetTime() * 3) % 4), sw / 2f, sh * 0.72f, 22, new Color(235, 230, 220, 230));
                    _hud.TextCentered("Tip: " + UiExtras.Tip, sw / 2f, sh * 0.72f + 40, 18, new Color(255, 214, 110, 200));
                }
                break;
            case Screen.Title:
                // the Gears of Aion scene
                Splash.Draw(sw, sh, (float) Raylib.GetTime());
                _hud.TextCentered(Pad.Available ? "Press A or Enter    (Y: options)" : "Press Enter    (O: options)", sw / 2f, sh * 0.72f, 30, World.Fade(Color.White, 0.6f + 0.4f * MathF.Sin((float) Raylib.GetTime() * 3)));
                _hud.TextCentered(Pad.Available ? "X: credits   LB: what's new" : "C: credits   N: what's new   R: watch your last run", sw / 2f, sh * 0.72f + 44, 20, new Color(210, 205, 195, 200));
                UiExtras.DrawOverlay(_hud);
                if (LastCrash.File != null)
                {
                    Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
                    var bw = Math.Min(sw - 80, 900); var bx = (sw - bw) / 2; var by = sh / 2 - 110;
                    Raylib.DrawRectangle(bx, by, bw, 220, new Color(26, 20, 30, 240));
                    Raylib.DrawRectangleLines(bx, by, bw, 220, new Color(255, 214, 120, 255));
                    _hud.TextCentered("Gears of Aion closed unexpectedly last time", sw / 2f, by + 26, 30, new Color(255, 214, 120, 255));
                    _hud.TextCentered("Sorry about that. A report was saved in the log folder.", sw / 2f, by + 78, 22, Color.White);
                    _hud.TextCentered("If you send it along (with latest.log), the cause can be found.", sw / 2f, by + 108, 20, new Color(210, 205, 195, 255));
                    _hud.TextCentered(Pad.Available ? "L: log folder     Y: safe mode (low graphics)     A: carry on" : "L: log folder     S: safe mode (low graphics)     Enter: carry on", sw / 2f, by + 160, 22, new Color(255, 214, 120, 230));
                }
                break;
            case Screen.ModeSelect:
                DrawModeSelect();
                break;
            case Screen.ClassSelect:
                DrawClassSelect();
                break;
            case Screen.Deploying:
            {
                Splash.Backdrop(sw, sh, (float) Raylib.GetTime(), new Vector2(sw / 2f, sh * 0.4f), 0.5f);
                var hero = Sheets.Characters.First(c => c.Id == Sheets.Classes[_pick].Character);
                _hud.TextCentered($"{Sheets.Classes[_pick].Name}", sw / 2f, sh * 0.38f, 64, World.Hex(Sheets.Classes[_pick].Color));
                _hud.TextCentered($"Deploying as {hero.Name} to {SelectedMap.Name}" + new string('.', (int) (Raylib.GetTime() * 3) % 4), sw / 2f, sh * 0.38f + 80, 28, Color.White);
                _hud.TextCentered(_assets.Status, sw / 2f, sh * 0.38f + 124, 20, new Color(200, 200, 210, 255));
                break;
            }
            case Screen.Viewer:
            {
                // dev view: every Gears character in a row, in one clip
                Raylib.ClearBackground(new Color(70, 70, 76, 255));
                // DAEVAS_VIEWER=<id,id,...> shows only those characters
                var only = Environment.GetEnvironmentVariable("DAEVAS_VIEWER")?.Split(',');
                var shown = _assets.Characters.Values.Where(c => only == null || only.Contains(c.Def.Id)).ToList();
                var nChars = shown.Count;
                // DAEVAS_VIEWERWINGS=<spread> puts Daeva wings on them (to compare with AION 2's)
                var wingSpread = float.TryParse(Environment.GetEnvironmentVariable("DAEVAS_VIEWERWINGS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ws) ? ws : -1;
                var cam = wingSpread >= 0
                    ? new Camera3D(new Vector3(0, 1.5f, 7.6f + nChars * 1.5f), new Vector3(0, 1.45f, 0), Vector3.UnitY, 45, CameraProjection.Perspective)
                    : new Camera3D(new Vector3(0, 1.3f, 3.2f + nChars * 1.05f), new Vector3(0, 1.0f, 0), Vector3.UnitY, 45, CameraProjection.Perspective);
                Raylib.BeginMode3D(cam);
                Lit.SetEye(cam.Position);
                Lit.ClearLights(); Lit.SendLights();
                Raylib.DrawGrid(10, 1);
                var i = 0;
                var t = (float) Raylib.GetTime() * 0.6f;
                foreach (var ch in shown)
                {
                    var pos = new Vector3((i - (nChars - 1) / 2f) * 1.6f, 0, 0);
                    var transform = Raymath.MatrixRotateY(_viewerYaw * MathF.PI / 180);
                    if (ch.Has(_viewerClip)) ch.Draw(_viewerClip, t, transform, pos, Color.White);
                    if (wingSpread >= 0)
                    {
                        var (wsn, wcs) = MathF.SinCos(_viewerYaw * MathF.PI / 180);
                        var wf = new Vector3(wsn, 0, wcs);
                        Wings.Draw(pos + Vector3.UnitY * 1.42f - wf * 0.22f, wf, Vector3.Normalize(Vector3.Cross(wf, Vector3.UnitY)), wingSpread, 0, new Color(120, 160, 255, 255), 0.95f);
                    }
                    if (_assets.HeldGuns.TryGetValue(ch.Def.Id, out var hg))
                    {
                        var h = ch.HandAt(_viewerClip, t);
                        var (sn, cs) = MathF.SinCos(_viewerYaw * MathF.PI / 180);
                        var dirv = new Vector3(sn, 0, cs);
                        foreach (var model in hg.Models)
                        {
                            var m = model;
                            m.Transform = Raymath.MatrixRotateY(MathF.Atan2(-dirv.Z, dirv.X));
                            Raylib.DrawModel(m, pos + new Vector3(h.X * cs + h.Z * sn, h.Y, -h.X * sn + h.Z * cs) + dirv * hg.Size.X * 0.3f, 1, Color.White);
                        }
                    }
                    i++;
                }
                Raylib.EndMode3D();
                _hud.Text($"{_viewerClip}", 20, 20, 30, Color.White);
                break;
            }
            case Screen.Playing:
            {
                var w = _world!;
                var eye = w.CameraEye();
                if (w.Lost)
                {
                    // the fall: the camera circles slowly round the hero
                    _deathOrbit += Raylib.GetFrameTime() * 0.35f;
                    eye = w.ClipCamera(w.P.Pos + Vector3.UnitY, w.P.Pos + new Vector3(MathF.Sin(_deathOrbit) * 6, 3.5f, MathF.Cos(_deathOrbit) * 6));
                }
                else if (Settings.S.CameraSmoothing && _camEye != Vector3.Zero && Vector3.Distance(_camEye, eye) < 6)
                    eye = Vector3.Lerp(_camEye, eye, MathF.Min(1, Raylib.GetFrameTime() * 14));
                _camEye = eye;
                if (w.Shake > 0.01f && !_paused)
                {
                    // camera shake: a quick jitter that fades
                    var j = w.Shake * w.Shake * 0.3f * Settings.S.CameraShake / 100f;
                    eye += new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * j;
                }
                var cam = new Camera3D(eye, w.Lost ? w.P.Pos + Vector3.UnitY : eye + w.AimDir() * 10, Vector3.UnitY, Settings.S.Fov, CameraProjection.Perspective);
                Audio.ListenerPos = eye; Audio.ListenerRight = w.P.Right;
                if (_photo)
                {
                    var pf = new Vector3(MathF.Sin(_photoYaw) * MathF.Cos(_photoPitch), MathF.Sin(_photoPitch), MathF.Cos(_photoYaw) * MathF.Cos(_photoPitch));
                    cam = new Camera3D(_photoEye, _photoEye + pf, Vector3.UnitY, Settings.S.Fov, CameraProjection.Perspective);
                }
                if (w.BossIntro > 0 && w.Boss != null)
                {
                    // a boss arrives: the camera turns to it for a moment
                    var k = MathF.Sin(MathF.PI * Math.Clamp(w.BossIntro / 2.6f, 0, 1));
                    cam.Target = Vector3.Lerp(cam.Target, w.Boss.Centre + Vector3.UnitY * w.Boss.Def.Size * 0.3f, k);
                }
                if (_recorder != null)
                {
                    // clips: a smooth chase camera behind and above the hero, easing round as they turn
                    var d = w.P.Yaw - _clipYaw;
                    while (d > MathF.PI) d -= MathF.Tau;
                    while (d < -MathF.PI) d += MathF.Tau;
                    _clipYaw += d * MathF.Min(1, Raylib.GetFrameTime() > 0 ? 2.2f / 30 : 0.07f);
                    var f = new Vector3(MathF.Sin(_clipYaw), 0, MathF.Cos(_clipYaw));
                    var r = new Vector3(-MathF.Cos(_clipYaw), 0, MathF.Sin(_clipYaw));
                    var want = w.P.Pos + new Vector3(0, 3.4f, 0) - f * 6.5f + r * 0.9f;
                    _clipEye = _clipEye == Vector3.Zero ? want : Vector3.Lerp(_clipEye, want, 0.08f);
                    var look = w.P.Pos + new Vector3(0, 1.5f, 0) + f * 5;
                    cam = new Camera3D(w.ClipCamera(look, _clipEye), look, Vector3.UnitY, 60, CameraProjection.Perspective);
                }
                if (_capsule)
                {
                    // dev (--capsule): the hero hovering on Daeva wings, seen from the front three-quarters, the street behind
                    var f = w.P.Forward; var r = w.P.Right;
                    var capEye = w.P.Pos + f * 4.4f + r * 1.7f + new Vector3(0, 1.7f, 0);
                    var capLook = w.P.Pos + new Vector3(0, 1.45f, 0) - r * 0.35f;
                    cam = new Camera3D(w.ClipCamera(capLook, capEye), capLook, Vector3.UnitY, 50, CameraProjection.Perspective);
                }
                if (_overview is { } ov)
                {
                    // dev: a high camera circling the play area
                    var ang = ov.yaw * MathF.PI / 180;
                    cam = new Camera3D(new Vector3(MathF.Sin(ang) * ov.dist, ov.height, MathF.Cos(ang) * ov.dist), Vector3.Zero, Vector3.UnitY, 55, CameraProjection.Perspective);
                }
                // dev: DAEVAS_CAM=ex,ey,ez,tx,ty,tz puts the camera anywhere (to inspect the city's edges)
                if (_devCam is { } dc) cam = new Camera3D(dc.eye, dc.at, Vector3.UnitY, 60, CameraProjection.Perspective);
                // dev: DAEVAS_CAMPATH="ex,ey,ez,tx,ty,tz;..." flies the camera through those shots over a recorded clip
                if (_camPath is { Length: > 1 } path)
                {
                    var u = Math.Clamp((_recorder != null ? _recordClock - _recordStart : (float) Raylib.GetTime() - 14) / _recordSeconds, 0, 1) * (path.Length - 1);
                    var k = Math.Min((int) u, path.Length - 2); var f = u - k; f = f * f * (3 - 2 * f);
                    cam = new Camera3D(Vector3.Lerp(path[k].eye, path[k + 1].eye, f), Vector3.Lerp(path[k].at, path[k + 1].at, f), Vector3.UnitY, 60, CameraProjection.Perspective);
                }
                var shadows = _overview == null && Settings.S.Shadows;
                if (shadows) w.DrawShadows();
                // hazy overcast sky, fading into the same colour as the distance fog
                Sky.Draw(cam);
                Raylib.BeginMode3D(cam);
                if (shadows) Lit.UseShadows(); else Lit.NoShadows();
                w.Draw3D(cam);
                Raylib.EndMode3D();
                if ((Settings.S.Bloom && !Settings.S.ReduceFlashing) || Settings.S.ColourBlind > 0) Post.Apply(cam);
                if (!_photo && !_noHud) _hud.Draw(w, cam);
                Ui.Begin();
                if (_noHud) { }
                else if (!_photo)
                {
                    UiExtras.DrawMinimap(w, _hud);
                    UiExtras.DrawObjectives(w, _hud);
                    UiExtras.DrawHint(_hud);
                    UiExtras.DrawBigMap(w, _hud);
                }
                else _hud.TextCentered("PHOTO MODE   WASD move   mouse look   Space/Ctrl up/down   F12 save   " + Keys.Name(Keys.Key("photo")) + " exit", sw / 2f, sh - 34, 16, new Color(255, 255, 255, 140));
                UiExtras.DrawFlash();
                if (_paused)
                {
                    Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
                    _hud.TextCentered("PAUSED", sw / 2f, sh * 0.08f, 64, new Color(255, 214, 110, 255));
                    _hud.TextCentered(Pad.Active ? "A: resume    X: retry    Y: options    B: class select" : "Enter: resume    R: retry    O: options    C: class select    F: feedback    Esc: resume", sw / 2f, sh * 0.08f + 74, 22, Color.White);
                    _hud.TextCentered($"{w.P.Cls.Name} skill book", sw / 2f, sh * 0.08f + 118, 26, World.Hex(w.P.Cls.Color));
                    _hud.DrawSkillBook(w, sh * 0.08f + 160, _bookPick);
                }
                if (w.Won || w.Lost)
                {
                    Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 140));
                    _hud.TextCentered(w.Won ? "VICTORY" : "DEFEAT", sw / 2f, sh * 0.36f, 96, w.Won ? new Color(255, 214, 110, 255) : new Color(230, 70, 60, 255));
                    _hud.TextCentered($"{Modes.Name(w.Mode)}{(w.Hardcore ? " (Hardcore)" : "")}  -  {w.P.Cls.Name}  -  level {w.P.Level}  -  {w.WaveLabel}  -  score {w.Score:N0}" + (w.BoardPlace > 0 ? $"  (#{w.BoardPlace} on your board)" : ""), sw / 2f, sh * 0.36f + 110, 28, Color.White);
                    if (w.Mode == GameMode.Campaign) _hud.TextCentered(Story.Epilogue(w.Won, w.Hardcore), sw / 2f, sh * 0.36f + 250, 20, new Color(190, 200, 220, 255));
                    var t = TimeSpan.FromSeconds(w.TimeAlive);
                    _hud.TextCentered($"{w.Kills} kills   {w.DamageDealt:N0} damage dealt   {w.DamageTaken:N0} taken   {w.SkillCasts} skills   {w.ChainCasts} chains   {(int) t.TotalMinutes}:{t.Seconds:00}", sw / 2f, sh * 0.36f + 150, 22, new Color(230, 220, 200, 255));
                    _hud.TextCentered($"best combo {w.BestCombo}   executions {w.Executions}   top: " + string.Join(", ", w.DamageBySource.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value:N0}")), sw / 2f, sh * 0.36f + 180, 20, new Color(210, 200, 185, 255));
                    _hud.TextCentered(Pad.Active ? "X: retry   A: choose a class again" : "R: retry   Enter: choose a class again", sw / 2f, sh * 0.36f + 216, 22, new Color(255, 214, 110, 255));
                }
                if (w.StigmaOpen) _hud.DrawStigma(w, _stigmaPick);
                BagScreen.Draw(w, _hud);
                Ui.End();
                break;
            }
        }
    }

    static Rectangle ClassCard(int i)
    {
        int sw = Ui.W, sh = Ui.H;
        float cw = MathF.Min(330, (sw - 120) / 4f), ch = 190;
        var x0 = sw / 2f - (cw * 4 + 30 * 3) / 2;
        return new Rectangle(x0 + (i % 4) * (cw + 30), sh * 0.22f + (i / 4) * (ch + 26), cw, ch);
    }

    static void DrawClassSelect()
    {
        int sw = Ui.W, sh = Ui.H;
        Splash.Backdrop(sw, sh, (float) Raylib.GetTime(), new Vector2(sw / 2f, sh * 0.4f), 0.55f);
        _hud.TextCentered("CHOOSE YOUR CLASS", sw / 2f, sh * 0.08f, 54, new Color(255, 214, 120, 255));
        for (var i = 0; i < Sheets.Classes.Length; i++)
        {
            var c = Sheets.Classes[i];
            var r = ClassCard(i);
            var col = World.Hex(c.Color);
            var sel = i == _pick;
            Raylib.DrawRectangleRounded(r, 0.08f, 6, sel ? new Color(36, 30, 26, 235) : new Color(14, 14, 18, 200));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, sel ? 3 : 1, sel ? col : new Color(80, 76, 70, 255));
            Raylib.DrawRectangle((int) r.X + 12, (int) r.Y + 16, 6, 34, col);
            _hud.Text(c.Name, r.X + 28, r.Y + 12, 32, sel ? Color.White : new Color(220, 214, 200, 255));
            _hud.Text(c.Role, r.X + 28, r.Y + 50, 16, new Color(200, 190, 170, 255));
            var gun = Sheets.Weapons.First(w => w.Id == c.Weapon);
            _hud.DrawGunIcon(gun.Id, new Rectangle(r.X + r.Width - 130, r.Y + r.Height - 62, 120, 52), Color.White);
            _hud.Text("HP " + c.MaxHp + "   MP " + c.MaxMp, r.X + 14, r.Y + 80, 17, new Color(235, 230, 220, 255));
            var heroName = Sheets.Characters.First(h => h.Id == Progression.HeroFor(c)).Name;
            _hud.Text($"{heroName}  -  {gun.Name}", r.X + 14, r.Y + r.Height - 30, 15, new Color(255, 200, 130, 255));
            var skills = c.Skills.Select(id => Sheets.Skills.First(s => s.Id == id).Name);
            _hud.Text(string.Join(", ", skills.Take(2)), r.X + 14, r.Y + 104, 15, col);
            _hud.Text(string.Join(", ", skills.Skip(2)), r.X + 14, r.Y + 124, 15, col);
        }
        var p = Sheets.Classes[_pick];
        _hud.TextCentered(p.Description, sw / 2f, sh * 0.22f + 2 * 216 + 10, 24, Color.White);
        _hud.TextCentered(Story.Bios.GetValueOrDefault(p.Id, ""), sw / 2f, sh * 0.22f + 2 * 216 + 44, 19, new Color(200, 205, 220, 255));
        _hud.TextCentered(Story.Quotes.GetValueOrDefault(Progression.HeroFor(p), ""), sw / 2f, sh * 0.22f + 2 * 216 + 124, 17, new Color(200, 190, 170, 220));
        var looks = Progression.HeroesFor(p);
        _hud.TextCentered($"Hero: {Sheets.Characters.First(c => c.Id == Progression.HeroFor(p)).Name}" + (looks.Count > 1 ? $"   ({(Pad.Active ? "Y" : "H")}: change, {looks.Count} unlocked)" : "   (clear the campaign with other classes to unlock their heroes)"), sw / 2f, sh * 0.22f + 2 * 216 + 100, 17, new Color(220, 210, 190, 255));
        if (Profile.P.Records.TryGetValue(p.Id, out var rec))
            _hud.TextCentered($"Best wave {rec.BestWave}   wins {rec.Wins}   runs {rec.Runs}", sw / 2f, sh * 0.22f + 2 * 216 + 72, 18, new Color(255, 214, 110, 255));
        _hud.TextCentered((Pad.Active ? "D-pad to choose  -  A to deploy  -  In game: sticks move/aim, RT attack, A run, B evade, Y fly, RS lock, D-pad skills 1-4, LT + D-pad skills 5-8"
            : "Arrows or mouse to choose  -  Enter or click to deploy  -  In game: WASD, mouse, left click attack, Shift run, Space evade, F fly, Tab target, 1-8 skills (again for chains)"),
            sw / 2f, sh - 50, 18, new Color(200, 190, 170, 255));
    }
}
