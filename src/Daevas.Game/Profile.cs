using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;

namespace Daevas.Game;

/// AION item grades, lowest first.
enum Rarity { Common, Superior, Heroic, Fabled, Eternal, Mythic }

/// A piece of gear: a slot, a grade, stats, an enchant level (+0..+15) and socketed manastones.
sealed class Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Slot { get; set; } = "weapon";
    public string Name { get; set; } = "";
    public Rarity Rarity { get; set; }
    public int Level { get; set; } = 1;
    public Dictionary<string, int> Stats { get; set; } = new();
    public int Enchant { get; set; }
    public List<string> Stones { get; set; } = new();   // "crit:2" = +2 crit
    public string Set { get; set; } = "";
    public string Class { get; set; } = "";            // weapons belong to one class
    public int Durability { get; set; } = 100;         // a lost run wears it; at 0 it gives half its stats

    public int Sockets => (int) Rarity;
    /// Stats with enchanting (+5% of each base stat per level, at least +1) and manastones.
    public Dictionary<string, int> Total()
    {
        var t = Stats.ToDictionary(k => k.Key, k => k.Value + Enchant * Math.Max(1, (int) MathF.Round(k.Value * 0.05f)));
        foreach (var s in Stones)
        {
            var p = s.Split(':');
            t[p[0]] = t.GetValueOrDefault(p[0]) + int.Parse(p[1]);
        }
        if (Durability <= 0) foreach (var k in t.Keys.ToList()) t[k] /= 2;
        return t;
    }
    public string FullName => (Enchant > 0 ? $"+{Enchant} " : "") + Name;
}

sealed class ClassRecord
{
    public int BestWave { get; set; }
    public int Wins { get; set; }
    public float BestWinSeconds { get; set; }
    public int Runs { get; set; }
}

/// Everything a player keeps between runs: Kinah, the bag, equipped gear per class, materials, records,
/// kills, achievements, Abyss Points and the chosen title. Saved to %LOCALAPPDATA%\GearsOfAion\profile.json.
sealed class ProfileData
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    public long Kinah { get; set; }
    public int AbyssPoints { get; set; }
    public List<Item> Bag { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Equipped { get; set; } = new();
    public Dictionary<string, int> Materials { get; set; } = new();
    public Dictionary<string, int> Kills { get; set; } = new();
    public Dictionary<string, ClassRecord> Records { get; set; } = new();
    public List<string> Achievements { get; set; } = new();
    public string Title { get; set; } = "";
    public int ShopSeed { get; set; }
    public Dictionary<string, List<ScoreEntry>> Leaderboards { get; set; } = new();
    public List<string> Codex { get; set; } = new();
    public Dictionary<string, int[]> Hotbars { get; set; } = new();
    public string Wings { get; set; } = "class";
    public List<string> Hints { get; set; } = new();
    public List<Mission> Missions { get; set; } = new();
    public int Reputation { get; set; }
    public Dictionary<string, string> HeroLooks { get; set; } = new();
    public string Dye { get; set; } = "none";
    public int Prestige { get; set; }
    public Dictionary<string, int> CustomWave { get; set; } = new() { ["wretch"] = 8, ["drone"] = 3, ["ticker"] = 2 };
    public string CustomBoss { get; set; } = "vrak";
    public List<int> LorePages { get; set; } = new();
    public string Map { get; set; } = "downtown";
    public List<float[]> SpawnPoints { get; set; } = new();   // spawn points drawn on the big map (x, z)   // Locust met so far
}

