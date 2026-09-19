using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Rain, drawn where the player can see it.
///
/// The weather model has had precipitation in it since it was written and nothing ever
/// drew it, so "Rain" was a word in a debug line and a change in the fog density. This is
/// the part you can actually see out of the window.
///
/// Two things decide how this is built. Rain has to exist in **world space** while its
/// emitter follows the camera - if the particles are local to a moving emitter they travel
/// with the aircraft like a swarm of insects rather than hanging in the air being flown
/// through. And the volume is deliberately small and centred slightly ahead of the camera:
/// at 120 kt you only ever see the drops within a few tens of metres, so a bigger volume
/// spends particles where nobody is looking.
/// </summary>
public sealed partial class WeatherEffects : Node3D
{
    /// <summary>Drops in the air at full downpour. Scaled down as the rain eases.</summary>
    [Export] public int MaxDrops { get; set; } = 5400;

    /// <summary>Half-size of the box the rain lives in, metres.</summary>
    [Export] public float Volume { get; set; } = 26f;

    private GpuParticles3D _rain = null!;
    private ParticleProcessMaterial _process = null!;
    private StandardMaterial3D _dropMat = null!;
    private float _shown = -1f;

    public override void _Ready()
    {
        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(Volume, Volume * 0.6f, Volume),
            Direction = new Vector3(0, -1, 0),
            Spread = 0.0f,
            Gravity = new Vector3(0, -22f, 0),
            InitialVelocityMin = 9f,
            InitialVelocityMax = 13f,
            ScaleMin = 0.75f,
            ScaleMax = 1.4f,
            // Align the quad to the direction of travel so a drop is a streak, not a dot.
            ParticleFlagAlignY = true,
        };

        // A long thin quad. Rain reads almost entirely as streak length and near-total
        // transparency; a fat opaque drop looks like snow.
        _dropMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.77f, 0.85f, 0.16f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
            DisableReceiveShadows = true,
        };

        _rain = new GpuParticles3D
        {
            Name = "Rain",
            Amount = MaxDrops,
            Lifetime = 2.4,
            ProcessMaterial = _process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.026f, 0.72f) },
            MaterialOverride = _dropMat,
            // World space, so the drops stay where they fall while the emitter chases the
            // camera. Local coordinates would carry the whole shower along with the
            // aircraft, which looks like flying inside a jar.
            LocalCoords = false,
            Explosiveness = 0f,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_rain);
    }

    public override void _Process(double delta)
    {
        Weather.Conditions c = SceneMood.Now;
        var amount = (float)c.Precipitation;

        if (amount <= 0.01f)
        {
            if (_rain.Emitting) { _rain.Emitting = false; _rain.Visible = false; }
            return;
        }

        Camera3D? cam = GetViewport().GetCamera3D();
        if (cam is null) return;

        // Sit the volume slightly ahead of and above the viewer. Rain you fly into is worth
        // far more than rain you have already passed.
        GlobalPosition = cam.GlobalPosition
                       + (-cam.GlobalTransform.Basis.Z) * Volume * 0.35f
                       + Vector3.Up * Volume * 0.25f;

        if (!_rain.Emitting) { _rain.Emitting = true; _rain.Visible = true; }

        // Only touch the particle system when the number has actually moved. Writing
        // Amount restarts the system, so driving it straight from a continuously varying
        // weather value would reset the rain every single frame and nothing would ever be
        // drawn.
        float wanted = Mathf.Round(amount * 10f) / 10f;
        if (!Mathf.IsEqualApprox(wanted, _shown))
        {
            _shown = wanted;
            _rain.Amount = Mathf.Max(64, (int)(MaxDrops * wanted));
            _dropMat.AlbedoColor = new Color(0.72f, 0.77f, 0.85f, 0.09f + 0.15f * wanted);
        }

        // Wind blows the rain sideways, and it is the clearest visual cue the game has for
        // which way the wind is going - far more legible than a number on a HUD.
        Vec3 wind = c.WindNed;
        _process.Gravity = new Vector3((float)wind.Y * 1.8f, -22f, (float)-wind.X * 1.8f);
    }
}
