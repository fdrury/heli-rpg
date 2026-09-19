shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Standing water in a dying landscape. Not crystal, not blue, not inviting.
// Murky, still, with scum and algae patches. From helicopter altitude it reads as a
// dark surface that catches light differently from the surrounding land.
//
// Entirely procedural - no textures. The surface is simple enough that noise-driven
// colour variation and a gentle normal perturbation carry it from any altitude the
// game cares about.

uniform vec3 water_color : source_color = vec3(0.065, 0.088, 0.072);
uniform vec3 deep_color : source_color = vec3(0.038, 0.055, 0.045);

// Open sea. The world is an archipelago, and sheltered water is not the same surface as
// the water outside the headlands: a river or a sound is still and scummed, and the sea
// has swell on it whatever the weather has been doing.
//
// Which is which is decided here by a cheap box test rather than by the real coastline.
// The exact answer lives in WorldHeight.ShoreDistance and cannot be had in a fragment
// shader without duplicating the island table into GLSL, where it would drift the first
// time anybody moved a lobe. The cost of the approximation is that a wide bay inside the
// island's bounding box reads as sheltered, which is what a wide bay inside an island IS.
uniform float island_extent = 7600.0;
uniform vec3 sea_color : source_color = vec3(0.043, 0.063, 0.074);
uniform vec3 sea_deep : source_color = vec3(0.022, 0.036, 0.048);
uniform float swell_length = 46.0;
uniform float swell_height = 0.055;

varying vec3 world_pos;

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
               mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 uv = world_pos.xz;

    // 0 in the rivers and the sounds, 1 out past the last headland.
    float boxd = max(abs(world_pos.x), abs(world_pos.z));
    float sea = smoothstep(island_extent * 0.80, island_extent * 1.30, boxd);

    // Large-scale patches: algae, sediment, surface film. What makes stagnant water
    // look stagnant rather than like a flat colour.
    float n = vnoise(uv * 0.012) * 0.5 + vnoise(uv * 0.035) * 0.3 + vnoise(uv * 0.085) * 0.2;

    // Very slow drift. Perceptible only if you hover and stare, which is the point:
    // it is alive but barely.
    float drift = vnoise(uv * 0.008 + vec2(TIME * 0.003, TIME * 0.002));

    vec3 col = mix(deep_color, water_color, n * 0.55 + drift * 0.18);
    col = mix(col, mix(sea_deep, sea_color, n * 0.5 + drift * 0.22), sea);

    // Subtle ripple normal. A calm surface with just enough perturbation to catch a
    // sun reflection differently from frame to frame.
    float e = 0.6;
    vec2 d = uv * 0.042 + TIME * vec2(0.004, 0.003);
    float nL = vnoise(d + vec2(-e, 0.0));
    float nR = vnoise(d + vec2(e, 0.0));
    float nD = vnoise(d + vec2(0.0, -e));
    float nU = vnoise(d + vec2(0.0, e));

    // Swell, only at sea. Two crossed travelling waves rather than one, because a single
    // sine reads as corduroy from the air the moment you are high enough to see a few
    // wavelengths of it at once. This perturbs the NORMAL only - the surface stays a flat
    // quad, which is what keeps the ocean free.
    vec2 dir1 = normalize(vec2(0.86, 0.51));
    vec2 dir2 = normalize(vec2(-0.42, 0.91));
    float k = 6.2831853 / swell_length;
    float s1 = sin(dot(uv, dir1) * k + TIME * 0.85);
    float s2 = sin(dot(uv, dir2) * k * 0.61 - TIME * 0.52);
    vec2 swell = (dir1 * s1 + dir2 * s2 * 0.7) * swell_height * sea;

    ALBEDO = col;
    ROUGHNESS = mix(0.22, 0.10, sea);
    METALLIC = 0.0;
    SPECULAR = mix(0.55, 0.80, sea);
    NORMAL = normalize(vec3((nL - nR) * 0.12 + swell.x, 1.0, (nD - nU) * 0.12 + swell.y));
}
