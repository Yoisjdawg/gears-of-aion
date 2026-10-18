using System.Linq;
using System;
using System.Numerics;
using Daevas.Game.Data;
using Raylib_cs;

namespace Daevas.Game;

/// Gears-style moves: standing on crates, ledges and roofs, vaulting low cover, climbing up a ledge, taking cover,
/// and flying up onto rooftops.
sealed partial class World
{
    /// What the hero stands on: the top of the box under them (a crate, a ledge, a roof), or the street (0).
    public float FloorY;
    /// The hero's height above that.
    public float Up => P.Pos.Y - FloorY;
    /// Standing still against waist-high (or taller) cover: shots from that side do less.
    public bool InCover;
    Vector3 _coverNormal;
    // a vault or a climb in progress
    float _moveT = -1, _moveLen, _moveArc;
    Vector3 _moveFrom, _moveTo;
    bool _vaulting;
    public int Vaults, Climbs;

    const float StepUp = 0.35f;      // how far up the hero steps without climbing
    const float ClimbMax = 2.6f;     // the highest ledge a hero can pull up onto
    const float CoverCut = 0.4f;     // shots from the cover side lose this much

    static bool InXZ(BoundingBox b, Vector3 p, float inset = 0.05f) =>
        p.X > b.Min.X + inset && p.X < b.Max.X - inset && p.Z > b.Min.Z + inset && p.Z < b.Max.Z - inset;

    /// The real street height under a point (the city's own sloped streets, sidewalks and stairs).
    public float GroundAt(Vector3 p) => _assets.City?.GroundAt(p.X, p.Z) ?? 0;

    /// The top of the highest box under `p` that is no more than `stepUp` above it, or the street surface.
    float FloorAt(Vector3 p, float stepUp)
    {
        // the street itself, stepping up kerbs and stairs (a sheer rise is left alone: the hero stays below it)
        var ground = GroundAt(p);
        var top = ground <= p.Y + MathF.Max(stepUp, 0.6f) ? ground : MathF.Min(0, p.Y);
        foreach (var b in Cover)
            if (b.Max.Y > top && b.Max.Y <= p.Y + stepUp && InXZ(b, p)) top = b.Max.Y;
        // piles: walked up as a slope (each frame only rises a little, so any pile can be climbed on foot)
        var mound = MoundTop(p);
        if (mound > top && mound <= p.Y + MathF.Max(stepUp, 0.6f)) top = mound;
        // stairs and ramps: walked up like a slope
        if (_assets.City != null)
            foreach (var r in _assets.City.Ramps)
            {
                var h = r.HeightAt(p);
                if (h > top && h <= p.Y + MathF.Max(stepUp, 0.6f)) top = h;
            }
        return top;
    }

    public int Rolls, LedgeGrabs;

    /// Falling just short of a ledge while pushing toward it: catch the edge and pull up.
    void TryLedgeGrab(Vector2 move)
    {
        if (P.Flying || Up < 0.4f || _moveT >= 0 || move.LengthSquared() < 0.25f || PlayerStun > 0) return;
        var dir = Flat(P.Forward * move.Y + P.Right * move.X);
        var probe = P.Pos + dir * 0.7f;
        foreach (var b in Cover)
        {
            if (b.Max.Y < P.Pos.Y + 0.1f || b.Max.Y > P.Pos.Y + 1.3f || !InXZ(b, probe, 0)) continue;
            var onto = P.Pos + dir * 1.1f;
            if (!InXZ(b, onto, 0.25f)) onto = probe;
            _vaulting = false; _moveFrom = P.Pos; _moveTo = onto with { Y = b.Max.Y };
            _moveLen = 0.4f; _moveArc = 0; _moveT = 0; Climbs++; LedgeGrabs++;
            Sfx.PlayGears("hero", "land", 0.4f);
            return;
        }
    }

