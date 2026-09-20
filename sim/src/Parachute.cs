using System;

namespace Rotorwash.Sim;

/// <summary>
/// Leaving the aircraft, and what it costs.
///
/// <para><b>Fred's idea, and the constraint that makes it interesting.</b> Bailing out is not
/// an escape hatch in this game, because D-008 says there is exactly one aircraft in the
/// world. Stepping out of Hugh means Hugh flies on without you and comes down somewhere, and
/// there is no second airframe to find. That could have been a game-over screen; it is more
/// interesting as a <b>distance problem</b>.</para>
///
/// <para><b>Altitude is the price.</b> An unattended helicopter does not stop - it carries on
/// on whatever trim it was left in until the fuel or the ground ends it. Jump from two
/// hundred feet and the wreck is in the next field; jump from three thousand and it can be
/// ten kilometres away over country you have not surveyed, and finding it is the rest of your
/// week. So the decision at the moment of bailing out is not "do I live" - you live either
/// way, that is what the canopy is for - it is "how far am I willing to walk".</para>
///
/// <para><b>The rotor is the hazard, not the fall.</b> Helicopter egress in flight is
/// dangerous because the disc is directly above the door and the tail rotor is behind it.
/// That is a real constraint and it is the one that stops this being free: you have to get
/// the aircraft slow and level first, which is exactly the thing that is hard when something
/// has gone wrong.</para>
/// </summary>
public static class Parachute
{
    /// <summary>Terminal velocity in a stable belly-to-earth freefall, m/s.</summary>
    public const double FreefallSpeed = 52.0;

    /// <summary>Descent rate under a round canopy with a person under it, m/s.</summary>
    public const double CanopySpeed = 5.5;

    /// <summary>
    /// How long the canopy takes to become a canopy, in seconds.
    ///
    /// Deployment is not instant and the difference matters: at 52 m/s you cover nearly
    /// ninety metres in the time it takes to open, which is what sets the hard deck below.
    /// </summary>
    public const double DeploySeconds = 1.7;

    /// <summary>
    /// The lowest height above the ground a jump can be survived from, metres.
    ///
    /// Derived rather than chosen: the deployment distance at freefall speed, plus a few
    /// seconds under the canopy to stop swinging. A player who leaves it later than this has
    /// made the wrong call, and the number is published so the HUD can say so while there is
    /// still time to act on it (D-005a: facts, not advice).
    /// </summary>
    public static double HardDeckM => FreefallSpeed * DeploySeconds + CanopySpeed * 3.0;

    /// <summary>
    /// Forward speed a steerable canopy makes good, m/s. Enough to pick which side of a
    /// ridge to land on and nowhere near enough to cross water.
    /// </summary>
    public const double GlideSpeed = 7.0;

    /// <summary>
    /// How fast the aircraft has to be for stepping out of it to be survivable, m/s.
    ///
    /// Above this the airflow puts the jumper into the tail boom rather than past it. It is
    /// the reason bailing out is a manoeuvre rather than a button: something has gone wrong,
    /// and the first thing you have to do about it is slow down.
    /// </summary>
    public const double MaxExitSpeed = 36.0;

    /// <summary>Beyond this bank angle you are not going out of the door, you are falling out of it.</summary>
    public const double MaxExitBankDeg = 45.0;

    /// <summary>Whether the aircraft is in a state a person can actually leave.</summary>
    public static bool CanBailOut(double airspeedMs, double bankDeg, double heightAgl)
        => airspeedMs <= MaxExitSpeed
           && Math.Abs(bankDeg) <= MaxExitBankDeg
           && heightAgl >= HardDeckM;

    /// <summary>Why not, for the strip. Empty when the answer is yes.</summary>
    public static string WhyNot(double airspeedMs, double bankDeg, double heightAgl)
    {
        if (heightAgl < HardDeckM) return $"too low - {HardDeckM:F0} m needed, {heightAgl:F0} m to go";
        if (airspeedMs > MaxExitSpeed) return $"too fast - {MaxExitSpeed * 1.94384:F0} kt or less";
        if (Math.Abs(bankDeg) > MaxExitBankDeg) return "wings level first";
        return "";
    }

    /// <summary>
    /// How far the abandoned aircraft gets before it hits something, metres.
    ///
    /// Not a simulation of the departure - it is a straight-line estimate over the ground,
    /// which is all anyone needs to decide whether the wreck will be findable. An unattended
    /// helicopter left in trim descends rather than holds height, so the distance is set by
    /// how long it takes to get down, which is set by how high it was.
    /// </summary>
    public static double AircraftRunOnM(double heightAgl, double airspeedMs)
    {
        // A helicopter left to itself does not fly straight and level for long - the
        // instability the whole game is built on sees to that - so this is deliberately
        // pessimistic about how long it stays up and generous about how far it goes while
        // it does. About a 4 m/s settle, which is a machine descending in a slow spiral.
        double seconds = Math.Max(0, heightAgl) / 4.0;
        return Math.Max(0, airspeedMs) * seconds * 0.8;
    }

    /// <summary>Where the jumper comes down, given a wind. Plain kinematics, no drama.</summary>
    public static (double Seconds, double DriftM) Descent(double heightAgl, double windMs)
    {
        double h = Math.Max(0, heightAgl);
        double freefall = Math.Min(h, FreefallSpeed * DeploySeconds);
        double underCanopy = Math.Max(0, h - freefall);
        double seconds = DeploySeconds + underCanopy / CanopySpeed;
        return (seconds, windMs * seconds);
    }
}
