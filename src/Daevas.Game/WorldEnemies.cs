using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// A COG soldier fighting beside you (Options > COG squadmate): follows, fires a Lancer, can go down and be revived.
sealed class Squadmate
{
    public readonly AnimBlend Blend = new();
    public Vector3 Pos;
    public float Yaw, Hp, MaxHp, FireCd, Down, Revive, WalkPhase;
    public bool Moving;
    public required string Character;
}

/// Smarter Locust: squads that come up together, elites rallying the others, big Locust that stagger, Drones that
/// take cover, Wretches that climb walls after a flying Daeva, and Kryll swarms in the dark. Plus the squadmate.
sealed partial class World
{
    // ---------------------------------------------------------------- formations

    Vector3? _lastSpot;
    float _lastSpotTime;
    int _squadCount;

    /// Spawns come up in squads of three around one spot.
    Vector3? FormationSpot(EnemyRow def)
    {
        if (_lastSpot is { } last && TimeAlive - _lastSpotTime < 1.5f && _squadCount < 3)
        {
            for (var tries = 0; tries < 8; tries++)
            {
                var at = last + new Vector3(((float) _rng.NextDouble() - 0.5f) * 5, 0, ((float) _rng.NextDouble() - 0.5f) * 5);
                if (!Blocked(at, MathF.Max(0.8f, def.Size * 0.3f))) { _squadCount++; _lastSpotTime = TimeAlive; return at; }
            }
        }
        var spot = SpawnSpot(def);
        if (spot != null) { _lastSpot = spot; _lastSpotTime = TimeAlive; _squadCount = 1; }
        return spot;
    }

    // ---------------------------------------------------------------- rally banners and stagger

    readonly List<Vector3> _rally = new();

    /// Elites carry a banner: Locust near one hit harder.
    float RallyMult(Enemy e) => _rally.Any(r => Dist2(r, e.Pos) < 8) ? 1.15f : 1;

    void UpdateRally()
    {
        _rally.Clear();
        foreach (var e in Enemies) if (e.Elite && e.Hp > 0) _rally.Add(e.Pos);
    }

    /// Big Locust and bosses build up stagger as they're hit; when it fills they reel for a moment.
    void AddStagger(Enemy e, float dmg)
    {
        if (!(IsBoss(e) || e.Def.Size > 2.2f) || e.Hp <= 0) return;
        e.Stagger += dmg / e.MaxHp * (IsBoss(e) ? 6 : 4);
        if (e.Stagger < 1) return;
        e.Stagger = 0;
        e.Stun = MathF.Max(e.Stun, IsBoss(e) ? 2f : 2.5f);
        Interrupt(e);
        AddNumber(e.Pos + new Vector3(0, e.Def.Size + 1.3f, 0), "Staggered!", new Color(255, 230, 120, 255), 1f);
        AddShake(0.2f);
    }

    // ---------------------------------------------------------------- Drones in cover

    Vector3? CoverSpot(Enemy e)
    {
        Vector3? best = null; var bestD = 9f;
        IEnumerable<BoundingBox> boxes = Cover;
        if (_assets.City != null) boxes = boxes.Concat(_assets.City.Colliders);
        foreach (var b in boxes)
        {
            var c = (b.Min + b.Max) / 2;
            if (Dist2(c, e.Pos) > 9 || b.Max.Y - b.Min.Y < 0.8f) continue;
            var half = MathF.Max(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z) / 2;
            if (half > 4) continue;
            var spot = (c + Flat(c - P.Pos) * (half + 0.9f)) with { Y = 0 };
            var d = Dist2(spot, e.Pos);
            if (d < bestD && Dist2(spot, P.Pos) < e.Def.AttackRange * 0.9f && Dist2(spot, P.Pos) > 6 && !Blocked(spot, 0.5f)) { best = spot; bestD = d; }
        }
        return best;
    }

    /// Ranged Drones head for cover between them and you; tucked in, they take less damage.
    Vector3? CoverStep(Enemy e)
    {
        if (!e.Def.Ranged || e.Def.FlyHeight > 0 || e.Def.Special is "boss" or "seed" || !e.Def.Character.StartsWith("drone") && e.Def.Character is not ("siegebreaker" or "grenadier")) return null;
        if ((e.CoverTimer -= 0.016f) <= 0) { e.CoverTimer = 2; e.CoverAt = CoverSpot(e); }
        if (e.CoverAt is not { } spot) { e.InCover = false; return null; }
        e.InCover = Dist2(spot, e.Pos) < 1.2f;
        return e.InCover ? Vector3.Zero : Flat(spot - e.Pos);
    }

