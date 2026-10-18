using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// The AION-style cube: the character sheet, the bag (equip, enchant, manastones, salvage), the vendor between
/// waves, crafting and achievements. I (or LB in the pause menu) opens it; the game waits while it's open.
static class BagScreen
{
    public static bool Open;
    static int _tab, _row, _scroll;
    static string _message = "";
    static float _messageTime;
    static readonly string[] Tabs = { "Character", "Bag", "Vendor", "Crafting", "Missions", "Stats", "Achievements", "Codex", "Looks" };
    const int TabMissions = 4, TabStats = 5, TabAchievements = 6, TabCodex = 7, TabLooks = 8;
    static readonly string[] StatRows = { "power", "hp", "crit", "speed" };

    /// Opens straight onto a tab (the supply station opens Crafting).
    public static void OpenAt(int tab) { Open = true; _tab = tab; _row = 0; _scroll = 0; }
    static readonly Random Rng = new();
    static Hud _hud = null!;

    static void Say(string m) { _message = m; _messageTime = 3; }

    static List<Item> BagItems(World w) =>
        Profile.P.Bag.OrderByDescending(i => Profile.Equipped(w.P.Cls.Id, i.Slot) == i).ThenByDescending(i => i.Rarity).ThenBy(i => Array.IndexOf(Profile.Slots, i.Slot)).ToList();

    static int RowCount(World w) => _tab switch
    {
        0 => Profile.Titles.Length + 1,
        1 => BagItems(w).Count,
        2 => w.WaveActive ? 0 : Profile.Stock(w.P.Cls.Id, w.Wave, w.P.Level).Count,
        3 => Profile.Recipes.Length,
        TabMissions => Progression.Board().Count,
        TabStats => StatRows.Length + 1,
        TabAchievements => Profile.AchievementList.Length,
        TabCodex => Sheets.Enemies.Length + Story.Lore.Length,
        _ => World.WingSkins.Length + Progression.Dyes.Length,
    };

    public static void Update(World w)
    {
        if (!Open) return;
        _messageTime -= Raylib.GetFrameTime();
        var n = RowCount(w);
        var (pdx, pdy) = Pad.MenuNav();
        if (Inp.IsKeyPressed(KeyboardKey.E) || Inp.IsKeyPressed(KeyboardKey.Right) || Pad.Pressed(GamepadButton.RightTrigger1) || pdx > 0) { _tab = (_tab + 1) % Tabs.Length; _row = 0; _scroll = 0; Sfx.Play(Sfx.Click); }
        if (Inp.IsKeyPressed(KeyboardKey.Q) || Inp.IsKeyPressed(KeyboardKey.Left) || Pad.Pressed(GamepadButton.LeftTrigger1) || pdx < 0) { _tab = (_tab + Tabs.Length - 1) % Tabs.Length; _row = 0; _scroll = 0; Sfx.Play(Sfx.Click); }
        if (n > 0)
        {
            if (Inp.IsKeyPressed(KeyboardKey.Down) || Inp.IsKeyPressed(KeyboardKey.S) || pdy > 0) _row = (_row + 1) % n;
            if (Inp.IsKeyPressed(KeyboardKey.Up) || Inp.IsKeyPressed(KeyboardKey.W) || pdy < 0) _row = (_row + n - 1) % n;
            _row = Math.Clamp(_row, 0, n - 1);
            var wheel = Inp.GetMouseWheelMove();
            if (wheel != 0) _row = Math.Clamp(_row - (int) wheel, 0, n - 1);
        }
        // mouse: tabs and rows
        var m = Ui.Mouse;
        for (var i = 0; i < Tabs.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, TabRect(i)) && Inp.IsMouseButtonPressed(MouseButton.Left)) { _tab = i; _row = 0; _scroll = 0; }
        var clicked = false;
        for (var i = 0; i < VisibleRows; i++)
            if (_scroll + i < n && Raylib.CheckCollisionPointRec(m, RowRect(i)) && Inp.IsMouseButtonPressed(MouseButton.Left))
            {
                clicked = _row == _scroll + i; // a second click acts
                _row = _scroll + i;
            }
        if (_row < _scroll) _scroll = _row;
        if (_row >= _scroll + VisibleRows) _scroll = _row - VisibleRows + 1;

