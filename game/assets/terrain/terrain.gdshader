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
uniform float detail_fade_start = 140.0;
uniform float detail_fade_end = 1150.0;

// Macro texture for the far field: ONE sample at a very large scale, standing in for the
// twelve the near field uses. A helicopter spends most of its time at 100-500 m, so most
// of the screen is always far field - it has to carry the image, not just be cheap.
uniform float macro_scale = 0.0045;
uniform float macro_strength = 0.42;

// Where bare rock takes over from ground cover, as the surface normal's Y component
// (1.0 flat, 0.0 vertical).
//
// Was 0.68 / 0.44, which is rock only past about 64 degrees. The world report puts the
// 90th percentile slope at 43 degrees and the 99th at 65, so effectively nothing in the
// world ever reached it and every hillside in the game - however steep - was grassed to
// the top. 0.84 starts rock at about 33 degrees and 0.55 is bare by 57.
uniform float slope_rock_start = 0.84;
uniform float slope_rock_end = 0.55;
// Bare high ground. Keyed well above the median terrain height and perturbed hard,
// because a sharp altitude threshold near the median draws contour lines across the
// whole landscape - which is exactly what it did at 135 m.
uniform float gravel_height = 285.0;
uniform float gravel_fade = 95.0;

uniform float patch_frequency = 0.0030;
uniform float field_frequency = 0.00048;

// --- Palette -------------------------------------------------------------
// Averaged far-field colours, used once detail sampling has faded out. Chosen by eye to
// match the textures under the grade rather than measured, because what matters is that
// the transition is invisible.
uniform vec3 far_grass : source_color = vec3(0.252, 0.256, 0.158);
uniform vec3 far_dirt : source_color = vec3(0.290, 0.238, 0.172);
uniform vec3 far_rock : source_color = vec3(0.225, 0.214, 0.198);
uniform vec3 far_gravel : source_color = vec3(0.288, 0.272, 0.238);

uniform float saturation = 0.64;
uniform vec3 grade_tint : source_color = vec3(1.03, 1.00, 0.91);
uniform vec3 dead_tint : source_color = vec3(0.86, 0.76, 0.54);
uniform float dead_amount = 0.55;

uniform float normal_strength = 1.1;

// --- Water, shore and the green line -------------------------------------
//
// The world drains to ONE level, so "how wet is this ground" is a pure function of how
// far it is above that level, and it can be answered here without knowing anything about
// where the rivers are.
//
// This matters more than it sounds. The water surface itself is a flat quad the streamer
// drops in at water_level; what makes a river READ from 300 m is not the water, it is the
// three bands of ground around it - black silt under the surface, wet mud at the edge,
// and then the only living green left in a landscape that has otherwise dried out. A
// pilot navigating without map markers is following that green line, not the water.
//
// Default matches WorldHeight.WaterLevel. It is a uniform so a future material can set it
// rather than have two files disagree about where the sea is.
uniform float water_level = -5.0;
uniform float wet_band = 2.6;        // metres above the water that still read as wet mud
uniform float riparian_band = 17.0;  // metres above it that anything still grows

uniform vec3 silt_color : source_color = vec3(0.055, 0.061, 0.048);
uniform vec3 mud_color : source_color = vec3(0.118, 0.104, 0.080);
uniform vec3 riparian_color : source_color = vec3(0.196, 0.247, 0.128);

varying vec3 world_pos;
varying vec3 world_normal;
varying vec2 surface_flags;   // x = true surface slope at this column

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
    surface_flags = COLOR.rg;
}

