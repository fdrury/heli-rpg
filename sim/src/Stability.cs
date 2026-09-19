using System;

namespace Rotorwash.Sim;

/// <summary>
/// A limited-authority stability augmentation system, of the kind bolted onto every
/// helicopter that people actually have to fly for a living.
///
/// The bare airframe is unstable, and correctly so - see <see cref="Trim"/> for why the
/// trim solver had to come first. But "correct" and "flyable" are different bars. Released
/// at a perfect trim, this airframe holds for about two seconds, then a small nose drop
/// builds airspeed, the pedal that was right in the hover is a third of a travel wrong at
/// 40 kt, the nose runs away, and the spiral takes it from there. Every one of those steps
/// is real. The result is an aircraft that demands continuous correction on three axes,
/// and continuous correction through a lagged actuator is how pilot-induced oscillation
/// happens.
///
/// The real world solved this in the 1950s and the answer was not "fly better". It was a
/// series actuator that feeds in a few per cent of control against the body rates, faster
/// than a person can react and without moving the stick. That is what this is.
///
/// Three properties matter, and they are what keep this honest rather than a cheat:
///
///   * It damps RATES, not position. It will not hold an attitude, fly to a heading, or
///     stop a deliberate manoeuvre - push and it gets out of the way, because the pilot's
///     command is rate too.
///   * Its authority is LIMITED, to a slice of total travel. Past that the aircraft is
///     exactly as unstable as it ever was, which is what makes running out of authority a
///     real event rather than an invisible one.
///   * It can be switched off, and the difference is dramatic. Flying it degraded is a
///     thing the game can ask of the player.
///
/// Reversible: delete the call in <see cref="Helicopter"/> and the airframe is bare again.
/// </summary>
/// <summary>How much the aircraft helps its pilot. See <see cref="Stability.Set"/>.</summary>
public enum AssistLevel
{
    /// <summary>Nothing. The bare airframe, which is a genuinely difficult aircraft.</summary>
    Off,
    /// <summary>Rate damping only, on a short leash. Takes the edge off without flying for you.</summary>
    Light,
    /// <summary>Rate damping, weak levelling and heading hold. The default.</summary>
    Standard,
    /// <summary>Strong levelling and generous authority: it will hold an attitude for you.</summary>
    Full,
}

public sealed class Stability
{
    /// <summary>
    /// Pick an assist level.
    ///
    /// A ladder rather than a switch, because "on or off" is the wrong shape for this. The
    /// bare airframe leaves trim in about six seconds, which is a specialist aircraft; full
    /// augmentation holds for half a minute, which is a different game. Everything between
    /// is where most people actually want to be, and the parts were all already here - only
    /// the presets were missing.
    /// </summary>
    public void Set(AssistLevel level)
    {
        Level = level;
        switch (level)
        {
            case AssistLevel.Off:
                Enabled = false;
                break;

            case AssistLevel.Light:
                // Rates only. It will stop a wobble turning into an attitude and do nothing
                // else - no levelling, no heading hold - so the pilot still flies it.
                // Gains scaled well down to match the shorter leash. D-021's sweep is the
                // authority here: at low authority a high gain saturates, the damper turns
                // bang-bang, and MORE gain makes things worse. The first guess at this rung
                // used Standard-ish gains on a 0.20 leash and came out worse than no
                // augmentation at all.
                // Rate damping plus a light hand on heading, and NO attitude levelling -
                // it will not hold your bank for you, which is what keeps this rung honest.
                //
                // The heading term is not a compromise, it is the point. Pure rate damping
                // measured 6.6 s hands-off against the bare airframe's 6.5, because the
                // departure is driven by the pedal trim changing with speed: damping a yaw
                // rate slows that down without ever correcting it. Yaw is also the axis
                // humans manage worst, which is why real rate-damping systems almost always
                // include a heading element.
                Enabled = true;
                Authority = 0.26;
                RollGain = 0.48; PitchGain = 0.54; YawGain = 0.76;
                AttitudeGain = 0.0; HeadingGain = 0.40;
                break;

            case AssistLevel.Standard:
                Enabled = true;
                Authority = 0.30;
                RollGain = 0.85; PitchGain = 0.95; YawGain = 1.35;
                AttitudeGain = 0.30; HeadingGain = 0.55;
                break;

            case AssistLevel.Full:
                // Close to an attitude-command system. Still limited authority, so it can
                // still be flown past - that property is not negotiable (D-021).
                // More authority and more damping, but only a little more ATTITUDE gain.
                // A strong attitude term fights the actuator lag and hunts: at 0.95 this
                // rung held worse than Standard, which is the same lesson from the other
                // end of the ladder.
                // Note what does NOT change: the rate gains. Standard's are already at the
                // edge of what the actuator lag will carry - pushing them to 1.05 with more
                // authority dropped this rung to 2.6 s, worse than the bare airframe. What
                // Full buys is HEADROOM before saturation, and a firmer hand on attitude.
                Enabled = true;
                Authority = 0.40;
                RollGain = 0.85; PitchGain = 0.95; YawGain = 1.35;
                AttitudeGain = 0.55; HeadingGain = 0.85;
                break;
        }
    }

