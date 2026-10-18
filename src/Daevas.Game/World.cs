using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

sealed class GunState
{
    public required WeaponRow Def;
    public int Ammo;
    public float Reload, ReloadTotal;
    /// Active Reload: one press per reload; a miss jams the gun.
    public bool Tried, Jammed;
    public float Progress => ReloadTotal > 0 ? 1 - Reload / ReloadTotal : 0;
}

sealed class Player
{
    public required ClassRow Cls;
    public Vector3 Pos;
    public float Yaw, Pitch;
    public float Hp, MaxHp, Mp, MaxMp;
    public int Level = 1, Xp;
    public readonly Dictionary<string, float> Cooldowns = new();
    public float BuffPct, BuffTime, Shield, ShieldTime, AuraTime, AuraTick, AuraPower, AuraRadius;
    /// AION self effects: damage taken down (guard), move speed up (haste), heal over time, stealth, salvation.
    public float Guard, GuardTime, Haste, HasteTime, RegenRate, RegenTime, Stealth, StealthBonus, Salvation;
    /// Basic attack cooldown, and the time into the hero's strike animation (-1 = none).
    public float BasicCd, ActionAnim = -1;
    /// A skill being cast (AION cast bar); moving cancels it.
    public CastState? Casting;
    public required GunState[] Guns;
    public int Gun;
    public float FireCd, HurtFlash, Recoil, MoveBob;
    /// Seconds left of the perfect Active Reload damage bonus.
    public float ActiveBonus;
    /// AION chain skills waiting in each hotbar slot.
    public readonly ChainOffer?[] Chains = new ChainOffer?[8];
    /// AION flight: in the air or not, and the flight time left.
    public bool Flying;
    public float FlightTime = Sheets.FlightSeconds;
    /// Evasion roll: seconds left, cooldown and direction.
    public float EvadeTime, EvadeCd;
    public Vector3 EvadeDir = Vector3.UnitZ;
    public bool Sprinting;
    public Vector3 Forward => new(MathF.Sin(Yaw), 0, MathF.Cos(Yaw));
    public Vector3 Right => new(-MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
}

sealed class CastState
{
    public required SkillRow Skill;
    public int Slot;
    public float Time;
}

/// A spirit or servant a skill summons: follows you and attacks the nearest enemy.
sealed class Ally
{
    public required SkillRow Skill;
    public Vector3 Pos;
    public float Life, AttackCd, Power, Bob, Hp, MaxHp;
    public Color Col;
}

/// A ranger trap waiting on the ground.
sealed class Trap
{
    public required SkillRow Skill;
    public Vector3 Pos;
    public float Life, Power;
    public Color Col;
}

/// A Gears emergence hole: the ground breaks open, the Locust climbs out, the hole collapses.
sealed class EHole
{
    public Vector3 Pos;
    public float Radius, Life, Age;
    public int Seed;
}

sealed class ChainOffer
{
    public required SkillRow Skill;
    public float Time;
}

sealed class Dot
{
    public float PerTick, Timer;
    public int Ticks, Stacks = 1;
    public Color Col;
    public string Source = "";
}

sealed class Enemy
{
    public readonly AnimBlend Blend = new();
    public required EnemyRow Def;
    public Vector3 Pos;
    public float Hp, MaxHp, AttackCd, Stun, Flash, Bob, Strafe = 1;
    public float Yaw, WalkPhase, AttackAnim = -1, Dead = -1;
    /// Seconds into climbing out of an E-hole (-1 once it's out and fighting).
    public float Emerge = -1;
    /// AION crowd control: rooted, slowed, in the air (and for how long in all), knocked down, pushed.
    public float Root, Slow, Air, AirTotal = 1, Down, Stuck;
    public bool Exploded;
    public Vector3 Push;
    /// Specials and bosses: time alive, ability cooldown, sniper aim, boss phase, enrage, Corpser burrowing.
    public float Age, SpecialCd, Aim, SpeedMult = 1, Burrow;
    public int Phase;
    public bool Enraged, Elite;
    /// Silenced (no special attacks), disarmed (no attacks), downed (crawling, waiting to be executed).
    public float Silence, Disarm, Downed;
    /// Wave modifiers: extra armour, glass cannons.
    public float ArmourBonus;
    public bool GlassCannon;
    /// Assassin rune marks on it, and seconds left of a Templar's taunt.
    public int Runes;
    public float Taunt;
    /// Stagger build-up (big Locust and bosses) and the cover spot a Drone is using.
    public float Stagger, CoverTimer, HitAnim, GetUp;
    public Vector3? CoverAt;
    public bool InCover;
    public Vector3 BurrowTo;
    public bool Moving;
    public readonly List<Dot> Dots = new();
    public Vector3 Centre => Pos + new Vector3(0, Def.Size * 0.5f, 0);
    public float Radius => Def.Size * 0.28f;
}

sealed class Shot
{
    public Vector3 Pos, Vel;
    public float Damage, Life, Splash;
    public bool Bile;   // Reaver bile: green, splashes
}

enum FxKind { Tracer, Ring, Beam, Pillar, Burst, Slash }

sealed class Fx
{
    public FxKind Kind;
    public Vector3 A, B;
    public float Radius, Age, Life;
    public Color Col;
    public string Element = "";
}

sealed class Number
{
    public Vector3 Pos;
    public required string Text;
    public Color Col;
    public float Scale, Age;
    public Vector2 Drift;
}

/// The arena, the player, enemies, guns, skills, XP and waves.
sealed partial class World
{
    public readonly Player P;
    public readonly List<Enemy> Enemies = new();
    public readonly List<EHole> Holes = new();
    public readonly List<Enemy> Dying = new();
    public readonly List<Shot> Shots = new();
    public readonly List<Fx> Effects = new();
    public readonly List<Ally> Allies = new();
    public readonly List<Trap> Traps = new();
    public int CcApplied, BasicAttacks;
    public readonly Particles Bits = new();
    /// Camera shake (0-1), and run stats for the end screen.
    public float Shake, DamageDealt, DamageTaken, TimeAlive;

    public void AddShake(float amount)
    {
        Shake = MathF.Min(1, Shake + amount);
        Pad.Rumble(MathF.Min(1, amount * 1.5f), 0.12f + amount * 0.2f);
    }

    static readonly Color Blood = new(110, 18, 14, 235);

    /// A Ticker's blast: hurts the player and any Locust caught in it (chain reactions included).
    void Explode(Enemy t)
    {
        var at = t.Centre;
        Effects.Add(new Fx { Kind = FxKind.Burst, A = at, Radius = t.Def.Splash, Col = new Color(255, 140, 40, 255), Life = 0.5f });
        Effects.Add(new Fx { Kind = FxKind.Ring, A = t.Pos, Radius = t.Def.Splash, Col = new Color(255, 120, 40, 255), Life = 0.5f });
        Bits.Emit(at, 40, new Color(255, 150, 50, 255), 7f, 0.12f, 0.6f, 3f, glow: true);
        Bits.Emit(at, 30, new Color(70, 60, 55, 220), 4f, 0.25f, 1.4f, 1f);
        if (Dist2(P.Pos, t.Pos) <= t.Def.Splash) TakeDamage(EnemyDamage(t) * (t.Exploded ? 1f : 0.5f));
        var blown = 0;
        foreach (var o in Enemies.ToList())
            if (o.Hp > 0 && Dist2(o.Pos, t.Pos) <= t.Def.Splash + o.Radius) { Damage(o, t.Def.Damage * 2.5f, false); if (o.Hp <= 0) blown++; }
        if (blown >= 3) Achieve("ticker-chain");
        Mark(t.Pos, t.Def.Splash * 0.6f, new Color(20, 16, 14, 170));
        DamageCover(t.Pos, t.Def.Splash, 220);
        AddShake(Dist2(P.Pos, t.Pos) < 12 ? 0.45f : 0.15f);
        Sfx.Play(Sfx.Kill);
    }
    public readonly List<Number> Numbers = new();
    public readonly List<BoundingBox> Cover = new();
    public readonly List<string> Banners = new();
    public float BannerTime;
    public int Wave;                    // index into Sheets.Waves of the current or next wave
    public float Breather = 3;
    public bool WaveActive, Won, Lost;
    readonly Queue<EnemyRow> _spawnQueue = new();
    int _weatherSeed;
    float _spawnTimer;
    float _waveHpScale = 1;
    public int Kills, SkillCasts, LevelUps;
    public event Action<string>? Milestone;

    readonly GameAssets _assets;
    readonly Random _rng;
    /// Half the size of the square play area: the city's, or the fort arena's.
    public readonly float ArenaHalf;
    /// The Gears hero this run wears (a class's own, or another unlocked look).
    public string HeroId = "";
    /// Stat points from levelling, spent per run (Bag > Stats): attack, health, crit, speed.
    public readonly Dictionary<string, int> StatSpend = new() { ["power"] = 0, ["hp"] = 0, ["crit"] = 0, ["speed"] = 0 };
    public int StatPoints => P.Level - 1 - StatSpend.Values.Sum();
    bool _bonusWave, _prestiged;
    public (bool[]? open, int n) NavGrid => (_navOpen, _navN);
    bool _gearsArena;

    Model _floor, _block, _cyl, _sphere;

    public World(ClassRow cls, GameAssets assets, int seed)
    {
        _assets = assets;
        HeroId = cls.Character;
        _rng = new Random(seed);
        Env.Seed(seed);
        _weatherSeed = seed;
        Env.Reset();
        var lvl = 1;
        P = new Player
        {
            Cls = cls,
            Guns = Sheets.Weapons.Select(w => new GunState { Def = w, Ammo = w.Clip }).ToArray(),
            MaxHp = cls.MaxHp, MaxMp = cls.MaxMp, Level = lvl,
        };
        P.Hp = P.MaxHp; P.Mp = P.MaxMp;
        P.Gun = Array.FindIndex(P.Guns, g => g.Def.Id == cls.Weapon);
        ArenaHalf = _assets.City != null ? Sheets.CityPlayHalf : Sheets.ArenaHalf;
        P.Pos = _assets.City != null ? Vector3.Zero : new Vector3(0, 0, -ArenaHalf + 6);
        BuildArena();
        ApplyGear();
        LoadSlotMap();
        Milestone += m => _seen.Add(m);
    }

    void BuildArena()
    {
        // Floor: big enough to reach the fort wall; the Gears ground texture repeats across it.
        var floorSize = _assets.City != null ? Sheets.CityDrawRadius * 2 + 60 : ArenaHalf * 2 + 50;
        var floorMesh = Raylib.GenMeshPlane(floorSize, floorSize, 8, 8);
        unsafe
        {
            var uv = floorMesh.TexCoordsAs<Vector2>();
            for (var i = 0; i < uv.Length; i++) uv[i] *= _assets.City != null ? floorSize / Sheets.CityGroundTileMetres : Sheets.ArenaGroundTiles * floorSize / (Sheets.ArenaHalf * 2);
            Raylib.UpdateMeshBuffer(floorMesh, 1, uv, 0);
        }
        _floor = Lit.Make(floorMesh);
        _block = Lit.Make(Raylib.GenMeshCube(1, 1, 1));
        _cyl = Lit.Make(Raylib.GenMeshCylinder(1, 1, 14));
        _sphere = Lit.Make(Raylib.GenMeshSphere(1, 12, 14));
        var img = Raylib.GenImageChecked(512, 512, 64, 64, new Color(74, 70, 66, 255), new Color(64, 61, 58, 255));
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        unsafe { _floor.Materials[0].Maps[(int) MaterialMapIndex.Albedo].Texture = _assets.HasGround ? _assets.Ground : tex; }
        if (_assets.City != null) Abyss.Build(_assets, _assets.HasGround ? _assets.Ground : tex, Sheets.CityGroundTileMetres);

        void Box(float x, float z, float w, float d, float h) =>
            Cover.Add(new BoundingBox(new Vector3(x - w / 2, 0, z - d / 2), new Vector3(x + w / 2, h, z + d / 2)));
        // Outer walls.
        Box(0, ArenaHalf + 0.5f, ArenaHalf * 2 + 2, 1, 5);
        Box(0, -ArenaHalf - 0.5f, ArenaHalf * 2 + 2, 1, 5);
        Box(ArenaHalf + 0.5f, 0, 1, ArenaHalf * 2, 5);
        Box(-ArenaHalf - 0.5f, 0, 1, ArenaHalf * 2, 5);
        // Cover from the Gears pieces on the arena sheet (collision = their footprint after the turn).
        if (_assets.City != null)
        {
            // the Gears city: every street-standing piece blocks; start on the nearest open street spot
            Cover.AddRange(_assets.City.Colliders);
            Mounds.AddRange(_assets.City.Mounds);
            _gearsArena = true;
            var city = _assets.City;
            Glow.Ground = p => city.GroundAt(p.X, p.Z);
            P.Pos = FreeSpot(Vector3.Zero, 1.5f);
            ScatterStreetProps();
            PlaceFires();
            PlaceWindStreams();
            PlaceStreetLights();
            PlaceStation();
            PlaceSquadmate();
            PlaceBurningWindows();
            PlaceDecals();
            PlaceGroundDecals();
            PlaceStoryBits();
            return;
        }
        _gearsArena = _assets.Props.Count > 0;
        foreach (var prop in _assets.Props)
        {
            if (prop.Def.Role != "cover") continue;
            var turned = prop.Def.Yaw % 180 != 0;
            var w = turned ? prop.Size.Z : prop.Size.X;
            var d = turned ? prop.Size.X : prop.Size.Z;
            Box(prop.Def.X, prop.Def.Z, w, d, prop.Size.Y);
        }
        if (_gearsArena) return;
        // Without the Gears pieces: plain waist-high cover and a few pillars.
        foreach (var (x, z, w, d) in new[] { (-8f, -10f, 4f, 1f), (8f, -10f, 4f, 1f), (0f, -2f, 6f, 1f), (-14f, 4f, 1f, 5f), (14f, 4f, 1f, 5f),
                                             (-6f, 10f, 4f, 1f), (6f, 10f, 4f, 1f), (0f, 16f, 8f, 1f), (-18f, -16f, 3f, 1f), (18f, -16f, 3f, 1f) })
            Box(x, z, w, d, 1.1f);
        foreach (var (x, z) in new[] { (-10f, 0f), (10f, 0f), (-3f, 20f), (3f, -18f) })
            Box(x, z, 1.4f, 1.4f, 4.5f);
    }

    // ---------------------------------------------------------------- skills and levels

    public SkillRow Skill(int slot) => Sheets.Skills.First(s => s.Id == P.Cls.Skills[SlotMap[slot]]);
    float MpCost(SkillRow s) => s.MpCost * (Has("focus") ? 0.85f : 1);
    float HeroScale => Transform > 0 ? 1.15f : 1;
    /// What the slot casts right now: a waiting chain skill, or the hotbar skill.
    public SkillRow ActiveSkill(int slot) => P.Chains[slot] is { Time: > 0 } c ? c.Skill : Skill(slot);
    public int ChainCasts, Evades;
    int _castHits;

    // ---------------------------------------------------------------- target lock (AION style)

    public Enemy? Locked;

    /// Tab: lock the enemy nearest the crosshair in front; again for the next one.
    public void CycleLock()
    {
        var fwd = Flat(AimDir());
        var list = Enemies
            .Select(e => (e, to: e.Pos - P.Pos))
            .Where(x => x.to.Length() <= Sheets.TargetLockRange && Vector3.Dot(Flat(x.to), fwd) > 0.3f)
            .OrderBy(x => (1 - Vector3.Dot(Flat(x.to), fwd)) * 30 + x.to.Length())
            .Select(x => x.e).ToList();
        if (list.Count == 0) { Locked = null; return; }
        var i = Locked == null ? -1 : list.IndexOf(Locked);
        Locked = list[(i + 1) % list.Count];
        Sfx.Play(Sfx.Click);
    }

    /// The enemy the target frame shows: the locked one, else the one under the crosshair.
    public Enemy? FrameTarget => Locked ?? AimHit(60).enemy;

    Enemy? LockedWithin(float range) => Locked != null && Enemies.Contains(Locked) && Dist2(Locked.Pos, P.Pos) <= range + Locked.Radius + 0.5f ? Locked : null;

    // ---------------------------------------------------------------- AION effects, casting, spirits, traps

    void UpdateAion(float dt, Vector2 move)
    {
        P.BasicCd -= dt;
        if (P.ActionAnim >= 0 && (P.ActionAnim += dt) > 0.55f) P.ActionAnim = -1;
        if (P.GuardTime > 0) P.GuardTime -= dt;
        if (P.HasteTime > 0) P.HasteTime -= dt;
        if (P.Stealth > 0) P.Stealth -= dt;
        if (P.Salvation > 0) P.Salvation -= dt;
        if (P.RegenTime > 0) { P.RegenTime -= dt; P.Hp = MathF.Min(P.MaxHp, P.Hp + P.RegenRate * dt); }
        if (P.Casting is { } c)
        {
            if (move.LengthSquared() > 0.02f || P.EvadeTime > 0 || Lost || Won) { P.Casting = null; ReloadFlash("Cast cancelled"); }
            else if ((c.Time += dt) >= c.Skill.CastTime)
            {
                P.Casting = null;
                var why = Execute(c.Slot, c.Skill);
                if (why != "") ReloadFlash(why);
            }
        }
        for (var i = Allies.Count - 1; i >= 0; i--)
        {
            var a = Allies[i];
            a.Bob += dt;
            if ((a.Life -= dt) <= 0) { Effects.Add(new Fx { Kind = FxKind.Burst, A = a.Pos + Vector3.UnitY, Radius = 0.8f, Col = a.Col, Life = 0.4f }); Allies.RemoveAt(i); continue; }
            var water = a.Skill.Element == "ice";
            var target = water ? null : CommandTime > 0 && Locked != null ? Locked : Enemies.Where(e => e.Hp > 0 && e.Emerge < 0 && e.Burrow <= 0).OrderBy(e => Dist2(e.Pos, a.Pos)).FirstOrDefault();
            if (water && (a.AttackCd -= dt) <= 0)
            {
                // a water spirit mends you instead of fighting
                a.AttackCd = 1.1f;
                var heal = MathF.Min(a.Power * 0.8f, P.MaxHp - P.Hp);
                if (heal >= 1) { P.Hp += heal; Effects.Add(new Fx { Kind = FxKind.Beam, A = a.Pos + Vector3.UnitY * 1.2f, B = P.Pos + Vector3.UnitY, Col = a.Col, Life = 0.3f, Radius = 0.06f, Element = "ice" }); }
            }
            var want = target != null && Dist2(target.Pos, P.Pos) < 18 ? target.Pos + Flat(a.Pos - target.Pos) * (target.Radius + 1.2f) : P.Pos + P.Right * 1.6f - P.Forward * 0.8f;
            var step = want - a.Pos; step.Y = 0;
            if (step.Length() > 0.2f) MoveCircle(ref a.Pos, Vector3.Normalize(step) * MathF.Min(step.Length(), 6 * dt), 0.4f);
            if (target != null && Dist2(target.Pos, a.Pos) < target.Radius + 2f && (a.AttackCd -= dt) <= 0)
            {
                a.AttackCd = a.Skill.Element == "wind" ? 0.55f : 1.1f;
                _lastSource = a.Skill.Name.Replace("Summon ", "");
                Damage(target, a.Power * DamageMult * (0.9f + (float) _rng.NextDouble() * 0.2f) * (CommandTime > 0 ? 1.5f : 1) * (StanceOn && P.Cls.Id == "chanter" ? 1.2f : 1), false);
                Effects.Add(new Fx { Kind = FxKind.Beam, A = a.Pos + Vector3.UnitY * 1.2f, B = target.Centre, Col = a.Col, Life = 0.2f, Radius = 0.08f });
            }
        }
        for (var i = Traps.Count - 1; i >= 0; i--)
        {
            var t = Traps[i];
            if ((t.Life -= dt) <= 0) { Traps.RemoveAt(i); continue; }
            var caught = Enemies.Where(e => e.Emerge < 0 && Dist2(e.Pos, t.Pos) <= t.Skill.Radius + e.Radius).ToList();
            if (caught.Count == 0) continue;
            foreach (var e in caught) HitWithSkill(e, t.Power, t.Skill);
            Effects.Add(new Fx { Kind = FxKind.Burst, A = t.Pos + Vector3.UnitY * 0.5f, Radius = t.Skill.Radius, Col = t.Col, Life = 0.5f });
            Traps.RemoveAt(i);
        }
    }

