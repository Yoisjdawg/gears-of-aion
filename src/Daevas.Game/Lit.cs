using System;
using System.Numerics;
using Raylib_cs;

namespace Daevas.Game;

/// A small lit shader (one sun, sky ambient, fog, sun shadows) used for the Gears guns, characters and the city.
static unsafe class Lit
{
    public static Shader Shader;
    static int _abyssLoc, _alphaLoc, _eyeLoc, _lightVpLoc, _shadowMapLoc, _useShadowLoc, _lightPosLoc, _lightColLoc, _lightCountLoc;
    static readonly Vector4[] _lightPos = new Vector4[16], _lightCol = new Vector4[16];
    static int _lights;

    /// The sun: the shader's light direction (keep the two in step).
    public static Vector3 SunDir => Env.SunDir;
    static int _sunDirLoc, _sunColLoc, _ambLoc, _fogColLoc, _fogLoc, _weatherLoc;

    /// The void around the district (night darkens it).
    public static Vector3 AbyssColor => Vector3.Lerp(new Vector3(0.05f, 0.03f, 0.10f), new Vector3(0.22f, 0.13f, 0.32f), Env.Day);

    /// The time of day and weather into the shader (call once a frame).
    public static void SetEnv()
    {
        Raylib.SetShaderValue(Shader, _sunDirLoc, Env.SunDir, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _sunColLoc, Env.SunColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _ambLoc, Env.Ambient, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _fogColLoc, Env.FogColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _abyssLoc, AbyssColor, ShaderUniformDataType.Vec3);
        var (start, range, max) = Env.Fog;
        Raylib.SetShaderValue(Shader, _fogLoc, new Vector4(start, range, max, Env.Time), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(Shader, _weatherLoc, new Vector4(Env.Wet, Env.Overcast, 1 - Env.Day, 0), ShaderUniformDataType.Vec4);
    }

    const string Vs = """
        #version 330
        in vec3 vertexPosition; in vec2 vertexTexCoord; in vec3 vertexNormal; in vec4 vertexColor;
        in vec4 vertexTangent;    // skinned characters: the vertex's four palette bones (weights in vertexColor)
        uniform mat4 mvp; uniform mat4 matModel; uniform mat4 matNormal; uniform mat4 lightVP;
        uniform sampler2D boneTex; // one row of bone matrices (3 texels each) per animation frame
        uniform vec4 skinA;       // on, row a, row b, a->b blend
        uniform vec4 skinB;       // row c, weight of c (the clip being blended out of)
        out vec2 uv; out vec3 n; out vec3 wpos; out vec4 vcol; out vec4 lpos;
        void skinRow(int row, vec4 w, float k, inout vec3 p, inout vec3 q) {
            vec4 v = vec4(vertexPosition, 1.0);
            for (int i = 0; i < 4; i++) {
                float wi = w[i] * k;
                if (wi <= 0.0) continue;
                int x = int(vertexTangent[i] + 0.5) * 3;
                vec4 r0 = texelFetch(boneTex, ivec2(x, row), 0);
                vec4 r1 = texelFetch(boneTex, ivec2(x + 1, row), 0);
                vec4 r2 = texelFetch(boneTex, ivec2(x + 2, row), 0);
                p += wi * vec3(dot(r0, v), dot(r1, v), dot(r2, v));
                q += wi * vec3(dot(r0.xyz, vertexNormal), dot(r1.xyz, vertexNormal), dot(r2.xyz, vertexNormal));
            }
        }
        void main() {
            uv = vertexTexCoord; vcol = vertexColor;
            vec3 P = vertexPosition, N = vertexNormal;
            if (skinA.x > 0.5) {
                vec4 w = vertexColor / max(dot(vertexColor, vec4(1.0)), 0.0001);
                vec3 p = vec3(0.0), q = vec3(0.0);
                float c = skinB.y;
                skinRow(int(skinA.y), w, (1.0 - skinA.w) * (1.0 - c), p, q);
                if (skinA.w > 0.0) skinRow(int(skinA.z), w, skinA.w * (1.0 - c), p, q);
                if (c > 0.0) skinRow(int(skinB.x), w, c, p, q);
                P = p; N = q; vcol = vec4(1.0);
            }
            n = normalize(vec3(matNormal * vec4(N, 0.0)));
            wpos = vec3(matModel * vec4(P, 1.0));
            lpos = lightVP * vec4(wpos, 1.0);
            gl_Position = mvp * vec4(P, 1.0);
        }
        """;

    const string Fs = """
        #version 330
        in vec2 uv; in vec3 n; in vec3 wpos; in vec4 vcol; in vec4 lpos;
        uniform sampler2D texture0; uniform vec4 colDiffuse; uniform vec3 eye;
        uniform int alphaMode;
        uniform vec3 abyssCol;
        uniform sampler2D shadowMap; uniform int useShadow;
        uniform vec4 lightPos[16]; uniform vec4 lightCol[16]; uniform int lightCount;
        uniform vec3 sunDir, sunCol, ambCol, fogCol;
        uniform vec4 fogParams;   // start, range, max, time
        uniform vec4 weather;     // wet, overcast (cloud shadows), night, unused
        out vec4 o;
        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float noise(vec2 p) {
            vec2 i = floor(p), f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
        }
        float sunLight(vec3 N) {
            if (useShadow == 0) return 1.0;
            vec3 p = lpos.xyz / lpos.w * 0.5 + 0.5;
            if (p.x <= 0.0 || p.y <= 0.0 || p.x >= 1.0 || p.y >= 1.0 || p.z >= 1.0) return 1.0;
            float bias = max(0.0025 * (1.0 - dot(N, -sunDir)), 0.0006);
            vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0));
            float lit = 0.0;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    lit += p.z - bias > texture(shadowMap, p.xy + vec2(x, y) * texel).r ? 0.0 : 1.0;
            return lit / 9.0;
        }
        void main() {
            // Gears textures often pack other data in alpha: it counts only where the material says so.
            // 0 opaque (the tint's alpha only: fades), 1 cut-out (fences, foliage), 2 blended (overlays, decals, wings), 3 glowing, 4 tinted glass
            vec4 tex = texture(texture0, uv);
            vec4 tint = colDiffuse * vcol; // (vertex colour is white unless a mesh sets it: the wings)
            float alpha = tint.a;
            if (alphaMode == 1) { if (tex.a < 0.5) discard; }
            else if (alphaMode == 2) { alpha *= tex.a; if (alpha < 0.04) discard; }
            else if (alphaMode == 4) { alpha = max(alpha * tex.a, 0.9); tex.rgb = mix(tex.rgb, vec3(0.05, 0.06, 0.08), 0.6); } // tinted glass
            vec4 base = vec4(tex.rgb * tint.rgb, alpha);
            vec3 N = normalize(gl_FrontFacing ? n : -n);
            // clouds drifting over the city dim the sun in moving patches
            float clouds = mix(1.0, 0.55 + 0.45 * smoothstep(0.35, 0.7, noise(wpos.xz * 0.025 + vec2(fogParams.w * 0.02, fogParams.w * 0.008))), 0.35 + 0.4 * weather.y);
            float d = max(dot(N, -sunDir), 0.0) * sunLight(N) * clouds;
            float sky = 0.5 + 0.5 * N.y;
            vec3 V = normalize(eye - wpos);
            // wet streets: darker and shinier
            float wet = weather.x * smoothstep(0.6, 0.95, N.y);
            base.rgb *= 1.0 - 0.35 * wet;
            // puddles on the street: they mirror the sky and catch the light
            float puddle = wet * smoothstep(0.55, 0.63, noise(wpos.xz * 0.12)) * step(wpos.y, 0.3);
            vec3 toEye = normalize(eye - wpos);
            vec3 mirror = reflect(-toEye, vec3(0.0, 1.0, 0.0));
            vec3 skyRef = mix(fogCol * 0.7, fogCol * 0.35 + vec3(0.05, 0.06, 0.1), clamp(mirror.y * 2.0, 0.0, 1.0)); // the sky in the water
            float glint = pow(max(dot(mirror, -sunDir), 0.0), 120.0) * 2.0;                             // the sun on it
            base.rgb = mix(base.rgb, skyRef + sunCol * glint, puddle * 0.8);
            wet = max(wet, puddle * 1.4);
            float spec = pow(max(dot(reflect(sunDir, N), V), 0.0), mix(24.0, 70.0, wet)) * (0.25 + 1.4 * wet) * d;
            // corners and the feet of walls are darker
            float ao = mix(0.72, 1.0, smoothstep(0.0, 2.2, wpos.y));
            vec3 c = base.rgb * (ambCol * (0.7 + 0.3 * sky) * ao + sunCol * d * 0.95) + spec * sunCol;
            // nearby fires, lamps and spells light the street and walls around them
            vec3 pl = vec3(0.0);
            for (int i = 0; i < lightCount; i++) {
                vec3 L = lightPos[i].xyz - wpos;
                float dist = length(L);
                float att = clamp(1.0 - dist / lightPos[i].w, 0.0, 1.0);
                pl += lightCol[i].rgb * att * att * (0.3 + 0.7 * max(dot(N, L / max(dist, 0.001)), 0.0));
            }
            c += base.rgb * min(pl, vec3(0.9)) + min(pl, vec3(0.9)) * wet * 0.25;
            float fog = clamp((length(eye - wpos) - fogParams.x) / fogParams.y, 0.0, fogParams.z);
            if (useShadow == 2) { o = vec4(vec3(sunLight(N)), 1.0); return; }
            if (alphaMode == 4) c += fogCol * 0.35 * pow(1.0 - abs(dot(N, normalize(eye - wpos))), 3.0); // the sky's sheen on glass
            // the Abyss lights what hangs below the street from beneath, violet
            c += base.rgb * abyssCol * 2.2 * max(-N.y, 0.0) * smoothstep(1.0, -40.0, wpos.y);
            if (alphaMode == 3) c = base.rgb * (1.1 + 0.9 * weather.z); // (weather.z: night, the crystals glow brighter) // glowing (the Abyss crystals): lit by themselves, still in the haze
            // below the street the haze turns to the Abyss's violet, and the deep fades into it
            float deep = smoothstep(-4.0, -120.0, wpos.y);
            vec3 fc = mix(fogCol, abyssCol, deep);
            fog = max(fog, smoothstep(-30.0, -300.0, wpos.y) * 0.92);
            o = vec4(mix(c, fc, fog), base.a);
        }
        """;

    public static void Load()
    {
        Shader = Environment.GetEnvironmentVariable("DAEVAS_UNLIT") == "1" ? Raylib.LoadShaderFromMemory((string) null!, (string) null!) : Raylib.LoadShaderFromMemory(Vs, Fs);
        Log.Info($"lit shader id {Shader.Id} ready={Raylib.IsShaderValid(Shader)}");
        _eyeLoc = Raylib.GetShaderLocation(Shader, "eye");
        _alphaLoc = Raylib.GetShaderLocation(Shader, "alphaMode");
        _abyssLoc = Raylib.GetShaderLocation(Shader, "abyssCol");
        _lightVpLoc = Raylib.GetShaderLocation(Shader, "lightVP");
        _shadowMapLoc = Raylib.GetShaderLocation(Shader, "shadowMap");
        _useShadowLoc = Raylib.GetShaderLocation(Shader, "useShadow");
        _lightPosLoc = Raylib.GetShaderLocation(Shader, "lightPos");
        _lightColLoc = Raylib.GetShaderLocation(Shader, "lightCol");
        _lightCountLoc = Raylib.GetShaderLocation(Shader, "lightCount");
        _sunDirLoc = Raylib.GetShaderLocation(Shader, "sunDir");
        _sunColLoc = Raylib.GetShaderLocation(Shader, "sunCol");
        _ambLoc = Raylib.GetShaderLocation(Shader, "ambCol");
        _fogColLoc = Raylib.GetShaderLocation(Shader, "fogCol");
        _fogLoc = Raylib.GetShaderLocation(Shader, "fogParams");
        _weatherLoc = Raylib.GetShaderLocation(Shader, "weather");
        _skinALoc = Raylib.GetShaderLocation(Shader, "skinA");
        _skinBLoc = Raylib.GetShaderLocation(Shader, "skinB");
        _boneTexLoc = Raylib.GetShaderLocation(Shader, "boneTex");
        SetEnv();
        NoSkin();
        ClearLights(); SendLights();
        SetShadows(false);
    }

    /// Point lights for the next 3D pass (fires, spells): up to 16, the first added win.
    public static void ClearLights() => _lights = 0;
    public static void AddLight(Vector3 at, float radius, Color col, float strength)
    {
        if (_lights >= _lightPos.Length || radius <= 0.1f || strength <= 0.01f) return;
        _lightPos[_lights] = new Vector4(at, radius);
        _lightCol[_lights] = new Vector4(col.R / 255f * strength, col.G / 255f * strength, col.B / 255f * strength, 1);
        _lights++;
    }
    public static void SendLights()
    {
        Raylib.SetShaderValueV(Shader, _lightPosLoc, _lightPos, ShaderUniformDataType.Vec4, _lightPos.Length);
        Raylib.SetShaderValueV(Shader, _lightColLoc, _lightCol, ShaderUniformDataType.Vec4, _lightCol.Length);
        Raylib.SetShaderValue(Shader, _lightCountLoc, _lights, ShaderUniformDataType.Int);
    }

    // ---------------------------------------------------------------- GPU skinning

    static int _skinALoc, _skinBLoc, _boneTexLoc;
    const int BoneSlot = 11;

    /// The next draws are a skinned character: blend rows a and b of its bone texture by f,
    /// then mix in row c by g (the clip it is blending out of).
    public static void Skin(Texture2D bones, int a, int b, float f, int c = 0, float g = 0)
    {
        Rlgl.ActiveTextureSlot(BoneSlot);
        Rlgl.EnableTexture(bones.Id);
        Raylib.SetShaderValue(Shader, _boneTexLoc, BoneSlot, ShaderUniformDataType.Int);
        Rlgl.ActiveTextureSlot(0);
        Raylib.SetShaderValue(Shader, _skinALoc, new Vector4(1, a, b, f), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(Shader, _skinBLoc, new Vector4(c, g, 0, 0), ShaderUniformDataType.Vec4);
    }

    public static void NoSkin() => Raylib.SetShaderValue(Shader, _skinALoc, Vector4.Zero, ShaderUniformDataType.Vec4);

    public static void SetEye(Vector3 eye) => Raylib.SetShaderValue(Shader, _eyeLoc, eye, ShaderUniformDataType.Vec3);

    public static Model Make(Mesh mesh)
    {
        var m = Raylib.LoadModelFromMesh(mesh);
        m.Materials[0].Shader = Shader;
        return m;
    }

    public static void Use(ref Model m) => m.Materials[0].Shader = Shader;

    public const int Opaque = 0, Cutout = 1, Blended = 2, Glow = 3, Glass = 4;
    static int _alphaMode;

    /// How texture alpha counts for what is drawn next (see the fragment shader); back to Opaque after.
    public static void AlphaMode(int mode)
    {
        if (mode == _alphaMode) return;
        Rlgl.DrawRenderBatchActive();
        _alphaMode = mode;
        Raylib.SetShaderValue(Shader, _alphaLoc, mode, ShaderUniformDataType.Int);
    }

    /// The alpha mode a Gears texture wants: glass by its name; otherwise by what its alpha holds. A hard mask is cut
    /// out, a largely clear one is blended (glass, overlays), and in-between values with few holes are packed data
    /// (roughness, height...) and are ignored.
    public static int AlphaFor(string? texture, byte[]? rgba)
    {
        var byName = AlphaFor(texture);
        if (byName == Blended || rgba == null || rgba.Length < 4) return byName;
        int n = 0, clear = 0, solid = 0; long sum = 0;
        for (var i = 3; i < rgba.Length; i += 4 * 7)
        {
            n++; sum += rgba[i];
            if (rgba[i] < 20) clear++; else if (rgba[i] > 235) solid++;
        }
        if (n == 0) return byName;
        var fc = clear / (float) n; var fs = solid / (float) n; var mean = sum / (255f * n);
        if (fc < 0.01f) return Opaque;                       // no holes: alpha is data (or nothing)
        if (fc + fs > 0.85f) return Cutout;                  // a hard mask: wire, fences, broken edges
        if (fc > 0.25f || mean < 0.5f) return Blended;       // largely clear: glass, grime and window overlays
        return Opaque;                                       // in-between values with few holes: packed data
    }

    /// The alpha mode a Gears texture wants, by its name.
    public static int AlphaFor(string? texture) =>
        texture == null ? Opaque
        : System.Text.RegularExpressions.Regex.IsMatch(texture, "glass|window_?pane", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? Blended
        : System.Text.RegularExpressions.Regex.IsMatch(texture, "fence|wire|chain|foliage|leaf|leaves|grass|ivy|vine|tree|plant|cable|_net|netting|grate|grill", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? Cutout
        : Opaque;

    // ---------------------------------------------------------------- sun shadows

    const int ShadowSize = 4096;
    const float ShadowSpan = 110;   // metres of ground the shadow map covers around the focus
    static RenderTexture2D _shadow;
    static bool _shadowReady;
    static Matrix4x4 _lightVp;

    static void SetShadows(bool on) => Raylib.SetShaderValue(Shader, _useShadowLoc, on ? 1 : 0, ShaderUniformDataType.Int);

    static void InitShadows()
    {
        _shadowReady = true;
        // depth only: raylib still sizes the viewport from the colour texture's width and height
        var rt = new RenderTexture2D { Id = Rlgl.LoadFramebuffer(), Texture = new Texture2D { Width = ShadowSize, Height = ShadowSize } };
        Rlgl.EnableFramebuffer(rt.Id);
        rt.Depth = new Texture2D { Id = Rlgl.LoadTextureDepth(ShadowSize, ShadowSize, false), Width = ShadowSize, Height = ShadowSize, Format = (PixelFormat) 19, Mipmaps = 1 };
        Rlgl.FramebufferAttach(rt.Id, rt.Depth.Id, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        var ok = Rlgl.FramebufferComplete(rt.Id);
        Rlgl.DisableFramebuffer();
        _shadow = ok ? rt : default;
        Log.Info($"sun shadows {(ok ? "ready" : "unavailable")} ({ShadowSize}x{ShadowSize})");
    }

    /// Renders what `drawCasters` draws from the sun into the shadow map, centred on `focus`.
    public static void ShadowPass(Vector3 focus, Action<Camera3D> drawCasters)
    {
        if (!_shadowReady) InitShadows();
        if (_shadow.Id == 0) return;
        SetShadows(false);
        var light = new Camera3D(focus - SunDir * 150, focus, Vector3.UnitY, ShadowSpan, CameraProjection.Orthographic);
        Raylib.BeginTextureMode(_shadow);
        Rlgl.ClearScreenBuffers();
        Rlgl.SetClipPlanes(1, 400);
        Raylib.BeginMode3D(light);
        var view = Rlgl.GetMatrixModelview();
        var proj = Rlgl.GetMatrixProjection();
        drawCasters(light);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        Rlgl.SetClipPlanes(0.12, 1000);
        _lightVp = Raymath.MatrixMultiply(view, proj);
    }

    public static void NoShadows() => SetShadows(false);

    /// Before the main 3D pass: sample the shadow map in the lit shader.
    public static void UseShadows()
    {
        if (_shadow.Id == 0) { SetShadows(false); return; }
        Raylib.SetShaderValueMatrix(Shader, _lightVpLoc, _lightVp);
        SetShadows(true);
        if (Environment.GetEnvironmentVariable("DAEVAS_SHADOWDEBUG") == "1") Raylib.SetShaderValue(Shader, _useShadowLoc, 2, ShaderUniformDataType.Int);
        var slot = 10;
        Rlgl.ActiveTextureSlot(slot);
        Rlgl.EnableTexture(_shadow.Depth.Id);
        Raylib.SetShaderValue(Shader, _shadowMapLoc, slot, ShaderUniformDataType.Int);
        Rlgl.ActiveTextureSlot(0);
    }
}
