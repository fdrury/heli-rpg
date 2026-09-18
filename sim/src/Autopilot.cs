namespace Rotorwash.Sim;

/// <summary>Minimal PID with output clamping and integral anti-windup.</summary>
public sealed class Pid
{
    public double P, I, D, Min = -1, Max = 1, IntegralLimit = 1;
    private double _integral, _prevError;
    private bool _hasPrev;

    public Pid(double p, double i, double d, double min = -1, double max = 1)
    { P = p; I = i; D = d; Min = min; Max = max; }

    public double Integral { get => _integral; set => _integral = value; }

    public void Reset() { _integral = 0; _hasPrev = false; }

    public double Update(double error, double dt)
    {
        if (dt <= 0) return 0;
        double derivative = _hasPrev ? (error - _prevError) / dt : 0;
        _prevError = error; _hasPrev = true;

        double raw = P * error + I * _integral + D * derivative;

        // Only integrate while the output is inside its limits, so a saturated axis
        // cannot wind up and then overshoot for several seconds afterwards.
        if (raw > Min && raw < Max)
            _integral = Math.Clamp(_integral + error * dt, -IntegralLimit, IntegralLimit);

        return Math.Clamp(P * error + I * _integral + D * derivative, Min, Max);
    }
}

/// <summary>What the autopilot is being asked to do. Null fields are not controlled.</summary>
public struct AutopilotDemand
{
    public double? Altitude;        // m
    public double? VerticalSpeed;   // m/s, positive up
    public double? ForwardSpeed;    // m/s, body axis
    public double? LateralSpeed;    // m/s, body axis, usually 0
    public double? Heading;         // rad
    public double? YawRate;         // rad/s
    public double? PitchAttitude;   // rad, overrides ForwardSpeed
    public double? RollAttitude;    // rad, overrides LateralSpeed
    public double? Collective;      // 0..1, bypass the vertical loops entirely

    /// <summary>
    /// Hold this rotor speed (as a fraction of nominal) with the collective. This is
    /// what a pilot actually does in an autorotation: the collective stops being a
    /// climb control and becomes the rotor's throttle, trading the energy stored in the
    /// rotor against the altitude being spent. Overrides the vertical loops.
    /// </summary>
    public double? RotorRpmFraction;
}

/// <summary>
/// Cascaded rate / attitude / velocity autopilot.
///
/// Three jobs, which is why it lives in the sim rather than the game:
///   * it flies the aircraft in headless flight tests, so the physics can be measured
///     without a human on the stick;
///   * it is the stability augmentation the player unlocks by scavenging avionics - an
///     attitude-hold box really is this code, gated behind a part that can be found,
///     installed, damaged and lost;
///   * it is the hands-off hold modes for long transits.
///
/// Structure is deliberately conventional: an inner loop on body rates, an attitude loop
/// commanding those rates, and a velocity loop commanding attitude. Rate innermost is
/// what makes it stable on an airframe as twitchy as a helicopter.
/// </summary>
public sealed class Autopilot
{
    // Inner: body rate -> control. Gains are in stick units per rad/s.
    public Pid RollRate = new(0.55, 0.05, 0.010) { IntegralLimit = 3 };
    public Pid PitchRate = new(0.70, 0.08, 0.012) { IntegralLimit = 3 };
    public Pid YawRate = new(0.90, 0.35, 0.020) { IntegralLimit = 3 };

    // Middle: attitude -> rate demand, in rad/s per rad.
    public Pid RollAttitude = new(2.2, 0.0, 0.0, -0.9, 0.9);
    public Pid PitchAttitude = new(2.2, 0.0, 0.0, -0.9, 0.9);
    public Pid HeadingHold = new(1.2, 0.0, 0.0, -0.8, 0.8);

    // Outer: velocity -> attitude demand, in rad per m/s.
    public Pid SpeedLoop = new(0.030, 0.006, 0.0) { Min = -0.45, Max = 0.45, IntegralLimit = 12 };
    public Pid DriftLoop = new(0.034, 0.007, 0.0) { Min = -0.45, Max = 0.45, IntegralLimit = 12 };

    // Vertical: altitude -> vertical speed -> collective.
    public Pid AltitudeLoop = new(0.40, 0.0, 0.0, -8, 8);
    public Pid VerticalSpeed = new(0.016, 0.0, 0.004) { Min = -0.4, Max = 0.4 };

    /// <summary>Rotor speed error (fraction of nominal) -> collective. Used in autorotation.</summary>
    public Pid RotorRpmLoop = new(3.0, 0.0, 0.35) { Min = -0.5, Max = 0.5 };

