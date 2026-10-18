using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// Marked ground that detonates (Delayed Blast) and holy ground (Sanctuary).
sealed class GroundSpell
{
    public required SkillRow Skill;
    public Vector3 Pos;
    public float Time, Life, Power, Tick;
    public Color Col;
}

/// Class mechanics: Gladiator Wrath, stances, Assassin runes, coatings and backstabs, Ranger focus, Chanter combo
/// strikes, pets that Locust can hit (and Clerics can raise), spirit commands, delayed blasts and holy ground.
sealed partial class World
{
    public readonly List<GroundSpell> Grounds = new();

    // ---------------------------------------------------------------- Gladiator Wrath

    public float Wrath;
    bool HasWrath => P.Cls.Id == "gladiator";

    void GainWrath(float n)
    {
        if (!HasWrath || Wrath >= 100) return;
        Wrath = MathF.Min(100, Wrath + n);
        if (Wrath >= 100) AddNumber(P.Pos + new Vector3(0, 2.5f, 0), "WRATH", new Color(255, 80, 50, 255), 1.1f);
    }

    float SpendWrath()
    {
        if (!HasWrath || Wrath < 100) return 1;
        Wrath = 0;
        AddShake(0.3f);
        Bits.Emit(P.Pos + Vector3.UnitY, 30, new Color(255, 80, 40, 255), 4f, 0.08f, 0.7f, 0, glow: true);
        return 1.5f;
    }

    // ---------------------------------------------------------------- stances (G)

    public bool StanceOn;
    public static (string name, float dmg, float taken, float block, float speed)? StanceOf(string cls) => cls switch
    {
        "gladiator" => ("Berserk Stance", 0.2f, 0.15f, 0, 0),
        "templar" => ("Shield Stance", -0.1f, -0.1f, 15, 0),
        "chanter" => ("Mantra of Battle", 0.1f, 0, 0, 0.1f),
        _ => null,
    };

