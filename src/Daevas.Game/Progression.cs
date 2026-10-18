using System;
using System.Collections.Generic;
using System.Linq;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// A mission on the board: daily, weekly or a bounty, with progress toward a target and a reward.
sealed class Mission
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "daily";     // daily, weekly, bounty
    public string Text { get; set; } = "";
    public string Counter { get; set; } = "";       // what counts: kills, kill:<enemy>, waves, chains, bosses, executions, wins
    public int Target { get; set; }
    public int Progress { get; set; }
    public int Reward { get; set; }                 // Kinah
    public bool Claimed { get; set; }
    public string Period { get; set; } = "";        // the day or week it belongs to
}

/// Account progression beyond loot: the mission board, hero looks and dyes, prestige, COG reputation,
/// gear wear and repairs, and bonus XP events.
static class Progression
{
    static string Day => DateTime.Now.ToString("yyyy-MM-dd");
    static string Week => $"{DateTime.Now.Year}-W{System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Now)}";

    /// Today's dailies, this week's weeklies and the standing bounties; old ones roll off.
    public static List<Mission> Board()
    {
        var p = Profile.P;
        p.Missions.RemoveAll(m => (m.Kind == "daily" && m.Period != Day) || (m.Kind == "weekly" && m.Period != Week));
        if (!p.Missions.Any(m => m.Kind == "daily"))
        {
            var rng = new Random(Day.GetHashCode());
            var pool = new (string text, string counter, int target, int reward)[]
            {
                ("Kill 60 Locust", "kills", 60, 300), ("Clear 5 waves", "waves", 5, 300), ("Land 15 chain skills", "chains", 15, 250),
                ("Execute 5 downed Locust", "executions", 5, 300), ("Kill 20 Wretches", "kill:wretch", 20, 250), ("Kill 10 Drones", "kill:drone", 10, 250),
                ("Kill 8 Tickers before they reach you", "kill:ticker", 8, 250), ("Defeat a boss", "bosses", 1, 400),
            };
            foreach (var (text, counter, target, reward) in pool.OrderBy(_ => rng.Next()).Take(3))
                p.Missions.Add(new Mission { Id = $"d-{counter}", Kind = "daily", Text = text, Counter = counter, Target = target, Reward = reward, Period = Day });
        }
        if (!p.Missions.Any(m => m.Kind == "weekly"))
        {
            p.Missions.Add(new Mission { Id = "w-kills", Kind = "weekly", Text = "Kill 600 Locust this week", Counter = "kills", Target = 600, Reward = 2000, Period = Week });
            p.Missions.Add(new Mission { Id = "w-wins", Kind = "weekly", Text = "Clear the campaign twice this week", Counter = "wins", Target = 2, Reward = 2500, Period = Week });
            p.Missions.Add(new Mission { Id = "w-bosses", Kind = "weekly", Text = "Defeat 6 bosses this week", Counter = "bosses", Target = 6, Reward = 2000, Period = Week });
        }
        if (!p.Missions.Any(m => m.Kind == "bounty"))
        {
            p.Missions.Add(new Mission { Id = "b-vrak", Kind = "bounty", Text = "Bounty: the Vrak, three times", Counter = "kill:vrak", Target = 3, Reward = 1500 });
            p.Missions.Add(new Mission { Id = "b-brumak", Kind = "bounty", Text = "Bounty: bring down a Brumak", Counter = "kill:brumak", Target = 1, Reward = 3000 });
            p.Missions.Add(new Mission { Id = "b-snipers", Kind = "bounty", Text = "Bounty: 25 Locust snipers", Counter = "kill:drone-sniper", Target = 25, Reward = 1500 });
            p.Missions.Add(new Mission { Id = "b-matrons", Kind = "bounty", Text = "Bounty: 10 Spore Matrons", Counter = "kill:sporematron", Target = 10, Reward = 1500 });
        }
        return p.Missions;
    }

    /// Counts something toward every open mission that tracks it.
    public static void Count(string counter, int n = 1)
    {
        foreach (var m in Board())
            if (!m.Claimed && m.Progress < m.Target && (m.Counter == counter)) m.Progress = Math.Min(m.Target, m.Progress + n);
    }

