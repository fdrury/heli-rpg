using System;

namespace Rotorwash.Sim;

/// <summary>
/// What the world sounds like at a point, derived from the terrain around it.
///
/// A helicopter in a valley bounces sound off the walls. A helicopter over water hears
/// nothing back. A helicopter fifty feet over a settlement hears the settlement through the
/// rotor. None of these have anything to do with recordings or with the aircraft itself —
/// they are properties of WHERE, and this class computes them from the height field.
///
/// The output is a small struct of reverb parameters that any audio engine can consume.
/// No Godot dependency — it lives in the sim so it can be tested from simlab.
///
/// <b>How it works:</b> sample the height field in a ring around the listener, at two
/// radii. The inner ring (50 m) measures the immediate enclosure — buildings, gully walls,
/// valley sides. The outer ring (200 m) measures the larger space — open plain vs deep
/// valley. The difference between the listener's height and the ring heights is the
/// enclosure, and enclosure drives reverb.
/// </summary>
public static class AcousticSpace
{
    /// <summary>Everything the audio system needs to set reverb for this frame.</summary>
    public struct Params
    {
        /// <summary>0 = anechoic outdoors, 1 = tight enclosed space (valley, among buildings).</summary>
        public double Enclosure;

        /// <summary>Reverb room size, 0..1. Small for gullies, large for valleys.</summary>
        public double RoomSize;

        /// <summary>Reverb damping, 0..1. High over grass/trees, low over rock/water.</summary>
        public double Damping;

        /// <summary>Reverb wet level, 0..1. How much reverb to mix in.</summary>
        public double WetLevel;

        /// <summary>Low-pass cutoff for distance filtering, Hz. Lower = more muffled.</summary>
        public double DistanceLpHz;

        /// <summary>0 = dry land, 1 = over water. Water reflects sound sharply.</summary>
        public double WaterProximity;
    }

    private const int RingSamples = 12;
    private const double InnerRadius = 50.0;
    private const double OuterRadius = 200.0;

    /// <summary>
    /// Compute acoustic parameters for a listener at the given world position.
    /// </summary>
    /// <param name="x">World X (east).</param>
    /// <param name="z">World Z (south).</param>
    /// <param name="listenerY">Listener altitude, metres.</param>
    /// <param name="heightAt">Height field function — WorldHeight.At or equivalent.</param>
    /// <param name="waterLevel">Water surface level, metres.</param>
    public static Params At(double x, double z, double listenerY,
                            Func<float, float, float> heightAt, float waterLevel = 0f)
    {
        double groundBelow = heightAt((float)x, (float)z);

        // --- Sample rings -------------------------------------------------------
        double innerAbove = 0;  // how much the inner ring rises above the listener
        double outerAbove = 0;
        double innerBelow = 0;  // how much the inner ring drops below the listener
        double waterCount = 0;

        for (int i = 0; i < RingSamples; i++)
        {
            double angle = Math.Tau * i / RingSamples;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);

            // Inner ring
            float ih = heightAt((float)(x + cos * InnerRadius), (float)(z + sin * InnerRadius));
            double iDelta = ih - listenerY;
            if (iDelta > 0) innerAbove += iDelta;
            else innerBelow -= iDelta;

            // Outer ring
            float oh = heightAt((float)(x + cos * OuterRadius), (float)(z + sin * OuterRadius));
            double oDelta = oh - listenerY;
            if (oDelta > 0) outerAbove += oDelta;

            // Water: check if terrain is below water level nearby
            if (ih < waterLevel) waterCount++;
        }

        innerAbove /= RingSamples;
        innerBelow /= RingSamples;
        outerAbove /= RingSamples;

        double agl = Math.Max(0, listenerY - groundBelow);

        // --- Enclosure ----------------------------------------------------------
        // How walled-in is the listener? Terrain rising above the listener on all sides
        // means a valley or a built-up area. Terrain falling away means open or elevated.
        //
        // Inner ring dominates: a gully 10 m deep gives stronger early reflections than
        // a valley 200 m away. AGL matters too: hovering 200 m above a valley is not
        // enclosed, even though the walls are 300 m high.
        double innerEnc = Math.Clamp(innerAbove / 20.0, 0, 1);
        double outerEnc = Math.Clamp(outerAbove / 40.0, 0, 1);
        double aglFade = Math.Clamp(1.0 - agl / 150.0, 0, 1);  // above 150m AGL, no enclosure
        double enclosure = Math.Clamp((innerEnc * 0.6 + outerEnc * 0.4) * aglFade, 0, 1);

        // --- Water proximity ----------------------------------------------------
        double water = Math.Clamp(waterCount / (RingSamples * 0.5), 0, 1);
        // Water beneath the listener
        if (groundBelow < waterLevel)
            water = Math.Max(water, Math.Clamp((waterLevel - groundBelow) / 3.0, 0, 1));

        // --- Reverb parameters --------------------------------------------------
        // Enclosure drives room size: tight = small room (early reflections), open = large.
        // But water is the special case: flat, hard, reflective — large room, low damping,
        // and the reflections arrive from below rather than from walls.
        double roomSize = 0.2 + enclosure * 0.5 + water * 0.3;

        // Damping: grass and trees absorb high frequencies (high damping). Rock and water
        // reflect them (low damping). Use inner ring depth as a proxy for "rocky walls".
        double rocky = Math.Clamp(innerAbove / 30.0, 0, 1);
        double damping = Math.Clamp(0.7 - rocky * 0.3 - water * 0.3, 0.1, 0.9);

        // Wet level: how much reverb the listener hears. Zero in the open, significant
        // in a valley, moderate over water.
        double wetLevel = Math.Clamp(enclosure * 0.45 + water * 0.25, 0, 0.55);

        // Distance low-pass: the helicopter's top end disappears with distance. At 50 m
        // AGL and close, full bandwidth. At 500 m it has lost everything above 2 kHz.
        // This is the number the game-side low-pass filter uses.
        double distanceLp = 4000.0 + Math.Clamp(1.0 - agl / 400.0, 0, 1) * 12000.0;

        return new Params
        {
            Enclosure = enclosure,
            RoomSize = Math.Clamp(roomSize, 0, 1),
            Damping = Math.Clamp(damping, 0, 1),
            WetLevel = wetLevel,
            DistanceLpHz = distanceLp,
            WaterProximity = water,
        };
    }
}
