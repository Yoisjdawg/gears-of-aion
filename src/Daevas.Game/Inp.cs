using System;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// Every input the game reads goes through here: the keyboard, mouse and pad are sampled once a frame, so a run can
/// be recorded and played back exactly (replays), or driven by a fixed pseudo-random player (--fake-input, for tests).
static class Inp
{
    const int KeyCount = 384;

    sealed class Frame
    {
        public float Dt;
        public readonly byte[] Keys = new byte[KeyCount / 8];
        public byte Mouse;
        public Vector2 MouseDelta, MousePos;
        public float Wheel;
        public bool Pad;
        public uint PadButtons;
        public readonly float[] Axes = new float[6];

        public void CopyTo(Frame o)
        {
            o.Dt = Dt; Keys.CopyTo(o.Keys, 0); o.Mouse = Mouse; o.MouseDelta = MouseDelta; o.MousePos = MousePos; o.Wheel = Wheel;
            o.Pad = Pad; o.PadButtons = PadButtons; Axes.CopyTo(o.Axes, 0);
        }
    }

    static readonly Frame _cur = new(), _prev = new();
    static BinaryWriter? _rec;
    static BinaryReader? _play;
    static Random? _fake;
    static int _fakeFrame;

    public static bool Recording => _rec != null;
    public static bool Playing => _play != null;
    /// Set when a played-back recording runs out of frames.
    public static bool PlaybackEnded;
    /// Playback waits while this is set (the run hasn't started yet).
    public static bool Hold;

    public static void Record(BinaryWriter w) { _rec = w; _play = null; }
    public static void Play(BinaryReader r) { _play = r; _rec = null; PlaybackEnded = false; }
    public static void StopRecording() { _rec = null; }
    public static void StopPlaying() { _play = null; }
    /// --fake-input <seed>: a fixed pseudo-random player instead of the devices (so recording can be tested unattended).
    public static void Fake(int seed) { _fake = new Random(seed); _fakeFrame = 0; }

    /// Once a frame, before anything reads input. During playback the recorded frame time replaces `dt`.
    public static void BeginFrame(ref float dt)
    {
        _cur.CopyTo(_prev);
        if (_play != null && Hold)
        {
            // waiting for the run to start (the district may still be building): no input, nothing used up
            Array.Clear(_cur.Keys); _cur.Mouse = 0; _cur.MouseDelta = default; _cur.Wheel = 0; _cur.PadButtons = 0;
            return;
        }
        if (_play != null)
        {
            try
            {
                var fdt = _play.ReadSingle();
                if (fdt < 0) throw new EndOfStreamException();
                _cur.Dt = fdt;
                var keys = _play.ReadBytes(KeyCount / 8); // (a plain Read can come back short from a gzip stream)
                if (keys.Length < KeyCount / 8) throw new EndOfStreamException();
                keys.CopyTo(_cur.Keys, 0);
                _cur.Mouse = _play.ReadByte();
                _cur.MouseDelta = new Vector2(_play.ReadSingle(), _play.ReadSingle());
                _cur.MousePos = new Vector2(_play.ReadSingle(), _play.ReadSingle());
                _cur.Wheel = _play.ReadSingle();
                _cur.Pad = _play.ReadBoolean();
                _cur.PadButtons = _play.ReadUInt32();
                for (var i = 0; i < 6; i++) _cur.Axes[i] = _play.ReadSingle();
                dt = fdt;
            }
            catch (EndOfStreamException) { _play = null; PlaybackEnded = true; Array.Clear(_cur.Keys); _cur.Mouse = 0; _cur.MouseDelta = default; }
            return;
        }
        _cur.Dt = dt;
        if (_fake != null) FakeFrame();
        else
        {
            Array.Clear(_cur.Keys);
            for (var k = 32; k < KeyCount; k++)
                if (Raylib.IsKeyDown((KeyboardKey) k)) _cur.Keys[k >> 3] |= (byte) (1 << (k & 7));
            _cur.Mouse = 0;
            for (var b = 0; b < 7; b++) if (Raylib.IsMouseButtonDown((MouseButton) b)) _cur.Mouse |= (byte) (1 << b);
            _cur.MouseDelta = Raylib.GetMouseDelta();
            _cur.MousePos = Raylib.GetMousePosition();
            _cur.Wheel = Raylib.GetMouseWheelMove();
            _cur.Pad = Raylib.IsGamepadAvailable(0);
            _cur.PadButtons = 0;
            if (_cur.Pad)
            {
                for (var b = 1; b < 18; b++) if (Raylib.IsGamepadButtonDown(0, (GamepadButton) b)) _cur.PadButtons |= 1u << b;
                for (var a = 0; a < 6; a++) _cur.Axes[a] = Raylib.GetGamepadAxisMovement(0, (GamepadAxis) a);
            }
        }
        if (_rec != null)
        {
            _rec.Write(_cur.Dt);
            _rec.Write(_cur.Keys);
            _rec.Write(_cur.Mouse);
            _rec.Write(_cur.MouseDelta.X); _rec.Write(_cur.MouseDelta.Y);
            _rec.Write(_cur.MousePos.X); _rec.Write(_cur.MousePos.Y);
            _rec.Write(_cur.Wheel);
            _rec.Write(_cur.Pad);
            _rec.Write(_cur.PadButtons);
            foreach (var a in _cur.Axes) _rec.Write(a);
        }
    }