    public static string Claim(Mission m)
    {
        if (m.Claimed) return "Already claimed";
        if (m.Progress < m.Target) return "Not finished yet";
        m.Claimed = true;
        Profile.P.Kinah += m.Reward;
        Profile.AddMat("enchant", m.Kind == "daily" ? 1 : 3);
        AddReputation(m.Kind == "daily" ? 20 : 60);
        Profile.Save();
        return $"Claimed {m.Reward:N0} Kinah";
    }

    // ---------------------------------------------------------------- COG reputation

    static readonly (int rep, string name, int discount)[] RepRanks = { (0, "Stranger", 0), (100, "Ally", 5), (300, "Trusted", 10), (700, "Honoured", 15), (1500, "Hero of the COG", 20) };
    public static void AddReputation(int n) => Profile.P.Reputation += n;
    public static (string name, int discount, int next) Reputation()
    {
        var i = 0;
        while (i + 1 < RepRanks.Length && Profile.P.Reputation >= RepRanks[i + 1].rep) i++;
        return (RepRanks[i].name, RepRanks[i].discount, i + 1 < RepRanks.Length ? RepRanks[i + 1].rep : 0);
    }
    public static long Discounted(long price) => price * (100 - Reputation().discount) / 100;

    // ---------------------------------------------------------------- gear wear and repair

    /// A lost run wears your equipped gear; worn-out gear (0%) gives half its stats until repaired.
    public static void WearGear(string cls)
    {
        foreach (var slot in Profile.Slots)
            if (Profile.Equipped(cls, slot) is { } it) it.Durability = Math.Max(0, it.Durability - 15);
        Profile.Save();
    }

    public static long RepairCost() => Profile.P.Bag.Where(i => i.Durability < 100).Sum(i => (100 - i.Durability) * (1 + (int) i.Rarity) * 2L);

    public static string RepairAll()
    {
        var cost = Discounted(RepairCost());
        if (cost == 0) return "Nothing needs repairing";
        if (Profile.P.Kinah < cost) return $"Repairs cost {cost:N0} Kinah";
        Profile.P.Kinah -= cost;
        foreach (var i in Profile.P.Bag) i.Durability = 100;
        Profile.Save();
        return $"Repaired everything for {cost:N0} Kinah";
    }

    // ---------------------------------------------------------------- hero looks, dyes, prestige

    /// Heroes you can wear on a class: its own, plus any hero whose class you have cleared the campaign with.
    public static List<string> HeroesFor(ClassRow cls)
    {
        var list = new List<string> { cls.Character };
        foreach (var c in Sheets.Classes)
            if (c.Id != cls.Id && Profile.P.Records.TryGetValue(c.Id, out var r) && (r.Wins > 0 || r.BestWave >= 4)) list.Add(c.Character);
        return list.Distinct().ToList();
    }

    public static string HeroFor(ClassRow cls) =>
        Profile.P.HeroLooks.TryGetValue(cls.Id, out var h) && HeroesFor(cls).Contains(h) ? h : cls.Character;

    public static readonly (string id, string name, Color tint, string needs)[] Dyes =
    {
        ("none", "No dye", Color.White, ""),
        ("ash", "Ash grey", new Color(190, 190, 195, 255), ""),
        ("crimson", "COG crimson", new Color(255, 170, 160, 255), "first-kill"),
        ("gold", "Daeva gold", new Color(255, 230, 160, 255), "level-10"),
        ("abyss", "Abyss violet", new Color(210, 180, 255, 255), "kills-500"),
        ("frost", "Frost blue", new Color(180, 215, 255, 255), "wave-3"),
        ("verdant", "Verdant", new Color(190, 240, 190, 255), "win"),
    };
    public static bool DyeOpen(string needs) => needs == "" || Profile.P.Achievements.Contains(needs);
    public static Color DyeTint() => Dyes.FirstOrDefault(d => d.id == Profile.P.Dye) is { id: not null } d && DyeOpen(d.needs) ? d.tint : Color.White;

    /// Each prestige (reaching level 30 in a run) is a permanent +1% damage.
    public static float PrestigeBonus => Profile.P.Prestige * 0.01f;

    /// Weekends give 50% more XP; some waves are bonus waves.
    public static float XpBonus => DateTime.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.5f : 1;
}
