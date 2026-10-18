using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The co-branding lockup: AION 2's logo x Gears of War: E-Day's logo x yoisjdawg. Both logos are read from the
/// player's own installs at start (AION 2's is cut out of its splash art; Gears' is its brand texture), never shipped.
static class Brand
{
    public static Texture2D Aion, Gears;

    /// AION 2's logo, cut out of its splash: the middle of the art, keeping the dark lettering and the blue "2"
    /// swoosh and letting the light sky behind them go clear.
    public static unsafe void CutAionLogo(string splashBmp)
    {
        try
        {
            var img = Raylib.LoadImage(splashBmp);
            if (img.Width == 0) return;
            // the logo sits in the middle third (as laid out in AION 2's 600x200 splash)
            var crop = new Rectangle(img.Width * 0.355f, img.Height * 0.11f, img.Width * 0.285f, img.Height * 0.74f);
            Raylib.ImageCrop(ref img, crop);
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
            var px = new Span<Color>(img.Data, img.Width * img.Height);
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float r = c.R, g = c.G, b = c.B;
                var lum = (r + g + b) / 3;
                var blue = b - MathF.Max(r, g);
                // dark letters stay, the saturated blue swoosh stays, the pale sky goes
                var letters = Math.Clamp((150 - lum) / 50, 0, 1);
                var swoosh = Math.Clamp((blue - 25) / 40, 0, 1) * Math.Clamp((b - 110) / 60, 0, 1);
                var a = MathF.Max(letters, swoosh);
                // the letters lifted a touch so they read on dark screens
                if (letters > swoosh) { r = r * 0.6f + 230 * 0.4f; g = g * 0.6f + 228 * 0.4f; b = b * 0.6f + 236 * 0.4f; }
                px[i] = new Color((byte) r, (byte) g, (byte) b, (byte) (255 * a));
            }
            Aion = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(Aion, TextureFilter.Bilinear);
            Raylib.UnloadImage(img);
        }
        catch (Exception e) { Log.Info("AION 2 logo not cut: " + e.Message); }
    }

    /// The lockup centred at `cx`, its logos `height` pixels tall, from `y` down.
    public static void DrawLockup(float cx, float y, float height, float alpha = 1)
    {
        var font = Logo.TitleFont();
        var tint = new Color((byte) 255, (byte) 255, (byte) 255, (byte) (255 * alpha));
        var gold = new Color((byte) 255, (byte) 214, (byte) 120, (byte) (255 * alpha));
        var by = height * 0.45f;
        var aionW = Aion.Id != 0 ? height * 1.35f * Aion.Width / MathF.Max(1, Aion.Height) : 0;
        var gearsW = Gears.Id != 0 ? height * Gears.Width / MathF.Max(1, Gears.Height) : 0;
        const string Name = "yoisjdawg";
        var nameSize = height * 0.42f;
        var nameW = Raylib.MeasureTextEx(font, Name, nameSize, nameSize * 0.05f).X;
        var gap = height * 0.35f; var xw = Raylib.MeasureTextEx(font, "x", by, 0).X;
        var parts = (aionW > 0 ? aionW + gap + xw + gap : 0) + (gearsW > 0 ? gearsW + gap + xw + gap : 0) + nameW;
        var at = cx - parts / 2;
        void Cross() { Raylib.DrawTextEx(font, "x", new Vector2(at + gap, y + height / 2 - by / 2), by, 0, gold); at += gap + xw + gap; }
        if (aionW > 0)
        {
            var h = height * 1.35f; // AION 2's mark is a tall square, drawn a bit larger to balance the wide Gears logo
            Raylib.DrawTexturePro(Aion, new Rectangle(0, 0, Aion.Width, Aion.Height), new Rectangle(at, y + height / 2 - h / 2, aionW, h), Vector2.Zero, 0, tint);
            at += aionW; Cross();
        }
        if (gearsW > 0)
        {
            Raylib.DrawTexturePro(Gears, new Rectangle(0, 0, Gears.Width, Gears.Height), new Rectangle(at, y, gearsW, height), Vector2.Zero, 0, tint);
            at += gearsW; Cross();
        }
        var p = new Vector2(at, y + height / 2 - nameSize / 2);
        Raylib.DrawTextEx(font, Name, p + new Vector2(0, nameSize * 0.05f), nameSize, nameSize * 0.05f, new Color((byte) 0, (byte) 0, (byte) 0, (byte) (200 * alpha)));
        Raylib.DrawTextEx(font, Name, p, nameSize, nameSize * 0.05f, gold);
    }
}
