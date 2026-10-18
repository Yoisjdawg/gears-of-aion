using System;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Daevas.Game;

/// The finishing pass over the 3D scene (before the HUD): a soft bloom around bright fires and spells, a Gears-style
/// grade (warm highlights, cool shadows, more contrast) and a vignette. The scene stays on the multisampled screen;
/// it is copied into a mipmapped texture, whose small mips give the bloom for free.
static class Post
{
    [DllImport("opengl32.dll")] static extern void glBindTexture(uint target, uint texture);
    [DllImport("opengl32.dll")] static extern void glCopyTexSubImage2D(uint target, int level, int xoff, int yoff, int x, int y, int width, int height);
    const uint Texture2DTarget = 0x0DE1;

    static Shader _shader;
    static int _resLoc, _sunLoc, _hazeLoc, _hazeCountLoc, _timeLoc, _cbLoc, _bloomLoc, _gradeLoc;
    /// Fires this frame (world positions) for the heat shimmer.
    public static readonly System.Collections.Generic.List<System.Numerics.Vector3> HazeSources = new();
    static Texture2D _copy;
    static bool _off;

    const string Fs = """
        #version 330
        in vec2 fragTexCoord;
        out vec4 o;
        uniform sampler2D texture0;
        uniform vec2 res;
        uniform vec3 sun;          // sun on screen (uv) and how strongly its shafts show
        uniform vec3 haze[6];      // fires on screen (uv) and their radius
        uniform int hazeCount;
        uniform float time;
        uniform int cbMode, bloomOn, gradeMode;
        void main() {
            vec2 uv = fragTexCoord;
            // heat shimmer above fires
            for (int i = 0; i < hazeCount; i++) {
                vec2 dd = (uv - haze[i].xy) * vec2(res.x / res.y, 1.0);
                float k = smoothstep(haze[i].z, 0.0, length(dd)) * step(uv.y, haze[i].y + haze[i].z * 0.3);
                uv += vec2(sin(uv.y * 90.0 + time * 9.0), cos(uv.x * 70.0 + time * 7.0)) * 0.0022 * k;
            }
            vec3 c = textureLod(texture0, uv, 0.0).rgb;
            // sun shafts: bright sky smeared out from the sun
            if (sun.z > 0.01) {
                vec2 step = (uv - sun.xy) / 28.0;
                vec2 p = uv;
                vec3 rays = vec3(0.0);
                float w = 1.0;
                for (int i = 0; i < 28; i++) {
                    p -= step;
                    vec3 s = textureLod(texture0, p, 3.0).rgb;
                    rays += max(s - 0.62, 0.0) * w;
                    w *= 0.94;
                }
                c += rays / 28.0 * vec3(1.0, 0.85, 0.65) * sun.z * 2.4;
            }
            // bloom: the bright parts of ever smaller copies of the frame, spread wide
            vec3 b = max(textureLod(texture0, uv, 2.0).rgb - 0.72, 0.0) * 0.45
                   + max(textureLod(texture0, uv, 3.5).rgb - 0.66, 0.0) * 0.6
                   + max(textureLod(texture0, uv, 5.0).rgb - 0.6, 0.0) * 0.8;
            if (bloomOn == 1) c += b;
            // grade: a little more colour and contrast, warm light and cool shade
            float l = dot(c, vec3(0.299, 0.587, 0.114));
            c = mix(vec3(l), c, 1.15);
            c = clamp(c, 0.0, 1.0);
            c = mix(c, c * c * (3.0 - 2.0 * c), 0.45);
            if (gradeMode == 0) c *= mix(vec3(0.93, 0.98, 1.06), vec3(1.07, 1.0, 0.9), smoothstep(0.15, 0.75, l));
            else if (gradeMode == 1) c *= mix(vec3(0.98, 0.9, 1.12), vec3(1.04, 0.98, 1.06), smoothstep(0.15, 0.75, l));
            // vignette
            vec2 d = uv - 0.5; d.x *= res.x / res.y;
            c *= 1.0 - smoothstep(0.45, 1.15, length(d)) * 0.5;
            // colour-blind filters: shift the colours that blend together toward ones that don't (daltonize)
            if (cbMode > 0) {
                mat3 sim = cbMode == 1 ? mat3(0.625, 0.7, 0.0, 0.375, 0.3, 0.3, 0.0, 0.0, 0.7)
                         : cbMode == 2 ? mat3(0.567, 0.558, 0.0, 0.433, 0.442, 0.242, 0.0, 0.0, 0.758)
                         : mat3(0.95, 0.0, 0.0, 0.05, 0.433, 0.475, 0.0, 0.567, 0.525);
                vec3 err = c - sim * c;
                c += mat3(0.0, 0.7, 0.7, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0) * err;
                c = clamp(c, 0.0, 1.0);
            }
            o = vec4(c, 1.0);
        }
        """;

