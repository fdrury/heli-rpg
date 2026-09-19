using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Lightning, thunder, and rain on the windscreen — everything that makes a storm look
/// different from heavy rain.
///
/// Lightning is managed in real time (wall-clock delta), not game time, because the game
/// clock runs at 30x. A flash that lasts 0.15 game-seconds would be a single frame at
/// 60 fps — invisible. The weather model decides IF there is a storm; this node decides
/// WHEN the bolts fall within it.
///
/// The windscreen rain is a full-screen canvas_item shader that draws procedural streaks
/// and drops on the glass, visible only in cockpit mode. It sits on the HUD canvas layer
/// so it composites on top of the 3D scene.
/// </summary>
public sealed partial class StormEffects : Node
{
    private WeatherAudio? _weatherAudio;
    private ChaseCamera? _camera;

    // Lightning state — all in real (wall-clock) seconds.
    private double _nextFlashIn;      // countdown to next strike
    private double _flashBrightness;  // current flash intensity, 0..1
    private double _flashDecay;       // how fast the current flash fades
    private readonly System.Random _rng = new(4419);

    // Windscreen rain overlay
    private ColorRect? _windscreenOverlay;
    private ShaderMaterial? _windscreenMat;

    /// <summary>Force a lightning flash right now (for screenshots).</summary>
    public void ForceFlash() => StartFlash(0.5);

    public override void _Ready()
    {
        _nextFlashIn = 2.0 + _rng.NextDouble() * 4.0;
    }

    /// <summary>Attach the windscreen rain overlay to a canvas layer.</summary>
    public void AttachWindscreen(CanvasLayer layer)
    {
        var shader = new Shader
        {
            Code = WindscreenShaderCode,
        };
        _windscreenMat = new ShaderMaterial { Shader = shader };
        _windscreenOverlay = new ColorRect
        {
            Name = "WindscreenRain",
            Material = _windscreenMat,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _windscreenOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_windscreenOverlay);
    }

    public override void _Process(double delta)
    {
        Weather.Conditions c = SceneMood.Now;
        float storm = (float)c.StormIntensity;
        bool isStorm = storm > 0.01f;

        // --- Lightning timing (real time) ------------------------------------
        if (isStorm)
        {
            _nextFlashIn -= delta;
            if (_nextFlashIn <= 0)
            {
                double intensity = 0.5 + storm * 0.5;
                StartFlash(intensity);
                // Next strike: shorter gaps in stronger storms.
                double minGap = 3.0 - storm * 1.5;   // 1.5 – 3.0 s
                double maxGap = 12.0 - storm * 6.0;   // 6.0 – 12.0 s
                _nextFlashIn = minGap + _rng.NextDouble() * (maxGap - minGap);
            }
        }
        else
        {
            // Reset timer so lightning starts promptly when a storm arrives.
            _nextFlashIn = 1.0 + _rng.NextDouble() * 3.0;
        }

        // Decay the flash.
        if (_flashBrightness > 0)
        {
            _flashBrightness -= delta * _flashDecay;
            if (_flashBrightness < 0) _flashBrightness = 0;
        }

        // --- Apply visual flash to environment brightness --------------------
        var env = GetEnvironment();
        if (env is not null)
        {
            // AdjustmentBrightness is multiplicative in Godot: 1.0 = no change.
            // SceneMoodDriver does not touch it, so the base is always 1.0.
            // Set rather than add — adding to the previous frame's value accumulates
            // and blows the scene to white in a few frames.
            float flashAdd = (float)_flashBrightness * 0.5f;
            env.AdjustmentBrightness = 1.0f + flashAdd;
        }

        // --- Windscreen rain ------------------------------------------------
        _camera ??= GetTree().Root.FindChild("Camera", true, false) as ChaseCamera;
        if (_windscreenMat is not null)
        {
            bool cockpit = _camera?.Mode == CameraMode.Cockpit;
            float precip = (float)c.Precipitation;
            // Visible when it is raining and we are in the cockpit.
            float intensity = cockpit ? precip : 0f;
            _windscreenMat.SetShaderParameter("intensity", intensity);
            _windscreenMat.SetShaderParameter("flash", (float)_flashBrightness);
        }
    }

    private void StartFlash(double intensity)
    {
        _flashBrightness = intensity;
        // Fast decay: the whole flash is over in ~0.4 real seconds.
        _flashDecay = 2.0 + _rng.NextDouble() * 1.5;

        // Trigger thunder in the audio system.
        _weatherAudio ??= GetTree().Root.FindChild("WeatherAudio", true, false) as WeatherAudio;
        _weatherAudio?.TriggerThunder(intensity);
    }

    private Godot.Environment? GetEnvironment()
    {
        var we = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        return we?.Environment;
    }

    // --------------------------------------------------------------------- shader

    private const string WindscreenShaderCode = @"
shader_type canvas_item;

// Rain on the windscreen, seen from the cockpit. Procedural streaks that run down the
// glass, with splash drops and wind-driven lateral drift. Intensity tracks precipitation;
// when it is not raining the shader outputs transparent and costs nothing.

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x),
               mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}

// A single raindrop streak: returns alpha at this UV.
float streak(vec2 uv, vec2 cell, float t) {
    float id = hash(cell);
    // Each streak has its own speed and phase.
    float speed = 0.3 + id * 0.5;
    float phase = id * 6.28;
    // Start position within the cell, biased toward top.
    float startY = fract(id * 13.7);
    float y = fract(startY + t * speed + phase);

    // The streak is a narrow vertical line, slightly curved by wind.
    vec2 cellUv = fract(uv * vec2(18.0, 1.0));
    float wobble = sin(y * 12.0 + id * 5.0) * 0.08;
    float xDist = abs(cellUv.x - 0.5 + wobble);
    float yDist = abs(fract(uv.y * 6.0) - y);

    // Thin streak with a round head.
    float head = smoothstep(0.04, 0.0, length(vec2(xDist * 4.0, yDist * 3.0)));
    float tail = smoothstep(0.018, 0.0, xDist) * smoothstep(0.15, 0.0, yDist);

    return (head * 0.7 + tail * 0.4) * step(0.02, intensity);
}

void fragment() {
    if (intensity < 0.01) {
        COLOR = vec4(0.0);
    } else {
        vec2 uv = SCREEN_UV;
        float t = TIME * 0.7;

        // Multiple layers of streaks at different scales for depth.
        float a = 0.0;
        // Large close drops.
        vec2 grid1 = floor(uv * vec2(18.0, 6.0));
        a += streak(uv, grid1, t * 1.1) * 0.7;
        // Smaller background drops.
        vec2 grid2 = floor(uv * vec2(32.0, 10.0));
        a += streak(uv * 1.7 + vec2(0.3, 0.1), grid2, t * 0.8) * 0.35;

        // Splash drops: brief circular marks where streaks land at the bottom.
        float splash = noise(uv * 40.0 + vec2(t * 3.0, -t * 8.0));
        splash = smoothstep(0.72, 0.80, splash) * 0.25;
        // Bias splashes toward lower windscreen.
        splash *= smoothstep(0.3, 0.85, uv.y);
        a += splash;

        a *= intensity;

        // Rain on glass refracts — a slight colour shift and brightness bump
        // over the streak areas, with an extra flash of white during lightning.
        vec3 col = vec3(0.75, 0.80, 0.88);
        col += vec3(flash * 0.5);

        COLOR = vec4(col, clamp(a, 0.0, 0.55));
    }
}
";
}
