using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// The sky behind the city: a dusty Gears of War: E-Day horizon blending into the fog colour, a low sun with
/// a glow, and slow layered clouds lit from the sun side. Drawn as a full-screen pass before the 3D scene.
static class Sky
{
    static Shader _shader;
    static int _right, _up, _fwd, _tanHalf, _aspect, _res, _time, _sun, _fog, _env, _abyss;

    const string Fs = """
        #version 330
        out vec4 o;
        uniform vec3 camRight, camUp, camFwd, sunDir, fogCol;
        uniform float tanHalf, aspect, time;
        uniform vec2 res;
        uniform vec4 env;   // day, overcast, dust, flash
        uniform vec3 abyss; // the void's colour (Lit.AbyssColor)
        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float noise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
        }
        float fbm(vec2 p) { float v = 0.0, a = 0.5; for (int i = 0; i < 5; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; } return v; }
        void main() {
            float day = env.x, overcast = env.y, dust = env.z, flash = env.w;
            vec2 uv = (gl_FragCoord.xy / res) * 2.0 - 1.0;
            vec3 d = normalize(camFwd + uv.x * tanHalf * aspect * camRight + uv.y * tanHalf * camUp);
            vec3 toSun = -sunDir;
            float h = clamp(d.y, -0.2, 1.0);
            vec3 horizon = fogCol;
            vec3 zenith = mix(vec3(0.02, 0.03, 0.07), vec3(0.30, 0.36, 0.46), day);
            zenith = mix(zenith, horizon * 0.8, overcast * 0.7 + dust * 0.8);
            vec3 mid = mix(horizon, zenith, 0.5);
            vec3 c = h < 0.25 ? mix(horizon, mid, smoothstep(0.0, 0.25, h)) : mix(mid, zenith, smoothstep(0.25, 0.9, h));
            float s = max(dot(d, toSun), 0.0);
            float clear = 1.0 - max(overcast * 0.8, dust * 0.9);
            if (day > 0.15) {
                // warm light around the sun, then the disc
                c += (vec3(1.0, 0.72, 0.42) * pow(s, 8.0) * 0.35 + vec3(1.0, 0.85, 0.6) * pow(s, 60.0) * 0.6) * clear;
                c = mix(c, vec3(1.0, 0.96, 0.86), smoothstep(0.9993, 0.9997, s) * clear);
            } else {
                // night: the moon and stars
                c = mix(c, vec3(0.85, 0.88, 0.95), smoothstep(0.9990, 0.9994, s) * clear);
                c += vec3(0.25, 0.3, 0.45) * pow(s, 30.0) * 0.4 * clear;
                vec2 sp = d.xz / max(d.y, 0.05) * 60.0;
                float star = step(0.9975, hash(floor(sp))) * smoothstep(0.05, 0.3, d.y) * clear * (0.6 + 0.4 * sin(time * 3.0 + hash(floor(sp)) * 40.0));
                c += vec3(star);
            }
            if (d.y > 0.01) {
                vec2 p = d.xz / (d.y + 0.08);
                float c1 = fbm(p * 0.9 + vec2(time * 0.006, time * 0.002) * (1.0 + overcast * 3.0));
                float c2 = fbm(p * 2.4 - vec2(time * 0.010, 0.0));
                float cover = smoothstep(0.48 - overcast * 0.35, 0.78, c1 * 0.75 + c2 * 0.35);
                vec3 lit = mix(vec3(0.46, 0.44, 0.44), vec3(0.95, 0.80, 0.62), pow(s, 3.0)) * (0.2 + 0.8 * day);
                vec3 shade = vec3(0.33, 0.33, 0.36) * (0.25 + 0.75 * day) * (1.0 - overcast * 0.4);
                vec3 cloud = mix(shade, lit, smoothstep(0.35, 0.9, c2) * clear);
                c = mix(c, cloud, cover * smoothstep(0.01, 0.2, d.y) * 0.85);
            }
            // below the horizon, the Abyss: a violet void with drifting nebulae, aether glints and stars
            if (d.y < 0.02) {
                float down = smoothstep(0.02, -0.45, d.y);
                vec2 q = d.xz / max(-d.y + 0.12, 0.12);
                float neb = fbm(q * 0.7 + vec2(time * 0.004, -time * 0.003));
                float neb2 = fbm(q * 1.9 - vec2(time * 0.006, 0.0));
                vec3 voidCol = mix(abyss * 1.25, abyss * 0.35, down);
                voidCol += vec3(0.30, 0.16, 0.42) * smoothstep(0.45, 0.85, neb) * 0.55 * (0.4 + 0.6 * day);
                voidCol += vec3(0.10, 0.30, 0.34) * smoothstep(0.55, 0.9, neb2) * 0.35;
                vec2 sq = q * 140.0;
                float star = step(0.994, hash(floor(sq))) * smoothstep(0.32, 0.0, length(fract(sq) - 0.5)) * (0.5 + 0.5 * sin(time * 2.0 + hash(floor(sq)) * 30.0)) * down;
                voidCol += vec3(0.8, 0.75, 1.0) * star * 0.8;
                c = mix(c, voidCol, smoothstep(0.02, -0.12, d.y));
            }
            c = mix(c, horizon, smoothstep(0.18, 0.0, abs(d.y)) * 0.6);
            c += vec3(0.75, 0.8, 1.0) * flash * 0.6;
            o = vec4(c, 1.0);
        }
        """;

    public static void Load()
    {
        _shader = Raylib.LoadShaderFromMemory((string) null!, Fs);
        _right = Raylib.GetShaderLocation(_shader, "camRight");
        _up = Raylib.GetShaderLocation(_shader, "camUp");
        _fwd = Raylib.GetShaderLocation(_shader, "camFwd");
        _tanHalf = Raylib.GetShaderLocation(_shader, "tanHalf");
        _aspect = Raylib.GetShaderLocation(_shader, "aspect");
        _res = Raylib.GetShaderLocation(_shader, "res");
        _time = Raylib.GetShaderLocation(_shader, "time");
        _sun = Raylib.GetShaderLocation(_shader, "sunDir");
        _fog = Raylib.GetShaderLocation(_shader, "fogCol");
        _env = Raylib.GetShaderLocation(_shader, "env");
        _abyss = Raylib.GetShaderLocation(_shader, "abyss");
        Log.Info($"sky shader ready={Raylib.IsShaderValid(_shader)}");
    }

    public static void Draw(Camera3D cam)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var fwd = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, cam.Up));
        var up = Vector3.Cross(right, fwd);
        Raylib.SetShaderValue(_shader, _right, right, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _up, up, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _fwd, fwd, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _tanHalf, MathF.Tan(cam.FovY * MathF.PI / 360), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _aspect, sw / (float) sh, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _res, new Vector2(sw, sh), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _time, (float) Raylib.GetTime(), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _sun, Lit.SunDir, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _fog, Env.FogColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _env, new Vector4(Env.Day, Env.Overcast, Env.Dust, Env.Flash), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_shader, _abyss, Lit.AbyssColor, ShaderUniformDataType.Vec3);
        Raylib.BeginShaderMode(_shader);
        Raylib.DrawRectangle(0, 0, sw, sh, Color.White);
        Raylib.EndShaderMode();
    }
}