    void DrawAion()
    {
        var col = Hex(P.Cls.Color);
        foreach (var a in Allies)
        {
            // a spirit: a glowing core with orbiting motes
            var c = a.Pos + new Vector3(0, 1.3f + MathF.Sin(a.Bob * 3) * 0.15f, 0);
            Glow.Sprite(c, 1.1f, Fade(a.Col, 0.7f));
            Glow.Sprite(c, 0.45f, Fade(Color.White, 0.9f));
            for (var k = 0; k < 4; k++)
            {
                var ang = a.Bob * 2.5f + k * MathF.PI / 2;
                Glow.Sprite(c + new Vector3(MathF.Cos(ang) * 0.85f, MathF.Sin(ang * 0.7f) * 0.3f, MathF.Sin(ang) * 0.85f), 0.18f, Color.White);
            }
            Glow.Decal(a.Pos, 0.9f, Fade(a.Col, 0.5f), ring: true);
            if (_rng.NextDouble() < 0.3) Bits.Emit(c, 1, a.Col, 0.4f, 0.08f, 0.8f, -0.5f, glow: true);
        }
        foreach (var t in Traps)
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float) Raylib.GetTime() * 5);
            Glow.Decal(t.Pos, t.Skill.Radius, Fade(t.Col, 0.4f + 0.4f * pulse), ring: true);
            Glow.Decal(t.Pos, t.Skill.Radius * 0.45f, Fade(t.Col, 0.6f), ring: false);
        }
        if (P.Casting is { } cast)
        {
            // casting circle under the hero
            var k = cast.Time / cast.Skill.CastTime;
            var spin = (float) Raylib.GetTime() * 120;
            Glow.Decal(P.Pos with { Y = 0 }, 1.5f, Fade(col, 0.9f), ring: true);
            Glow.Decal(P.Pos with { Y = 0 }, 1.5f * k, Fade(Color.White, 0.5f), ring: true);
            for (var i = 0; i < 6; i++)
            {
                var a = (spin + i * 60) * MathF.PI / 180;
                Glow.Sprite(P.Pos + new Vector3(MathF.Cos(a) * 1.2f, 0.8f + k * 0.8f, MathF.Sin(a) * 1.2f), 0.16f, col);
            }
            Glow.Sprite(HoldPoint(), 0.25f + 0.25f * k, Fade(col, 0.9f));
        }
    }

    // ---------------------------------------------------------------- flight (AION style)

    public int Flights;

    /// F: spread the wings and rise, or land.
    public void ToggleFlight()
    {
        if (Lost || Won) return;
        if (P.Flying) { P.Flying = false; return; }
        if (P.FlightTime < 1) { FlightNote = "Not enough flight time"; return; }
        P.Flying = true;
        Sfx.PlayGears("hero", "flap", 0.8f);
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 2.2f, Col = World.Fade(Hex(P.Cls.Color), 0.9f), Life = 0.5f });
        Sfx.Play(Sfx.Cast);
        if (++Flights == 1) Milestone?.Invoke("flight");
    }

    public string FlightNote = "";

    float _stuckFor;
    Vector3 _stuckFrom;

    /// Never stuck: pushing to move but going nowhere while inside something solid for a moment gets a nudge to the
    /// nearest open spot.
    void Unstick(float dt)
    {
        var pushing = _moveInput.LengthSquared() > 0.09f && !P.Flying && _moveT < 0;
        var moved = Vector2.Distance(new Vector2(P.Pos.X, P.Pos.Z), new Vector2(_stuckFrom.X, _stuckFrom.Z));
        if (pushing && moved < 0.05f && Blocked(P.Pos, 0.3f)) _stuckFor += dt; else _stuckFor = 0;
        _stuckFrom = P.Pos;
        if (_stuckFor < 1.2f) return;
        _stuckFor = 0;
        var to = FreeSpot(P.Pos, 1.2f);
        Log.Info($"unstuck: {P.Pos} -> {to}");
        P.Pos = to with { Y = MathF.Max(to.Y, GroundAt(to)) };
    }

    float _edgeNote, _edgeShimmer;
    Vector3 _edgeAt;

    /// The edge of the district glimmers where the hero pressed against it.
    void DrawEdgeShimmer()
    {
        if (_edgeShimmer <= 0) return;
        _edgeShimmer -= Raylib.GetFrameTime() * 0.8f;
        var lim = ArenaHalf - 0.6f;
        // which face of the boundary: the side the hero is pressed to
        var alongX = MathF.Abs(MathF.Abs(_edgeAt.Z) - lim) < MathF.Abs(MathF.Abs(_edgeAt.X) - lim);
        for (var i = -6; i <= 6; i++)
            for (var j = -3; j <= 4; j++)
            {
                var at = alongX ? new Vector3(_edgeAt.X + i * 0.9f, _edgeAt.Y + j * 0.9f, MathF.Sign(_edgeAt.Z) * (lim + 0.4f))
                                : new Vector3(MathF.Sign(_edgeAt.X) * (lim + 0.4f), _edgeAt.Y + j * 0.9f, _edgeAt.Z + i * 0.9f);
                var fall = MathF.Max(0, 1 - (i * i + j * j) / 30f) * _edgeShimmer;
                var twinkle = 0.6f + 0.4f * MathF.Sin((float) Raylib.GetTime() * 6 + i * 1.3f + j * 2.1f);
                Glow.Sprite(at, 0.5f, new Color((byte) 190, (byte) 150, (byte) 255, (byte) (120 * fall * twinkle)));
            }
    }

    void UpdateFlight(float dt)
    {
        // the district ends at the play area, in the air too: past it there is only the distant skyline (no ground, no
        // colliders), which is scenery to look at, not to fly into
        var lim = ArenaHalf - 0.6f;
        if (MathF.Abs(P.Pos.X) > lim || MathF.Abs(P.Pos.Z) > lim)
        {
            P.Pos.X = Math.Clamp(P.Pos.X, -lim, lim); P.Pos.Z = Math.Clamp(P.Pos.Z, -lim, lim);
            if ((_edgeNote -= dt) <= 0) { FlightNote = "The Siege lines end here"; _edgeNote = 3; }
            _edgeShimmer = 1; _edgeAt = P.Pos;
        }
        else _edgeNote = 0;
        if (P.Flying)
        {
            if ((P.FlightTime -= dt * (Has("wings") ? 0.67f : 1)) <= 0) { P.FlightTime = 0; P.Flying = false; FlightNote = "Out of flight time"; }
        }
        else if (Up < 0.05f) P.FlightTime = MathF.Min(Sheets.FlightSeconds, P.FlightTime + Sheets.FlightRegenPerSecond * dt);
        if (_diving || Climbing) return;
        var want = P.Flying ? FlightClimb(MathF.Max(Sheets.FlightHeight, FloorY + 1.5f)) : FloorY;
        var step = Sheets.FlightRiseSpeed * dt * (P.Flying ? 1 : 1.4f * FallSpeedMult);
        P.Pos.Y = P.Pos.Y < want ? MathF.Min(want, P.Pos.Y + step) : MathF.Max(want, P.Pos.Y - step);
    }

    /// AION Daeva wings: from each shoulder a wing arm sweeps up and out, carrying three rows of feathers (short
    /// coverts on top, secondaries along the inner half, long primaries at the tip), silver-white shading into the
    /// class colour at the tips, with a soft glow in the class colour. They flap in flight.
    float _wingOpen, _wingPhase, _swayPhase, _takeoff;
    (Vector3 shoulders, Vector3 fwd, Vector3 right, float spread, float flap, Color col, float sway)? _wingPose;
    bool _wasFlying;
    Vector3 _wingPrev;

    void DrawWings(float spread, float alpha)
    {
        var col = WingColor();
        var fwd = P.Forward; var right = P.Right; var up = Vector3.UnitY;
        // the wingbeat follows the flight: hard and fast climbing, slow cruising, nearly still in a glide down;
        // folded, the wings sway with the hero's stride
        var dt = MathF.Max(1e-3f, Raylib.GetFrameTime());
        var vel = (P.Pos - _wingPrev) / dt; _wingPrev = P.Pos;
        if (vel.LengthSquared() > 400) vel = Vector3.Zero; // a teleport (respawn, evade) is not a speed
        var climb = Math.Clamp(vel.Y / 4, -1, 1);
        var rate = P.Flying ? 3.2f + 3.2f * MathF.Max(0, climb) - 1.6f * MathF.Max(0, -climb) : 1.6f;
        var depth = P.Flying ? 0.22f + 0.2f * MathF.Max(0, climb) - 0.14f * MathF.Max(0, -climb) : 0.05f;
        _wingPhase += dt * rate;
        var flap = MathF.Sin(_wingPhase) * depth * spread;
        var stride = MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        _swayPhase += dt * (1.5f + stride * 1.1f);
        var sway = Math.Clamp(stride / 6, 0, 1) * MathF.Sin(_swayPhase) * (1 - spread);
        // take-off: the wings sweep open past full and beat hard once
        if (P.Flying && !_wasFlying) _takeoff = 0.7f;
        _wasFlying = P.Flying;
        if (_takeoff > 0)
        {
            _takeoff -= dt;
            var k = _takeoff / 0.7f;
            spread = MathF.Min(1.12f, spread + 0.12f * MathF.Sin(MathF.PI * k));
            flap += 0.45f * MathF.Sin(MathF.PI * k);
        }
        var shoulders = P.Pos + up * 1.42f - fwd * 0.22f;
        _wingPose = (shoulders, fwd, right, spread, flap, col, sway);
        Wings.Draw(shoulders, fwd, right, spread, flap, col, alpha, sway);
        // a boost in flight streams a trail of light and feathers off the wingtips
        if (P.Flying && P.Sprinting && _rng.NextDouble() < 0.7)
            for (var side = -1; side <= 1; side += 2)
            {
                var tip = shoulders + right * side * 2.2f * Wings.Size * spread + up * 0.3f - fwd * 0.6f;
                Bits.Emit(tip, 1, new Color((int) Math.Min(255, col.R + 90), (int) Math.Min(255, col.G + 90), (int) Math.Min(255, col.B + 90), 200), 0.2f, 0.05f, 0.7f, 0, glow: true);
                if (_rng.NextDouble() < 0.15) Bits.Emit(tip, 1, Settings.S.WingStyle == 0 ? new Color(236, 236, 244, 220) : new Color(60, 46, 80, 220), 0.6f, 0.08f, 2f, 0.5f);
            }
        // AION wings shimmer a little in flight
        if (P.Flying && _rng.NextDouble() < 0.3)
        {
            var side = _rng.NextDouble() < 0.5 ? -1 : 1;
            var at = shoulders + right * side * (0.5f + (float) _rng.NextDouble() * 2.6f) * spread * Wings.Size + up * (0.6f - (float) _rng.NextDouble() * 1.6f) * spread * Wings.Size - fwd * 0.3f;
            Bits.Emit(at, 1, new Color((int) Math.Min(255, col.R + 80), (int) Math.Min(255, col.G + 80), (int) Math.Min(255, col.B + 80), 255), 0.3f, 0.04f, 0.8f, 0.4f, glow: true);
        }
    }

    // ---------------------------------------------------------------- evasion (AION style)

    public void Evade(Vector2 move)
    {
        if (P.Flying) { _move = move; if (AirDash() && ++Evades == 1) Milestone?.Invoke("evade"); return; }
        if (TryBreakFree() || PlayerStun > 0) return;
        if (TryVaultOrClimb(move)) return;
        if (Lost || Won || P.EvadeTime > 0 || P.EvadeCd > 0) return;
        var dir = P.Forward * move.Y + P.Right * move.X;
        P.EvadeDir = dir.LengthSquared() > 0.01f ? Vector3.Normalize(dir) : -P.Forward;
        P.EvadeTime = Sheets.EvasionSeconds;
        P.EvadeCd = Sheets.EvasionCooldown;
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 1.2f, Col = new Color(200, 190, 170, 255), Life = 0.35f });
        Sfx.PlayGears("hero", "evade", 0.7f);
        Voice("roll", 0.5f);
        Bits.Emit(P.Pos + Vector3.UnitY * 0.1f, 14, new Color(140, 128, 112, 180), 2f, 0.16f, 0.8f, 1f);
        if (++Evades == 1) Milestone?.Invoke("evade");
    }
    public bool Unlocked(SkillRow s) => P.Level >= s.UnlockLevel;
    public int Rank(SkillRow s) => Unlocked(s) ? 1 + s.RankLevels.Count(l => P.Level >= l) : 0;
    public float SkillPower(SkillRow s) => (s.Power + s.PowerPerRank * (Rank(s) - 1)) * P.Cls.SkillPower;
    public LevelRow LevelRow => Sheets.Levels[P.Level - 1];
    /// Difficulty (options): how hard Locust hit and how much HP they have.
    public static float DifficultyDamage => Settings.S.Difficulty switch { 0 => 0.6f, 2 => 1.35f, 3 => 1.75f, _ => 1f };
    public static float DifficultyHp => Settings.S.Difficulty switch { 0 => 0.75f, 2 => 1.3f, 3 => 1.7f, _ => 1f };
    float EnemyDamage(Enemy e) => RallyMult(e) * (e.Taunt > 0 ? 0.8f : 1) * e.Def.Damage * DifficultyDamage * (e.Elite ? 1.3f : 1) * (e.Enraged ? 1.5f : 1) * (e.GlassCannon ? 1.6f : 1);

    float DamageMult => 1 + (OnHighGround ? 0.15f : 0) + P.BuffPct / 100f + (P.Level - 1) * 0.06f + GearStat("power") / 100f + ComboBonus + StanceDamage + TransformDamage + (Has("fury") ? 0.08f : 0) + Progression.PrestigeBonus + StatSpend["power"] * 0.02f;

    public string TryCast(int slot)
    {
        var s = ActiveSkill(slot);
        if (!Unlocked(s)) return $"Unlocks at level {s.UnlockLevel}";
        if (P.Cooldowns.TryGetValue(s.Id, out var cd0) && cd0 > 0) return "Not ready";
        if (P.Mp < MpCost(s)) return "Not enough MP";
        if (PlayerStun > 0) return "Stunned";
        if (P.Casting != null) return "Already casting";
        if (s.CastTime > 0)
        {
            // AION cast bar: the skill goes off when the bar fills, moving cancels it
            P.Casting = new CastState { Skill = s, Slot = slot };
            Sfx.Play(Sfx.Cast);
            return "";
        }
        return Execute(slot, s);
    }

    static readonly bool CastLog = Environment.GetEnvironmentVariable("DAEVAS_CASTLOG") == "1";

    string Execute(int slot, SkillRow s)
    {
        if (CastLog) Log.Info($"cast {s.Name} (lv {P.Level}, {Enemies.Count} enemies, nearest {(Enemies.Count > 0 ? Enemies.Min(e => Dist2(e.Pos, P.Pos)) : -1):0.0} m)");
        var chained = s.ChainFrom != "none";
        _castHits = 0;
        if (P.Mp < MpCost(s)) return "Not enough MP";
        var aim = AimHit(s.Range > 0 ? s.Range : 60);
        var col = ElementColor(s.Element);
        var power = SkillPower(s);
        if (s.Kind is "heal" or "regen" or "zone") power *= PassiveHeal;
        var fx0 = Effects.Count;
        var locked = LockedWithin(s.Range > 0 ? s.Range : s.Radius);
        Enemy? Target() => locked ?? aim.enemy ?? (Settings.S.AutoTarget ? NearestInCone(P.Pos, aim.dir, s.Range, 0.35f) : null);
        // strikes, dashes and blasts turn to a locked target
        if (locked != null) aim = (aim.enemy, locked.Centre, Vector3.Normalize(locked.Centre - CameraEye()));
        switch (s.Kind)
        {
            case "strike":
            {
                var fwd = Flat(aim.dir);
                var cos = MathF.Cos(MathF.Atan2(s.Radius, s.Range));
                foreach (var e in Enemies.ToList())
                {
                    var to = e.Pos - P.Pos; to.Y = 0;
                    var dist = to.Length();
                    if (InReach(e) && dist <= s.Range + e.Radius && (dist < 0.8f || Vector3.Dot(to / dist, fwd) >= cos)) HitWithSkill(e, power, s);
                }
                Effects.Add(new Fx { Kind = FxKind.Slash, A = P.Pos + Vector3.UnitY, B = fwd, Radius = s.Range, Col = col, Life = 0.25f });
                break;
            }
            case "shot":
            {
                var t = Target();
                var head = t != null && t == aim.enemy && aim.point.Y > t.Pos.Y + t.Def.Size * 0.78f;
                var end = head ? aim.point : t?.Centre ?? aim.point;
                Effects.Add(new Fx { Kind = FxKind.Beam, A = Muzzle(), B = end, Col = col, Life = 0.3f, Radius = 0.12f });
                if (t != null) HitWithSkill(t, power, s, head);
                break;
            }
            case "nova":
                foreach (var e in Enemies.ToList()) if (InReach(e) && Dist2(e.Pos, P.Pos) <= s.Radius + e.Radius) HitWithSkill(e, power, s);
                Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = s.Radius, Col = col, Life = 0.45f });
                break;
            case "blast":
            {
                var at = aim.point; at.Y = 0;
                foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, at) <= s.Radius + e.Radius) HitWithSkill(e, power, s);
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = at, Radius = s.Radius, Col = col, Life = 0.6f });
                Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = s.Radius, Col = col, Life = 0.5f });
                break;
            }
            case "dot":
            {
                var t = Target();
                if (t == null) return "No target";
                var ticks = Math.Max(1, (int) (s.Duration * Sheets.SkillKindsDotTicksPerSecond));
                // AION stacking: the same poison or curse refreshes and stacks up to three times
                var perTick = ElementDamage(t, power * DamageMult / ticks, s.Element, out _);
                if (t.Dots.FirstOrDefault(d => d.Source == s.Id) is { } same)
                {
                    same.Stacks = Math.Min(3, same.Stacks + 1);
                    same.PerTick = perTick * same.Stacks; same.Ticks = ticks;
                    AddNumber(t.Pos + new Vector3(0.5f, t.Def.Size + 0.6f, 0), $"x{same.Stacks}", col, 0.7f);
                }
                else t.Dots.Add(new Dot { Source = s.Id, PerTick = perTick, Ticks = ticks, Timer = 1f / Sheets.SkillKindsDotTicksPerSecond, Col = col });
                _castHits++;
                Effects.Add(new Fx { Kind = FxKind.Beam, A = Muzzle(), B = t.Centre, Col = col, Life = 0.3f, Radius = 0.06f });
                Effects.Add(new Fx { Kind = FxKind.Burst, A = t.Centre, Radius = 0.8f, Col = col, Life = 0.4f });
                break;
            }
            case "stun":
            {
                var t = Target();
                if (t == null) return "No target";
                t.Stun = Math.Max(t.Stun, s.Duration);
                HitWithSkill(t, power, s);
                Effects.Add(new Fx { Kind = FxKind.Beam, A = Muzzle(), B = t.Centre, Col = col, Life = 0.25f, Radius = 0.1f });
                break;
            }
            case "dash":
            {
                var fwd = Flat(aim.dir);
                var from = P.Pos;
                var to = from;
                for (var d = 0f; d < s.Range; d += 0.25f)
                {
                    var next = from + fwd * d;
                    if (Blocked(next, 0.45f)) break;
                    to = next;
                }
                foreach (var e in Enemies.ToList()) if (DistToSegment(e.Pos, from, to) <= s.Radius + e.Radius) HitWithSkill(e, power, s);
                Effects.Add(new Fx { Kind = FxKind.Tracer, A = from + Vector3.UnitY, B = to + Vector3.UnitY, Col = col, Life = 0.35f });
                P.Pos = to;
                break;
            }
            case "heal":
            {
                var amount = MathF.Min(power, P.MaxHp - P.Hp);
                P.Hp += amount;
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), "+" + (int) power, new Color(110, 230, 120, 255), 1.1f);
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 1.2f, Col = new Color(110, 230, 120, 255), Life = 0.6f });
                break;
            }
            case "buff":
                P.BuffPct = power; P.BuffTime = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 2, Col = col, Life = 0.6f });
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), $"+{(int) power}% damage", col, 0.8f);
                break;
            case "shield":
                P.Shield = power; P.ShieldTime = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Burst, A = P.Pos + Vector3.UnitY, Radius = 1.4f, Col = new Color(120, 180, 255, 255), Life = 0.5f });
                break;
            case "aura":
                P.AuraTime = s.Duration; P.AuraPower = power; P.AuraRadius = s.Radius; P.AuraTick = 0;
                break;
            case "leap":
            {
                // leap to the target (or the aim point) and crash down on everything around the landing
                var dest = (Target()?.Pos ?? aim.point) with { Y = 0 };
                var dir = Flat(dest - P.Pos);
                var dist = MathF.Min(Dist2(dest, P.Pos), s.Range);
                var to = P.Pos;
                for (var d = 0f; d <= dist - 1.0f; d += 0.25f) { var next = P.Pos + dir * d; if (Blocked(next, 0.45f)) break; to = next; }
                Effects.Add(new Fx { Kind = FxKind.Tracer, A = P.Pos + Vector3.UnitY * 2.5f, B = to + Vector3.UnitY, Col = col, Life = 0.35f });
                P.Pos = to with { Y = P.Pos.Y };
                foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, to) <= s.Radius + e.Radius) HitWithSkill(e, power, s);
                Effects.Add(new Fx { Kind = FxKind.Ring, A = to, Radius = s.Radius, Col = col, Life = 0.5f });
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = to, Radius = s.Radius * 0.5f, Col = new Color(150, 140, 120, 255), Life = 0.4f });
                P.ActionAnim = 0;
                break;
            }
            case "pull":
            {
                var t = Target();
                if (t == null) return "No target";
                var near = P.Pos + Flat(t.Pos - P.Pos) * (1.6f + t.Radius);
                Effects.Add(new Fx { Kind = FxKind.Beam, A = Muzzle(), B = t.Centre, Col = col, Life = 0.35f, Radius = 0.08f });
                if (!Blocked(near, t.Radius)) t.Pos = near with { Y = 0 };
                HitWithSkill(t, power, s);
                break;
            }
            case "drain":
            {
                var t = Target();
                if (t == null) return "No target";
                var dealt = HitWithSkill(t, power, s);
                var heal = MathF.Min(dealt * 0.5f, P.MaxHp - P.Hp);
                P.Hp += heal;
                if (heal >= 1) AddNumber(P.Pos + new Vector3(0, 2.2f, 0), "+" + (int) heal, new Color(110, 230, 120, 255), 0.9f);
                Effects.Add(new Fx { Kind = FxKind.Beam, A = t.Centre, B = P.Pos + Vector3.UnitY * 1.2f, Col = new Color(200, 60, 90, 255), Life = 0.4f, Radius = 0.1f });
                if (s.Range <= 4) P.ActionAnim = 0;
                break;
            }
            case "pet":
                Allies.RemoveAll(a => a.Skill.Id == s.Id);
                Allies.Add(NewAlly(s, power, col));
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos + P.Right * 1.6f, Radius = 1, Col = col, Life = 0.6f });
                break;
            case "stealth":
                P.Stealth = s.Duration; P.StealthBonus = power;
                Effects.Add(new Fx { Kind = FxKind.Burst, A = P.Pos + Vector3.UnitY, Radius = 1.2f, Col = new Color(60, 50, 80, 255), Life = 0.4f });
                break;
            case "trap":
            {
                var at = aim.point with { Y = 0 };
                if (Dist2(at, P.Pos) > s.Range) at = P.Pos + Flat(at - P.Pos) * s.Range;
                Traps.Add(new Trap { Skill = s, Pos = at, Life = s.Duration, Power = power, Col = col });
                Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = s.Radius, Col = col, Life = 0.4f });
                break;
            }
            case "blink":
            {
                // leap back the way the camera faces from (power = metres)
                var dir = -Flat(AimDir());
                var to = P.Pos;
                for (var d = 0f; d <= s.Power; d += 0.25f) { var next = P.Pos + dir * d; if (Blocked(next, 0.45f) || MathF.Abs(next.X) > ArenaHalf - 1 || MathF.Abs(next.Z) > ArenaHalf - 1) break; to = next; }
                Effects.Add(new Fx { Kind = FxKind.Burst, A = P.Pos + Vector3.UnitY, Radius = 1, Col = col, Life = 0.3f });
                P.Pos = to with { Y = P.Pos.Y };
                Effects.Add(new Fx { Kind = FxKind.Burst, A = P.Pos + Vector3.UnitY, Radius = 1, Col = col, Life = 0.3f });
                P.EvadeTime = MathF.Max(P.EvadeTime, 0.01f); // a moment of untouchability
                break;
            }
            case "guard":
                P.Guard = power; P.GuardTime = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 1.6f, Col = new Color(200, 200, 220, 255), Life = 0.5f });
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), $"-{(int) power}% damage taken", new Color(200, 210, 230, 255), 0.75f);
                break;
            case "haste":
                P.Haste = power; P.HasteTime = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 1.6f, Col = new Color(120, 230, 200, 255), Life = 0.5f });
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), $"+{(int) power}% speed", new Color(140, 240, 210, 255), 0.75f);
                break;
            case "regen":
                P.RegenRate = power / MathF.Max(1, s.Duration); P.RegenTime = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 1.2f, Col = new Color(110, 230, 120, 255), Life = 0.6f });
                break;
            case "mana":
                P.Mp = MathF.Min(P.MaxMp, P.Mp + power);
                AddNumber(P.Pos + new Vector3(0, 2.2f, 0), "+" + (int) power + " MP", new Color(110, 160, 255, 255), 0.9f);
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 1.2f, Col = new Color(90, 140, 255, 255), Life = 0.6f });
                break;
            case "delayed":
            {
                var at = aim.point with { Y = 0 };
                if (Dist2(at, P.Pos) > s.Range) at = P.Pos + Flat(at - P.Pos) * s.Range;
                Grounds.Add(new GroundSpell { Skill = s, Pos = at, Power = power, Col = col });
                _castHits++;
                break;
            }
            case "zone":
                Grounds.Add(new GroundSpell { Skill = s, Pos = P.Pos with { Y = 0 }, Life = s.Duration, Power = power, Col = col });
                break;
            case "salvation":
                P.Salvation = s.Duration;
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 1.5f, Col = new Color(255, 240, 170, 255), Life = 0.8f });
                break;
        }
        if (s.Kind is "strike" or "dash") P.ActionAnim = 0;
        if (s.Id == "assa_venom") CoatTime = 12; // the blades stay poisoned for a while
        if (s.Kind is "heal" or "regen" or "salvation" or "zone")
        {
            TryResurrect();
            if (P.Cls.Id == "chanter" && Van is { Hp: > 0 } van && Dist2(van.Pos, P.Pos) < 20)
            {
                van.Hp = MathF.Min(van.MaxHp, van.Hp + van.MaxHp * 0.08f);
                Say("Mantra mends the ammo van", new Color(130, 240, 200, 255));
            }
            // healing also cleanses Locust acid
            if (PoisonTime > 0) AddNumber(P.Pos + new Vector3(0.4f, 2.4f, 0), "Cleansed", new Color(150, 255, 160, 255), 0.75f);
            PoisonTime = 0;
        }
        if (s.Kind is "heal" or "regen" or "salvation" or "mana")
            for (var i = 0; i < 18; i++)
            {
                var a = (float) _rng.NextDouble() * MathF.Tau;
                Bits.Emit(P.Pos + new Vector3(MathF.Cos(a) * 0.7f, 0.2f + (float) _rng.NextDouble() * 1.6f, MathF.Sin(a) * 0.7f), 1,
                    s.Kind == "mana" ? new Color(130, 170, 255, 255) : new Color(150, 255, 160, 255), 0.4f, 0.06f, 1.1f, -2.2f, glow: true);
            }
        // the skill's element: its effects take its look, and bolts leave a trail of it
        for (var i = fx0; i < Effects.Count; i++)
        {
            var f = Effects[i];
            if (f.Element == "") f.Element = s.Element;
            var arrowTrail = s.Element == "physical" && P.Cls.BasicKind == "arrow";
            if (f.Kind is FxKind.Beam or FxKind.Tracer && arrowTrail)
                for (var d = 0.5f; d < Vector3.Distance(f.A, f.B); d += 0.5f)
                    Bits.Emit(Vector3.Lerp(f.A, f.B, d / Vector3.Distance(f.A, f.B)), 1, new Color(235, 225, 200, 120), 0.15f, 0.04f, 0.35f, 0, glow: true);
            if (f.Kind is FxKind.Beam or FxKind.Tracer && s.Element is not "physical" and not "lightning")
                for (var d = 0.5f; d < Vector3.Distance(f.A, f.B); d += 0.6f)
                    Bits.Emit(Vector3.Lerp(f.A, f.B, d / Vector3.Distance(f.A, f.B)), 1, col, 0.4f, s.Element is "shadow" or "earth" ? 0.18f : 0.07f, 0.5f, s.Element == "fire" ? -2.5f : 0.5f, glow: s.Element is not ("shadow" or "earth"));
        }
        Bits.Emit(P.Pos + new Vector3(0, 1.2f, 0), 14, col, 1.6f, 0.07f, 0.9f, -1.5f, glow: true);
        if (s.Kind is "leap" or "blast") AddShake(0.35f);
        else if (s.Kind == "nova") AddShake(0.2f);
        if (s.Kind is not ("stealth" or "buff" or "guard" or "haste" or "regen" or "mana" or "salvation" or "shield" or "heal" or "pet")) P.Stealth = 0;
        P.Mp -= MpCost(s);
        P.Cooldowns[s.Id] = s.Cooldown * (Has("haste") ? 0.9f : 1);
        ElementSound(s.Element, s.Kind);
        if (s.Kind is not ("strike" or "dash")) PlayOnce(Clip.Cast);
        Voice("attack", 0.3f);
        if (Has("echo") && s.Cooldown > 0 && _rng.NextDouble() < 0.1) { P.Cooldowns[s.Id] = 0; AddNumber(P.Pos + new Vector3(0, 2.6f, 0), "Echo!", new Color(200, 140, 255, 255), 0.8f); }
        SkillCasts++;
        Sfx.Play(Sfx.Cast);
        if (SkillCasts == 1) Milestone?.Invoke("skill-cast");
        if (chained)
        {
            AddNumber(P.Pos + new Vector3(0, 2.5f, 0), s.Name, new Color(255, 225, 140, 255), 0.8f);
            Progression.Count("chains");
            if (++ChainCasts == 1) Milestone?.Invoke("chain-skill");
        }
        // AION chains: a skill that lands opens its chain skill in the same slot for a moment
        P.Chains[slot] = null;
        var landed = _castHits > 0 || s.Kind is "heal" or "buff" or "shield" or "aura";
        var chainNext = Sheets.Skills.FirstOrDefault(k => k.ChainFrom == s.Id && Unlocked(k));
        if (landed && chainNext != null) P.Chains[slot] = new ChainOffer { Skill = chainNext, Time = Sheets.ChainSkillsWindow };
        return "";
    }

    float HitWithSkill(Enemy e, float power, SkillRow? s = null, bool headshot = false)
    {
        var crit = _rng.NextDouble() < CritChance(0.12f);
        var dmg = power * DamageMult * (0.92f + (float) _rng.NextDouble() * 0.16f) * (crit ? 1.5f : 1f) * StealthMult * ConsumeCounter(e.Centre) * (headshot ? 1.5f : 1f);
        var element = s?.Element ?? "physical";
        _lastSource = s?.Name ?? "Skill";
        dmg = ElementDamage(e, dmg, element, out var tag);
        if (headshot) tag = "HEADSHOT";
        dmg *= ClassBonus(e, s, false);
        if (IsBoss(e) && Has("abyss")) dmg *= 1.15f;
        Juggle(e);
        _castHits++;
        Damage(e, dmg, crit, tag);
        ElementImpact(element, e.Centre, crit ? 1.5f : 1);
        if (s != null && s.Cc != "none" && e.Hp > 0) ApplyCc(e, s.Cc, s.CcTime);
        return dmg;
    }

    float StealthMult => P.Stealth > 0 ? 1 + P.StealthBonus / 100f : 1;

    /// Melee and close-range skills only reach what is about level with you (fly up to fight flyers).
    bool InReach(Enemy e) => MathF.Abs(e.Pos.Y - P.Pos.Y) < e.Def.Size * 0.6f + 2.5f;

    /// AION crowd control, with its name popping up over the target.
    void ApplyCc(Enemy e, string cc, float t)
    {
        if (IsBoss(e) && cc is "stun" or "knockback" or "knockdown" or "airborne")
        {
            // bosses can't be thrown around, but a hard hit still breaks their wind-up
            Interrupt(e);
            e.Stun = MathF.Max(e.Stun, 0.3f);
            AddNumber(e.Pos + new Vector3(-0.3f, e.Def.Size + 0.7f, 0), "Resisted", new Color(205, 170, 255, 255), 0.7f);
            return;
        }
        if (cc is "stun" or "knockback" or "knockdown" or "airborne" or "silence") Interrupt(e);
        switch (cc)
        {
            case "stun": e.Stun = MathF.Max(e.Stun, t); break;
            case "knockback": e.Push = Flat(e.Pos - P.Pos) * 10f; e.Stun = MathF.Max(e.Stun, t); break;
            case "knockdown": e.Down = MathF.Max(e.Down, t); e.Stun = MathF.Max(e.Stun, t); break;
            case "airborne": e.Air = MathF.Max(e.Air, t); e.AirTotal = MathF.Max(e.Air, 0.1f); e.Stun = MathF.Max(e.Stun, t); break;
            case "root": e.Root = MathF.Max(e.Root, t); break;
            case "slow": e.Slow = MathF.Max(e.Slow, t); break;
            case "silence": e.Silence = MathF.Max(e.Silence, t); break;
            case "disarm": e.Disarm = MathF.Max(e.Disarm, t); break;
            case "taunt": e.Taunt = MathF.Max(e.Taunt, t); break;
            default: return;
        }
        var label = cc switch { "taunt" => "Taunted", "knockback" => "Knocked back", "knockdown" => "Knocked down", "airborne" => "Airborne", "root" => "Rooted", "slow" => "Slowed", "silence" => "Silenced", "disarm" => "Disarmed", _ => "Stunned" };
        CombatNote($"{e.Def.Name}: {label.ToLowerInvariant()}", new Color(205, 170, 255, 255));
        AddNumber(e.Pos + new Vector3(-0.3f, e.Def.Size + 0.7f, 0), label, new Color(205, 170, 255, 255), 0.7f);
        if (++CcApplied == 1) Milestone?.Invoke("crowd-control");
    }

    /// The class's AION basic attack (left mouse / RT): a weapon swing, a magic bolt or an arrow.
    public void BasicAttack()
    {
        if (P.BasicCd > 0 || P.Casting != null || Lost || Won || P.EvadeTime > 0 || PlayerStun > 0) return;
        P.BasicCd = P.Cls.BasicInterval;
        _lastSource = "Basic attack";
        var dmg = P.Cls.BasicDamage * DamageMult * StealthMult;
        var col = Hex(P.Cls.Color);
        if (P.Cls.BasicKind == "melee")
        {
            if (Locked is { } lunge && Dist2(lunge.Pos, P.Pos) > P.Cls.BasicRange + lunge.Radius && Dist2(lunge.Pos, P.Pos) < P.Cls.BasicRange + lunge.Radius + 4 && P.Pos.Y < 0.3f)
            {
                var d = Flat(lunge.Pos - P.Pos);
                var stop = Dist2(lunge.Pos, P.Pos) - P.Cls.BasicRange * 0.7f - lunge.Radius;
                var from = P.Pos;
                for (var k = 0f; k <= stop; k += 0.25f) { var next = from + d * k; if (Blocked(next, 0.45f)) break; P.Pos = next; }
                Bits.Emit(from + Vector3.UnitY * 0.2f, 8, new Color(130, 120, 105, 140), 1.5f, 0.2f, 0.6f, 0, grow: 0.4f);
            }
            var fwd = Flat(LockedWithin(P.Cls.BasicRange + 1) is { } l ? l.Pos - P.Pos : AimDir());
            var hit = 0;
            foreach (var e in Enemies.OrderBy(e => Dist2(e.Pos, P.Pos)).ToList())
            {
                var to = e.Pos - P.Pos; to.Y = 0;
                var dist = to.Length();
                if (!InReach(e) || dist > P.Cls.BasicRange + e.Radius || (dist > 0.8f && Vector3.Dot(to / dist, fwd) < 0.55f)) continue;
                var crit = _rng.NextDouble() < CritChance(0.1f);
                var d = ElementDamage(e, dmg * (0.9f + (float) _rng.NextDouble() * 0.2f) * (crit ? 1.5f : 1f) * ConsumeCounter(e.Centre) * ClassBonus(e, null, true), P.Cls.BasicElement, out var tag);
                Damage(e, d, crit, tag);
                if (++hit == 2) break;
            }
            Effects.Add(new Fx { Kind = FxKind.Slash, A = P.Pos + Vector3.UnitY, B = fwd, Radius = P.Cls.BasicRange, Col = col, Life = 0.18f });
            Sfx.PlayGears("hero", "swing", 0.5f);
            if (hit > 0) HitSound();
            if (hit > 0) Bits.Emit(P.Pos + Vector3.UnitY * 1.1f + fwd * 1.4f, 8, new Color(255, 215, 150, 255), 3.5f, 0.05f, 0.35f, 4f, glow: true);
            P.ActionAnim = 0;
            Sfx.Play(Sfx.Reload);
        }
        else
        {
            var aim = AimHit(P.Cls.BasicRange);
            var t = LockedWithin(P.Cls.BasicRange) ?? aim.enemy ?? NearestInCone(P.Pos, aim.dir, P.Cls.BasicRange, 0.4f);
            var end = t?.Centre ?? aim.point;
            var arrow = P.Cls.BasicKind == "arrow";
            if (arrow) PlayOnce(Clip.Cast); // draw the bow
            if (!arrow) col = ElementColor(P.Cls.BasicElement);
            Effects.Add(new Fx { Kind = arrow ? FxKind.Tracer : FxKind.Beam, A = Muzzle(), B = end, Col = arrow ? new Color(230, 210, 170, 255) : col, Life = arrow ? 0.12f : 0.22f, Radius = 0.07f, Element = P.Cls.BasicElement });
            if (t != null)
            {
                var crit = _rng.NextDouble() < CritChance(0.1f);
                // a headshot hits harder; past 70% of the range shots start to lose bite
                var head = t == aim.enemy && aim.point.Y > t.Pos.Y + t.Def.Size * 0.78f;
                var falloff = 1 - 0.3f * Math.Clamp((Dist2(t.Pos, P.Pos) / P.Cls.BasicRange - 0.7f) / 0.3f, 0, 1);
                var d = ElementDamage(t, dmg * (0.9f + (float) _rng.NextDouble() * 0.2f) * (crit ? 1.5f : 1f) * (head ? 1.5f : 1) * falloff * ConsumeCounter(t.Centre) * ClassBonus(t, null, true), P.Cls.BasicElement, out var tag);
                Damage(t, d, crit, head ? "HEADSHOT" : tag);
                if (!arrow) ElementImpact(P.Cls.BasicElement, t.Centre, 0.6f);
            }
            Bits.Emit(end, 8, arrow ? new Color(255, 230, 190, 255) : col, 2.5f, 0.07f, 0.35f, 1f, glow: true);
            Sfx.Play(arrow ? Sfx.Click : Sfx.Cast);
        }
        P.Stealth = 0;
        if (++BasicAttacks == 1) Milestone?.Invoke("basic-attack");
    }

    void Damage(Enemy e, float dmg, bool crit, string tag = "")
    {
        if (tag != "" && e.Hp > 0) AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.75f, 0), tag, tag == "WEAK" ? new Color(255, 140, 90, 255) : new Color(160, 170, 190, 255), 0.6f);
        if (e.Hp <= 0 || e.Burrow > 0) return;
        if (e.InCover) dmg *= 0.7f; // tucked behind cover
        if (e.HitAnim <= 0 && dmg > e.MaxHp * 0.04f) e.HitAnim = 0.45f;
        AddStagger(e, dmg);
        CountDamage(MathF.Min(dmg, e.Hp));
        ChargeUltimate(dmg / 45f);
        RegisterHit();
        e.Hp -= dmg;
        e.Flash = 0.12f;
        DamageDealt += dmg;
        Bits.Emit(e.Centre, crit ? 14 : 8, Blood, crit ? 4f : 3f, 0.05f, 0.7f);
        AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.3f, 0), ((int) dmg).ToString("N0"), crit ? new Color(255, 200, 40, 255) : Color.White, crit ? Sheets.DamageNumbersCritScale : 1f);
        if (e.Hp <= 0 && !TryDown(e)) Kill(e);
    }

    void Kill(Enemy e, bool reward = true)
    {
        // out of the fight first, so a blast it sets off can't come back to it
        if (!Enemies.Remove(e)) return;
        e.Hp = MathF.Min(e.Hp, 0);
        if (e.Def.Splash > 0 && !e.Def.Ranged) Explode(e);
        Bits.Emit(e.Centre, 30, Blood, 4.5f, 0.06f, 0.9f);
        if (e.Def.FlyHeight == 0) Mark(e.Pos, 0.5f + e.Def.Size * 0.25f, new Color(70, 10, 8, 150), 60);
        Bits.Emit(e.Centre, 8, new Color(70, 22, 18, 255), 5f, 0.12f, 1.2f);
        if (e.Def.Size > 2.2f) AddShake(0.3f);
        e.Dead = 0;
        Dying.Add(e);
        if (reward) { Kills++; AddScore(e); ChargeUltimate(3); Progression.Count("kills"); Progression.Count("kill:" + e.Def.Id); DropLoot(e); Say($"Killed Lv {EnemyLevel(e)} {e.Def.Name}", new Color(170, 165, 160, 255)); CombatNote($"{e.Def.Name} dies", new Color(200, 190, 180, 255)); }
        if (IsBoss(e)) OnBossKilled(e);
        Effects.Add(new Fx { Kind = FxKind.Burst, A = e.Centre, Radius = e.Def.Size * 0.6f, Col = Hex(e.Def.Color), Life = 0.5f });
        if (reward) AddNumber(e.Pos + new Vector3(0, e.Def.Size + 0.9f, 0), $"+{e.Def.Xp} XP", new Color(190, 140, 255, 255), 0.75f);
        if (!Sfx.PlayGearsAt(e.Def.Id, "death", e.Pos, Falloff(e.Pos))) Sfx.Play(Sfx.Kill);
        if (!reward) return;
        if (Kills == 1) Milestone?.Invoke("enemy-killed");
        GainXp(e.Elite ? e.Def.Xp * 2 : e.Def.Xp);
    }

    void GainXp(int xp)
    {
        xp = (int) (xp * Progression.XpBonus * (_bonusWave ? 2 : 1));
        P.Xp += xp;
        while (P.Level < Sheets.XpMaxLevel && P.Xp >= LevelRow.XpToNext)
        {
            P.Xp -= LevelRow.XpToNext;
            P.Level++;
            P.MaxHp += P.Cls.HpPerLevel; P.MaxMp += P.Cls.MpPerLevel;
            P.Hp = P.MaxHp; P.Mp = P.MaxMp;
            LevelUps++;
            if (P.Level >= 10) Achieve("level-10");
            if (P.Level >= Sheets.XpMaxLevel && !_prestiged)
            {
                _prestiged = true;
                Profile.P.Prestige++;
                Banner($"PRESTIGE {Profile.P.Prestige}\n+1% damage on every Daeva, for good");
                Profile.Save();
            }
            var news = new List<string>();
            foreach (var s in Sheets.Skills.Where(k => k.Class == P.Cls.Id))
            {
                if (s.UnlockLevel == P.Level) news.Add(s.ChainFrom != "none" ? $"New chain: {s.Name}" : $"New skill: {s.Name}");
                else if (s.RankLevels.Contains(P.Level)) news.Add($"{s.Name} rank {Rank(s)}");
            }
            Banner($"LEVEL {P.Level}  {LevelRow.Title}" + (news.Count > 0 ? "\n" + string.Join("   ", news) : ""));
            Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 1.5f, Col = new Color(255, 215, 90, 255), Life = 1.0f });
            Sfx.Play(Sfx.LevelUp);
            Milestone?.Invoke("level-up");
        }
        if (P.Level >= Sheets.XpMaxLevel) P.Xp = 0;
    }

    /// One line per finished run, for balance passes over autoplay and soak logs.
    readonly Queue<string> _recentHits = new();

    void LogRunSummary() =>
        Log.Info($"run summary: class {P.Cls.Id}, mode {Mode}, {(Won ? "won" : "fell")} on wave {Wave + (Won ? 0 : 1)}, level {P.Level}, {TimeAlive:0} s, kills {Kills}, dealt {DamageDealt:0}, taken {DamageTaken:0}, "
            + "top " + string.Join(" / ", DamageBySource.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value:0}"))
            + (Won ? "" : $"; last hits (max HP {P.MaxHp:0}): " + string.Join(", ", _recentHits)));

    public void TakeDamage(float dmg, bool melee = false)
    {
        if (Lost || Won) return;
        if ((P.Flying || Up > 1) && P.EvadeTime <= 0)
        {
            // hit in the air: feathers knocked loose drift down
            var white = Settings.S.WingStyle == 0;
            for (var i = 0; i < 6; i++)
                Bits.Emit(P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 2.4f * Wings.Size, 1.6f, ((float) _rng.NextDouble() - 0.5f) * 1.2f) - P.Forward * 0.4f, 1,
                    white ? new Color(236, 236, 244, 230) : new Color(60, 46, 80, 230), 1.4f, 0.09f, 2.6f, 0.6f, push: new Vector3(0.6f, 0, 0.2f));
        }
        if (P.EvadeTime > 0)
        {
            CounterTime = 1.5f; // a dodged hit opens a counter-attack
            AddNumber(P.Pos + new Vector3(-0.4f, 2.0f, 0), "Evade", new Color(170, 220, 255, 255), 0.9f);
            return;
        }
        if (P.Shield > 0)
        {
            var absorbed = MathF.Min(P.Shield, dmg);
            P.Shield -= absorbed; dmg -= absorbed;
            AddNumber(P.Pos + new Vector3(0.4f, 2.0f, 0), $"{(int) absorbed} absorbed", new Color(120, 180, 255, 255), 0.7f);
        }
        if (dmg <= 0) return;
        dmg = Defend(dmg, melee);
        if (dmg <= 0) return;
        if (P.GuardTime > 0) dmg *= 1 - P.Guard / 100f;
        dmg *= GearDefence * StanceTaken;
        GainWrath(6);
        if (dmg > P.MaxHp * 0.05f) { if (P.ActionAnim < 0 && P.Casting == null) PlayOnce(Clip.Hit); Voice("pain", 0.6f); }
        P.Hp -= dmg;
        if (dmg >= 1)
        {
            // the last few hits, for the run summary (what a run actually died to)
            var near = Enemies.Where(e => e.Hp > 0).OrderBy(e => Dist2(e.Pos, P.Pos)).FirstOrDefault();
            _recentHits.Enqueue($"{TimeAlive:0.0}s {(int) dmg}{(melee ? " melee" : "")} near {near?.Def.Id ?? "-"}" + (Telegraphs.Count > 0 ? $" telegraphs {string.Join("+", Telegraphs.Select(t => t.Kind))}" : ""));
            while (_recentHits.Count > 6) _recentHits.Dequeue();
        }
        if (dmg >= 1) CombatNote($"You take {(int) dmg}", new Color(255, 140, 120, 255));
        DamageTaken += dmg;
        AddShake(MathF.Min(0.6f, dmg / P.MaxHp * 4));
        if (P.Hp <= 0 && P.Salvation > 0 && !Hardcore)
        {
            // AION salvation: rise again with half HP
            P.Hp = P.MaxHp / 2; P.Salvation = 0;
            Banner("SALVATION");
            Effects.Add(new Fx { Kind = FxKind.Pillar, A = P.Pos, Radius = 2, Col = new Color(255, 240, 170, 255), Life = 1.2f });
        }
        P.HurtFlash = 0.25f;
        AddNumber(P.Pos + new Vector3(-0.4f, 2.0f, 0), "-" + (int) dmg, new Color(255, 70, 60, 255), 0.9f);
        Sfx.Play(Sfx.Hurt);
        if (P.Hp <= 0) { P.Hp = 0; Lost = true; Voice("death"); _voiceCd = 0; Banner("YOU FELL"); Log.Info($"player fell on wave {Wave + 1} at level {P.Level}"); LogRunSummary(); }
    }

    // ---------------------------------------------------------------- guns

    public GunState Gun => P.Guns[P.Gun];

    public void SwitchGun(int index)
    {
        if (index == P.Gun || index < 0 || index >= P.Guns.Length) return;
        P.Gun = index;
        P.FireCd = 0.35f;
        Gun.Reload = 0;
    }

    public int PerfectReloads;
    public string ReloadText = "";
    public float ReloadTextTime;
    public float GunBonus => P.ActiveBonus > 0 ? 1 + Sheets.ActiveReloadBonusDamagePct / 100f : 1;

    /// Starts a reload (an empty clip does this by itself).
    public void Reload()
    {
        if (Gun.Reload > 0 || Gun.Ammo == Gun.Def.Clip) return;
        Gun.Reload = Gun.ReloadTotal = Gun.Def.ReloadSeconds;
        Gun.Tried = Gun.Jammed = false;
        if (!Sfx.PlayGears(Gun.Def.Id, "reload")) Sfx.Play(Sfx.Reload);
    }

    /// The reload button: starts a reload, or during one tries the Active Reload.
    public void ReloadPressed()
    {
        var g = Gun;
        if (g.Reload <= 0) { Reload(); return; }
        if (g.Tried) return;
        g.Tried = true;
        var at = g.Progress - Sheets.ActiveReloadZoneStart;
        if (at >= 0 && at <= Sheets.ActiveReloadPerfectWidth)
        {
            P.ActiveBonus = Sheets.ActiveReloadBonusSeconds;
            PerfectReloads++;
            ReloadFlash($"PERFECT  +{Sheets.ActiveReloadBonusDamagePct}% damage");
            FinishReload("reload-perfect");
            if (PerfectReloads == 1) Milestone?.Invoke("active-reload");
        }
        else if (at >= 0 && at <= Sheets.ActiveReloadGoodWidth)
        {
            ReloadFlash("ACTIVE RELOAD");
            FinishReload("reload-done");
        }
        else
        {
            g.Jammed = true;
            g.Reload += Sheets.ActiveReloadJamSeconds;
            g.ReloadTotal += Sheets.ActiveReloadJamSeconds;
            ReloadFlash("JAMMED");
            if (!Sfx.PlayGears(g.Def.Id, "reload-jam")) Sfx.Play(Sfx.Hurt);
        }
    }

    void FinishReload(string sound)
    {
        Gun.Reload = 0;
        Gun.Ammo = Gun.Def.Clip;
        if (!Sfx.PlayGears(Gun.Def.Id, sound)) Sfx.Play(Sfx.Click);
    }

    void ReloadFlash(string text) { ReloadText = text; ReloadTextTime = 1.4f; }

    public void Fire()
    {
        if (!Sheets.GunsEnabled) { BasicAttack(); return; }
        var g = Gun;
        if (P.FireCd > 0 || g.Reload > 0) return;
        if (g.Ammo <= 0) { Reload(); return; }
        g.Ammo--;
        P.FireCd = g.Def.FireInterval;
        P.Recoil = 1;
        var cam = CameraEye();
        var aimDir = AimDir();
        for (var i = 0; i < g.Def.Pellets; i++)
        {
            var dir = Spread(aimDir, g.Def.SpreadDegrees);
            var hit = Raycast(cam, dir, g.Def.Range + 6);
            Effects.Add(new Fx { Kind = FxKind.Tracer, A = Muzzle(), B = hit.point, Col = P.ActiveBonus > 0 ? new Color(255, 120, 60, 255) : new Color(255, 214, 140, 255), Life = 0.08f });
            if (hit.enemy is { } e)
            {
                var crit = _rng.NextDouble() < g.Def.CritChance;
                var falloff = g.Def.Pellets > 1 ? Math.Clamp(1.4f - Vector3.Distance(P.Pos, e.Pos) / g.Def.Range, 0.35f, 1.2f) : 1f;
                Damage(e, g.Def.Damage * P.Cls.GunPower * DamageMult * GunBonus * falloff * (crit ? 2f : 1f), crit);
            }
        }
        if (!Sfx.PlayGears(g.Def.Id, "fire")) Sfx.Play(g.Def.Pellets > 1 ? Sfx.Shotgun : g.Def.Damage >= 100 ? Sfx.Sniper : Sfx.Rifle);
        if (g.Ammo == 0) Reload();
    }

    Vector3 Spread(Vector3 dir, float degrees)
    {
        if (degrees <= 0) return dir;
        var r = degrees * MathF.PI / 180 * MathF.Sqrt((float) _rng.NextDouble());
        var a = (float) _rng.NextDouble() * MathF.Tau;
        var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        var up = Vector3.Cross(right, dir);
        return Vector3.Normalize(dir + (right * MathF.Cos(a) + up * MathF.Sin(a)) * MathF.Tan(r));
    }

    // ---------------------------------------------------------------- aiming

    public Vector3 AimDir() =>
        Vector3.Normalize(new Vector3(MathF.Sin(P.Yaw) * MathF.Cos(P.Pitch), MathF.Sin(P.Pitch), MathF.Cos(P.Yaw) * MathF.Cos(P.Pitch)));

    /// Pulls a camera eye in front of the first wall between it and `look` (clips).
    /// How far a camera ray from `from` toward `dir` gets before a wall or building piece (walls the hero stands inside are ignored).
    float CameraRay(Vector3 from, Vector3 dir, float len)
    {
        var ray = new Ray(from, dir);
        var near = len;
        void Test(List<BoundingBox> boxes)
        {
            foreach (var box in boxes)
            {
                // quick reject: boxes nowhere near the ray's span
                if (box.Max.X < MathF.Min(from.X, from.X + dir.X * len) - 0.5f || box.Min.X > MathF.Max(from.X, from.X + dir.X * len) + 0.5f) continue;
                if (box.Max.Z < MathF.Min(from.Z, from.Z + dir.Z * len) - 0.5f || box.Min.Z > MathF.Max(from.Z, from.Z + dir.Z * len) + 0.5f) continue;
                if (from.X > box.Min.X && from.X < box.Max.X && from.Y > box.Min.Y && from.Y < box.Max.Y && from.Z > box.Min.Z && from.Z < box.Max.Z) continue;
                var c = Raylib.GetRayCollisionBox(ray, box);
                if (c.Hit && c.Distance > 0 && c.Distance < near) { near = c.Distance; LastCameraBox = box; }
            }
        }
        Test(Cover);
        Test(Mounds); // the camera doesn't sink into piles either
        if (_assets.City != null)
        {
            Test(_assets.City.CameraBoxes);
            // big buildings: their actual walls
            var end = from + dir * len;
            int x0 = (int) MathF.Floor((MathF.Min(from.X, end.X) - 0.3f) / City.TriCell), x1 = (int) MathF.Floor((MathF.Max(from.X, end.X) + 0.3f) / City.TriCell);
            int z0 = (int) MathF.Floor((MathF.Min(from.Z, end.Z) - 0.3f) / City.TriCell), z1 = (int) MathF.Floor((MathF.Max(from.Z, end.Z) + 0.3f) / City.TriCell);
            for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++)
                    if (_assets.City.CameraTris.TryGetValue((x, z), out var tris))
                        foreach (var (a, b, c) in tris)
                        {
                            var hit = Raylib.GetRayCollisionTriangle(ray, a, b, c);
                            if (hit.Hit && hit.Distance > 0 && hit.Distance < near) near = hit.Distance;
                        }
        }
        return near;
    }

    /// Pulls a camera eye in front of the first wall between it and `look` (clips).
    public Vector3 ClipCamera(Vector3 look, Vector3 eye)
    {
        var to = eye - look;
        var len = to.Length();
        if (len < 0.01f) return eye;
        var near = CameraRay(look, to / len, len);
        return near < len ? look + to / len * MathF.Max(0.5f, near - 0.3f) : eye;
    }

    /// True when the camera had to come in close to the hero (the hero is then drawn see-through).
    public bool CameraClose;
    public BoundingBox LastCameraBox; // dev: the last box that pulled the camera in

    public Vector3 CameraEye()
    {
        // pulled back in flight and when a boss is close, so the fight stays in frame
        var frame = (P.Flying || P.Pos.Y > 1.5f ? 1.3f : 1) * (Boss is { } bf && Dist2(bf.Pos, P.Pos) < 22 ? 1.25f : 1);
        var back = -AimDir() * Sheets.MovementCameraDistance * Settings.S.CameraDistance * frame;
        var eye = P.Pos + new Vector3(0, Sheets.MovementCameraHeight + (frame > 1.2f ? 0.6f : 0), 0) + back + P.Right * Sheets.MovementShoulderOffset * (Settings.S.LeftShoulder ? -1 : 1);
        eye.Y = MathF.Max(eye.Y, 0.4f);
        // keep the camera out of walls: pull it in front of the first wall or building piece between the hero and the eye
        var head = P.Pos + new Vector3(0, Sheets.MovementCameraHeight * 0.85f, 0);
        var to = eye - head;
        var len = to.Length();
        CameraClose = false;
        if (len > 0.01f)
        {
            var near = CameraRay(head, to / len, len);
            if (near < len) { eye = head + to / len * MathF.Max(0.3f, near - 0.3f); CameraClose = near < 1.8f; }
            // never leave the lens inside a wall: walk it back toward the hero until it's clear
            for (var k = 0; k < 10 && InsideAnyBox(eye, head); k++) { eye = Vector3.Lerp(eye, head, 0.3f); CameraClose = true; }
        }
        return eye;
    }

    /// Is p inside a wall or cover box (boxes the hero's head is inside don't count, like the camera ray)?
    bool InsideAnyBox(Vector3 p, Vector3 head)
    {
        static bool In(BoundingBox b, Vector3 p) => p.X > b.Min.X && p.X < b.Max.X && p.Y > b.Min.Y && p.Y < b.Max.Y && p.Z > b.Min.Z && p.Z < b.Max.Z;
        foreach (var b in Cover) if (In(b, p) && !In(b, head)) return true;
        if (_assets.City != null) foreach (var b in _assets.City.CameraBoxes) if (In(b, p) && !In(b, head)) return true;
        return false;
    }

    public Vector3 HoldPoint()
    {
        if (_assets.Characters.TryGetValue(HeroId, out var hero) && hero.Def.HandBone != "none")
        {
            var h = hero.HandAt(Clip.Aim, 0);
            var (s, c) = MathF.SinCos(P.Yaw);
            return P.Pos + new Vector3(h.X * c + h.Z * s, h.Y, -h.X * s + h.Z * c) + AimDir() * (0.12f - P.Recoil * 0.04f);
        }
        return P.Pos + new Vector3(0, 1.25f - P.Recoil * 0.02f, 0) + P.Right * 0.38f + P.Forward * 0.25f;
    }

    public Vector3 Muzzle()
    {
        var len = _assets.Guns.TryGetValue(Gun.Def.Id, out var m) ? m.Size.X : 0.8f;
        return HoldPoint() + AimDir() * (len * 0.5f + 0.05f);
    }

    public (Enemy? enemy, Vector3 point, Vector3 dir) AimHit(float range)
    {
        var dir = AimDir();
        var hit = Raycast(CameraEye(), dir, range + Sheets.MovementCameraDistance);
        return (hit.enemy, hit.point, dir);
    }

    (Enemy? enemy, Vector3 point) Raycast(Vector3 from, Vector3 dir, float range)
    {
        var ray = new Ray(from, dir);
        var best = range;
        Enemy? who = null;
        foreach (var e in Enemies)
        {
            var c = Raylib.GetRayCollisionSphere(ray, e.Centre, e.Def.Size * 0.42f);
            if (c.Hit && c.Distance < best) { best = c.Distance; who = e; }
        }
        foreach (var b in Cover)
        {
            var c = Raylib.GetRayCollisionBox(ray, b);
            if (c.Hit && c.Distance < best) { best = c.Distance; who = null; }
        }
        if (dir.Y < 0)
        {
            var t = -from.Y / dir.Y;
            if (t < best) { best = t; who = null; }
        }
        return (who, from + dir * best);
    }

    Enemy? NearestInCone(Vector3 from, Vector3 dir, float range, float minDot)
    {
        Enemy? best = null;
        var bestD = float.MaxValue;
        var f = Flat(dir);
        foreach (var e in Enemies)
        {
            var to = e.Pos - from; to.Y = 0;
            var d = to.Length();
            if (d > range + e.Radius || d < 0.01f) continue;
            if (Vector3.Dot(to / d, f) < minDot) continue;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    // ---------------------------------------------------------------- update

    public void Update(float dt, Vector2 move, bool sprint)
    {
        if (BannerTime > 0) BannerTime -= dt;
        UpdateLoot(dt);
        UpdateModes(dt);
        UpdateClasses(dt, move);
        UpdateAscend(dt);
        UpdateFlightExtras(dt, move);
        UpdateHeroAnim(dt);
        UpdateWeather(dt);
        UpdateAudio(dt);
        UpdateDressing(dt);
        UpdateStory();
        UpdateCombat(dt);
        if (Won || Lost) RecordRun();
        foreach (var k in P.Cooldowns.Keys.ToList()) P.Cooldowns[k] = MathF.Max(0, P.Cooldowns[k] - dt);
        P.Mp = MathF.Min(P.MaxMp, P.Mp + P.Cls.MpRegen * PassiveMpRegen * dt);
        P.FireCd -= dt; P.HurtFlash -= dt; P.Recoil = MathF.Max(0, P.Recoil - dt * 8);
        if (P.BuffTime > 0 && (P.BuffTime -= dt) <= 0) P.BuffPct = 0;
        if (P.ShieldTime > 0 && (P.ShieldTime -= dt) <= 0) P.Shield = 0;
        if (Gun.Reload > 0 && (Gun.Reload -= dt) <= 0) FinishReload("reload-done");
        if (P.ActiveBonus > 0) P.ActiveBonus -= dt;
        if (ReloadTextTime > 0) ReloadTextTime -= dt;
        if (P.AuraTime > 0)
        {
            P.AuraTime -= dt; P.AuraTick -= dt;
            if (P.AuraTick <= 0)
            {
                P.AuraTick = 1f / Sheets.SkillKindsAuraTicksPerSecond;
                foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, P.Pos) <= P.AuraRadius + e.Radius) HitWithSkill(e, P.AuraPower);
                Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = P.AuraRadius, Col = Hex(P.Cls.Color), Life = 0.4f });
            }
        }

        P.EvadeCd -= dt;
        Bits.Update(dt);
        UpdateAmbience(dt);
        Shake = MathF.Max(0, Shake - dt * 2.5f);
        if (!Won && !Lost) TimeAlive += dt;
        if (Up > 0.3f && _rng.NextDouble() < dt * 30)
            Bits.Emit(P.Pos + Vector3.UnitY * 1.5f + P.Right * ((float) _rng.NextDouble() * 2.4f - 1.2f) - P.Forward * 0.4f, 1, Hex(P.Cls.Color), 0.3f, 0.06f, 1.2f, 0.6f, glow: true);
        _moveInput = move;
        UpdateMoves(dt, move);
        UpdateFlight(dt);
        Abyss.Update(dt, P.Pos);
        UpdateWreckSmoke(dt);
        UpdateShards();
        UpdateAbyssStorm(dt);
        UpdateVantage(dt);
        Unstick(dt);
        UpdateAion(dt, move);
        foreach (var c in P.Chains) if (c != null) c.Time -= dt;
        if (Locked != null && !Enemies.Contains(Locked)) Locked = null;
        if (P.EvadeTime > 0 && !Lost && !Won)
        {
            MoveCircle(ref P.Pos, P.EvadeDir * (Sheets.EvasionDistance / Sheets.EvasionSeconds) * dt, 0.45f);
            P.EvadeTime -= dt;
            if ((_ghostTimer -= dt) <= 0)
            {
                _ghostTimer = 0.045f;
                _ghosts.Add((P.Pos, MathF.Atan2(P.EvadeDir.X, P.EvadeDir.Z), 1 - P.EvadeTime / Sheets.EvasionSeconds, 0));
            }
        }
        else if (!Lost && !Won && PlayerStun <= 0 && !Climbing)
        {
            P.Sprinting = sprint && move.Y > 0.1f;
            if (Up > 0.3f) P.Sprinting = false;
            var speed = P.Cls.MoveSpeed * (P.Sprinting ? Sheets.MovementSprintMultiplier : 1f) * (P.Flying ? Sheets.FlightSpeedMultiplier : 1f) * (P.HasteTime > 0 ? 1 + P.Haste / 100f : 1f) * GearSpeed * StanceSpeed * TransformSpeed;
            var wish = P.Forward * move.Y + P.Right * move.X;
            if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);
            var was = P.Pos;
            MoveCircle(ref P.Pos, wish * speed * dt, 0.45f);
            // a sheer rise in the street (a plaza's edge) is a ledge: climb it (Space), don't walk into it
            if (!P.Flying && GroundAt(P.Pos) > P.Pos.Y + 0.6f) { P.Pos.X = was.X; P.Pos.Z = was.Z; }
            P.MoveBob += wish.Length() * dt * MathF.Tau / WalkCycle(HeroId) * (P.Sprinting ? 1.4f : 1f);
        }

        for (var i = _ghosts.Count - 1; i >= 0; i--)
        {
            var g = _ghosts[i];
            g.age += dt;
            if (g.age > 0.28f) _ghosts.RemoveAt(i); else _ghosts[i] = g;
        }
        // dust kicked up by running feet
        if (Up < 0.05f && P.MoveBob != _dustBob && _rng.NextDouble() < dt * (P.Sprinting ? 16 : 6))
            Bits.Emit(P.Pos + new Vector3(0, 0.08f, 0) - P.Forward * 0.2f, 1, new Color(120, 112, 100, 110), 0.8f, 0.14f, 0.9f, -0.2f, grow: 0.5f);
        _dustBob = P.MoveBob;
        // the dead dissolve: ash and embers lift off as they sink away
        foreach (var d in Dying)
            if (d.Dead > 1.0f && _rng.NextDouble() < dt * 14)
                Bits.Emit(d.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 0.8f, 0.3f + (float) _rng.NextDouble() * d.Def.Size * 0.4f, ((float) _rng.NextDouble() - 0.5f) * 0.8f), 1,
                    _rng.NextDouble() < 0.4 ? new Color(255, 120, 50, 255) : new Color(60, 55, 52, 160), 0.6f, 0.06f, 1.2f, -1.6f, glow: _rng.NextDouble() < 0.4);

        UpdateNav(dt);
        UpdateRally();
        UpdateEnemies(dt);
        UpdateSquadmate(dt);
        UpdateKryll(dt);
        UpdateBossAndSpecials(dt);
        for (var i = Dying.Count - 1; i >= 0; i--) if ((Dying[i].Dead += dt) > MathF.Max(2.2f, ClipLength(Dying[i], Clip.Death) + 1.8f)) Dying.RemoveAt(i);
        for (var i = Holes.Count - 1; i >= 0; i--) if ((Holes[i].Life -= dt) <= 0) { Mark(Holes[i].Pos, Holes[i].Radius * 1.3f, new Color(30, 24, 20, 170), 120); Holes.RemoveAt(i); }
        UpdateShots(dt);
        UpdateWaves(dt);

        for (var i = Effects.Count - 1; i >= 0; i--) if ((Effects[i].Age += dt) >= Effects[i].Life) Effects.RemoveAt(i);
        for (var i = Numbers.Count - 1; i >= 0; i--)
        {
            var n = Numbers[i];
            n.Age += dt;
            n.Pos.Y += Sheets.DamageNumbersRiseSpeed * dt;
            if (n.Age >= Sheets.DamageNumbersLifetime) Numbers.RemoveAt(i);
        }
    }

    void UpdateEnemies(float dt)
    {
        foreach (var e in Enemies.ToList())
        {
            e.Flash -= dt; e.Bob += dt;
            for (var i = e.Dots.Count - 1; i >= 0; i--)
            {
                var d = e.Dots[i];
                if ((d.Timer -= dt) > 0) continue;
                d.Timer += 1f / Sheets.SkillKindsDotTicksPerSecond;
                d.Ticks--;
                if (d.Ticks <= 0) e.Dots.RemoveAt(i);
                AddNumber(e.Pos + new Vector3(0.3f, e.Def.Size + 0.1f, 0), ((int) d.PerTick).ToString(), d.Col, 0.75f);
                e.Hp -= d.PerTick;
                if (e.Hp <= 0) { Kill(e); break; }
            }
            if (e.Hp <= 0 || Lost) continue;
            if (e.Burrow > 0) { e.Moving = false; continue; }
            if (e.Emerge >= 0)
            {
                // climbing out of the E-hole: can be shot, but doesn't move or attack yet
                var face = P.Pos - e.Pos;
                e.Yaw = MathF.Atan2(face.X, face.Z);
                if ((e.Emerge += dt) >= ClipLength(e, Clip.Spawn)) e.Emerge = -1;
                continue;
            }
            if (e.Push.LengthSquared() > 0.04f)
            {
                var was = e.Pos;
                MoveCircle(ref e.Pos, e.Push * dt, e.Radius);
                if (e.Push.Length() > 5 && Vector3.Distance(was, e.Pos) < e.Push.Length() * dt * 0.3f) WallSlam(e);
                e.Push *= MathF.Max(0, 1 - 7 * dt);
                if (e.Hp <= 0) continue;
            }
            if (e.Root > 0) e.Root -= dt;
            if (e.Slow > 0) e.Slow -= dt;
            if (e.Air > 0) e.Air -= dt;
            if (e.Down > 0 && (e.Down -= dt) <= 0) e.GetUp = 0.7f;
            if (e.GetUp > 0) e.GetUp -= dt;
            if (e.HitAnim > 0) e.HitAnim -= dt;
            if (e.Stun > 0) { e.Stun -= dt; continue; }
            var forVan = GoesForVan(e);
            if (P.Stealth > 0 && !forVan) { e.Moving = false; continue; } // they've lost you
            var allyGoal = forVan ? null : AllyTarget(e);
            var goal = forVan ? Van!.Pos : allyGoal?.Pos ?? P.Pos;
            var to = goal - e.Pos; to.Y = 0;
            var dist = forVan ? MathF.Max(0, to.Length() - 2.4f) : to.Length();
            var dir = dist > 0.01f ? to / dist : Vector3.Zero;
            var step = Vector3.Zero;
            var flyer = e.Def.FlyHeight > 0;
            var sees = flyer || forVan || HasLineOfSight(e.Pos, P.Pos);
            if (sees) e.Stuck = 0;
            else if (Unreachable(e.Pos)) e.Stuck += dt;
            if (!sees && e.Stuck > 4 && ReachableSpot(10) is { } elsewhere)
            {
                // walled in somewhere the player can't be reached from: come back out of an E-hole elsewhere
                e.Pos = elsewhere; e.Stuck = 0;
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = elsewhere, Radius = 1, Col = new Color(150, 60, 50, 255), Life = 0.6f });
            }
            if (ClimbAfter(e, dt, dist)) { e.Yaw = MathF.Atan2(dir.X, dir.Z); e.AttackCd -= dt; continue; }
            if (!flyer && !forVan && !ShowStragglers && CoverStep(e) is { } toCover && sees && dist <= e.Def.AttackRange)
            {
                step = toCover;
            }
            else if (flyer)
            {
                // circle the player at range; every 14 s it swoops down to the street for a few seconds (melee can reach it then)
                var swoop = e.Bob % 14 > 10;
                step = swoop ? (dist > 3 ? dir : Vector3.Zero)
                    : dist > e.Def.AttackRange * 0.7f ? dir : new Vector3(-dir.Z, 0, dir.X) * e.Strafe + dir * (dist < 10 ? -0.5f : 0.1f);
                // out of sight behind the buildings: come round them along the streets (the walkers' flow field)
                if (!forVan && dist > e.Def.AttackRange * 0.7f && !HasLineOfSight(e.Pos with { Y = 1.2f }, P.Pos)) step = FlowDir(e.Pos, dir);
                if (_rng.NextDouble() < dt * 0.15) e.Strafe = -e.Strafe;
                var wantY = swoop ? 0.8f : e.Def.FlyHeight + MathF.Sin(e.Bob * 0.8f) * 0.8f;
                // a building in the way: float up and over it rather than pressing against the wall
                var over = TopAhead(e.Pos + (step.LengthSquared() > 0.01f ? Vector3.Normalize(step with { Y = 0 }) : dir) * (e.Radius + 1.5f));
                if (over > wantY - 1.2f) wantY = MathF.Min(over + 1.2f, 30);
                e.Pos.Y += Math.Clamp(wantY - e.Pos.Y, -7 * dt, 7 * dt);
            }
            else if (e.Def.Ranged)
            {
                if (dist > e.Def.AttackRange * 0.8f || !sees) step = FlowDir(e.Pos, dir);
                else if (dist < 7) step = -dir;
                else step = new Vector3(-dir.Z, 0, dir.X) * e.Strafe * 0.6f;
                if (_rng.NextDouble() < dt * 0.3) e.Strafe = -e.Strafe;
            }
            else if (dist > e.Def.AttackRange * 0.85f)
            {
                step = forVan ? dir : FlowDir(e.Pos, dir);
                // melee Locust fan out to come at you from the sides
                if (dist > 5 && sees) step = Vector3.Normalize(step + new Vector3(-dir.Z, 0, dir.X) * e.Strafe * 0.45f);
            }
            if (e.Def.Ranged && e.Def.Special != "boss" && e.Hp < e.MaxHp * 0.25f && dist < e.Def.AttackRange) step = -dir * 0.9f; // badly hurt: back off
            // keep a little apart from each other
            foreach (var o in Enemies)
            {
                if (o == e) continue;
                var away = e.Pos - o.Pos; away.Y = 0;
                var d2 = away.Length();
                var min = e.Radius + o.Radius + 0.3f;
                if (d2 > 0.001f && d2 < min) step += away / d2 * (min - d2) * 2;
            }
            var before = e.Pos;
            if (e.Root > 0) step = Vector3.Zero;
            MoveCircle(ref e.Pos, step * e.Def.Speed * e.SpeedMult * (e.Slow > 0 ? 0.5f : 1f) * dt, e.Radius);
            var moved = Vector3.Distance(before, e.Pos);
            if (!sees && step.LengthSquared() > 0.04f && moved < 0.25f * e.Def.Speed * dt) e.Stuck += dt; // wedged somewhere
            e.Moving = moved > 0.2f * dt;
            e.WalkPhase += moved / (e.Def.Speed * WalkCycle(e.Def.Character));
            if (dist > 0.01f) e.Yaw = LerpAngle(e.Yaw, MathF.Atan2(dir.X, dir.Z), MathF.Min(1, dt * 8));
            if (e.AttackAnim >= 0 && (e.AttackAnim += dt) > 0.7f) e.AttackAnim = -1;
            e.AttackCd -= dt;
            if (forVan && dist <= e.Def.AttackRange && e.AttackCd <= 0 && e.Disarm <= 0)
            {
                e.AttackCd = e.Def.AttackInterval; e.AttackAnim = 0;
                HitVan(EnemyDamage(e));
            }
            else if (!forVan && dist <= e.Def.AttackRange && e.AttackCd <= 0 && sees && e.Disarm <= 0 && e.Downed <= 0 && (e.Def.Ranged || (Up < 1.2f && P.Pos.Y - e.Pos.Y < 2.8f)))
            {
                e.AttackCd = e.Def.AttackInterval;
                e.AttackAnim = 0;
                if (e.Def.Ranged)
                {
                    var from = e.Pos + new Vector3(0, e.Def.Size * 0.8f, 0);
                    if (_assets.Characters.TryGetValue(e.Def.Character, out var ecm) && _assets.HeldGuns.TryGetValue(e.Def.Character, out var ehg))
                    {
                        var hand = EnemyHand(e, ecm, Clip.Idle, 0);
                        from = hand + Vector3.Normalize(P.Pos + new Vector3(0, 1.2f, 0) - hand) * ehg.Size.X * 0.8f;
                    }
                    var aim = Vector3.Normalize(P.Pos + new Vector3(0, 1.2f, 0) - from);
                    Shots.Add(new Shot { Pos = from, Vel = aim * e.Def.ProjectileSpeed, Damage = EnemyDamage(e), Life = 3, Splash = e.Def.Splash, Bile = e.Def.FlyHeight > 0 });
                    Sfx.PlayGearsAt(e.Def.Id, "fire", e.Pos, Falloff(e.Pos));
                    if (e.Def.Id == "boomer" && _rng.NextDouble() < 0.6) Sfx.PlayGearsAt("boomer", "shout", e.Pos, Falloff(e.Pos));
                    else if (e.Def.Id is "drone" && _rng.NextDouble() < 0.15) Sfx.PlayGearsAt("drone", "shout", e.Pos, Falloff(e.Pos));
                }
                else if (e.Def.Splash > 0)
                {
                    // a Ticker: it blows itself up beside you
                    e.Exploded = true;
                    Kill(e, reward: false);
                }
                else
                {
                    if (e.Def.Id == "rusher" && _rng.NextDouble() < 0.3) Sfx.PlayGearsAt("rusher", "shout", e.Pos, Falloff(e.Pos));
                    if (allyGoal != null) HitAlly(allyGoal, EnemyDamage(e));
                    else TakeDamage(EnemyDamage(e), melee: true);
                    if (Modifier == "Bloodlust") e.Hp = MathF.Min(e.MaxHp, e.Hp + EnemyDamage(e) * 0.5f);
                    Effects.Add(new Fx { Kind = FxKind.Slash, A = e.Pos + Vector3.UnitY, B = dir, Radius = e.Def.AttackRange, Col = new Color(255, 80, 60, 255), Life = 0.2f });
                }
            }
        }
    }

    void UpdateShots(float dt)
    {
        for (var i = Shots.Count - 1; i >= 0; i--)
        {
            var s = Shots[i];
            s.Pos += s.Vel * dt;
            s.Life -= dt;
            // trails: Boomer shells smoke, Reaver bile drips
            if (s.Bile && _rng.NextDouble() < dt * 30) Bits.Emit(s.Pos, 1, new Color(120, 200, 60, 220), 0.6f, 0.08f, 0.6f, 9.8f, glow: true);
            else if (s.Splash > 0 && _rng.NextDouble() < dt * 40) Bits.Emit(s.Pos, 1, new Color(80, 74, 70, 120), 0.3f, 0.18f, 1.1f, -0.4f, grow: 0.5f);
            var hitPlayer = Vector3.Distance(s.Pos, P.Pos + new Vector3(0, 1.0f, 0)) < 0.7f;
            BoundingBox? struck = null;
            foreach (var b in Cover) if (Inside(b, s.Pos)) { struck = b; break; }
            var ended = hitPlayer || s.Life <= 0 || s.Pos.Y < 0 || struck != null;
            if (!hitPlayer && s.Splash <= 0 && !s.Bile && (struck != null || s.Pos.Y < 0)) Impact(s.Pos, struck);
            if (hitPlayer && s.Splash <= 0) TakeDamage(s.Damage * CoverFactor(s.Vel));
            if (ended && s.Splash > 0)
            {
                var d = Vector3.Distance(s.Pos, P.Pos + new Vector3(0, 1.0f, 0));
                if (d < s.Splash) TakeDamage(s.Damage * (1 - 0.6f * d / s.Splash) * CoverFactor(s.Vel));
                if (s.Bile)
                {
                    Effects.Add(new Fx { Kind = FxKind.Ring, A = s.Pos with { Y = 0 }, Radius = s.Splash, Col = new Color(120, 210, 60, 255), Life = 0.6f });
                    Bits.Emit(s.Pos, 30, new Color(130, 210, 60, 255), 5f, 0.08f, 0.7f, 9.8f, glow: true);
                    Bits.Emit(s.Pos, 6, new Color(70, 100, 40, 140), 1f, 0.4f, 1.4f, -0.3f, grow: 0.6f);
                }
                else
                {
                    Effects.Add(new Fx { Kind = FxKind.Ring, A = s.Pos with { Y = 0 }, Radius = s.Splash, Col = new Color(255, 120, 40, 255), Life = 0.5f });
                    Effects.Add(new Fx { Kind = FxKind.Burst, A = s.Pos, Radius = s.Splash * 0.6f, Col = new Color(255, 160, 60, 255), Life = 0.5f });
                    Bits.Emit(s.Pos, 24, new Color(255, 150, 50, 255), 6f, 0.1f, 0.5f, 3f, glow: true);
                    Bits.Emit(s.Pos, 10, new Color(90, 80, 72, 160), 2f, 0.4f, 1.6f, -0.5f, grow: 0.9f);
                    Bits.Emit(s.Pos, 10, new Color(60, 52, 46, 255), 6f, 0.07f, 1.0f, 12f);
                    Mark(s.Pos, s.Splash * 0.5f, new Color(22, 18, 15, 160), 600);
                    DamageCover(s.Pos, s.Splash, 120);
                    if (Vector3.Distance(s.Pos, P.Pos) < s.Splash * 3) AddShake(0.25f);
                }
            }
            if (ended)
            {
                Effects.Add(new Fx { Kind = FxKind.Burst, A = s.Pos, Radius = 0.4f, Col = new Color(160, 220, 80, 255), Life = 0.25f });
                Shots.RemoveAt(i);
            }
        }
    }

    void UpdateWaves(float dt)
    {
        if (Won || Lost || Mode == GameMode.Practice) return;
        if (Mode == GameMode.Tutorial && TutorialStep < TutorialSteps.Length) return;
        if (!WaveActive)
        {
            if ((Breather -= dt) > 0) return;
            StartWave();
            return;
        }
        if (TimeAlive - _waveStartedAt > 180 && TimeAlive - _longWaveNote > 60)
        {
            _longWaveNote = TimeAlive;
            Log.Info($"wave {Wave + 1} still running after {TimeAlive - _waveStartedAt:0} s: {Enemies.Count} Locust left ({string.Join(", ", Enemies.Take(6).Select(e => $"{e.Def.Id} at {e.Pos.X:0},{e.Pos.Y:0},{e.Pos.Z:0} hp {e.Hp:0} emerge {e.Emerge:0.0} burrow {e.Burrow:0.0} stuck {e.Stuck:0.0}"))}), queued {_spawnQueue.Count}, extras done {WaveExtrasDone}, hero at {P.Pos.X:0},{P.Pos.Y:0},{P.Pos.Z:0}");
        }
        if (_spawnQueue.Count > 0 && (_spawnTimer -= dt) <= 0)
        {
            _spawnTimer = 0.55f;
            QueueSpawn(_spawnQueue.Dequeue());
        }
        if (_spawnQueue.Count == 0 && Enemies.Count == 0 && WaveExtrasDone)
        {
            WaveActive = false;
            Log.Info($"wave {Wave + 1} cleared: class {P.Cls.Id}, level {P.Level}, {TimeAlive - _waveStartedAt:0} s, HP {P.Hp / P.MaxHp * 100:0}%, potions {PotionsUsed}");
            Wave++;
            OnWaveCleared();
            if (Wave >= WaveCount) { Won = true; Banner(Mode == GameMode.Tutorial ? "TUTORIAL COMPLETE" : Mode == GameMode.Rescue ? "THE KING RAVEN LIFTS THEM OUT\nSquad rescued" : "ARENA CLEARED"); Sfx.Play(Sfx.LevelUp); LogRunSummary(); }
            else { Breather = Sheets.WavesBreatherSeconds; Banner($"Wave cleared. Next: {WaveInfo(Wave).Name}"); }
        }
    }

    public void SkipBreather() { if (!WaveActive && !Won && !Lost && Mode != GameMode.Practice && (Mode != GameMode.Tutorial || TutorialStep >= TutorialSteps.Length)) Breather = 0; }

    /// Dev (clips): start at a later wave and level.
    public void Jump(int wave, int level)
    {
        Wave = Math.Clamp(wave - 1, 0, Math.Max(0, Math.Min(WaveCount, 99) - 1));
        while (P.Level < Math.Min(level, Sheets.XpMaxLevel)) GainXp(LevelRow.XpToNext - P.Xp);
        Banners.Clear(); BannerTime = 0; Effects.Clear();
    }

    float _waveStartedAt, _longWaveNote;
    public int PotionsUsed;

    void StartWave()
    {
        _waveStartedAt = TimeAlive;
        var w = WaveInfo(Wave);
        _waveHpScale = w.HpScale;
        PickModifier(Wave);
        _bonusWave = Wave > 0 && _rng.NextDouble() < 0.12;
        if (_bonusWave) Say("Bonus XP wave: double XP", new Color(190, 140, 255, 255));
        Env.ForWave(Mode, Wave, Mode == GameMode.Daily ? Modes.DailySeed : _weatherSeed);
        OnWaveStart();
        if (Mode == GameMode.Campaign && Wave < Story.Briefings.Length) QueueRadio(Story.Briefings[Wave]);
        var list = new List<EnemyRow>();
        void Add(string id, int n) { for (var i = 0; i < n; i++) list.Add(Sheets.Enemies.First(e => e.Id == id)); }
        Add("wretch", w.Wretch + (Modifier == "Swarm" ? 8 : 0)); Add("drone", w.Drone); Add("boomer", w.Boomer);
        Add("ticker", Modifier == "Double Tickers" ? w.Ticker * 2 + 3 : w.Ticker); Add("reaver", w.Reaver);
        foreach (var e in list.OrderBy(_ => _rng.Next())) _spawnQueue.Enqueue(e);
        QueueWaveExtras(w);
        WaveActive = true;
        PlanWaveIntro();
        PlaceShards();
        _spawnTimer = 0;
        Banner($"WAVE {w.Wave}: {w.Name}" + (Modifier != "none" ? $"\nModifier: {Modifier}" : "") + (Env.Weather != "Clear" ? $"\n{Env.Weather}" : ""));
    }

    /// Somewhere reachable, well away from the player, for a Locust to come up.
    Vector3? SpawnSpot(EnemyRow def)
    {
        if (_navDist == null) UpdateNav(0);
        if (def.FlyHeight <= 0 && _introSpots.Count > 0) return _introSpots.Dequeue(); // where the wave intro showed
        if (Profile.P.SpawnPoints.Count > 0 && _rng.NextDouble() < 0.7)
        {
            var sp = Profile.P.SpawnPoints[_rng.Next(Profile.P.SpawnPoints.Count)];
            var at = new Vector3(sp[0], 0, sp[1]) + new Vector3(((float) _rng.NextDouble() - 0.5f) * 3, 0, ((float) _rng.NextDouble() - 0.5f) * 3);
            if (Dist2(at, P.Pos) > 10 && !Blocked(at, MathF.Max(1, def.Size * 0.3f))) return at;
        }
        for (var tries = 0; tries < 60; tries++)
        {
            var a = (float) _rng.NextDouble() * MathF.Tau;
            var pos = _assets.City != null
                ? ReachableSpot(14) ?? new Vector3(((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 3), 0, ((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 3))
                : new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (ArenaHalf - 2.5f);
            if (Dist2(pos, P.Pos) < 14 || Blocked(pos, MathF.Max(1, def.Size * 0.3f))) continue;
            return pos;
        }
        return null;
    }

    Enemy? Spawn(EnemyRow def) => SpawnSpot(def) is { } pos ? SpawnAt(def, pos) : null;

    Enemy SpawnAt(EnemyRow def, Vector3 pos)
    {
        var hp = def.Hp * _waveHpScale * DifficultyHp;
        if (_rng.NextDouble() < 0.35) Sfx.PlayGearsAt(def.Id, "spawn", pos, Falloff(pos) * 0.8f);
        if (def.FlyHeight > 0) pos = _assets.City != null && def.Special != "boss" ? FromTheVoid() : pos with { Y = def.FlyHeight };
        var e = new Enemy { Def = def, Pos = pos, Hp = hp, MaxHp = hp, AttackCd = 1 + (float) _rng.NextDouble(), Bob = (float) _rng.NextDouble() * 5, SpecialCd = 2 + (float) _rng.NextDouble() * 2 };
        // now and then a tougher Elite comes up (from wave 3)
        if (def.Special != "boss" && def.Title == "none" && Wave >= 2 && _rng.NextDouble() < 0.05) { e.Elite = true; e.MaxHp = e.Hp = hp * 2; OpenRift(pos); }
        ApplyModifierToSpawn(e);
        Enemies.Add(e);
        if (!Profile.P.Codex.Contains(def.Id)) { Profile.P.Codex.Add(def.Id); Say($"Codex: {def.Name}", new Color(170, 200, 255, 255)); }
        if (def.FlyHeight > 0) { Banner(_assets.City != null ? $"A {def.Name} rises out of the Abyss" : $"A {def.Name} circles overhead"); AddShake(0.2f); }
        else if (ClipLength(e, Clip.Spawn) > 0)
        {
            // Gears-style emergence: the ground breaks open and the Locust climbs out
            e.Emerge = 0;
            Bits.Emit(pos + Vector3.UnitY * 0.2f, 34, new Color(118, 104, 90, 200), 3.2f, 0.2f, 1.4f, 3f);
            Bits.Emit(pos + Vector3.UnitY * 0.2f, 12, new Color(60, 52, 46, 255), 5f, 0.1f, 1.1f);
            if (Dist2(pos, P.Pos) < 20) AddShake(0.15f);
            EHoleSound(pos);
            Holes.Add(new EHole { Pos = pos, Radius = 0.9f + def.Size * 0.3f, Life = ClipLength(e, Clip.Spawn) + 4, Seed = _rng.Next() });
        }
        else Effects.Add(new Fx { Kind = FxKind.Pillar, A = pos, Radius = 1, Col = new Color(150, 60, 50, 255), Life = 0.6f });
        return e;
    }

    // ---------------------------------------------------------------- navigation: a flow field toward the player

    const float NavCell = 1.5f;
    int _navN;
    bool[]? _navOpen;
    float[]? _navDist;
    float _navTimer;

    Vector3 CellCentre(int x, int z) => new(-ArenaHalf + (x + 0.5f) * NavCell, 0, -ArenaHalf + (z + 0.5f) * NavCell);
    (int x, int z) CellOf(Vector3 p) => (Math.Clamp((int) ((p.X + ArenaHalf) / NavCell), 0, _navN - 1), Math.Clamp((int) ((p.Z + ArenaHalf) / NavCell), 0, _navN - 1));

    float[]? _navCost;

    void UpdateNav(float dt)
    {
        if (_navOpen == null)
        {
            _navN = (int) MathF.Ceiling(ArenaHalf * 2 / NavCell);
            _navOpen = new bool[_navN * _navN];
            for (var x = 0; x < _navN; x++)
                for (var z = 0; z < _navN; z++)
                    _navOpen[x * _navN + z] = !Blocked(CellCentre(x, z), 0.55f);
            // the Locust prefer flat street: piles cost more to cross
            _navCost = new float[_navN * _navN];
            for (var i = 0; i < _navCost.Length; i++) _navCost[i] = MoundTop(CellCentre(i / _navN, i % _navN)) > 0.3f ? 3f : 1f;
            MoveToLargestArea();
            _navTimer = 0;
        }
        if ((_navTimer -= dt) > 0) return;
        _navTimer = 0.4f;
        var dist = _navDist ??= new float[_navN * _navN];
        Array.Fill(dist, float.MaxValue);
        var (px, pz) = CellOf(P.Pos);
        var queue = new PriorityQueue<int, float>();
        // the hero on a roof or a crate stands in a closed cell: the field starts from the nearest open one below
        if (!_navOpen[px * _navN + pz])
        {
            var best = -1; var bestD = float.MaxValue;
            for (var dx = -12; dx <= 12; dx++)
                for (var dz = -12; dz <= 12; dz++)
                {
                    int nx = px + dx, nz = pz + dz;
                    if (nx < 0 || nz < 0 || nx >= _navN || nz >= _navN || !_navOpen[nx * _navN + nz]) continue;
                    var d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = nx * _navN + nz; }
                }
            if (best >= 0) { px = best / _navN; pz = best % _navN; }
        }
        dist[px * _navN + pz] = 0;
        queue.Enqueue(px * _navN + pz, 0);
        while (queue.TryDequeue(out var i, out var d))
        {
            if (d > dist[i]) continue;
            int x = i / _navN, z = i % _navN;
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= _navN || nz >= _navN || !_navOpen[nx * _navN + nz]) continue;
                    if (dx != 0 && dz != 0 && (!_navOpen[(x + dx) * _navN + z] || !_navOpen[x * _navN + z + dz])) continue; // no corner cutting
                    var nd = d + (dx != 0 && dz != 0 ? 1.414f : 1f) * (_navCost is { } cost ? cost[nx * _navN + nz] : 1);
                    if (nd >= dist[nx * _navN + nz]) continue;
                    dist[nx * _navN + nz] = nd;
                    queue.Enqueue(nx * _navN + nz, nd);
                }
        }
    }

    /// Puts the player in the biggest connected open area (the start spot can be a closed courtyard in the city).
    void MoveToLargestArea()
    {
        var comp = new int[_navN * _navN];
        Array.Fill(comp, -1);
        var sizes = new List<int>();
        for (var i = 0; i < comp.Length; i++)
        {
            if (!_navOpen![i] || comp[i] >= 0) continue;
            var id = sizes.Count; var count = 0;
            var stack = new Stack<int>(); stack.Push(i); comp[i] = id;
            while (stack.Count > 0)
            {
                var c = stack.Pop(); count++;
                int x = c / _navN, z = c % _navN;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= _navN || nz >= _navN) continue;
                    var n = nx * _navN + nz;
                    if (_navOpen[n] && comp[n] < 0) { comp[n] = id; stack.Push(n); }
                }
            }
            sizes.Add(count);
        }
        if (sizes.Count == 0) return;
        var biggest = sizes.IndexOf(sizes.Max());
        var (px, pz) = CellOf(P.Pos);
        if (comp[px * _navN + pz] == biggest) return;
        var best = -1; var bestD = float.MaxValue;
        for (var i = 0; i < comp.Length; i++)
        {
            if (comp[i] != biggest) continue;
            var d = Dist2(CellCentre(i / _navN, i % _navN), P.Pos);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best >= 0) { P.Pos = CellCentre(best / _navN, best % _navN); Log.Info($"start moved {bestD:0} m into the main open area ({sizes.Max()} of {comp.Count(c => c >= 0)} open cells)"); }
    }

    /// Which way to walk toward the player from here: straight when nothing is in the way, else down the flow field.
    Vector3 FlowDir(Vector3 from, Vector3 direct)
    {
        if (_navDist == null || HasLineOfSight(from, P.Pos)) return direct;
        var (x, z) = CellOf(from);
        var best = _navDist[x * _navN + z];
        Vector3? to = null;
        for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= _navN || nz >= _navN) continue;
                var d = _navDist[nx * _navN + nz];
                if (d < best) { best = d; to = CellCentre(nx, nz); }
            }
        return to is { } t ? Flat(t - from) : direct;
    }

    public bool PlayerSees(Enemy e) => HasLineOfSight(P.Pos, e.Pos);

    /// The top of the tallest building piece at x/z (0 when the street is open there).
    float TopAhead(Vector3 at)
    {
        var top = 0f;
        foreach (var b in Cover)
            if (b.Max.Y > top && at.X > b.Min.X && at.X < b.Max.X && at.Z > b.Min.Z && at.Z < b.Max.Z) top = b.Max.Y;
        return top;
    }

    /// Which way the hero walks to reach `target` around the buildings: the flow field runs from every open cell
    /// to the hero, so follow it down from the target and take the step that arrives next to the hero.
    public Vector3 HeroPathDir(Vector3 target)
    {
        var direct = Flat(target - P.Pos);
        if (_navDist == null || FloorY > 1 || HasLineOfSight(P.Pos, target)) return direct; // up on a roof: just walk off it
        var (x, z) = CellOf(target);
        var (hx, hz) = CellOf(P.Pos);
        Vector3? step = null;
        for (var i = 0; i < 600; i++)
        {
            if (Math.Abs(x - hx) <= 1 && Math.Abs(z - hz) <= 1) break;
            int bx = x, bz = z; var best = _navDist[x * _navN + z];
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= _navN || nz >= _navN) continue;
                    var d = _navDist[nx * _navN + nz];
                    if (d < best) { best = d; bx = nx; bz = nz; }
                }
            if (bx == x && bz == z) break; // no way down: walk straight
            x = bx; z = bz;
            step = CellCentre(x, z);
        }
        return step is { } s2 ? Flat(s2 - P.Pos) : direct;
    }

    /// The last few Locust of a long wave: marked on screen so they can be found among the buildings.
    public bool ShowStragglers => WaveActive && Enemies.Count is > 0 and <= 4 && _spawnQueue.Count == 0 && TimeAlive - _waveStartedAt > 50;

    bool Unreachable(Vector3 p)
    {
        if (_navDist == null) return false;
        var (x, z) = CellOf(p);
        return _navDist[x * _navN + z] >= float.MaxValue;
    }

    /// True when no wall or building piece stands between two points (at chest height).
    bool HasLineOfSight(Vector3 a, Vector3 b)
    {
        var from = a + new Vector3(0, 1.2f, 0);
        var to = b + new Vector3(0, 1.2f, 0);
        var len = Vector3.Distance(from, to);
        if (len < 0.01f) return true;
        var ray = new Ray(from, (to - from) / len);
        foreach (var box in Cover)
        {
            var c = Raylib.GetRayCollisionBox(ray, box);
            if (c.Hit && c.Distance < len) return false;
        }
        return true;
    }

    /// A random open spot the player can be reached from, at least `minPath` metres of walking away.
    Vector3? ReachableSpot(float minPath)
    {
        if (_navDist == null) return null;
        for (var tries = 0; tries < 200; tries++)
        {
            int x = _rng.Next(_navN), z = _rng.Next(_navN);
            var d = _navDist[x * _navN + z] * NavCell;
            if (d < minPath || d == float.MaxValue * NavCell || d > 1e6f) continue;
            var p = CellCentre(x, z);
            if (MathF.Abs(p.X) < ArenaHalf - 2 && MathF.Abs(p.Z) < ArenaHalf - 2) return p;
        }
        return null;
    }

    // ---------------------------------------------------------------- helpers

    /// Wrecks, barricades and rubble from the arena sheet's street rows, dropped on open street reachable from the start.
    readonly List<(PropModel prop, Vector3 pos, float yaw)> _street = new();
    /// Rubble, debris and dirt piles: no wall, a mound to walk up and over (see MoundTop).
    public readonly List<BoundingBox> Mounds = new();

    /// The height of the mounds at a point (a dome over each pile's footprint), or float.MinValue off them.
    public float MoundTop(Vector3 p)
    {
        var top = float.MinValue;
        foreach (var b in Mounds)
        {
            var hx = (b.Max.X - b.Min.X) / 2; var hz = (b.Max.Z - b.Min.Z) / 2;
            if (hx < 0.05f || hz < 0.05f) continue;
            var dx = (p.X - (b.Min.X + b.Max.X) / 2) / hx; var dz = (p.Z - (b.Min.Z + b.Max.Z) / 2) / hz;
            var t = 1 - (dx * dx + dz * dz);
            if (t <= 0) continue;
            var h = b.Min.Y + (b.Max.Y - b.Min.Y) * MathF.Sqrt(t) * 0.9f; // rounded, a touch under the pile's peak
            if (h > top) top = h;
        }
        return top;
    }

    /// Burning spots in the streets (flames, embers, smoke) and far plumes over the rooftops.
    readonly List<Vector3> _fires = new(), _plumes = new();

    readonly List<(Vector3 pos, float yaw, float k, float age)> _ghosts = new();
    float _ghostTimer, _dustBob;

    /// Standing on a rooftop or a high ledge (not flying): +15% damage, the vantage of high ground.
    public bool OnHighGround => !P.Flying && FloorY > 4 && Up < 0.2f;
    bool _vantageSaid;

    void UpdateVantage(float dt)
    {
        if (OnHighGround && !_vantageSaid) { _vantageSaid = true; FlightNote = "High ground: +15% damage"; }
        else if (!OnHighGround) _vantageSaid = false;
    }

    /// Burning wrecks: black smoke climbing off their engines (the fire itself is in _fires).
    readonly List<Vector3> _wreckSmoke = new();

    void UpdateWreckSmoke(float dt)
    {
        foreach (var at in _wreckSmoke)
        {
            if (Dist2(at, P.Pos) > 60 || _rng.NextDouble() > dt * 9) continue;
            var c = (byte) (28 + _rng.Next(25));
            Bits.Emit(at + new Vector3(((float) _rng.NextDouble() - 0.5f) * 0.8f, 0, ((float) _rng.NextDouble() - 0.5f) * 0.8f), 1, new Color(c, c, (byte) (c + 4), (byte) 170), 0.6f, 0.5f, 4.5f, -1.6f, push: new Vector3(0.6f, 0, 0.25f), grow: 1.6f);
            if (_rng.NextDouble() < 0.25) Bits.Emit(at, 1, new Color(255, 150, 60, 230), 0.8f, 0.04f, 1.6f, -1.2f, glow: true);
        }
    }

    void PlaceFires()
    {
        UpdateNav(0); // the street props changed the open cells
        for (var i = 0; i < 9; i++) if (ReachableSpot(5) is { } at) _fires.Add(at);
        foreach (var (prop, pos, _) in _street)
            if (prop.Def.Id is "st-police-car" or "st-armadillo" or "st-military-truck" or "st-semi" or "st-bus" && _rng.NextDouble() < 0.6)
            {
                _fires.Add(pos + new Vector3(0, MathF.Min(prop.Size.Y, 1.6f), 0));
                _wreckSmoke.Add(pos + new Vector3(0, MathF.Min(prop.Size.Y, 1.6f) + 0.4f, 0));
            }
        for (var i = 0; i < 6; i++)
        {
            var a = (float) _rng.NextDouble() * MathF.Tau;
            _plumes.Add(new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (70 + (float) _rng.NextDouble() * 60) + new Vector3(0, 20, 0));
        }
    }

    void UpdateAmbience(float dt)
    {
        foreach (var f in _fires)
        {
            if (_rng.NextDouble() < dt * 26) Bits.Emit(f + new Vector3(0, 0.2f, 0), 1, new Color(255, 120 + _rng.Next(80), 30, 255), 1.0f, 0.32f, 0.55f, -3.5f, glow: true);
            if (_rng.NextDouble() < dt * 6) Bits.Emit(f + new Vector3(0, 0.6f, 0), 1, new Color(255, 200, 120, 255), 1.6f, 0.035f, 1.6f, -1.2f, glow: true);
            if (_rng.NextDouble() < dt * 5) Bits.Emit(f + new Vector3(0, 1.2f, 0), 1, new Color(70, 64, 60, 100), 0.6f, 0.5f, 5f, -0.9f, push: new Vector3(0.5f, 0, 0.2f), grow: 0.6f);
        }
        foreach (var p in _plumes)
            if (_rng.NextDouble() < dt * 5) Bits.Emit(p + new Vector3(((float) _rng.NextDouble() - 0.5f) * 4, 0, ((float) _rng.NextDouble() - 0.5f) * 4), 1, new Color(92, 86, 82, 80), 1.0f, 2.6f, 14f, -1.4f, push: new Vector3(1.1f, 0, 0.4f), grow: 1.1f);
        // ash drifting through the air around the hero
        if (_rng.NextDouble() < dt * 20)
        {
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, 4 + (float) _rng.NextDouble() * 8, ((float) _rng.NextDouble() - 0.5f) * 30);
            Bits.Emit(at, 1, new Color(200, 190, 175, 140), 0.25f, 0.03f, 6f, 0.25f, push: new Vector3(0.6f, 0, 0.25f));
        }
    }

    /// Point lights for this frame: spells first (they're brief and bright), then the fires nearest the hero.
    void GatherLights()
    {
        Lit.ClearLights();
        var t = (float) Raylib.GetTime();
        foreach (var f in Effects)
        {
            var fade = 1 - f.Age / MathF.Max(f.Life, 0.01f);
            switch (f.Kind)
            {
                case FxKind.Burst: Lit.AddLight(f.A + Vector3.UnitY * 0.8f, 4 + f.Radius * 2, f.Col, 0.9f * fade); break;
                case FxKind.Pillar: Lit.AddLight(f.A + Vector3.UnitY * 1.5f, 4 + f.Radius * 1.5f, f.Col, 0.6f * fade); break;
                case FxKind.Ring when f.Radius > 2: Lit.AddLight(f.A + Vector3.UnitY * 0.7f, f.Radius * 1.4f, f.Col, 0.6f * fade); break;
                case FxKind.Beam: Lit.AddLight(f.B + Vector3.UnitY * 0.3f, 4, f.Col, 0.8f * fade); break;
            }
        }
        AddLampLights();
        Post.HazeSources.Clear();
        Post.HazeSources.AddRange(_fires);
        foreach (var f in _fires.OrderBy(f => Vector3.DistanceSquared(f, P.Pos)).Take(8))
        {
            var flicker = 0.8f + 0.2f * MathF.Sin(t * 13 + f.X) * MathF.Sin(t * 7.3f + f.Z);
            Lit.AddLight(f + new Vector3(0, 1.2f, 0), 9, new Color(255, 125, 50, 255), 0.85f * flicker);
        }
        Lit.SendLights();
    }

    void DrawFires()
    {
        foreach (var f in _fires)
        {
            var flicker = 0.75f + 0.25f * MathF.Sin((float) Raylib.GetTime() * 13 + f.X) * MathF.Sin((float) Raylib.GetTime() * 7.3f + f.Z);
            Glow.Decal(f, 3.2f * flicker, new Color(255, 120, 40, 90), ring: false);
            Glow.Sprite(f + new Vector3(0, 0.6f, 0), 1.2f * flicker, new Color(255, 140, 50, 120));
        }
    }

    void ScatterStreetProps()
    {
        UpdateNav(0); // open cells and paths from the start
        var placed = 0;
        foreach (var prop in _assets.Props.Where(p => p.Def.Role == "street"))
            for (var n = 0; n < prop.Def.Count; n++)
                for (var tries = 0; tries < 40; tries++)
                {
                    if (ReachableSpot(7) is not { } spot) break;
                    var at = spot with { Y = GroundAt(spot) }; // on the street's real height (slopes, kerbs), not hovering or sunk
                    var yaw = _rng.Next(4) * 90f;
                    var turned = yaw % 180 != 0;
                    var w = turned ? prop.Size.Z : prop.Size.X;
                    var d = turned ? prop.Size.X : prop.Size.Z;
                    var box = new BoundingBox(new Vector3(at.X - w / 2, at.Y, at.Z - d / 2), new Vector3(at.X + w / 2, at.Y + prop.Size.Y, at.Z + d / 2));
                    // keep a lane free around it, and keep away from the start
                    var lane = new BoundingBox(box.Min - new Vector3(1.6f, 0, 1.6f), box.Max + new Vector3(1.6f, 0, 1.6f));
                    if (Cover.Any(c => Overlap(c, lane)) || Mounds.Any(c => Overlap(c, lane)) || Dist2(at, P.Pos) < 6) continue;
                    if (City.IsPile(prop.Def.Mesh)) Mounds.Add(box); else Cover.AddRange(PropBoxes(prop, at, yaw, box));
                    _street.Add((prop, at, yaw));
                    placed++;
                    break;
                }
        _navOpen = null; _navDist = null; // the props block cells now: rebuild the flow field
        Log.Info($"  street dressing: {placed} pieces placed");
    }

    static bool Overlap(BoundingBox a, BoundingBox b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;

    /// The nearest point to `near` (spiralling out) with `clear` metres of room.
    Vector3 FreeSpot(Vector3 near, float clear)
    {
        for (var r = 0f; r < ArenaHalf; r += 1f)
            for (var a = 0; a < 24; a++)
            {
                var p = near + new Vector3(MathF.Cos(a * MathF.Tau / 24), 0, MathF.Sin(a * MathF.Tau / 24)) * r;
                if (MathF.Abs(p.X) < ArenaHalf - 2 && MathF.Abs(p.Z) < ArenaHalf - 2 && !Blocked(p, clear)) return p;
                if (r == 0) break;
            }
        return near;
    }

    void DrawEHole(EHole h)
    {
        h.Age += Raylib.GetFrameTime();
        // opens fast, stays while the Locust climbs out, then closes over the last second
        var open = MathF.Min(1, h.Age / 0.35f) * MathF.Min(1, h.Life);
        if (open <= 0) return;
        var r = h.Radius * open;
        Raylib.DrawCylinder(h.Pos + new Vector3(0, 0.015f, 0), r * 1.35f, r * 1.35f, 0.01f, 20, new Color(58, 48, 40, 255));
        Raylib.DrawCylinder(h.Pos + new Vector3(0, 0.02f, 0), r, r * 0.92f, 0.012f, 20, new Color(12, 8, 6, 255));
        var rng = new Random(h.Seed);
        for (var i = 0; i < 11; i++)
        {
            var a = i / 11f * MathF.Tau + (float) rng.NextDouble() * 0.4f;
            var d = r * (1.05f + (float) rng.NextDouble() * 0.35f);
            var size = new Vector3(0.25f + (float) rng.NextDouble() * 0.35f, 0.12f + (float) rng.NextDouble() * 0.25f, 0.2f + (float) rng.NextDouble() * 0.3f) * open;
            var c = 70 + rng.Next(40);
            Raylib.DrawModelEx(_block, h.Pos + new Vector3(MathF.Cos(a) * d, size.Y * 0.4f, MathF.Sin(a) * d), Vector3.UnitY, a * 57.3f, size, new Color(c, c - 12, c - 22, 255));
        }
    }

    /// Length of a Gears clip on this enemy's character; 0 when it has none.
    float ClipLength(Enemy e, Clip clip) =>
        _assets.Characters.TryGetValue(e.Def.Character, out var c) && c.Has(clip) ? c.Duration[clip] : 0;

    public void Banner(string text) { Banners.Add(text); if (Banners.Count > 1) Banners.RemoveAt(0); BannerTime = 3.2f; }

    void AddNumber(Vector3 at, string text, Color col, float scale) =>
        Numbers.Add(new Number { Pos = at, Text = text, Col = col, Scale = scale, Drift = new Vector2((float) _rng.NextDouble() * 40 - 20, 0) });

    bool Blocked(Vector3 p, float r) => Cover.Any(b => p.X + r > b.Min.X && p.X - r < b.Max.X && p.Z + r > b.Min.Z && p.Z - r < b.Max.Z);

    static bool Inside(BoundingBox b, Vector3 p) => p.X > b.Min.X && p.X < b.Max.X && p.Y > b.Min.Y && p.Y < b.Max.Y && p.Z > b.Min.Z && p.Z < b.Max.Z;

    void MoveCircle(ref Vector3 pos, Vector3 delta, float r)
    {
        pos += delta;
        foreach (var b in Cover)
        {
            if (pos.Y > b.Max.Y - 0.1f) continue; // flying over it
            var cx = Math.Clamp(pos.X, b.Min.X, b.Max.X);
            var cz = Math.Clamp(pos.Z, b.Min.Z, b.Max.Z);
            var dx = pos.X - cx; var dz = pos.Z - cz;
            var d2 = dx * dx + dz * dz;
            if (d2 >= r * r) continue;
            if (d2 < 1e-6f)
            {
                // centre inside the box: push out along the shallowest side
                var pushes = new[] { (b.Min.X - r - pos.X, 0f), (b.Max.X + r - pos.X, 0f), (0f, b.Min.Z - r - pos.Z), (0f, b.Max.Z + r - pos.Z) };
                var best = pushes.OrderBy(p => MathF.Abs(p.Item1) + MathF.Abs(p.Item2)).First();
                pos.X += best.Item1; pos.Z += best.Item2;
                continue;
            }
            var d = MathF.Sqrt(d2);
            pos.X = cx + dx / d * r; pos.Z = cz + dz / d * r;
        }
    }

    float Falloff(Vector3 at) => Math.Clamp(1.1f - Vector3.Distance(at, P.Pos) / 45f, 0.15f, 1f);

    float WalkCycle(string character) => _assets.Characters.TryGetValue(character, out var c) ? c.Duration[Clip.Walk] : 1f;

    static float LerpAngle(float a, float b, float t)
    {
        var d = b - a;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return a + d * t;
    }

    static float Dist2(Vector3 a, Vector3 b) { a.Y = 0; b.Y = 0; return Vector3.Distance(a, b); }

    static Vector3 Flat(Vector3 v) { v.Y = 0; return v.LengthSquared() > 0 ? Vector3.Normalize(v) : Vector3.UnitZ; }

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        p.Y = a.Y = b.Y = 0;
        var ab = b - a;
        var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
        return Vector3.Distance(p, a + ab * t);
    }

    public static Color Hex(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new Color((byte) (v >> 16), (byte) (v >> 8), (byte) v, (byte) 255);
    }

    // ---------------------------------------------------------------- drawing (3D)

    /// True while the sun's shadow pass draws: only the city, props and characters (no floor, effects or HUD shapes).
    public bool ShadowPass;

    /// Sun shadows for the frame: the city, props and characters around the player, seen from the sun.
    public void DrawShadows() => Lit.ShadowPass(P.Pos with { Y = 0 }, light => { ShadowPass = true; try { Draw3D(light); } finally { ShadowPass = false; } });

    public void Draw3D(Camera3D cam)
    {
        Lit.SetEye(cam.Position);
        if (!ShadowPass) Lit.SetEnv();
        Glow.Begin(cam);
        if (!ShadowPass) GatherLights();
        DrawVan();
        DrawStreetProps();
        DrawSquadmate();
        DrawStory();
        // the filler under the real Gears streets (they sit at 0): it only shows through holes in them
        // the city stands on its island in the Abyss; the fort arena on a plain floor
        if (!ShadowPass) { DrawEdgeShimmer(); DrawShards(); DrawWaveIntro(); DrawPocks(); }
        if (!ShadowPass) { if (_assets.City != null && Abyss.Ready) { Abyss.Draw(); Abyss.DrawGlows(); } else Raylib.DrawModel(_floor, new Vector3(0, _assets.City != null ? -0.04f : 0, 0), 1, Color.White); }
        DrawGroundDecals();
        // Gears' merged set pieces are one-sided and built to be seen from outside, so draw both faces.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        _assets.City?.Draw(cam.Position, Vector3.Normalize(cam.Target - cam.Position), far: !ShadowPass);
        foreach (var prop in _assets.Props)
        {
            if (prop.Def.Role == "street") continue;
            var transform = Raymath.MatrixRotateY(prop.Def.Yaw * MathF.PI / 180);
            foreach (var model in prop.Models) { var m = model; m.Transform = transform; Raylib.DrawModel(m, new Vector3(prop.Def.X, 0, prop.Def.Z), 1, Color.White); }
        }
        foreach (var (prop, at, yaw) in _street)
        {
            var transform = Raymath.MatrixRotateY(yaw * MathF.PI / 180);
            for (var k = 0; k < prop.Models.Length; k++)
            {
                Lit.AlphaMode(k < prop.Alpha.Length ? prop.Alpha[k] : Lit.Opaque); // masks (barbed wire) cut out, packed data ignored
                var m = prop.Models[k]; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White);
            }
        }
        Lit.AlphaMode(Lit.Opaque);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        foreach (var b in Cover)
        {
            if (_gearsArena) break;
            var size = b.Max - b.Min;
            var tall = size.Y > 2;
            Raylib.DrawModelEx(_block, (b.Min + b.Max) / 2, Vector3.UnitY, 0, size, tall ? new Color(96, 92, 90, 255) : new Color(122, 108, 92, 255));
        }

        // player: the class's Gears character, carrying the held Gears gun
        var body = Hex(P.Cls.Color);
        var tint = P.HurtFlash > 0 ? new Color(255, 150, 140, 255) : Progression.DyeTint();
        if (CameraClose && !ShadowPass) tint = tint with { A = 70 }; // camera squeezed in by a wall: see through the hero
        if (_assets.Characters.TryGetValue(HeroId, out var hero))
        {
            var moving = P.MoveBob != _lastBob;
            if (!ShadowPass) _lastBob = P.MoveBob;
            if (P.EvadeTime > 0)
            {
                // the Gears combat roll, turned to the dodge direction
                var k = 1 - P.EvadeTime / Sheets.EvasionSeconds;
                var yaw = MathF.Atan2(P.EvadeDir.X, P.EvadeDir.Z);
                if (hero.Has(Clip.Evade)) DrawCharacter(hero, Clip.Evade, MathF.Min(0.999f, k), P.Pos, yaw, 0, tint, HeroScale);
                else DrawCharacter(hero, Clip.Walk, k, P.Pos, yaw, -MathF.Sin(k * MathF.PI) * 0.6f, tint, HeroScale);
            }
            else
            {
                // in the air: no running legs, a forward lean while moving
                var airborne = Up > 0.3f;
                var walk = moving && !airborne;
                if (P.Stealth > 0) tint = new Color(90, 90, 110, 255);
                var (hc, ht, lean) = HeroClip(hero, walk, airborne, moving);
                DrawCharacter(hero, hc, ht, P.Pos, P.Yaw, lean, tint, HeroScale, _heroBlend);
            }
            if (!ShadowPass)
                foreach (var g in _ghosts)
                {
                    // AION evasion afterimages: fading blue copies along the roll
                    var ghost = new Color(140, 185, 255, (int) (95 * Math.Clamp(1 - g.age / 0.28f, 0, 1)));
                    if (hero.Has(Clip.Evade)) DrawCharacter(hero, Clip.Evade, MathF.Min(0.999f, g.k), g.pos, g.yaw, 0, ghost);
                    else DrawCharacter(hero, Clip.Walk, g.k, g.pos, g.yaw, -MathF.Sin(g.k * MathF.PI) * 0.6f, ghost);
                }
        }
        else
        {
            Raylib.DrawModelEx(_cyl, P.Pos + new Vector3(0, 0.05f, 0), Vector3.UnitY, 0, new Vector3(0.28f, 1.25f, 0.28f), body);
            Raylib.DrawModelEx(_sphere, P.Pos + new Vector3(0, 1.58f, 0), Vector3.UnitY, 0, new Vector3(0.19f), body);
        }
        // AION Daeva wings: folded against the back on the ground, spread in flight (and gliding or transformed)
        var spread = MathF.Max(Math.Clamp(Up / 0.8f, 0, 1), Transform > 0 || Gliding ? 1 : 0);
        _wingOpen += (spread - _wingOpen) * MathF.Min(1, Raylib.GetFrameTime() * 6); // they open and fold over a moment
        if (!ShadowPass) DrawWings(_wingOpen, 0.95f);
        else if (_wingPose is { } pose) Wings.Draw(pose.shoulders, pose.fwd, pose.right, pose.spread, pose.flap, pose.col, 1, pose.sway); // their shadow: on the hero's back and the street
        if (Up > 0.1f && !ShadowPass)
            Raylib.DrawCylinder(new Vector3(P.Pos.X, FloorY + 0.03f, P.Pos.Z), 0.55f, 0.55f, 0.01f, 16, new Color(0, 0, 0, (int) (110 * MathF.Max(0.3f, 1 - Up / 8))));
        if (P.Shield > 0 && !ShadowPass)
        {
            var pulse = 0.8f + 0.2f * MathF.Sin((float) Raylib.GetTime() * 4);
            Glow.Sprite(P.Pos + new Vector3(0, 1, 0), 1.5f * pulse, new Color(90, 150, 255, 70));
            Glow.Decal(P.Pos with { Y = 0 }, 1.3f, new Color(120, 180, 255, 140), ring: true);
        }
        if (P.EvadeTime <= 0 && Sheets.GunsEnabled) DrawGun();
        if (P.EvadeTime <= 0 && !Sheets.GunsEnabled && _assets.HandWeapons.TryGetValue(P.Cls.Id, out var handWeapon)) DrawHandWeapon(handWeapon);
        if (!ShadowPass) DrawAion();

        foreach (var e in Enemies.Concat(Dying))
        {
            var dead = e.Dead >= 0;
            var col = e.Flash > 0 ? new Color(255, 200, 190, 255) : e.Stun > 0 ? new Color(150, 170, 255, 255) : Color.White;
            if (_assets.Characters.TryGetValue(e.Def.Character, out var cm))
            {
                var gearsDeath = dead && cm.Has(Clip.Death);
                var emerging = !dead && e.Emerge >= 0 && cm.Has(Clip.Spawn);
                var hitting = !dead && !emerging && e.HitAnim > 0 && e.AttackAnim < 0 && cm.Has(Clip.Hit);
                var clip = gearsDeath ? Clip.Death : emerging ? Clip.Spawn : e.AttackAnim >= 0 && !dead ? Clip.Attack : hitting ? Clip.Hit : e.Moving && !dead ? Clip.Walk : Clip.Idle;
                var t = clip switch
                {
                    Clip.Death => MathF.Min(0.999f, e.Dead / cm.Duration[Clip.Death]),
                    Clip.Spawn => MathF.Min(0.999f, e.Emerge / cm.Duration[Clip.Spawn]),
                    Clip.Attack => MathF.Min(0.999f, e.AttackAnim / 0.7f),
                    Clip.Hit => MathF.Min(0.999f, 1 - e.HitAnim / 0.45f),
                    Clip.Walk => e.WalkPhase,
                    _ => e.Bob / cm.Duration[Clip.Idle],
                };
                // without a Gears death the body tips over; either way it sinks away at the end
                var fall = dead && !gearsDeath ? MathF.Min(1, e.Dead / 0.6f) * 1.5f : 0f;
                var sinkAfter = gearsDeath ? cm.Duration[Clip.Death] + 0.8f : 1.2f;
                var pos = dead ? e.Pos - new Vector3(0, MathF.Max(0, e.Dead - sinkAfter) * 0.8f, 0) : e.Pos;
                if (dead && e.Def.FlyHeight > 0) pos.Y = MathF.Max(-MathF.Max(0, e.Dead - sinkAfter) * 0.8f, e.Pos.Y * (1 - MathF.Min(1, e.Dead * e.Dead * 0.8f)));
                // scale the Gears model to the enemy's height on the sheet
                var scale = e.Def.Size / MathF.Max(0.5f, cm.Height);
                if (!dead && e.Air > 0) pos += new Vector3(0, MathF.Sin(MathF.PI * Math.Clamp(1 - e.Air / e.AirTotal, 0, 1)) * 1.6f, 0);
                if (!dead && e.Down > 0) fall = 1.35f;
                if (!dead && e.Flash > 0 && fall == 0 && !hitting) fall = -0.12f * (e.Flash / 0.12f);
                if (!dead && e.Push.LengthSquared() > 9 && fall == 0) fall = -0.55f;                        // thrown back
                if (!dead && e.Air > 0) fall = -0.9f * MathF.Sin(MathF.PI * Math.Clamp(1 - e.Air / e.AirTotal, 0, 1)); // tumbling in the air
                if (!dead && e.GetUp > 0) fall = 1.35f * e.GetUp / 0.7f;                                      // getting back up
                if (!dead && e.Stun > 0 && e.Down <= 0 && e.Air <= 0 && !ShadowPass)
                    for (var k = 0; k < 3; k++)
                    {
                        // dazed: little stars circling the head
                        var ang = TimeAlive * 4 + k * MathF.Tau / 3;
                        Glow.Sprite(pos + new Vector3(MathF.Cos(ang) * 0.4f, e.Def.Size * scale * 0.98f + 0.2f, MathF.Sin(ang) * 0.4f), 0.09f, new Color(255, 240, 150, 230));
                    }
                if (!dead && e.Burrow > 0) continue;
                if (!dead && e.Root > 0) Glow.Decal(e.Pos, e.Radius + 0.6f, new Color(120, 220, 90, 200), ring: true);
                if (!dead && e.Slow > 0) col = new Color((int) (col.R * 0.7f), (int) (col.G * 0.85f), 255, 255);
                if (dead)
                {
                    var life = MathF.Max(2.2f, ClipLength(e, Clip.Death) + 1.8f);
                    col = col with { A = (byte) (255 * Math.Clamp((life - e.Dead) / 0.9f, 0, 1)) };
                }
                if (e.Def.FlyHeight <= 0 && e.Pos.Y < 0.5f) pos.Y += MathF.Max(GroundAt(e.Pos), MoundTop(e.Pos)); // on the real street surface (and up over piles)
                DrawCharacter(cm, clip, t, pos, e.Yaw, -fall, col, scale, e.Blend);
                if (!dead && !emerging && _assets.HeldGuns.TryGetValue(e.Def.Character, out var hg))
                {
                    var hand = EnemyHand(e, cm, clip, t);
                    var aim = Vector3.Normalize(P.Pos + new Vector3(0, 1.2f, 0) - hand);
                    DrawHeld(hg, hand + aim * hg.Size.X * 0.3f, aim);
                }
            }
            else
            {
                Raylib.DrawModelEx(_cyl, e.Pos, Vector3.UnitY, 0, new Vector3(e.Radius, e.Def.Size * 0.72f, e.Radius), Hex(e.Def.Color));
            }
            if (!dead && e.Dots.Count > 0 && _rng.NextDouble() < 0.25) Bits.Emit(e.Centre, 1, e.Dots[0].Col, 0.8f, 0.09f, 0.7f, -1.5f, glow: true);
        }
        if (ShadowPass) return;
        DrawFires();
        foreach (var h in Holes) DrawEHole(h);
        if (Locked != null)
        {
            // AION-style lock ring under the target
            var spin = (float) Raylib.GetTime() * 90;
            Glow.Decal(Locked.Pos with { Y = MathF.Max(0, Locked.Pos.Y - 0.05f) }, Locked.Radius + 0.8f, new Color(255, 70, 50, 220), ring: true);
            for (var i = 0; i < 3; i++)
            {
                var a = (spin + i * 120) * MathF.PI / 180;
                var r = Locked.Radius + 0.65f;
                var at = Locked.Pos + new Vector3(MathF.Cos(a) * r, 0.08f, MathF.Sin(a) * r);
                Glow.Sprite(at, 0.14f, new Color(255, 110, 70, 255));
            }
        }
        foreach (var s in Shots)
        {
            // Locust fire: hot slugs, Boomer and Reaver shells glowing
            var big = s.Splash > 0;
            if (s.Bile)
            {
                Glow.Sprite(s.Pos, 0.65f, new Color(130, 220, 60, 220));
                Glow.Beam(s.Pos - Vector3.Normalize(s.Vel) * 1.2f, s.Pos, 0.16f, new Color(100, 180, 50, 150));
                continue;
            }
            Glow.Sprite(s.Pos, big ? 0.7f : 0.3f, big ? new Color(255, 140, 50, 230) : new Color(255, 220, 140, 230));
            Glow.Beam(s.Pos - Vector3.Normalize(s.Vel) * (big ? 1.2f : 2.5f), s.Pos, big ? 0.18f : 0.05f, big ? new Color(255, 110, 40, 160) : new Color(255, 200, 120, 150));
        }
        if (P.AuraTime > 0)
        {
            var t = (float) Raylib.GetTime();
            var spirit = P.Pos + new Vector3(MathF.Cos(t * 2) * 0.8f, 2.4f + MathF.Sin(t * 3) * 0.15f, MathF.Sin(t * 2) * 0.8f);
            Glow.Sprite(spirit, 0.55f, new Color(255, 120, 30, 200));
            Glow.Sprite(spirit, 0.2f, new Color(255, 230, 160, 255));
            if (_rng.NextDouble() < 0.5) Bits.Emit(spirit, 1, new Color(255, 140, 40, 255), 0.3f, 0.08f, 0.6f, -1f, glow: true);
        }

        DrawDecals();
        DrawDressing();
        Rlgl.DisableDepthMask();
        DrawTelegraphs();
        DrawGrounds();
        DrawWindStreams();
        DrawLamps();
        foreach (var f in Effects) DrawFx(f);
        Rlgl.EnableDepthMask();
        Glow.Flush();
        Bits.Draw(cam);
        DrawWeather(cam);
    }

    float _lastBob;

    static void DrawCharacter(CharacterModel cm, Clip clip, float t, Vector3 pos, float yaw, float fall, Color tint, float scale = 1, AnimBlend? blend = null)
    {
        var transform = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Raymath.MatrixScale(scale, scale, scale), Raymath.MatrixRotateX(fall)), Raymath.MatrixRotateY(yaw));
        cm.Draw(clip, t, transform, pos, tint, blend);
    }

    readonly AnimBlend _heroBlend = new();

    static Vector3 EnemyHand(Enemy e, CharacterModel cm, Clip clip, float t)
    {
        var h = cm.HandAt(clip, t) * (e.Def.Size / MathF.Max(0.5f, cm.Height));
        var (s, c) = MathF.SinCos(e.Yaw);
        return e.Pos + new Vector3(h.X * c + h.Z * s, h.Y, -h.X * s + h.Z * c);
    }

    static void DrawHeld(HeldGunModel hg, Vector3 at, Vector3 dir)
    {
        var transform = Raymath.MatrixMultiply(Raymath.MatrixRotateZ(MathF.Asin(Math.Clamp(dir.Y, -1, 1))), Raymath.MatrixRotateY(MathF.Atan2(-dir.Z, dir.X)));
        foreach (var model in hg.Models) { var m = model; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White); }
    }

    /// The class's Gears hand weapon: a bow held upright toward the aim; melee weapons carried up and swung down with the attack.
    void DrawHandWeapon(HeldGunModel hand)
    {
        var at = HoldPoint();
        Vector3 dir;
        if (P.Cls.BasicKind == "arrow")
        {
            // the bow is held out in the left hand, upright, toward the aim
            at = P.Pos + new Vector3(0, 0.95f, 0) + Flat(AimDir()) * 0.7f - P.Right * 0.45f;
            dir = Vector3.Normalize(Vector3.UnitY * 1.6f + AimDir() * 0.35f);
        }
        else if (P.ActionAnim >= 0)
        {
            var k = Math.Clamp(P.ActionAnim / 0.4f, 0, 1);
            var a = (-0.35f + k * 1.25f) * MathF.PI; // from raised behind to swept forward and down
            dir = Vector3.Normalize(Vector3.UnitY * MathF.Cos(a) + P.Forward * MathF.Sin(a) * 1.1f + P.Right * 0.25f);
        }
        else dir = Vector3.Normalize(Vector3.UnitY * 1.0f + P.Forward * 0.55f + P.Right * 0.2f);
        DrawHeld(hand, at, dir);
    }

    void DrawGun()
    {
        if (!_assets.Guns.TryGetValue(Gun.Def.Id, out var gm)) return;
        var dir = AimDir();
        var reloadTilt = Gun.Reload > 0 ? MathF.Sin(MathF.Min(1, Gun.Progress) * MathF.PI) * 0.7f : 0f;
        var yaw = MathF.Atan2(-dir.Z, dir.X);
        var pitch = MathF.Asin(Math.Clamp(dir.Y, -1, 1)) - reloadTilt + P.Recoil * 0.06f;
        var transform = Raymath.MatrixMultiply(Raymath.MatrixRotateZ(pitch), Raymath.MatrixRotateY(yaw));
        var at = HoldPoint();
        foreach (var model in gm.Models) { var m = model; m.Transform = transform; Raylib.DrawModel(m, at, 1, Color.White); }
    }

    void DrawFx(Fx f)
    {
        var k = 1 - f.Age / f.Life;
        var c = Fade(f.Col, k);
        switch (f.Kind)
        {
            case FxKind.Tracer when f.Element == "lightning":
            case FxKind.Beam when f.Element == "lightning":
                DrawLightning(f.A, f.B, MathF.Max(0.05f, f.Radius * 1.2f), c);
                DrawLightning(f.A, f.B, 0.03f, Fade(Color.White, k));
                Glow.Sprite(f.B, 0.6f + f.Radius * 4, Fade(f.Col, k * 0.8f));
                break;
            case FxKind.Tracer:
                Glow.Beam(f.A, f.B, 0.06f, c);
                break;
            case FxKind.Beam:
                Glow.Beam(f.A, f.B, MathF.Max(0.08f, f.Radius * 1.6f * k), c);
                if (f.Element is "fire" or "holy" or "arcane" or "ice") Glow.Beam(f.A, f.B, MathF.Max(0.03f, f.Radius * 0.5f * k), Fade(Color.White, k * 0.8f));
                Glow.Sprite(f.B, f.Radius * 9 * k + 0.3f, Fade(f.Col, k * 0.8f));
                break;
            case FxKind.Ring:
            {
                var r = f.Radius * (0.35f + 0.65f * (1 - k));
                Glow.Decal(f.A, r * 1.1f, c, ring: true);
                Glow.Decal(f.A, r * 0.9f, Fade(f.Col, k * 0.35f), ring: false);
                break;
            }
            case FxKind.Pillar:
                Glow.Decal(f.A, f.Radius * 1.3f, c, ring: false);
                Glow.Beam(f.A + new Vector3(0, 0.1f, 0), f.A + new Vector3(0, 1 + 5 * k, 0), f.Radius * 0.45f * k + 0.05f, Fade(f.Col, k * 0.4f));
                break;
            case FxKind.Burst:
                Glow.Sprite(f.A, f.Radius * (1.6f - k * 0.7f), c);
                Glow.Sprite(f.A, f.Radius * 0.6f * k, Fade(Color.White, k));
                break;
            case FxKind.Slash:
            {
                // a glowing arc in front: bright at the edge, fading toward the hero
                var fwd = f.B; var side = new Vector3(-fwd.Z, 0, fwd.X);
                Rlgl.DrawRenderBatchActive();
                Rlgl.DisableBackfaceCulling();
                Raylib.BeginBlendMode(BlendMode.Additive);
                Rlgl.Begin(DrawMode.Triangles);
                for (var i = -6; i < 6; i++)
                {
                    var a0 = i / 6f * 0.95f; var a1 = (i + 1) / 6f * 0.95f;
                    var p0 = f.A + (fwd * MathF.Cos(a0) + side * MathF.Sin(a0)) * f.Radius;
                    var p1 = f.A + (fwd * MathF.Cos(a1) + side * MathF.Sin(a1)) * f.Radius;
                    var q0 = f.A + (p0 - f.A) * 0.55f; var q1 = f.A + (p1 - f.A) * 0.55f;
                    void V(Vector3 v, float a) { Rlgl.Color4ub(f.Col.R, f.Col.G, f.Col.B, (byte) Math.Clamp(255 * k * a, 0, 255)); Rlgl.Vertex3f(v.X, v.Y, v.Z); }
                    V(q0, 0); V(p0, 0.9f); V(p1, 0.9f);
                    V(q0, 0); V(p1, 0.9f); V(q1, 0);
                }
                Rlgl.End();
                Rlgl.DrawRenderBatchActive();
                Raylib.EndBlendMode();
                Rlgl.EnableBackfaceCulling();
                break;
            }
        }
    }

    public static Color Fade(Color c, float a) => new(c.R, c.G, c.B, (byte) Math.Clamp(a * c.A, 0, 255));
}
