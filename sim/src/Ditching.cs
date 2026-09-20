using System;
using System.Collections.Generic;

namespace Rotorwash.Sim;

/// <summary>
/// What happens when the aircraft touches the water.
///
/// <para><b>Why this exists.</b> D-059 made the world an archipelago and D-077 spread it out
/// until 85% of the envelope is sea. The entire argument for that layout is one sentence:
/// *over land an engine failure is an autorotation into a field and a walk home; over water
/// it is a swim and the aircraft is gone.* That sentence was doing all the work and **nothing
/// implemented it**. Water was a landing surface that scored zero and threw spray instead of
/// dust; the visual water is a `MeshInstance3D` with no collider, so an aircraft descending
/// at sea fell straight through the surface and sat on the seabed eleven to seventy-five
/// metres down, undamaged, able to lift off again and fly home through the water. Every
/// committed crossing in the world was free.</para>
///
/// <para><b>The physics that decides everything here.</b> A helicopter does not land on
/// water, and it does not sink gently either. The rotor is the problem: a disc turning at
/// 324 rpm with a tip speed near 210 m/s cannot take a blade into water. Water is eight
/// hundred times denser than air, the drag on the immersed blade is enormous and asymmetric,
/// and the rotor tears itself off the mast in well under a revolution. That is why ditching
/// drills for helicopters without flotation are about attitude and rotor RPM at the moment
/// of contact and not about touching down softly - softly does not help.</para>
///
/// <para><b>What this class does NOT decide.</b> Whether the run continues. D-008 says there
/// is exactly one aircraft in the world, so "find another airframe" is not available by
/// construction, and what a ditched aircraft costs the player is a design question with
/// several defensible answers. This produces damage and a verdict; the consequence is
/// somebody else's decision, and it is flagged for Fred rather than invented here.</para>
/// </summary>
public static class Ditching
{
    /// <summary>
    /// How fast the rotor has to be turning for water contact to destroy it.
    ///
    /// Expressed as a fraction of nominal Nr. Very low on purpose: this is not a threshold
    /// where a slower rotor survives, it is the point below which the blades are no longer
    /// carrying enough energy to tear the head off the mast. A rotor at 15% is windmilling
    /// and will simply stop; a rotor at 60% still has thirty-six times the energy of one at
    /// 10%, because energy goes as the square.
    /// </summary>
    public const double RotorDestroysItselfAbove = 0.20;

    /// <summary>
    /// Water contact that counts. Metres of the aircraft's lowest point below the surface.
    ///
    /// Not zero, because a skid kissing a wave top in a low hover over the sea is a scare
    /// and not a ditching, and because the waterline is a flat quad while a real one is
    /// not. Half a metre is past any reasonable ambiguity.
    /// </summary>
    public const double ContactDepthM = 0.5;

    /// <summary>One ditching, as it happened.</summary>
    /// <param name="Depth">How far the lowest point was under when it was caught, m.</param>
    /// <param name="VerticalSpeed">Rate of descent at contact, m/s positive down.</param>
    /// <param name="GroundSpeed">Speed over the water, m/s.</param>
    /// <param name="RotorFraction">Nr as a fraction of nominal at contact.</param>
    /// <param name="RotorDestroyed">The disc took water while turning.</param>
    /// <param name="Survivable">The crew got out.</param>
    /// <param name="Summary">What to write in the journal.</param>
    public readonly record struct Report(
        double Depth,
        double VerticalSpeed,
        double GroundSpeed,
        double RotorFraction,
        bool RotorDestroyed,
        bool Survivable,
        string Summary);

    /// <summary>
    /// Work out what the water did to the aircraft.
    ///
    /// Everything is a consequence of two facts - whether the rotor was turning, and how
    /// hard the aircraft arrived. There is no "good" ditching in the sense a landing can be
    /// good; there is one you walk away from and one you do not.
    /// </summary>
    public static Report Evaluate(double depth, double verticalSpeed, double groundSpeed,
                                  double rotorFraction)
    {
        bool rotorDestroyed = rotorFraction > RotorDestroysItselfAbove;

        // Survivability is about the arrival, not about the aircraft. A controlled ditching
        // - wings level, low rate of descent, little forward speed - is a survivable event
        // and has been for as long as people have been flying over water. Arriving at ten
        // metres a second is hitting concrete that happens to be wet: water does not
        // compress at that rate and the deceleration is the same either way.
        bool survivable = verticalSpeed < 8.0 && groundSpeed < 35.0;

        string how = rotorDestroyed
            ? "rotor into the water"
            : "settled in with the rotor stopped";
        string arrival = verticalSpeed > 8.0 ? "arrived hard"
                       : groundSpeed > 35.0 ? "arrived fast"
                       : "under control";

        return new Report(depth, verticalSpeed, groundSpeed, rotorFraction, rotorDestroyed,
                          survivable, $"ditched, {arrival} - {how}");
    }

    /// <summary>
    /// The damage the water does, per component.
    ///
    /// Returned rather than applied so this file stays pure and the caller keeps ownership
    /// of the damage model. The shape is deliberately not a scaled version of a hard
    /// landing: a hard landing is a shock load through the skids and up the mast, and a
    /// ditching is a submersion. Different components, different reasons.
    /// </summary>
    public static IEnumerable<(Component Part, double Amount, string Why)> DamageFrom(Report r)
    {
        if (r.RotorDestroyed)
        {
            // The blade that goes in first stops; the one opposite does not. The head takes
            // the whole of that asymmetry in a fraction of a revolution.
            yield return (Component.MainRotor, 0.98, "blades into the water at speed");
            // And the mast passes it straight to the gearbox.
            yield return (Component.Transmission, 0.85, "rotor stopped by the water");
            yield return (Component.TailRotor, 0.80, "tail boom in the water");
        }
        else
        {
            yield return (Component.MainRotor, 0.35, "blades in the water, rotor stopped");
        }

        // Everything below the waterline floods, and on a helicopter that is everything.
        // The engine is the one that does not care how gently you arrived: an intake under
        // water ingests it, and water does not compress in a compressor.
        yield return (Component.Engine, 0.90, "water ingestion");
        yield return (Component.Avionics, 1.00, "submerged");
        yield return (Component.Hydraulics, 0.60, "flooded");
        yield return (Component.Fuselage, Math.Clamp(0.30 + r.VerticalSpeed * 0.06, 0.30, 0.95),
                      "hull flooded");
        yield return (Component.Skids, 0.50, "in the water");
    }

    /// <summary>
    /// A line for the journal and the radio. Facts, no advice, no drama (D-005a).
    /// </summary>
    public static string Describe(in Report r)
    {
        string state = r.Survivable
            ? "You are out and the aircraft is not."
            : "It arrived faster than water allows for.";
        return $"In the water. {r.Depth:F1} m under at {r.VerticalSpeed:F1} m/s, " +
               $"{r.GroundSpeed:F0} m/s across, rotor at {r.RotorFraction * 100:F0}%. {state}";
    }
}
