using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// The Daeva's growth in a run: the ultimate meter and Transformation, Ascension at level 10, Stigma passives at
/// levels 5, 10 and 15, class passives, and the hotbar order (saved per class).
sealed partial class World
{
    // ---------------------------------------------------------------- ultimate and Transformation

    public float Ultimate;          // 0..100, fills as you deal damage and kill
    public float Transform;         // seconds left of Daeva Transformation
    public bool Ascended;

    public static (string name, string element, float radius) UltimateOf(string cls) => cls switch
    {
        "gladiator" => ("Titan's Rage", "earth", 7),
        "templar" => ("Aegis of Light", "holy", 7),
        "assassin" => ("Thousand Cuts", "shadow", 6),
        "ranger" => ("Sky Hunter's Barrage", "wind", 9),
        "sorcerer" => ("Cataclysm", "fire", 9),
        "spiritmaster" => ("Elemental Convergence", "arcane", 8),
        "cleric" => ("Divine Judgment", "holy", 8),
        _ => ("Mantra of Ascension", "lightning", 7),
    };

    void ChargeUltimate(float amount) { if (Transform <= 0) Ultimate = MathF.Min(100, Ultimate + amount); }

    /// The ultimate (Q, or LT + RS): Daeva Transformation for 12 s and a class blast around you.
    public string UseUltimate()
    {
        if (Lost || Won || PlayerStun > 0) return "";
        if (Ultimate < 100) return $"Ultimate at {Ultimate:0}%";
        var (name, element, radius) = UltimateOf(P.Cls.Id);
        Ultimate = 0;
        Transform = 12;
        var col = ElementColor(element);
        var power = 380 + 55 * P.Level;
        foreach (var e in Enemies.ToList())
            if (Dist2(e.Pos, P.Pos) <= radius + e.Radius)
            {
                _lastSource = name;
                Damage(e, power * DamageMult * ElementMult(e, element), true);
                if (e.Hp > 0) ApplyCc(e, "knockdown", 1.5f);
                ElementImpact(element, e.Centre, 1.5f);
            }
        if (P.Cls.Id is "cleric" or "templar" or "chanter") P.Hp = MathF.Min(P.MaxHp, P.Hp + P.MaxHp * 0.3f);
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos with { Y = 0 }, Radius = radius, Col = col, Life = 0.8f, Element = element });
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos with { Y = 0 }, Radius = 2.5f, Col = col, Life = 1.2f, Element = element });
        Bits.Emit(P.Pos + Vector3.UnitY, 80, col, 8f, 0.1f, 1.0f, 0, glow: true);
        AddShake(0.7f);
        Pad.Rumble(0.9f, 0.5f);
        Banner($"{name.ToUpperInvariant()}\nDaeva Transformation");
        Audio.Duck(2);
        Sfx.Play(Sfx.LevelUp);
        CombatNote($"You unleash {name}", new Color(255, 214, 110, 255));
        return "";
    }

    static float ElementMult(Enemy e, string element) => element == e.Def.Weak ? 1.3f : element == e.Def.Resist ? 0.7f : 1;

    float TransformDamage => Transform > 0 ? 0.3f : 0;
    float TransformSpeed => Transform > 0 ? 1.2f : 1;

    // ---------------------------------------------------------------- Ascension

    void Ascend()
    {
        if (Ascended) return;
        Ascended = true;
        P.MaxHp *= 1.05f; P.Hp = P.MaxHp;
        Banner("ASCENSION\nYour wings burn gold");
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos with { Y = 0 }, Radius = 3, Col = new Color(255, 215, 120, 255), Life = 2.0f });
        Bits.Emit(P.Pos + Vector3.UnitY, 60, new Color(255, 220, 140, 255), 3f, 0.08f, 1.6f, -2f, glow: true);
    }

    /// The wings' colour: the class colour, golden once ascended, blazing in Transformation.
    Color WingColor()
    {
        var c = Hex(P.Cls.Color);
        if (Transform > 0) return new Color(255, 230, 150, 255);
        if (WingSkins.FirstOrDefault(w => w.Id == Profile.P.Wings) is { Id: not "class" } skin && WingUnlocked(skin)) return skin.Col;
        if (!Ascended) return c;
        return new Color((c.R + 255 * 2) / 3, (c.G + 210 * 2) / 3, (c.B + 110 * 2) / 3, 255);
    }
    float WingGlow => Transform > 0 ? 1.6f : Ascended ? 1.25f : 1 + P.Level * 0.02f;

    // ---------------------------------------------------------------- Stigma passives

    public sealed record StigmaDef(string Id, string Name, string About);
    public static readonly StigmaDef[][] StigmaTiers =
    {
        new[] { new StigmaDef("fury", "Stigma of Fury", "+8% damage"), new StigmaDef("iron", "Stigma of Iron", "+10% max HP"), new StigmaDef("haste", "Stigma of Haste", "skills cool down 10% faster") },
        new[] { new StigmaDef("hunter", "Stigma of the Hunter", "+6% crit chance"), new StigmaDef("renewal", "Stigma of Renewal", "heal 1% HP every second"), new StigmaDef("focus", "Stigma of Focus", "skills cost 15% less MP") },
        new[] { new StigmaDef("abyss", "Stigma of the Abyss", "+15% damage to bosses"), new StigmaDef("wings", "Stigma of Wings", "+50% flight time"), new StigmaDef("echo", "Stigma of Echoes", "10% chance a skill resets its cooldown") },
    };
    public readonly List<string> Stigmas = new();
    public int StigmaPoints => (P.Level >= 5 ? 1 : 0) + (P.Level >= 10 ? 1 : 0) + (P.Level >= 15 ? 1 : 0) - Stigmas.Count;
    public bool StigmaOpen;
    float _stigmaAuto;

    public bool Has(string stigma) => Stigmas.Contains(stigma);

    public void PickStigma(int option)
    {
        if (StigmaPoints <= 0) return;
        var tier = StigmaTiers[Stigmas.Count];
        var s = tier[Math.Clamp(option, 0, tier.Length - 1)];
        Stigmas.Add(s.Id);
        if (s.Id == "iron") { P.MaxHp *= 1.1f; P.Hp = MathF.Min(P.MaxHp, P.Hp + P.MaxHp * 0.1f); }
        StigmaOpen = false;
        Banner($"{s.Name}\n{s.About}");
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 2.4f, Col = new Color(200, 140, 255, 255), Life = 0.6f });
    }

    float StigmaDamage(Enemy? e) => (Has("fury") ? 0.08f : 0) + (e != null && IsBoss(e) && Has("abyss") ? 0.15f : 0);

    // ---------------------------------------------------------------- class passives

    public static string PassiveOf(string cls) => cls switch
    {
        "gladiator" => "Wrath: hitting and being hit builds Wrath; at 100 your next hit lands 50% harder. Berserk Stance (G).",
        "templar" => "Bulwark: high block and parry, Shield Stance (G) for more. Provoke taunts Locust onto you.",
        "assassin" => "Runes and shadows: hits carve runes that Rune Carve detonates; +50% from behind; Venom Edge coats your blades.",
        "ranger" => "Focus: stand still for a second and your shots hit 25% harder.",
        "sorcerer" => "Arcane Flow: MP comes back 25% faster.",
        "spiritmaster" => "Spirit Bond: four elemental spirits; command them to focus your target (C).",
        "cleric" => "Devotion: heals 20% stronger, and they raise a fallen spirit.",
        _ => "Combo strikes: every third basic attack hits hard and knocks back. Mantra of Battle (G).",
    };

    float PassiveMpRegen => P.Cls.Id == "sorcerer" ? 1.25f : 1;
    float PassiveHeal => P.Cls.Id == "cleric" ? 1.2f : 1;

    // ---------------------------------------------------------------- hotbar order (saved per class)

    public int[] SlotMap = { 0, 1, 2, 3, 4, 5, 6, 7 };

    void LoadSlotMap()
    {
        if (Profile.P.Hotbars.TryGetValue(P.Cls.Id, out var map) && map.Length == 8 && map.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 8))) SlotMap = map.ToArray();
    }

    /// Swaps two hotbar slots (the skill book's rebinding).
    public void SwapSlots(int a, int b)
    {
        (SlotMap[a], SlotMap[b]) = (SlotMap[b], SlotMap[a]);
        (P.Chains[a], P.Chains[b]) = (P.Chains[b], P.Chains[a]);
        Profile.P.Hotbars[P.Cls.Id] = SlotMap.ToArray();
        Profile.Save();
    }

    void UpdateAscend(float dt)
    {
        if (Transform > 0)
        {
            Transform -= dt;
            if (_rng.NextDouble() < dt * 20) Bits.Emit(P.Pos + new Vector3(0, 1 + (float) _rng.NextDouble(), 0), 1, new Color(255, 220, 140, 255), 1f, 0.06f, 0.8f, -1.5f, glow: true);
        }
        if (Has("renewal") && !Lost) P.Hp = MathF.Min(P.MaxHp, P.Hp + P.MaxHp * 0.01f * dt);
        if (P.Level >= 10) Ascend();
        // test and clip runs pick the first Stigma on their own
        if (StigmaPoints > 0 && AutoStigma && (_stigmaAuto += dt) > 2) { _stigmaAuto = 0; PickStigma(0); }
    }
    public bool AutoStigma;
}
