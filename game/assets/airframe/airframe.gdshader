shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Wear on a painted airframe, computed in OBJECT space.
//
// Object space because the airframe has no UVs at all - it is lofted and boxed together in
// AirframeBuilder with no SetUV anywhere, so there is no texture space to paint into.
// Unwrapping a procedurally generated hull is a great deal of work for a result that world
// or object coordinates give directly, and object space has the property that matters here:
// the wear is fixed to the aircraft and does not swim as it flies.
//
// Everything is derived from position and normal. No textures, so nothing to author, nothing
// to load, and it scales with the aircraft rather than being baked for one hull shape.

uniform vec3 livery : source_color = vec3(0.40, 0.42, 0.35);
uniform vec3 bleach : source_color = vec3(0.62, 0.62, 0.53);

// 0 factory fresh, 1 a machine that has been outside for twenty years.
uniform float wear : hint_range(0.0, 1.0) = 0.55;

// Soot and scorching, driven by the damage model.
uniform float damage : hint_range(0.0, 1.0) = 0.0;

uniform float panel_spacing = 0.92;
uniform float base_roughness : hint_range(0.0, 1.0) = 0.68;

varying vec3 obj_pos;
varying vec3 obj_nrm;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x),
               mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.07; a *= 0.5; }
    return v;
}

// Distance to the nearest line of a repeating grid, anti-aliased against the screen-space
// derivative so panel lines stay a hairline at any range instead of aliasing into noise.
float seam(float coord, float spacing) {
    float f = abs(fract(coord / spacing) - 0.5) * spacing;
    float w = fwidth(coord) * 1.4 + 1e-5;
    return 1.0 - smoothstep(0.0, w, f);
}

void vertex() {
    obj_pos = VERTEX;
    obj_nrm = NORMAL;
}

void fragment() {
    vec3 n = normalize(obj_nrm);
    vec3 col = livery;

    // --- Sun bleaching -----------------------------------------------------
    // Paint fades where the sky can see it. Upward-facing panels on a machine left out in
    // the weather go chalky and lose their colour long before the sides do.
    float up = clamp(n.y, 0.0, 1.0);
    float fade = up * up * wear * (0.55 + 0.45 * fbm(obj_pos.xz * 1.7));
    col = mix(col, bleach, fade * 0.65);

    // --- Grime -------------------------------------------------------------
    // And it collects underneath, where nothing ever washes it off.
    float down = clamp(-n.y, 0.0, 1.0);
    col *= 1.0 - down * wear * 0.38;

    // --- Panel lines -------------------------------------------------------
    // Along the fuselage and around it. Two families is enough to read as panelling; a
    // third starts to look like a grid drawn on the outside.
    float lines = max(seam(obj_pos.z, panel_spacing),
                      seam(obj_pos.y, panel_spacing * 1.35) * 0.7);
    col *= 1.0 - lines * 0.30;

    // Rivet-ish stippling next to the seams, faint.
    float stipple = step(0.72, hash(floor(obj_pos.zy * 26.0)));
    col *= 1.0 - lines * stipple * 0.18;

    // --- Streaking ---------------------------------------------------------
    // Dirt runs DOWNWARD from every seam and every opening, which is why a weathered
    // aircraft reads as vertical smears rather than as even dirt. Sampling the noise with a
    // compressed vertical coordinate is what smears it.
    float streak = fbm(vec2(obj_pos.x * 5.0 + obj_pos.z * 1.3, obj_pos.y * 0.45));
    streak = smoothstep(0.42, 0.85, streak);
    col *= 1.0 - streak * wear * 0.26 * (0.35 + 0.65 * (1.0 - up));

    // --- Repairs -----------------------------------------------------------
    // Big soft patches in a slightly wrong shade: panels replaced from something else and
    // never repainted to match. This is the detail that makes it read as a machine that has
    // been kept going rather than one that came off a line.
    float patch = fbm(obj_pos.xz * 0.55 + vec2(11.3, 4.7));
    float patchMask = smoothstep(0.62, 0.70, patch) * wear;
    col = mix(col, livery * vec3(1.14, 1.06, 0.92), patchMask * 0.55);

    // --- Damage ------------------------------------------------------------
    float soot = smoothstep(0.35, 0.9, fbm(obj_pos.xz * 2.3 + 31.0)) * damage;
    col *= 1.0 - soot * 0.65;

    ALBEDO = col;

    // Rough where it is faded and dirty, still a little glossy where the paint survives.
    float rough = base_roughness + fade * 0.22 + streak * 0.10 - patchMask * 0.12;
    ROUGHNESS = clamp(rough, 0.25, 0.98);
    METALLIC = 0.10 * (1.0 - fade);
    SPECULAR = 0.35;
}
