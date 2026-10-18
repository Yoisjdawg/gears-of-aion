using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// A mark left on the street: a scorch crater, an E-hole scar, blood, a footprint, ash or spent casings.
struct StreetMark
{
    public Vector3 Pos;
    public float Radius, Age, Life, Yaw;
    public Color Col;
}

/// The living city around the fight: marks that stay on the street, cover that breaks, wrecks that burn, fog banks,
/// burning buildings, searchlights, Ravens passing over and COG firefights in the distance.
sealed partial class World
{
    readonly List<StreetMark> _marks = new();

    public void Mark(Vector3 at, float radius, Color col, float life = 90)
    {
        _marks.Add(new StreetMark { Pos = at with { Y = 0 }, Radius = radius, Col = col, Life = life, Yaw = (float) _rng.NextDouble() * MathF.Tau });
        if (_marks.Count > 400) _marks.RemoveAt(0);
    }

    // ---------------------------------------------------------------- cover that breaks

    readonly Dictionary<int, float> _streetHp = new();

    /// Blasts chew through street cover: sandbags, barricades and rubble give way after enough of them.
    void DamageCover(Vector3 at, float radius, float dmg)
    {
        for (var i = _street.Count - 1; i >= 0; i--)
        {
            var (prop, pos, _) = _street[i];
            if (Dist2(pos, at) > radius + MathF.Max(prop.Size.X, prop.Size.Z) / 2) continue;
            if (prop.Def.Id.Contains("armadillo") || prop.Def.Id.Contains("semi") || prop.Def.Id.Contains("truck") || prop.Def.Id.Contains("bus") || prop.Def.Id.Contains("police"))
            {
                // wrecks stay, but a blast tears panels and glass off them: scraps of metal, sparks and grit fly
                if (dmg > 40)
                {
                    Bits.Emit(pos + Vector3.UnitY * 1.2f, 10, new Color(52, 50, 54, 255), 6f, 0.12f, 1.4f, 9f);
                    Bits.Emit(pos + Vector3.UnitY * 1.2f, 6, new Color(255, 220, 150, 255), 7f, 0.03f, 0.5f, 9f, glow: true);
                    Bits.Emit(pos + Vector3.UnitY * 1.0f, 8, new Color(170, 190, 200, 200), 4f, 0.05f, 1.0f, 9f); // glass
                }
                continue;
            }
            var hp = _streetHp.TryGetValue(i, out var h) ? h : 260;
            hp -= dmg;
            Bits.Emit(pos + Vector3.UnitY * 0.6f, 6, new Color(120, 110, 95, 220), 4f, 0.08f, 0.8f, 9f);
            if (hp > 0) { _streetHp[i] = hp; continue; }
            // it breaks: gone from the street, a pile of debris and dust in its place
            var box = Cover.FirstOrDefault(c => Dist2((c.Min + c.Max) / 2, pos) < 0.3f);
            if (!Cover.Remove(box)) Mounds.RemoveAll(c => Dist2((c.Min + c.Max) / 2, pos) < 0.3f);
            _street.RemoveAt(i);
            _streetHp.Remove(i);
            Bits.Emit(pos + Vector3.UnitY * 0.5f, 30, new Color(110, 100, 88, 230), 5f, 0.12f, 1.2f, 9f);
            Bits.Emit(pos + Vector3.UnitY * 0.5f, 10, new Color(140, 130, 115, 150), 1.5f, 0.6f, 1.8f, -0.3f, grow: 0.8f);
            Mark(pos, 1.6f, new Color(40, 36, 32, 150));
            AddNumber(pos + Vector3.UnitY * 2, "Cover destroyed", new Color(220, 200, 170, 255), 0.6f);
            _navOpen = null; _navDist = null;
        }
        for (var i = Sandbags.Count - 1; i >= 0; i--)
        {
            var c = (Sandbags[i].Min + Sandbags[i].Max) / 2;
            if (Dist2(c, at) > radius + 1.3f || _rng.NextDouble() > dmg / 400) continue;
            Cover.Remove(Sandbags[i]); Sandbags.RemoveAt(i);
            Bits.Emit(c, 24, new Color(150, 130, 100, 220), 4f, 0.12f, 1f, 9f);
            _navOpen = null; _navDist = null;
        }
    }

