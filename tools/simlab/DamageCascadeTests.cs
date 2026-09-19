using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The damage system's clocks, measured.
///
/// <para>Everything here asks the same question in a different place: <b>how many minutes
/// does this failure give the pilot?</b> D-007 says the fuel-and-wear loop is core but
/// forgiving, and "forgiving" is not a feeling, it is a number of minutes. A gearbox that
/// seizes in forty seconds is a gotcha; one that gives twelve minutes is a decision about
/// where to put down. These tests pin those minutes so that a tuning change to a cooling
/// coefficient cannot quietly turn the second into the first.</para>
///
/// <para>The clock tests drive <c>DamageState.UpdateSystems</c> directly at one-second
/// steps instead of flying the aircraft, for two reasons. They have to cover twenty
/// simulated minutes and a 240 Hz flight model would take 288,000 steps to do it; and a
/// clock measured through an autopilot is really measuring the autopilot. The load handed
/// to them is not invented - <see cref="MeasureCruiseLoad"/> flies the real aircraft and
/// reads what a cruise actually costs, and that measurement is printed alongside.</para>
///
/// <para><see cref="InFlightCascade"/> is the one that goes the long way round, through the
/// whole flight model, and checks that the couplings are real when nothing is being handed
/// to them by hand.</para>
/// </summary>
public static class DamageCascadeTests
{
    const double ClockDt = 1.0;          // seconds per step for the clock tests
    const double MaxMinutes = 40.0;      // give up after this; no clock here should be longer

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// What a real cruise costs this aircraft: power through the gearbox, torque as a
    /// fraction of the placard, and power as a fraction of what the engine has.
    ///
    /// Measured rather than assumed. Every clock below is a function of these three and
    /// they are the difference between "a gearbox under load" and a number I liked.
    /// </summary>
    static SystemLoad MeasureCruiseLoad(double speedKt = 80, double altitude = 300)
    {
        var heli = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        heli.InvalidateMass();
        heli.PlaceInFlightTrimmed(altitude, speedKt * 0.5144);

        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand
        {
            Altitude = altitude,
            ForwardSpeed = speedKt * 0.5144,
            LateralSpeed = 0,
            Heading = 0,
        };
        for (int i = 0; i < (int)(30 / Scenarios.Dt); i++)
        {
            heli.Input = ap.Update(heli, demand, Scenarios.Dt);
            heli.Step(Scenarios.Dt);
        }

        double nominal = heli.Airframe.MainRotor.NominalOmega;

        // The rev-averaged breakdown, not Telemetry.PowerRequired. PowerRequired is an
        // instantaneous shaft torque times omega, and a two-bladed rotor puts a violent
        // 2/rev into shaft torque (D-043): sampling it at the end of a run gave 292 kW for
        // a cruise that the torque gauge, in the same frame, said was 570 kW. The first
        // version of this test believed the 292 and every clock below it was twice as long
        // as it should have been.
        double shaft = Math.Max(heli.Telemetry.MainRotorPower, 0)
                       + Math.Max(heli.Telemetry.TailRotorPower, 0)
                       + Math.Max(heli.Telemetry.DrivetrainPower, 0);
        return new SystemLoad(
            RotorFraction: heli.RotorOmega / nominal,
            TorqueFraction: heli.Telemetry.TorquePercent / 100.0,
            ShaftPowerW: shaft,
            PowerFraction: shaft / Math.Max(heli.Telemetry.PowerAvailable, 1.0),
            AmbientTempC: 15.0,
            EngineRunning: true);
    }

    static SystemLoad ShutDown(in SystemLoad l)
        => new(RotorFraction: 0, TorqueFraction: 0, ShaftPowerW: 0, PowerFraction: 0,
               AmbientTempC: l.AmbientTempC, EngineRunning: false);

    static string Panel(DamageState d)
    {
        var lit = d.WarningPanel();
        return lit.Count == 0 ? "-" : string.Join(" ", lit);
    }

    static void ClockHeader(string what)
    {
        Console.WriteLine($"   min   {what}");
    }

    // ------------------------------------------------------- 1. the gearbox clock

