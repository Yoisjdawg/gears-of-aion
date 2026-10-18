using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

enum GameMode { Campaign, Endless, Siege, BossRush, TimeAttack, Daily, Practice, Tutorial, Custom, Rescue }

static class Modes
{
    public static readonly (GameMode mode, string name, string about)[] All =
    {
        (GameMode.Campaign, "Campaign", "Eight waves through the Siege, with radio from COG Control and four bosses."),
        (GameMode.Endless, "Endless Horde", "Waves without end. They get harder, they get modifiers, and a boss every fifth wave."),
        (GameMode.Siege, "Siege Defence", "Keep the COG ammo van standing. Some Locust go straight for it."),
        (GameMode.BossRush, "Boss Rush", "Vrak, the Corpser, Vrak the Scorched and the Brumak, back to back. You start at level 10."),
        (GameMode.TimeAttack, "Time Attack", "The campaign against the clock. Faster clears score more."),
        (GameMode.Daily, "Daily Challenge", "Today's waves and modifiers are the same for everyone. One score a day counts."),
        (GameMode.Practice, "Practice Range", "Training targets that never fight back. Try your rotations and watch the damage meter."),
        (GameMode.Tutorial, "Tutorial", "Learn to move, fight, evade, fly and chain, then clear one small wave."),
        (GameMode.Rescue, "Rescue", "Three COG soldiers are stranded in the street. Keep them alive for six waves, until the King Raven lifts them out."),
        (GameMode.Custom, "Custom Waves", "Build your own waves: pick the Locust and the boss, then fight five rounds of them."),
    };
    public static string Name(GameMode m) => All.First(x => x.mode == m).name;
    public static string DailyKey => $"daily-{DateTime.Now:yyyy-MM-dd}";
    public static int DailySeed => DateTime.Now.Year * 1000 + DateTime.Now.DayOfYear;
}

/// A COG ammo van to defend (Siege Defence).
sealed class Van
{
    public Vector3 Pos;
    public float Hp, MaxHp, Flash;
    public bool Survivors; // Rescue: three stranded COG soldiers instead of the ammo van
}

sealed partial class World
{
    public GameMode Mode = GameMode.Campaign;
    public bool Hardcore;
    public string Modifier = "none";
    public Van? Van;
    public float Score;
    public float RunClock;

    public int WaveCount => Mode switch
    {
        GameMode.Endless => int.MaxValue,
        GameMode.BossRush => 4,
        GameMode.Practice => 0,
        GameMode.Tutorial => 1,
        GameMode.Custom => 5,
        GameMode.Rescue => 6,
        _ => Sheets.Waves.Length,
    };

    public string WaveLabel => WaveCount == int.MaxValue ? $"Wave {Wave + 1}" : $"Wave {Math.Min(Wave + 1, WaveCount)}/{WaveCount}";

    static readonly string[] Bosses = { "vrak", "corpser", "vrak-scorched", "brumak" };
    static readonly string[] ModifierList = { "Double Tickers", "Armoured", "Frenzy", "Bloodlust", "Glass Cannons", "Swarm", "Abyss Storm" };

    /// The wave plan for any wave of any mode: the sheet's waves, or ones made up from them.
    public WaveRow WaveInfo(int i)
    {
        var sheet = Sheets.Waves;
        switch (Mode)
        {
            case GameMode.Tutorial: return new WaveRow(1, "Your first Locust", 3, 0, 0, 0.8f, 0, 0, "none", "none");
            case GameMode.Custom:
            {
                var c = Profile.P.CustomWave;
                int N(string id) => c.GetValueOrDefault(id);
                var extra = string.Join(" ", c.Where(kv => kv.Value > 0 && kv.Key is not ("wretch" or "drone" or "boomer" or "ticker" or "reaver" or "boss")).Select(kv => $"{kv.Key}:{kv.Value}"));
                var boss = Profile.P.CustomBoss;
                return new WaveRow(i + 1, $"Custom wave {i + 1}", N("wretch"), N("drone"), N("boomer"), 1 + i * 0.15f, N("ticker"), N("reaver"), extra == "" ? "none" : extra, i == 4 && boss != "none" ? boss : "none");
            }
            case GameMode.BossRush:
                return new WaveRow(i + 1, $"Boss {i + 1}: {Sheets.Enemies.First(e => e.Id == Bosses[Math.Min(i, 3)]).Name}", 3 + i, 1 + i, 0, 1 + i * 0.1f, 2, 0, "none", Bosses[Math.Min(i, 3)]);
            case GameMode.Endless when i >= sheet.Length:
            {
                var n = i + 1;
                var rng = new Random(n * 7919);
                var types = new[] { "wretch-armored", "grenadier", "flooder", "drone-sniper", "drone-shotgun", "sporematron", "seeder", "broodshifter" };
                var extra = string.Join(" ", types.OrderBy(_ => rng.Next()).Take(2 + n / 6).Select(t => $"{t}:{1 + rng.Next(1 + n / 8)}"));
                var boss = n % 5 == 0 ? Bosses[rng.Next(Bosses.Length)] : "none";
                return new WaveRow(n, boss != "none" ? "The horde brings a champion" : $"The horde, wave {n}", 8 + n, 3 + n / 2, n / 3, 1.75f + (n - sheet.Length) * 0.12f, 3 + n / 2, n / 4, extra, boss);
            }
            default: return sheet[Math.Clamp(i, 0, sheet.Length - 1)];
        }
    }

