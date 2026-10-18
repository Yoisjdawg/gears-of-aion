using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Which Gears animation the hero plays: casting with hands raised, flinching when hit, sprinting, strafing, jumping
/// into flight and landing out of it, an idle break, a victory pose, falling, getting back up, turning on the spot.
/// Also the hero's voice (grunts, pain, death) from their Gears voice actor.
sealed partial class World
{
    Clip _oneShot = Clip.Idle;
    float _oneShotTime = -1, _idleTime, _lastYaw, _lostTime, _voiceCd;
    bool _wasAirborne;

    void PlayOnce(Clip c) { _oneShot = c; _oneShotTime = 0; }

    void Voice(string when, float chance = 1)
    {
        if (_voiceCd > 0 || _rng.NextDouble() > chance) return;
        if (Sfx.PlayGears("voice-" + HeroId, when, 0.9f)) _voiceCd = 1.2f;
    }

    void UpdateHeroAnim(float dt)
    {
        if (_voiceCd > 0) _voiceCd -= dt;
        if (_oneShotTime >= 0) _oneShotTime += dt;
        var air = Up > 0.3f && !Climbing;
        if (air && !_wasAirborne) { PlayOnce(Clip.Jump); Voice("jump", 0.6f); }
        if (!air && _wasAirborne) { PlayOnce(Clip.Land); Voice("land", 0.5f); }
        _wasAirborne = air;
        if (Lost) _lostTime += dt;
    }

    (Clip clip, float t, float lean) HeroClip(CharacterModel hero, bool walk, bool airborne, bool moving)
    {
        float Prog(Clip c, float time) => hero.Has(c) ? MathF.Min(0.999f, time / MathF.Max(0.1f, hero.Duration[c])) : 0;
        if (Lost && hero.Has(Clip.Death)) return (Clip.Death, Prog(Clip.Death, _lostTime), 0);
        if (Won && hero.Has(Clip.Victory)) return (Clip.Victory, (float) Raylib.GetTime() / hero.Duration[Clip.Victory] % 1, 0);
        if (PlayerStun > 0 && PlayerStunLabel == "Knocked down") { _oneShot = Clip.GetUp; _oneShotTime = -0.0001f; return (Clip.Aim, 0, 1.35f); }
        if (_oneShot == Clip.GetUp && _oneShotTime < 0 && PlayerStun <= 0) PlayOnce(Clip.GetUp);
        if (P.ActionAnim >= 0 && hero.Has(Clip.Attack)) return (Clip.Attack, MathF.Min(0.999f, P.ActionAnim / 0.55f), 0);
        if (P.Casting is { } cast && hero.Has(Clip.Cast)) return (Clip.Cast, MathF.Min(0.999f, cast.Time / MathF.Max(0.2f, cast.Skill.CastTime) * 0.8f), 0);
        // one-shots: a cast going off, a flinch, take-off, landing, getting up, an idle break
        if (_oneShotTime >= 0 && hero.Has(_oneShot))
        {
            var len = _oneShot is Clip.Land or Clip.Jump ? MathF.Min(0.6f, hero.Duration[_oneShot]) : hero.Duration[_oneShot];
            if (_oneShotTime < len) return (_oneShot, MathF.Min(0.999f, _oneShotTime / len), 0);
            _oneShotTime = -1;
        }
        if (walk)
        {
            _idleTime = 0;
            if (P.Sprinting && hero.Has(Clip.Sprint)) return (Clip.Sprint, P.MoveBob / MathF.Tau * 0.8f % 1, 0);
            if (MathF.Abs(_move.X) > MathF.Abs(_move.Y) + 0.2f)
            {
                var strafe = _move.X < 0 ? Clip.StrafeL : Clip.StrafeR;
                if (hero.Has(strafe)) return (strafe, P.MoveBob / MathF.Tau % 1, 0);
            }
            return (Clip.Walk, P.MoveBob / MathF.Tau, 0);
        }
        // turning on the spot: shuffle the feet
        var turn = MathF.Abs(LerpAngleDelta(_lastYaw, P.Yaw));
        _lastYaw = P.Yaw;
        if (!airborne && turn > 0.03f) return (Clip.Walk, (float) Raylib.GetTime() * 0.8f % 1, 0);
        _idleTime += Raylib.GetFrameTime();
        if (_idleTime > 9 && hero.Has(Clip.Idle2)) { _idleTime = 0; PlayOnce(Clip.Idle2); }
        return (Clip.Aim, (float) Raylib.GetTime() / hero.Duration[Clip.Aim], airborne && moving ? -0.3f : 0);
    }

    static float LerpAngleDelta(float a, float b)
    {
        var d = b - a;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
}