static class Profile
{
    public static ProfileData P = new();
    static bool _persist;
    static string File => System.IO.Path.Combine(Paths.DataDir, "profile.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static readonly string[] Slots = { "weapon", "helmet", "chest", "gloves", "boots", "earring", "necklace", "ring" };
    public static readonly string[] StatIds = { "power", "hp", "mp", "crit", "speed", "defence" };

    public static string StatName(string id) => id switch
    {
        "power" => "Attack", "hp" => "Max HP", "mp" => "Max MP", "crit" => "Crit", "speed" => "Speed", "defence" => "Defence", _ => id,
    };
    public static string StatText(string id, int v) => id is "power" or "crit" or "speed" or "defence" ? $"+{v}% {StatName(id)}" : $"+{v} {StatName(id)}";

    public static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Common => new Color(220, 220, 220, 255),
        Rarity.Superior => new Color(110, 220, 90, 255),
        Rarity.Heroic => new Color(80, 160, 255, 255),
        Rarity.Fabled => new Color(255, 200, 60, 255),
        Rarity.Eternal => new Color(255, 130, 40, 255),
        _ => new Color(200, 110, 255, 255),
    };

    public static void Load()
    {
        // test and background runs play with a fresh, unsaved profile so checks never depend on a player's bag
        _persist = Environment.GetEnvironmentVariable("DAEVAS_TEST") == null && !Environment.GetCommandLineArgs().Contains("--background");
        if (Environment.GetEnvironmentVariable("DAEVAS_MAP") is { } devMap) P.Map = devMap;
        if (!_persist) return;
        try { if (System.IO.File.Exists(File)) P = JsonSerializer.Deserialize<ProfileData>(System.IO.File.ReadAllText(File), Json) ?? new(); Migrate(); if (Environment.GetEnvironmentVariable("DAEVAS_MAP") is { } dm) P.Map = dm; }
        catch (Exception e)
        {
            Log.Info("profile unreadable, starting fresh: " + e.Message);
            try { System.IO.File.Copy(File, File + ".bad", true); } catch { }
            P = new();
        }
    }

    /// Brings a profile from an older version up to date (new fields get their defaults; a backup is kept).
    static void Migrate()
    {
        if (P.Version >= ProfileData.CurrentVersion) return;
        try { System.IO.File.Copy(File, File + $".v{P.Version}.bak", true); } catch { }
        // version 1 -> 2: missions, reputation, looks, prestige and gear condition arrived
        P.Missions ??= new(); P.Hotbars ??= new(); P.HeroLooks ??= new(); P.Hints ??= new(); P.Codex ??= new(); P.Leaderboards ??= new();
        foreach (var it in P.Bag) if (it.Durability <= 0 && P.Version < 2) it.Durability = 100;
        Log.Info($"profile updated from version {P.Version} to {ProfileData.CurrentVersion}");
        P.Version = ProfileData.CurrentVersion;
    }

    static ProfileData? _ownBeforeReplay;
    static bool _persistBeforeReplay;

    /// A replay plays with the bag it was recorded with (unsaved); the player's own comes back after.
    public static void Watch(string json)
    {
        if (_ownBeforeReplay == null) { _ownBeforeReplay = P; _persistBeforeReplay = _persist; }
        _persist = false;
        try { P = JsonSerializer.Deserialize<ProfileData>(json, Json) ?? new(); } catch { P = new(); }
    }

    public static void Unwatch()
    {
        if (_ownBeforeReplay == null) return;
        P = _ownBeforeReplay; _persist = _persistBeforeReplay; _ownBeforeReplay = null;
    }

    public static string Snapshot() => JsonSerializer.Serialize(P, Json);

