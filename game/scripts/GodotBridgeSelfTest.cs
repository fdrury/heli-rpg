using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Headless verification that the engine bridge agrees with the flight model.
///
/// `tools/simlab` proves the physics is right. This proves the physics survives the trip
/// through Godot's rigid body solver and back: that the axis conversion is correct in
/// both directions, that the inertia tensor was transposed into Godot's axes properly,
/// that forces are applied at the centre of gravity, and that nothing produces a NaN.
///
/// These are exactly the bugs that are invisible until you fly and then take a day to
/// find, so they get a test that runs without a window:
///
///     godot --headless --path game -- --selftest
/// </summary>
public sealed partial class GodotBridgeSelfTest : Node
{
    private HelicopterController _heli = null!;
    private readonly List<string> _failures = new();
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private int _phase;
    private double _phaseTime;
    private Vector3 _phaseStartPos;
    private float _phaseStartHeading;

    /// <summary>Every measurement starts from the same clean hover, high above the terrain.</summary>
    private static readonly Vector3 TestStation = new(0, 900, 0);


    /// <summary>Open ground, deliberately nowhere near a site and its graded pad.</summary>
    private static readonly Vector3 DropStation = new(3100, 0, -2450);
    private float _dropGround;
    private bool _groundReady;
    private const double SettleSeconds = 14.0;
    private bool _settled;
    private float _godotStartHeading;
    private Vector3 _phaseStartRight = Vector3.Right;
    private double _peakRate;

    public GodotBridgeSelfTest(HelicopterController heli) { _heli = heli; }

    public override void _Ready()
    {
        GD.Print("=== Godot bridge self-test ===========================================");
        GD.Print($"  mass {_heli.Mass:F0} kg   inertia {_heli.Inertia}   com {_heli.CenterOfMass}");
        _heli.OverrideControls = Controls.Neutral;

        // Calm air and standard atmosphere. This measures the aircraft, not the weather.
        _heli.UseWorldWeather = false;
        _heli.Environment.SteadyWind = Vec3.Zero;
        _heli.Environment.GustIntensity = 0;
        _heli.Environment.Atmosphere.IsaDeviation = 0;
        _heli.TeleportTo(TestStation);
        _phaseStartPos = TestStation;
    }

