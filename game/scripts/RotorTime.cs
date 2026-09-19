using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Rotor Time: tactical slow-motion that works in the air and on the ground.
///
/// The resource fills through committed flying — bank angle, G-load, speed at low
/// altitude, VRS recovery — and drains when active. It is ONE mechanic in two contexts:
/// in flight the world slows so the pilot can aim and decide; on foot the world slows
/// for called shots. The fact that it charges from flying and spends on foot is the
/// mechanical bridge that makes the pilot feel like the same person in and out.
///
/// Time dilation uses Engine.TimeScale. The camera compensates by dividing delta by
/// the scale, so the player's view stays responsive while the world crawls.
/// </summary>
public sealed partial class RotorTime : Node
{
    /// <summary>Current charge, 0..1.</summary>
    public float Charge { get; set; }

    /// <summary>True while the effect is active.</summary>
    public bool Active { get; private set; }

    /// <summary>The current time scale (1.0 = normal, 0.3 = active).</summary>
    public float TimeScale => Active ? SlowScale : 1.0f;

    // --- Tuning constants ---

    /// <summary>Minimum charge to activate.</summary>
    public float ActivationThreshold { get; set; } = 0.15f;

    /// <summary>Drain rate per real second while active.</summary>
    public float DrainRate { get; set; } = 0.25f;

    /// <summary>Engine.TimeScale when active.</summary>
    public float SlowScale { get; set; } = 0.3f;

    /// <summary>Peak charge rate per second from committed flying.</summary>
    public float ChargeRate { get; set; } = 0.15f;

    /// <summary>FOV during Rotor Time (narrower = zoom feel).</summary>
    public float ActiveFov { get; set; } = 58f;

    /// <summary>Normal FOV.</summary>
    public float NormalFov { get; set; } = 68f;

    private HelicopterController? _heli;
    private float _commitment;

    public void SetHelicopter(HelicopterController heli) => _heli = heli;

    /// <summary>
    /// Feed flight telemetry to accumulate charge. Call every physics frame.
    /// The charge rate scales with how committed the flying is — a straight-and-level
    /// cruise earns nothing; a low-altitude banking turn at high speed earns fast.
    /// </summary>
    public void UpdateCharge(double delta)
    {
        if (_heli is null) return;
        if (Active) return; // do not charge while spending

        var t = _heli.Sim.Telemetry;
        var s = _heli.Sim.State;

        // Commitment: how much the pilot is asking of the machine right now.
        float commitment = 0f;

        // Bank angle: anything over 25 degrees is a real manoeuvre.
        float bankDeg = Mathf.Abs(Mathf.RadToDeg((float)s.Orientation.Roll));
        if (bankDeg > 25f) commitment += Mathf.Clamp((bankDeg - 25f) / 35f, 0, 1f) * 0.5f;

        // G-load: anything above 1.4 or below 0.6 is pulling or pushing.
        // (Approximate G from vertical acceleration relative to gravity.)
        float gEst = (float)(1.0 + _heli.LinearVelocity.Y * 0.02);
        if (gEst > 1.4f) commitment += Mathf.Clamp((gEst - 1.4f) / 1.5f, 0, 1f) * 0.4f;
        if (gEst < 0.6f) commitment += Mathf.Clamp((0.6f - gEst) / 0.5f, 0, 1f) * 0.3f;

        // Low-altitude high-speed: below 50m AGL and above 30 kt.
        float agl = _heli.HeightAgl();
        float kts = (float)(t.AirspeedTrue * SimBridge.MetresPerSecondToKnots);
        if (agl < 50f && kts > 30f)
            commitment += Mathf.Clamp((1f - agl / 50f) * (kts / 80f), 0, 0.6f);

        // VRS recovery: clawing out of vortex ring state is the most committed flying there is.
        if (t.VrsSeverity > 0.3f)
            commitment += (float)t.VrsSeverity * 0.7f;

        // Smooth the commitment so it does not flicker.
        _commitment = Mathf.Lerp(_commitment, commitment, (float)delta * 3f);

        // Charge accumulates proportional to commitment.
        Charge = Mathf.Clamp(Charge + _commitment * ChargeRate * (float)delta, 0, 1f);
    }

    /// <summary>Try to activate. Returns true if successful.</summary>
    public bool TryActivate()
    {
        if (Active) return false;
        if (Charge < ActivationThreshold) return false;
        Active = true;
        Engine.TimeScale = SlowScale;
        return true;
    }

    /// <summary>Deactivate, restoring normal time.</summary>
    public void Deactivate()
    {
        if (!Active) return;
        Active = false;
        Engine.TimeScale = 1.0;
    }

    /// <summary>
    /// Drain charge while active. Call every physics frame.
    /// Delta here is the ENGINE's delta (already scaled by TimeScale), so we need
    /// to convert to real time for consistent drain.
    /// </summary>
    public void UpdateDrain(double engineDelta)
    {
        if (!Active) return;

        // Convert engine delta to real-time delta.
        double realDelta = engineDelta / Math.Max(SlowScale, 0.01);
        Charge = Mathf.Clamp(Charge - DrainRate * (float)realDelta, 0, 1f);

        if (Charge <= 0.001f)
            Deactivate();
    }

    /// <summary>Current FOV, smoothly interpolated.</summary>
    public float CurrentFov(float currentFov, float delta)
    {
        float target = Active ? ActiveFov : NormalFov;
        return Mathf.Lerp(currentFov, target, 1f - Mathf.Exp(-8f * delta));
    }

    public override void _ExitTree()
    {
        // Safety: always restore time scale when removed.
        if (Active) Engine.TimeScale = 1.0;
    }
}