void fragment() {
    vec2 uv = world_pos.xz;
    // Slope comes from the vertex rather than the interpolated geometric normal. Edge
    // vertices are stitched down to match coarser neighbours, which tilts their geometric
    // normal without the ground under them having changed; reading the vertex value keeps
    // the material blend continuous across every chunk boundary.
    float slope = clamp(surface_flags.x, 0.0, 1.0);

    // --- Blend weights ----------------------------------------------------
    float patch = fbm(uv * patch_frequency);
    float field = fbm(uv * field_frequency);

    float rock_w = 1.0 - smoothstep(slope_rock_end, slope_rock_start, slope);
    float gravel_w = smoothstep(gravel_height - gravel_fade, gravel_height + gravel_fade,
                                world_pos.y + (patch - 0.5) * 210.0) * (1.0 - rock_w);
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

    // Macro breakup, always on. Two rotated samples of one texture at a scale of tens of
    // metres, which from the air reads as ground cover rather than as a repeating tile.
    {
        vec2 muv = uv * macro_scale;
        vec2 muv2 = vec2(muv.x * 0.7071 - muv.y * 0.7071, muv.x * 0.7071 + muv.y * 0.7071) * 2.31;
        float m = texture(grass_col, muv).g * 0.62 + texture(rock_col, muv2).g * 0.38;
        // Kept near unity on average and bounded at both ends. An earlier version scaled
        // up to 1.9x, and every sunlit slope in the world blew out to white.
        col *= mix(1.0, clamp(0.72 + m * 0.74, 0.62, 1.34), macro_strength);
    }

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
        // Weighted hard toward the colour-matched version. At 0.65 the raw texture kept
        // enough of its own green that the near field read as lawn while the far field
        // read as dry olive - a seam you could see from the air. The detail textures are
        // here for TEXTURE; the palette above decides the colour.
        col = mix(col, mix(dcol, dcol_matched, 0.88), detail);
        nrm = mix(nrm, dnrm, detail);
        rgh = mix(rgh, drgh, detail);
    }

    // --- Grade ------------------------------------------------------------
    // Dead ground: large, soft patches where whatever was growing has given up. This is
    // most of what stops the landscape reading as farmland.
    float dead = smoothstep(0.32, 0.82, field) * dead_amount;
    col = mix(col, col * dead_tint * 1.18, dead * (grass_w * 0.85 + dirt_w * 0.3));

    // Strata. Exposed rock in real country is bedded, and horizontal banding is the
    // single cheapest cue that a face is rock rather than a steep field. Perturbed by a
    // large-scale noise so the beds tilt and wander instead of ringing the world in
    // contour lines - which is what an unperturbed height term does, and did.
    if (rock_w > 0.01) {
        float bed = sin(world_pos.y * 0.085 + fbm(uv * 0.0032) * 5.5);
        float strata = 0.90 + 0.20 * smoothstep(-0.25, 0.25, bed);
        col = mix(col, col * strata, rock_w * 0.75);
    }

    col = desaturate(col, saturation) * grade_tint;

    // --- Water, shore and the green line ----------------------------------
    // After the grade, because these are the bands the eye navigates by and they should
    // not be desaturated along with everything else. That is the whole point of them: in
    // a landscape pushed toward dead olive and ochre, the one place with any colour left
    // is the one place with water in it.
    float above = world_pos.y - water_level;

    // Under the surface. Mostly hidden by the water plane the streamer drops in, but only
    // chunks that measure themselves as having water get one, and the coarse levels of
    // detail can miss a narrow channel entirely - so the bed has to read as a river bed
    // rather than as dry ground with a hole in it.
    float submerged = 1.0 - smoothstep(-0.4, 1.2, above);

    // The strand. Ground the water has had recently: dark, smooth, and far less rough
    // than anything dry, so it catches the sun differently from the bank above it. This
    // is the single strongest cue at altitude that a line on the ground is a river and
    // not a road.
    float wetness = 1.0 - smoothstep(0.0, wet_band, above);

    // And the green. What still grows in a country that has dried out is whatever has its
    // feet in the water, so this band IS the map - a pilot with no markers follows it.
    // Keyed on height alone, which works because the whole world drains to one level:
    // ground within seventeen metres of the waterline is riverbank, lake shore or dune,
    // and there is nowhere else on the island that low.
    float green = (1.0 - smoothstep(wet_band, riparian_band, above)) * (1.0 - submerged);

    col = mix(col, riparian_color * (0.80 + patch * 0.50), green * 0.62 * (1.0 - rock_w));
    col = mix(col, mud_color * (0.75 + patch * 0.60), wetness * 0.85);
    col = mix(col, silt_color, submerged);
    rgh = mix(rgh, 0.26, max(wetness, submerged) * 0.9);
    col *= mix(0.80, 1.14, fbm(uv * 0.00026));   // very large scale brightness drift

    ALBEDO = clamp(col, vec3(0.0), vec3(1.0));
    NORMAL_MAP = nrm;
    NORMAL_MAP_DEPTH = normal_strength * detail;
    ROUGHNESS = clamp(rgh * 0.9 + 0.12, 0.35, 1.0);
    SPECULAR = 0.28;
    METALLIC = 0.0;
}