    // ---------------------------------------------------------------- the city around the fight

    float _fogTimer, _searchAngle, _ravenTimer = 25, _gunfireTimer = 4;
    Vector3 _ravenFrom, _ravenTo;
    float _ravenT = -1;
    readonly List<(Vector3 pos, float phase)> _burning = new();

    void PlaceBurningWindows()
    {
        if (_assets.City == null) return;
        foreach (var b in _assets.City.CameraBoxes.Where(b => b.Max.Y - b.Min.Y > 8).OrderBy(_ => _rng.Next()).Take(26))
        {
            var c = (b.Min + b.Max) / 2;
            var face = _rng.Next(4);
            var at = face switch
            {
                0 => new Vector3(b.Min.X - 0.1f, 0, c.Z), 1 => new Vector3(b.Max.X + 0.1f, 0, c.Z),
                2 => new Vector3(c.X, 0, b.Min.Z - 0.1f), _ => new Vector3(c.X, 0, b.Max.Z + 0.1f),
            };
            at.Y = 3 + (float) _rng.NextDouble() * MathF.Min(10, b.Max.Y - 4);
            _burning.Add((at, (float) _rng.NextDouble() * 10));
        }
    }

    void UpdateDressing(float dt)
    {
        for (var i = _marks.Count - 1; i >= 0; i--) { var m = _marks[i]; m.Age += dt; if (m.Age > m.Life) _marks.RemoveAt(i); else _marks[i] = m; }
        // footprints in wet streets and dust
        if (Up < 0.05f && (Env.Wet > 0.4f || Env.Dust > 0.4f) && MathF.Floor(P.MoveBob / MathF.PI) != MathF.Floor(_printBob / MathF.PI))
            Mark(P.Pos + P.Right * (MathF.Floor(P.MoveBob / MathF.PI) % 2 == 0 ? 0.15f : -0.15f), 0.13f, Env.Wet > 0.4f ? new Color(20, 22, 26, 90) : new Color(60, 50, 38, 110), 25);
        _printBob = P.MoveBob;
        // ash settling in a dust storm
        if (Env.Dust > 0.5f && _rng.NextDouble() < dt * 2) Mark(P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, 0, ((float) _rng.NextDouble() - 0.5f) * 30), 1 + (float) _rng.NextDouble() * 2, new Color(70, 62, 52, 50), 120);
        // fog banks rolling low through the street
        if ((Env.Wet > 0.3f || Env.Dust > 0.3f || Env.Night) && (_fogTimer -= dt) <= 0)
        {
            _fogTimer = 0.25f;
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 40, 0.4f, ((float) _rng.NextDouble() - 0.5f) * 40);
            var c = Vector3.Clamp(Env.FogColor, Vector3.Zero, Vector3.One); // lightning can push the fog past white
            Bits.Emit(at, 1, new Color((int) (c.X * 255), (int) (c.Y * 255), (int) (c.Z * 255), 20), 0.3f, 1.8f, 10f, 0, push: new Vector3(1.2f, 0, 0.5f), grow: 0.3f);
        }
        _searchAngle += dt * 0.35f;
        // a King Raven crossing the sky now and then
        if (_ravenT >= 0)
        {
            _ravenT += dt / 14;
            if (_ravenT > 1) _ravenT = -1;
        }
        else if ((_ravenTimer -= dt) <= 0)
        {
            _ravenTimer = 35 + (float) _rng.NextDouble() * 40;
            var a = (float) _rng.NextDouble() * MathF.Tau;
            var side = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            _ravenFrom = P.Pos with { Y = 34 } - side * 140 + new Vector3(-side.Z, 0, side.X) * 20;
            _ravenTo = P.Pos with { Y = 30 } + side * 140;
            _ravenT = 0;
        }
        // COG firefights a few streets over
        if ((_gunfireTimer -= dt) <= 0)
        {
            _gunfireTimer = 0.6f + (float) _rng.NextDouble() * 3;
            var a = (float) _rng.NextDouble() * MathF.Tau;
            var centre = P.Pos with { Y = 0 } + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (70 + (float) _rng.NextDouble() * 40);
            for (var k = 0; k < 4; k++)
            {
                var from = centre + new Vector3(((float) _rng.NextDouble() - 0.5f) * 10, 1.4f, ((float) _rng.NextDouble() - 0.5f) * 10);
                var to = centre + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, 1.2f, ((float) _rng.NextDouble() - 0.5f) * 30);
                Effects.Add(new Fx { Kind = FxKind.Tracer, A = from, B = to, Col = new Color(255, 210, 140, 255), Life = 0.12f });
            }
            Sfx.PlayGearsAt("lancer", "fire", centre, 0.08f);
        }
    }
    float _printBob;

    void DrawDressing()
    {
        // marks on the street (alpha blended, darkening)
        foreach (var m in _marks)
        {
            var fade = 1 - Math.Clamp((m.Age - (m.Life - 5)) / 5, 0, 1);
            Glow.Mark(m.Pos, m.Radius, m.Col with { A = (byte) (m.Col.A * fade) }, m.Yaw);
        }
        // puddles are in the lit shader; burning windows, searchlights, the Raven
        var dusk = Math.Clamp((0.55f - Env.Day) / 0.4f, 0, 1);
        if (dusk > 0)
            foreach (var (pos, phase) in _burning)
            {
                var flick = 0.7f + 0.3f * MathF.Sin(TimeAlive * 9 + phase) * MathF.Sin(TimeAlive * 5.3f + phase * 2);
                Glow.Sprite(pos, 1.1f * flick, new Color(255, 120, 40, (int) (150 * Math.Clamp(dusk, 0, 1))));
                if (_rng.NextDouble() < 0.02) Bits.Emit(pos + Vector3.UnitY, 1, new Color(60, 54, 50, 120), 0.6f, 0.8f, 4f, -1.2f, grow: 0.6f);
            }
        if (dusk > 0.2f)
            for (var i = 0; i < 3; i++)
            {
                var a = i * MathF.Tau / 3 + 0.8f;
                var origin = P.Pos with { Y = 0 } + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 85;
                var sweep = _searchAngle * (i % 2 == 0 ? 1 : -1.3f) + i;
                var dir = Vector3.Normalize(new Vector3(MathF.Cos(sweep) * 0.5f, 1, MathF.Sin(sweep) * 0.5f));
                Glow.Beam(origin, origin + dir * 90, 1.6f, new Color(220, 230, 255, (int) (28 * Math.Clamp(dusk, 0, 1))));
            }
        if (_ravenT >= 0)
        {
            var p = Vector3.Lerp(_ravenFrom, _ravenTo, _ravenT);
            var f = Vector3.Normalize(_ravenTo - _ravenFrom);
            var yaw = MathF.Atan2(f.X, f.Z) * 180 / MathF.PI;
            var dark = new Color(28, 30, 32, 255);
            Raylib.DrawModelEx(_block, p, Vector3.UnitY, yaw, new Vector3(1.6f, 1.6f, 5.2f), dark);
            Raylib.DrawModelEx(_block, p - f * 4.2f + Vector3.UnitY * 0.3f, Vector3.UnitY, yaw, new Vector3(0.4f, 0.5f, 4f), dark);
            Glow.Sprite(p + Vector3.UnitY * 1.1f, 4.2f, new Color(40, 40, 44, 50));                  // rotor blur
            if ((int) (TimeAlive * 2) % 2 == 0) Glow.Sprite(p - f * 6, 0.5f, new Color(255, 60, 50, 230)); // tail light
            Glow.Sprite(p + f * 2.6f - Vector3.UnitY * 0.6f, 0.4f, new Color(240, 240, 255, 200));
            if (Env.Day < 0.4f) Glow.Beam(p + f * 2.6f, p + f * 8 - Vector3.UnitY * 30, 1.4f, new Color(230, 235, 255, 30)); // its spotlight
        }
    }
}
