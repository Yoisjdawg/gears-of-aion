using System;
using System.Collections.Generic;
using System.Linq;
using Raylib_cs;

namespace Daevas.Game;

/// Rebindable keyboard controls (Options > Key bindings). Bindings live in settings.json.
static class Keys
{
    public static readonly (string id, string label, KeyboardKey key)[] Actions =
    {
        ("forward", "Move forward", KeyboardKey.W), ("back", "Move back", KeyboardKey.S), ("left", "Move left", KeyboardKey.A), ("right", "Move right", KeyboardKey.D),
        ("sprint", "Sprint", KeyboardKey.LeftShift), ("evade", "Evade / vault, climb / air dash", KeyboardKey.Space), ("lock", "Lock target", KeyboardKey.Tab),
        ("flight", "Fly", KeyboardKey.F), ("dive", "Dive (in flight)", KeyboardKey.R), ("ultimate", "Ultimate", KeyboardKey.Q),
        ("skill1", "Skill 1", KeyboardKey.One), ("skill2", "Skill 2", KeyboardKey.Two), ("skill3", "Skill 3", KeyboardKey.Three), ("skill4", "Skill 4", KeyboardKey.Four),
        ("skill5", "Skill 5", KeyboardKey.Five), ("skill6", "Skill 6", KeyboardKey.Six), ("skill7", "Skill 7", KeyboardKey.Seven), ("skill8", "Skill 8", KeyboardKey.Eight),
        ("hp", "Life potion", KeyboardKey.Z), ("mp", "Mana potion", KeyboardKey.X), ("serum", "Wind serum", KeyboardKey.V),
        ("execute", "Execute", KeyboardKey.E), ("stance", "Stance", KeyboardKey.G), ("command", "Command spirits", KeyboardKey.C),
        ("bag", "Bag", KeyboardKey.I), ("sandbags", "Sandbags", KeyboardKey.B), ("stigma", "Stigma", KeyboardKey.K),
        ("log", "Combat log", KeyboardKey.L), ("map", "Map", KeyboardKey.N), ("photo", "Photo mode", KeyboardKey.F10),
    };

    public static KeyboardKey Key(string id) =>
        Settings.S.KeyBinds.TryGetValue(id, out var k) ? (KeyboardKey) k : Actions.First(a => a.id == id).key;

    public static bool Pressed(string id) => Inp.IsKeyPressed(Key(id));
    public static bool Down(string id) => Inp.IsKeyDown(Key(id));

    public static void Bind(string id, KeyboardKey key)
    {
        // a key does one thing: whatever had it before swaps to this action's old key
        var old = Key(id);
        foreach (var a in Actions.Where(a => a.id != id && Key(a.id) == key)) Settings.S.KeyBinds[a.id] = (int) old;
        Settings.S.KeyBinds[id] = (int) key;
        Settings.Save();
    }

    public static string Name(KeyboardKey k) => k switch
    {
        >= KeyboardKey.Zero and <= KeyboardKey.Nine => ((int) k - (int) KeyboardKey.Zero).ToString(),
        KeyboardKey.LeftShift => "Left Shift", KeyboardKey.RightShift => "Right Shift", KeyboardKey.LeftControl => "Left Ctrl",
        KeyboardKey.Space => "Space", KeyboardKey.Tab => "Tab", _ => k.ToString(),
    };

    // ---------------------------------------------------------------- gamepad remapping (face buttons)

    public static readonly (string id, string label, GamepadButton button)[] PadActions =
    {
        ("evade", "Evade", GamepadButton.RightFaceRight), ("flight", "Fly", GamepadButton.RightFaceUp),
        ("sprint", "Sprint", GamepadButton.RightFaceDown), ("execute", "Execute", GamepadButton.RightFaceLeft),
    };
    public static readonly GamepadButton[] FaceButtons = { GamepadButton.RightFaceDown, GamepadButton.RightFaceRight, GamepadButton.RightFaceLeft, GamepadButton.RightFaceUp };
    public static string PadName(GamepadButton b) => b switch
    {
        GamepadButton.RightFaceDown => "A", GamepadButton.RightFaceRight => "B", GamepadButton.RightFaceLeft => "X", GamepadButton.RightFaceUp => "Y", _ => b.ToString(),
    };

    public static GamepadButton Pad(string id) =>
        Settings.S.PadBinds.TryGetValue(id, out var b) ? (GamepadButton) b : PadActions.First(a => a.id == id).button;

    /// Steps an action to the next face button, swapping with whichever action had it.
    public static void CyclePad(string id, int d)
    {
        var cur = Pad(id);
        var next = FaceButtons[(Array.IndexOf(FaceButtons, cur) + d + FaceButtons.Length) % FaceButtons.Length];
        foreach (var a in PadActions.Where(a => a.id != id && Pad(a.id) == next)) Settings.S.PadBinds[a.id] = (int) cur;
        Settings.S.PadBinds[id] = (int) next;
        Settings.Save();
    }
}
