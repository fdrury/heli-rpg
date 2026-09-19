shader_type spatial;
render_mode unshaded, cull_back, blend_mix;

// Interior light behind window glass, driven by the day/night cycle.
//
// Unshaded because this IS the light source — sun and moon have no business
// illuminating the inside of a room. The daylight uniform comes from
// SceneMoodDriver via the shared ShaderMaterial on SiteKit.Palette.Glow.
//
// Per-window variation is derived from world position so that each window gets a
// stable identity: some rooms are dark, some are warmer, and none of them flicker
// in sync. The hash resolution (~2 m cells) is coarse enough that every fragment
// of a single window pane lands in the same cell.

uniform float daylight : hint_range(0.0, 1.0) = 1.0;

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    // One hash per window: floor to ~2 m cells so every fragment on the same pane
    // agrees. Mixing in Y separates stacked windows on multi-storey buildings.
    vec2 cell = floor(world_pos.xz * 0.45 + vec2(world_pos.y * 0.9, 0.0));
    float h = hash(cell);

    // ~30 % of windows stay dark — empty rooms, storage, boarded from inside.
    float occupied = step(0.30, h);

    // Warm kerosene / firelight colour with per-window variation.
    // The base is amber; some shift toward orange, a few toward pale gold.
    vec3 warm = mix(vec3(1.0, 0.60, 0.26), vec3(1.0, 0.50, 0.20), fract(h * 7.3));
    warm = mix(warm, vec3(1.0, 0.72, 0.38), fract(h * 3.1) * 0.3);

    // Fade in through twilight: invisible above 0.38, full below 0.10.
    float glow = smoothstep(0.38, 0.10, daylight) * occupied;

    // Subtle flicker — fire breathes, it does not strobe.
    float flicker = 0.92 + 0.08 * sin(TIME * (1.2 + h * 3.5) + h * 6.283);

    float intensity = glow * flicker;

    ALBEDO = warm;
    EMISSION = warm * intensity * 3.8;
    // Invisible during the day, nearly opaque when lit.
    ALPHA = intensity * 0.94;
}
