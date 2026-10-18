using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Weather in the streets: rain and razorhail falling around the camera, dust blowing through, lightning that
/// strikes the street in storms, explosions on the distant skyline, and street lamps at night.
sealed partial class World
{
    readonly Vector3[] _drops = new Vector3[700];
    bool _dropsReady;
    float _nextBoom = 8;
    Vector3 _rainAround;

    void UpdateWeather(float dt)
    {
        if (Env.Update(dt)) LightningStrike();
        // explosions far off on the skyline
        if ((_nextBoom -= dt) <= 0)
        {
            _nextBoom = 7 + (float) _rng.NextDouble() * 12;
            var a = (float) _rng.NextDouble() * MathF.Tau;
            var at = P.Pos with { Y = 0 } + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (90 + (float) _rng.NextDouble() * 50) + new Vector3(0, 6 + (float) _rng.NextDouble() * 20, 0);
            Effects.Add(new Fx { Kind = FxKind.Burst, A = at, Radius = 7, Col = new Color(255, 150, 60, 255), Life = 0.9f });
            Bits.Emit(at, 3, new Color(70, 64, 60, 150), 2f, 4f, 12f, -1.2f, grow: 1.5f);
            if (!Sfx.PlayGearsAt("world", "explosion", at, 0.5f)) Sfx.PlayQuiet(Sfx.Kill, 0.25f, 0.55f);
        }
        // the city is burning: ash drifting down through the streets, embers rising off the fires
        if (_assets.City != null && _rng.NextDouble() < dt * 14)
        {
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 36, 6 + (float) _rng.NextDouble() * 10, ((float) _rng.NextDouble() - 0.5f) * 36);
            var grey = (byte) (95 + _rng.Next(50));
            Bits.Emit(at, 1, new Color(grey, (byte) (grey - 6), (byte) (grey - 10), (byte) 170), 0.15f, 0.07f, 7f, 0.25f, push: new Vector3(0.8f, 0, 0.3f));
        }
        if (_assets.City != null && _rng.NextDouble() < dt * 7)
        {
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, 0.5f + (float) _rng.NextDouble() * 3, ((float) _rng.NextDouble() - 0.5f) * 30);
            Bits.Emit(at, 1, new Color(255, 120 + _rng.Next(80), 40, 230), 0.4f, 0.045f, 3.5f, -0.5f, push: new Vector3(0.5f, 0, 0.2f), glow: true);
        }
        // dust blowing through the street
        if (Env.Dust > 0.2f && _rng.NextDouble() < dt * 40 * Env.Dust)
        {
            var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.8f) * 40, (float) _rng.NextDouble() * 4, ((float) _rng.NextDouble() - 0.5f) * 40);
            Bits.Emit(at, 1, new Color(150, 120, 85, (int) (70 * Math.Clamp(Env.Dust, 0, 1))), 0.5f, 0.9f, 3f, 0, push: new Vector3(7, 0, 2), grow: 0.8f);
        }
    }

    /// A storm's lightning hits the street somewhere near: a bolt from the sky, and anything there gets hurt.
    void LightningStrike()
    {
        var at = P.Pos + new Vector3(((float) _rng.NextDouble() - 0.5f) * 40, 0, ((float) _rng.NextDouble() - 0.5f) * 40);
        if (Dist2(at, P.Pos) < 5) at += Flat(at - P.Pos + new Vector3(0.1f, 0, 0)) * 6;
        Effects.Add(new Fx { Kind = FxKind.Beam, A = at + new Vector3(((float) _rng.NextDouble() - 0.5f) * 8, 45, 0), B = at, Col = new Color(200, 210, 255, 255), Life = 0.35f, Radius = 0.25f, Element = "lightning" });
        Effects.Add(new Fx { Kind = FxKind.Ring, A = at, Radius = 3, Col = new Color(200, 210, 255, 255), Life = 0.5f });
        Bits.Emit(at + Vector3.UnitY * 0.2f, 30, new Color(220, 230, 255, 255), 6f, 0.05f, 0.4f, 6f, glow: true);
        foreach (var e in Enemies.ToList()) if (Dist2(e.Pos, at) < 3 + e.Radius) { _lastSource = "Lightning"; Damage(e, 80 + 10 * Wave, false); }
        AddShake(Dist2(at, P.Pos) < 15 ? 0.25f : 0.08f);
        if (!Sfx.PlayGearsAt("world", "thunder", at, 1)) Sfx.PlayQuiet(Sfx.Kill, 0.5f, 0.45f);
    }

    /// Rain streaks and razorhail around the camera.
    void DrawWeather(Camera3D cam)
    {
        var rain = Env.Weather is "Rain" or "Thunderstorm" ? 1f : Env.Weather == "Razorhail" ? 0.6f : 0f;
        if (rain <= 0) return;
        var hail = Env.Weather == "Razorhail";
        var centre = cam.Position;
        if (!_dropsReady || Vector3.Distance(centre, _rainAround) > 30)
        {
            for (var i = 0; i < _drops.Length; i++) _drops[i] = centre + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, (float) _rng.NextDouble() * 16 - 3, ((float) _rng.NextDouble() - 0.5f) * 30);
            _dropsReady = true; _rainAround = centre;
        }
        var dt = Raylib.GetFrameTime();
        var fall = new Vector3(hail ? 1.5f : 1.2f, hail ? -24 : -16, 0.6f);
        var n = (int) (_drops.Length * rain);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.Begin(DrawMode.Lines);
        for (var i = 0; i < n; i++)
        {
            ref var d = ref _drops[i];
            d += fall * dt;
            if (d.Y < -0.2f || MathF.Abs(d.X - centre.X) > 15 || MathF.Abs(d.Z - centre.Z) > 15)
            {
                if (d.Y < 0.3f && _rng.NextDouble() < 0.15) Bits.Emit(d with { Y = 0.05f }, 1, new Color(170, 190, 220, 120), 1f, 0.03f, 0.2f, 6f);
                d = centre + new Vector3(((float) _rng.NextDouble() - 0.5f) * 30, 10 + (float) _rng.NextDouble() * 6, ((float) _rng.NextDouble() - 0.5f) * 30);
            }
            var tail = d - Vector3.Normalize(fall) * (hail ? 0.35f : 0.7f);
            var a = (byte) (hail ? 150 : 70);
            Rlgl.Color4ub(hail ? (byte) 220 : (byte) 170, hail ? (byte) 235 : (byte) 185, 255, a);
            Rlgl.Vertex3f(d.X, d.Y, d.Z);
            Rlgl.Color4ub(170, 185, 255, 0);
            Rlgl.Vertex3f(tail.X, tail.Y, tail.Z);
        }
        Rlgl.End();
        Rlgl.DrawRenderBatchActive();
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// At dusk and night the street lamps nearest you come on.
    void AddLampLights()
    {
        if (_assets.City == null || Env.Day > 0.45f) return;
        var k = Math.Clamp((0.45f - Env.Day) / 0.3f, 0, 1);
        var t = (float) Raylib.GetTime();
        foreach (var lamp in _assets.City.Lamps.OrderBy(l => Vector3.DistanceSquared(l, P.Pos)).Take(5))
        {
            // a war-torn grid: every lamp hums a little; about a third are damaged, stutter, cut out and spark
            var id = (int) (lamp.X * 7.1f + lamp.Z * 3.3f);
            var damaged = ((id * 2654435761u) >> 16) % 3 == 0;
            var hum = 0.94f + 0.06f * MathF.Sin(t * 50 + id);
            var level = hum;
            if (damaged)
            {
                var beat = MathF.Sin(t * 2.3f + id) + MathF.Sin(t * 7.9f + id * 0.7f) * 0.6f;
                level = beat > 1.1f ? 0.05f : beat > 0.7f ? 0.4f + 0.3f * MathF.Sin(t * 37) : hum;
                if (beat > 1.1f && _rng.NextDouble() < 0.08)
                    Bits.Emit(lamp, 4, new Color(255, 230, 160, 255), 3f, 0.03f, 0.6f, 9f, glow: true);
            }
            Lit.AddLight(lamp, 12, new Color(255, 200, 130, 255), 0.9f * k * level);
        }
    }

    readonly List<(string id, Vector3 pos, float yaw)> _streetProps = new();

    /// Gears street lamps and traffic lights along the streets (they block like posts).
    void PlaceStreetLights()
    {
        if (_assets.City == null) return;
        _assets.City.Lamps.Clear();
        for (var i = 0; i < 18; i++)
        {
            if (ReachableSpot(4) is not { } at) continue;
            var id = i % 4 == 3 ? "trafficlight" : "streetlamp";
            var yaw = (float) _rng.NextDouble() * MathF.Tau;
            _streetProps.Add((id, at, yaw));
            Cover.Add(new BoundingBox(at - new Vector3(0.2f, 0, 0.2f), at + new Vector3(0.2f, 3, 0.2f)));
            var h = _assets.Characters.TryGetValue(id, out var cm) ? cm.Height : 6;
            if (id == "streetlamp") _assets.City.Lamps.Add(at + new Vector3(0, MathF.Max(3, h - 0.5f), 0));
        }
        _navOpen = null; _navDist = null;
        Log.Info($"night lights: {_assets.City.Lamps.Count} street lamps, {_fires.Count} fires");
    }

    void DrawStreetProps()
    {
        foreach (var (id, pos, yaw) in _streetProps)
            if (_assets.Characters.TryGetValue(id, out var cm)) DrawCharacter(cm, Clip.Idle, 0, pos, yaw, 0, Color.White);
    }

    void DrawLamps()
    {
        if (_assets.City == null || Env.Day > 0.45f) return;
        var k = Math.Clamp((0.45f - Env.Day) / 0.3f, 0, 1);
        foreach (var lamp in _assets.City.Lamps)
            if (Vector3.DistanceSquared(lamp, P.Pos) < 3600) Glow.Sprite(lamp, 0.7f, new Color(255, 210, 140, (int) (200 * Math.Clamp(k, 0, 1))));
    }
}
