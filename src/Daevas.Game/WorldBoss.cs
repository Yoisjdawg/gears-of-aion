using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// A danger zone on the street: it fills up, then whatever is inside gets hit (bosses' slams, grenades,
/// beams, a Corpser coming up from below). Evading at the right moment avoids it.
sealed class Telegraph
{
    public Vector3 Pos, From;
    public float Radius, Time, Total, Damage;
    public bool Line;          // a beam from From to Pos, Radius wide
    public bool Arc;           // a grenade: draw it flying from From
    public string Kind = "slam";
    public Enemy? Owner;       // stunning the owner before it lands cancels it
    public string Cc = "";     // what it does to the hero: knockdown, airborne
    public Color Col = new(255, 60, 40, 255);
}

/// A boss's loot chest: walk up to it (or clear the wave) to open it.
sealed class Chest
{
    public Vector3 Pos;
    public float Age;
    public bool Opened;
    public int Tier;
}

/// Bosses (phases, telegraphed attacks, enrage, intro and defeat), the special Locust (healers, Seeders,
/// snipers, grenadiers, Brood Shifters, Flooders), elites and spawn telegraphs.
sealed partial class World
{
    public readonly List<Telegraph> Telegraphs = new();
    public readonly List<Chest> Chests = new();
    public Enemy? Boss;
    /// Seconds left of the boss intro (the camera turns to it) and of the defeat slow motion.
    public float BossIntro, SlowMo;
    (EnemyRow def, float delay)? _bossPending;
    readonly List<(EnemyRow def, Vector3 pos, float time)> _pendingSpawns = new();
    public float PoisonTime, PoisonDps;
    float _autoEvadeCd;

    public static bool IsBoss(Enemy e) => e.Def.Special == "boss";
    public int EnemyLevel(Enemy e) => 1 + Wave + (IsBoss(e) ? 3 : e.Elite ? 1 : 0);

    /// Extras and the boss from the wave sheet.
    void QueueWaveExtras(WaveRow w)
    {
        var extra = new List<EnemyRow>();
        if (w.Extra != "none")
            foreach (var pair in w.Extra.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var (id, n) = (pair.Split(':')[0], int.Parse(pair.Split(':')[1]));
                for (var i = 0; i < n; i++) extra.Add(Sheets.Enemies.First(e => e.Id == id));
            }
        // the extras join the queue, mixed in after the opening few
        var rest = _spawnQueue.ToList();
        _spawnQueue.Clear();
        foreach (var e in rest.Take(3).Concat(rest.Skip(3).Concat(extra).OrderBy(_ => _rng.Next()))) _spawnQueue.Enqueue(e);
        _bossPending = w.Boss != "none" ? (Sheets.Enemies.First(e => e.Id == w.Boss), 16f) : null;
    }

    bool WaveExtrasDone => _bossPending == null && _pendingSpawns.Count == 0;

    /// Spawning goes through a short telegraph: the street cracks and glows where a Locust will come up.
    void QueueSpawn(EnemyRow def)
    {
        if (def.FlyHeight > 0 || def.Special == "boss") { Spawn(def); return; }
        var pos = FormationSpot(def);
        if (pos == null) return;
        _pendingSpawns.Add((def, pos.Value, 1.1f));
        Sfx.PlayGears(def.Id, "spawn", Falloff(pos.Value) * 0.5f);
    }

