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
            // in a couple of seconds, and once it passes 180 degrees the Euler angle
            // wraps and the test starts reporting the opposite of the truth.
            case 2:
            {
                _heli.OverrideControls = Trim(cyclicRoll: 0.22);
                _peakRate = Math.Abs(sim.State.AngularVelocity.X) > Math.Abs(_peakRate)
                            ? sim.State.AngularVelocity.X : _peakRate;
                if (_phaseTime > 1.2)
                {
                    Vector3 d = _heli.GlobalPosition - _phaseStartPos;
                    float lateral = d.Dot(new Vector3(_phaseStartRight.X, 0, _phaseStartRight.Z).Normalized());
                    float rollDeg = Mathf.RadToDeg((float)sim.State.Orientation.Roll);
                    float godotRollRate = -_heli.AngularVelocity.Dot(_heli.GlobalTransform.Basis.Z);
                    GD.Print($"  rgt cyclic: roll rate {_peakRate * 57.3,6:F1} deg/s (godot {godotRollRate * 57.3f:F1}), " +
                             $"attitude {rollDeg:F1} deg, drift {lateral:F2} m right");
                    if (_peakRate < 0.05) Fail($"right cyclic produced {_peakRate * 57.3:F1} deg/s of roll - wrong direction");
                    if (rollDeg < 2) Fail($"right cyclic reached {rollDeg:F1} deg of roll - wrong direction");
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
