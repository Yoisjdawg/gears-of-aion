using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// More AION flight: gliding down, wind streams that refill the wings, an air dash, a diving strike with a landing
/// shockwave, juggling airborne Locust, a flight boost, and wing skins.
sealed partial class World
{
    public readonly List<Vector3> WindStreams = new();
    public bool Gliding;
    public float AirDashCd, DiveCd;
    Vector2 _move;
    float _fallFrom;
    bool _diving;
    Enemy? _diveTarget;

    void PlaceWindStreams()
    {
        for (var i = 0; i < 3; i++) if (ReachableSpot(8) is { } at) WindStreams.Add(at);
    }

    /// Space while flying: a quick dash where you're steering (costs some flight time).
    public bool AirDash()
    {
        if (!P.Flying || AirDashCd > 0 || P.FlightTime < 1.5f) return false;
        var dir = P.Forward * _move.Y + P.Right * _move.X;
        if (dir.LengthSquared() < 0.01f) dir = P.Forward;
        dir = Vector3.Normalize(dir);
        var to = P.Pos;
        for (var d = 0f; d < 7; d += 0.25f) { var next = P.Pos + dir * d; if (Blocked(next with { Y = 0 }, 0.45f) && P.Pos.Y < 3) break; to = next; }
        Effects.Add(new Fx { Kind = FxKind.Tracer, A = P.Pos + Vector3.UnitY, B = to + Vector3.UnitY, Col = WingColor(), Life = 0.3f });
        P.Pos = to;
        P.FlightTime -= 1.5f;
        AirDashCd = 2.5f;
        P.EvadeTime = MathF.Max(P.EvadeTime, 0.01f);
        Bits.Emit(P.Pos + Vector3.UnitY, 14, WingColor(), 3f, 0.06f, 0.5f, 0, glow: true);
        return true;
    }

    /// R while flying (LT + A on a pad): dive onto the locked (or nearest) Locust and crash down with a shockwave.
    public string Dive()
    {
        if (!P.Flying || P.Pos.Y < 2) return "Dive needs you in the air";
        if (DiveCd > 0) return "Dive not ready";
        var t = Locked ?? Enemies.Where(e => e.Hp > 0 && e.Emerge < 0).OrderBy(e => Dist2(e.Pos, P.Pos)).FirstOrDefault();
        if (t == null || Dist2(t.Pos, P.Pos) > 22) return "Nothing to dive on";
        _diving = true; _diveTarget = t;
        P.Flying = false;
        DiveCd = 8;
        return "";
    }

    void UpdateFlightExtras(float dt, Vector2 move)
    {
        _move = move;
        if (AirDashCd > 0) AirDashCd -= dt;
        if (DiveCd > 0) DiveCd -= dt;
        // the dive: straight down onto the target
        if (_diving)
        {
            var goal = (_diveTarget is { Hp: > 0 } dt2 ? dt2.Pos : P.Pos) with { Y = _diveTarget is { Hp: > 0 } ? 0 : FloorY };
            var to = goal - P.Pos;
            var step = MathF.Min(to.Length(), 28 * dt);
            if (to.Length() > 0.01f) P.Pos += Vector3.Normalize(to) * step;
            if (_rng.NextDouble() < 0.6) Bits.Emit(P.Pos + Vector3.UnitY, 2, WingColor(), 1f, 0.07f, 0.4f, 0, glow: true);
            if (Up <= 0.05f || to.Length() < 0.3f) { _diving = false; P.Pos.Y = MathF.Max(P.Pos.Y, FloorAt(P.Pos, StepUp)); Shockwave(4.5f, 260 + 30 * P.Level, "airborne"); }
            return;
        }
        // flight boost: sprint in the air flies faster and burns the wings faster
        if (P.Flying && P.Sprinting) P.FlightTime -= dt;
        // gliding: out of the air but holding forward, you drift down slowly
        Gliding = !P.Flying && Up > 0.4f && move.Y > 0.3f;
        if (Up > 3) _fallFrom = MathF.Max(_fallFrom, Up);
        if (!P.Flying && Up <= 0.05f && _fallFrom > 4)
        {
            if (!Gliding) Shockwave(3f, 60 + 8 * P.Level, "knockback");
            Sfx.PlayGears("hero", "land", 0.9f);
            // a long drop ends in a roll
            if (!Gliding && _assets.Characters.TryGetValue(HeroId, out var hero) && hero.Has(Clip.Evade)) { PlayOnce(Clip.Evade); Rolls++; }
            Bits.Emit(P.Pos + Vector3.UnitY * 0.2f, 18, new Color(130, 120, 105, 170), 2.2f, 0.16f, 0.8f, -0.2f, grow: 0.6f);
            _fallFrom = 0;
        }
        if (P.Flying) _fallFrom = MathF.Max(_fallFrom, Up);
        // wind streams: stand or fly into one to fill your wings and be lifted
        foreach (var w in WindStreams)
        {
            if (_rng.NextDouble() < dt * 30)
            {
                var a = (float) _rng.NextDouble() * MathF.Tau;
                Bits.Emit(w + new Vector3(MathF.Cos(a) * 1.2f, (float) _rng.NextDouble() * 2, MathF.Sin(a) * 1.2f), 1, new Color(170, 255, 230, 160), 0.3f, 0.06f, 1.4f, -6f, glow: true,
                    push: new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * 3);
            }
            if (Dist2(P.Pos, w) < 1.8f && P.FlightTime < Sheets.FlightSeconds - 0.1f)
            {
                P.FlightTime = MathF.Min(Sheets.FlightSeconds, P.FlightTime + Sheets.FlightSeconds * dt);
                if (!P.Flying && P.FlightTime > 2) { P.Flying = true; FlightNote = "Wind stream: wings refilled"; }
            }
        }
    }