    /// <summary>
    /// A gearbox losing oil, and how long the pilot has.
    ///
    /// The ladder this asserts is the real one, and it is the whole point of splitting
    /// health from the fluids: <b>temperature moves first</b> (the cooler loses
    /// circulation long before the pump loses suction), <b>pressure and the chip light
    /// come much later</b>, and <b>unserviceable is later again</b>. The pilot who watches
    /// the temperature gauge gets minutes of notice; the pilot who waits for a caption
    /// gets fewer; the one who waits for the chip light has already lost the gearbox.
    /// </summary>
    public static string? TransmissionOilLoss()
    {
        var cruise = MeasureCruiseLoad();
        Console.WriteLine($"  cruise load, measured: {cruise.ShaftPowerW / 1000:F0} kW through the gearbox, " +
                          $"{cruise.TorqueFraction * 100:F0}% torque, {cruise.PowerFraction * 100:F0}% of available power, " +
                          $"Nr {cruise.RotorFraction * 100:F0}%");
        Console.WriteLine();

        var cases = new (string label, double health)[]
        {
            ("wear          0.85", 0.85),
            ("cracked case  0.50", 0.50),
            ("fragment      0.35", 0.35),
        };

        var firstCaution = new Dictionary<string, double>();
        var firstCaption = new Dictionary<string, double>();
        var unserviceable = new Dictionary<string, double>();
        DamageState? moderate = null;

        foreach (var (label, health) in cases)
        {
            var d = new DamageState();
            d.SetHealth(Component.Transmission, health);
            d.UpdateSystems(cruise, 1e-3);   // prime the gauges to a running aircraft

            Console.WriteLine($"  {label}");
            ClockHeader("oil%   temp C  trend C/min  to limit  press psi  health   panel");

            double tCaution = double.NaN, tCaption = double.NaN, tDead = double.NaN;
            int steps = (int)(MaxMinutes * 60 / ClockDt);
            for (int i = 0; i <= steps; i++)
            {
                double minutes = i * ClockDt / 60.0;

                var gauges = d.Gauges();
                bool tempInCaution = gauges[0].InCaution;
                var lit = d.WarningPanel();

                if (tempInCaution && double.IsNaN(tCaution)) tCaution = minutes;
                if (lit.Count > 0 && double.IsNaN(tCaption)) tCaption = minutes;
                if (d.Health(Component.Transmission) <= DamageState.TransmissionFloor && double.IsNaN(tDead))
                    tDead = minutes;

                if (i % 120 == 0 || (!double.IsNaN(tDead) && Math.Abs(minutes - tDead) < 1e-9))
                {
                    string toLimit = double.IsPositiveInfinity(d.XmsnMinutesToLimit)
                        ? "   --" : $"{d.XmsnMinutesToLimit,5:F1}";
                    Console.WriteLine($"  {minutes,5:F1}   {d.XmsnOilFraction * 100,4:F0}  {d.XmsnOilTempC,7:F1} " +
                                      $"{d.XmsnOilTempTrendPerMin,11:F2}  {toLimit}    " +
                                      $"{d.XmsnOilPressurePsi,7:F0}   {d.Health(Component.Transmission),6:F3}   {Panel(d)}");
                }

                if (!double.IsNaN(tDead)) break;
                d.UpdateSystems(cruise, ClockDt);
            }

            Console.WriteLine($"         temp in caution at {Fmt(tCaution)}, first caption at {Fmt(tCaption)}, " +
                              $"unserviceable at {Fmt(tDead)}");
            Console.WriteLine();

            firstCaution[label] = tCaution;
            firstCaption[label] = tCaption;
            unserviceable[label] = tDead;
            if (health == 0.50) moderate = d;
        }

        // --- assertions -------------------------------------------------------

        // Wear is not a problem. A gearbox at 0.85 has been flown a lot; it must not cook
        // itself inside a sortie, or the player learns that condition is a countdown and
        // stops caring about the difference between worn and damaged.
        if (!double.IsNaN(unserviceable["wear          0.85"]))
            return "a merely worn gearbox (0.85) destroyed itself within 40 minutes";
        if (!double.IsNaN(firstCaption["wear          0.85"]))
            return "a merely worn gearbox (0.85) lit a caption";

        // A cracked case is a problem with a clock on it.
        double modCaution = firstCaution["cracked case  0.50"];
        double modDead = unserviceable["cracked case  0.50"];
        if (double.IsNaN(modCaution) || modCaution < 1.0 || modCaution > 10.0)
            return $"a 0.50 gearbox put the temperature into caution at {Fmt(modCaution)} min; wanted 1-10";
        if (double.IsNaN(modDead) || modDead < 8.0 || modDead > 25.0)
            return $"a 0.50 gearbox became unserviceable at {Fmt(modDead)} min; wanted 8-25";

        // A fragment through the case is the same failure with less time, and it must
        // still be enough to reach a field: 4 minutes at the measured 100 kt cruise is
        // about 6 nautical miles of choices (D-003a, D-007).
        double sevDead = unserviceable["fragment      0.35"];
        if (double.IsNaN(sevDead) || sevDead < 4.0 || sevDead > 15.0)
            return $"a 0.35 gearbox became unserviceable at {Fmt(sevDead)} min; wanted 4-15";
        if (sevDead >= modDead)
            return "a worse gearbox lasted at least as long as a better one";

        // The warning must precede the damage, or the gauges are decoration.
        if (!(modCaution < unserviceable["cracked case  0.50"]))
            return "the temperature caution did not arrive before the gearbox was unserviceable";
        if (moderate is null || !moderate.ChipLight)
            return "the gearbox was destroyed without the chip detector ever lighting";

        return null;
    }