    public string ToggleStance()
    {
        if (StanceOf(P.Cls.Id) is not { } st) return "Your class has no stance";
        StanceOn = !StanceOn;
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 2.2f, Col = Hex(P.Cls.Color), Life = 0.5f });
        return StanceOn ? $"{st.name} on" : $"{st.name} off";
    }

    float StanceDamage => StanceOn && StanceOf(P.Cls.Id) is { } s ? s.dmg : 0;
    float StanceTaken => StanceOn && StanceOf(P.Cls.Id) is { } s ? 1 + s.taken : 1;
    float StanceBlock => StanceOn && StanceOf(P.Cls.Id) is { } s ? s.block : 0;
    float StanceSpeed => StanceOn && StanceOf(P.Cls.Id) is { } s ? 1 + s.speed : 1;

    // ---------------------------------------------------------------- Assassin runes, coatings and backstabs; Ranger focus

    public float CoatTime, StillTime;

    /// Extra damage on a hit from class mechanics (and the marks they leave).
    float ClassBonus(Enemy e, SkillRow? s, bool basic)
    {
        var mult = 1f;
        switch (P.Cls.Id)
        {
            case "assassin":
            {
                // from behind: Backstab
                var face = new Vector3(MathF.Sin(e.Yaw), 0, MathF.Cos(e.Yaw));
                if (Vector3.Dot(face, Flat(P.Pos - e.Pos)) < -0.3f) { mult *= 1.5f; AddNumber(e.Pos + new Vector3(-0.5f, e.Def.Size + 0.9f, 0), "Backstab!", new Color(200, 140, 255, 255), 0.8f); }
                // Rune Carve spends the runes the other blades left
                if (s?.Id == "assa_rune" && e.Runes > 0)
                {
                    mult *= 1 + 0.3f * e.Runes;
                    AddNumber(e.Pos + new Vector3(0.5f, e.Def.Size + 1.1f, 0), $"Rune burst x{e.Runes}", new Color(200, 120, 255, 255), 0.9f);
                    e.Runes = 0;
                }
                else e.Runes = Math.Min(5, e.Runes + 1);
                if (basic && CoatTime > 0 && e.Hp > 0)
                {
                    var same = e.Dots.FirstOrDefault(d => d.Source == "coat");
                    if (same != null) { same.Stacks = Math.Min(5, same.Stacks + 1); same.PerTick = 6 * DamageMult * same.Stacks; same.Ticks = 6; }
                    else e.Dots.Add(new Dot { Source = "coat", PerTick = 6 * DamageMult, Ticks = 6, Timer = 1f / Sheets.SkillKindsDotTicksPerSecond, Col = new Color(140, 220, 60, 255) });
                }
                break;
            }
            case "ranger" when StillTime > 1:
                mult *= 1.25f; // focused aim from standing still
                break;
            case "chanter" when basic:
                if (++_chanterChain % 3 == 0)
                {
                    mult *= 1.6f;
                    e.Push = Flat(e.Pos - P.Pos) * 6;
                    AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.9f, 0), "Combo strike!", new Color(130, 240, 200, 255), 0.8f);
                }
                break;
        }
        GainWrath(4);
        return mult * SpendWrath();
    }
    int _chanterChain;

    // ---------------------------------------------------------------- pets: aggro, water spirits, commands, resurrection

    (SkillRow skill, float power, Color col, float time)? _fallen;
    public float CommandTime, CommandCd;

    public string CommandPets()
    {
        if (Allies.Count == 0) return "No spirits to command";
        if (CommandCd > 0) return "Not ready yet";
        CommandTime = 6; CommandCd = 12;
        foreach (var a in Allies) Effects.Add(new Fx { Kind = FxKind.Ring, A = a.Pos, Radius = 1.2f, Col = a.Col, Life = 0.4f });
        return Locked != null ? $"Spirits: attack the {Locked.Def.Name}!" : "Spirits: attack!";
    }

    /// Melee Locust near a spirit (and nearer to it than to you) go for the spirit.
    Ally? AllyTarget(Enemy e)
    {
        if (e.Taunt > 0 || e.Def.Ranged || Allies.Count == 0) return null;
        var near = Allies.OrderBy(a => Dist2(a.Pos, e.Pos)).First();
        return Dist2(near.Pos, e.Pos) < 6 && Dist2(near.Pos, e.Pos) < Dist2(P.Pos, e.Pos) ? near : null;
    }

    void HitAlly(Ally a, float dmg)
    {
        a.Hp -= dmg;
        AddNumber(a.Pos + new Vector3(0, 2.0f, 0), ((int) dmg).ToString(), new Color(255, 160, 140, 255), 0.6f);
        if (a.Hp > 0) return;
        _fallen = (a.Skill, a.Power, a.Col, TimeAlive);
        Allies.Remove(a);
        Effects.Add(new Fx { Kind = FxKind.Burst, A = a.Pos + Vector3.UnitY, Radius = 1.2f, Col = a.Col, Life = 0.5f });
        Say($"{a.Skill.Name.Replace("Summon ", "")} has fallen", new Color(255, 160, 140, 255));
    }

    /// Cleric heals raise a spirit that fell in the last 30 seconds.
    void TryResurrect()
    {
        if (P.Cls.Id != "cleric" || _fallen is not { } f || TimeAlive - f.time > 30) return;
        _fallen = null;
        var a = NewAlly(f.skill, f.power, f.col);
        a.Hp = a.MaxHp * 0.5f;
        Allies.Add(a);
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = a.Pos, Radius = 1.2f, Col = new Color(255, 240, 170, 255), Life = 0.8f });
        Say("Spirit resurrected", new Color(255, 235, 160, 255));
    }

    Ally NewAlly(SkillRow s, float power, Color col)
    {
        var hp = 260 + power * 3;
        return new Ally { Skill = s, Pos = P.Pos + P.Right * 1.6f, Life = s.Duration, Power = power, Col = col, Hp = hp, MaxHp = hp };
    }

    // ---------------------------------------------------------------- delayed blasts and holy ground

    void UpdateGrounds(float dt)
    {
        for (var i = Grounds.Count - 1; i >= 0; i--)
        {
            var g = Grounds[i];
            g.Time += dt;
            if (g.Skill.Kind == "delayed")
            {
                if (g.Time < g.Skill.Duration) continue;
                Grounds.RemoveAt(i);
                foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, g.Pos) <= g.Skill.Radius + e.Radius) HitWithSkill(e, g.Power, g.Skill);
                Effects.Add(new Fx { Kind = FxKind.Burst, A = g.Pos + Vector3.UnitY, Radius = g.Skill.Radius * 0.7f, Col = g.Col, Life = 0.5f, Element = g.Skill.Element });
                Effects.Add(new Fx { Kind = FxKind.Ring, A = g.Pos, Radius = g.Skill.Radius, Col = g.Col, Life = 0.5f, Element = g.Skill.Element });
                ElementImpact(g.Skill.Element, g.Pos + Vector3.UnitY, 2);
                AddShake(0.35f);
                continue;
            }
            // holy ground: every half second it heals you if you stand in it and burns Locust in it
            if (g.Time >= g.Life) { Grounds.RemoveAt(i); continue; }
            if ((g.Tick -= dt) > 0) continue;
            g.Tick = 0.5f;
            if (Dist2(P.Pos, g.Pos) <= g.Skill.Radius) P.Hp = MathF.Min(P.MaxHp, P.Hp + g.Power * 0.5f);
            foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, g.Pos) <= g.Skill.Radius + e.Radius) { _lastSource = g.Skill.Name; Damage(e, g.Power * 0.4f * DamageMult, false); }
        }
    }

    void DrawGrounds()
    {
        foreach (var g in Grounds)
        {
            var pulse = 0.7f + 0.3f * MathF.Sin(g.Time * 8);
            if (g.Skill.Kind == "delayed")
            {
                var k = Math.Clamp(g.Time / g.Skill.Duration, 0, 1);
                Glow.Decal(g.Pos, g.Skill.Radius, Fade(g.Col, 0.8f * pulse), ring: true);
                Glow.Decal(g.Pos, g.Skill.Radius * k, Fade(g.Col, 0.35f), ring: false);
                Glow.Sprite(g.Pos + Vector3.UnitY * (0.5f + k * 1.5f), 0.4f + k * 0.6f, Fade(g.Col, 0.8f));
            }
            else
            {
                Glow.Decal(g.Pos, g.Skill.Radius, Fade(g.Col, 0.55f * pulse), ring: true);
                Glow.Decal(g.Pos, g.Skill.Radius * 0.95f, Fade(g.Col, 0.18f), ring: false);
                if (_rng.NextDouble() < 0.4) Bits.Emit(g.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * g.Skill.Radius * 1.6f, 0.1f, ((float) _rng.NextDouble() - 0.5f) * g.Skill.Radius * 1.6f), 1, g.Col, 0.3f, 0.06f, 1.2f, -1.5f, glow: true);
            }
        }
        // Assassin runes over their marks
        if (P.Cls.Id == "assassin")
            foreach (var e in Enemies)
                for (var r = 0; r < e.Runes; r++)
                    Glow.Sprite(e.Pos + new Vector3((r - (e.Runes - 1) / 2f) * 0.3f, e.Def.Size + 0.2f, 0), 0.12f, new Color(200, 120, 255, 230));
    }

    void UpdateClasses(float dt, Vector2 move)
    {
        UpdateGrounds(dt);
        if (CoatTime > 0) CoatTime -= dt;
        if (CommandTime > 0) CommandTime -= dt;
        if (CommandCd > 0) CommandCd -= dt;
        StillTime = move.LengthSquared() < 0.01f && Up < 0.1f ? StillTime + dt : 0;
        foreach (var e in Enemies) if (e.Taunt > 0) e.Taunt -= dt;
        if (StanceOn && P.Cls.Id == "chanter")
        {
            P.Mp -= dt;
            if (P.Mp <= 0) { P.Mp = 0; StanceOn = false; ReloadFlash("Out of MP: mantra ends"); }
        }
        if (HasWrath && Wrath > 0 && Wrath < 100 && !WaveActive) Wrath = MathF.Max(0, Wrath - dt * 5);
    }
}
