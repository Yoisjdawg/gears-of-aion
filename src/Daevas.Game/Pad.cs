using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Gamepad layout (Xbox names), Gears-style shooting with an AION console-style skill pad:
///   Left stick move, Right stick aim, A (hold) roadie run, RT fire, X reload,
///   RB next gun, LB previous gun, D-pad Up/Right/Down/Left = skills 1-4,
///   B evade (AION evasion roll), Y flight (AION wings), right stick click = target lock,
///   Start = start the next wave, Back/View = class select.
///   Menus: D-pad or left stick to choose, A to confirm, B to go back.
static class Pad
{
    const int Id = 0;
    const float Dead = 0.18f;
    public static bool Active;            // last input came from the gamepad (the HUD shows pad labels)
    static bool _rtWasDown;
    static Vector2 _menuStick;

    public static bool Disabled;          // background test runs never read the controller
    public static bool Available => !Disabled && Inp.IsGamepadAvailable(Id);

    public static void Poll()
    {
        if (!Available) { Active = false; return; }
        if (Raylib.GetGamepadButtonPressed() != (int) GamepadButton.Unknown || Move.LengthSquared() > 0 || Look.LengthSquared() > 0) Active = true;
        if (Raylib.GetKeyPressed() != 0 || Inp.GetMouseDelta().LengthSquared() > 4 || Inp.IsMouseButtonPressed(MouseButton.Left)) Active = false;
    }

    static float Axis(GamepadAxis a)
    {
        var v = Inp.GetGamepadAxisMovement(Id, a);
        return MathF.Abs(v) < Dead ? 0 : (v - MathF.Sign(v) * Dead) / (1 - Dead);
    }

    public static Vector2 Move => Available ? new Vector2(Axis(GamepadAxis.LeftX), -Axis(GamepadAxis.LeftY)) : Vector2.Zero;
    public static Vector2 Look => Available ? new Vector2(Axis(GamepadAxis.RightX), Axis(GamepadAxis.RightY)) : Vector2.Zero;

    public static bool Pressed(GamepadButton b) => Available && Inp.IsGamepadButtonPressed(Id, b);
    public static bool Down(GamepadButton b) => Available && Inp.IsGamepadButtonDown(Id, b);

    public static bool FireDown => Available && (Inp.GetGamepadAxisMovement(Id, GamepadAxis.RightTrigger) > 0.3f || Down(GamepadButton.RightTrigger2));

    /// RT edge for semi-automatic guns.
    public static bool FirePressed()
    {
        var down = FireDown;
        var pressed = down && !_rtWasDown;
        _rtWasDown = down;
        return pressed;
    }

    public static bool Sprint => Down(Keys.Pad("sprint")) || Down(GamepadButton.LeftThumb);
    public static bool Execute => !AltHeld && Pressed(Keys.Pad("execute"));
    public static bool Reload => Pressed(GamepadButton.RightFaceLeft);
    public static bool Evade => !AltHeld && Pressed(Keys.Pad("evade"));

    /// Controller vibration (both motors).
    public static void Rumble(float strength, float seconds)
    {
        if (Available && Settings.S.Rumble) Raylib.SetGamepadVibration(Id, strength, strength, seconds);
    }
    public static bool Lock => !AltHeld && Pressed(GamepadButton.RightThumb);
    public static bool Ultimate => AltHeld && Pressed(GamepadButton.RightThumb);
    public static bool StigmaKey => AltHeld && Pressed(GamepadButton.RightFaceLeft);
    public static bool NextGun => Pressed(GamepadButton.RightTrigger1);
    public static bool Flight => !AltHeld && Pressed(Keys.Pad("flight"));
    /// LT + Y: class stance; LT + B: command spirits.
    public static bool Stance => AltHeld && Pressed(GamepadButton.RightFaceUp);
    public static bool Command => AltHeld && Pressed(GamepadButton.RightFaceRight);
    public static bool Alt => AltHeld;
    public static bool Dive => AltHeld && Pressed(GamepadButton.RightFaceDown);
    public static bool PrevGun => Pressed(GamepadButton.LeftTrigger1);
    public static bool Start => Pressed(GamepadButton.MiddleRight);
    public static bool Back => Pressed(GamepadButton.MiddleLeft);
    public static bool Confirm => Pressed(GamepadButton.RightFaceDown) || Pressed(GamepadButton.MiddleRight);
    public static bool Cancel => Pressed(GamepadButton.RightFaceRight);

    static readonly GamepadButton[] SkillButtons =
        { GamepadButton.LeftFaceUp, GamepadButton.LeftFaceRight, GamepadButton.LeftFaceDown, GamepadButton.LeftFaceLeft };
    public static readonly string[] SkillLabels = { "D-Up", "D-Right", "D-Down", "D-Left", "LT Up", "LT Right", "LT Down", "LT Left" };
    /// Skills 1-4 on the D-pad; holding LT turns the D-pad into skills 5-8.
    public static bool Skill(int slot) => slot < 4 ? !AltHeld && Pressed(SkillButtons[slot]) : AltHeld && Pressed(SkillButtons[slot - 4]);
    static bool AltHeld => Available && (Down(GamepadButton.LeftTrigger2) || Inp.GetGamepadAxisMovement(Id, GamepadAxis.LeftTrigger) > 0.3f);

    /// Menu navigation: D-pad or a flick of the left stick, as (dx, dy).
    public static (int dx, int dy) MenuNav()
    {
        int dx = 0, dy = 0;
        if (Pressed(GamepadButton.LeftFaceRight)) dx = 1;
        if (Pressed(GamepadButton.LeftFaceLeft)) dx = -1;
        if (Pressed(GamepadButton.LeftFaceDown)) dy = 1;
        if (Pressed(GamepadButton.LeftFaceUp)) dy = -1;
        var s = Available ? new Vector2(Inp.GetGamepadAxisMovement(Id, GamepadAxis.LeftX), Inp.GetGamepadAxisMovement(Id, GamepadAxis.LeftY)) : Vector2.Zero;
        if (MathF.Abs(_menuStick.X) < 0.5f && MathF.Abs(s.X) >= 0.5f) dx = MathF.Sign(s.X) > 0 ? 1 : -1;
        if (MathF.Abs(_menuStick.Y) < 0.5f && MathF.Abs(s.Y) >= 0.5f) dy = MathF.Sign(s.Y) > 0 ? 1 : -1;
        _menuStick = s;
        return (dx, dy);
    }
}
