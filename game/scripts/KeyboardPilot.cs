using System;
using Godot;

namespace Rotorwash;

/// <summary>
/// A person's hands on the keyboard, trying to hold a hover.
///
/// <para><b>Why this is shared.</b> <see cref="PlayProbe"/> grew one of these to answer
/// "can the game be flown", and it flew the cyclic only. It then reported, correctly for
/// what it measured and wrongly for what it claimed, that the aircraft could not be flown
/// from the keyboard at all - 156 degrees from level and into the ground. It was not
/// touching the pedals. An unattended pedal in a helicopter means the nose runs away, and
/// once the nose has gone the cyclic trim is wrong too and the rest follows.</para>
///
/// <para>The same pilot, flying all four controls, holds the same aircraft to nine degrees.
/// So the two harnesses now share one pilot: a probe and a sweep that disagree about how
/// to fly are measuring different aircraft, and the difference will be discovered the slow
/// way.</para>
///
/// <para><b>It flies rates, not angles.</b> Bang-bang on attitude alone drives a textbook
/// pilot-induced oscillation and reports the controller's problem as the aircraft's.
/// Anticipating where the attitude is going - angle plus about a second of current rate -
/// is what a person does without thinking, and is the minimum needed to judge whether a
/// control is usable at all.</para>
/// </summary>
public sealed class KeyboardPilot
{
    private const double Deg = 57.2958;

    private readonly HelicopterController _heli;

    private Key _heldRoll = Key.None, _heldPitch = Key.None;
    private Key _heldColl = Key.None, _heldPedal = Key.None;

    /// <summary>Height above ground the pilot is trying to hold, in metres.</summary>
    public float TargetAgl { get; set; } = 150f;

    /// <summary>Heading the pilot is trying to hold, in radians.</summary>
    public float TargetHeading { get; set; }

    // --- what the flight looked like ---------------------------------------
    public double WorstTilt { get; private set; }
    public double WorstDrift { get; private set; }
    public double WorstAglError { get; private set; }
    public double WorstHeadingError { get; private set; }
    public double MeanTilt => _samples > 0 ? _sumTilt / _samples : 0;
    public double MeanHeadingError => _samples > 0 ? _sumHeading / _samples : 0;

    private double _sumTilt, _sumHeading;
    private int _samples;
    private Vector3 _holdFrom;

    public KeyboardPilot(HelicopterController heli) => _heli = heli;

    private static void Send(Key k, bool down)
        => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });

    private static void SetHeld(ref Key held, Key want)
    {
        if (held == want) return;
        if (held != Key.None) Send(held, false);
        if (want != Key.None) Send(want, true);
        held = want;
    }

    /// <summary>Let go of everything. Always call this between flights.</summary>
    public void ReleaseAll()
    {
        SetHeld(ref _heldRoll, Key.None);
        SetHeld(ref _heldPitch, Key.None);
        SetHeld(ref _heldColl, Key.None);
        SetHeld(ref _heldPedal, Key.None);
    }

    /// <summary>Take the current position and heading as what we are holding, and reset the log.</summary>
    public void Begin()
    {
        _holdFrom = _heli.GlobalPosition;
        TargetHeading = _heli.GlobalTransform.Basis.GetEuler().Y;
        WorstTilt = WorstDrift = WorstAglError = WorstHeadingError = 0;
        _sumTilt = _sumHeading = 0;
        _samples = 0;
    }

    /// <summary>One frame of flying, through the real keyboard path and nothing else.</summary>
    public void Fly()
    {
        var sim = _heli.Sim;

        double rollDeg = sim.State.Orientation.Roll * Deg;
        double pitchDeg = sim.State.Orientation.Pitch * Deg;
        double rollLead = rollDeg + sim.State.AngularVelocity.X * Deg * 0.9;
        double pitchLead = pitchDeg + sim.State.AngularVelocity.Y * Deg * 0.9;

        // Positive cyclic roll gives a positive roll rate, so an excursion is corrected
        // with the opposite key. Up arrow is aft cyclic and raises the nose, so a nose-up
        // excursion is corrected with Down. Both signs measured, not assumed - see the
        // derivatives quoted in Stability.
        SetHeld(ref _heldRoll, rollLead > 2 ? Key.Left : rollLead < -2 ? Key.Right : Key.None);
        SetHeld(ref _heldPitch, pitchLead > 2 ? Key.Down : pitchLead < -2 ? Key.Up : Key.None);

        float agl = _heli.HeightAgl();
        double aglError = agl - TargetAgl;
        double aglLead = aglError + _heli.LinearVelocity.Y * 2.0;
        SetHeld(ref _heldColl, aglLead < -3 ? Key.W : aglLead > 3 ? Key.S : Key.None);

        float heading = _heli.GlobalTransform.Basis.GetEuler().Y;
        double hdgError = Mathf.Wrap(heading - TargetHeading, -Mathf.Pi, Mathf.Pi) * Deg;
        double hdgLead = hdgError + sim.State.AngularVelocity.Z * Deg * 0.5;
        SetHeld(ref _heldPedal, hdgLead > 4 ? Key.D : hdgLead < -4 ? Key.A : Key.None);

        float tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
            _heli.GlobalTransform.Basis.Y.Dot(Vector3.Up), -1f, 1f)));
        WorstTilt = Math.Max(WorstTilt, tilt);

        var flat = new Vector2(_heli.GlobalPosition.X - _holdFrom.X,
                               _heli.GlobalPosition.Z - _holdFrom.Z);
        WorstDrift = Math.Max(WorstDrift, flat.Length());
        WorstAglError = Math.Max(WorstAglError, Math.Abs(aglError));
        WorstHeadingError = Math.Max(WorstHeadingError, Math.Abs(hdgError));

        _sumTilt += tilt;
        _sumHeading += Math.Abs(hdgError);
        _samples++;
    }
}