    /// A restless test player: walks in bursts, turns, attacks, uses skills, rolls and flies now and then.
    static void FakeFrame()
    {
        var r = _fake!;
        _fakeFrame++;
        Array.Clear(_cur.Keys);
        void Hold(KeyboardKey k) => _cur.Keys[(int) k >> 3] |= (byte) (1 << ((int) k & 7));
        var phase = _fakeFrame / 90 % 6;
        if (phase is 0 or 1 or 4) Hold(KeyboardKey.W);
        if (phase == 2) Hold(KeyboardKey.A);
        if (phase == 3) Hold(KeyboardKey.D);
        if (phase == 5) Hold(KeyboardKey.S);
        if (_fakeFrame % 37 < 2) Hold(KeyboardKey.One + r.Next(8));
        if (_fakeFrame % 211 == 0) Hold(KeyboardKey.Space);
        if (_fakeFrame % 997 == 0) Hold(KeyboardKey.F);
        if (_fakeFrame % 300 < 2) Hold(KeyboardKey.Tab);
        _cur.Mouse = (byte) (_fakeFrame % 40 < 30 ? 1 : 0);
        _cur.MouseDelta = new Vector2((float) (r.NextDouble() - 0.45) * 14, (float) (r.NextDouble() - 0.5) * 3);
        _cur.MousePos = new Vector2(800, 450);
        _cur.Wheel = 0; _cur.Pad = false; _cur.PadButtons = 0;
    }

    static bool Bit(byte[] b, int k) => k >= 0 && k < KeyCount && (b[k >> 3] & (1 << (k & 7))) != 0;

    public static bool IsKeyDown(KeyboardKey k) => Bit(_cur.Keys, (int) k);
    public static bool IsKeyPressed(KeyboardKey k) => Bit(_cur.Keys, (int) k) && !Bit(_prev.Keys, (int) k);
    public static bool IsMouseButtonDown(MouseButton b) => (_cur.Mouse & (1 << (int) b)) != 0;
    public static bool IsMouseButtonPressed(MouseButton b) => (_cur.Mouse & (1 << (int) b)) != 0 && (_prev.Mouse & (1 << (int) b)) == 0;
    public static Vector2 GetMouseDelta() => _cur.MouseDelta;
    public static Vector2 GetMousePosition() => _cur.MousePos;
    public static float GetMouseWheelMove() => _cur.Wheel;
    public static bool IsGamepadAvailable(int pad) => pad == 0 && _cur.Pad;
    public static bool IsGamepadButtonDown(int pad, GamepadButton b) => pad == 0 && (_cur.PadButtons & (1u << (int) b)) != 0;
    public static bool IsGamepadButtonPressed(int pad, GamepadButton b) => IsGamepadButtonDown(pad, b) && (_prev.PadButtons & (1u << (int) b)) == 0;
    public static float GetGamepadAxisMovement(int pad, GamepadAxis a) => pad == 0 && _cur.Pad ? _cur.Axes[(int) a] : 0;
}