    static string Fmt(double minutes) => double.IsNaN(minutes) ? "never" : $"{minutes:F1}";

    // ----------------------------------------------- 2. shutting down stops it

    /// <summary>
    /// The same failure, with the pilot doing the right thing.
    ///
    /// This is the test that makes the whole system a decision rather than a countdown.
    /// The gearbox is destroyed by metal-to-metal contact under load, so with the rotor
    /// stopped there is no load, no further damage, and the temperature falls. If this
    /// test ever fails, the damage model has become a timer and the correct play has
    /// become "ignore it", which is the opposite of what D-007 asks for.
    /// </summary>
    public static string? ShutdownStopsTheClock()
    {
        var cruise = MeasureCruiseLoad();
        var shut = ShutDown(cruise);

        var d = new DamageState();
        d.SetHealth(Component.Transmission, 0.50);

        // Fly it until the caption is lit and the gearbox has started to come apart, so
        // that there is something to stop.
        double flownMinutes = 0;
        for (int i = 0; i < (int)(MaxMinutes * 60 / ClockDt); i++)
        {
            d.UpdateSystems(cruise, ClockDt);
            flownMinutes = (i + 1) * ClockDt / 60.0;
            if (d.Health(Component.Transmission) < 0.42) break;
        }

        double healthAtTouchdown = d.Health(Component.Transmission);
        double tempAtTouchdown = d.XmsnOilTempC;
        double hobbs = d.RotorTurningSeconds;
        Console.WriteLine($"  flew {flownMinutes:F1} min with a 0.50 gearbox, then shut down");
        Console.WriteLine($"  Hobbs meter at shutdown: {hobbs / 60:F1} min of rotor-turning time, not yet booked");
        Console.WriteLine($"  at shutdown:  oil {d.XmsnOilFraction * 100:F0}%, {tempAtTouchdown:F1} C, " +
                          $"health {healthAtTouchdown:F3}, panel: {Panel(d)}");
        Console.WriteLine();
        ClockHeader("temp C   health   panel");

        for (int i = 0; i < (int)(30 * 60 / ClockDt); i++)
        {
            d.UpdateSystems(shut, ClockDt);
            double minutes = (i + 1) * ClockDt / 60.0;
            if (i % 300 == 299)
                Console.WriteLine($"  {minutes,5:F1}   {d.XmsnOilTempC,6:F1}   {d.Health(Component.Transmission),6:F3}   {Panel(d)}");
        }

        double drift = healthAtTouchdown - d.Health(Component.Transmission);
        double hoursCost = Salvage.WearOver(Component.Transmission, healthAtTouchdown, hobbs / 3600.0);
        Console.WriteLine($"  30 minutes shut down cost {drift:F5} of gearbox health, of which " +
                          $"{hoursCost:F5} is the flight hours being booked at shutdown;");
        Console.WriteLine($"  the consequential damage stopped dead, and the oil settled at {d.XmsnOilTempC:F1} C");

        if (healthAtTouchdown >= 0.50)
            return "the gearbox never started to come apart, so there was nothing to stop";
        if (hobbs < 60)
            return "the Hobbs meter did not run while the rotor was turning";
        if (hoursCost <= 0)
            return "shutting down did not book the flight hours onto the components";
        // Everything lost on the ground must be the hours, and nothing else. The ongoing
        // consequential damage has to be exactly zero, not merely small.
        if (drift > hoursCost * 1.05 + 1e-6)
            return $"shutting down did not stop the damage: {drift:F5} lost on the ground, " +
                   $"only {hoursCost:F5} of which is flight hours";
        if (d.XmsnOilTempC >= DamageState.XmsnOilTempLimitC)
            return $"the gearbox never cooled below the limit after shutdown ({d.XmsnOilTempC:F0} C)";
        if (!d.ChipLight)
            return "the chip light cleared itself on shutdown; it is supposed to latch until maintenance";

        // And the aircraft is still flyable afterwards: forgiving means you get to keep it.
        if (d.Health(Component.Transmission) <= DamageState.TransmissionFloor)
            return "the aircraft that landed in time was still written off";

        return null;
    }

