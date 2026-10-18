using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// Combat depth: combos, evade counters, block/parry/resist, stuns on the hero (and breaking free), downed Locust
/// and executions, headshots, interrupts, wall slams, the combat log and the damage meter.
sealed partial class World
{
    // ---------------------------------------------------------------- combo counter

    public int Combo;
    public float ComboTimer;
    public int BestCombo;
    float ComboBonus => MathF.Min(0.1f, Combo / 5 * 0.01f);

    void RegisterHit()
    {
        Combo = ComboTimer > 0 ? Combo + 1 : 1;
        ComboTimer = 2.5f;
        BestCombo = Math.Max(BestCombo, Combo);
        if (Combo % 10 == 0) ComboFinisher();
    }

    // ---------------------------------------------------------------- evade counter

    /// Seconds left of the counter-attack bonus after a dodged hit.
    public float CounterTime;

    float ConsumeCounter(Vector3 at)
    {
        if (CounterTime <= 0) return 1;
        CounterTime = 0;
        AddNumber(at + new Vector3(0.4f, 0.6f, 0), "Counter!", new Color(140, 220, 255, 255), 0.85f);
        return 1.5f;
    }

    // ---------------------------------------------------------------- the hero's defences and stuns

    public float PlayerStun, BreakFreeCd;
    public string PlayerStunLabel = "";

    /// Rolls block, parry and resist for an incoming hit; returns the damage left.
    float Defend(float dmg, bool melee)
    {
        var at = P.Pos + new Vector3(-0.4f, 2.0f, 0);
        if (melee && _rng.NextDouble() < P.Cls.Parry / 100f) { AddNumber(at, "Parry", new Color(220, 220, 255, 255), 0.85f); CombatNote("You parry", new Color(200, 200, 255, 255)); return 0; }
        if (!melee && _rng.NextDouble() < P.Cls.Resist / 100f) { AddNumber(at, "Resist", new Color(200, 170, 255, 255), 0.85f); CombatNote("You resist", new Color(200, 170, 255, 255)); return 0; }
        if (_rng.NextDouble() < (P.Cls.Block + StanceBlock) / 100f) { AddNumber(at, "Block", new Color(220, 220, 230, 255), 0.85f); return dmg * 0.5f; }
        return dmg;
    }

    /// Locust crowd control on the hero (a Brumak's stomp, a Corpser coming up underneath).
    public void StunPlayer(float t, string label)
    {
        if (P.EvadeTime > 0 || Lost || Won) return;
        if (P.Flying) { P.Flying = false; } // knocked out of the air
        PlayerStun = MathF.Max(PlayerStun, t);
        PlayerStunLabel = label;
        P.Casting = null;
        AddNumber(P.Pos + new Vector3(0, 2.3f, 0), label, new Color(255, 170, 120, 255), 0.85f);
        CombatNote($"You are {label.ToLowerInvariant()}", new Color(255, 170, 120, 255));
    }