    /// Space while pushing into low cover: vault over a thin wall, or climb onto a crate, ledge or roof edge.
    public bool TryVaultOrClimb(Vector2 move)
    {
        if (P.Flying || Up > 0.1f || _moveT >= 0 || PlayerStun > 0 || P.EvadeTime > 0 || Lost || Won) return false;
        var wish = P.Forward * move.Y + P.Right * move.X;
        if (wish.LengthSquared() < 0.25f) return false; // only when pushing toward it
        var dir = Flat(wish);
        var probe = P.Pos + dir * 0.75f;
        BoundingBox? found = null;
        foreach (var b in Cover)
        {
            if (b.Max.Y <= FloorY + StepUp + 0.05f || b.Max.Y > FloorY + ClimbMax || b.Min.Y > P.Pos.Y + 0.5f) continue;
            if (InXZ(b, probe, 0) && (found == null || b.Max.Y > found.Value.Max.Y)) found = b;
        }
        if (found is null)
        {
            // no box: perhaps a ledge in the street itself (a raised plaza)
            var ledge = GroundAt(probe);
            if (ledge > FloorY + StepUp + 0.05f && ledge <= FloorY + ClimbMax)
                found = new BoundingBox(probe with { Y = FloorY } - new Vector3(0.6f, 0, 0.6f), probe with { Y = ledge } + new Vector3(0.6f, 0, 0.6f));
        }
        if (found is not { } box) return false;
        var height = box.Max.Y - FloorY;
        var d = 0.75f;
        while (d < 6 && InXZ(box, P.Pos + dir * d, 0)) d += 0.1f;
        var beyond = P.Pos + dir * (d + 0.55f);
        _vaulting = height <= 1.4f && d - 0.75f < 1.3f && !Blocked(beyond, 0.45f);
        _moveFrom = P.Pos;
        if (_vaulting) _moveTo = beyond with { Y = FloorAt(beyond with { Y = P.Pos.Y }, StepUp) };
        else
        {
            var onto = P.Pos + dir * 1.15f;
            // the far side of something thin: stand in the middle of its top
            if (!InXZ(box, onto, 0.3f)) onto = P.Pos + dir * (0.75f + (d - 0.75f) / 2);
            _moveTo = onto with { Y = box.Max.Y };
        }
        _moveLen = _vaulting ? 0.42f : 0.45f + 0.12f * height;
        _moveArc = _vaulting ? MathF.Max(0.2f, height + 0.25f - (_moveTo.Y - FloorY)) : 0;
        _moveT = 0;
        InCover = false;
        if (_vaulting) Vaults++; else Climbs++;
        if (_assets.Characters.TryGetValue(HeroId, out var hero) && hero.Has(Clip.Jump)) PlayOnce(Clip.Jump);
        Sfx.PlayGears("hero", "land", 0.45f);
        Bits.Emit(P.Pos + Vector3.UnitY * 0.3f, 8, new Color(130, 120, 105, 150), 1.4f, 0.14f, 0.6f, -0.3f, grow: 0.4f);
        return true;
    }

    /// Before the flight and walking code each frame: what's underfoot, a vault or climb in progress, and cover.
    void UpdateMoves(float dt, Vector2 move)
    {
        if (_moveT >= 0)
        {
            _moveT += dt;
            var k = Math.Clamp(_moveT / _moveLen, 0, 1);
            Vector3 at;
            if (_vaulting)
            {
                at = Vector3.Lerp(_moveFrom, _moveTo, k);
                at.Y += _moveArc * MathF.Sin(MathF.PI * k);
            }
            else
            {
                // pull up first, then step forward onto the top
                at = Vector3.Lerp(_moveFrom, _moveTo with { Y = _moveFrom.Y }, Math.Clamp((k - 0.45f) / 0.55f, 0, 1));
                at.Y = _moveFrom.Y + (_moveTo.Y - _moveFrom.Y) * MathF.Sin(MathF.PI / 2 * Math.Clamp(k / 0.6f, 0, 1));
            }
            P.Pos = at;
            if (k >= 1) { _moveT = -1; P.Pos = _moveTo; }
            FloorY = MathF.Min(P.Pos.Y, FloorAt(P.Pos, StepUp));
            InCover = false;
            return;
        }
        FloorY = FloorAt(P.Pos, StepUp);
        TryLedgeGrab(move);

        // cover: standing still right beside a box at least waist high
        InCover = false;
        if (P.Flying || Up > 0.1f || move.LengthSquared() > 0.01f) return;
        foreach (var b in Cover)
        {
            if (b.Max.Y < FloorY + 0.9f || b.Min.Y > P.Pos.Y + 0.3f) continue;
            var cx = Math.Clamp(P.Pos.X, b.Min.X, b.Max.X);
            var cz = Math.Clamp(P.Pos.Z, b.Min.Z, b.Max.Z);
            var away = new Vector3(P.Pos.X - cx, 0, P.Pos.Z - cz);
            var dist = away.Length();
            if (dist < 0.02f || dist > 0.75f) continue;
            InCover = true;
            _coverNormal = away / dist;
            return;
        }
    }