    // ------------------------------------------------------ 3. hydraulic cliff

    /// <summary>
    /// A holed hydraulic line: a cliff, not a slope.
    ///
    /// The old model turned a 40% hit into 40% slower controls instantly and permanently,
    /// which is neither how hydraulics fail nor an interesting failure. A pump keeps up
    /// with a leak until the reservoir unports; the pilot gets a minute or two of entirely
    /// normal controls in which to decide where he is going, and then he is flying on
    /// manual reversion with no stability augmentation. The two go together because they
    /// are the same fluid - that pairing was already in this file and this failure is
    /// built on it.
    /// </summary>
    public static string? HydraulicLeak()
    {
        var cruise = MeasureCruiseLoad();

        Console.WriteLine("   hyd health   normal-controls window   time to <1000 psi   slowdown after   SAS after");
        double windowAt45 = double.NaN, collapseAt45 = double.NaN;
        double slowdownAfter = 0, sasAfter = 1, damageDone = 0;

        foreach (double health in new[] { 0.80, 0.45, 0.20 })
        {
            var d = new DamageState();
            d.SetHealth(Component.Hydraulics, health);
            double start = d.Health(Component.Hydraulics);

            // "Normal controls" is defined on what the pilot feels, not on a pressure
            // number: the actuators are still doing what they did before the hit.
            //
            // Run past the caption rather than stopping at it. The first version of this
            // test broke the loop the moment the HYD PRESSURE caption lit and then read
            // the actuators, which measured the aircraft half way down the cliff (SAS
            // 0.31) and concluded the augmentation had survived. The reservoir has to be
            // empty before "what is left" means anything.
            double window = double.NaN, collapse = double.NaN;
            for (int i = 0; i < (int)(MaxMinutes * 60 / ClockDt); i++)
            {
                d.UpdateSystems(cruise, ClockDt);
                double sec = (i + 1) * ClockDt;
                if (d.ActuatorSlowdown > 1.5 && double.IsNaN(window)) window = sec;
                if (d.HydraulicPressurePsi < DamageState.HydraulicPressCautionPsi && double.IsNaN(collapse))
                    collapse = sec;
                if (d.HydraulicFluidFraction <= 0) break;
            }

            Console.WriteLine($"  {health,10:F2}   {(double.IsNaN(window) ? "never" : $"{window,6:F0} s"),22}   " +
                              $"{(double.IsNaN(collapse) ? "never" : $"{collapse,6:F0} s"),17}   " +
                              $"{d.ActuatorSlowdown,14:F2}   {d.ActuatorEffectiveness,9:F2}");

            if (Math.Abs(health - 0.45) < 1e-9)
            {
                windowAt45 = window;
                collapseAt45 = collapse;
                slowdownAfter = d.ActuatorSlowdown;
                sasAfter = d.ActuatorEffectiveness;
                damageDone = start - d.Health(Component.Hydraulics);
            }
        }

        var fresh = new DamageState();
        fresh.SetHealth(Component.Hydraulics, 0.45);
        Console.WriteLine();
        Console.WriteLine($"  at the moment of the hit: {fresh.HydraulicPressurePsi:F0} psi, " +
                          $"slowdown {fresh.ActuatorSlowdown:F2}, SAS {fresh.ActuatorEffectiveness:F2}");

        // The hit itself must not change how the aircraft handles. A 3000 psi system with
        // a hole in it is still a 3000 psi system until the fluid runs out.
        if (fresh.ActuatorSlowdown > 2.0)
            return $"the controls were already {fresh.ActuatorSlowdown:F2}x slow the instant the line was holed";

        if (double.IsNaN(windowAt45) || windowAt45 < 45)
            return $"a 0.45 hydraulic system only gave {windowAt45:F0} s of normal controls; wanted at least 45";
        if (double.IsNaN(collapseAt45) || collapseAt45 > 360)
            return $"a 0.45 hydraulic system took {Fmt(collapseAt45 / 60)} min to fail; that is not a cliff";
        if (slowdownAfter < 3.0)
            return $"losing all hydraulic pressure only slowed the controls to {slowdownAfter:F2}x";
        if (sasAfter > 0.15)
            return $"the stability augmentation survived the loss of hydraulic pressure ({sasAfter:F2})";

        // A leak is not progressive damage. The fluid goes, the component does not.
        if (damageDone > 0.02)
            return $"the leak itself destroyed {damageDone:F3} of hydraulic health; a leak empties a reservoir, it does not eat a pump";

        return null;
    }

