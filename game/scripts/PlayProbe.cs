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
    private Key _heldRoll = Key.None, _heldPitch = Key.None;

    /// <summary>Press a key and release whatever was held for that axis. Real input events.</summary>
    private static void SetHeld(ref Key held, Key want)
    {
        if (held == want) return;
        if (held != Key.None) Hold(held, false);
        if (want != Key.None) Hold(want, true);
        held = want;
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
        // The question the whole suite could not ask. A simple proportional pilot, driving
        // the same four KEYS a person has, trying to keep the aircraft level and where it
        // is. If this cannot hold it, neither can anybody, and no amount of green tests
        // elsewhere means the game can be flown.
        if (_step == 0)
        {
            // Put the aircraft back to a clean hover FIRST - and that means the Godot rigid
            // body, not just the sim.
            //
            // PlaceInFlightTrimmed resets the sim's own copy of the state and nothing else,
            // so the first version of this started its "hover hold" with the Godot body
            // still tumbling from the control probes above (which hold full collective,
            // then full cyclic, then full pedal, one after another). It was face down at
            // 2.3 m before the measurement began, and then faithfully reported that a
            // keyboard pilot could not hold a hover. Two states, one of them reset.
            float ground = WorldHeight.At(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
            _heli.GlobalTransform = new Transform3D(
                Basis.Identity, new Vector3(_heli.GlobalPosition.X, ground + 150f, _heli.GlobalPosition.Z));
            _heli.LinearVelocity = Vector3.Zero;
            _heli.AngularVelocity = Vector3.Zero;
            _heli.Sim.PlaceInFlightTrimmed(ground + 150f);

            _holdFrom = _heli.GlobalPosition;
            _step = 10;
            _t = 0;
            GD.Print("");
            GD.Print("  keyboard pilot holding a hover for 30 s:");
            return;
        }

        if (_step == 10)
        {
            var sim = _heli.Sim;
            double rollDeg = sim.State.Orientation.Roll * 57.2958;
            double pitchDeg = sim.State.Orientation.Pitch * 57.2958;

            // A pilot flies the RATE, not the angle.
            //
            // The first version of this pilot was bang-bang on attitude alone, and against
            // a responsive control it did what bang-bang always does: drove a textbook
            // pilot-induced oscillation and reported 180 degrees of bank, which says
            // something about the controller and nothing about the aircraft. Anticipating
            // where the attitude is GOING - angle plus a second or so of current rate - is
            // what a person does without thinking about it, and is the minimum needed to
            // judge whether a control is usable.
            double rollRate = sim.State.AngularVelocity.X * 57.2958;
            double pitchRate = sim.State.AngularVelocity.Y * 57.2958;
            double rollLead = rollDeg + rollRate * 0.9;
            double pitchLead = pitchDeg + pitchRate * 0.9;

            // Positive cyclic rolls LEFT in this sim (Stability.RollSign, measured), so a
            // right bank is corrected by pressing Right.
            Key rollKey = rollLead > 2 ? Key.Left : rollLead < -2 ? Key.Right : Key.None;
            // Positive pitch is NOSE UP (the bridge self-test asserts forward cyclic gives
            // a negative pitch), and Up arrow is aft cyclic, which raises the nose further.
            // So a nose-up excursion is corrected with DOWN. Had this backwards first time
            // and the probe dutifully flew the aircraft into the ground, then reported that
            // the aircraft could not be flown.
            Key pitchKey = pitchLead > 2 ? Key.Down : pitchLead < -2 ? Key.Up : Key.None;
            SetHeld(ref _heldRoll, rollKey);
            SetHeld(ref _heldPitch, pitchKey);

            // Tilt from vertical, not Euler roll. Euler roll WRAPS to 180 the moment pitch
            // passes 90, so the first two runs of this probe reported "176 degrees of bank"
            // for what was actually a pitch excursion - the exact trap the bridge self-test
            // warns about in its own comments, walked into twice in ten minutes. The angle
            // between the aircraft's own up vector and the world's cannot wrap and is what
            // "did it stay upright" actually means.
            float tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
                _heli.GlobalTransform.Basis.Y.Dot(Vector3.Up), -1f, 1f)));
            _worstBank = Math.Max(_worstBank, tilt);

            if (_t - _lastLog > 5.0)
            {
                _lastLog = _t;
                GD.Print($"    t+{_t,4:F0}s  tilt {tilt,5:F1}  roll {rollDeg,6:F1}  " +
                         $"pitch {pitchDeg,6:F1}  agl {_heli.HeightAgl(),6:F1}");
            }
            var flat = new Vector2(_heli.GlobalPosition.X - _holdFrom.X,
                                   _heli.GlobalPosition.Z - _holdFrom.Z);
            _worstDrift = Math.Max(_worstDrift, flat.Length());

            if (_t > 30.0)
            {
                SetHeld(ref _heldRoll, Key.None);
                SetHeld(ref _heldPitch, Key.None);
                GD.Print($"    worst tilt      {_worstBank:F1} deg from level");
                GD.Print($"    worst drift     {_worstDrift:F0} m");
                GD.Print($"    still airborne  {_heli.HeightAgl() > 3}");

                if (_worstBank > 60)
                    _problems.Add($"a keyboard pilot correcting every frame still reached " +
                                  $"{_worstBank:F0} deg from level - the aircraft cannot be " +
                                  "flown from the keyboard");
                if (_worstDrift > 400)
                    _problems.Add($"drifted {_worstDrift:F0} m while trying to hold a hover");
                _step = 0;
                _t = 4.0;
                _step = 20;
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
