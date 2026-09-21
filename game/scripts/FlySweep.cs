using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Sweep the keyboard control tuning and report which settings can actually be flown.
///
/// <para><b>Why this exists.</b> <see cref="PlayProbe"/> answers one question - can a
/// keyboard pilot hold this aircraft - and the answer was no. That is the right question
/// and a useless amount of information: it says the game is unplayable without saying
/// which number is wrong. Every candidate fix then costs a full Godot session to evaluate,
/// one at a time, by eye.</para>
///
/// <para>This runs the whole grid in one session. Same pilot, same aircraft, same starting
/// hover, one row per configuration, so the tuning is chosen by measurement rather than by
/// argument. It is the same move the stability system's "sassweep" made for the SAS gains,
/// and it is here for the same reason: a control law that is tuned by vibes is tuned
/// wrong.</para>
///
/// <para><b>The pilot flies all four controls</b>, unlike PlayProbe, which only works the
/// cyclic. A player holding a hover is also holding altitude and heading, and a test that
/// ignores two of the four axes is flattering itself - the collective and pedal inputs are
/// what make the cyclic's job hard.</para>
///
///     godot --headless --path game -- --flysweep
/// </summary>
public sealed partial class FlySweep : Node
{
    private readonly HelicopterController _heli;

    /// <summary>One configuration to try.</summary>
    private readonly record struct Trial(float Authority, float Rate, float Return,
                                         float PedalAuthority, float PedalRate, float PedalReturn);

    /// <summary>
    /// The grid.
    ///
    /// The cyclic sweep ran first and settled the question it was built for: at the
    /// shipping 0.40 authority a pilot flying all four controls held the aircraft to ten
    /// degrees of tilt. Authority was never the problem. What that sweep exposed instead
    /// was the heading column - 95 to 180 degrees of wander on every single row, because
    /// the keyboard pedal had no spring and no authority limit at all and every tap was a
    /// full deflection.
    ///
    /// So the cyclic is pinned at what measured well and the pedal is the variable. The
    /// authority ladder has already been run and is not repeated here: every rung below
    /// 0.70 crashed or went unflyable, because the pedal that trims a hover is a third of
    /// a travel wrong by 40 kt and a pilot who cannot reach that third cannot stop a yaw.
    /// What is left is the spring, against the unsprung original as a control.
    ///
    /// **This grid returned a null result and the code is honest about it.** Averaged over
    /// three flights each, the sprung rows did not beat the switch on heading error. That
    /// is not evidence the spring is wrong; it is evidence this instrument cannot see the
    /// difference, because the pilot below re-presses its keys every frame and bang-bang
    /// is precisely the input pattern a machine handles best and a person handles worst.
    /// The spring shipped on the argument, not on this table. A human at a keyboard is the
    /// outstanding question - see docs/wiki/playtest.md.
    /// </summary>
    private static readonly Trial[] Grid =
    {
        //         cyclic              pedal
        new(0.40f, 0.42f, 0.28f,  1.00f, 0.05f, 0.05f),   // as it shipped: a switch
        new(0.40f, 0.42f, 0.28f,  1.00f, 0.30f, 0.20f),   // full travel, but sprung
        new(0.40f, 0.42f, 0.28f,  0.90f, 0.30f, 0.20f),
        new(0.40f, 0.42f, 0.28f,  0.80f, 0.30f, 0.20f),
    };

    private const int Repeats = 3;

    private const double SettleSeconds = 1.5;
    private const double HoldSeconds = 20.0;
    private const float HoldAgl = 150f;

    private int _trial = -1;
    private int _repeat;
    private double _t;
    private bool _flying;
    private readonly List<string> _rows = new();
    // Per-configuration accumulators, across repeats.
    private double _accWorstTilt, _accMeanTilt, _accWorstHdg, _accMeanHdg, _accDrift, _accAlt;
    private int _crashes;

    private readonly KeyboardPilot _pilot;

    public FlySweep(HelicopterController heli)
    {
        _heli = heli;
        _pilot = new KeyboardPilot(heli) { TargetAgl = HoldAgl };
    }

    /// <summary>Put the aircraft - body AND sim - back into a clean trimmed hover.</summary>
    private void ResetToHover()
    {
        float ground = WorldHeight.At(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        // A different heading per repeat: same aircraft, same air, different flight.
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(_repeat * 37f));
        _heli.GlobalTransform = new Transform3D(
            basis, new Vector3(_heli.GlobalPosition.X, ground + HoldAgl, _heli.GlobalPosition.Z));
        _heli.LinearVelocity = Vector3.Zero;
        _heli.AngularVelocity = Vector3.Zero;

        var t = _heli.Sim.PlaceInFlightTrimmed(ground + HoldAgl);

        // The datum has to follow the trim solution, or every trial starts with the stick
        // already displaced and measures the recovery from that instead of the hover.
        _heli.Input.SetTrim(t.Controls.CyclicPitch, t.Controls.CyclicRoll, t.Controls.Pedal);
        _heli.Input.SetCollectivePosition((float)t.Controls.Collective);

        _pilot.Begin();
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t < 3.0 && _trial < 0) return;   // let the world stream in once

