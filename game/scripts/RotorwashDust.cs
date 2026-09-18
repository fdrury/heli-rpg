using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The dust the rotor kicks up, and the brownout it causes.
///
/// Two parts, because it is two things: an outward-rolling ring of dust seen from outside
/// (which is what rotorwash looks like, and what the game is named after), and a wall of
/// haze in front of the camera when you are inside it. The second is the one with teeth -
/// it arrives in the last ten metres of a landing, exactly when the pilot needs to see the
/// ground, and it is the reason a confined-area landing on loose ground is frightening.
/// </summary>
public sealed partial class RotorwashDust : Node3D
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath LandingControllerPath { get; set; } = "";

    private HelicopterController _heli = null!;
    private LandingController _landing = null!;
    private GpuParticles3D _dust = null!;
    private ColorRect? _haze;
    private ShaderMaterial? _hazeMaterial;

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
        _landing = GetNode<LandingController>(LandingControllerPath);

        _dust = BuildDust((float)_heli.Sim.Airframe.MainRotor.Radius);
        AddChild(_dust);
    }

    private static GpuParticles3D BuildDust(float rotorRadius)
    {
        var mat = new ParticleProcessMaterial
        {
            // Emit from a flat disc the size of the rotor: the dust comes off the ground
            // under the whole disc, not from a point.
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius = rotorRadius * 0.85f,
            EmissionRingInnerRadius = rotorRadius * 0.15f,
            EmissionRingHeight = 0.3f,
            EmissionRingAxis = Vector3.Up,

            Direction = new Vector3(0, 0.30f, 1),
            Spread = 24.0f,
            Gravity = new Vector3(0, 0.55f, 0),     // dust is buoyant, it does not fall

            InitialVelocityMin = 5.0f,
            InitialVelocityMax = 13.0f,
            RadialVelocityMin = 6.0f,
            RadialVelocityMax = 16.0f,

            ScaleMin = 2.2f,
            ScaleMax = 6.5f,
            Damping = new Vector2(1.6f, 3.4f),

            Color = new Color(0.60f, 0.55f, 0.44f, 0.30f),
            AngleMin = -180, AngleMax = 180,
            AngularVelocityMin = -22, AngularVelocityMax = 22,
        };

        // Fade in fast, hang, then thin out.
        var curve = new Curve();
        curve.AddPoint(new Vector2(0.0f, 0.0f));
        curve.AddPoint(new Vector2(0.18f, 1.0f));
        curve.AddPoint(new Vector2(0.55f, 0.85f));
        curve.AddPoint(new Vector2(1.0f, 0.0f));
        mat.AlphaCurve = new CurveTexture { Curve = curve };

        var scaleCurve = new Curve();
        scaleCurve.AddPoint(new Vector2(0.0f, 0.35f));
        scaleCurve.AddPoint(new Vector2(1.0f, 1.0f));
        mat.ScaleCurve = new CurveTexture { Curve = scaleCurve };

        var quad = new QuadMesh { Size = new Vector2(1, 1) };
        quad.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(0.66f, 0.61f, 0.50f, 1f),
            AlbedoTexture = SoftDisc(64),
            DisableReceiveShadows = false,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };

        return new GpuParticles3D
        {
            Name = "Dust",
            Amount = 220,
            Lifetime = 3.2,
            Explosiveness = 0.0f,
            ProcessMaterial = mat,
            DrawPass1 = quad,
            Emitting = false,
            LocalCoords = false,   // dust stays where it was made, it does not follow you
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
        };
    }

    /// <summary>A soft round blob, generated rather than imported so it matches the palette.</summary>
    private static ImageTexture SoftDisc(int size)
    {
        var img = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x - c, y - c).Length() / c;
                float a = Mathf.Clamp(1.0f - d, 0, 1);
                a = a * a * (3 - 2 * a);          // smoothstep
                img.SetPixel(x, y, new Color(1, 1, 1, a * 0.85f));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Attach the screen haze to a canvas layer. Optional - the world dust works alone.</summary>
    public void AttachHaze(CanvasLayer layer)
    {
        var shader = new Shader
        {
            Code = @"
shader_type canvas_item;

// Brownout seen from inside it. Not a flat colour wash: a moving, grainy, directional
// haze that eats contrast from the edges inward, because what actually kills you in a
// brownout is losing your peripheral cues while the patch straight ahead still looks fine.

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dust_colour : source_color = vec3(0.58, 0.52, 0.41);
uniform float time_scale = 0.55;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(41.7, 289.1))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x),
               mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}

void fragment() {
    if (intensity < 0.003) { COLOR = vec4(0.0); }
    else {
        vec2 uv = SCREEN_UV;
        float t = TIME * time_scale;

        // Streaming, swirling dust.
        float n = noise(uv * 5.0 + vec2(t * 1.3, -t * 0.7)) * 0.55
                + noise(uv * 11.0 - vec2(t * 0.9, t * 1.6)) * 0.30
                + noise(uv * 23.0 + vec2(t * 2.1, t * 0.4)) * 0.15;

        // Closes in from the edges. The last thing to go is straight ahead.
        float vign = smoothstep(0.15, 0.95, length(uv - vec2(0.5)) * 1.55);
        float a = intensity * (0.42 + n * 0.72) * (0.55 + vign * 0.95);

        COLOR = vec4(dust_colour * (0.85 + n * 0.30), clamp(a, 0.0, 0.97));
    }
}",
        };

        _hazeMaterial = new ShaderMaterial { Shader = shader };
        _haze = new ColorRect
        {
            Name = "BrownoutHaze",
            Material = _hazeMaterial,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _haze.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_haze);
    }

    public override void _Process(double delta)
    {
        float b = _landing.Brownout;

        // Park the emitter on the ground under the aircraft, not on the aircraft: the
        // dust comes from the surface, and it should stay put when you translate away.
        Vector3 p = _heli.GlobalPosition;
        GlobalPosition = new Vector3(p.X, WorldHeight.At(p.X, p.Z) + 0.35f, p.Z);

        _dust.Emitting = b > 0.02f;
        _dust.AmountRatio = Mathf.Clamp(b * 1.15f, 0.05f, 1f);

        if (_hazeMaterial is not null)
        {
            // The haze only bites when the CAMERA is inside the cloud. From the cockpit
            // that is always; from an external view it depends on where the camera is,
            // and washing the whole screen while watching from fifty metres away is both
            // wrong and unreadable.
            float cameraFactor = 0f;
            Camera3D? cam = GetViewport().GetCamera3D();
            if (cam is not null)
            {
                Vector3 cp = cam.GlobalPosition;
                float radial = new Vector2(cp.X - GlobalPosition.X, cp.Z - GlobalPosition.Z).Length();
                float above = cp.Y - GlobalPosition.Y;
                float rotorR = (float)_heli.Sim.Airframe.MainRotor.Radius;
                // Inside the cloud: within about three rotor radii and low down.
                float inRadius = 1f - Mathf.Clamp((radial - rotorR * 1.2f) / (rotorR * 2.2f), 0, 1);
                float inHeight = 1f - Mathf.Clamp((above - 3f) / (rotorR * 1.6f), 0, 1);
                cameraFactor = inRadius * inHeight;
            }
            _hazeMaterial.SetShaderParameter("intensity", Mathf.Clamp(b * 0.92f * cameraFactor, 0, 1));
        }
    }
}