    // -------------------------------------------------- 4. engine hot section

    /// <summary>
    /// The engine cooking itself, and the pilot's way out.
    ///
    /// A tired turbine makes less power, so the same task needs a higher fraction of what
    /// is left, so TOT is higher, so the turbine gets tireder. The loop is real and the
    /// escape from it is real too: ask for less power. This test measures the wear rate at
    /// full power and at reduced power, and asserts that backing off is worth doing -
    /// which is the entire reason the loop is in the game rather than being a scripted
    /// failure.
    /// </summary>
    public static string? EngineHotSection()
    {
        Console.WriteLine("   engine   power   TOT C   over red   health lost/min   panel");

        // Flight hours are booked at shutdown, not in flight (see
        // DamageState.AccrueFlightHours), so the rotor never stops in these runs and the
        // only wear on show is the hot section's. A healthy engine at full power must
        // therefore lose exactly nothing.
        double baseline = 0, wearAtFull = 0, wearAtReduced = 0, totAtReduced = 0;

        foreach (var (health, powerFrac, label) in new (double, double, string)[]
        {
            (1.00, 1.00, "healthy, full power"),
            (0.55, 1.00, "worn, full power"),
            (0.55, 0.70, "worn, backed off"),
        })
        {
            var d = new DamageState();
            d.SetHealth(Component.Engine, health);
            var load = new SystemLoad(
                RotorFraction: 1.0, TorqueFraction: 0.55, ShaftPowerW: 600_000,
                PowerFraction: powerFrac, AmbientTempC: 15.0, EngineRunning: true);

            // Two minutes, not five. The hot-section loop feeds itself, so a long window
            // measures a runaway rather than the rate at the health the row is labelled
            // with - the first version ran a 0.55 engine all the way to zero and reported
            // the average as if it were a rate.
            for (int i = 0; i < 60; i++) d.UpdateSystems(load, ClockDt);   // let TOT settle
            double before = d.Health(Component.Engine);
            for (int i = 0; i < 120; i++) d.UpdateSystems(load, ClockDt);
            double lostPerMin = (before - d.Health(Component.Engine)) / 2.0;

            Console.WriteLine($"  {health,7:F2}  {powerFrac * 100,5:F0}%  {d.TurbineOutletTempC,6:F0}   " +
                              $"{Math.Max(0, d.TurbineOutletTempC - DamageState.TotLimitC),8:F0}   " +
                              $"{lostPerMin,15:F5}   {Panel(d)}     ({label})");

            if (health > 0.99)
            {
                baseline = lostPerMin;
                if (d.TurbineOutletTempC >= DamageState.TotLimitC)
                    return $"a healthy engine at full power sat at {d.TurbineOutletTempC:F0} C, over the red line";
            }
            else if (powerFrac > 0.99) wearAtFull = lostPerMin - baseline;
            else { wearAtReduced = lostPerMin - baseline; totAtReduced = d.TurbineOutletTempC; }
        }

        Console.WriteLine();
        Console.WriteLine($"  a healthy engine held at full power loses {baseline:F5} of health per minute.");
        Console.WriteLine($"  a 0.55 engine held at full power loses {wearAtFull:F5} per minute,");
        Console.WriteLine($"  which is {(wearAtFull > 0 ? ((0.55 - DamageState.EngineFloor) / wearAtFull).ToString("F0") : "inf")} " +
                          $"minutes to unserviceable if the pilot never backs off.");

        if (baseline > 0)
            return $"a healthy engine inside its limits wore itself out at {baseline:F5}/min; " +
                   "nothing should charge a pilot for flying the aircraft as placarded";
        if (wearAtFull <= 0)
            return "a worn engine at full power did not wear faster than a healthy one; the hot-section loop is not connected";

        double minutesToDead = (0.55 - DamageState.EngineFloor) / wearAtFull;
        if (minutesToDead < 5.0)
            return $"a 0.55 engine held at full power died in {minutesToDead:F1} minutes; that is a gotcha, not a clock";
        if (minutesToDead > 45.0)
            return $"a 0.55 engine held at full power took {minutesToDead:F0} minutes to die; nobody will ever notice";

        if (wearAtReduced >= wearAtFull * 0.5)
            return $"backing off to 70% power barely helped ({wearAtReduced:F4} vs {wearAtFull:F4} per minute)";
        if (totAtReduced >= DamageState.TotLimitC)
            return $"backing off to 70% power left TOT at {totAtReduced:F0} C, still over the red line";

        return null;
    }