    public override void _PhysicsProcess(double delta)
    {
        _phaseTime += delta;
        var sim = _heli.Sim;
        var t = sim.Telemetry;

        if (!_heli.GlobalPosition.IsFinite() || !_heli.LinearVelocity.IsFinite())
        {
            Fail("aircraft state went non-finite");
            Finish();
            return;
        }

        // Each measurement begins from an identical, freshly trimmed hover. Chaining
        // them instead lets the aircraft depart, and the test then measures the departure.
        if (!_settled)
        {
            var hover = new AutopilotDemand
            {
                Altitude = TestStation.Y, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0,
            };
            _heli.OverrideControls = _ap.Update(sim, hover, delta);
            if (_phaseTime > SettleSeconds)
            {
                _settled = true;
                _phaseTime = 0;
                _phaseStartPos = _heli.GlobalPosition;
                _phaseStartHeading = (float)sim.State.Orientation.Yaw;
                Vector3 n0 = -_heli.GlobalTransform.Basis.Z;
                _godotStartHeading = Mathf.Atan2(n0.X, -n0.Z);
                _phaseStartRight = _heli.GlobalTransform.Basis.X;
                _peakRate = 0;

                if (_phase == 0)
                {
                    double err = _heli.GlobalPosition.Y - TestStation.Y;
                    GD.Print($"  hover:      altitude error {err,7:F2} m, collective {sim.Actual.Collective:F3}, " +
                             $"Nr {t.RotorRpmPercent:F1}%, thrust {t.Thrust:F0} N vs weight {sim.TotalMass * 9.80665:F0} N");
                    if (Math.Abs(err) > 8) Fail($"could not hold a hover through the solver ({err:F1} m)");
                    if (t.RotorRpmPercent < 95 || t.RotorRpmPercent > 106) Fail($"Nr {t.RotorRpmPercent:F0}% in the hover");
                }
            }
            return;
        }

        switch (_phase)
        {
            case 0:
                NextPhase();
                break;

            // ---- 1: forward cyclic must move the aircraft forward ------------
            case 1:
            {
                _heli.OverrideControls = Trim(cyclicPitch: 0.22);
                if (_phaseTime > 3.5)
                {
                    Vector3 delta3 = _heli.GlobalPosition - _phaseStartPos;
                    Vector3 noseDir = -_heli.GlobalTransform.Basis.Z;
                    float along = delta3.Dot(new Vector3(noseDir.X, 0, noseDir.Z).Normalized());
                    float pitchDeg = Mathf.RadToDeg((float)sim.State.Orientation.Pitch);
                    GD.Print($"  fwd cyclic: moved {along,7:F1} m along the nose, pitch {pitchDeg:F1} deg, " +
                             $"IAS {t.AirspeedTrue * 1.94384:F0} kt");
                    if (along < 3) Fail($"forward cyclic moved the aircraft {along:F1} m forward - axis conversion is wrong");
                    if (pitchDeg > 2) Fail($"forward cyclic pitched the nose UP ({pitchDeg:F1} deg)");
                    NextPhase();
                }
                break;
            }

            // ---- 2: right cyclic must roll right ------------------------------
            // Measured as a RATE over a short window. An uncontrolled helicopter departs
            // in a couple of seconds, and once it passes 90 degrees the Euler angle
            // wraps sign and the test starts reporting the opposite of the truth.
            // 0.6 seconds at ~60 deg/s peak keeps the roll under 45 degrees,
            // well clear of gimbal lock.
            case 2:
            {
                _heli.OverrideControls = Trim(cyclicRoll: 0.22);
                _peakRate = Math.Abs(sim.State.AngularVelocity.X) > Math.Abs(_peakRate)
                            ? sim.State.AngularVelocity.X : _peakRate;
                if (_phaseTime > 0.6)
                {
                    Vector3 d = _heli.GlobalPosition - _phaseStartPos;
                    float lateral = d.Dot(new Vector3(_phaseStartRight.X, 0, _phaseStartRight.Z).Normalized());
                    float rollDeg = Mathf.RadToDeg((float)sim.State.Orientation.Roll);
                    float godotRollRate = -_heli.AngularVelocity.Dot(_heli.GlobalTransform.Basis.Z);
                    GD.Print($"  rgt cyclic: roll rate {_peakRate * 57.3,6:F1} deg/s (godot {godotRollRate * 57.3f:F1}), " +
                             $"attitude {rollDeg:F1} deg, drift {lateral:F2} m right");
                    if (_peakRate < 0.05) Fail($"right cyclic produced {_peakRate * 57.3:F1} deg/s of roll - wrong direction");
                    if (rollDeg < 2) Fail($"right cyclic reached {rollDeg:F1} deg of roll - wrong direction");
                    AgreeWithGodot("roll", sim.State.AngularVelocity.X, godotRollRate);
                    NextPhase();
                }
                break;
            }

            // ---- 3: right pedal must yaw the nose right ------------------------
            case 3:
            {
                _heli.OverrideControls = Trim(pedal: 0.45);
                _peakRate = Math.Abs(sim.State.AngularVelocity.Z) > Math.Abs(_peakRate)
                            ? sim.State.AngularVelocity.Z : _peakRate;
                if (_phaseTime > 1.2)
                {
                    float yawed = Mathf.RadToDeg((float)Airfoil.WrapPi(
                        sim.State.Orientation.Yaw - _phaseStartHeading));
                    float godotYawRate = -_heli.AngularVelocity.Y;
                    GD.Print($"  rgt pedal:  yaw rate {_peakRate * 57.3,6:F1} deg/s (godot {godotYawRate * 57.3f:F1}), " +
                             $"turned {yawed:F1} deg");
                    if (_peakRate < 0.05) Fail($"right pedal produced {_peakRate * 57.3:F1} deg/s of yaw - anti-torque sense is reversed");
                    if (yawed < 2) Fail($"right pedal turned the aircraft {yawed:F1} deg - wrong direction");
                    AgreeWithGodot("yaw", sim.State.AngularVelocity.Z, godotYawRate);
                    NextPhase();
                }
                break;
            }

            // ---- 4: engine failure must produce a survivable autorotation -----
            case 4:
            {
                if (_phaseTime < 0.05) sim.Engine.Fail();
                var demand = new AutopilotDemand
                {
                    RotorRpmFraction = 1.0,
                    ForwardSpeed = 30,
                    LateralSpeed = 0,
                    Heading = sim.State.Orientation.Yaw,
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);
                if (_phaseTime > 14)
                {
                    double rod = -sim.State.Velocity.Z;
                    GD.Print($"  autorot:    Nr {t.RotorRpmPercent:F1}%, descending {-rod * 196.85:F0} fpm, " +
                             $"freewheel {(t.Autorotating ? "open" : "driving")}");
                    if (t.RotorRpmPercent < 70) Fail($"rotor decayed to {t.RotorRpmPercent:F0}% in autorotation");
                    NextPhase();
                }
                break;
            }

            case 5:
            {
                // Hands off, through the PLAYER path.
                //
                // Every other phase drives OverrideControls, which bypasses the pilot's
                // input layer entirely - so none of them exercise the two things a player
                // actually depends on: that the stick's neutral position is the trim
                // position, and that the stability augmentation is running. Headless there
                // is no stick, which makes this exactly a hands-off release.
                _heli.OverrideControls = null;
                if (_phaseTime > 20)
                {
                    double bank = sim.State.Orientation.Roll * 57.2958;
                    GD.Print($"  hands off:  bank {bank:F1} deg after 20 s, " +
                             $"{t.AirspeedTrue * 1.94384:F0} kt, {sim.State.Altitude:F0} m, " +
                             $"sas {(sim.Sas.Enabled ? "on" : "OFF")}" +
                             $"{(sim.Sas.Saturated ? ", SATURATED" : "")}");
                    if (!sim.Sas.Enabled)
                        Fail("stability augmentation is not running on the player path");
                    if (Math.Abs(bank) > 30)
                        Fail($"left trim hands-off: {bank:F0} deg of bank inside 20 s");
                    NextPhase();
                }
                break;
            }

            case 6:
            {
                // Does terrain actually stop a body?
                //
                // This has never been tested, and it was broken the whole time. Terrain
                // collision was a HeightMapShape3D scaled by (8, 1, 8) to widen its one-unit
                // sample spacing to the chunk's 512 m, and Godot fails that in the most
                // misleading way there is: RAYCASTS hit the scaled shape at exactly the
                // right height while BODIES fall straight through. Every probe said the
                // ground was there. Nothing could stand on it.
                //
                // Landings never caught it because every site sits on a graded pad with its
                // own collision. So: drop the aircraft onto open terrain, engine off, well
                // away from anything, and require that it stops.
                if (_phaseTime < 0.05)
                {
                    sim.Engine.Fail();
                    _dropGround = (float)WorldHeight.At(DropStation.X, DropStation.Z);
                    _heli.TeleportTo(new Vector3(DropStation.X, _dropGround + 9f, DropStation.Z));
                }
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0 };

                // Hold the aircraft up until the ground beneath it has streamed in.
                //
                // This is measuring whether terrain can STOP a body, not how fast chunks
                // load - and after a teleport the ground genuinely does not exist for a
                // second or so, which is long enough for a falling aircraft to pass through
                // where it will shortly be. Worth knowing about in its own right, but it is
                // a different bug from the one this phase is for.
                if (!_groundReady)
                {
                    var space = _heli.GetWorld3D().DirectSpaceState;
                    var from = new Vector3(DropStation.X, _dropGround + 60f, DropStation.Z);
                    var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 200f);
                    q.Exclude = new Godot.Collections.Array<Rid> { _heli.GetRid() };
                    if (space.IntersectRay(q).Count > 0)
                    {
                        _groundReady = true;
                        GD.Print($"  terrain collision ready after {_phaseTime:F1} s");
                        _heli.TeleportTo(new Vector3(DropStation.X, _dropGround + 9f, DropStation.Z));
                        _phaseTime = 0;
                    }
                    else if (_phaseTime > 15) { Fail("terrain collision never appeared"); NextPhase(); }
                    break;
                }

                if (_phaseTime > 12)
                {
                    float y = _heli.GlobalPosition.Y;
                    float above = y - _dropGround;
                    GD.Print($"  terrain hit: resting {above:F1} m above ground " +
                             $"({y:F1} vs terrain {_dropGround:F1})");

                    if (above < -3f)
                        Fail($"fell THROUGH open terrain: {above:F1} m below the surface");
                    else if (above > 6f)
                        Fail($"never reached the ground: {above:F1} m above it after 12 s");
                    NextPhase();
                }
                break;
            }

