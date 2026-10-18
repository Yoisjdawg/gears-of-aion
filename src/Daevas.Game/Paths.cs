using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Daevas.Game;

/// Where the two games are on this PC. Melty passes both folders at launch (--gears, --aion);
/// a developer run without them looks in the usual store folders.
static class Paths
{
    public static string? Gears, Aion;
    public const string AionSplash = "Aion2/Content/Splash/Splash.bmp";

    /// Everything the game keeps on this PC (profile, settings, caches, replays, logs).
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GearsOfAion");

    /// The game was called Daevas of E-Day: its folder moves to the new name once.
    public static void MoveOldData()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaevasOfEDay");
        try { if (Directory.Exists(old) && !Directory.Exists(DataDir)) Directory.Move(old, DataDir); } catch { }
    }

    public static string LogDir => Path.Combine(DataDir, "logs");

    public static void Resolve(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--gears") Gears = args[i + 1];
            if (args[i] == "--aion") Aion = args[i + 1];
        }
        Gears ??= Environment.GetEnvironmentVariable("DAEVAS_GEARS_DIR") ?? FindGears();
        Aion ??= Environment.GetEnvironmentVariable("DAEVAS_AION_DIR") ?? FindAion();
    }

    static string? FindGears()
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            var p = Path.Combine(d.RootDirectory.FullName, "XboxGames", "Gears of War- E-Day");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    static string? FindAion()
    {
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam == null) return null;
        var libs = new[] { steam }.ToList();
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libs.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        foreach (var lib in libs)
        {
            var p = Path.Combine(lib, "steamapps", "common", "AION2");
            if (File.Exists(Path.Combine(p, AionSplash))) return p;
        }
        return null;
    }
}

static class Log
{
    static StreamWriter? _w;

    public static void Open()
    {
        TryOpen();
        // one line per start, kept across runs (latest.log is replaced each time)
        Launch("args " + string.Join(' ', Environment.GetCommandLineArgs()[1..]));
    }

    static long _retryAt;
    static readonly System.Collections.Generic.List<string> _missed = new();

    /// latest.log, or (when another copy of the game holds it) a log of this run's own. If the folder is busy
    /// (an antivirus scan, say) it tries again every few seconds and writes what it missed.
    static void TryOpen()
    {
        try { Directory.CreateDirectory(Paths.LogDir); } catch { }
        foreach (var name in new[] { "latest.log", $"latest-{Environment.ProcessId}.log" })
        {
            try { _w = new StreamWriter(Path.Combine(Paths.LogDir, name), false) { AutoFlush = true }; break; }
            catch { /* logging is best effort */ }
        }
        _retryAt = Environment.TickCount64 + 3000;
        if (_w != null && _missed.Count > 0) { foreach (var l in _missed) _w.WriteLine(l); _missed.Clear(); }
    }

    /// Memory at a moment of loading: the .NET heap, and the whole process (native buffers, driver copies).
    public static void Memory(string when)
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        Info($"memory {when}: managed {GC.GetTotalMemory(false) / 1048576} MB (heap committed {GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576} MB), process private {p.PrivateMemorySize64 / 1048576} MB, working set {p.WorkingSet64 / 1048576} MB");
    }

    /// One line per start and close, kept across runs (latest.log is replaced each time).
    static readonly System.Collections.Generic.List<string> _missedLaunches = new();

    public static void Launch(string what)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid {Environment.ProcessId} {what}";
        lock (_missedLaunches)
        {
            _missedLaunches.Add(line);
            try { File.AppendAllLines(Path.Combine(Paths.LogDir, "launches.log"), _missedLaunches); _missedLaunches.Clear(); }
            catch { /* best effort: kept for the next try */ }
        }
    }

    /// Recent problems, for the in-game error viewer (F8).
    public static readonly System.Collections.Generic.List<string> Problems = new();

    public static void Info(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        lock (Problems) // loading threads log too
        {
            Console.WriteLine(line);
            if (_w == null)
            {
                if (_missed.Count < 2000) _missed.Add(line);
                if (Environment.TickCount64 > _retryAt) TryOpen();
            }
            else _w.WriteLine(line);
        }
        if (msg.Contains("failed", StringComparison.OrdinalIgnoreCase) || msg.Contains("not read", StringComparison.OrdinalIgnoreCase) || msg.Contains("error", StringComparison.OrdinalIgnoreCase) || msg.Contains("missing", StringComparison.OrdinalIgnoreCase))
            lock (Problems) { Problems.Add(line); if (Problems.Count > 40) Problems.RemoveAt(0); }
    }

    /// Writes a crash report next to the logs (and keeps the game from vanishing without a trace).
    public static void Crash(Exception e)
    {
        try
        {
            var file = Path.Combine(Paths.LogDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, $"Gears of Aion crashed at {DateTime.Now}\n\n{e}\n");
            Info("CRASH: " + e.GetType().Name + ": " + e.Message + "  (report: " + file + ")");
            Launch("crash " + e.GetType().Name);
        }
        catch { }
    }
}

/// Keeps Windows from marking the window "Not Responding" during long main-thread loading steps (uploads):
/// pumps the window's messages if a step has run for a while.
static class Responsive
{
    static long _last;
    /// Only on loading screens: mid-frame in play it would eat key presses.
    public static bool Enabled;

    public static void Tick()
    {
        if (!Enabled) return;
        var now = Environment.TickCount64;
        if (now - _last < 250) return;
        _last = now;
        if (Raylib_cs.Raylib.IsWindowReady()) Raylib_cs.Raylib.PollInputEvents();
    }
}

/// A crash in the last run, shown once on the title screen (with the log folder a key away).
static class LastCrash
{
    public static string? File;

    /// Finds a crash report newer than the last one shown (call once at the start).
    public static void Check()
    {
        try
        {
            var seen = System.IO.Path.Combine(Paths.LogDir, "crash-seen.txt");
            var since = System.IO.File.Exists(seen) ? System.IO.File.GetLastWriteTimeUtc(seen) : DateTime.MinValue;
            var newest = new System.IO.DirectoryInfo(Paths.LogDir).GetFiles("crash-*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest != null && newest.LastWriteTimeUtc > since) File = newest.FullName;
        }
        catch { }
    }

    /// Shown: don't show it again.
    public static void Seen()
    {
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Paths.LogDir, "crash-seen.txt"), DateTime.Now.ToString("u")); } catch { }
        File = null;
    }

    public static void OpenFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Paths.LogDir) { UseShellExecute = true }); } catch { }
    }
}
