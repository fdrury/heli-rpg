using Godot;

namespace Rotorwash;

/// <summary>
/// The aircraft's own lights: navigation, anti-collision beacon, and a landing light.
///
/// Added the moment night became flyable, because a night with no lights on the aircraft is
/// a night where you cannot tell which way you are pointing. The nav lights are the classic
/// arrangement and they are genuinely useful rather than decorative: red on the left, green
/// on the right, white at the tail, so a glance at the reflection off the ground or a look
/// back along the boom tells you your own orientation.
///
/// On cost: only two of these are real lights. The beacon gets an omni because a flashing
/// red glow washing over the boom is the whole point of it, and the landing light is a spot
/// because it has to actually illuminate ground. The nav lights are emissive geometry with
/// no light attached at all - they are meant to be SEEN, not to light anything, and three
/// more omnis on every aircraft is a bill with nothing on the other side of it.
/// </summary>
public sealed partial class AircraftLights : Node3D
{
    private const float NoseZ = -4.30f;

    /// <summary>Landing light, toggled by the pilot.</summary>
    public bool LandingLightOn { get; private set; }

    /// <summary>Beacon and nav lights, on whenever the aircraft has power.</summary>
    public bool Powered { get; set; } = true;

    private SpotLight3D _landing = null!;
    private OmniLight3D _beaconGlow = null!;
    private MeshInstance3D _beaconLens = null!;
    private MeshInstance3D[] _nav = new MeshInstance3D[3];
    private StandardMaterial3D _beaconMat = null!;

    private double _t;
    private bool _keyHeld;
    private bool _pilotDecides;

    public override void _Ready()
    {
        // --- Anti-collision beacon, on top of the boom ------------------------
        _beaconMat = Emissive(new Color(1.0f, 0.13f, 0.10f), 6.0f);
        _beaconLens = Lens(new Vector3(0, 1.58f, NoseZ + 7.6f), 0.17f, _beaconMat);
        AddChild(_beaconLens);

        _beaconGlow = new OmniLight3D
        {
            Name = "Beacon",
            Position = new Vector3(0, 1.50f, NoseZ + 7.6f),
            LightColor = new Color(1.0f, 0.17f, 0.12f),
            OmniRange = 7.5f,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            LightEnergy = 0f,
        };
        AddChild(_beaconGlow);

        // --- Navigation lights -------------------------------------------------
        _nav[0] = Lens(new Vector3(-1.34f, -0.06f, NoseZ + 2.85f), 0.15f,
                       Emissive(new Color(1.0f, 0.12f, 0.12f), 9.0f));
        _nav[1] = Lens(new Vector3(1.34f, -0.06f, NoseZ + 2.85f), 0.15f,
                       Emissive(new Color(0.12f, 1.0f, 0.22f), 9.0f));
        _nav[2] = Lens(new Vector3(0, 0.92f, NoseZ + 11.4f), 0.13f,
                       Emissive(new Color(1.0f, 0.96f, 0.90f), 7.0f));
        foreach (MeshInstance3D m in _nav) AddChild(m);

        // --- Landing light -----------------------------------------------------
        // Under the nose, aimed down the approach path rather than straight ahead: a
        // landing light pointed at the horizon lights nothing you are about to land on.
        _landing = new SpotLight3D
        {
            Name = "LandingLight",
            Position = new Vector3(0, -1.02f, NoseZ + 0.95f),
            LightColor = new Color(1.0f, 0.97f, 0.90f),
            LightEnergy = 17.0f,
            SpotRange = 300f,
            SpotAngle = 19f,
            SpotAngleAttenuation = 0.55f,
            SpotAttenuation = 0.9f,
            ShadowEnabled = QualityTier.Current >= QualityTier.Tier.High,
            ShadowBias = 0.06f,
            Visible = false,
        };
        _landing.RotationDegrees = new Vector3(-14f, 0, 0);
        AddChild(_landing);

        // Deliberately NOT decided here. Reading the clock once at _Ready gave the wrong
        // answer every time, because the clock had not been set yet - the aircraft is built
        // before whatever decides what time it is has run. It is a per-frame rule now.
    }

    public override void _Process(double delta)
    {
        _t += delta;

        // Edge-triggered so holding the key does not strobe the light.
        bool key = Input.IsKeyPressed(Key.L);
        if (key && !_keyHeld) { LandingLightOn = !LandingLightOn; _pilotDecides = true; }
        _keyHeld = key;

        // Until the pilot touches the switch, the light follows the light outside. Anyone
        // starting a sortie in the dark would already have reached for it, and having to
        // discover a keybind before you can see the ground is not an interesting first
        // thirty seconds. The moment they do touch it, it is theirs.
        if (!_pilotDecides) LandingLightOn = SceneMood.SunNow.DaylightFraction < 0.32;

        // A real beacon is a rotating reflector, so it does not blink on and off - it
        // sweeps, and the flash has a sharp rise and a longer fall. A square wave reads as
        // a warning indicator on a dashboard; this reads as a machine.
        double phase = (_t * 1.15) % 1.0;
        float flash = (float)Mathf.Pow(Mathf.Max(0.0, 1.0 - phase * 4.2), 2.2);

        _beaconGlow.LightEnergy = Powered ? flash * 3.2f : 0f;
        _beaconGlow.Visible = _beaconGlow.LightEnergy > 0.01f;
        _beaconMat.EmissionEnergyMultiplier = Powered ? 0.4f + flash * 9.0f : 0f;
        _beaconLens.Visible = Powered;

        foreach (MeshInstance3D m in _nav) m.Visible = Powered;

        _landing.Visible = LandingLightOn && Powered;
    }

    // ------------------------------------------------------------------- helpers

    private static StandardMaterial3D Emissive(Color colour, float energy) => new()
    {
        // NOT Unshaded. An unshaded material writes its albedo straight out and ignores
        // emission completely, so the first version of these lenses rendered as flat
        // coloured dots that never exceeded 1.0 and therefore never bloomed - which is to
        // say, they were invisible at night, which is the only time they matter.
        AlbedoColor = colour * 0.15f,
        EmissionEnabled = true,
        Emission = colour,
        EmissionEnergyMultiplier = energy,
        // A nav light is a point source in reality; a two-sided lens is the cheapest way to
        // stop it vanishing at grazing angles.
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static MeshInstance3D Lens(Vector3 at, float radius, Material mat) => new()
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 8, Rings = 4 },
        Position = at,
        MaterialOverride = mat,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
