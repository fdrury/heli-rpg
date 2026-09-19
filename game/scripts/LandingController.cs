using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Watches the aircraft near the ground: assesses where it is about to be put down,
/// detects the touchdown and grades it, spots rotor strikes, and drives the brownout.
///
/// The flight model already knows everything needed; this class is the part that decides
/// what it costs. Landing is where the game charges the player (D-003a), so it needs to be
/// legible: the assessment is shown while there is still time to go around, and the
/// verdict is stated plainly afterwards.
/// </summary>
public sealed partial class LandingController : Node
{
    [Export] public NodePath HelicopterPath { get; set; } = "";

    private HelicopterController _heli = null!;
    private bool _wasAirborne = true;
    private double _timeOnGround;
    private double _peakDescentRate;

    /// <summary>Current dust/spray intensity, 0..1. Read by the camera and the post effect.</summary>
    public float Brownout { get; private set; }

    /// <summary>Assessment of the ground directly below. Updated continuously below 60 m.</summary>
    public LandingSite Site { get; private set; }

    /// <summary>True while the skids are carrying weight.</summary>
    public bool OnGround { get; private set; }

    /// <summary>The last touchdown, for the HUD to report.</summary>
    public TouchdownReport LastTouchdown { get; private set; }
    public double LastTouchdownTime { get; private set; } = -999;

    public event Action<TouchdownReport>? Touchdown;
    public event Action<string>? RotorStrike;

    /// <summary>Latch, so one impact is one strike rather than one per frame.</summary>
    private bool _rotorStruck;

