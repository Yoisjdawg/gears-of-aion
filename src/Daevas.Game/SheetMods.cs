using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daevas.Game.Data;

namespace Daevas.Game;

/// Sheet mods: JSON files in %LOCALAPPDATA%\GearsOfAion\mods, in the same shape as sheets/*.json
/// ({"sheet": "enemies", "rows": [...]}). A row whose key (its first column: id, level or wave) matches
/// a sheet row changes only the columns it gives; a new key adds a row. The files are read at start and
/// again whenever one changes, so balance can be tuned while the game runs (new spawns and casts use it).
static class SheetMods
{
    public static string Dir => System.IO.Path.Combine(Paths.DataDir, "mods");

    /// What the last load did, for the toast and the problems list (F8).
    public static string Status = "";
    public static int Rows;

    static readonly Dictionary<string, Array> _original = new();
    static string _stamp = "";
    static float _poll;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// The sheet tables by sheet name ("enemies" -> Sheets.Enemies).
    static IEnumerable<FieldInfo> Tables => typeof(Sheets).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => !f.IsInitOnly && f.FieldType.IsArray && f.FieldType.GetElementType()!.Name.EndsWith("Row"));

    static string Norm(string s) => s.Replace("_", "").Replace("-", "").ToLowerInvariant();

    /// Reads the mods folder now (at start, and on a change).
    public static void Load()
    {
        foreach (var f in Tables)
        {
            if (!_original.ContainsKey(f.Name)) _original[f.Name] = (Array) f.GetValue(null)!;
            f.SetValue(null, _original[f.Name]);
        }
        Rows = 0;
        _stamp = Stamp();
        if (!Directory.Exists(Dir)) { Status = ""; return; }
        var notes = new List<string>();
        foreach (var file in Directory.GetFiles(Dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                var sheet = doc["sheet"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(file);
                var table = Tables.FirstOrDefault(f => Norm(f.Name) == Norm(sheet));
                if (table == null) { notes.Add($"{name}: there is no sheet called {sheet}"); continue; }
                if (doc["rows"] is not JsonArray rows) { notes.Add($"{name}: no rows"); continue; }
                var type = table.FieldType.GetElementType()!;
                var list = ((Array) table.GetValue(null)!).Cast<object>().ToList();
                var keyProp = type.GetProperties()[0];
                foreach (var node in rows)
                {
                    if (node is not JsonObject mod) continue;
                    var keyField = mod.FirstOrDefault(kv => Norm(kv.Key) == Norm(keyProp.Name));
                    if (keyField.Value == null) { notes.Add($"{name}: a row without {JsonNamingPolicy.CamelCase.ConvertName(keyProp.Name)}"); continue; }
                    var key = keyField.Value.ToString();
                    var at = list.FindIndex(r => keyProp.GetValue(r)?.ToString() == key);
                    // start from the sheet's row (or the first row, for a new one) and lay the mod's columns over it
                    var merged = JsonSerializer.SerializeToNode(at >= 0 ? list[at] : list[0], type, Json)!.AsObject();
                    foreach (var (k, v) in mod)
                    {
                        var target = merged.Select(p => p.Key).FirstOrDefault(p => Norm(p) == Norm(k));
                        if (target == null) { notes.Add($"{name}: {sheet} has no column {k}"); continue; }
                        merged[target] = v?.DeepClone();
                    }
                    var row = merged.Deserialize(type, Json)!;
                    if (at >= 0) list[at] = row; else list.Add(row);
                    Rows++;
                }
                var arr = Array.CreateInstance(type, list.Count);
                for (var i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
                table.SetValue(null, arr);
            }
            catch (Exception e) { notes.Add($"{name}: {e.Message}"); }
        }
        Status = Rows > 0 || notes.Count > 0 ? $"Sheet mods: {Rows} rows" + (notes.Count > 0 ? $", {notes.Count} problems (F8)" : "") : "";
        foreach (var n in notes) Log.Info("sheet mod error: " + n);
        if (Rows > 0) Log.Info(Status);
    }

    /// Call once a frame: reloads when a mod file was added, changed or removed. True when it did.
    public static bool Poll(float dt)
    {
        if ((_poll -= dt) > 0) return false;
        _poll = 1;
        if (Stamp() == _stamp) return false;
        Load();
        return true;
    }

    static string Stamp()
    {
        try
        {
            if (!Directory.Exists(Dir)) return "";
            return string.Join("|", Directory.GetFiles(Dir, "*.json").Select(f => f + File.GetLastWriteTimeUtc(f).Ticks));
        }
        catch { return _stamp; }
    }
}