        if (_trial < 0)
        {
            GD.Print("=== keyboard tuning sweep ==========================================");
            GD.Print($"  profile: {_heli.Input.DeviceDescription}");
            GD.Print($"  each row: a keyboard pilot flying all four controls for {HoldSeconds:F0} s, " +
                     $"{Repeats} flights averaged");
            GD.Print("");
            GD.Print("    cyclic             pedal             | tilt worst/mean  hdg worst/mean  drift  alt | verdict");
            GD.Print("    auth  rate  ret    auth  rate  ret     |");
            GD.Print("    ---------------------------------------------------------------------------------------");
            _trial = 0;
            StartTrial();
            return;
        }

        if (_trial >= Grid.Length) return;

        if (!_flying)
        {
            if (_t < SettleSeconds) return;
            _flying = true;
            _t = 0;
            ResetToHover();     // reset AFTER settling, so nothing is still moving
            return;
        }

        _pilot.Fly();

        if (_t >= HoldSeconds)
        {
            _pilot.ReleaseAll();
            RecordRepeat();
            _repeat++;
            if (_repeat < Repeats) { StartTrial(); return; }

            RecordTrial();
            _repeat = 0;
            _trial++;
            if (_trial < Grid.Length) StartTrial();
            else Finish();
        }
    }

    private void StartTrial()
    {
        Trial g = Grid[_trial];
        _heli.Input.Profile.KeyboardCyclicAuthority = g.Authority;
        _heli.Input.Profile.KeyboardCyclicRate = g.Rate;
        _heli.Input.Profile.KeyboardCyclicReturn = g.Return;
        _heli.Input.Profile.KeyboardPedalAuthority = g.PedalAuthority;
        _heli.Input.Profile.KeyboardPedalRate = g.PedalRate;
        _heli.Input.Profile.KeyboardPedalReturn = g.PedalReturn;
        if (_repeat == 0)
        {
            _accWorstTilt = _accMeanTilt = _accWorstHdg = _accMeanHdg = _accDrift = _accAlt = 0;
            _crashes = 0;
        }
        _pilot.ReleaseAll();
        ResetToHover();
        _flying = false;
        _t = 0;
    }

    /// <summary>Fold one flight into this configuration's running totals.</summary>
    private void RecordRepeat()
    {
        _accWorstTilt += _pilot.WorstTilt;
        _accMeanTilt += _pilot.MeanTilt;
        _accWorstHdg += _pilot.WorstHeadingError;
        _accMeanHdg += _pilot.MeanHeadingError;
        _accDrift += _pilot.WorstDrift;
        _accAlt += _pilot.WorstAglError;
        if (_heli.HeightAgl() <= 3) _crashes++;
    }

    private void RecordTrial()
    {
        Trial g = Grid[_trial];
        double worstTilt = _accWorstTilt / Repeats;
        double meanTilt = _accMeanTilt / Repeats;
        double worstHdg = _accWorstHdg / Repeats;
        double meanHdg = _accMeanHdg / Repeats;

        // Heading counts. An aircraft that holds attitude and altitude while spinning
        // through 180 degrees is not being flown, and the first sweep scored exactly that
        // as "solid" because it only looked at tilt.
        string verdict = _crashes > 0 ? $"CRASHED {_crashes}/{Repeats}"
                       : worstTilt > 60 ? "unflyable"
                       : worstTilt > 30 || meanHdg > 40 ? "twitchy"
                       : worstTilt > 15 || meanHdg > 20 ? "OK"
                       : "solid";

        _rows.Add($"    {g.Authority,4:F2}  {g.Rate,4:F2}  {g.Return,4:F2}   " +
                  $"{g.PedalAuthority,4:F2}  {g.PedalRate,4:F2}  {g.PedalReturn,4:F2}    | " +
                  $"{worstTilt,6:F1}/{meanTilt,-6:F1}  {worstHdg,6:F0}/{meanHdg,-6:F0} " +
                  $"{_accDrift / Repeats,5:F0} {_accAlt / Repeats,4:F0} | {verdict}");
        GD.Print(_rows[^1]);
    }

    private void Finish()
    {
        GD.Print("");
        GD.Print("  tilt is the angle between the aircraft's own up and the world's, so it");
        GD.Print("  cannot wrap the way Euler roll does. Drift, altitude and heading errors");
        GD.Print("  are the worst reached while holding, in m, m and deg.");
        GD.Print("====================================================================");
        GetTree().Quit(0);
    }
}
