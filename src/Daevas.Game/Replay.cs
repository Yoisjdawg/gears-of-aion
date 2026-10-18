using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Daevas.Game;

/// Deterministic replays: a run is its seed, the class, mode, map, options and bag it started with, and every frame's
/// input and frame time (Inp). Played back, the same world plays out again; a hash of the end state checks it did.
/// Runs are kept in %LOCALAPPDATA%\GearsOfAion\replays (the last 10).
static class Replay
{
    public sealed class Header
    {
        public int Version { get; set; } = 1;
        public int Seed { get; set; }
        public string Class { get; set; } = "";
        public string Hero { get; set; } = "";
        public string Mode { get; set; } = "";
        public bool Hardcore { get; set; }
        public string Map { get; set; } = "";
        public int JumpWave { get; set; }
        public int JumpLevel { get; set; }
        public string Settings { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Started { get; set; } = "";
    }

    public static string Dir => System.IO.Path.Combine(Paths.DataDir, "replays");

    static FileStream? _file;
    static GZipStream? _zip;
    static BinaryWriter? _writer;
    static BinaryReader? _reader;
    public static Header? Watching;
    public static string? WatchingFile;
    static string? _recordedHash;
    static bool _finished;

    /// The end state a replay must reach.
    public static string Hash(World w) =>
        $"{w.P.Pos.X:F2},{w.P.Pos.Z:F2},{w.P.Hp:F0},{w.Score},{w.Kills},{w.Wave},{w.TimeAlive:F1}";

    public static void StartRecording(Header h)
    {
        StopRecording(null);
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var old in Directory.GetFiles(Dir, "*.dreplay").OrderByDescending(File.GetCreationTimeUtc).Skip(9)) File.Delete(old);
            var path = Path.Combine(Dir, $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{h.Class}.dreplay");
            _file = File.Create(path);
            _zip = new GZipStream(_file, CompressionLevel.Fastest);
            _writer = new BinaryWriter(_zip, Encoding.UTF8);
            _writer.Write(JsonSerializer.Serialize(h));
            Inp.Record(_writer);
            _finished = false;
            Log.Info("recording replay " + path);
        }
        catch (Exception e) { Log.Info("replay not recorded: " + e.Message); StopRecording(null); }
    }

    /// Ends the recording (with the end state when the run finished).
    public static void StopRecording(World? w)
    {
        if (_writer == null) return;
        Inp.StopRecording();
        try
        {
            _writer.Write(-1f);
            _writer.Write(w != null ? Hash(w) : "");
            _writer.Dispose();
        }
        catch (Exception e) { Log.Info("replay not finished: " + e.Message); }
        _writer = null; _zip = null; _file = null;
    }

    public static void RunEnded(World w)
    {
        if (_finished) return;
        _finished = true;
        StopRecording(w);
    }

    /// The newest recorded run, if any.
    public static string? Latest() =>
        Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.dreplay").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;

    /// Opens a replay to watch: the run's options and bag stand in (unsaved) until it ends.
    public static Header? Open(string path)
    {
        try
        {
            // the end-state hash sits after the frames: read it from a second pass
            using (var scan = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8))
            {
                scan.ReadString();
                var frame = 4 + 48 + 1 + 16 + 4 + 1 + 4 + 24;
                while (true)
                {
                    var dt = scan.ReadSingle();
                    if (dt < 0) { _recordedHash = scan.ReadString(); break; }
                    scan.ReadBytes(frame - 4);
                }
            }
            _reader = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8);
            var h = JsonSerializer.Deserialize<Header>(_reader.ReadString())!;
            Watching = h; WatchingFile = path;
            Daevas.Game.Settings.Watch(h.Settings);
            Daevas.Game.Profile.Watch(h.Profile);
            Inp.Play(_reader);
            Log.Info($"watching replay {Path.GetFileName(path)}: {h.Class} in {h.Mode} on {h.Map}, seed {h.Seed}");
            return h;
        }
        catch (Exception e) { Log.Info("replay unreadable: " + e.Message); return null; }
    }

    /// The replay reached its end (or the run did): compare with the recorded end state and give the player's things back.
    public static string Close(World? w)
    {
        if (Watching == null) return "";
        var got = w != null ? Hash(w) : "";
        var verdict = _recordedHash is null or "" ? "the recording has no end state" : got == _recordedHash ? "matches the recorded run" : $"differs from the recorded run ({_recordedHash})";
        Log.Info($"replay finished: {got} - {verdict}");
        Inp.StopPlaying();
        _reader?.Dispose(); _reader = null;
        Watching = null;
        Daevas.Game.Settings.Unwatch();
        Daevas.Game.Profile.Unwatch();
        return verdict;
    }
}