    // ---------------------------------------------------------------- Wretches up the walls

    /// When you fly, Wretches near a wall climb it and pounce from the top.
    bool ClimbAfter(Enemy e, float dt, float dist)
    {
        if (e.Def.Character is not ("wretch" or "wretch-armored") || P.Pos.Y < 2.5f || dist > 9) { if (e.Pos.Y > 0 && e.Def.FlyHeight == 0) e.Pos.Y = MathF.Max(0, e.Pos.Y - 9 * dt); return false; }
        var wall = false;
        for (var i = 0; i < 8 && !wall; i++)
        {
            var a = i / 8f * MathF.Tau;
            wall = Blocked(e.Pos with { Y = 0 } + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 1.2f, 0.3f);
        }
        if (!wall) return false;
        e.Pos.Y = MathF.Min(P.Pos.Y, e.Pos.Y + 3.5f * dt);
        e.Moving = true;
        if (e.Pos.Y >= P.Pos.Y - 0.5f && dist < 4 && e.AttackCd <= 0)
        {
            // the pounce
            e.AttackCd = e.Def.AttackInterval; e.AttackAnim = 0;
            TakeDamage(EnemyDamage(e) * 1.3f, melee: true);
            e.Pos = (P.Pos with { Y = P.Pos.Y - 0.5f }) - Flat(P.Pos - e.Pos) * 1.2f;
            AddNumber(e.Pos + new Vector3(0, 2.2f, 0), "Pounce!", new Color(255, 140, 100, 255), 0.8f);
        }
        return true;
    }

    // ---------------------------------------------------------------- Kryll

    public bool KryllNear;
    /// Seconds out of the light at night (the Kryll bite after KryllGrace).
    public float DarkTime;
    public const float KryllGrace = 3;

    /// The nearest lamp or fire (where the Kryll won't come), if any.
    public Vector3? NearestLight()
    {
        var lights = _fires.Concat(_assets.City?.Lamps.Select(l => l with { Y = 0 }) ?? Enumerable.Empty<Vector3>());
        return lights.Any() ? lights.OrderBy(l => Dist2(l, P.Pos)).First() : null;
    }
    float _kryllHurt;

    /// At night the Kryll swarm anyone out of the light (a lamp or a fire nearby keeps them off).
    void UpdateKryll(float dt)
    {
        KryllNear = false;
        if (!Env.Night || Mode is GameMode.Practice or GameMode.Tutorial || Won || Lost) return;
        var lit = _fires.Any(f => Dist2(f, P.Pos) < 5) || (_assets.City?.Lamps.Any(l => Dist2(l, P.Pos) < 7) ?? false) || Transform > 0;
        if (lit) { DarkTime = 0; return; }
        KryllNear = true;
        DarkTime += dt;
        for (var i = 0; i < 3; i++)
        {
            var a = (float) _rng.NextDouble() * MathF.Tau;
            var r = 2 + (float) _rng.NextDouble() * 4;
            Bits.Emit(P.Pos + new Vector3(MathF.Cos(a) * r, 1.5f + (float) _rng.NextDouble() * 3, MathF.Sin(a) * r), 1, new Color(14, 12, 16, 200), 0.6f, 0.09f, 0.7f, 0, push: new Vector3(-MathF.Sin(a), 0.2f, MathF.Cos(a)) * 6);
        }
        // the swarm gathers for a few seconds first (time to get into the light), then bites harder the longer you stay out
        if (DarkTime < KryllGrace || (_kryllHurt -= dt) > 0) return;
        _kryllHurt = 0.5f;
        if (P.EvadeTime > 0) return;
        var bite = P.MaxHp * Math.Clamp(0.01f + (DarkTime - KryllGrace) * 0.0005f, 0.01f, 0.015f) * 0.5f * DifficultyDamage;
        P.Hp -= bite; DamageTaken += bite; P.HurtFlash = 0.1f;
        if (P.Hp <= 0) { P.Hp = 1; TakeDamage(2); }
    }

    // ---------------------------------------------------------------- the COG squadmate

    public Squadmate? Squad;

