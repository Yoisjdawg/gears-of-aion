using System.Collections.Generic;
using System.Linq;

namespace Daevas.Game;

/// Captions for the Gears sounds (Options > Sound captions): who made the sound and what it was.
static class Captions
{
    public static readonly List<(string text, float time)> Lines = new();

    public static void Add(string owner, string when)
    {
        if (!Settings.S.Captions) return;
        string? text = owner switch
        {
            "world" => when switch { "thunder" => "[Thunder]", "explosion" => "[Distant explosion]", "ehole" => "[The ground breaks open]", _ => null },
            "music" or "ambience" or "hero" or "ui" => null,
            _ => Data.Sheets.Enemies.FirstOrDefault(e => e.Id == owner) is { } en ? when switch
            {
                "spawn" => $"[{en.Name} emerges]", "fire" => $"[{en.Name} fires]", "death" => $"[{en.Name} dies]", "shout" => $"[{en.Name}: \"Boom!\"]", "beep" => $"[{en.Name} ticking]", _ => null,
            } : null,
        };
        if (text == null) return;
        Lines.RemoveAll(l => l.text == text);
        Lines.Add((text, 2.5f));
        if (Lines.Count > 4) Lines.RemoveAt(0);
    }

    public static void Update(float dt)
    {
        for (var i = Lines.Count - 1; i >= 0; i--) { var l = Lines[i]; l.time -= dt; if (l.time <= 0) Lines.RemoveAt(i); else Lines[i] = l; }
    }
}
