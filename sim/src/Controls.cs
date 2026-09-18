namespace Rotorwash.Sim;

/// <summary>
/// Pilot inputs, already normalised and already through whatever assist layer is active.
/// The raw stick/HOTAS handling and the assists live above this; the flight model only
/// ever sees these numbers.
/// </summary>
public struct Controls
{
    /// <summary>Collective lever, 0 (full down) to 1 (full up).</summary>
    public double Collective;

    /// <summary>Cyclic fore/aft. Positive = stick forward = disc tilts forward = nose down.</summary>
    public double CyclicPitch;

    /// <summary>Cyclic left/right. Positive = stick right.</summary>
    public double CyclicRoll;

    /// <summary>Anti-torque pedals. Positive = right pedal = nose right.</summary>
    public double Pedal;

    /// <summary>Throttle / governor beep. 1.0 = flight, 0 = idle. Twist grip on a piston machine.</summary>
    public double Throttle;

    /// <summary>Wheel/rotor brake, 0..1.</summary>
    public double Brake;

    public static Controls Neutral => new() { Collective = 0, Throttle = 1.0 };

    public void ClampToRange()
    {
        Collective = Math.Clamp(Collective, 0, 1);
        CyclicPitch = Math.Clamp(CyclicPitch, -1, 1);
        CyclicRoll = Math.Clamp(CyclicRoll, -1, 1);
        Pedal = Math.Clamp(Pedal, -1, 1);
        Throttle = Math.Clamp(Throttle, 0, 1);
        Brake = Math.Clamp(Brake, 0, 1);
    }
}