    // ------------------------------------------------- 5. the whole aeroplane

    /// <summary>
    /// The couplings, through the flight model, with nothing handed to them.
    ///
    /// The clock tests above feed <c>UpdateSystems</c> a load by hand. This one flies the
    /// aircraft and lets the load emerge, and checks four things that no line of Damage.cs
    /// asks for.
    ///
    /// <para><b>What this test learned the hard way.</b> It was first written to prove "a
    /// sick engine cooks the gearbox" by cruising two aircraft that differed only in engine
    /// health. That is not how the coupling works. An engine with power to spare does not
    /// change the collective at all - the rotor needs what the rotor needs - so a 0.60
    /// engine at 80 kt, where 502 kW is wanted out of 770 kW available, flies
    /// indistinguishably from a new one. The coupling is real but it is <i>conditional</i>:
    /// it appears when the engine is short, because then the rotor droops and the pilot has
    /// to pull more pitch to make the same thrust at lower Nr. So the comparison is made
    /// where the engine is genuinely short - an out-of-ground-effect hover - and it shows
    /// up immediately.</para>
    ///
    /// <para>The first version also confounded itself a second way: it damaged the gearbox
    /// to 0.45 in <i>both</i> aircraft, which drops the torque ceiling to 64%, which made
    /// both of them torque-limited and neither of them a control.</para>
    /// </summary>
    public static string? InFlightCascade()
    {
        const double speed = 80 * 0.5144;
        const double alt = 300;

        static (double collective, double power, double torque, double temp,
                double xmsnLost, double hydLost, double vib, string panel)
            Fly(double engine, double xmsn, double rotor, double seconds, double fwd)
        {
            var heli = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 700 };
            heli.Damage.SetHealth(Component.Engine, engine);
            heli.Damage.SetHealth(Component.Transmission, xmsn);
            heli.Damage.SetHealth(Component.MainRotor, rotor);
            heli.InvalidateMass();
            heli.PlaceInFlightTrimmed(alt, fwd);

            var ap = new Autopilot { CollectiveTrim = 0.55 };
            var demand = new AutopilotDemand { Altitude = alt, ForwardSpeed = fwd, LateralSpeed = 0, Heading = 0 };

            double collective = 0, power = 0, torque = 0, n = 0;
            int steps = (int)(seconds / Scenarios.Dt);
            for (int i = 0; i < steps; i++)
            {
                heli.Input = ap.Update(heli, demand, Scenarios.Dt);
                heli.Step(Scenarios.Dt);
                if (i > steps / 3)   // let the trim settle before averaging
                {
                    collective += heli.Actual.Collective;
                    power += heli.Telemetry.MainRotorPower + heli.Telemetry.TailRotorPower
                             + heli.Telemetry.DrivetrainPower;
                    torque += heli.Telemetry.TorquePercent;
                    n++;
                }
            }
            var d = heli.Damage;
            return (collective / n, power / n, torque / n, d.XmsnOilTempC,
                    xmsn - d.Health(Component.Transmission),
                    1.0 - d.Health(Component.Hydraulics), d.VibrationIps, Panel(d));
        }

