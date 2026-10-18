using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The time of day and the weather. Each wave sets them (the campaign runs from late afternoon into a stormy
/// night); the sky, the lit shader, the fog and the weather particles all read from here.
static class Env
{
    public static string Weather = "Clear";      // Clear, Overcast, Rain, Razorhail, Dust storm, Thunderstorm
    public static float Hour = 16.5f;             // 0..24
    static float _targetHour = 16.5f;
    public static float Wet, Overcast, Dust, Flash, Time;
    static float _wetTarget, _overcastTarget, _dustTarget, _nextFlash = 6;
    static Random Rng = new(3);

    /// The weather's own dice, from the run's seed (so a replay's lightning falls the same).
    public static void Seed(int seed) => Rng = new Random(seed * 7 + 3);

    /// 0 at night, 1 at midday.
    public static float Day => Math.Clamp(MathF.Sin((Hour - 5) / 15 * MathF.PI) * 1.5f, 0, 1);
    public static bool Night => Day < 0.15f;

    public static Vector3 SunDir
    {
        get
        {
            // the sun (or the moon at night) crosses the sky from east to west, never quite overhead
            var h = Night ? Hour + 12 : Hour;
            var a = Night ? (h - 6) / 12 * MathF.PI : (h - 5) / 15 * MathF.PI;
            var up = MathF.Max(0.18f, MathF.Sin(a) * 0.75f);
            return -Vector3.Normalize(new Vector3(MathF.Cos(a), up, 0.55f));
        }
    }

    public static Vector3 SunColor
    {
        get
        {
            var warm = 1 - Math.Clamp((Day - 0.3f) / 0.6f, 0, 1);    // low sun is warmer
            var c = Vector3.Lerp(new Vector3(1.05f, 0.95f, 0.85f), new Vector3(1.15f, 0.72f, 0.45f), warm) * (0.25f + 0.75f * Day);
            if (Night) c = new Vector3(0.40f, 0.46f, 0.62f) * 0.7f;   // moonlight
            return c * (1 - 0.55f * Overcast - 0.3f * Dust) + new Vector3(Flash * 2.2f);
        }
    }

    public static Vector3 Ambient => Vector3.Lerp(new Vector3(0.30f, 0.32f, 0.42f), new Vector3(0.55f, 0.56f, 0.63f), Day) * (1 - 0.15f * Overcast) + new Vector3(Flash * 0.8f);

    public static Vector3 FogColor
    {
        get
        {
            var c = Vector3.Lerp(new Vector3(0.10f, 0.11f, 0.16f), new Vector3(0.62f, 0.58f, 0.53f), Day);
            c = Vector3.Lerp(c, new Vector3(0.45f, 0.46f, 0.48f) * (0.3f + 0.7f * Day), Overcast * 0.6f);
            c = Vector3.Lerp(c, new Vector3(0.62f, 0.50f, 0.36f) * (0.6f + 0.4f * Day), Dust * 0.8f);
            return c + new Vector3(Flash * 0.5f);
        }
    }

    /// Fog: where it starts, how far until it's at its thickest, and how thick that is.
    public static (float start, float range, float max) Fog
    {
        get
        {
            // each district's own air: Downtown's smoke between towers, the Historic quarter clearer, the docks' haze
            var (near, far, most) = City.Current.Id switch { "historic" => (1.25f, 1.3f, 0.9f), "industrial" => (0.85f, 0.9f, 1.05f), _ => (1f, 1f, 1f) };
            return ((30 - 12 * Dust - 6 * Overcast) * near, (90 - 35 * Dust) * far, MathF.Min(0.95f, (0.7f + 0.15f * Dust) * most));
        }
    }

    /// Each wave's weather: the campaign drifts from late afternoon into night; other modes vary.
    public static void ForWave(GameMode mode, int wave, int seed)
    {
        var rng = new Random(seed * 131 + wave * 17);
        string[] campaign = { "Clear", "Clear", "Overcast", "Dust storm", "Rain", "Razorhail", "Thunderstorm", "Thunderstorm" };
        float[] hours = { 16.5f, 17.2f, 17.8f, 18.4f, 19.0f, 20.5f, 22.5f, 23.5f };
        if (mode is GameMode.Practice or GameMode.Tutorial) { Set("Clear", 15); return; }
        if (mode is GameMode.Campaign or GameMode.TimeAttack or GameMode.Siege)
        {
            var i = Math.Clamp(wave, 0, campaign.Length - 1);
            Set(campaign[i], hours[i]);
            return;
        }
        string[] any = { "Clear", "Overcast", "Rain", "Dust storm", "Razorhail", "Thunderstorm" };
        Set(any[rng.Next(any.Length)], 6 + rng.Next(18) + (float) rng.NextDouble());
    }

    public static void Set(string weather, float hour)
    {
        Weather = weather;
        _targetHour = hour;
        _wetTarget = weather is "Rain" or "Thunderstorm" ? 1 : weather == "Razorhail" ? 0.5f : 0;
        _overcastTarget = weather is "Overcast" or "Rain" or "Thunderstorm" or "Razorhail" ? 1 : weather == "Dust storm" ? 0.4f : 0;
        _dustTarget = weather == "Dust storm" ? 1 : 0;
    }

    /// Eases toward the wave's sky and fires lightning in storms. Returns true on a lightning strike.
    public static bool Update(float dt)
    {
        Time += dt;
        Hour += Math.Clamp(_targetHour - Hour, -dt * 0.4f, dt * 0.4f);
        Wet += Math.Clamp(_wetTarget - Wet, -dt * 0.05f, dt * 0.1f);
        Overcast += Math.Clamp(_overcastTarget - Overcast, -dt * 0.15f, dt * 0.15f);
        Dust += Math.Clamp(_dustTarget - Dust, -dt * 0.15f, dt * 0.15f);
        Flash = MathF.Max(0, Flash - dt * 4);
        if (Weather is "Thunderstorm" or "Razorhail" && (_nextFlash -= dt) <= 0)
        {
            _nextFlash = 4 + (float) Rng.NextDouble() * 9;
            Flash = 0.8f + (float) Rng.NextDouble() * 0.4f;
            return true;
        }
        return false;
    }

    public static void Reset() { Hour = _targetHour = 16.5f; Wet = Overcast = Dust = Flash = 0; Set("Clear", 16.5f); }
}
