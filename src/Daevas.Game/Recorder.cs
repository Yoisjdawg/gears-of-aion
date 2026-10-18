using System;
using System.Diagnostics;
using System.IO;
using Raylib_cs;

namespace Daevas.Game;

/// Dev: records gameplay clips by piping raw frames into ffmpeg (DAEVAS_FFMPEG, or ffmpeg on the PATH).
sealed unsafe class Recorder : IDisposable
{
    readonly Process _ffmpeg;
    readonly Stream _pipe;
    readonly int _w, _h;
    public int Frames { get; private set; }

    public Recorder(string file, int width, int height)
    {
        _w = width; _h = height;
        var exe = Environment.GetEnvironmentVariable("DAEVAS_FFMPEG") ?? "ffmpeg";
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r 30 -i - -vf scale=1280:-2 -c:v libopenh264 -b:v 8M -pix_fmt yuv420p -movflags +faststart \"{file}\"",
        };
        _ffmpeg = Process.Start(psi)!;
        _ffmpeg.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Info("ffmpeg: " + e.Data); };
        _ffmpeg.BeginErrorReadLine();
        _pipe = _ffmpeg.StandardInput.BaseStream;
        Log.Info($"recording {width}x{height} at 30 fps to {file}");
    }

    /// The frame just drawn (call before the buffer swap).
    public void Frame()
    {
        var img = Raylib.LoadImageFromScreen();
        try
        {
            if (img.Width == _w && img.Height == _h) _pipe.Write(new ReadOnlySpan<byte>(img.Data, _w * _h * 4));
            Frames++;
        }
        finally { Raylib.UnloadImage(img); }
    }

    public void Dispose()
    {
        _pipe.Flush(); _pipe.Close();
        _ffmpeg.WaitForExit(60000);
        Log.Info($"recorded {Frames} frames");
    }
}