    public static void Load()
    {
        _off = Environment.GetEnvironmentVariable("DAEVAS_NO_POST") == "1";
        if (_off) return;
        _shader = Raylib.LoadShaderFromMemory((string) null!, Fs);
        _resLoc = Raylib.GetShaderLocation(_shader, "res");
        _sunLoc = Raylib.GetShaderLocation(_shader, "sun");
        _hazeLoc = Raylib.GetShaderLocation(_shader, "haze");
        _hazeCountLoc = Raylib.GetShaderLocation(_shader, "hazeCount");
        _timeLoc = Raylib.GetShaderLocation(_shader, "time");
        _cbLoc = Raylib.GetShaderLocation(_shader, "cbMode");
        _gradeLoc = Raylib.GetShaderLocation(_shader, "gradeMode");
        _bloomLoc = Raylib.GetShaderLocation(_shader, "bloomOn");
        _off = !Raylib.IsShaderValid(_shader);
        Log.Info($"post pass ready={!_off}");
    }

    /// Grades what is on screen so far (the sky and the 3D scene). Call after EndMode3D, before the HUD.
    public static void Apply(Camera3D cam)
    {
        if (_off) return;
        int w = Raylib.GetRenderWidth(), h = Raylib.GetRenderHeight();
        if (w <= 0 || h <= 0) return;
        if (_copy.Width != w || _copy.Height != h)
        {
            if (_copy.Id != 0) Raylib.UnloadTexture(_copy);
            var img = Raylib.GenImageColor(w, h, Color.Black);
            _copy = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
        }
        Rlgl.DrawRenderBatchActive();
        glBindTexture(Texture2DTarget, _copy.Id);
        glCopyTexSubImage2D(Texture2DTarget, 0, 0, 0, 0, 0, w, h);
        glBindTexture(Texture2DTarget, 0);
        Raylib.GenTextureMipmaps(ref _copy);
        Raylib.SetTextureFilter(_copy, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(_copy, TextureWrap.Clamp);
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.SetShaderValue(_shader, _resLoc, new System.Numerics.Vector2(sw, sh), ShaderUniformDataType.Vec2);
        // where the sun is on screen, and how much it shows (in front of the camera, daytime, not overcast)
        var fwd = System.Numerics.Vector3.Normalize(cam.Target - cam.Position);
        var toSun = -Env.SunDir;
        var facing = System.Numerics.Vector3.Dot(fwd, toSun);
        var sunAt = Raylib.GetWorldToScreen(cam.Position + toSun * 500, cam);
        var strength = facing > 0.2f ? MathF.Min(1, (facing - 0.2f) * 2) * Env.Day * (1 - Env.Overcast * 0.8f) * (1 - Env.Dust * 0.5f) : 0;
        Raylib.SetShaderValue(_shader, _sunLoc, new System.Numerics.Vector3(sunAt.X / sw, 1 - sunAt.Y / sh, strength), ShaderUniformDataType.Vec3);
        var haze = new System.Numerics.Vector3[6];
        var n = 0;
        foreach (var f in HazeSources)
        {
            if (n >= 6) break;
            var to = f - cam.Position;
            if (System.Numerics.Vector3.Dot(to, fwd) < 1 || to.Length() > 40) continue;
            var sp = Raylib.GetWorldToScreen(f + System.Numerics.Vector3.UnitY * 1.5f, cam);
            haze[n++] = new System.Numerics.Vector3(sp.X / sw, 1 - sp.Y / sh, MathF.Min(0.15f, 2.5f / to.Length()));
        }
        Raylib.SetShaderValueV(_shader, _hazeLoc, haze, ShaderUniformDataType.Vec3, 6);
        Raylib.SetShaderValue(_shader, _hazeCountLoc, n, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _timeLoc, (float) Raylib.GetTime(), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _cbLoc, Settings.S.ColourBlind, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _gradeLoc, Settings.S.Grade, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _bloomLoc, Settings.S.Bloom && !Settings.S.ReduceFlashing ? 1 : 0, ShaderUniformDataType.Int);
        Raylib.BeginShaderMode(_shader);
        // the copy is bottom-up, so flip it back
        Raylib.DrawTexturePro(_copy, new Rectangle(0, 0, w, -h), new Rectangle(0, 0, sw, sh), System.Numerics.Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
    }
}