    void PlaceSquadmate()
    {
        if (!Settings.S.Squadmate || Mode is GameMode.Practice or GameMode.Tutorial) return;
        var who = P.Cls.Character == "dom" || HeroId == "dom" ? "marcus" : "dom";
        if (!_assets.Characters.ContainsKey(who)) return;
        Squad = new Squadmate { Character = who, Pos = P.Pos - P.Forward * 2 + P.Right * 1.5f, Hp = 400 * DifficultyHp, MaxHp = 400 * DifficultyHp, FireCd = 1 };
    }

    void UpdateSquadmate(float dt)
    {
        if (Squad is not { } s) return;
        if (s.Down > 0)
        {
            // down: you revive them by standing close
            s.Down -= dt;
            if (Dist2(P.Pos, s.Pos) < 2.5f) s.Revive += dt; else s.Revive = MathF.Max(0, s.Revive - dt);
            if (s.Revive >= 2) { s.Down = 0; s.Hp = s.MaxHp * 0.5f; s.Revive = 0; Say($"{Sheets.Characters.First(c => c.Id == s.Character).Name} is back up", new Color(140, 200, 255, 255)); }
            if (s.Down <= 0 && s.Hp <= 0) { s.Down = 0.01f; }
            return;
        }
        var target = Enemies.Where(e => e.Hp > 0 && e.Emerge < 0 && e.Burrow <= 0 && Dist2(e.Pos, s.Pos) < 30 && HasLineOfSight(s.Pos, e.Pos)).OrderBy(e => Dist2(e.Pos, s.Pos)).FirstOrDefault();
        var want = P.Pos - P.Forward * 2.5f + P.Right * 2f;
        var step = want - s.Pos; step.Y = 0;
        var before = s.Pos;
        if (step.Length() > 1.2f) MoveCircle(ref s.Pos, Vector3.Normalize(step) * MathF.Min(step.Length(), 5.2f * dt), 0.45f);
        s.Moving = Vector3.Distance(before, s.Pos) > 0.01f;
        s.WalkPhase += Vector3.Distance(before, s.Pos) / 3f;
        var look = target != null ? target.Pos - s.Pos : P.Forward;
        s.Yaw = LerpAngle(s.Yaw, MathF.Atan2(look.X, look.Z), MathF.Min(1, dt * 8));
        if (target != null && (s.FireCd -= dt) <= 0)
        {
            s.FireCd = 0.9f;
            _lastSource = "Squadmate";
            Damage(target, (22 + 4 * Wave) * DifficultyHp, false);
            Effects.Add(new Fx { Kind = FxKind.Tracer, A = s.Pos + new Vector3(0, 1.4f, 0), B = target.Centre, Col = new Color(255, 220, 150, 255), Life = 0.1f });
            Sfx.PlayGearsAt("lancer", "fire", s.Pos, 0.35f * Falloff(s.Pos));
            if (_rng.NextDouble() < 0.4) Mark(s.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 1.2f, 0, ((float) _rng.NextDouble() - 0.5f) * 1.2f), 0.05f, new Color(200, 160, 70, 200), 40);
        }
        // Locust in reach claw at them
        foreach (var e in Enemies)
            if (!e.Def.Ranged && e.Hp > 0 && e.Emerge < 0 && Dist2(e.Pos, s.Pos) < 1.8f) s.Hp -= 8 * DifficultyDamage * dt;
        if (s.Hp <= 0) { s.Hp = 0; s.Down = 30; Say($"{Sheets.Characters.First(c => c.Id == s.Character).Name} is down! Stand by them to revive", new Color(255, 160, 140, 255)); }
    }

    void DrawSquadmate()
    {
        if (Squad is not { } s || !_assets.Characters.TryGetValue(s.Character, out var cm)) return;
        DrawCharacter(cm, s.Moving && s.Down <= 0 ? Clip.Walk : Clip.Aim, s.Moving ? s.WalkPhase : (float) Raylib.GetTime() / MathF.Max(0.1f, cm.Duration[Clip.Aim]), s.Pos + new Vector3(0, GroundAt(s.Pos), 0), s.Yaw, s.Down > 0 ? -1.35f : 0, Color.White, 1, s.Blend);
        if (ShadowPass) return;
        if (s.Down > 0) Glow.Decal(s.Pos, 1.4f + 0.2f * MathF.Sin((float) Raylib.GetTime() * 6), new Color(255, 90, 70, 180), ring: true);
        else Glow.Decal(s.Pos, 0.8f, new Color(90, 160, 255, 90), ring: true);
    }
}