    public static void Save()
    {
        if (!_persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            var tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(P, Json));
            System.IO.File.Move(tmp, File, true);
        }
        catch (Exception e) { Log.Info("profile not saved: " + e.Message); }
    }

    public static int Mat(string id) => P.Materials.GetValueOrDefault(id);
    public static void AddMat(string id, int n) { P.Materials[id] = Math.Max(0, Mat(id) + n); }

    public static Item? Equipped(string cls, string slot) =>
        P.Equipped.TryGetValue(cls, out var e) && e.TryGetValue(slot, out var id) ? P.Bag.FirstOrDefault(i => i.Id == id) : null;

    public static bool IsEquipped(Item i) => P.Equipped.Values.Any(e => e.ContainsValue(i.Id));

    public static void Equip(string cls, Item i)
    {
        if (!P.Equipped.TryGetValue(cls, out var e)) P.Equipped[cls] = e = new();
        // an item is worn by one class at a time
        foreach (var other in P.Equipped.Values) foreach (var k in other.Where(kv => kv.Value == i.Id).Select(kv => kv.Key).ToList()) other.Remove(k);
        e[i.Slot] = i.Id;
    }

    public static void Unequip(string cls, string slot) { if (P.Equipped.TryGetValue(cls, out var e)) e.Remove(slot); }

    /// The summed stats of a class's gear, with set bonuses, the title and the Abyss rank.
    public static Dictionary<string, int> GearStats(string cls)
    {
        var t = StatIds.ToDictionary(s => s, _ => 0);
        var worn = Slots.Select(s => Equipped(cls, s)).Where(i => i != null).ToList();
        foreach (var i in worn) foreach (var kv in i!.Total()) t[kv.Key] = t.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var set in Sets)
        {
            var n = worn.Count(i => i!.Set == set.Name);
            if (n >= 2) t[set.Two.stat] += set.Two.value;
            if (n >= 4) t[set.Four.stat] += set.Four.value;
        }
        if (Titles.FirstOrDefault(x => x.Id == P.Title) is { } title) t[title.Stat] += title.Value;
        t["power"] += AbyssRankIndex();
        return t;
    }

    // ---------------------------------------------------------------- loot

    public sealed record SetDef(string Name, (string stat, int value) Two, (string stat, int value) Four);
    public static readonly SetDef[] Sets =
    {
        new("Siege Veteran's", ("power", 5), ("hp", 120)),
        new("Ascended Guardian's", ("defence", 5), ("hp", 160)),
        new("Kantus Bane", ("crit", 4), ("power", 8)),
        new("Windrider's", ("speed", 6), ("mp", 80)),
    };

    static string WeaponName(string cls) => cls switch
    {
        "gladiator" => "Greatsword", "templar" => "Longsword", "assassin" => "Twin Daggers", "ranger" => "Longbow",
        "sorcerer" => "Spellbook", "spiritmaster" => "Orb", "cleric" => "Mace", "chanter" => "Staff", _ => "Weapon",
    };

    static readonly Dictionary<string, string[]> SlotNames = new()
    {
        ["helmet"] = new[] { "Helm", "Hood", "Crown" }, ["chest"] = new[] { "Breastplate", "Jerkin", "Robe" },
        ["gloves"] = new[] { "Gauntlets", "Gloves", "Handguards" }, ["boots"] = new[] { "Greaves", "Boots", "Sabatons" },
        ["earring"] = new[] { "Earring" }, ["necklace"] = new[] { "Necklace", "Pendant" }, ["ring"] = new[] { "Ring", "Band" },
    };

    static readonly string[] Prefix = { "Worn", "Daeva's", "Elyos", "Asmodian", "Balaur-forged", "Empyrean" };
    static readonly string[] Origin = { "of the Siege", "of Atreia", "of Sera", "of the Abyss", "of Ruin", "of the Hollow" };

    /// Rolls a grade: later waves and stronger Locust make better gear likelier.
    public static Rarity RollRarity(Random rng, int wave, float luck)
    {
        var r = rng.NextDouble() / (1 + wave * 0.12 + luck);
        return r < 0.004 ? Rarity.Mythic : r < 0.02 ? Rarity.Eternal : r < 0.07 ? Rarity.Fabled : r < 0.2 ? Rarity.Heroic : r < 0.5 ? Rarity.Superior : Rarity.Common;
    }

    public static Item MakeItem(Random rng, string cls, int level, Rarity rarity, string? slot = null, string? set = null)
    {
        slot ??= Slots[rng.Next(Slots.Length)];
        var mult = 1 + (int) rarity * 0.45f;
        var it = new Item { Slot = slot, Rarity = rarity, Level = level };
        int Roll(float baseValue) => Math.Max(1, (int) MathF.Round(baseValue * mult * (0.85f + (float) rng.NextDouble() * 0.3f) * (1 + level * 0.08f)));
        switch (slot)
        {
            case "weapon": it.Class = cls; it.Stats["power"] = Roll(4); if (rng.NextDouble() < 0.5) it.Stats["crit"] = Roll(1); break;
            case "chest": it.Stats["hp"] = Roll(28); it.Stats["defence"] = Roll(1.5f); break;
            case "helmet": it.Stats["hp"] = Roll(16); it.Stats["defence"] = Roll(1); break;
            case "gloves": it.Stats["power"] = Roll(1.5f); it.Stats["crit"] = Roll(0.8f); break;
            case "boots": it.Stats["hp"] = Roll(12); it.Stats["speed"] = Roll(1.2f); break;
            case "earring": it.Stats["mp"] = Roll(14); it.Stats["crit"] = Roll(0.6f); break;
            case "necklace": it.Stats["mp"] = Roll(18); it.Stats["power"] = Roll(1); break;
            default: it.Stats["hp"] = Roll(10); it.Stats["power"] = Roll(1); break;
        }
        // a random extra stat on better gear
        if (rarity >= Rarity.Heroic)
        {
            var extra = StatIds[rng.Next(StatIds.Length)];
            it.Stats[extra] = it.Stats.GetValueOrDefault(extra) + Roll(extra is "hp" or "mp" ? 8 : 0.6f);
        }
        if (rarity >= Rarity.Fabled && (set != null || rng.NextDouble() < 0.5)) it.Set = set ?? Sets[rng.Next(Sets.Length)].Name;
        var baseName = slot == "weapon" ? WeaponName(cls) : SlotNames[slot][rng.Next(SlotNames[slot].Length)];
        it.Name = it.Set != "" ? $"{it.Set} {baseName}" : rarity >= Rarity.Heroic ? $"{Prefix[Math.Min(Prefix.Length - 1, (int) rarity)]} {baseName} {Origin[rng.Next(Origin.Length)]}" : $"{Prefix[(int) rarity]} {baseName}";
        return it;
    }

    // ---------------------------------------------------------------- enchanting, manastones, salvage, crafting

    public static long EnchantCost(Item i) => 40L * (i.Enchant + 1) * (1 + (int) i.Rarity);
    public static float EnchantChance(Item i) => MathF.Max(0.2f, 1 - i.Enchant * 0.065f);

    /// AION enchanting: one enchantment stone and some Kinah; a failure drops the item a level.
    public static string Enchant(Item i, Random rng)
    {
        if (i.Enchant >= 15) return "Already +15";
        if (Mat("enchant") < 1) return "Needs an enchantment stone";
        if (P.Kinah < EnchantCost(i)) return "Not enough Kinah";
        AddMat("enchant", -1);
        P.Kinah -= EnchantCost(i);
        if (rng.NextDouble() < EnchantChance(i)) { i.Enchant++; Save(); return $"Success! {i.FullName}"; }
        if (i.Enchant > 0) i.Enchant--;
        Save();
        return $"Enchant failed - {i.FullName}";
    }

    public static string Socket(Item i, string stone)
    {
        if (i.Stones.Count >= i.Sockets) return "No free socket";
        if (Mat(stone) < 1) return "No manastone";
        AddMat(stone, -1);
        i.Stones.Add(stone["stone:".Length..]);
        Save();
        return "Manastone socketed";
    }

    public static string Salvage(Item i)
    {
        if (IsEquipped(i)) return "Take it off first";
        P.Bag.Remove(i);
        var chitin = new[] { 1, 2, 4, 8, 16, 30 }[(int) i.Rarity] + i.Enchant;
        AddMat("chitin", chitin);
        var imulsion = i.Rarity >= Rarity.Fabled ? 1 + (int) i.Rarity - 3 : 0;
        if (imulsion > 0) AddMat("imulsion", imulsion);
        Save();
        return $"Salvaged into {chitin} Locust chitin" + (imulsion > 0 ? $" and {imulsion} Imulsion crystal" : "");
    }

    public static string MaterialName(string id) => id switch
    {
        "chitin" => "Locust chitin", "imulsion" => "Imulsion crystal", "enchant" => "Enchantment stone",
        "hp-potion" => "Life potion", "mp-potion" => "Mana potion", "serum" => "Wind serum",
        _ when id.StartsWith("stone:") => $"Manastone ({StatText(id.Split(':')[1], int.Parse(id.Split(':')[2]))})",
        _ => id,
    };

    public sealed record Recipe(string Name, string Makes, int Chitin, int Imulsion, int Kinah);
    public static readonly Recipe[] Recipes =
    {
        new("Life potion", "hp-potion", 3, 0, 0),
        new("Mana potion", "mp-potion", 3, 0, 0),
        new("Wind serum", "serum", 5, 0, 0),
        new("Enchantment stone", "enchant", 6, 0, 20),
        new("Manastone", "stone", 10, 1, 50),
        new("Heroic gear", "gear:Heroic", 40, 2, 200),
        new("Fabled set piece", "gear:Fabled", 80, 5, 600),
    };

    public static string Craft(Recipe r, string cls, int level, Random rng)
    {
        if (Mat("chitin") < r.Chitin || Mat("imulsion") < r.Imulsion || P.Kinah < r.Kinah) return "Not enough materials";
        AddMat("chitin", -r.Chitin); AddMat("imulsion", -r.Imulsion); P.Kinah -= r.Kinah;
        string made;
        if (r.Makes == "stone") { var s = RandomStone(rng); AddMat(s, 1); made = MaterialName(s); }
        else if (r.Makes.StartsWith("gear:"))
        {
            var rar = Enum.Parse<Rarity>(r.Makes[5..]);
            if (rng.NextDouble() < 0.15) rar++;
            var it = MakeItem(rng, cls, level, rar, set: rar >= Rarity.Fabled ? Sets[rng.Next(Sets.Length)].Name : null);
            P.Bag.Add(it); made = it.Name;
        }
        else { AddMat(r.Makes, 1); made = MaterialName(r.Makes); }
        Save();
        return $"Crafted {made}";
    }

    public static string RandomStone(Random rng)
    {
        var stat = StatIds[rng.Next(StatIds.Length)];
        var v = stat is "hp" or "mp" ? 10 + rng.Next(3) * 5 : 1 + rng.Next(3);
        return $"stone:{stat}:{v}";
    }

    // ---------------------------------------------------------------- vendor (stock changes every wave)

    public sealed record Offer(string Name, string Gives, long Price, Item? Gear);

    public static List<Offer> Stock(string cls, int wave, int level)
    {
        var rng = new Random(P.ShopSeed * 31 + wave * 7919 + cls.GetHashCode());
        var repair = Progression.RepairCost();
        var list = new List<Offer>
        {
            new(repair > 0 ? "Repair all gear" : "Repair all gear (nothing worn)", "repair", Progression.Discounted(repair), null),
            new("Life potion", "hp-potion", 60, null),
            new("Mana potion", "mp-potion", 60, null),
            new("Wind serum", "serum", 120, null),
            new("Enchantment stone", "enchant", 150, null),
        };
        var stone = RandomStone(rng);
        list.Add(new(MaterialName(stone), stone, 280, null));
        for (var i = 0; i < 3; i++)
        {
            var rar = (Rarity) Math.Min(4, rng.Next(1, 3 + wave / 2) + (Progression.Reputation().discount >= 15 ? 1 : 0));
            var it = MakeItem(rng, cls, level, rar);
            list.Add(new(it.Name, "gear", 180L * (1 + (int) rar) * (1 + (int) rar) + level * 20, it));
        }
        return list.Select(o => o.Gives == "repair" ? o : o with { Price = Progression.Discounted(o.Price) }).ToList();
    }

    public static string Buy(Offer o)
    {
        if (o.Gives == "repair") return Progression.RepairAll();
        if (P.Kinah < o.Price) return "Not enough Kinah";
        P.Kinah -= o.Price;
        if (o.Gear != null)
        {
            // a fresh copy, so the shelf item can't be bought twice as one
            var copy = JsonSerializer.Deserialize<Item>(JsonSerializer.Serialize(o.Gear, Json), Json)!;
            copy.Id = Guid.NewGuid().ToString("N")[..10];
            P.Bag.Add(copy);
        }
        else AddMat(o.Gives, 1);
        Save();
        return $"Bought {o.Name}";
    }

    // ---------------------------------------------------------------- Abyss ranks, titles, achievements

    static readonly (int ap, string name)[] AbyssRanks =
    {
        (0, "Soldier, Rank 9"), (300, "Soldier, Rank 7"), (900, "Soldier, Rank 5"), (2000, "Soldier, Rank 3"), (4000, "Soldier, Rank 1"),
        (8000, "Army 1-Star Officer"), (14000, "Army 3-Star Officer"), (24000, "Army 5-Star Officer"), (40000, "General"), (65000, "Great General"),
        (100000, "Commander"), (160000, "Governor"),
    };
    public static int AbyssRankIndex() { var i = 0; while (i + 1 < AbyssRanks.Length && P.AbyssPoints >= AbyssRanks[i + 1].ap) i++; return i; }
    public static string AbyssRank => AbyssRanks[AbyssRankIndex()].name;
    public static int NextRankAp => AbyssRankIndex() + 1 < AbyssRanks.Length ? AbyssRanks[AbyssRankIndex() + 1].ap : 0;

    public sealed record TitleDef(string Id, string Name, string Stat, int Value, string Needs);
    public static readonly TitleDef[] Titles =
    {
        new("first-blood", "Locust Hunter", "power", 2, "first-kill"),
        new("e-hole", "Hole Plugger", "hp", 40, "kills-500"),
        new("survivor", "Survivor of Sera", "defence", 3, "win"),
        new("flawless", "Untouchable", "crit", 3, "flawless-wave"),
        new("ascended", "Ascended Daeva", "power", 4, "level-10"),
        new("collector", "Treasure Seeker", "mp", 40, "fabled-item"),
        new("all-classes", "Master of the Eight", "power", 5, "win-all"),
    };

    public sealed record AchievementDef(string Id, string Name, string About);
    public static readonly AchievementDef[] AchievementList =
    {
        new("first-kill", "First Blood", "Kill a Locust"),
        new("kills-100", "Exterminator", "Kill 100 Locust"),
        new("kills-500", "E-Hole Plugger", "Kill 500 Locust"),
        new("kills-2000", "Scourge of the Hollow", "Kill 2,000 Locust"),
        new("level-10", "Ascension", "Reach level 10"),
        new("wave-3", "Holding the Line", "Reach wave 3"),
        new("win", "Siege Broken", "Clear all six waves"),
        new("win-all", "Master of the Eight", "Clear the waves with all eight classes"),
        new("flawless-wave", "Untouchable", "Clear a wave without taking damage"),
        new("chain-50", "Chain Master", "Land 50 chain skills in one run"),
        new("fabled-item", "Treasure Seeker", "Find a Fabled item or better"),
        new("enchant-10", "Lucky Hand", "Enchant an item to +10"),
        new("kinah-10000", "Wealthy Daeva", "Hold 10,000 Kinah"),
        new("reaver", "Grounded", "Kill a Reaver"),
        new("ticker-chain", "Chain Reaction", "Blow up 3 Locust with one Ticker"),
        new("flight-kill", "Death from Above", "Kill a Locust while flying"),
    };

    /// Unlocks an achievement once; returns its name the first time.
    public static string? Unlock(string id)
    {
        if (P.Achievements.Contains(id)) return null;
        var a = AchievementList.FirstOrDefault(x => x.Id == id);
        if (a == null) return null;
        P.Achievements.Add(id);
        Save();
        return a.Name;
    }

    public static int TotalKills => P.Kills.Values.Sum();
}
