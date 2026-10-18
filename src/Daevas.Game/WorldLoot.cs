using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// Loot, gear stats, potions, achievements and run records (the saved profile lives in Profile).
sealed partial class World
{
    /// The loot and kill feed in the corner: text, colour, seconds shown.
    public readonly List<(string text, Color col, float time)> Feed = new();
    Dictionary<string, int> _gear = Profile.StatIds.ToDictionary(s => s, _ => 0);
    public int GearStat(string id) => _gear.GetValueOrDefault(id);
    public readonly Dictionary<string, float> PotionCd = new();
    float _waveDamageStart;
    int _tickerChain;

    public void Say(string text, Color col)
    {
        Feed.Add((text, col, 6));
        if (Feed.Count > 7) Feed.RemoveAt(0);
    }

    /// Puts the class's equipped gear into effect (call again after changing gear).
    public void ApplyGear()
    {
        var g = Profile.GearStats(P.Cls.Id);
        g["hp"] += (int) (StatSpend["hp"] * P.Cls.MaxHp * 0.03f);
        g["crit"] += StatSpend["crit"];
        g["speed"] += StatSpend["speed"];
        var hp = g["hp"] - _gear["hp"]; var mp = g["mp"] - _gear["mp"];
        P.MaxHp += hp; P.Hp = Math.Clamp(P.Hp + MathF.Max(0, hp), 1, P.MaxHp);
        P.MaxMp += mp; P.Mp = Math.Clamp(P.Mp + MathF.Max(0, mp), 0, P.MaxMp);
        _gear = g;
    }

    float CritChance(float baseChance) => baseChance + GearStat("crit") / 100f + (Has("hunter") ? 0.06f : 0);
    float GearSpeed => 1 + GearStat("speed") / 100f;
    float GearDefence => 1 - MathF.Min(0.5f, GearStat("defence") / 100f);

    void Achieve(string id)
    {
        if (Profile.Unlock(id) is { } name)
        {
            Say($"Achievement: {name}", new Color(255, 214, 110, 255));
            if (Profile.Titles.FirstOrDefault(t => t.Needs == id) is { } title) Say($"Title unlocked: {title.Name}", new Color(255, 214, 110, 255));
            Sfx.Play(Sfx.LevelUp);
        }
    }

    /// AION loot from a kill: Kinah and Abyss Points always, sometimes materials, stones or gear.
    void DropLoot(Enemy e)
    {
        var prof = Profile.P;
        prof.Kills[e.Def.Id] = prof.Kills.GetValueOrDefault(e.Def.Id) + 1;
        if (e.Def.Id == "dummy") return;
        var kinah = Math.Max(1, (int) (e.Def.Xp / 3f * (0.8 + _rng.NextDouble() * 0.4) * (Hardcore ? 1.5 : 1)));
        prof.Kinah += kinah;
        prof.AbyssPoints += Math.Max(1, e.Def.Xp / 10);
        var big = e.Def.Size > 2.2f;
        var luck = (big ? 1.5f : 0) + (Hardcore ? 0.5f : 0);
        if (_rng.NextDouble() < 0.3) { var n = 1 + _rng.Next(big ? 4 : 2); Profile.AddMat("chitin", n); Say($"{n} Locust chitin", new Color(200, 190, 170, 255)); }
        if (_rng.NextDouble() < (big ? 0.15 : 0.02)) { Profile.AddMat("imulsion", 1); Say("Imulsion crystal", new Color(120, 220, 255, 255)); }
        if (_rng.NextDouble() < (big ? 0.25 : 0.05)) { Profile.AddMat("enchant", 1); Say("Enchantment stone", new Color(150, 200, 255, 255)); }
        if (_rng.NextDouble() < (big ? 0.12 : 0.015)) { var s = Profile.RandomStone(_rng); Profile.AddMat(s, 1); Say(Profile.MaterialName(s), new Color(200, 160, 255, 255)); }
        if (_rng.NextDouble() < (big ? 0.06 : 0.01)) { var p = new[] { "hp-potion", "mp-potion", "serum" }[_rng.Next(3)]; Profile.AddMat(p, 1); Say(Profile.MaterialName(p), new Color(150, 255, 160, 255)); }
        if (_rng.NextDouble() < (big ? 0.4 : 0.06))
        {
            var it = Profile.MakeItem(_rng, P.Cls.Id, P.Level, Profile.RollRarity(_rng, Wave, luck));
            prof.Bag.Add(it);
            Say($"Looted {it.Name}", Profile.RarityColor(it.Rarity));
            Effects.Add(new Fx { Kind = FxKind.Pillar, A = e.Pos with { Y = 0 }, Radius = 0.5f, Col = Profile.RarityColor(it.Rarity), Life = 1.2f });
            if (it.Rarity >= Rarity.Fabled) { Achieve("fabled-item"); Banner($"{it.Rarity.ToString().ToUpperInvariant()} DROP\n{it.Name}"); }
        }
        var total = Profile.TotalKills;
        Achieve("first-kill");
        if (total >= 100) Achieve("kills-100");
        if (total >= 500) Achieve("kills-500");
        if (total >= 2000) Achieve("kills-2000");
        if (e.Def.FlyHeight > 0) Achieve("reaver");
        if (P.Flying) Achieve("flight-kill");
        if (prof.Kinah >= 10000) Achieve("kinah-10000");
    }

