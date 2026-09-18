shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Terrain surface shader.
//
// Four CC0 PBR material sets blended by slope, altitude and low-frequency noise.
//
// Two constraints shape every decision here:
//
//  1. THE VIEW. This game is played from 50-500 m up, looking down, moving fast. Macro
//     variation matters enormously; fine detail almost not at all beyond a few hundred
//     metres. So detail sampling fades out with distance and the far field is carried by
//     noise-driven colour variation, which costs almost nothing.
//
//  2. THE PALETTE. A photographic grass texture is a vivid, healthy green and reads as a
//     golf course. This world is drying out and coming apart. Everything is desaturated,
//     pushed toward olive and ochre, and broken up with dead patches - not to look bleak,
//     but to look like somewhere real that stopped being looked after.

uniform sampler2D grass_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_rgh : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D dirt_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_rgh : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform float detail_scale = 0.22;
uniform float detail_fade_start = 90.0;
uniform float detail_fade_end = 520.0;

uniform float slope_rock_start = 0.68;
uniform float slope_rock_end = 0.44;
uniform float gravel_height = 135.0;
uniform float gravel_fade = 80.0;

uniform float patch_frequency = 0.0030;
uniform float field_frequency = 0.00048;

// --- Palette -------------------------------------------------------------
// Averaged far-field colours, used once detail sampling has faded out. Chosen by eye to
// match the textures under the grade rather than measured, because what matters is that
// the transition is invisible.
uniform vec3 far_grass : source_color = vec3(0.235, 0.255, 0.150);
uniform vec3 far_dirt : source_color = vec3(0.250, 0.205, 0.150);
uniform vec3 far_rock : source_color = vec3(0.235, 0.225, 0.210);
uniform vec3 far_gravel : source_color = vec3(0.300, 0.285, 0.250);

uniform float saturation = 0.58;
uniform vec3 grade_tint : source_color = vec3(1.02, 1.00, 0.88);
uniform vec3 dead_tint : source_color = vec3(0.62, 0.55, 0.36);
uniform float dead_amount = 0.55;

uniform float normal_strength = 1.1;

varying vec3 world_pos;
varying vec3 world_normal;

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
               mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
    return value_noise(p) * 0.62 + value_noise(p * 2.7) * 0.26 + value_noise(p * 6.1) * 0.12;
}

vec3 desaturate(vec3 c, float amount) {
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    return mix(vec3(l), c, amount);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec2 uv = world_pos.xz;
    float slope = clamp(world_normal.y, 0.0, 1.0);

    // --- Blend weights ----------------------------------------------------
    float patch = fbm(uv * patch_frequency);
    float field = fbm(uv * field_frequency);

    float rock_w = 1.0 - smoothstep(slope_rock_end, slope_rock_start, slope);
    float gravel_w = smoothstep(gravel_height - gravel_fade, gravel_height + gravel_fade,
                                world_pos.y + (patch - 0.5) * 110.0) * (1.0 - rock_w);
    float dirt_w = smoothstep(0.50, 0.74, patch) * (1.0 - rock_w) * (1.0 - gravel_w);
    float grass_w = max(0.0, 1.0 - rock_w - dirt_w - gravel_w);

    float total = max(rock_w + dirt_w + gravel_w + grass_w, 1e-4);
    rock_w /= total; dirt_w /= total; gravel_w /= total; grass_w /= total;

    // --- Detail, faded out with distance ----------------------------------
    // Most pixels on screen are far away. Sampling twelve textures for each of them is
    // most of the frame cost and none of the image, so past a few hundred metres the
    // surface becomes flat tinted colour and nobody can tell.
    float dist = length(world_pos - CAMERA_POSITION_WORLD);
    float detail = 1.0 - smoothstep(detail_fade_start, detail_fade_end, dist);

    vec3 col = far_grass * grass_w + far_dirt * dirt_w + far_rock * rock_w + far_gravel * gravel_w;
    float rgh = 0.85;
    vec3 nrm = vec3(0.5, 0.5, 1.0);

    if (detail > 0.004) {
        vec2 duv = uv * detail_scale;
        vec3 dcol = texture(grass_col, duv).rgb * grass_w
                  + texture(dirt_col, duv).rgb * dirt_w
                  + texture(rock_col, duv).rgb * rock_w
                  + texture(gravel_col, duv).rgb * gravel_w;

        vec3 dnrm = texture(grass_nrm, duv).rgb * grass_w
                  + texture(dirt_nrm, duv).rgb * dirt_w
                  + texture(rock_nrm, duv).rgb * rock_w
                  + texture(gravel_nrm, duv).rgb * gravel_w;

        float drgh = texture(grass_rgh, duv).r * grass_w
                   + texture(dirt_rgh, duv).r * dirt_w
                   + texture(rock_rgh, duv).r * rock_w
                   + texture(gravel_rgh, duv).r * gravel_w;

        // Match the detail texture to the far-field colour before blending, so the two
        // agree in tone and the fade is invisible rather than a visible ring.
        vec3 dcol_matched = dcol * (col / max(dcol, vec3(0.03)));
        col = mix(col, mix(dcol, dcol_matched, 0.65), detail);
        nrm = mix(nrm, dnrm, detail);
        rgh = mix(rgh, drgh, detail);
    }

    // --- Grade ------------------------------------------------------------
    // Dead ground: large, soft patches where whatever was growing has given up. This is
    // most of what stops the landscape reading as farmland.
    float dead = smoothstep(0.42, 0.78, field) * dead_amount;
    col = mix(col, col * dead_tint * 1.6, dead * (grass_w * 0.85 + dirt_w * 0.3));

    col = desaturate(col, saturation) * grade_tint;
    col *= mix(0.80, 1.14, fbm(uv * 0.00026));   // very large scale brightness drift

    ALBEDO = clamp(col, vec3(0.0), vec3(1.0));
    NORMAL_MAP = nrm;
    NORMAL_MAP_DEPTH = normal_strength * detail;
    ROUGHNESS = clamp(rgh * 0.9 + 0.12, 0.35, 1.0);
    SPECULAR = 0.28;
    METALLIC = 0.0;
}
