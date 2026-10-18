using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Raylib_cs;

namespace Daevas.Game;

/// The player's options, kept in %LOCALAPPDATA%\GearsOfAion\settings.json between runs.
sealed class SettingsData
{
    public int Version { get; set; } = 1;
    public int MasterVolume { get; set; } = 80;      // 0..100
    public int EffectsVolume { get; set; } = 100;    // 0..100, scales every game sound
    public int MusicVolume { get; set; } = 70;
    public int AmbienceVolume { get; set; } = 80;
    public float MouseSensitivity { get; set; } = 1; // x the sheet value
    public float PadLookSpeed { get; set; } = 1;
    public bool InvertLook { get; set; }
    public int Fov { get; set; } = 62;
    public int CameraShake { get; set; } = 100;      // percent
    public bool ReduceFlashing { get; set; }
    public bool Bloom { get; set; } = true;
    public bool Shadows { get; set; } = true;
    public bool Fullscreen { get; set; }
    public int FrameCap { get; set; } = 0;           // 0 = VSync
    public bool LeftShoulder { get; set; }
    public float CameraDistance { get; set; } = 1;   // x the sheet value (mouse wheel zooms)
    public bool DamageNumbers { get; set; } = true;
    public int UiScale { get; set; } = 100;          // percent
    public bool AutoTarget { get; set; } = true;     // skills turn to the nearest Locust when nothing is aimed at
    public bool HoldToSprint { get; set; } = true;
    public int Difficulty { get; set; } = 1;
    public bool AutoAttack { get; set; } = true;   // keep basic-attacking a locked target
    public Dictionary<string, int> KeyBinds { get; set; } = new();
    public Dictionary<string, int> PadBinds { get; set; } = new();
    public int ColourBlind { get; set; }             // 0 off, 1 deuteranopia, 2 protanopia, 3 tritanopia
    public int LookCurve { get; set; } = 1;          // 0 linear, 1 smooth, 2 sharp (gamepad)
    public bool AimAssist { get; set; } = true;      // gamepad: the aim drifts onto Locust near the crosshair
    public bool MouseOnly { get; set; }              // right mouse moves forward, wheel picks a skill, middle click casts it
    public int GameSpeed { get; set; } = 100;        // percent: slower play for accessibility
    public bool AutoEvade { get; set; }              // roll out of a red circle at the last moment (once every 6 s)
    public bool SimpleHotbar { get; set; }           // show four big slots (keys 5-8 still work)
    public bool CameraSmoothing { get; set; } = true;
    public bool LockOnCamera { get; set; }           // the camera turns to keep a locked target in view
    public bool Captions { get; set; } = true;       // captions for Locust sounds
    public bool Squadmate { get; set; } = true;      // a COG soldier fights beside you
    public int Preset { get; set; } = 2;             // 0 Low, 1 Medium, 2 High
    public int WingSize { get; set; } = 100;         // percent of the wings' usual size (smaller shows more of the fight)
    public int WingStyle { get; set; }               // 0 Elyos white, 1 Asmodian dark
    public bool Rumble { get; set; } = true;         // controller vibration on hits and blasts
    public int CaptionSize { get; set; } = 100;      // percent: subtitles and captions
    public int Grade { get; set; }                   // 0 Gears (warm light, cool shade), 1 Abyss (cool violet), 2 Neutral
    public int Monitor { get; set; }         // 0 Easy, 1 Normal, 2 Hard, 3 Daeva
}

static class Settings
{
    public static SettingsData S = new();
    static string File => System.IO.Path.Combine(Paths.DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Load()
    {
        // test and background runs always use the defaults, so a player's options never change a check
        if (Environment.GetEnvironmentVariable("DAEVAS_TEST") != null || Environment.GetCommandLineArgs().Contains("--background")) return;
        try
        {
            if (System.IO.File.Exists(File)) S = JsonSerializer.Deserialize<SettingsData>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch (Exception e) { Log.Info("settings unreadable, using defaults: " + e.Message); S = new(); }
        Clamp();
    }

    static SettingsData? _ownBeforeReplay;

    /// A replay plays with the options it was recorded with (unsaved); the player's own come back after.
    public static void Watch(string json)
    {
        _ownBeforeReplay ??= S;
        try { S = JsonSerializer.Deserialize<SettingsData>(json) ?? new(); } catch { S = new(); }
        Clamp();
    }

    public static void Unwatch() { if (_ownBeforeReplay != null) { S = _ownBeforeReplay; _ownBeforeReplay = null; } }

    public static string Snapshot() => JsonSerializer.Serialize(S);

    public static void Save()
    {
        if (_ownBeforeReplay != null) return;
        if (Environment.GetEnvironmentVariable("DAEVAS_TEST") != null || Environment.GetCommandLineArgs().Contains("--background")) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(S, Json));
        }
        catch (Exception e) { Log.Info("settings not saved: " + e.Message); }
    }

    static void Clamp()
    {
        S.MasterVolume = Math.Clamp(S.MasterVolume, 0, 100);
        S.EffectsVolume = Math.Clamp(S.EffectsVolume, 0, 100);
        S.MouseSensitivity = Math.Clamp(S.MouseSensitivity, 0.2f, 4f);
        S.PadLookSpeed = Math.Clamp(S.PadLookSpeed, 0.2f, 4f);
        S.Fov = Math.Clamp(S.Fov, 50, 100);
        S.CameraShake = Math.Clamp(S.CameraShake, 0, 100);
        S.CameraDistance = Math.Clamp(S.CameraDistance, 0.6f, 1.8f);
        S.UiScale = Math.Clamp(S.UiScale, 75, 150);
        S.WingSize = Math.Clamp(S.WingSize, 50, 130);
        S.WingStyle = Math.Clamp(S.WingStyle, 0, 1);
        S.CaptionSize = Math.Clamp(S.CaptionSize, 75, 200);
        S.Grade = Math.Clamp(S.Grade, 0, 2);
        S.Difficulty = Math.Clamp(S.Difficulty, 0, 3);
        S.ColourBlind = Math.Clamp(S.ColourBlind, 0, 3);
        S.LookCurve = Math.Clamp(S.LookCurve, 0, 2);
        if (S.GameSpeed is not (60 or 80 or 100)) S.GameSpeed = 100;
        S.KeyBinds ??= new(); S.PadBinds ??= new();
        if (S.FrameCap is not (0 or 30 or 60 or 120 or 144 or 240)) S.FrameCap = 0;
    }

    /// Puts the options into effect (volume, window, frame rate).
    public static void Apply(bool muted)
    {
        Clamp();
        Raylib.SetMasterVolume(muted ? 0 : S.MasterVolume / 100f);
        Raylib.SetTargetFPS(S.FrameCap);
        if (S.FrameCap == 0) Raylib.SetWindowState(ConfigFlags.VSyncHint); else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
        if (S.Monitor > 0 && S.Monitor < Raylib.GetMonitorCount() && S.Monitor != Raylib.GetCurrentMonitor()) Raylib.SetWindowMonitor(S.Monitor);
        if (S.Fullscreen != Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode)) Raylib.ToggleBorderlessWindowed();
        City.DetailRadius = S.Preset switch { 0 => 0.6f, 1 => 0.8f, _ => 1f };
        Particles.CapScale = S.Preset switch { 0 => 0.4f, 1 => 0.7f, _ => 1f };
    }
}