        // --- 1. a serviceable aircraft must be left alone ---------------------
        var clean = Fly(1.00, 1.00, 1.00, 360, speed);
        Console.WriteLine($"  serviceable, six minutes of cruise: gearbox oil {clean.temp:F1} C, " +
                          $"{clean.torque:F0}% torque, {clean.xmsnLost:F5} gearbox health lost, panel: {clean.panel}");
        if (clean.temp < 60 || clean.temp > DamageState.XmsnOilTempCautionC)
            return $"a serviceable aircraft cruises with the gearbox oil at {clean.temp:F0} C; not a plausible reading";
        if (clean.panel != "-")
            return $"a serviceable aircraft lit the panel in a six minute cruise: {clean.panel}";
        if (clean.xmsnLost > 0.002)
            return $"a serviceable aircraft lost {clean.xmsnLost:F4} of gearbox in six minutes of cruise";

        // --- 2. a short engine loads the gearbox ------------------------------
        // Hover, where the engine has no margin to hide behind. Ninety seconds, so that
        // wear does not contaminate the measurement.
        Console.WriteLine();
        Console.WriteLine("   engine   collective   torque %   gearbox kW    (90 s OGE hover at 300 m)");
        var hoverGood = Fly(1.00, 1.00, 1.00, 90, 0);
        var hoverSick = Fly(0.55, 1.00, 1.00, 90, 0);
        foreach (var (label, r) in new[] { ("1.00", hoverGood), ("0.55", hoverSick) })
            Console.WriteLine($"  {label,7}   {r.collective,10:F3}   {r.torque,8:F1}   {r.power / 1000,10:F0}");

        Console.WriteLine($"  a 0.55 engine in the hover: {hoverSick.collective - hoverGood.collective:+0.000;-0.000} collective, " +
                          $"{hoverSick.torque - hoverGood.torque:+0.0;-0.0} points of torque through the gearbox");

        if (hoverSick.collective <= hoverGood.collective)
            return "a short engine did not make the pilot hold more collective; there is no cascade to have";
        if (hoverSick.torque <= hoverGood.torque)
            return $"more collective did not load the gearbox harder ({hoverSick.torque:F1}% vs {hoverGood.torque:F1}%)";

        // --- 3. a leaking gearbox cooks itself, through the whole flight model -
        Console.WriteLine();
        var leak = Fly(1.00, 0.45, 1.00, 480, speed);
        Console.WriteLine($"  a 0.45 gearbox, eight minutes of cruise: oil {leak.temp:F0} C, " +
                          $"{leak.xmsnLost:F3} gearbox health lost, {leak.hydLost:F3} hydraulics lost, panel: {leak.panel}");
        if (leak.temp <= DamageState.XmsnOilTempLimitC)
            return $"a 0.45 gearbox flown for eight minutes never passed the red line ({leak.temp:F0} C)";
        if (leak.xmsnLost < 0.05)
            return $"a 0.45 gearbox flown past the red line for minutes only lost {leak.xmsnLost:F3} of health";
        if (leak.hydLost <= 0)
            return "the gearbox came apart without taking the hydraulic pump drive with it";

        // --- 4. vibration chafes what is bolted around it ---------------------
        Console.WriteLine();
        var shake = Fly(1.00, 1.00, 0.40, 600, speed);
        Console.WriteLine($"  a 0.40 rotor, ten minutes of cruise: {shake.vib:F2} ips, " +
                          $"{shake.hydLost:F4} hydraulics lost, panel: {shake.panel}");

        if (shake.vib < DamageState.VibrationCautionIps)
            return $"a 0.40 main rotor only produced {shake.vib:F2} ips of vibration";
        if (shake.hydLost <= 0)
            return "ten minutes of heavy vibration chafed nothing";
        if (shake.hydLost > 0.15)
            return $"ten minutes of vibration cost {shake.hydLost:F3} of hydraulic health; that is a death sentence, not a reason to go home";