    /// Life potion (Z / LB), mana potion (X / RB) and wind serum (V / LT+LB): each on its own short cooldown.
    public string UsePotion(string id)
    {
        if (Lost || Won) return "";
        if (Hardcore) return "No potions in Hardcore";
        if (PotionCd.GetValueOrDefault(id) > 0) return "Not ready yet";
        if (Profile.Mat(id) < 1) return $"No {Profile.MaterialName(id)}";
        switch (id)
        {
            case "hp-potion":
                if (P.Hp >= P.MaxHp) return "HP is full";
                var heal = MathF.Min(P.MaxHp * 0.35f, P.MaxHp - P.Hp);
                P.Hp += heal;
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), "+" + (int) heal, new Color(110, 230, 120, 255), 1f);
                break;
            case "mp-potion":
                if (P.Mp >= P.MaxMp) return "MP is full";
                P.Mp = MathF.Min(P.MaxMp, P.Mp + P.MaxMp * 0.4f);
                break;
            case "serum":
                P.FlightTime = Sheets.FlightSeconds;
                break;
            default: return "";
        }
        Profile.AddMat(id, -1);
        PotionsUsed++;
        PotionCd[id] = 8;
        Bits.Emit(P.Pos + Vector3.UnitY, 16, id == "mp-potion" ? new Color(110, 160, 255, 255) : id == "serum" ? new Color(190, 255, 230, 255) : new Color(130, 255, 140, 255), 1.2f, 0.06f, 0.9f, -2f, glow: true);
        Sfx.Play(Sfx.Cast);
        return "";
    }

    void UpdateLoot(float dt)
    {
        for (var i = Feed.Count - 1; i >= 0; i--) { var f = Feed[i]; f.time -= dt; if (f.time <= 0) Feed.RemoveAt(i); else Feed[i] = f; }
        foreach (var k in PotionCd.Keys.ToList()) PotionCd[k] -= dt;
    }

    void OnWaveStart() { _waveDamageStart = DamageTaken; if (Wave + 1 >= 3) Achieve("wave-3"); }

    void OnWaveCleared()
    {
        Progression.Count("waves");
        Progression.AddReputation(5);
        if (DamageTaken <= _waveDamageStart) Achieve("flawless-wave");
        // bonus Kinah for each cleared wave, more for a clean one
        var bonus = 50 * (Wave + 1) * (DamageTaken <= _waveDamageStart ? 2 : 1);
        Profile.P.Kinah += bonus;
        Say($"Wave bonus: {bonus} Kinah", new Color(255, 214, 110, 255));
        Profile.P.ShopSeed++;
        Profile.Save();
    }

    /// Records the run in the profile when it ends (once).
    bool _recorded;
    void RecordRun()
    {
        if (_recorded) return;
        _recorded = true;
        var rec = Profile.P.Records.TryGetValue(P.Cls.Id, out var r) ? r : Profile.P.Records[P.Cls.Id] = new ClassRecord();
        rec.Runs++;
        rec.BestWave = Math.Max(rec.BestWave, Math.Min(Wave + 1, WaveCount));
        if (Lost) Progression.WearGear(P.Cls.Id);
        if (Won)
        {
            rec.Wins++;
            Progression.Count("wins");
            Progression.AddReputation(Mode == GameMode.Siege ? 80 : 50);
            if (rec.BestWinSeconds <= 0 || TimeAlive < rec.BestWinSeconds) rec.BestWinSeconds = TimeAlive;
            Achieve("win");
            if (Sheets.Classes.All(c => Profile.P.Records.TryGetValue(c.Id, out var cr) && cr.Wins > 0)) Achieve("win-all");
        }
        if (ChainCasts >= 50) Achieve("chain-50");
        Profile.Save();
    }
}
