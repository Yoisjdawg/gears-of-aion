using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Daevas.Game;

/// Music and ambience from the Gears install (looping beds that fade between cues), ducking for big moments,
/// and a filter on the final mix: a low-pass when you're badly hurt and an echo in narrow streets.
static unsafe class Audio
{
    sealed class Channel
    {
        public string Cue = "";
        public Sound? Sound;
        public float Fade, Volume;
    }

    static readonly Dictionary<string, Channel> _channels = new();
    static float _duck;
    public static float Muffle, Echo;        // 0..1, set by the game each frame
    static bool _attached;

    /// Keeps a looping bed playing on a channel ("music", "ambience", "rain"); a new cue fades the old one out.
    public static void Cue(string channel, string owner, string when, float volume)
    {
        if (!_channels.TryGetValue(channel, out var ch)) _channels[channel] = ch = new Channel();
        var cue = owner == "" ? "" : owner + "/" + when;
        ch.Volume = volume;
        if (ch.Cue == cue) return;
        if (ch.Sound is { } old) Raylib.StopSound(old);
        ch.Cue = cue; ch.Fade = 0;
        ch.Sound = cue == "" ? null : Sfx.GearsLoop(owner, when);
    }

    /// A big moment (a boss, an ultimate, a level-up): the music dips for a moment.
    public static void Duck(float seconds) => _duck = MathF.Max(_duck, seconds);

    public static void Update(float dt)
    {
        if (!_attached && Raylib.IsAudioDeviceReady())
        {
            _attached = true;
            Raylib.AttachAudioMixedProcessor(&Process);
        }
        _duck = MathF.Max(0, _duck - dt);
        foreach (var (name, ch) in _channels)
        {
            if (ch.Sound is not { } s) continue;
            ch.Fade = MathF.Min(1, ch.Fade + dt * 0.5f);
            var level = name == "music" ? Settings.S.MusicVolume / 100f * (_duck > 0 ? 0.35f : 1) : Settings.S.AmbienceVolume / 100f;
            Raylib.SetSoundVolume(s, Math.Clamp(ch.Volume * ch.Fade * level, 0, 1));
            if (!Raylib.IsSoundPlaying(s)) Raylib.PlaySound(s); // loop
        }
    }

    public static void StopAll()
    {
        foreach (var ch in _channels.Values) if (ch.Sound is { } s) Raylib.StopSound(s);
        _channels.Clear();
    }

    // ---------------------------------------------------------------- the mix filter (runs on the audio thread)

    const int DelayFrames = 6200;           // about 0.14 s at 44.1 kHz
    static readonly float[] _delayL = new float[DelayFrames], _delayR = new float[DelayFrames];
    static int _pos;
    static float _lpL, _lpR;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void Process(void* buffer, uint frames)
    {
        var s = (float*) buffer;
        var alpha = 1 - Math.Clamp(Muffle, 0, 1) * 0.92f;
        var echo = Math.Clamp(Echo, 0, 1);
        for (var i = 0; i < frames; i++)
        {
            var l = s[i * 2]; var r = s[i * 2 + 1];
            _lpL += (l - _lpL) * alpha; _lpR += (r - _lpR) * alpha;
            l = _lpL; r = _lpR;
            if (echo > 0.01f)
            {
                var dl = _delayL[_pos]; var dr = _delayR[_pos];
                _delayL[_pos] = l + dr * 0.35f * echo;   // cross-fed so it widens a little
                _delayR[_pos] = r + dl * 0.35f * echo;
                l += dl * 0.32f * echo; r += dr * 0.32f * echo;
            }
            _pos = (_pos + 1) % DelayFrames;
            s[i * 2] = l; s[i * 2 + 1] = r;
        }
    }

    // ---------------------------------------------------------------- positional sound

    public static Vector3 ListenerPos, ListenerRight = Vector3.UnitX;

    /// Left/right pan (0..1, 0.5 centre) for a sound at a point.
    public static float PanFor(Vector3 at)
    {
        var to = at - ListenerPos;
        if (to.LengthSquared() < 0.01f) return 0.5f;
        var side = Vector3.Dot(Vector3.Normalize(to), ListenerRight);
        return Math.Clamp(0.5f - side * 0.4f, 0.05f, 0.95f);
    }
}
