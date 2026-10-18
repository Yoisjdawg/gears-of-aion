using System;
using System.Collections.Generic;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// Our own synthesized sounds (no audio is taken from either game).
static unsafe class Sfx
{
    public static Sound Rifle, Shotgun, Sniper, Reload, Cast, Kill, Hurt, LevelUp, Click, Radio;
    static bool _ready;

    public static void Load()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return;
        var rng = new Random(7);
        Rifle = Make(0.12f, t => Noise(rng) * Env(t, 0.12f, 30) * 0.5f + MathF.Sin(t * 900) * Env(t, 0.05f, 60) * 0.3f);
        Shotgun = Make(0.35f, t => Noise(rng) * Env(t, 0.35f, 12) * 0.7f + MathF.Sin(t * 300) * Env(t, 0.1f, 25) * 0.4f);
        Sniper = Make(0.6f, t => Noise(rng) * Env(t, 0.6f, 9) * 0.6f + MathF.Sin(t * 180 + MathF.Sin(t * 30)) * Env(t, 0.3f, 8) * 0.5f);
        Reload = Make(0.25f, t => (t < 0.04f || (t > 0.18f && t < 0.22f) ? Noise(rng) * 0.4f : 0) + MathF.Sin(t * 2400) * Env(t, 0.03f, 90) * 0.2f);
        Cast = Make(0.4f, t => MathF.Sin(t * (700 + t * 1600) * MathF.Tau / 6) * Env(t, 0.4f, 6) * 0.35f);
        Kill = Make(0.3f, t => MathF.Sin(t * 220 * MathF.Tau / (1 + t * 6)) * Env(t, 0.3f, 10) * 0.4f);
        Hurt = Make(0.18f, t => (Noise(rng) * 0.4f + MathF.Sin(t * 140 * MathF.Tau) * 0.4f) * Env(t, 0.18f, 18));
        LevelUp = Make(1.0f, t =>
        {
            var note = (int) (t * 8) % 4;
            var f = new[] { 523f, 659f, 784f, 1047f }[note];
            return MathF.Sin(t * f * MathF.Tau) * Env(t, 1f, 2.5f) * 0.3f;
        });
        Click = Make(0.05f, t => MathF.Sin(t * 1800 * MathF.Tau) * Env(t, 0.05f, 80) * 0.25f);
        // a radio squelch: a crackle burst and a short tone
        Radio = Make(0.22f, t => (Noise(rng) * 0.35f * Env(t, 0.22f, 9) + MathF.Sin(t * 1250 * MathF.Tau) * (t < 0.06f ? 0.18f : 0)) * (MathF.Sin(t * 60) > -0.3f ? 1 : 0.4f));
        _ready = true;
    }

    // Gears of War: E-Day recordings, by owner and moment ("lancer/fire", "drone/death").
    sealed class GearsVoice { public required Sound[] Voices; public int Next; }
    sealed class GearsBank { public required SoundRow Def; public List<GearsVoice> Main = new(), Thump = new(); }
    static readonly Dictionary<string, GearsBank> _gears = new();
    public static int GearsSounds => _gears.Count;

    public static void AddGears(SoundRow def, List<Daevas.Gears.GearsSound> main, List<Daevas.Gears.GearsSound> thump)
    {
        if (!_ready || main.Count == 0) return;
        var bank = new GearsBank { Def = def };
        foreach (var s in main) bank.Main.Add(MakeVoice(s));
        foreach (var s in thump) bank.Thump.Add(MakeVoice(s));
        _gears[$"{def.Owner}/{def.When}"] = bank;
    }

    static GearsVoice MakeVoice(Daevas.Gears.GearsSound s)
    {
        var data = (short*) Raylib.MemAlloc((uint) (s.Samples.Length * sizeof(short)));
        s.Samples.AsSpan().CopyTo(new Span<short>(data, s.Samples.Length));
        var wave = new Wave { FrameCount = (uint) (s.Samples.Length / s.Channels), SampleRate = (uint) s.Rate, SampleSize = 16, Channels = (uint) s.Channels, Data = data };
        var first = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        // a few aliases so rapid fire can overlap
        var voices = new Sound[4];
        voices[0] = first;
        for (var i = 1; i < voices.Length; i++) voices[i] = Raylib.LoadSoundAlias(first);
        return new GearsVoice { Voices = voices };
    }

    public static bool HasGears(string owner, string when) => _gears.ContainsKey($"{owner}/{when}");

    /// A recording to loop on its own (music and ambience beds use the last alias, so one-shots don't cut them off).
    public static Sound? GearsLoop(string owner, string when)
    {
        if (!_ready || !_gears.TryGetValue($"{owner}/{when}", out var bank) || bank.Main.Count == 0) return null;
        var v = bank.Main[Random.Shared.Next(bank.Main.Count)];
        return v.Voices[^1];
    }

    /// A Gears recording from a point in the world: quieter with distance, panned left or right.
    public static bool PlayGearsAt(string owner, string when, System.Numerics.Vector3 at, float volume = 1) => PlayGears(owner, when, volume, Audio.PanFor(at));

    /// Plays the Gears recording for owner/when; false when there is none (use a synthesized one).
    public static bool PlayGears(string owner, string when, float volume = 1, float pan = 0.5f)
    {
        if (!_ready || !_gears.TryGetValue($"{owner}/{when}", out var bank)) return false;
        void One(List<GearsVoice> list, float vol)
        {
            if (list.Count == 0) return;
            var v = list[Random.Shared.Next(list.Count)];
            var s = v.Voices[v.Next++ % v.Voices.Length];
            Raylib.SetSoundVolume(s, Math.Clamp(vol * Settings.S.EffectsVolume / 100f, 0, 1));
            Raylib.SetSoundPitch(s, 0.97f + Random.Shared.NextSingle() * 0.06f);
            Raylib.SetSoundPan(s, pan);
            Raylib.PlaySound(s);
        }
        One(bank.Main, bank.Def.Volume * volume);
        One(bank.Thump, bank.Def.Volume * volume * 0.8f);
        if (volume > 0.15f) Captions.Add(owner, when);
        return true;
    }

    /// A quieter, lower play (distant booms, thunder).
    public static void PlayQuiet(Sound s, float volume, float pitch)
    {
        if (!_ready) return;
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundVolume(s, volume * Settings.S.EffectsVolume / 100f);
        Raylib.PlaySound(s);
    }

    public static void Play(Sound s)
    {
        if (!_ready) return;
        if (s.Stream.Buffer == Click.Stream.Buffer && PlayGears("ui", "click", 0.8f)) return;
        if (s.Stream.Buffer == LevelUp.Stream.Buffer) PlayGears("hero", "levelup");
        Raylib.SetSoundPitch(s, 0.94f + Random.Shared.NextSingle() * 0.12f);
        Raylib.SetSoundVolume(s, Settings.S.EffectsVolume / 100f);
        Raylib.PlaySound(s);
    }

    static float Noise(Random r) => r.NextSingle() * 2 - 1;
    static float Env(float t, float len, float k) => t >= len ? 0 : MathF.Exp(-t * k) * (1 - t / len);

    static Sound Make(float seconds, Func<float, float> f)
    {
        const int rate = 44100;
        var n = (int) (seconds * rate);
        var data = (short*) Raylib.MemAlloc((uint) (n * sizeof(short)));
        for (var i = 0; i < n; i++) data[i] = (short) (Math.Clamp(f(i / (float) rate), -1f, 1f) * 30000);
        var wave = new Wave { FrameCount = (uint) n, SampleRate = rate, SampleSize = 16, Channels = 1, Data = data };
        var s = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        return s;
    }
}