    /// Sets up a run of this mode (call right after making the world).
    public void SetMode(GameMode mode, bool hardcore)
    {
        Mode = mode; Hardcore = hardcore;
        switch (mode)
        {
            case GameMode.BossRush:
                while (P.Level < 10) GainXp(LevelRow.XpToNext - P.Xp);
                Banners.Clear(); Effects.Clear();
                break;
            case GameMode.Siege:
                PlaceVan();
                break;
            case GameMode.Rescue:
                PlaceVan(survivors: true);
                break;
            case GameMode.Practice:
                Squad = null;
                Breather = float.MaxValue;
                for (var i = 0; i < 3; i++) SpawnDummy(i);
                break;
            case GameMode.Tutorial:
                Squad = null;
                Breather = float.MaxValue;
                SpawnDummy(1);
                break;
        }
        if (hardcore) { P.Salvation = 0; Banner("HARDCORE\nOne life. No potions. Better loot."); }
        if (mode == GameMode.Campaign) QueueRadio(Story.Intro);
    }

    // ---------------------------------------------------------------- practice targets

    void SpawnDummy(int i)
    {
        var def = Sheets.Enemies.First(e => e.Id == "dummy");
        var at = P.Pos + P.Forward * 9 + P.Right * (i - 1) * 4;
        for (var k = 0; k < 20 && Blocked(at, 0.8f); k++) at += P.Forward * 0.5f;
        var e = new Enemy { Def = def, Pos = at with { Y = 0 }, Hp = def.Hp, MaxHp = def.Hp, AttackCd = 999 };
        Enemies.Add(e);
    }

    // ---------------------------------------------------------------- Siege Defence: the ammo van

