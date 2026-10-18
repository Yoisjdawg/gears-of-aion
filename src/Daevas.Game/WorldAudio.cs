using System;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// What the run sounds like: music for the moment, ambience beds, footsteps, wings, Ticker beeps, fire crackle,
/// the echo of narrow streets and the muffle of being badly hurt.
sealed partial class World
{
    float _stepBob, _flapTimer, _beepTimer, _crackleTimer, _echoTimer, _holeTimer, _hitTimer;
    bool _stingPlayed;
    (string who, string line)? _lastRadio;

    void UpdateAudio(float dt)
    {
        // music: the boss theme while a boss lives, battle music in a wave, quieter between waves
        if (Won || Lost)
        {
            Audio.Cue("music", "", "", 0);
            if (!_stingPlayed) { _stingPlayed = true; Sfx.PlayGears("ui", Won ? "victory" : "defeat"); }
        }
        else if (Boss != null) Audio.Cue("music", "music", "boss", 0.55f);
        else if (WaveActive) Audio.Cue("music", "music", Wave % 2 == 0 ? "fight" : "siege", 0.45f);
        else Audio.Cue("music", "music", "siege", 0.25f);
        Audio.Cue("ambience", "ambience", "city", 0.5f);
        Audio.Cue("battle", WaveActive ? "ambience" : "", "battle", 0.4f);
        Audio.Cue("rain", Env.Wet > 0.25f ? "ambience" : "", "rain", 0.4f + 0.5f * Env.Wet);
        Audio.Cue("wind", Env.Dust > 0.3f ? "ambience" : "", "wind", 0.3f + 0.6f * Env.Dust);

        // badly hurt: the world goes muffled; narrow streets echo
        Audio.Muffle = Lost ? 0.6f : Math.Clamp((0.3f - P.Hp / P.MaxHp) * 3.5f, 0, 0.85f);
        if ((_echoTimer -= dt) <= 0)
        {
            _echoTimer = 0.5f;
            var blocked = 0;
            for (var i = 0; i < 16; i++)
            {
                var a = i / 16f * MathF.Tau;
                if (Blocked(P.Pos + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 5, 0.3f)) blocked++;
            }
            Audio.Echo = Math.Clamp((blocked / 16f - 0.3f) * 1.6f, 0, 0.8f);
        }

        // footsteps on the asphalt, and a thud on landing
        if (Up < 0.05f && MathF.Floor(P.MoveBob / MathF.PI) != MathF.Floor(_stepBob / MathF.PI)) Sfx.PlayGears("hero", "step", P.Sprinting ? 0.9f : 0.6f);
        _stepBob = P.MoveBob;
        // wing beats in flight
        if (P.Flying && (_flapTimer -= dt) <= 0) { _flapTimer = 1.4f; Sfx.PlayGears("hero", "flap", 0.5f); }
        // Tickers tick louder the closer they get
        if ((_beepTimer -= dt) <= 0)
        {
            var near = Enemies.Where(e => e.Def.Id == "ticker" && e.Emerge < 0).OrderBy(e => Dist2(e.Pos, P.Pos)).FirstOrDefault();
            if (near != null && Dist2(near.Pos, P.Pos) < 12) { Sfx.PlayGearsAt("ticker", "beep", near.Pos, Falloff(near.Pos)); _beepTimer = 0.4f + Dist2(near.Pos, P.Pos) * 0.08f; }
            else _beepTimer = 0.5f;
        }
        // fire crackle from the nearest burning wreck
        if ((_crackleTimer -= dt) <= 0)
        {
            _crackleTimer = 3.5f;
            if (_fires.Count > 0)
            {
                var f = _fires.OrderBy(x => Vector3.DistanceSquared(x, P.Pos)).First();
                if (Dist2(f, P.Pos) < 10) Sfx.PlayGearsAt("world", "fire", f, 0.6f * Falloff(f));
            }
        }
        if (_holeTimer > 0) _holeTimer -= dt;
        if (_hitTimer > 0) _hitTimer -= dt;
        // radio crackle when a new line comes in
        if (RadioNow != null && RadioNow != _lastRadio) Sfx.Play(Sfx.Radio);
        _lastRadio = RadioNow;
    }

    /// The ground breaking open near you.
    void EHoleSound(Vector3 at)
    {
        if (_holeTimer > 0 || Dist2(at, P.Pos) > 25) return;
        _holeTimer = 1.5f;
        Sfx.PlayGearsAt("world", "ehole", at, Falloff(at));
    }

    /// A melee hit landing (not every one, so a cleave through five Locust doesn't stack five thumps).
    void HitSound()
    {
        if (_hitTimer > 0) return;
        _hitTimer = 0.12f;
        Sfx.PlayGears("hero", P.Cls.Id == "gladiator" && _rng.NextDouble() < 0.3 ? "chainsaw" : "hit", 0.7f);
    }

    /// Each element sounds different as the skill goes off.
    void ElementSound(string element, string kind)
    {
        switch (element)
        {
            case "lightning": Sfx.PlayQuiet(Sfx.Kill, 0.25f, 1.8f); Sfx.PlayGears("world", "thunder", 0.25f); break;
            case "earth": Sfx.PlayGears("world", "explosion", 0.4f); break;
            case "fire": Sfx.PlayGears("world", "fire", 0.5f); break;
            case "holy" when kind is "heal" or "regen" or "zone" or "salvation": Sfx.PlayGears("hero", "heal", 0.8f); break;
            case "physical": Sfx.PlayGears("hero", "swing", 0.6f); break;
            case "ice": Sfx.PlayQuiet(Sfx.Cast, 0.45f, 1.5f); break;
            case "shadow": Sfx.PlayQuiet(Sfx.Cast, 0.45f, 0.6f); break;
            case "wind": Sfx.PlayGears("hero", "evade", 0.5f); break;
            default: Sfx.PlayQuiet(Sfx.Cast, 0.45f, 0.9f + (float) _rng.NextDouble() * 0.3f); break;
        }
    }
}