    /// Flying into a building lifts the hero over it, onto the roof (but not past 40 m).
    float FlightClimb(float want)
    {
        var wish = P.Forward * _moveInput.Y + P.Right * _moveInput.X;
        if (wish.LengthSquared() < 0.01f) return want;
        var ahead = FloorAt(P.Pos + Flat(wish) * 1.3f, 8f);
        return MathF.Min(40, MathF.Max(want, ahead + 1.2f));
    }
    Vector2 _moveInput;

    /// How much of a shot travelling along `vel` gets through: less when it comes over the hero's cover.
    float CoverFactor(Vector3 vel)
    {
        if (!InCover || vel.LengthSquared() < 1e-4f) return 1;
        return Vector3.Dot(Flat(vel), _coverNormal) > 0.25f ? 1 - CoverCut : 1;
    }

    bool Climbing => _moveT >= 0;

    /// Dev (--movetest): a low wall and a crate in front of the hero, then vault, climb, walk off and take cover.
    public string MoveSelfTest()
    {
        var notes = new System.Collections.Generic.List<string>();
        var start = P.Pos with { Y = GroundAt(P.Pos) };
        P.Yaw = 0; // facing +Z
        var wall = new BoundingBox(start + new Vector3(-1.5f, 0, 1.0f), start + new Vector3(1.5f, 1.0f, 1.3f));
        var crate = new BoundingBox(start + new Vector3(-1.2f, 0, 4.0f), start + new Vector3(1.2f, 2.0f, 6.4f));
        Cover.Add(wall); Cover.Add(crate);
        void Run(float seconds, Vector2 move) { for (var t = 0f; t < seconds; t += 1 / 60f) { UpdateMoves(1 / 60f, move); UpdateFlight(1 / 60f); if (!Climbing) MoveCircle(ref P.Pos, P.Forward * move.Y * 5 / 60f, 0.45f); } }
        P.Pos = start + new Vector3(0, 0, 0.3f);
        Run(0.1f, Vector2.Zero);
        notes.Add($"against the wall: in cover {InCover}");
        var vaulted = TryVaultOrClimb(new Vector2(0, 1));
        Run(0.6f, Vector2.Zero);
        notes.Add($"vault {vaulted}: z {P.Pos.Z - start.Z:0.00} (past 1.3), y {P.Pos.Y:0.00}");
        P.Pos = start + new Vector3(0, 0, 3.4f);
        Run(0.1f, Vector2.Zero);
        var climbed = TryVaultOrClimb(new Vector2(0, 1));
        Run(0.9f, Vector2.Zero);
        notes.Add($"climb {climbed}: y {P.Pos.Y:0.00} (2.00), floor {FloorY:0.00}");
        Run(1.2f, new Vector2(0, 1));
        Run(1.5f, Vector2.Zero);
        notes.Add($"walked off: z {P.Pos.Z - start.Z:0.00} (past 6.4), y {P.Pos.Y:0.00}");
        // fly into a 9 m building and land on its roof
        var house = new BoundingBox(start + new Vector3(-4f, 0, 10f), start + new Vector3(4f, 9f, 18f));
        Cover.Add(house);
        P.Pos = start + new Vector3(0, 0, 7.5f);
        P.Flying = true; P.FlightTime = Sheets.FlightSeconds;
        _moveInput = new Vector2(0, 1);
        for (var t = 0f; t < 6 && P.Pos.Z - start.Z < 14; t += 0.1f) Run(0.1f, new Vector2(0, 1));
        notes.Add($"flew at the building: y {P.Pos.Y:0.0}, z {P.Pos.Z - start.Z:0.0} (roof 9 m, z 10-18)");
        P.Flying = false; _moveInput = Vector2.Zero;
        Run(2f, Vector2.Zero);
        notes.Add($"landed: y {P.Pos.Y:0.0}, floor {FloorY:0.0}");
        Cover.Remove(house);
        Cover.Remove(wall); Cover.Remove(crate);
        // walk straight over a 1.6 m dirt pile (4 m across)
        var pile = new BoundingBox(start + new Vector3(-2f, 0, 2f), start + new Vector3(2f, 1.6f, 6f));
        Mounds.Add(pile);
        P.Pos = start; var peak = 0f;
        for (var t = 0f; t < 2.4f; t += 0.05f) { Run(0.05f, new Vector2(0, 1)); peak = MathF.Max(peak, P.Pos.Y - start.Y); }
        Run(1f, Vector2.Zero);
        notes.Add($"over a pile: z {P.Pos.Z - start.Z:0.0} (past 6), peak {peak:0.00} m (about 1.4), y {P.Pos.Y - start.Y:0.00}");
        Mounds.Remove(pile);
        // walk up a flight of stairs (2 m over 4 m)
        if (_assets.City != null)
        {
            var stairs = new Ramp(new BoundingBox(start + new Vector3(-1.5f, 0, 1.5f), start + new Vector3(1.5f, 2f, 5.5f)), 1, 1);
            _assets.City.Ramps.Add(stairs);
            P.Pos = start; P.Yaw = 0; var top = 0f;
            for (var t = 0f; t < 1.6f; t += 0.05f) { Run(0.05f, new Vector2(0, 1)); top = MathF.Max(top, P.Pos.Y - start.Y); }
            notes.Add($"up the stairs: reached {top:0.00} m (2.0 at the top)");
            _assets.City.Ramps.Remove(stairs);
        }
        // fall just short of a 3 m ledge while pushing toward it: catch it
        {
            var ledge = new BoundingBox(start + new Vector3(-2, 0, 2), start + new Vector3(2, 3, 6));
            Cover.Add(ledge);
            var grabs = LedgeGrabs;
            P.Pos = start + new Vector3(0, 3.4f, 1.4f); P.Yaw = 0; P.Flying = false;
            for (var t = 0f; t < 1.2f; t += 0.05f) Run(0.05f, new Vector2(0, 1));
            notes.Add($"ledge grab: {(LedgeGrabs > grabs ? "caught" : "missed")}, y {P.Pos.Y - start.Y:0.0} (3.0)");
            Cover.Remove(ledge);
        }
        // climb onto a wreck: its sliced collision lets the low parts be climbed
        if (_assets.Props.FirstOrDefault(p => p.Def.Role == "street" && p.Def.Mesh.Contains("Police", StringComparison.OrdinalIgnoreCase)) is { } car)
        {
            var at = start + new Vector3(0, 0, 4);
            var whole = new BoundingBox(at - new Vector3(car.Size.X / 2, 0, car.Size.Z / 2), at + new Vector3(car.Size.X / 2, car.Size.Y, car.Size.Z / 2));
            var boxes = PropBoxes(car, at, 0, whole);
            Cover.AddRange(boxes);
            P.Pos = new Vector3(start.X + 0.3f, start.Y, boxes.Min(b => b.Min.Z) - 0.5f); // (not on a slice boundary)
            P.Yaw = 0;
            Run(0.1f, Vector2.Zero);
            var up = TryVaultOrClimb(new Vector2(0, 1));
            Run(1.2f, Vector2.Zero);
            notes.Add($"wreck ({boxes.Count} slices, tops {string.Join("/", boxes.Select(b => (b.Max.Y - at.Y).ToString("0.0")))} m): climbed {up}, y {P.Pos.Y - start.Y:0.00}");
            foreach (var b in boxes) Cover.Remove(b);
        }
        P.Pos = start;
        return string.Join("; ", notes);
    }
}