        return null;
    }

    // ---------------------------------------------------------- 6. legibility

    /// <summary>
    /// The panel, across four aircraft, as the player would read it.
    ///
    /// The requirement is that "this is wear", "this is a problem" and "this is about to
    /// kill you" are three visibly different panels, and that none of them is a number
    /// between nought and one. This test prints what the pilot sees and asserts the three
    /// states are actually distinguishable - specifically that a worn aircraft lights
    /// nothing (or the game teaches the player to ignore captions) and that a dying one
    /// lights several.
    /// </summary>
    public static string? WarningPanelLadder()
    {
        var cruise = MeasureCruiseLoad();

        var states = new (string label, Action<DamageState> setup, double minutes)[]
        {
            ("serviceable",   d => { }, 5),
            ("worn",          d => { d.SetHealth(Component.Engine, 0.70);
                                     d.SetHealth(Component.Transmission, 0.80);
                                     d.SetHealth(Component.MainRotor, 0.75); }, 5),
            ("a problem",     d => { d.SetHealth(Component.Transmission, 0.50); }, 7),
            ("about to kill", d => { d.SetHealth(Component.Transmission, 0.30);
                                     d.SetHealth(Component.Engine, 0.45);
                                     d.SetHealth(Component.Hydraulics, 0.40);
                                     d.SetHealth(Component.MainRotor, 0.35); }, 9),
        };

        var lits = new List<int>();
        var totReadings = new List<double>();
        var vibReadings = new List<double>();

        foreach (var (label, setup, minutes) in states)
        {
            var d = new DamageState();
            setup(d);
            for (int i = 0; i < (int)(minutes * 60 / ClockDt); i++) d.UpdateSystems(cruise, ClockDt);

            Console.WriteLine($"  --- {label} (after {minutes:F0} min of cruise) " + new string('-', 30 - label.Length));
            foreach (var g in d.Gauges())
            {
                string mark = g.PastLimit ? "RED" : g.InCaution ? "amber" : "";
                Console.WriteLine($"     {g.Name,-15} {g.Value,8:F1} {g.Unit,-4} {mark}");
            }
            string trend = double.IsPositiveInfinity(d.XmsnMinutesToLimit)
                ? "not rising" : $"{d.XmsnMinutesToLimit:F1} min to the red line";
            Console.WriteLine($"     XMSN trend      {d.XmsnOilTempTrendPerMin,8:F2} C/min  ({trend})");
            Console.WriteLine($"     CHIP            {(d.ChipLight ? "METAL" : "-")}");
            Console.WriteLine($"     panel:          {Panel(d)}");
            Console.WriteLine();

            lits.Add(d.WarningPanel().Count);
            totReadings.Add(d.TurbineOutletTempC);
            vibReadings.Add(d.VibrationIps);
        }

        Console.WriteLine($"  captions lit: serviceable {lits[0]}, worn {lits[1]}, a problem {lits[2]}, about to kill {lits[3]}");

        if (lits[0] != 0) return "a serviceable aircraft lit a caption";

        // Wear must be READABLE without being ANNOUNCED. This is the D-005a line: the
        // player is given a hotter turbine and a rougher aircraft, and is left to decide
        // what that means. If a worn machine lit captions the player would learn to ignore
        // the panel, and the panel is the whole warning system.
        if (lits[1] != 0)
            return "a worn but undamaged aircraft lit a caption; the player will learn to ignore the panel";
        if (totReadings[1] <= totReadings[0] + 5)
            return $"a worn engine read the same TOT as a new one ({totReadings[1]:F0} vs {totReadings[0]:F0} C); wear is invisible";
        if (vibReadings[1] <= vibReadings[0] + 0.05)
            return $"a worn rotor read the same vibration as a new one ({vibReadings[1]:F2} vs {vibReadings[0]:F2} ips)";

        if (lits[2] == 0) return "an aircraft with a gearbox leaking oil for seven minutes lit nothing";
        if (lits[3] <= lits[2]) return "the dying aircraft did not light more than the merely damaged one";

        return null;
    }
}