        var act = Inp.IsKeyPressed(KeyboardKey.Enter) || Pad.Pressed(GamepadButton.RightFaceDown) || clicked;
        var enchant = Inp.IsKeyPressed(KeyboardKey.N) || (!PadAlt && Pad.Pressed(GamepadButton.RightFaceUp));
        var socket = Inp.IsKeyPressed(KeyboardKey.M) || (PadAlt && Pad.Pressed(GamepadButton.RightFaceUp));
        var salvage = Inp.IsKeyPressed(KeyboardKey.X) || Pad.Pressed(GamepadButton.RightFaceLeft);
        foreach (var (rect, key) in ActionButtons())
            if (Raylib.CheckCollisionPointRec(m, rect) && Inp.IsMouseButtonPressed(MouseButton.Left))
            {
                if (key == "act") act = true; else if (key == "enchant") enchant = true; else if (key == "socket") socket = true; else if (key == "salvage") salvage = true;
            }
        switch (_tab)
        {
            case 0 when act:
                Profile.P.Title = _row == 0 ? "" : Profile.P.Achievements.Contains(Profile.Titles[_row - 1].Needs) ? Profile.Titles[_row - 1].Id : Profile.P.Title;
                if (_row > 0 && Profile.P.Title != Profile.Titles[_row - 1].Id) Say("Earn the achievement first");
                Profile.Save(); w.ApplyGear();
                break;
            case 1 when n > 0:
            {
                var it = BagItems(w)[_row];
                if (act)
                {
                    if (it.Slot == "weapon" && it.Class != w.P.Cls.Id) Say($"Only a {Sheets.Classes.First(c => c.Id == it.Class).Name} can use this");
                    else if (Profile.Equipped(w.P.Cls.Id, it.Slot) == it) { Profile.Unequip(w.P.Cls.Id, it.Slot); Say("Taken off"); }
                    else { Profile.Equip(w.P.Cls.Id, it); Say($"Equipped {it.FullName}"); }
                    Profile.Save(); w.ApplyGear(); Sfx.Play(Sfx.Click);
                }
                if (enchant)
                {
                    var r = Profile.Enchant(it, Rng); Say(r); w.ApplyGear();
                    Sfx.Play(r.StartsWith("Success") ? Sfx.LevelUp : Sfx.Hurt);
                    if (it.Enchant >= 10 && Profile.Unlock("enchant-10") is { } a) w.Say($"Achievement: {a}", new Color(255, 214, 110, 255));
                }
                if (socket)
                {
                    var stone = Profile.P.Materials.Where(kv => kv.Key.StartsWith("stone:") && kv.Value > 0).Select(kv => kv.Key).OrderByDescending(s => s).FirstOrDefault();
                    Say(stone == null ? "No manastones (craft or buy one)" : Profile.Socket(it, stone)); w.ApplyGear();
                }
                if (salvage) { Say(Profile.Salvage(it)); _row = Math.Max(0, _row - 1); }
                break;
            }
            case 2 when act && n > 0:
                Say(Profile.Buy(Profile.Stock(w.P.Cls.Id, w.Wave, w.P.Level)[_row])); Sfx.Play(Sfx.Click);
                break;
            case TabLooks when act:
            {
                if (_row < World.WingSkins.Length)
                {
                    var skin = World.WingSkins[_row];
                    if (!World.WingUnlocked(skin)) Say("Earn its achievement first");
                    else { Profile.P.Wings = skin.Id; Profile.Save(); Say($"{skin.Name} on"); }
                }
                else
                {
                    var dye = Progression.Dyes[_row - World.WingSkins.Length];
                    if (!Progression.DyeOpen(dye.needs)) Say("Earn its achievement first");
                    else { Profile.P.Dye = dye.id; Profile.Save(); Say($"{dye.name} on"); }
                }
                break;
            }
            case TabMissions when act:
                Say(Progression.Claim(Progression.Board()[_row]));
                break;
            case TabStats when act:
                if (_row < StatRows.Length)
                {
                    if (w.StatPoints <= 0) Say("No stat points left (one per level)");
                    else { w.StatSpend[StatRows[_row]]++; w.ApplyGear(); Say("Point spent"); }
                }
                else if (w.StatSpend.Values.Sum() == 0) Say("Nothing to reset");
                else if (Profile.P.Kinah < 100) Say("A reset costs 100 Kinah");
                else
                {
                    Profile.P.Kinah -= 100;
                    foreach (var k in StatRows) w.StatSpend[k] = 0;
                    w.ApplyGear();
                    Say("Stat points refunded");
                }
                break;
            case 3 when act:
                Say(Profile.Craft(Profile.Recipes[_row], w.P.Cls.Id, w.P.Level, Rng)); Sfx.Play(Sfx.Click);
                break;
        }
        if (Inp.IsKeyPressed(KeyboardKey.Escape) || Inp.IsKeyPressed(KeyboardKey.I) || Pad.Cancel || Pad.Back) { Open = false; Profile.Save(); Sfx.Play(Sfx.Click); }
    }

    static bool PadAlt => Pad.Available && Inp.GetGamepadAxisMovement(0, GamepadAxis.LeftTrigger) > 0.3f;

    const int VisibleRows = 14;
    static float Sw => Ui.W;
    static float Sh => Ui.H;
    static float Left => Sw / 2 - 640;
    static Rectangle TabRect(int i) => new(Left + i * (1280f / Tabs.Length), 70, 1280f / Tabs.Length - 6, 40);
    static Rectangle RowRect(int i) => new(Left, 130 + i * 40, 600, 36);

    static IEnumerable<(Rectangle, string)> ActionButtons()
    {
        var y = 130 + VisibleRows * 40 + 16;
        var labels = _tab switch
        {
            0 => new[] { ("act", "Use title") },
            1 => new[] { ("act", "Equip"), ("enchant", "Enchant"), ("socket", "Manastone"), ("salvage", "Salvage") },
            2 => new[] { ("act", "Buy") },
            3 => new[] { ("act", "Craft") },
            TabLooks => new[] { ("act", "Wear") },
            TabMissions => new[] { ("act", "Claim") },
            TabStats => new[] { ("act", "Spend / reset") },
            _ => Array.Empty<(string, string)>(),
        };
        for (var i = 0; i < labels.Length; i++) yield return (new Rectangle(Left + i * 150, y, 140, 36), labels[i].Item1);
    }

    static string ButtonLabel(string key) => key switch
    {
        "act" => _tab switch { 0 => "Use title", 1 => "Equip", 2 => "Buy", TabLooks => "Wear", TabMissions => "Claim", TabStats => "Spend", _ => "Craft" } + (Pad.Active ? " (A)" : " (Enter)"),
        "enchant" => Pad.Active ? "Enchant (Y)" : "Enchant (N)",
        "socket" => Pad.Active ? "Manastone (LT+Y)" : "Manastone (M)",
        _ => Pad.Active ? "Salvage (X)" : "Salvage (X)",
    };

    public static void Draw(World w, Hud hud)
    {
        if (!Open) return;
        _hud = hud;
        Raylib.DrawRectangle(0, 0, (int) Sw, (int) Sh, new Color(8, 8, 12, 225));
        var p = Profile.P;
        hud.Text($"{p.Kinah:N0} Kinah", Left, 24, 26, new Color(255, 214, 110, 255));
        hud.Text($"Chitin {Profile.Mat("chitin")}   Imulsion {Profile.Mat("imulsion")}   Enchant stones {Profile.Mat("enchant")}   Potions {Profile.Mat("hp-potion")}/{Profile.Mat("mp-potion")}/{Profile.Mat("serum")}",
            Left + 260, 30, 18, new Color(210, 205, 195, 255));
        for (var i = 0; i < Tabs.Length; i++)
        {
            var r = TabRect(i);
            Raylib.DrawRectangleRec(r, i == _tab ? new Color(80, 66, 40, 240) : new Color(30, 30, 36, 220));
            if (i == _tab) Raylib.DrawRectangleLinesEx(r, 2, new Color(255, 214, 110, 255));
            hud.TextCentered(Tabs[i], r.X + r.Width / 2, r.Y + 10, 18, i == _tab ? new Color(255, 225, 150, 255) : Color.White);
        }
        var n = RowCount(w);
        var right = new Rectangle(Left + 620, 130, 660, VisibleRows * 40 - 4);
        Raylib.DrawRectangleRec(right, new Color(22, 22, 28, 230));
        switch (_tab)
        {
            case 0: DrawCharacter(w, right); break;
            case 1: DrawBag(w, right); break;
            case 2: DrawVendor(w, right); break;
            case 3: DrawCrafting(right); break;
            case TabMissions: DrawMissions(right); break;
            case TabStats: DrawStats(w, right); break;
            case TabAchievements: DrawAchievements(right); break;
            case TabCodex: DrawCodex(right); break;
            default: DrawLooks(right); break;
        }
        foreach (var (rect, key) in ActionButtons())
        {
            Raylib.DrawRectangleRec(rect, new Color(50, 46, 40, 240));
            Raylib.DrawRectangleLinesEx(rect, 1, new Color(160, 140, 100, 255));
            hud.TextCentered(ButtonLabel(key), rect.X + rect.Width / 2, rect.Y + 9, 17, Color.White);
        }
        if (_messageTime > 0) hud.TextCentered(_message, Sw / 2, Sh - 80, 24, new Color(255, 225, 150, 255));
        hud.TextCentered(Pad.Active ? "LB/RB: tabs   D-pad: choose   B: close" : "Q/E or Left/Right: tabs   Up/Down or click: choose   I or Esc: close", Sw / 2, Sh - 40, 18, new Color(190, 190, 200, 255));
    }

    static void Row(int i, string text, Color col, string right = "")
    {
        var r = RowRect(i - _scroll);
        if (i - _scroll < 0 || i - _scroll >= VisibleRows) return;
        Raylib.DrawRectangleRec(r, i == _row ? new Color(70, 60, 40, 230) : new Color(30, 30, 36, 210));
        if (i == _row) Raylib.DrawRectangleLinesEx(r, 2, new Color(255, 214, 110, 255));
        _hud.Text(text, r.X + 12, r.Y + 8, 19, col);
        if (right != "") { var s = _hud.Measure(right, 17); _hud.Text(right, r.X + r.Width - 12 - s.X, r.Y + 9, 17, new Color(200, 200, 210, 255)); }
    }

    static void Lines(Rectangle box, IEnumerable<(string text, Color col)> lines)
    {
        var y = box.Y + 14;
        foreach (var (t, c) in lines) { _hud.Text(t, box.X + 16, y, 20, c); y += 27; }
    }

    static readonly Color Dim = new(190, 190, 200, 255), Gold = new(255, 214, 110, 255), Good = new(130, 230, 120, 255), Bad = new(255, 120, 100, 255);

    static void DrawCharacter(World w, Rectangle box)
    {
        var p = Profile.P;
        Row(0, "No title", p.Title == "" ? Gold : Color.White);
        for (var i = 0; i < Profile.Titles.Length; i++)
        {
            var t = Profile.Titles[i];
            var have = p.Achievements.Contains(t.Needs);
            Row(i + 1, (p.Title == t.Id ? "> " : "") + t.Name, have ? (p.Title == t.Id ? Gold : Color.White) : new Color(110, 110, 120, 255), Profile.StatText(t.Stat, t.Value));
        }
        var lines = new List<(string, Color)>
        {
            ($"{w.P.Cls.Name}  -  level {w.P.Level}" + (p.Title != "" ? $"  -  {Profile.Titles.First(t => t.Id == p.Title).Name}" : ""), World.Hex(w.P.Cls.Color)),
            ($"Abyss rank: {Profile.AbyssRank}  ({p.AbyssPoints:N0} AP" + (Profile.NextRankAp > 0 ? $", next at {Profile.NextRankAp:N0})" : ")"), Gold),
            ($"HP {w.P.MaxHp:N0}   MP {w.P.MaxMp:N0}", Color.White),
        };
        lines.Add((string.Join("   ", Profile.StatIds.Where(s => s is not ("hp" or "mp")).Select(s => $"{Profile.StatName(s)} +{w.GearStat(s)}%")), Color.White));
        lines.Add(("", Dim));
        foreach (var slot in Profile.Slots)
        {
            var it = Profile.Equipped(w.P.Cls.Id, slot);
            lines.Add(($"{char.ToUpper(slot[0]) + slot[1..],-9} {(it == null ? "-" : it.FullName)}", it == null ? Dim : Profile.RarityColor(it.Rarity)));
        }
        lines.Add(("", Dim));
        lines.Add(("Locust killed: " + string.Join("   ", Sheets.Enemies.Select(e => $"{e.Name} {p.Kills.GetValueOrDefault(e.Id)}")), Dim));
        if (p.Records.TryGetValue(w.P.Cls.Id, out var rec))
            lines.Add(($"Best wave {rec.BestWave}   Wins {rec.Wins}   Runs {rec.Runs}" + (rec.BestWinSeconds > 0 ? $"   Fastest clear {(int) rec.BestWinSeconds / 60}:{(int) rec.BestWinSeconds % 60:00}" : ""), Dim));
        Lines(box, lines);
    }

    static void ItemLines(List<(string, Color)> lines, Item it, Item? compare)
    {
        lines.Add((it.FullName, Profile.RarityColor(it.Rarity)));
        lines.Add(($"{it.Rarity} {it.Slot}  -  item level {it.Level}" + (it.Class != "" ? $"  -  {Sheets.Classes.First(c => c.Id == it.Class).Name} only" : ""), Dim));
        var total = it.Total();
        var other = compare?.Total() ?? new Dictionary<string, int>();
        foreach (var kv in total)
        {
            var d = kv.Value - other.GetValueOrDefault(kv.Key);
            lines.Add((Profile.StatText(kv.Key, kv.Value) + (compare != null && compare != it && d != 0 ? $"   ({(d > 0 ? "+" : "")}{d})" : ""), compare != null && compare != it ? (d > 0 ? Good : d < 0 ? Bad : Color.White) : Color.White));
        }
        lines.Add(($"Condition {it.Durability}%" + (it.Durability <= 0 ? " - worn out: half stats until repaired" : ""), it.Durability <= 0 ? Bad : Dim));
        lines.Add(($"Manastones {it.Stones.Count}/{it.Sockets}" + (it.Stones.Count > 0 ? ": " + string.Join(", ", it.Stones.Select(s => Profile.StatText(s.Split(':')[0], int.Parse(s.Split(':')[1])))) : ""), Dim));
        if (it.Set != "" && Profile.Sets.FirstOrDefault(s => s.Name == it.Set) is { } set)
            lines.Add(($"Set: 2 pieces {Profile.StatText(set.Two.stat, set.Two.value)}, 4 pieces {Profile.StatText(set.Four.stat, set.Four.value)}", Gold));
        lines.Add(($"Enchant +{it.Enchant}/15: {Profile.EnchantChance(it) * 100:0}% chance, {Profile.EnchantCost(it):N0} Kinah + 1 stone", Dim));
    }

    static void DrawBag(World w, Rectangle box)
    {
        var items = BagItems(w);
        if (items.Count == 0) { Lines(box, new[] { ("Your bag is empty. Locust drop gear, more often the big ones.", Dim) }); return; }
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var worn = Profile.Equipped(w.P.Cls.Id, it.Slot) == it;
            Row(i, (worn ? "[E] " : "") + it.FullName, Profile.RarityColor(it.Rarity), it.Slot);
        }
        var sel = items[Math.Clamp(_row, 0, items.Count - 1)];
        var lines = new List<(string, Color)>();
        ItemLines(lines, sel, Profile.Equipped(w.P.Cls.Id, sel.Slot));
        Lines(box, lines);
    }

    static void DrawVendor(World w, Rectangle box)
    {
        if (w.WaveActive) { Lines(box, new[] { ("The vendor only trades between waves.", Dim), ("Clear this wave, then open the bag again.", Dim) }); return; }
        var stock = Profile.Stock(w.P.Cls.Id, w.Wave, w.P.Level);
        for (var i = 0; i < stock.Count; i++)
            Row(i, stock[i].Name, stock[i].Gear != null ? Profile.RarityColor(stock[i].Gear!.Rarity) : Color.White, $"{stock[i].Price:N0} K");
        var o = stock[Math.Clamp(_row, 0, stock.Count - 1)];
        var lines = new List<(string, Color)> { ("Field quartermaster - new stock after every wave", Gold), ("", Dim) };
        if (o.Gear != null) ItemLines(lines, o.Gear, Profile.Equipped(w.P.Cls.Id, o.Gear.Slot));
        else lines.Add((o.Gives switch
        {
            "hp-potion" => "Restores 35% HP (Z / LB)", "mp-potion" => "Restores 40% MP (X / RB)", "serum" => "Refills your flight time (V / LT+LB)",
            "enchant" => "Used to enchant gear (Bag tab)", _ => "Socket it into gear (Bag tab, Manastone)",
        }, Color.White));
        lines.Add(("", Dim));
        lines.Add(($"You have {Profile.P.Kinah:N0} Kinah", Profile.P.Kinah >= o.Price ? Good : Bad));
        Lines(box, lines);
    }

    static void DrawCrafting(Rectangle box)
    {
        for (var i = 0; i < Profile.Recipes.Length; i++)
        {
            var r = Profile.Recipes[i];
            var can = Profile.Mat("chitin") >= r.Chitin && Profile.Mat("imulsion") >= r.Imulsion && Profile.P.Kinah >= r.Kinah;
            Row(i, r.Name, can ? Color.White : new Color(120, 120, 130, 255), $"{r.Chitin} chitin" + (r.Imulsion > 0 ? $", {r.Imulsion} Imulsion" : "") + (r.Kinah > 0 ? $", {r.Kinah} K" : ""));
        }
        Lines(box, new[]
        {
            ("Daeva crafting", Gold),
            ("Salvage gear in the Bag tab to get Locust chitin;", Dim),
            ("big Locust drop Imulsion crystals.", Dim),
            ("", Dim),
            ($"Chitin: {Profile.Mat("chitin")}   Imulsion: {Profile.Mat("imulsion")}   Kinah: {Profile.P.Kinah:N0}", Color.White),
            ("Crafted gear can come out a grade better.", Dim),
        });
    }

    static void DrawMissions(Rectangle box)
    {
        var board = Progression.Board();
        for (var i = 0; i < board.Count; i++)
        {
            var m = board[i];
            var done = m.Progress >= m.Target;
            Row(i, (m.Claimed ? "[x] " : done ? "[!] " : "[ ] ") + m.Text, m.Claimed ? new Color(120, 120, 130, 255) : done ? Gold : Color.White, $"{m.Progress}/{m.Target}");
        }
        var sel = board[Math.Clamp(_row, 0, board.Count - 1)];
        var (rank, discount, next) = Progression.Reputation();
        Lines(box, new[]
        {
            (sel.Kind switch { "daily" => "Daily mission (new ones tomorrow)", "weekly" => "Weekly challenge (resets on Monday)", _ => "Bounty (stays until claimed)" }, Gold),
            (sel.Text, Color.White),
            ($"Reward: {sel.Reward:N0} Kinah, enchantment stones and COG reputation", Dim),
            ("", Dim),
            ($"COG reputation: {rank} ({Profile.P.Reputation}" + (next > 0 ? $", next at {next})" : ")"), Gold),
            ($"Vendor discount: {discount}%", Dim),
        });
    }

    static void DrawStats(World w, Rectangle box)
    {
        string Label(string id) => id switch { "power" => "Attack  (+2% each)", "hp" => "Health  (+3% HP each)", "crit" => "Crit  (+1% each)", _ => "Speed  (+1% each)" };
        for (var i = 0; i < StatRows.Length; i++) Row(i, Label(StatRows[i]), Color.White, $"{w.StatSpend[StatRows[i]]} points");
        Row(StatRows.Length, "Reset all points (100 Kinah)", new Color(255, 170, 140, 255));
        Lines(box, new[]
        {
            ("Stat points", Gold),
            ($"You get one point each level. Points left: {w.StatPoints}", Color.White),
            ("They last for this run.", Dim),
            ("", Dim),
            ($"Prestige: {Profile.P.Prestige}  (+{Profile.P.Prestige}% damage on every Daeva)", Gold),
            ("Reach level 30 in a run to prestige.", Dim),
        });
    }

    static void DrawLooks(Rectangle box)
    {
        for (var i = 0; i < World.WingSkins.Length; i++)
        {
            var s = World.WingSkins[i];
            var open = World.WingUnlocked(s);
            Row(i, (Profile.P.Wings == s.Id ? "> " : "") + s.Name, open ? (s.Id == "class" ? Color.White : s.Col) : new Color(110, 110, 120, 255), open ? "wings" : "locked");
        }
        for (var i = 0; i < Progression.Dyes.Length; i++)
        {
            var d = Progression.Dyes[i];
            var open = Progression.DyeOpen(d.needs);
            Row(World.WingSkins.Length + i, (Profile.P.Dye == d.id ? "> " : "") + d.name, open ? d.tint : new Color(110, 110, 120, 255), open ? "armour dye" : "locked");
        }
        if (_row >= World.WingSkins.Length)
        {
            var d = Progression.Dyes[Math.Clamp(_row - World.WingSkins.Length, 0, Progression.Dyes.Length - 1)];
            var needD = Profile.AchievementList.FirstOrDefault(a => a.Id == d.needs);
            Lines(box, new[] { (d.name, Gold), (needD == null ? "Free to wear." : $"Unlocked by {needD.Name}: {needD.About}.", Color.White) });
            return;
        }
        var sel = World.WingSkins[Math.Clamp(_row, 0, World.WingSkins.Length - 1)];
        var need = Profile.AchievementList.FirstOrDefault(a => a.Id == sel.Needs);
        Lines(box, new[]
        {
            (sel.Name, Gold),
            (need == null ? "Your class colours (gold once you ascend)." : $"Unlocked by the achievement {need.Name}: {need.About}.", Color.White),
            (World.WingUnlocked(sel) ? "Unlocked" : "Locked", World.WingUnlocked(sel) ? Good : Bad),
        });
    }

    static void DrawCodex(Rectangle box)
    {
        var list = Sheets.Enemies;
        for (var i = 0; i < Story.Lore.Length; i++)
        {
            var have = Profile.P.LorePages.Contains(i);
            Row(list.Length + i, have ? "Lore: " + Story.Lore[i].title : "Lore: ? ? ?", have ? new Color(230, 210, 170, 255) : new Color(110, 110, 120, 255));
        }
        if (_row >= list.Length)
        {
            var page = _row - list.Length;
            if (!Profile.P.LorePages.Contains(page)) { Lines(box, new[] { ("Not found yet. Lore pages glow in the streets.", Dim) }); }
            else
            {
                var lore = new List<(string, Color)> { (Story.Lore[page].title, Gold) };
                foreach (var l in Wrap(Story.Lore[page].text, 58)) lore.Add((l, Color.White));
                Lines(box, lore);
            }
            for (var i = 0; i < list.Length; i++) Row(i, Profile.P.Codex.Contains(list[i].Id) ? list[i].Name : "? ? ?", new Color(170, 170, 180, 255));
            return;
        }
        for (var i = 0; i < list.Length; i++)
        {
            var met = Profile.P.Codex.Contains(list[i].Id);
            Row(i, met ? list[i].Name : "? ? ?", met ? Color.White : new Color(110, 110, 120, 255), met ? $"killed {Profile.P.Kills.GetValueOrDefault(list[i].Id)}" : "");
        }
        var e = list[Math.Clamp(_row, 0, list.Length - 1)];
        if (!Profile.P.Codex.Contains(e.Id)) { Lines(box, new[] { ("Not met yet.", Dim) }); return; }
        var lines = new List<(string, Color)> { (e.Name + (e.Title != "none" ? $"  -  {e.Title}" : ""), Gold) };
        foreach (var l in Wrap(Story.Codex.GetValueOrDefault(e.Id, ""), 58)) lines.Add((l, Color.White));
        lines.Add(("", Dim));
        lines.Add(($"HP {e.Hp}   damage {e.Damage}   armour {e.Armour}%", Dim));
        lines.Add(($"Weak to {e.Weak}   resists {e.Resist}", Dim));
        Lines(box, lines);
    }

    static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line != "") { yield return line; line = ""; }
            line = line == "" ? word : line + " " + word;
        }
        if (line != "") yield return line;
    }

    static void DrawAchievements(Rectangle box)
    {
        var list = Profile.AchievementList;
        for (var i = 0; i < list.Length; i++)
        {
            var have = Profile.P.Achievements.Contains(list[i].Id);
            Row(i, (have ? "[x] " : "[ ] ") + list[i].Name, have ? Gold : new Color(150, 150, 160, 255));
        }
        var a = list[Math.Clamp(_row, 0, list.Length - 1)];
        var title = Profile.Titles.FirstOrDefault(t => t.Needs == a.Id);
        Lines(box, new[]
        {
            (a.Name, Gold), (a.About, Color.White),
            (title != null ? $"Unlocks the title {title.Name} ({Profile.StatText(title.Stat, title.Value)})" : "", Dim),
            ("", Dim), ($"{Profile.P.Achievements.Count} of {list.Length} earned", Dim),
        });
    }
}