    /// <summary>Maximum attitude the outer loops will command, radians.</summary>
    public double AttitudeLimit { get; set; } = 22.0 * Math.PI / 180.0;

    /// <summary>
    /// Learned hover collective. The vertical loop is proportional around this, and this
    /// value integrates slowly toward whatever actually holds altitude - which means the
    /// controller discovers the trim for any aircraft, any weight, any density altitude,
    /// without being told. It is also exactly what a real collective friction/trim does.
    /// </summary>
    public double CollectiveTrim { get; set; } = 0.5;

    /// <summary>How fast the trim integrates, lever units per second per unit error.</summary>
    public double TrimRate { get; set; } = 0.25;

    public void Reset()
    {
        RollRate.Reset(); PitchRate.Reset(); YawRate.Reset();
        RollAttitude.Reset(); PitchAttitude.Reset(); HeadingHold.Reset();
        SpeedLoop.Reset(); DriftLoop.Reset();
        AltitudeLoop.Reset(); VerticalSpeed.Reset(); RotorRpmLoop.Reset();
    }

    public Controls Update(Helicopter heli, AutopilotDemand demand, double dt, Controls baseline = default)
    {
        var c = baseline;
        c.Throttle = 1.0;

        var st = heli.State;
        Vec3 vBody = st.Orientation.InverseRotate(st.Velocity);
        Vec3 rate = st.AngularVelocity;
        double roll = st.Orientation.Roll, pitch = st.Orientation.Pitch, yaw = st.Orientation.Yaw;

        // --- Vertical --------------------------------------------------------
        if (demand.RotorRpmFraction is double nrTarget)
        {
            // Raising collective loads the rotor and slows it, so the sign is inverted
            // relative to every other use of the lever.
            double nr = heli.RotorOmega / Math.Max(heli.Airframe.MainRotor.NominalOmega, 1e-6);
            double u = RotorRpmLoop.Update(nr - nrTarget, dt);
            CollectiveTrim = Math.Clamp(CollectiveTrim + u * TrimRate * dt, 0.0, 1.0);
            c.Collective = Math.Clamp(CollectiveTrim + u, 0, 1);
        }
        else if (demand.Collective is double fixedCollective)
        {
            c.Collective = Math.Clamp(fixedCollective, 0, 1);
        }
        else
        {
            double? vsDemand = demand.VerticalSpeed;
            if (demand.Altitude is double targetAlt)
                vsDemand = AltitudeLoop.Update(targetAlt - st.Altitude, dt);

            if (vsDemand is double vs)
            {
                double vsError = vs - (-st.Velocity.Z);
                double u = VerticalSpeed.Update(vsError, dt);
                CollectiveTrim = Math.Clamp(CollectiveTrim + u * TrimRate * dt, 0.0, 1.0);
                c.Collective = Math.Clamp(CollectiveTrim + u, 0, 1);
            }
        }

        // --- Longitudinal ----------------------------------------------------
        double pitchTarget;
        if (demand.PitchAttitude is double pt) pitchTarget = pt;
        else if (demand.ForwardSpeed is double fs) pitchTarget = -SpeedLoop.Update(fs - vBody.X, dt);
        else pitchTarget = 0;
        pitchTarget = Math.Clamp(pitchTarget, -AttitudeLimit, AttitudeLimit);

        double qDemand = PitchAttitude.Update(pitchTarget - pitch, dt);
        // Stick forward is positive and pitches the nose down, so a nose-up rate demand
        // needs negative stick.
        c.CyclicPitch = -PitchRate.Update(qDemand - rate.Y, dt);

        // --- Lateral ---------------------------------------------------------
        double rollTarget;
        if (demand.RollAttitude is double rt) rollTarget = rt;
        else if (demand.LateralSpeed is double ls) rollTarget = DriftLoop.Update(ls - vBody.Y, dt);
        else rollTarget = 0;
        rollTarget = Math.Clamp(rollTarget, -AttitudeLimit, AttitudeLimit);

        double pDemand = RollAttitude.Update(rollTarget - roll, dt);
        c.CyclicRoll = RollRate.Update(pDemand - rate.X, dt);

        // --- Directional -----------------------------------------------------
        double rDemand = demand.YawRate ?? 0;
        if (demand.Heading is double hdg)
            rDemand = HeadingHold.Update(Airfoil.WrapPi(hdg - yaw), dt);
        c.Pedal = YawRate.Update(rDemand - rate.Z, dt);

        c.ClampToRange();
        return c;
    }
}