    private double _now;

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
    }

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        var sim = _heli.Sim;
        float agl = _heli.HeightAgl();

        // --- Site assessment --------------------------------------------------
        if (agl < 80f) Site = AssessBelow();

        // --- Brownout ---------------------------------------------------------
        double downwash = sim.Rotor.Inflow.Lambda0 * sim.RotorOmega * sim.Airframe.MainRotor.Radius * 2.0;
        double groundSpeed = new Vector2(_heli.LinearVelocity.X, _heli.LinearVelocity.Z).Length();
        float target = (float)Landing.Brownout(Site.Surface, agl, sim.Airframe.MainRotor.Radius,
                                               downwash, groundSpeed);
        // Dust takes a second or two to build and rather longer to settle.
        float rate = target > Brownout ? 1.4f : 0.5f;
        Brownout = Mathf.Lerp(Brownout, target, Mathf.Clamp((float)delta * rate * 3f, 0, 1));

        // --- Rotor strike -----------------------------------------------------
        // Latched. Striking the ground is an EVENT - the rotor is destroyed and that is
        // the end of the story - but the geometric test that detects it stays true for as
        // long as the wreck lies there, so an unlatched check re-fires every frame. The
        // bridge self-test's drop phase logged eleven hundred identical strikes from one
        // impact, each one re-applying full damage and each one worth a journal entry.
        //
        // It re-arms only once the disc is clear of the ground again, so a second strike
        // on a second bounce is still a second strike.
        string what = string.Empty;
        bool striking = agl < sim.Airframe.MainRotor.Radius * 1.5f && CheckRotorStrike(out what);
        if (striking && !_rotorStruck)
        {
            _rotorStruck = true;
            sim.Damage.Apply(Component.MainRotor, 0.85, DamageCause.RotorStrike, what);
            sim.Damage.Apply(Component.Transmission, 0.35, DamageCause.RotorStrike, what);
            RotorStrike?.Invoke(what);
        }
        else if (!striking && agl > sim.Airframe.MainRotor.Radius * 1.8f)
        {
            _rotorStruck = false;
        }

        // --- Touchdown --------------------------------------------------------
        bool contact = _heli.GetContactCount() > 0;
        double descentRate = -_heli.LinearVelocity.Y;
        if (!contact) _peakDescentRate = Math.Max(0, descentRate);

        if (contact && _wasAirborne)
        {
            var report = Landing.Evaluate(
                sim.Airframe, sim.CentreOfGravity,
                Math.Max(_peakDescentRate, descentRate),
                groundSpeed,
                sim.State.Orientation.Roll,
                sim.State.Orientation.Pitch,
                Mathf.DegToRad((float)Site.SlopeDegrees),
                rotorStruck: false);

            ApplyTouchdownDamage(report);
            LastTouchdown = report;
            LastTouchdownTime = _now;
            Touchdown?.Invoke(report);
        }

        OnGround = contact;
        _wasAirborne = !contact;
        if (contact) _timeOnGround += delta; else _timeOnGround = 0;
    }

    private void ApplyTouchdownDamage(TouchdownReport r)
    {
        var d = _heli.Sim.Damage;
        if (r.StructuralDamage <= 0.001) return;

        d.Apply(Component.Skids, r.StructuralDamage, r.Rollover ? DamageCause.Rollover : DamageCause.HardLanding, r.Summary);

        // A heavy arrival does not stop at the skids. The transmission takes the shock
        // through the mast, and that is the expensive part to replace.
        if (r.StructuralDamage > 0.3)
        {
            d.Apply(Component.Transmission, r.StructuralDamage * 0.35, DamageCause.HardLanding, r.Summary);
            d.Apply(Component.Fuselage, r.StructuralDamage * 0.45, DamageCause.HardLanding, r.Summary);
        }
        if (r.Rollover)
        {
            d.Apply(Component.MainRotor, 0.95, DamageCause.Rollover, "blades into the ground");
            d.Apply(Component.TailRotor, 0.7, DamageCause.Rollover, r.Summary);
        }
    }

    /// <summary>
    /// Sample the terrain around the rotor disc. A strike happens when the ground rises
    /// above the plane the blades are sweeping - which is why you never approach a rising
    /// slope with the tail low, and why a confined area with one tall tree is a trap.
    /// </summary>
    private bool CheckRotorStrike(out string what)
    {
        what = "";
        var sim = _heli.Sim;
        var cfg = sim.Airframe.MainRotor;

        Transform3D t = _heli.GlobalTransform;
        Vector3 hub = t * SimBridge.ToGodot(cfg.HubPosition - sim.CentreOfGravity) + t.Origin - t.Origin;
        hub = t * SimBridge.ToGodot(cfg.HubPosition - sim.CentreOfGravity);

        // The disc plane follows the aircraft attitude plus the flapping the sim reports.
        Vector3 discUp = t.Basis * SimBridge.ToGodot(-sim.Rotor.ShaftAxisDown);
        float radius = (float)cfg.Radius;

        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            Vector3 radial = (t.Basis.X * Mathf.Cos(a) + (-t.Basis.Z) * Mathf.Sin(a)).Normalized();
            // Project the radial into the disc plane so a banked rotor is tested where it
            // actually is rather than where it would be if the aircraft were level.
            radial = (radial - discUp * radial.Dot(discUp)).Normalized();
            Vector3 tip = hub + radial * radius;

            float ground = WorldHeight.At(tip.X, tip.Z);
            if (tip.Y < ground)
            {
                what = $"blade into terrain at {Mathf.RadToDeg(a):F0} deg azimuth";
                return true;
            }
        }
        return false;
    }

    /// <summary>Assess the ground under the skids: slope, roughness, clearance and surface.</summary>
    private LandingSite AssessBelow()
    {
        Vector3 p = _heli.GlobalPosition;
        float rotorR = (float)_heli.Sim.Airframe.MainRotor.Radius;

        // Slope and roughness over the skid footprint, not a single point: a rock under
        // one skid is what actually ends the landing.
        const float footprint = 2.6f;
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            float h = WorldHeight.At(p.X + Mathf.Cos(a) * footprint, p.Z + Mathf.Sin(a) * footprint);
            minH = Mathf.Min(minH, h);
            maxH = Mathf.Max(maxH, h);
        }
        float centre = WorldHeight.At(p.X, p.Z);
        Vector3 n = WorldHeight.NormalAt(p.X, p.Z, 2.5f);
        float slopeDeg = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Y, -1, 1)));
        float roughness = (maxH - minH) - Mathf.Abs(maxH - minH) * Mathf.Cos(Mathf.DegToRad(slopeDeg));

        // Rotor clearance: how far out can the disc go before the terrain comes up to meet it.
        float clearance = rotorR * 2.2f;
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            for (float r = rotorR * 0.6f; r < rotorR * 2.2f; r += rotorR * 0.2f)
            {
                float h = WorldHeight.At(p.X + Mathf.Cos(a) * r, p.Z + Mathf.Sin(a) * r);
                // Anything more than about a rotor-hub height above the skids is in the way.
                if (h > centre + 2.4f) { clearance = Mathf.Min(clearance, r); break; }
            }
        }

        return new LandingSite(slopeDeg, Mathf.Max(0, roughness), clearance,
                               SurfaceAt(p.X, p.Z, centre, slopeDeg), _heli.HeightAgl());
    }

    /// <summary>
    /// What the ground is made of, derived from the same rules the terrain shader blends
    /// with, so what the player sees is what the dust and the skids get.
    /// </summary>
    public static SurfaceKind SurfaceAt(float x, float z, float height, float slopeDeg)
    {
        // Water first, and before the slope test, because a submerged bank is still water
        // however steep it is. This is the one surface that is not a landing: Landing
        // scores it zero whatever the attitude and reports the verdict as "water", and the
        // brownout model turns it into spray, which blinds a pilot exactly as well as dust
        // does. The world now has rivers, a drowned wetland and an ocean around the whole
        // island, so this is no longer a case that cannot happen.
        if (height < WorldHeight.WaterLevel) return SurfaceKind.Water;
        // The strand: wet silt and shingle where the water has just been. Not dusty -
        // wet ground is the one place a helicopter does not brown itself out - but not
        // ground you want to sit a skid on either.
        if (height < WorldHeight.WaterLevel + 2.5f) return SurfaceKind.Hardpack;
        if (slopeDeg > 34f) return SurfaceKind.Rock;
        if (height > 150f) return SurfaceKind.Loose;      // bare high ground, gravel
        if (height < 2f) return SurfaceKind.Hardpack;     // dry basin floor
        return SurfaceKind.Grass;
    }
}