            default:
                Finish();
                break;
        }
    }

    private Controls Trim(double cyclicPitch = 0, double cyclicRoll = 0, double pedal = 0)
    {
        var c = _heli.Sim.Actual;
        c.CyclicPitch += cyclicPitch;
        c.CyclicRoll += cyclicRoll;
        c.Pedal += pedal;
        c.Throttle = 1.0;
        c.ClampToRange();
        return c;
    }

    private void NextPhase()
    {
        _phase++;
        _phaseTime = 0;
        _settled = false;
        _ap.Reset();
        _heli.TeleportTo(TestStation);
    }

    /// <summary>
    /// The sim and the rigid body must say the same thing about the same instant.
    ///
    /// This is the single most valuable check in the file and it did not exist. The bridge
    /// reads the body's state into the sim every tick, so the two CANNOT legitimately
    /// disagree about a body rate - and when they do, something is wrong in a way no
    /// headless test can see, because `simlab` never runs the bridge at all.
    ///
    /// D-051 is the case in point. A blade-element change that was correct physics, improved
    /// autorotation, and passed the entire headless suite also reversed right cyclic: the
    /// sim reported -31.7 deg/s where the body reported +11.2. Both numbers were printed
    /// side by side in this very function and nothing compared them, so it was caught by a
    /// human reading the log. Now it is caught by the test.
    ///
    /// The tolerance is deliberately loose. They are sampled a frame apart and the sim
    /// integrates its own copy forward, so a few per cent of drift is normal; what is not
    /// normal is a different answer.
    /// </summary>
    private void AgreeWithGodot(string axis, double simRate, double godotRate)
    {
        double scale = Math.Max(Math.Abs(simRate), Math.Abs(godotRate));
        if (scale < 0.02) return;                       // both effectively still
        double disagreement = Math.Abs(simRate - godotRate) / scale;

        if (Math.Sign(simRate) != Math.Sign(godotRate))
            Fail($"{axis}: the sim and the rigid body disagree about the DIRECTION " +
                 $"({simRate * 57.3:F1} vs {godotRate * 57.3:F1} deg/s) - the bridge and the " +
                 "flight model are not describing the same aircraft");
        else if (disagreement > 0.35)
            Fail($"{axis}: sim {simRate * 57.3:F1} deg/s against body {godotRate * 57.3:F1} - " +
                 $"{disagreement:P0} apart, which they never are when healthy");
    }

    private void Fail(string message) { _failures.Add(message); GD.PrintErr("  FAIL  " + message); }

    private void Finish()
    {
        GD.Print("======================================================================");
        if (_failures.Count == 0)
        {
            GD.Print("  BRIDGE SELF-TEST PASSED");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"  BRIDGE SELF-TEST FAILED ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