    /// <summary>The level last selected. Informational; the gains are the truth.</summary>
    public AssistLevel Level { get; private set; } = AssistLevel.Standard;

    /// <summary>
    /// Off by default, and deliberately so.
    ///
    /// Everything that measures the flight model - control derivatives, autorotation,
    /// hands-off stability - has to see the bare airframe, or it is measuring the
    /// augmentation and reporting it as aerodynamics. Switching this on by default
    /// silently reversed the sign of the measured roll derivative the first time it was
    /// tried. The game turns it on; the instruments do not.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How much control travel the system may use on its own, per axis.
    ///
    /// Thirty per cent, which is more than a 1960s series actuator got, and the number was
    /// measured rather than chosen. Scanning gain against authority (the "sassweep"
    /// scenario) splits cleanly in two:
    ///
    ///     authority 0.18:  gain x0.4 -> 7.2 s,  x0.65 -> 5.3 s,  x1.0 -> 4.0 s
    ///     authority 0.30:  gain x0.4 -> 7.2 s,  x0.65 -> 14.3 s, x1.0 -> 22.3 s
    ///
    /// At 0.18 the system is hard against its limit almost all the time, so it stops being
    /// a damper and becomes a bang-bang controller - and more gain makes it worse, which is
    /// the signature of that. At 0.30 it stays inside its authority and behaves linearly,
    /// and more gain simply helps. Against a bare airframe that departs at 6.5 s, this
    /// configuration gives 22.3 s.
    ///
    /// It is still a limit. Saturation is still reachable, still an event, and still
    /// something the aircraft can be flown past.
    /// </summary>
    public double Authority { get; set; } = 0.30;

    /// <summary>Damping gains, in control units per rad/s of body rate.</summary>
    public double RollGain { get; set; } = 0.85;
    public double PitchGain { get; set; } = 0.95;
    public double YawGain { get; set; } = 1.35;

    // The sign of each control's effect, MEASURED rather than assumed - see the "cyclic"
    // scenario in simlab, which reports the derivative about the hover trim:
    //
    //     cyclic left/right -> roll rate    +6.2 deg/s per 0.25 stick
    //     cyclic fore/aft   -> pitch rate   -3.0 deg/s per 0.25 stick
    //     pedal             -> yaw rate    +25.1 deg/s per 0.30 stick
    //
    // Fore/aft cyclic is the odd one out: positive stick is forward, and forward stick
    // pitches the nose DOWN, so its derivative is negative while the other two are
    // positive. Damping all three with the same -rate*gain therefore damps roll and yaw
    // and DRIVES pitch. That is not a subtle error - with it in place the aircraft left
    // trim four times faster than with no augmentation at all.
    private const double RollSign = -1.0;
    private const double PitchSign = +1.0;
    private const double YawSign = -1.0;

    /// <summary>
    /// A weak pull back toward level, in control units per radian of attitude error.
    ///
    /// Deliberately feeble. This is not an autopilot and must never feel like one; it only
    /// stops a slow roll-off from becoming a spiral while the pilot is looking at the map.
    /// It is disabled entirely once the pilot is commanding, so it cannot fight a turn.
    /// </summary>
    public double AttitudeGain { get; set; } = 0.30;

    /// <summary>Heading hold, in control units per radian of heading error.</summary>
    public double HeadingGain { get; set; } = 0.55;

