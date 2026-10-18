using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// AION elements: each skill has one (skills.element). It sets the skill's colours and impact effects, and how
/// Locust take the hit (armour cuts physical damage; each Locust has a weakness and a resistance, enemies sheet).
sealed partial class World
{
    /// The glow colour of an element (physical uses the class colour).
    public Color ElementColor(string element) => element switch
    {
        "fire" => new Color(255, 120, 40, 255),
        "ice" => new Color(150, 210, 255, 255),
        "lightning" => new Color(170, 190, 255, 255),
        "holy" => new Color(255, 225, 130, 255),
        "shadow" => new Color(150, 80, 200, 255),
        "wind" => new Color(130, 240, 200, 255),
        "earth" => new Color(200, 150, 90, 255),
        "spirit" => new Color(120, 255, 170, 255),
        "poison" => new Color(140, 220, 60, 255),
        "arcane" => new Color(200, 120, 255, 255),
        _ => Hex(P.Cls.Color),
    };

    /// Damage after the target's armour, weakness and resistance; `tag` is shown next to the number.
    static float ElementDamage(Enemy e, float dmg, string element, out string tag)
    {
        tag = "";
        if (element == "physical") dmg *= 1 - MathF.Min(85, e.Def.Armour + e.ArmourBonus) / 100f;
        if (element == e.Def.Weak) { dmg *= 1.3f; tag = "WEAK"; }
        else if (element == e.Def.Resist) { dmg *= 0.7f; tag = "RESIST"; }
        return dmg;
    }

    /// What a skill of this element looks like where it lands.
    void ElementImpact(string element, Vector3 at, float scale)
    {
        var col = ElementColor(element);
        switch (element)
        {
            case "fire":
                Bits.Emit(at, (int) (14 * scale), new Color(255, 140, 40, 255), 2.6f, 0.22f, 0.6f, -3f, glow: true);
                Bits.Emit(at, (int) (5 * scale), new Color(70, 62, 58, 120), 1f, 0.35f, 1.6f, -1f, grow: 0.6f);
                break;
            case "ice":
                Bits.Emit(at, (int) (16 * scale), new Color(220, 240, 255, 255), 4.5f, 0.06f, 0.8f, 9.8f, glow: true);
                Bits.Emit(at, (int) (6 * scale), new Color(200, 225, 255, 140), 1.2f, 0.3f, 0.9f, 0.5f, grow: 0.4f);
                Effects.Add(new Fx { Kind = FxKind.Ring, A = at with { Y = 0 }, Radius = 1.2f * scale, Col = new Color(170, 220, 255, 200), Life = 0.6f, Element = element });
                break;
            case "lightning":
                Bits.Emit(at, (int) (12 * scale), new Color(220, 230, 255, 255), 7f, 0.04f, 0.3f, 2f, glow: true);
                for (var i = 0; i < 2; i++)
                {
                    var off = new Vector3((float) _rng.NextDouble() - 0.5f, 0.6f + (float) _rng.NextDouble(), (float) _rng.NextDouble() - 0.5f) * 2.4f * scale;
                    Effects.Add(new Fx { Kind = FxKind.Beam, A = at + off, B = at, Col = col, Life = 0.15f, Radius = 0.04f, Element = element });
                }
                break;
            case "holy":
                Bits.Emit(at, (int) (12 * scale), new Color(255, 235, 160, 255), 1.6f, 0.08f, 1.0f, -2.5f, glow: true);
                Effects.Add(new Fx { Kind = FxKind.Pillar, A = at with { Y = 0 }, Radius = 0.6f * scale, Col = col, Life = 0.45f, Element = element });
                break;
            case "shadow":
                Bits.Emit(at, (int) (8 * scale), new Color(40, 20, 55, 170), 1.4f, 0.35f, 1.0f, -0.6f, grow: 0.5f);
                Bits.Emit(at, (int) (8 * scale), new Color(170, 90, 230, 255), 2.5f, 0.05f, 0.6f, -1f, glow: true);
                break;
            case "wind":
                for (var i = 0; i < (int) (12 * scale); i++)
                {
                    var a = i / (12f * scale) * MathF.Tau;
                    Bits.Emit(at + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.6f, 1, new Color(190, 255, 230, 200), 0.3f, 0.06f, 0.6f, -2f, glow: true,
                        push: new Vector3(-MathF.Sin(a), 0.3f, MathF.Cos(a)) * 5);
                }
                break;
            case "earth":
                Bits.Emit(at, (int) (12 * scale), new Color(110, 90, 70, 255), 5f, 0.12f, 1.1f, 14f);
                Bits.Emit(at with { Y = 0.2f }, (int) (5 * scale), new Color(130, 115, 95, 130), 1.5f, 0.4f, 1.2f, -0.3f, grow: 0.8f);
                break;
            case "spirit":
                Bits.Emit(at, (int) (10 * scale), col, 1.8f, 0.09f, 0.9f, -2f, glow: true);
                break;
            case "poison":
                Bits.Emit(at, (int) (10 * scale), new Color(140, 220, 60, 255), 1.6f, 0.07f, 0.9f, 6f, glow: true);
                Bits.Emit(at, (int) (4 * scale), new Color(90, 130, 40, 110), 0.8f, 0.3f, 1.2f, -0.5f, grow: 0.5f);
                break;
            case "arcane":
                Bits.Emit(at, (int) (14 * scale), col, 3f, 0.06f, 0.7f, 0, glow: true);
                Effects.Add(new Fx { Kind = FxKind.Burst, A = at, Radius = 0.7f * scale, Col = col, Life = 0.3f, Element = element });
                break;
            default:
                Bits.Emit(at, (int) (8 * scale), new Color(255, 220, 160, 255), 5f, 0.04f, 0.3f, 6f, glow: true);
                break;
        }
    }

    /// The practice range's effect previewer: an element's impact on every training target.
    public void PreviewElement(string element)
    {
        foreach (var e in Enemies) ElementImpact(element, e.Centre, 1.5f);
        ElementSound(element, "blast");
    }

    /// A crackling lightning bolt from a to b: a fresh jagged path each frame.
    void DrawLightning(Vector3 a, Vector3 b, float width, Color col)
    {
        var len = Vector3.Distance(a, b);
        var n = Math.Clamp((int) (len / 0.6f), 3, 24);
        var dir = Vector3.Normalize(b - a);
        var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(side, dir);
        var prev = a;
        for (var i = 1; i <= n; i++)
        {
            var t = i / (float) n;
            var p = Vector3.Lerp(a, b, t);
            if (i < n)
            {
                var j = MathF.Min(len * 0.08f, 0.5f);
                p += side * ((float) _rng.NextDouble() - 0.5f) * 2 * j + up * ((float) _rng.NextDouble() - 0.5f) * 2 * j;
            }
            Glow.Beam(prev, p, width, col);
            prev = p;
        }
    }
}