    void PlaceVan(bool survivors = false)
    {
        for (var tries = 0; tries < 600; tries++)
        {
            // near the start first, then further out, with a little less room needed as it goes
            var reach = 16 + tries * 0.1f;
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * reach, 0, ((float) _rng.NextDouble() - 0.5f) * reach);
            if (Dist2(at, P.Pos) < 5 || Blocked(at, tries < 300 ? 3.2f : 2.4f)) continue;
            var hp = (survivors ? 2400 : 3000) * DifficultyHp;
            Van = new Van { Pos = at, Hp = hp, MaxHp = hp, Survivors = survivors };
            // the van blocks like a vehicle; the survivors crouch behind a low sandbag ring
            Cover.Add(survivors ? new BoundingBox(at - new Vector3(1.6f, 0, 1.6f), at + new Vector3(1.6f, 0.9f, 1.6f))
                                : new BoundingBox(at - new Vector3(1.3f, 0, 2.6f), at + new Vector3(1.3f, 2.6f, 2.6f)));
            _navOpen = null; _navDist = null; // it blocks the street now
            Banner(survivors ? "RESCUE THE STRANDED SQUAD\nHold for six waves" : "DEFEND THE AMMO VAN");
            if (survivors) QueueRadio([("COG Control", "Squad pinned down in your street. Raven inbound, six waves out. Keep them breathing.")]);
            return;
        }
    }

    /// Siege Defence: some Locust go for the van instead of you.
    bool GoesForVan(Enemy e) => Van != null && Van.Hp > 0 && e.Def.Special != "boss" && e.Taunt <= 0 && (e.GetHashCode() & 1) == 0;

    void HitVan(float dmg)
    {
        if (Van == null || Van.Hp <= 0 || Lost) return;
        Van.Hp -= dmg; Van.Flash = 0.15f;
        Bits.Emit(Van.Pos + Vector3.UnitY * 1.5f, 6, new Color(255, 200, 120, 255), 4f, 0.05f, 0.4f, 6f, glow: true);
        if (Van.Hp <= 0)
        {
            Lost = true;
            Banner(Van.Survivors ? "THE SQUAD IS DOWN" : "THE AMMO VAN IS GONE");
            Bits.Emit(Van.Pos + Vector3.UnitY, 80, new Color(255, 150, 50, 255), 9f, 0.15f, 1.2f, 3f, glow: true);
            AddShake(0.8f);
        }
    }

    void DrawVan()
    {
        DrawSandbags();
        DrawStation();
        if (Van == null) return;
        var v = Van.Pos;
        if (Van.Survivors) { DrawSurvivors(); return; }
        var hull = Van.Flash > 0 ? new Color(150, 120, 100, 255) : Van.Hp <= 0 ? new Color(40, 36, 34, 255) : new Color(70, 78, 60, 255);
        Raylib.DrawModelEx(_block, v + new Vector3(0, 1.25f, -0.4f), Vector3.UnitY, 0, new Vector3(2.4f, 1.9f, 3.6f), hull);
        Raylib.DrawModelEx(_block, v + new Vector3(0, 0.95f, 1.9f), Vector3.UnitY, 0, new Vector3(2.3f, 1.3f, 1.3f), hull);
        Raylib.DrawModelEx(_block, v + new Vector3(0, 1.45f, 2.3f), Vector3.UnitY, 0, new Vector3(2.0f, 0.5f, 0.3f), new Color(30, 34, 40, 255));
        foreach (var (x, z) in new[] { (-1.15f, -1.6f), (1.15f, -1.6f), (-1.15f, 1.7f), (1.15f, 1.7f) })
            Raylib.DrawModelEx(_cyl, v + new Vector3(x, 0.05f, z), Vector3.UnitX, 90, new Vector3(0.42f, 0.25f, 0.42f), new Color(22, 22, 24, 255));
        if (Van.Hp > 0) Glow.Sprite(v + new Vector3(0, 2.3f, 0), 0.35f, new Color(255, 180, 60, 200 + (int) (55 * MathF.Sin(TimeAlive * 4))));
        Glow.Decal(v, 3.4f, new Color(90, 160, 255, 70), ring: true);
    }

    /// Rescue: three COG soldiers crouched in a sandbag ring, aiming out (down when the squad's HP is gone).
    void DrawSurvivors()
    {
        var v = Van!.Pos;
        string[] who = ["carmine", "dom", "marcus"];
        for (var i = 0; i < 3; i++)
        {
            if (!_assets.Characters.TryGetValue(who[i], out var cm)) continue;
            var a = i * MathF.Tau / 3 + 0.4f;
            var at = v + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.8f;
            var yaw = a * 180 / MathF.PI + 90;
            var down = Van.Hp <= 0 ? -1.35f : 0;
            DrawCharacter(cm, Clip.Aim, (float) Raylib.GetTime() / MathF.Max(0.1f, cm.Duration[Clip.Aim]) + i, at + new Vector3(0, GroundAt(at), 0), yaw, down, Van.Flash > 0 ? new Color(255, 190, 170, 255) : Color.White);
        }
        if (ShadowPass) return;
        for (var k = 0; k < 10; k++)
        {
            var a = k * MathF.Tau / 10;
            Raylib.DrawModelEx(_block, v + new Vector3(MathF.Cos(a) * 1.6f, 0.4f, MathF.Sin(a) * 1.6f), Vector3.UnitY, -a * 180 / MathF.PI, new Vector3(0.5f, 0.8f, 1.05f), new Color(120, 108, 86, 255));
        }
        Glow.Decal(v, 2.6f, new Color(90, 160, 255, 70), ring: true);
    }

    // ---------------------------------------------------------------- the COG supply station

    public Vector3? Station;
    public bool NearStation => Station is { } s && Dist2(s, P.Pos) < 3.2f && !WaveActive && !Won && !Lost;

    void PlaceStation()
    {
        for (var tries = 0; tries < 200; tries++)
        {
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 14, 0, ((float) _rng.NextDouble() - 0.5f) * 14);
            if (Dist2(at, P.Pos) < 4 || Blocked(at, 1.6f)) continue;
            Station = at;
            Cover.Add(new BoundingBox(at - new Vector3(0.8f, 0, 0.6f), at + new Vector3(0.8f, 1.2f, 0.6f)));
            _navOpen = null; _navDist = null;
            return;
        }
    }

    void DrawStation()
    {
        if (Station is not { } s) return;
        Raylib.DrawModelEx(_block, s + new Vector3(0, 0.5f, 0), Vector3.UnitY, 0, new Vector3(1.6f, 1.0f, 1.2f), new Color(70, 84, 64, 255));
        Raylib.DrawModelEx(_block, s + new Vector3(0, 1.08f, 0), Vector3.UnitY, 0, new Vector3(1.66f, 0.16f, 1.26f), new Color(52, 60, 48, 255));
        Raylib.DrawModelEx(_block, s + new Vector3(0.3f, 1.35f, 0), Vector3.UnitY, 0, new Vector3(0.6f, 0.4f, 0.5f), new Color(160, 140, 90, 255));
        if (!WaveActive) { Glow.Sprite(s + new Vector3(0, 1.9f, 0), 0.4f, new Color(120, 200, 255, 220)); Glow.Decal(s, 2.2f, new Color(120, 200, 255, 90), ring: true); }
    }

    // ---------------------------------------------------------------- fortifications (between waves)

    public readonly List<BoundingBox> Sandbags = new();
    public const int SandbagCost = 150;

    /// Drops a sandbag wall in front of the hero between waves (Kinah, up to six a run).
    public string Fortify()
    {
        if (WaveActive || Won || Lost) return "Only between waves";
        if (Sandbags.Count >= 6) return "No more sandbags this run";
        if (Profile.P.Kinah < SandbagCost) return $"Needs {SandbagCost} Kinah";
        var f = P.Forward;
        var along = MathF.Abs(f.X) > MathF.Abs(f.Z); // the wall runs across the way you face
        var at = P.Pos + f * 2.2f;
        var half = along ? new Vector3(0.35f, 0, 1.3f) : new Vector3(1.3f, 0, 0.35f);
        var box = new BoundingBox(at - half, at + half + new Vector3(0, 1.1f, 0));
        if (Blocked(at, 0.6f)) return "No room there";
        Profile.P.Kinah -= SandbagCost;
        Sandbags.Add(box);
        Cover.Add(box);
        _navOpen = null; _navDist = null;
        Bits.Emit(at + Vector3.UnitY * 0.5f, 20, new Color(150, 130, 100, 200), 2f, 0.2f, 0.9f, 3f);
        Sfx.Play(Sfx.Click);
        return $"Sandbags placed ({Sandbags.Count}/6)";
    }

    void DrawSandbags()
    {
        foreach (var b in Sandbags)
        {
            var size = b.Max - b.Min;
            var c = (b.Min + b.Max) / 2;
            var alongX = size.X > size.Z;
            var n = 5;
            for (var row = 0; row < 3; row++)
                for (var i = 0; i < n - (row == 2 ? 1 : 0); i++)
                {
                    var t = (i + 0.5f + (row == 1 ? 0.5f : 0)) / n - 0.5f;
                    var p = c + (alongX ? new Vector3(t * size.X, 0, 0) : new Vector3(0, 0, t * size.Z));
                    p.Y = b.Min.Y + 0.2f + row * 0.36f;
                    var s = alongX ? new Vector3(size.X / n * 0.98f, 0.36f, size.Z) : new Vector3(size.X, 0.36f, size.Z / n * 0.98f);
                    var shade = (byte) (128 + ((i + row) % 3) * 12);
                    Raylib.DrawModelEx(_block, p, Vector3.UnitY, 0, s, new Color(shade, (byte) (shade - 18), (byte) (shade - 46), (byte) 255));
                }
        }
    }

    // ---------------------------------------------------------------- modifiers, score, records

    void PickModifier(int i)
    {
        Modifier = "none";
        if (Mode == GameMode.Daily) Modifier = ModifierList[new Random(Modes.DailySeed + i).Next(ModifierList.Length)];
        else if (Mode == GameMode.Endless && i >= 3) Modifier = ModifierList[new Random(i * 31 + 7).Next(ModifierList.Length)];
    }

    void ApplyModifierToSpawn(Enemy e)
    {
        switch (Modifier)
        {
            case "Frenzy": e.SpeedMult *= 1.3f; break;
            case "Glass Cannons": e.MaxHp *= 0.6f; e.Hp = MathF.Min(e.Hp, e.MaxHp); e.GlassCannon = true; break;
            case "Armoured": e.ArmourBonus = 25; break;
        }
    }

    void AddScore(Enemy e) => Score += (e.Elite ? 2 : 1) * e.Def.Xp * (1 + Wave * 0.1f) * (Hardcore ? 1.5f : 1) * (Settings.S.Difficulty switch { 0 => 0.6f, 2 => 1.4f, 3 => 2f, _ => 1f });

    void FinishScore()
    {
        if (Won && Mode is GameMode.Campaign or GameMode.TimeAttack or GameMode.BossRush or GameMode.Daily)
            Score += MathF.Max(0, (Mode == GameMode.TimeAttack ? 60000 : 20000) - RunClock * (Mode == GameMode.TimeAttack ? 60 : 20));
        var key = Mode == GameMode.Daily ? Modes.DailyKey : Mode.ToString() + (Hardcore ? "-hardcore" : "");
        if (!Profile.P.Leaderboards.TryGetValue(key, out var board)) Profile.P.Leaderboards[key] = board = new();
        var entry = new ScoreEntry { Class = P.Cls.Name, Score = (int) Score, Wave = Wave + (Won ? 0 : 1), Seconds = (int) RunClock, Date = DateTime.Now.ToString("yyyy-MM-dd") };
        board.Add(entry);
        board.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (board.Count > 10) board.RemoveRange(10, board.Count - 10);
        BoardPlace = board.IndexOf(entry) + 1;
    }
    public int BoardPlace;

    // ---------------------------------------------------------------- tutorial

    public static readonly (string text, string padText)[] TutorialSteps =
    {
        ("Move with W A S D and look with the mouse", "Move with the left stick and look with the right"),
        ("Left click: basic attack the training target", "RT: basic attack the training target"),
        ("Press 1: use your first skill", "D-pad up: use your first skill"),
        ("Press Tab: lock on to a target", "RS click: lock on to a target"),
        ("Press Space: evade (dodge rolls make you untouchable)", "B: evade (dodge rolls make you untouchable)"),
        ("Press F: take flight with your Daeva wings", "Y: take flight with your Daeva wings"),
        ("Land a skill, then press it again for its chain", "Land a skill, then press it again for its chain"),
    };
    public int TutorialStep;
    readonly HashSet<string> _seen = new();

    void UpdateTutorial()
    {
        if (Mode != GameMode.Tutorial || TutorialStep >= TutorialSteps.Length) return;
        var done = TutorialStep switch
        {
            0 => P.MoveBob > 3,
            1 => _seen.Contains("basic-attack"),
            2 => _seen.Contains("skill-cast"),
            3 => Locked != null,
            4 => _seen.Contains("evade"),
            5 => _seen.Contains("flight"),
            _ => _seen.Contains("chain-skill") || ChainCasts > 0,
        };
        if (!done) return;
        TutorialStep++;
        Sfx.Play(Sfx.LevelUp);
        if (TutorialStep >= TutorialSteps.Length)
        {
            Banner("Well done, Daeva.\nNow the real thing.");
            Enemies.RemoveAll(e => e.Def.Id == "dummy");
            Breather = 4;
        }
    }

    // ---------------------------------------------------------------- radio

    public readonly Queue<(string who, string line)> Radio = new();
    public (string who, string line)? RadioNow;
    public float RadioTime;

    public void QueueRadio(IEnumerable<(string who, string line)> lines) { foreach (var l in lines) Radio.Enqueue(l); }

    void UpdateRadio(float dt)
    {
        if (RadioTime > 0) { RadioTime -= dt; return; }
        RadioNow = Radio.Count > 0 ? Radio.Dequeue() : null;
        if (RadioNow != null) RadioTime = 3.2f + RadioNow.Value.line.Length * 0.035f;
    }

    void UpdateModes(float dt)
    {
        if (!Lost && !Won) RunClock += dt;
        if (Van != null && Van.Flash > 0) Van.Flash -= dt;
        UpdateRadio(dt);
        UpdateTutorial();
        if (Mode == GameMode.Practice)
            for (var i = Enemies.Count(e => e.Def.Id == "dummy"); i < 3; i++) SpawnDummy(i);
        if ((Won || Lost) && !_finished)
        {
            _finished = true;
            FinishScore();
            if (Mode == GameMode.Campaign && Won) QueueRadio(Hardcore ? Story.SecretEnding : Story.Ending);
        }
    }
    bool _finished;
}

sealed class ScoreEntry
{
    public string Class { get; set; } = "";
    public int Score { get; set; }
    public int Wave { get; set; }
    public int Seconds { get; set; }
    public string Date { get; set; } = "";
}
