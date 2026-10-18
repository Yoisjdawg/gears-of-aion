using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Daevas.Game;

/// Translations: drop a lang.json (English text -> translated text) into %LOCALAPPDATA%\GearsOfAion and every
/// on-screen line that matches is shown translated. --dump-strings writes lang-template.json with every line seen.
static class Lang
{
    static Dictionary<string, string> _map = new();
    static readonly HashSet<string> _seen = new();
    static bool _dump;
    static string Dir => Paths.DataDir;

    public static void Load(bool dump)
    {
        _dump = dump;
        try
        {
            var f = Path.Combine(Dir, "lang.json");
            if (File.Exists(f)) _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) ?? new();
            if (_map.Count > 0) Log.Info($"translations: {_map.Count} lines from lang.json");
        }
        catch (Exception e) { Log.Info("lang.json unreadable: " + e.Message); }
    }

    public static string T(string s)
    {
        if (_dump && s.Length > 1 && !char.IsDigit(s[0])) _seen.Add(s);
        return _map.Count > 0 && _map.TryGetValue(s, out var t) ? t : s;
    }

    public static void Save()
    {
        if (!_dump) return;
        try
        {
            var template = new SortedDictionary<string, string>();
            foreach (var s in _seen) template[s] = s;
            File.WriteAllText(Path.Combine(Dir, "lang-template.json"), JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
