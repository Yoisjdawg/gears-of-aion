using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The Abyss in play: flyers rising out of the void, aether rifts that bring elites, aether shards on the rooftops
/// to gather in flight, the Abyss storm modifier, wave intros that show where the Locust come from, and combo finishers.
sealed partial class World
{
    // ---------------------------------------------------------------- flyers out of the void

    /// A flyer comes up out of the void at the district's edge (and climbs to its height on its own).
    Vector3 FromTheVoid()
    {
        var side = _rng.Next(4); var along = ((float) _rng.NextDouble() * 2 - 1) * (ArenaHalf - 4);
        var edge = ArenaHalf - 2;
        var at = side switch { 0 => new Vector3(edge, 0, along), 1 => new Vector3(-edge, 0, along), 2 => new Vector3(along, 0, edge), _ => new Vector3(along, 0, -edge) };
        for (var i = 0; i < 14; i++) Bits.Emit(at + new Vector3(0, -6 + i, 0), 2, new Color(190, 140, 255, 200), 1.5f, 0.12f, 1.4f, -2f, glow: true);
        return at with { Y = -24 };
    }

    // ---------------------------------------------------------------- aether rifts

    /// An elite comes through a tear in the air: a violet rift opens where it stands.
    void OpenRift(Vector3 at)
    {
        Effects.Add(new Fx { Kind = FxKind.Pillar, A = at, Radius = 1.6f, Col = new Color(170, 110, 255, 255), Life = 1.4f });
        Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = 3.5f, Col = new Color(200, 150, 255, 255), Life = 0.9f });
        Bits.Emit(at + Vector3.UnitY * 1.5f, 40, new Color(200, 150, 255, 230), 5f, 0.1f, 1.2f, -0.5f, glow: true);
        Say("An aether rift tears open: an elite comes through", new Color(200, 160, 255, 255));
        AddShake(0.2f);
    }

    // ---------------------------------------------------------------- aether shards

    /// Shards of aether on the rooftops: fly up and gather them (XP and Kinah). New ones each wave.
    readonly List<Vector3> _shards = new();
    public int ShardsGathered;

    void PlaceShards()
    {
        _shards.Clear();
        if (_assets.City == null) return;
        var roofs = _assets.City.Colliders.Where(b => b.Max.Y > 5 && b.Max.Y < 30 && MathF.Abs((b.Min.X + b.Max.X) / 2) < ArenaHalf - 4 && MathF.Abs((b.Min.Z + b.Max.Z) / 2) < ArenaHalf - 4 && (b.Max.X - b.Min.X) * (b.Max.Z - b.Min.Z) > 6).ToList();
        for (var i = 0; i < 5 && roofs.Count > 0; i++)
        {
            var b = roofs[_rng.Next(roofs.Count)];
            _shards.Add(new Vector3((b.Min.X + b.Max.X) / 2, b.Max.Y + 1.0f, (b.Min.Z + b.Max.Z) / 2));
        }
    }

    void UpdateShards()
    {
        for (var i = _shards.Count - 1; i >= 0; i--)
        {
            if (Vector3.DistanceSquared(_shards[i], P.Pos + Vector3.UnitY) > 2.6f * 2.6f) continue;
            var at = _shards[i];
            _shards.RemoveAt(i);
            ShardsGathered++;
            GainXp(25 + 5 * P.Level);
            Profile.P.Kinah += 40;
            Bits.Emit(at, 30, new Color(170, 250, 255, 230), 4f, 0.08f, 1f, -0.5f, glow: true);
            Sfx.Play(Sfx.Cast);
            Say($"Aether shard gathered ({ShardsGathered}): +XP, +40 Kinah", new Color(150, 240, 255, 255));
        }
    }

    void DrawShards()
    {
        var t = (float) Raylib.GetTime();
        foreach (var at in _shards)
        {
            var bob = at + Vector3.UnitY * MathF.Sin(t * 2 + at.X) * 0.25f;
            Abyss.DrawCrystal(bob, 0.9f, t * 0.8f + at.Z);
            Glow.Sprite(bob + Vector3.UnitY * 0.5f, 1.4f, new Color(150, 230, 255, 90));
        }
    }

    // ---------------------------------------------------------------- the Abyss storm

    float _abyssBolt = 6;

    void UpdateAbyssStorm(float dt)
    {
        if (Modifier != "Abyss Storm" || !WaveActive) return;
        if ((_abyssBolt -= dt) > 0) return;
        _abyssBolt = 5 + (float) _rng.NextDouble() * 5;
        LightningStrike();
        Env.Flash = MathF.Max(Env.Flash, 0.6f);
    }

    // ---------------------------------------------------------------- wave intros

    /// Where the first Locust of the wave will come from, marked on the street for a moment.
    readonly Queue<Vector3> _introSpots = new();
    float _introTime;

    void PlanWaveIntro()
    {
        _introSpots.Clear();
        if (_spawnQueue.Count == 0) return;
        var first = _spawnQueue.Peek();
        if (first.FlyHeight > 0) return;
        for (var i = 0; i < 3; i++) if (SpawnSpot(first) is { } at) _introSpots.Enqueue(at);
        if (_introSpots.Count == 0) return;
        _introTime = 4;
        var mean = _introSpots.Aggregate(Vector3.Zero, (a, b) => a + b) / _introSpots.Count - P.Pos;
        var bearing = (MathF.Atan2(mean.X, -mean.Z) * 180 / MathF.PI + 360) % 360;
        var names = new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        Say($"The ground is breaking to the {names[(int) MathF.Round(bearing / 45) % 8]}", new Color(255, 170, 120, 255));
    }

    void DrawWaveIntro()
    {
        if ((_introTime -= Raylib.GetFrameTime()) <= 0) return;
        var pulse = 0.6f + 0.4f * MathF.Sin((float) Raylib.GetTime() * 6);
        foreach (var at in _introSpots)
        {
            Glow.Decal(at, 2.2f, new Color(255, 90, 60, (int) (150 * pulse)), ring: true);
            Glow.Sprite(at + Vector3.UnitY * 0.4f, 0.9f, new Color(255, 120, 70, (int) (110 * pulse)));
        }
    }

    // ---------------------------------------------------------------- combo finishers

    /// Every tenth hit in a combo: a finisher in the class's own style.
    void ComboFinisher()
    {
        var col = Hex(P.Cls.Color);
        var power = 90 + 14 * P.Level;
        switch (P.Cls.Id)
        {
            case "gladiator" or "templar": Shockwave(4.5f, power, "knockback"); break;
            case "assassin" or "ranger": Shockwave(3.5f, power * 1.2f, "stun"); break;
            case "sorcerer" or "spiritmaster": Shockwave(5f, power * 0.9f, "airborne"); break;
            default: Shockwave(4f, power, "knockback"); P.Hp = MathF.Min(P.MaxHp, P.Hp + P.MaxHp * 0.08f); break; // clerics and chanters mend a little
        }
        Effects.Add(new Fx { Kind = FxKind.Ring, A = P.Pos, Radius = 5, Col = col, Life = 0.7f });
        AddNumber(P.Pos + new Vector3(0, 2.4f, 0), $"Finisher x{Combo}", new Color(255, 214, 120, 255), 1.1f);
        AddShake(0.25f);
    }
}
