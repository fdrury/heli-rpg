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

    // Large-scale patches: algae, sediment, surface film. What makes stagnant water
    // look stagnant rather than like a flat colour.
    float n = vnoise(uv * 0.012) * 0.5 + vnoise(uv * 0.035) * 0.3 + vnoise(uv * 0.085) * 0.2;

    // Very slow drift. Perceptible only if you hover and stare, which is the point:
    // it is alive but barely.
    float drift = vnoise(uv * 0.008 + vec2(TIME * 0.003, TIME * 0.002));

    vec3 col = mix(deep_color, water_color, n * 0.55 + drift * 0.18);

    // Subtle ripple normal. A calm surface with just enough perturbation to catch a
    // sun reflection differently from frame to frame.
    float e = 0.6;
    vec2 d = uv * 0.042 + TIME * vec2(0.004, 0.003);
    float nL = vnoise(d + vec2(-e, 0.0));
    float nR = vnoise(d + vec2(e, 0.0));
    float nD = vnoise(d + vec2(0.0, -e));
    float nU = vnoise(d + vec2(0.0, e));

    ALBEDO = col;
    ROUGHNESS = 0.22;
    METALLIC = 0.0;
    SPECULAR = 0.55;
    NORMAL = normalize(vec3((nL - nR) * 0.12, 1.0, (nD - nU) * 0.12));
}