    /// AION's break free: evade out of a stun, once every 30 s.
    bool TryBreakFree()
    {
        if (PlayerStun <= 0 || BreakFreeCd > 0) return false;
        PlayerStun = 0; BreakFreeCd = 30;
        AddNumber(P.Pos + new Vector3(0, 2.3f, 0), "Break free!", new Color(140, 220, 255, 255), 0.9f);
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 2.2f, Col = new Color(140, 220, 255, 255), Life = 0.5f });
        return true;
    }

    // ---------------------------------------------------------------- downed Locust and executions

    /// Humanoid Locust sometimes go down instead of dying: they crawl for a few seconds, then get back up.
    bool TryDown(Enemy e)
    {
        if (e.Downed > 0 || IsBoss(e) || e.Def.FlyHeight > 0 || e.Def.Character is not ("drone" or "drone-sniper" or "drone-shotgun" or "grenadier")) return false;
        if (_rng.NextDouble() > 0.3) return false;
        e.Hp = 1; e.Downed = 5; e.Down = 5; e.Stun = 5;
        AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.5f, 0), "Down!", new Color(255, 200, 120, 255), 0.8f);
        return true;
    }

    public Enemy? ExecuteTarget => Enemies.FirstOrDefault(e => e.Downed > 0 && Dist2(e.Pos, P.Pos) < 2.8f);

    /// A Gears execution on a downed Locust: a sure kill, a burst of blood, bonus XP and a little HP back.
    public void ExecuteDowned()
    {
        if (ExecuteTarget is not { } e) return;
        e.Downed = 0;
        Bits.Emit(e.Centre, 50, Blood, 6f, 0.07f, 1.0f);
        AddShake(0.35f);
        P.Hp = MathF.Min(P.MaxHp, P.Hp + P.MaxHp * 0.05f);
        AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.9f, 0), "EXECUTION", new Color(255, 90, 70, 255), 1.1f);
        CombatNote($"You execute the {e.Def.Name}", new Color(255, 120, 90, 255));
        Executions++;
        Progression.Count("executions");
        _lastSource = "Execution";
        CountDamage(e.Hp);
        Kill(e);
        GainXp(e.Def.Xp / 2);
        P.ActionAnim = 0;
    }
    public int Executions;

    // ---------------------------------------------------------------- interrupts and wall slams

    void Interrupt(Enemy e)
    {
        var cut = Telegraphs.RemoveAll(t => t.Owner == e && t.Time < t.Total * 0.85f);
        if (e.Aim > 0) { e.Aim = 0; cut++; }
        if (cut > 0)
        {
            AddNumber(e.Pos + new Vector3(0, e.Def.Size + 1.1f, 0), "Interrupted!", new Color(140, 220, 255, 255), 0.85f);
            CombatNote($"You interrupt the {e.Def.Name}", new Color(140, 220, 255, 255));
        }
    }

    /// A knocked-back Locust that hits a wall takes extra damage and is stunned.
    void WallSlam(Enemy e)
    {
        e.Push = Vector3.Zero;
        e.Stun = MathF.Max(e.Stun, 1.2f);
        AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.9f, 0), "Wall slam!", new Color(255, 200, 120, 255), 0.85f);
        Bits.Emit(e.Centre, 16, new Color(120, 110, 100, 220), 4f, 0.1f, 0.8f, 6f);
        AddShake(0.15f);
        _lastSource = "Wall slam";
        Damage(e, e.MaxHp * 0.08f + 20, false);
    }

    // ---------------------------------------------------------------- combat log and damage meter

    public readonly List<(float time, string text, Color col)> CombatLog = new();
    public readonly Dictionary<string, float> DamageBySource = new();
    string _lastSource = "Basic attack";
    public float WaveDamage, WaveTime;
    public bool ShowLog;

    public void CombatNote(string text, Color col)
    {
        CombatLog.Add((TimeAlive, text, col));
        if (CombatLog.Count > 60) CombatLog.RemoveAt(0);
    }

    void CountDamage(float dmg)
    {
        DamageBySource[_lastSource] = DamageBySource.GetValueOrDefault(_lastSource) + dmg;
        WaveDamage += dmg;
    }

    public float Dps => WaveTime > 1 ? WaveDamage / WaveTime : 0;

    void UpdateCombat(float dt)
    {
        if (ComboTimer > 0 && (ComboTimer -= dt) <= 0) Combo = 0;
        if (CounterTime > 0) CounterTime -= dt;
        if (BreakFreeCd > 0) BreakFreeCd -= dt;
        if (PlayerStun > 0) PlayerStun -= dt;
        if (WaveActive) WaveTime += dt;
        foreach (var e in Enemies)
        {
            if (e.Silence > 0) e.Silence -= dt;
            if (e.Disarm > 0) e.Disarm -= dt;
            if (e.Downed > 0 && (e.Downed -= dt) <= 0)
            {
                // nobody finished it off: it gets back up
                e.Hp = e.MaxHp * 0.3f; e.Down = 0; e.Stun = 0;
                AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.5f, 0), "Revived", new Color(255, 150, 120, 255), 0.7f);
            }
        }
    }

    /// AION auto-attack: with a target locked and in reach, the basic attack keeps going.
    public void AutoAttack()
    {
        if (!Settings.S.AutoAttack || Locked == null || P.Casting != null || PlayerStun > 0) return;
        var reach = P.Cls.BasicKind == "melee" ? P.Cls.BasicRange + Locked.Radius + 3.5f : P.Cls.BasicRange;
        if (Dist2(Locked.Pos, P.Pos) <= reach && HasLineOfSight(P.Pos, Locked.Pos)) BasicAttack();
    }
}
