using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Fly the aircraft the way a PLAYER does, through the keyboard, and report what happened.
///
/// <para><b>The gap this closes.</b> Every headless check in this project - the bridge
/// self-test, the loop test, the save test, the combat test - drives the aircraft by setting
/// <c>HelicopterController.OverrideControls</c> directly. Not one of them goes through
/// <see cref="FlightInput"/>. So the entire path a real player's hands take - key state,
/// spring-return, trim, expo, profile bindings, the mapping into
/// <see cref="Rotorwash.Sim.Controls"/> - was covered by nothing at all, and a suite that was
/// 100% green was silent about whether the game could be flown.</para>
///
/// <para>It found out the hard way: "completely unplayable, can't control the aircraft" from
/// somebody actually holding a keyboard, against a suite with no failures in it.</para>
///
/// <para><b>How.</b> Keys are injected with <c>Input.ParseInputEvent</c>, which updates the
/// same input state <c>Input.IsKeyPressed</c> reads, so FlightInput cannot tell the
/// difference between this and a person. Nothing here reaches past the keyboard.</para>
/// </summary>
public sealed partial class PlayProbe : Node
{
    private readonly Main _main;
    private readonly HelicopterController _heli;
    private readonly PilotController _pilot;
    private readonly ChaseCamera _camera;

    private double _t;
    private int _step;
    private readonly List<string> _problems = new();

    public PlayProbe(Main main, HelicopterController heli, PilotController pilot, ChaseCamera camera)
    {
        _main = main;
        _heli = heli;
        _pilot = pilot;
        _camera = camera;
    }