    /// Glide: falling slowly instead of dropping.
    float FallSpeedMult => Gliding ? 0.3f : _diving ? 0 : 1;

    void Shockwave(float radius, float power, string cc)
    {
        foreach (var e in Enemies.ToList())
            if (Dist2(e.Pos, P.Pos) <= radius + e.Radius)
            {
                _lastSource = "Landing shockwave";
                Damage(e, power * DamageMult, false);
                if (e.Hp > 0) ApplyCc(e, cc, cc == "airborne" ? 1.2f : 0.6f);
            }
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos with { Y = 0 }, Radius = radius, Col = WingColor(), Life = 0.5f });
        Bits.Emit(P.Pos + Vector3.UnitY * 0.2f, 30, new Color(140, 125, 110, 200), 4f, 0.25f, 1.0f, 2f, grow: 0.6f);
        AddShake(0.4f);
    }

    /// Juggles: an airborne Locust hit by a hero who is also in the air stays up a bit longer.
    void Juggle(Enemy e)
    {
        if (e.Air <= 0 || P.Pos.Y < 1 || IsBoss(e)) return;
        e.Air = MathF.Min(e.Air + 0.5f, 2.5f); e.AirTotal = MathF.Max(e.AirTotal, e.Air); e.Stun = MathF.Max(e.Stun, e.Air);
        AddNumber(e.Pos + new Vector3(0.4f, e.Def.Size + 1.2f, 0), "Juggle!", new Color(170, 230, 255, 255), 0.7f);
    }

    void DrawWindStreams()
    {
        foreach (var w in WindStreams)
        {
            Glow.Decal(w, 1.6f, new Color(150, 255, 220, 120), ring: true);
            Glow.Beam(w + Vector3.UnitY * 0.2f, w + Vector3.UnitY * 9, 0.6f, new Color(150, 255, 220, 30));
        }
    }

    // ---------------------------------------------------------------- wing skins

    public sealed record WingSkin(string Id, string Name, Color Col, string Needs);
    public static readonly WingSkin[] WingSkins =
    {
        new("class", "Class colours", new Color(0, 0, 0, 0), ""),
        new("ember", "Ember wings", new Color(255, 120, 40, 255), "kills-100"),
        new("frost", "Frost wings", new Color(150, 210, 255, 255), "wave-3"),
        new("void", "Void wings", new Color(120, 60, 200, 255), "win"),
        new("radiant", "Radiant wings", new Color(255, 225, 130, 255), "level-10"),
        new("verdant", "Verdant wings", new Color(110, 230, 120, 255), "flawless-wave"),
        new("abyssal", "Abyssal wings", new Color(200, 40, 80, 255), "kills-500"),
    };
    public static bool WingUnlocked(WingSkin s) => s.Needs == "" || Profile.P.Achievements.Contains(s.Needs);
}