    void UpdateBossAndSpecials(float dt)
    {
        if (BossIntro > 0) BossIntro -= dt;
        if (SlowMo > 0) SlowMo -= dt;
        if (Boss != null && !Enemies.Contains(Boss)) Boss = null;
        // pending spawns: rumble, then up they come
        for (var i = _pendingSpawns.Count - 1; i >= 0; i--)
        {
            var (def, pos, t) = _pendingSpawns[i];
            t -= dt;
            if (_rng.NextDouble() < dt * 20) Bits.Emit(pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 1.6f, 0.1f, ((float) _rng.NextDouble() - 0.5f) * 1.6f), 1, new Color(110, 96, 84, 200), 1.4f, 0.08f, 0.6f, 6f);
            if (t <= 0) { _pendingSpawns.RemoveAt(i); SpawnAt(def, pos); }
            else _pendingSpawns[i] = (def, pos, t);
        }
        if (_bossPending is { } bp && WaveActive)
        {
            var d = bp.delay - dt;
            if (d <= 0 || (_spawnQueue.Count == 0 && Enemies.Count <= 2)) { _bossPending = null; SpawnBoss(bp.def); }
            else _bossPending = (bp.def, d);
        }
        // acid on the hero
        if (PoisonTime > 0 && !Lost)
        {
            PoisonTime -= dt;
            P.Hp -= PoisonDps * dt * GearDefence;
            DamageTaken += PoisonDps * dt;
            if (_rng.NextDouble() < dt * 10) Bits.Emit(P.Pos + new Vector3(0, 1.2f, 0), 1, new Color(140, 220, 60, 255), 0.8f, 0.06f, 0.6f, 3f, glow: true);
            if (P.Hp <= 0) { P.Hp = 1; PoisonTime = 0; TakeDamage(1); }
        }
        // telegraphs fill, then strike (the auto-evade assist rolls you out of one at the last moment)
        if (_autoEvadeCd > 0) _autoEvadeCd -= dt;
        for (var i = Telegraphs.Count - 1; i >= 0; i--)
        {
            var t = Telegraphs[i];
            t.Time += dt;
            if (Settings.S.AutoEvade && _autoEvadeCd <= 0 && t.Total - t.Time < 0.3f && !t.Line && Dist2(P.Pos, t.Pos) < t.Radius && P.EvadeCd <= 0)
            {
                var away = Flat(P.Pos - t.Pos);
                if (away.LengthSquared() < 0.01f) away = P.Forward;
                var local = new Vector2(Vector3.Dot(away, P.Right), Vector3.Dot(away, P.Forward));
                Evade(local);
                _autoEvadeCd = 6;
            }
            if (t.Time < t.Total) continue;
            Telegraphs.RemoveAt(i);
            StrikeTelegraph(t);
        }
        foreach (var c in Chests)
        {
            c.Age += dt;
            if (!c.Opened && (Dist2(c.Pos, P.Pos) < 2.6f || (!WaveActive && c.Age > 1))) OpenChest(c);
        }
        Chests.RemoveAll(c => c.Opened && c.Age > 30);
        foreach (var e in Enemies.ToList()) if (e.Hp > 0 && e.Emerge < 0) UpdateSpecial(e, dt);
    }

    void StrikeTelegraph(Telegraph t)
    {
        bool Inside(Vector3 p) => t.Line ? DistToSegment(p, t.From with { Y = 0 }, t.Pos with { Y = 0 }) <= t.Radius : Dist2(p, t.Pos) <= t.Radius;
        if (Inside(P.Pos) && (t.Kind != "slam" || P.Pos.Y < 2.5f))
        {
            var hp = P.Hp;
            TakeDamage(t.Damage);
            if (t.Cc != "" && P.Hp < hp) StunPlayer(t.Cc == "airborne" ? 1.0f : 1.4f, t.Cc == "airborne" ? "Airborne" : "Knocked down");
        }
        if (t.Kind == "acid" && Inside(P.Pos)) { PoisonTime = 4; PoisonDps = t.Damage * 0.25f; }
        var at = t.Pos with { Y = 0.1f };
        switch (t.Kind)
        {
            case "beam":
                for (var d = 0f; d < Vector3.Distance(t.From, t.Pos); d += 0.7f)
                    Bits.Emit(Vector3.Lerp(t.From, t.Pos, d / Vector3.Distance(t.From, t.Pos)), 2, new Color(200, 110, 255, 255), 2f, 0.09f, 0.5f, 0, glow: true);
                Effects.Add(new Fx { Kind = FxKind.Beam, A = t.From, B = t.Pos, Col = new Color(200, 110, 255, 255), Life = 0.5f, Radius = 0.35f });
                break;
            case "acid":
                Bits.Emit(at, 26, new Color(140, 220, 60, 255), 4f, 0.08f, 0.8f, 8f, glow: true);
                Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = t.Radius, Col = new Color(140, 220, 60, 255), Life = 0.6f });
                break;
            default:
                Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = t.Radius, Col = new Color(255, 120, 40, 255), Life = 0.5f });
                Effects.Add(new Fx { Kind = FxKind.Burst, A = at + Vector3.UnitY, Radius = t.Radius * 0.6f, Col = new Color(255, 150, 60, 255), Life = 0.45f });
                Bits.Emit(at, 30, new Color(255, 150, 50, 255), 6f, 0.1f, 0.5f, 3f, glow: true);
                Bits.Emit(at, 16, new Color(90, 80, 70, 170), 2.4f, 0.45f, 1.5f, -0.4f, grow: 0.9f);
                Bits.Emit(at, 14, new Color(60, 52, 46, 255), 7f, 0.08f, 1.0f, 14f);
                Mark(at, t.Radius * 0.7f, new Color(22, 18, 15, 170));
                DamageCover(at, t.Radius, t.Damage * 3);
                AddShake(Dist2(at, P.Pos) < t.Radius * 3 ? 0.45f : 0.15f);
                Sfx.Play(Sfx.Kill);
                break;
        }
    }

    Enemy? _warnOwner;
    void Warn(Vector3 at, float radius, float time, float damage, string kind = "slam", Vector3? from = null, bool line = false, bool arc = false, string cc = "")
    {
        Telegraphs.Add(new Telegraph { Owner = _warnOwner, Cc = cc, Pos = at with { Y = 0 }, Radius = radius, Total = time, Damage = damage, Kind = kind, From = from ?? at, Line = line, Arc = arc,
            Col = kind == "acid" ? new Color(140, 220, 60, 255) : kind == "beam" ? new Color(200, 110, 255, 255) : new Color(255, 60, 40, 255) });
    }

    // ---------------------------------------------------------------- special Locust

    void UpdateSpecial(Enemy e, float dt)
    {
        e.Age += dt;
        e.SpecialCd -= dt;
        _warnOwner = e;
        if (e.Def.Special != "boss" && (e.Silence > 0 || e.Downed > 0)) return;
        var dist = Dist2(e.Pos, P.Pos);
        switch (e.Def.Special)
        {
            case "heal" when e.SpecialCd <= 0:
            {
                e.SpecialCd = 4;
                var healed = 0;
                foreach (var o in Enemies)
                    if (o != e && o.Hp < o.MaxHp && Dist2(o.Pos, e.Pos) < 11)
                    {
                        var amount = MathF.Min(o.MaxHp * 0.15f, o.MaxHp - o.Hp);
                        o.Hp += amount;
                        AddNumber(o.Pos + new Vector3(0, o.Def.Size + 0.4f, 0), "+" + (int) amount, new Color(150, 255, 120, 255), 0.7f);
                        Effects.Add(new Fx { Kind = FxKind.Beam, A = e.Centre, B = o.Centre, Col = new Color(170, 255, 110, 255), Life = 0.5f, Radius = 0.06f, Element = "spirit" });
                        healed++;
                    }
                if (healed > 0) Effects.Add(new Fx { Kind = FxKind.Ring, A = e.Pos with { Y = 0 }, Radius = 11, Col = new Color(150, 255, 110, 160), Life = 0.7f });
                break;
            }
            case "seed" when e.SpecialCd <= 0:
            {
                e.SpecialCd = e.Def.AttackInterval;
                if (Enemies.Count(o => o.Def.Id == "nemacyst") >= 6) break;
                e.AttackAnim = 0;
                var nem = Sheets.Enemies.First(x => x.Id == "nemacyst");
                var at = e.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 2, nem.FlyHeight, ((float) _rng.NextDouble() - 0.5f) * 2);
                var n = new Enemy { Def = nem, Pos = at, Hp = nem.Hp * _waveHpScale, MaxHp = nem.Hp * _waveHpScale, AttackCd = 1, Bob = (float) _rng.NextDouble() * 5 };
                Enemies.Add(n);
                Bits.Emit(at, 14, new Color(150, 200, 90, 255), 3f, 0.1f, 0.7f, 2f, glow: true);
                break;
            }
            case "snipe":
                // the laser shows for 1.4 s before the shot (evade it, or break line of sight)
                if (e.Aim > 0)
                {
                    e.Aim -= dt;
                    if (e.Aim <= 0)
                    {
                        if (HasLineOfSight(e.Pos, P.Pos)) TakeDamage(e.Def.Damage * DifficultyDamage);
                        Effects.Add(new Fx { Kind = FxKind.Tracer, A = e.Centre + Vector3.UnitY * 0.4f, B = P.Pos + Vector3.UnitY * 1.2f, Col = new Color(255, 80, 60, 255), Life = 0.2f });
                        Sfx.Play(Sfx.Sniper);
                    }
                }
                else if (e.SpecialCd <= 0 && dist < e.Def.AttackRange && HasLineOfSight(e.Pos, P.Pos)) { e.SpecialCd = e.Def.AttackInterval; e.Aim = 1.4f; }
                e.AttackCd = 99; // its shot is the laser one
                break;
            case "grenade" when e.SpecialCd <= 0 && dist < e.Def.AttackRange && dist > 4:
                e.SpecialCd = e.Def.AttackInterval;
                e.AttackAnim = 0;
                Warn(P.Pos, e.Def.Splash, 1.7f, e.Def.Damage * DifficultyDamage, from: e.Centre + Vector3.UnitY * 0.6f, arc: true);
                e.AttackCd = 99;
                break;
            case "beam":
                if (e.SpecialCd <= 0 && dist < e.Def.AttackRange)
                {
                    e.SpecialCd = e.Def.AttackInterval;
                    e.AttackAnim = 0;
                    var dir = Flat(P.Pos - e.Pos);
                    Warn(P.Pos + dir * 6, 1.3f, 1.4f, e.Def.Damage * 3 * DifficultyDamage, "beam", from: e.Pos with { Y = 0 } + dir * 1.5f, line: true);
                }
                e.AttackCd = 99;
                break;
            case "acid" when e.SpecialCd <= 0 && dist < e.Def.AttackRange:
                e.SpecialCd = e.Def.AttackInterval;
                e.AttackAnim = 0;
                Warn(P.Pos, e.Def.Splash, 1.3f, e.Def.Damage * DifficultyDamage, "acid", from: e.Centre, arc: true);
                e.AttackCd = 99;
                break;
            case "boss":
                UpdateBoss(e, dt);
                break;
        }
    }

    // ---------------------------------------------------------------- bosses

    void SpawnBoss(EnemyRow def)
    {
        var e = Spawn(def);
        if (e == null) return;
        Boss = e;
        e.Emerge = -1;
        BossIntro = 2.6f;
        Banner($"{def.Name.ToUpperInvariant()}\n{def.Title}");
        AddShake(0.6f);
        Pad.Rumble(0.8f, 0.6f);
        Bits.Emit(e.Pos + Vector3.UnitY * 0.3f, 60, new Color(110, 96, 84, 220), 5f, 0.3f, 1.8f, 4f);
        Holes.Add(new EHole { Pos = e.Pos, Radius = 1.4f + def.Size * 0.3f, Life = 8, Seed = _rng.Next() });
        Sfx.PlayGears(def.Id, "spawn", 1);
        Sfx.PlayGears("music", "boss-sting", 1);
        Audio.Duck(3);
        Say($"{def.Name} has emerged!", new Color(255, 120, 90, 255));
    }

    void UpdateBoss(Enemy e, float dt)
    {
        var frac = e.Hp / e.MaxHp;
        var phase = frac < 0.33f ? 2 : frac < 0.66f ? 1 : 0;
        if (phase > e.Phase)
        {
            // a new phase: it roars, calls for help, the street catches fire and it speeds up
            e.Phase = phase;
            Banner(phase == 1 ? $"{e.Def.Name} is enraged!" : $"{e.Def.Name}: final stand!");
            AddShake(0.5f);
            e.SpeedMult = 1 + phase * 0.2f;
            for (var i = 0; i < 2 + phase * 2; i++) _spawnQueue.Enqueue(Sheets.Enemies.First(x => x.Id == (i % 3 == 2 ? "ticker" : "wretch")));
            for (var i = 0; i < 2; i++) _fires.Add(e.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 10, 0, ((float) _rng.NextDouble() - 0.5f) * 10));
            Effects.Add(new Fx { Kind = FxKind.Ring, A = e.Pos with { Y = 0 }, Radius = 9, Col = new Color(255, 90, 40, 255), Life = 0.8f });
        }
        if (!e.Enraged && e.Age > 150)
        {
            e.Enraged = true;
            Banner($"{e.Def.Name} goes berserk!");
            e.SpeedMult += 0.3f;
        }
        if (e.Burrow > 0)
        {
            e.Burrow -= dt;
            if (e.Burrow <= 0)
            {
                // the Corpser bursts up where the telegraph was
                e.Pos = e.BurrowTo; e.Emerge = 0;
                Bits.Emit(e.Pos, 50, new Color(110, 96, 84, 220), 6f, 0.25f, 1.6f, 6f);
            }
            return;
        }
        if (e.SpecialCd > 0 || e.Silence > 0 || e.Stun > 0) return;
        var dmg = e.Def.Damage * DifficultyDamage * (e.Enraged ? 1.5f : 1);
        var gap = ((e.Enraged ? 4.5f : 7f) - e.Phase) * (Settings.S.Difficulty >= 2 ? 0.75f : 1);
        if (Settings.S.Difficulty >= 2 && e.Phase > 0 && _rng.NextDouble() < 0.5) Warn(P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 6, 0, ((float) _rng.NextDouble() - 0.5f) * 6), 3f, 1.6f, dmg * 0.8f, from: e.Centre + Vector3.UnitY, arc: true);
        switch (e.Def.Id)
        {
            case "vrak":
            case "vrak-scorched":
                if (_rng.NextDouble() < 0.5)
                {
                    // a leaping slam at you
                    Warn(P.Pos, 4f, 1.5f, dmg * 1.4f);
                }
                else
                {
                    // triple grenades around you
                    for (var i = 0; i < 3; i++)
                    {
                        var a = i * MathF.Tau / 3 + (float) _rng.NextDouble();
                        Warn(P.Pos + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (i == 0 ? 0 : 3.2f), 3f, 1.8f + i * 0.2f, dmg, from: e.Centre + Vector3.UnitY, arc: true);
                    }
                }
                if (e.Def.Id == "vrak-scorched" && e.Phase > 0) Warn(e.Pos, 6f, 1.6f, dmg * 0.8f);
                e.AttackAnim = 0;
                break;
            case "corpser":
                if (_rng.NextDouble() < 0.45 + e.Phase * 0.1f)
                {
                    // dive under the street and come up beneath you
                    e.Burrow = 2.0f; e.BurrowTo = P.Pos with { Y = 0 };
                    Warn(P.Pos, 4.5f, 2.0f, dmg * 1.6f, cc: "airborne");
                    Bits.Emit(e.Pos, 40, new Color(110, 96, 84, 220), 5f, 0.25f, 1.4f, 6f);
                    e.Pos = e.Pos with { Y = -8 };
                }
                else Warn(e.Pos + Flat(P.Pos - e.Pos) * 3, 3.5f, 1.2f, dmg);
                break;
            case "brumak":
                if (_rng.NextDouble() < 0.5) Warn(e.Pos, 7.5f, 1.8f, dmg * 1.3f, cc: "knockdown"); // stomp
                else
                    for (var i = 0; i < 4 + e.Phase * 2; i++)
                    {
                        // a rocket barrage around you
                        var off = new Vector3(((float) _rng.NextDouble() - 0.5f) * 10, 0, ((float) _rng.NextDouble() - 0.5f) * 10);
                        Warn(P.Pos + (i == 0 ? Vector3.Zero : off), 2.8f, 1.5f + i * 0.18f, dmg * 0.7f, from: e.Centre + Vector3.UnitY * 2, arc: true);
                    }
                e.AttackAnim = 0;
                break;
            default:
                Warn(P.Pos, 3.5f, 1.4f, dmg);
                break;
        }
        e.SpecialCd = gap;
    }

    void OnBossKilled(Enemy e)
    {
        Progression.Count("bosses");
        SlowMo = 1.6f;
        Banner($"{e.Def.Name.ToUpperInvariant()} DEFEATED");
        AddShake(0.8f);
        Pad.Rumble(1f, 0.8f);
        Bits.Emit(e.Centre, 90, new Color(255, 170, 70, 255), 9f, 0.12f, 1.2f, 3f, glow: true);
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = e.Pos with { Y = 0 }, Radius = 3, Col = new Color(255, 215, 90, 255), Life = 2f });
        Chests.Add(new Chest { Pos = e.Pos with { Y = 0 }, Tier = e.Def.Hp >= 5000 ? 2 : 1 });
        Telegraphs.Clear();
        Say($"{e.Def.Name} dropped a war chest", new Color(255, 214, 110, 255));
    }

    void OpenChest(Chest c)
    {
        c.Opened = true; c.Age = 0;
        var rar = c.Tier >= 2 ? Rarity.Eternal : Rarity.Fabled;
        if (_rng.NextDouble() < 0.2) rar++;
        var it = Profile.MakeItem(_rng, P.Cls.Id, P.Level, rar, set: Profile.Sets[_rng.Next(Profile.Sets.Length)].Name);
        Profile.P.Bag.Add(it);
        var kinah = 400 * c.Tier + _rng.Next(200);
        Profile.P.Kinah += kinah;
        Profile.AddMat("enchant", 2 * c.Tier);
        Profile.AddMat("imulsion", c.Tier);
        Banner($"WAR CHEST\n{it.Name}");
        Say($"War chest: {it.Name}, {kinah} Kinah, {2 * c.Tier} enchantment stones", Profile.RarityColor(rar));
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = c.Pos, Radius = 1.5f, Col = Profile.RarityColor(rar), Life = 1.5f });
        Bits.Emit(c.Pos + Vector3.UnitY, 40, Profile.RarityColor(rar), 4f, 0.08f, 1.2f, -1f, glow: true);
        Achieve("fabled-item");
        Profile.Save();
        Sfx.Play(Sfx.LevelUp);
    }

    // ---------------------------------------------------------------- drawing

    void DrawTelegraphs()
    {
        foreach (var t in Telegraphs)
        {
            var k = Math.Clamp(t.Time / t.Total, 0, 1);
            var pulse = 0.75f + 0.25f * MathF.Sin(t.Time * 18);
            if (t.Line)
            {
                var a = t.From with { Y = 0.08f }; var b = t.Pos with { Y = 0.08f };
                Glow.Beam(a, b, t.Radius * (0.3f + 0.7f * k), Fade(t.Col, 0.35f * pulse));
                Glow.Beam(a, b, 0.05f, Fade(t.Col, 0.9f));
                continue;
            }
            // an outline from the start and a fill that grows to it: when the fill reaches the edge, it hits
            Glow.Decal(t.Pos, t.Radius, Fade(t.Col, 0.7f * pulse), ring: true);
            Glow.Decal(t.Pos, t.Radius * k, Fade(t.Col, 0.28f + 0.2f * k), ring: false);
            if (t.Arc)
            {
                // the lobbed grenade or shell on its way
                var p = Vector3.Lerp(t.From, t.Pos, k);
                p.Y += MathF.Sin(k * MathF.PI) * MathF.Max(3, Vector3.Distance(t.From, t.Pos) * 0.35f) + (1 - k) * t.From.Y;
                Glow.Sprite(p, 0.35f, t.Kind == "acid" ? new Color(150, 230, 70, 230) : new Color(255, 170, 80, 230));
            }
        }
        foreach (var e in Enemies)
        {
            if (e.Def.Special == "snipe" && e.Aim > 0)
            {
                var from = e.Centre + Vector3.UnitY * 0.4f; var to = P.Pos + Vector3.UnitY * 1.2f;
                Glow.Beam(from, to, 0.02f + 0.03f * (1 - e.Aim / 1.4f), new Color(255, 40, 30, 200));
                Glow.Sprite(to, 0.25f, new Color(255, 60, 40, 220));
            }
            if (e.Elite && !ShadowPass)
            {
                Glow.Decal(e.Pos, e.Radius + 0.7f, new Color(255, 200, 80, 120), ring: true);
                // the rally banner over it, and its reach
                Glow.Beam(e.Pos + new Vector3(0, e.Def.Size, 0), e.Pos + new Vector3(0, e.Def.Size + 1.4f, 0), 0.03f, new Color(255, 200, 120, 200));
                Glow.Sprite(e.Pos + new Vector3(0.25f, e.Def.Size + 1.2f, 0), 0.35f, new Color(220, 40, 30, 220));
                Glow.Decal(e.Pos, 8, new Color(220, 60, 40, 40), ring: true);
            }
        }
        foreach (var (def, pos, time) in _pendingSpawns)
        {
            var k = 1 - time / 1.1f;
            Glow.Decal(pos, 0.6f + k * 1.2f, new Color(255, 110, 50, (int) (90 + 120 * k)), ring: true);
            Glow.Decal(pos, 0.5f + k, new Color(200, 70, 30, 90), ring: false);
        }
        foreach (var c in Chests)
        {
            if (c.Opened) continue;
            var bob = MathF.Sin(c.Age * 3) * 0.1f;
            Raylib.DrawCube(c.Pos + new Vector3(0, 0.45f + bob, 0), 1.2f, 0.8f, 0.8f, new Color(90, 62, 30, 255));
            Raylib.DrawCube(c.Pos + new Vector3(0, 0.9f + bob, 0), 1.25f, 0.18f, 0.85f, new Color(200, 160, 60, 255));
            Glow.Sprite(c.Pos + new Vector3(0, 1.0f, 0), 1.6f, new Color(255, 200, 80, 120));
            Glow.Decal(c.Pos, 1.8f, new Color(255, 200, 80, 150), ring: true);
        }
    }
}
