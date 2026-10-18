using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Daevas.Game;

/// Melty test runs: while %LOCALAPPDATA%\Melty\test\<slug>.json exists, the game picks a class from
/// the run's seed, goes straight into wave 1, plays itself and appends "pass <check>" lines.
sealed class TestMode
{
    public const string Slug = "gears-of-aion"; // was daevas-of-e-day before the rename
    public int Seed { get; private set; }
    public int Run { get; private set; }
    string? _checksFile;
    readonly HashSet<string> _reported = new();

    public static TestMode? Detect()
    {
        if (Environment.GetEnvironmentVariable("DAEVAS_NO_TEST") == "1") return null; // dev runs while Melty is testing
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test");
        var file = Path.Combine(dir, Slug + ".json");
        if (!File.Exists(file)) file = Directory.Exists(dir) ? Directory.GetFiles(dir, "gears-of-aion*.json").Concat(Directory.GetFiles(dir, "daevas*.json")).FirstOrDefault() ?? "" : "";
        var env = Environment.GetEnvironmentVariable("DAEVAS_TEST");
        if (!File.Exists(file) && env == null) return null;
        var t = new TestMode();
        try
        {
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.TryGetProperty("seed", out var s)) t.Seed = s.ValueKind == JsonValueKind.Number ? s.GetInt32() : s.GetString()!.GetHashCode();
                if (r.TryGetProperty("run", out var run) && run.ValueKind == JsonValueKind.Number) t.Run = run.GetInt32();
                if (r.TryGetProperty("checks", out var c)) t._checksFile = c.GetString();
            }
            else t._checksFile = env;
        }
        catch (Exception e) { Log.Info("test file unreadable: " + e.Message); }
        Log.Info($"TEST MODE run={t.Run} seed={t.Seed} checks={t._checksFile}");
        return t;
    }

    public void Pass(string check) => Report($"pass {check}", check);
    public void Fail(string check, string why) => Report($"fail {check}: {why}", check);

    void Report(string line, string check)
    {
        if (!_reported.Add(check)) return;
        Log.Info("TEST " + line);
        if (_checksFile == null) return;
        try
        {
            var d = Path.GetDirectoryName(_checksFile);
            if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
            File.AppendAllText(_checksFile, line + Environment.NewLine);
        }
        catch (Exception e) { Log.Info("could not write checks: " + e.Message); }
    }
}