    /// <summary>The bank the aircraft should settle at, from the trim solution.</summary>
    public double TrimRollRad { get; set; }
    public double TrimPitchRad { get; set; }

    /// <summary>
    /// Where the controls sit at trim.
    ///
    /// This is what "hands off" has to be measured against, and getting it wrong made the
    /// levelling terms dead code. A trimmed hover holds about 0.275 of lateral cyclic, so
    /// comparing the pilot's stick against ZERO decided the pilot was commanding a roll at
    /// all times and switched the levelling off permanently. Hands off means the stick is
    /// where trim left it, not that the stick is centred.
    /// </summary>
    public Controls TrimControls { get; set; } = Controls.Neutral;

    private double _headingHoldRad;
    private bool _headingHeld;

    /// <summary>How much of its authority the system is currently using, per axis, 0..1.</summary>
    public Vec3 Saturation { get; private set; }

    /// <summary>True when any axis has run out of authority and the pilot is on their own.</summary>
    public bool Saturated => Saturation.X >= 0.999 || Saturation.Y >= 0.999 || Saturation.Z >= 0.999;

    /// <summary>
    /// Fold the augmentation into the pilot's command.
    /// </summary>
    /// <param name="pilot">What the pilot is asking for.</param>
    /// <param name="state">Current flight state.</param>
    /// <param name="effectiveness">
    /// Scales the whole system down as the hydraulics or the computer take damage, so a
    /// degraded SAS fades rather than switching off in one frame.
    /// </param>
    public Controls Augment(Controls pilot, in FlightState state, double effectiveness = 1.0)
    {
        if (!Enabled || effectiveness <= 0.0)
        {
            Saturation = Vec3.Zero;
            return pilot;
        }

        Vec3 rate = state.AngularVelocity;

        double roll = RollSign * rate.X * RollGain;
        double pitch = PitchSign * rate.Y * PitchGain;
        double yaw = YawSign * rate.Z * YawGain;

        // Levelling and heading hold, each only while the pilot is hands-off on that axis.
        // A pilot holding lateral cyclic is asking for a bank, and an augmentation system
        // that argues with that is worse than none at all.
        const double deadband = 0.06;
        bool rollFree = Math.Abs(pilot.CyclicRoll - TrimControls.CyclicRoll) < deadband;
        bool pitchFree = Math.Abs(pilot.CyclicPitch - TrimControls.CyclicPitch) < deadband;
        bool yawFree = Math.Abs(pilot.Pedal - TrimControls.Pedal) < deadband;

        if (rollFree)
            roll += RollSign * WrapAngle(state.Orientation.Roll - TrimRollRad) * AttitudeGain;
        if (pitchFree)
            pitch += PitchSign * WrapAngle(state.Orientation.Pitch - TrimPitchRad) * AttitudeGain;

        // Heading hold. This is the one function that addresses why the aircraft departs at
        // all: the pedal that trims the hover is nearly a third of a travel wrong by 40 kt,
        // so any acceleration walks the nose away and the spiral follows. Holding a heading
        // absorbs that changing trim automatically, which is exactly the job real heading
        // hold does. It captures on release and drops the moment the pilot touches a pedal.
        if (yawFree)
        {
            if (!_headingHeld) { _headingHoldRad = state.Orientation.Yaw; _headingHeld = true; }
            yaw += YawSign * WrapAngle(state.Orientation.Yaw - _headingHoldRad) * HeadingGain;
        }
        else _headingHeld = false;

        double limit = Authority * effectiveness;
        Saturation = new Vec3(
            Math.Min(1.0, Math.Abs(roll) / Math.Max(limit, 1e-9)),
            Math.Min(1.0, Math.Abs(pitch) / Math.Max(limit, 1e-9)),
            Math.Min(1.0, Math.Abs(yaw) / Math.Max(limit, 1e-9)));

        var augmented = pilot;
        augmented.CyclicRoll = Math.Clamp(pilot.CyclicRoll + Math.Clamp(roll, -limit, limit), -1, 1);
        augmented.CyclicPitch = Math.Clamp(pilot.CyclicPitch + Math.Clamp(pitch, -limit, limit), -1, 1);
        augmented.Pedal = Math.Clamp(pilot.Pedal + Math.Clamp(yaw, -limit, limit), -1, 1);
        return augmented;
    }

    private static double WrapAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
