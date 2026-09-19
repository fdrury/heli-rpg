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
    private StandardMaterial3D _panelMat = null!;
    private OmniLight3D _panelFlood = null!;
    private readonly OmniLight3D[] _fill = new OmniLight3D[2];
    private readonly System.Collections.Generic.List<MeshInstance3D> _panel = new();

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

        // --- Cockpit skylight fill ---------------------------------------------
        // Standing in for the sky bounce the renderer does not model at this quality tier.
        // It belongs here rather than in the airframe because it has to DIM: it represents
        // daylight finding its way in through the glass, and at night there is none. Built
        // as part of the airframe it stayed at full strength after dark, and the cockpit
        // sat brightly lit inside a black world.
        _fill[0] = Fill("FillLower", new Vector3(0, 0.15f, NoseZ + 2.00f), 3.6f);
        _fill[1] = Fill("FillUpper", new Vector3(0, 0.62f, NoseZ + 1.55f), 2.8f);
        foreach (OmniLight3D f in _fill) AddChild(f);

        // --- Instrument faces and panel flood ----------------------------------
        // Dials you can read in the dark, and a wash of warm light over the console. The
        // colour is the traditional one for a reason: it is dim enough not to wreck night
        // vision and it reads instantly as "instrument" rather than "warning".
        _panelMat = Emissive(new Color(1.0f, 0.52f, 0.20f), 1.0f);
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.62f : 0.62f;
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    var face = new MeshInstance3D
                    {
                        Mesh = new QuadMesh { Size = new Vector2(0.115f, 0.115f) },
                        Position = new Vector3(x - 0.20f + c * 0.20f, -0.34f - r * 0.22f, NoseZ + 1.23f),
                        MaterialOverride = _panelMat,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    // Quads face +Z by default, which is aft - toward the pilot. Correct.
                    _panel.Add(face);
                    AddChild(face);
                }
            }
        }

        _panelFlood = new OmniLight3D
        {
            Name = "PanelFlood",
            Position = new Vector3(0, -0.02f, NoseZ + 1.55f),
            LightColor = new Color(1.0f, 0.58f, 0.26f),
            OmniRange = 1.9f,
            OmniAttenuation = 1.8f,
            ShadowEnabled = false,
            LightEnergy = 0f,
        };
        AddChild(_panelFlood);

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

        // Panel lighting follows the outside light, the way a pilot would turn it up as
        // the day goes. Full dark is not full brightness: instrument lighting that outshines
        // the world outside is how you lose the horizon.
        float day = (float)SceneMood.SunNow.DaylightFraction;
        float dark = 1f - day;

        // Skylight fill follows the daylight, with a small floor so the interior never goes
        // completely black even with the panel lights off.
        _fill[0].LightEnergy = 0.10f + 1.40f * day;
        _fill[1].LightEnergy = 0.06f + 1.04f * day;
        float lit = Powered ? Mathf.SmoothStep(0f, 1f, dark) : 0f;
        // Restrained. Instrument lighting that outshines the world is how you lose the
        // horizon, and the first attempt blew the glareshield to white.
        _panelMat.EmissionEnergyMultiplier = 0.10f + lit * 0.85f;
        _panelFlood.LightEnergy = lit * 0.30f;
        _panelFlood.Visible = _panelFlood.LightEnergy > 0.01f;
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

    private static OmniLight3D Fill(string name, Vector3 at, float range) => new()
    {
        Name = name,
        Position = at,
        LightColor = new Color(0.80f, 0.84f, 0.92f),
        LightEnergy = 0f,
        OmniRange = range,
        OmniAttenuation = 1.1f,
        ShadowEnabled = false,
        LightSpecular = 0.25f,
    };

    private static MeshInstance3D Lens(Vector3 at, float radius, Material mat) => new()
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 8, Rings = 4 },
        Position = at,
        MaterialOverride = mat,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
