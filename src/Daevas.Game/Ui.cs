using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The interface's virtual screen: menus and panels are laid out for a window about 900 px high,
/// so on big screens (1440p, 4K, ultrawide) everything 2D is drawn larger by `Scale`.
static class Ui
{
    /// The player's interface size, grown with the screen height above 1000 px.
    public static float Scale => Settings.S.UiScale / 100f * Math.Clamp(Raylib.GetScreenHeight() / 1000f, 1f, 3f);
    public static int W => (int) (Raylib.GetScreenWidth() / Scale);
    public static int H => (int) (Raylib.GetScreenHeight() / Scale);
    public static Vector2 Mouse => Inp.GetMousePosition() / Scale;

    static int _depth;

    /// Draw 2D in virtual coordinates until End.
    public static void Begin()
    {
        if (_depth++ > 0) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.PushMatrix();
        var s = Scale;
        Rlgl.Scalef(s, s, 1);
    }

    public static void End()
    {
        if (_depth == 0 || --_depth > 0) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.PopMatrix();
    }

    /// Scissor rectangles are in real pixels: this takes virtual ones.
    public static void Scissor(float x, float y, float w, float h)
    {
        var s = Scale;
        Raylib.BeginScissorMode((int) (x * s), (int) (y * s), (int) MathF.Ceiling(w * s), (int) MathF.Ceiling(h * s));
    }
}