    private static void Hold(Key k, bool down)
        => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });

    private readonly record struct Probe(string Name, Key Key, double Seconds,
                                         Func<Rotorwash.Sim.Controls, double> Read,
                                         double WantAtLeast);

    /// <summary>
    /// One probe per control. Each holds a key and asks whether the control the sim is
    /// ACTUALLY being handed moved in the right direction - not whether the aircraft went
    /// anywhere, which is a different question with a slower answer.
    /// </summary>
    private static readonly Probe[] Probes =
    {
        new("collective up (W)",   Key.W,     2.0, c => c.Collective,   0.05),
        new("cyclic fwd (Up)",     Key.Up,    2.0, c => -c.CyclicPitch, 0.05),
        new("cyclic right (Right)",Key.Right, 2.0, c => c.CyclicRoll,   0.05),
        new("pedal right (D)",     Key.D,     2.0, c => c.Pedal,        0.05),
    };

    private Rotorwash.Sim.Controls _before;
    private int _probe = -1;
    private Vector3 _holdFrom;
    private double _worstBank, _worstDrift, _lastLog;
    private KeyboardPilot? _hands;

    /// <summary>Put the Godot body and the sim's own copy of the state back into a clean hover.</summary>
    private void ResetToHover()
    {
        float ground = WorldHeight.At(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _heli.GlobalTransform = new Transform3D(
            Basis.Identity, new Vector3(_heli.GlobalPosition.X, ground + 150f, _heli.GlobalPosition.Z));
        _heli.LinearVelocity = Vector3.Zero;
        _heli.AngularVelocity = Vector3.Zero;

        // The datum follows the trim solution, or the hover starts with the stick already
        // displaced and this measures the recovery from that instead.
        var trim = _heli.Sim.PlaceInFlightTrimmed(ground + 150f);
        _heli.Input.SetTrim(trim.Controls.CyclicPitch, trim.Controls.CyclicRoll,
                            trim.Controls.Pedal);
        _heli.Input.SetCollectivePosition((float)trim.Controls.Collective);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        // Let the world settle and the aircraft trim before touching anything.
        if (_t < 4.0) return;

        if (_probe < 0)
        {
            GD.Print("=== play probe: the keyboard, through the real input path =========");
            GD.Print($"  profile: {_heli.Input.DeviceDescription}");
            GD.Print("    control                  before     after    moved");
            _probe = 0;
            _before = _heli.Sim.Actual;
            _step = 0;
            _t = 4.0;
            Hold(Probes[0].Key, true);
            return;
        }

        if (_probe < Probes.Length)
        {
            Probe p = Probes[_probe];
            if (_t < 4.0 + p.Seconds) return;

            Rotorwash.Sim.Controls after = _heli.Sim.Actual;
            double from = p.Read(_before), to = p.Read(after);
            double moved = to - from;
            bool ok = moved >= p.WantAtLeast;
            GD.Print($"    {p.Name,-24} {from,7:F3}   {to,7:F3}   {moved,+7:F3}  {(ok ? "" : "  NO RESPONSE")}");
            if (!ok) _problems.Add($"{p.Name} did nothing ({moved:+0.000;-0.000})");

            Hold(p.Key, false);
            _probe++;
            _t = 4.0;
            if (_probe < Probes.Length)
            {
                _before = _heli.Sim.Actual;
                Hold(Probes[_probe].Key, true);
            }
            return;
        }

        // ---- can a keyboard pilot actually hold it? --------------------------
        //
        // The question the whole suite could not ask. A proportional pilot with rate lead,
        // driving the same keys a person has - all four controls, which the first version
        // of this did not: it flew the cyclic, left the pedals alone, watched the nose run
        // away and concluded the aircraft was unflyable. See KeyboardPilot.
        if (_step == 0)
        {
            ResetToHover();
            _step = 5;
            _t = 0;
            return;
        }

        // Let the teleport actually land before measuring anything.
        //
        // ResetToHover was already here, with a comment about the Godot body and the sim
        // being two states with only one of them reset. It was right and it was still
        // not enough: writing GlobalTransform on a rigid body is a request, the physics
        // server applies it on its own schedule, and this probe started flying on the same
        // frame it asked. So the "clean hover" it measured was in fact the tail end of the
        // control-probe tumble above - full collective, then full cyclic, then full pedal -
        // and it reported that as the aircraft being unflyable, for the second time and for
        // a different reason than the first.
        //
        // Settle, then reset AGAIN from a body that has stopped moving, then fly. The
        // sweep next door does exactly this, which is why the same pilot holds the same
        // aircraft there to nine degrees and here to a hundred and fifty.
        if (_step == 5)
        {
            if (_t < 1.5) return;
            ResetToHover();
            _hands = new KeyboardPilot(_heli) { TargetAgl = 150f };
            _hands.Begin();
            _holdFrom = _heli.GlobalPosition;
            _step = 10;
            _t = 0;
            GD.Print("");
            GD.Print("  keyboard pilot holding a hover for 30 s, flying all four controls:");
            return;
        }

        if (_step == 10)
        {
            _hands!.Fly();

            if (_t - _lastLog > 5.0)
            {
                _lastLog = _t;
                float tiltNow = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
                    _heli.GlobalTransform.Basis.Y.Dot(Vector3.Up), -1f, 1f)));
                GD.Print($"    t+{_t,4:F0}s  tilt {tiltNow,5:F1}  " +
                         $"roll {_heli.Sim.State.Orientation.Roll * 57.2958,6:F1}  " +
                         $"pitch {_heli.Sim.State.Orientation.Pitch * 57.2958,6:F1}  " +
                         $"hdg err {_hands.WorstHeadingError,5:F0}  agl {_heli.HeightAgl(),6:F1}");
            }

            if (_t > 30.0)
            {
                _hands.ReleaseAll();
                _worstBank = _hands.WorstTilt;
                _worstDrift = _hands.WorstDrift;
                GD.Print($"    worst tilt      {_worstBank:F1} deg from level " +
                         $"(mean {_hands.MeanTilt:F1})");
                GD.Print($"    worst drift     {_worstDrift:F0} m");
                GD.Print($"    worst heading   {_hands.WorstHeadingError:F0} deg off " +
                         $"(mean {_hands.MeanHeadingError:F0})");
                GD.Print($"    worst altitude  {_hands.WorstAglError:F0} m off");
                GD.Print($"    still airborne  {_heli.HeightAgl() > 3}");

                if (_worstBank > 60)
                    _problems.Add($"a keyboard pilot correcting every frame still reached " +
                                  $"{_worstBank:F0} deg from level - the aircraft cannot be " +
                                  "flown from the keyboard");
                if (_worstDrift > 400)
                    _problems.Add($"drifted {_worstDrift:F0} m while trying to hold a hover");
                if (_heli.HeightAgl() <= 3)
                    _problems.Add("the aircraft ended the hover on the ground");
                _step = 20;
                _t = 4.0;
            }
            return;
        }

        // ---- and then the thing that looked like freecam ---------------------
        if (_step == 20)
        {
            GD.Print("");
            GD.Print("  dismounting:");
            Vector3 heliAt = _heli.GlobalPosition;
            _main.ForceDismount();

            Vector3 pilotAt = _pilot.GlobalPosition;
            Vector3 camAt = _camera.GlobalPosition;
            float ground = WorldHeight.At(pilotAt.X, pilotAt.Z);

            GD.Print($"    helicopter at   {heliAt}");
            GD.Print($"    pilot at        {pilotAt}   (ground here {ground:F1})");
            GD.Print($"    camera at       {camAt}   mode {_camera.Mode}");
            GD.Print($"    pilot visible   {_pilot.Visible}");
            GD.Print($"    pilot -> heli   {pilotAt.DistanceTo(heliAt):F1} m");
            GD.Print($"    camera -> pilot {camAt.DistanceTo(pilotAt):F1} m");

            if (!_pilot.Visible) _problems.Add("the pilot is invisible after dismounting");
            if (pilotAt.DistanceTo(heliAt) > 25f)
                _problems.Add($"the pilot spawned {pilotAt.DistanceTo(heliAt):F0} m from the " +
                              "aircraft - that is why the helicopter is not on screen");
            if (pilotAt.Y < ground - 2f)
                _problems.Add($"the pilot is {ground - pilotAt.Y:F1} m UNDER the ground");
            if (camAt.DistanceTo(pilotAt) > 30f)
                _problems.Add($"the camera is {camAt.DistanceTo(pilotAt):F0} m from the pilot - " +
                              "it is not following anything, which reads as freecam");
            _step = 21;
            _t = 4.0;
            return;
        }

        // A second later: has the camera actually converged on the pilot, or is it adrift?
        if (_step == 21 && _t > 5.5)
        {
            float d = _camera.GlobalPosition.DistanceTo(_pilot.GlobalPosition);
            GD.Print($"    camera -> pilot after a second: {d:F1} m");
            if (d > 30f)
                _problems.Add($"a second later the camera is still {d:F0} m away - " +
                              "third person is not tracking the pilot");

            GD.Print("");
            if (_problems.Count == 0) GD.Print("  PROBLEMS: none. It can be flown from the keyboard.");
            else foreach (string p in _problems) GD.Print($"  !! {p}");
            GD.Print("==================================================================");
            GetTree().Quit(_problems.Count == 0 ? 0 : 1);
        }
    }
}
