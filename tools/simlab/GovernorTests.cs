using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The governor, measured: how far the rotor droops when the pilot pulls, how long it
/// takes to come back, what it costs when the fuel control is going, what a start costs
/// when it is done badly, and how much of the aircraft a hot high day takes away.
///
/// <para>These exist because rotor speed was the one gauge in the aircraft that never
/// moved. A governor that holds 100% in every condition is not a system, it is a constant,
/// and a constant cannot be flown, degraded, or lost - so the throttle was a switch and the
/// tachometer was decoration. The numbers below are the ones that decide whether that is
/// still true.</para>
///
/// <para><b>Two instruments, on purpose.</b> <see cref="Droop"/>,
/// <see cref="ManualThrottle"/>, <see cref="ColdStart"/>, <see cref="HotStart"/> and
/// <see cref="DensityAltitude"/> fly the whole aircraft, because the question they ask is
/// what the pilot experiences. <see cref="Degraded"/> runs the powerplant on a bench
/// against a rotor-shaped load, because the question it asks - what does losing the
/// governor cost, holding everything else equal - cannot be asked of the aircraft: the
/// damage that takes the governor also takes engine power, and a droop measured with less
/// power under it is measuring both at once.</para>
/// </summary>
public static class GovernorTests
{
    const double Dt = 1.0 / 240.0;

    // ------------------------------------------------------------------ rigging

    static Helicopter MakeHeli(double isaDev = 0, double groundElevation = 0, double fuelKg = 500)
    {
        var env = new FlatEnvironment { GroundElevation = groundElevation };
        env.Atmosphere.IsaDeviation = isaDev;
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = fuelKg };
        heli.InvalidateMass();
        return heli;
    }

    static double NrFraction(Helicopter h) => h.RotorOmega / h.Airframe.MainRotor.NominalOmega;

    /// <summary>What a collective pull did to the rotor.</summary>
    struct Pull
    {
        public double SettledNr;      // fraction of nominal before the pull
        public double SettledTorque;  // fraction of the placard before the pull
        public double MinNr;          // deepest droop
        public double TimeToMin;      // s after the pull began
        public double SteadyNr;       // mean over the last two seconds
        public double Recovery;       // s from the pull to back within 0.3% of SteadyNr
        public double PeakTorque;
        public double EndAltitude;
        public bool Flyable;          // still upright, still in one piece, rotor still turning
    }

    /// <summary>
    /// Stabilise the aircraft in an out-of-ground-effect hover, freeze the collective, then
    /// pull it and watch the tachometer.
    ///
    /// The autopilot holds attitude, heading and position throughout and is given the
    /// collective as a fixed demand, so it cannot quietly undo the pull being measured. A
    /// helicopter with its controls genuinely frozen departs in about ten seconds - the
    /// first version of this measurement was reading a hover that had become a 20 m/s
    /// descent, and calling the resulting rotor decay droop.
    /// </summary>
    /// <param name="onStep">
    /// Called every step with the aircraft and the time relative to the pull, which is
    /// negative during the thirteen seconds of settling. It runs after the autopilot has
    /// written <c>heli.Input</c> and before the step, so it can have the last word on the
    /// controls - which is how the pilot in <see cref="ManualThrottle"/> gets his hand on
    /// the throttle.
    /// </param>
    static Pull MeasurePull(Helicopter heli, double altitude, double step,
                            double seconds = 14, Action<Helicopter, double>? onStep = null)
    {
        heli.PlaceInFlightTrimmed(altitude, 0);
        var ap = new Autopilot { CollectiveTrim = heli.Input.Collective };
        var hold = new AutopilotDemand
        {
            Altitude = altitude,
            ForwardSpeed = 0,
            LateralSpeed = 0,
            Heading = 0,
        };

        // Let the vertical loop find the collective that actually holds this hover, then
        // hand that number back to it as a fixed demand so the pull is the only thing
        // moving.
        int settle = (int)(10 / Dt), freeze = (int)(3 / Dt);
        for (int i = 0; i < settle; i++)
        {
            heli.Input = ap.Update(heli, hold, Dt);
            onStep?.Invoke(heli, (i - settle - freeze) * Dt);
            heli.Step(Dt);
        }
        double trimCollective = ap.CollectiveTrim;
        var frozen = hold;
        frozen.Collective = trimCollective;
        frozen.Altitude = null;

        for (int i = 0; i < freeze; i++)
        {
            heli.Input = ap.Update(heli, frozen, Dt);
            onStep?.Invoke(heli, (i - freeze) * Dt);
            heli.Step(Dt);
        }

        var r = new Pull
        {
            SettledNr = NrFraction(heli),
            SettledTorque = heli.Telemetry.TorquePercent / 100.0,
            MinNr = double.MaxValue,
            Recovery = double.NaN,
        };

        var pulled = frozen;
        pulled.Collective = Math.Clamp(trimCollective + step, 0, 1);

        double tailSum = 0; int tailN = 0;
        var trace = new List<(double t, double nr)>();
        for (double t = 0; t < seconds; t += Dt)
        {
            heli.Input = ap.Update(heli, pulled, Dt);
            onStep?.Invoke(heli, t);
            heli.Step(Dt);

            double nr = NrFraction(heli);
            trace.Add((t, nr));
            if (nr < r.MinNr) { r.MinNr = nr; r.TimeToMin = t; }
            r.PeakTorque = Math.Max(r.PeakTorque, heli.Telemetry.TorquePercent / 100.0);
            if (t > seconds - 2.0) { tailSum += nr; tailN++; }
        }
        r.SteadyNr = tailN > 0 ? tailSum / tailN : r.MinNr;
        foreach (var (t, nr) in trace)
            if (t > r.TimeToMin && nr >= r.SteadyNr - 0.005) { r.Recovery = t; break; }

        r.EndAltitude = heli.State.Altitude;
        r.Flyable = NrFraction(heli) > 0.80
                    && Math.Abs(heli.State.Orientation.Roll) < 0.6
                    && Math.Abs(heli.State.Orientation.Pitch) < 0.6
                    && heli.State.Altitude > 5;
        return r;
    }

    // ------------------------------------------------------- 1. droop under load

    /// <summary>
    /// Pull the collective and measure what the rotor does about it.
    ///
    /// The characteristic thing about flying a turbine helicopter is that power does not
    /// arrive when you ask for it. The anticipator opens the fuel valve as the lever comes
    /// up, but the gas generator still has to accelerate, and in the second or so that
    /// takes, the rotor is paying for the extra lift out of its own inertia. The pilot
    /// learns the size of that sag and leads it; a pilot who does not learn it arrives at
    /// the bottom of an approach with the needle low and nothing left to pull.
    ///
    /// The bar here is character, not a single number: a firm pull must produce droop a
    /// pilot can see, it must come back on its own within a few seconds, and it must never
    /// run away.
    /// </summary>
    public static string? Droop()
    {
        Console.WriteLine("  hover OGE at 300 m, collective frozen at trim, then pulled");
        Console.WriteLine();
        Console.WriteLine("   pull    Nr before   torque before    min Nr    at s    recovered s    steady Nr   peak torque");

        var steps = new[] { 0.03, 0.05, 0.08 };
        var results = new Pull[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            var heli = MakeHeli();
            results[i] = MeasurePull(heli, 300, steps[i], 16);
            var r = results[i];
            Console.WriteLine($"  +{steps[i],5:F2} {r.SettledNr * 100,11:F1} {r.SettledTorque * 100,15:F0} " +
                              $"{r.MinNr * 100,9:F1} {r.TimeToMin,8:F2} {r.Recovery,14:F2} " +
                              $"{r.SteadyNr * 100,12:F1} {r.PeakTorque * 100,13:F0}");
        }
        Console.WriteLine();

        var firm = results[1];
        double droopPct = (firm.SettledNr - firm.MinNr) * 100;
        Console.WriteLine($"  a firm pull (+0.05 lever, a third of the power margin) droops {droopPct:F1}% of Nr");
        Console.WriteLine($"  and is back within half a percent of its new governed speed {firm.Recovery:F1} s later.");
        Console.WriteLine($"  the +0.08 pull asks for more than the engine has: it sits on the ceiling at " +
                          $"{results[2].PeakTorque * 100:F0}% torque and trades the rest for climb.");

        for (int i = 0; i < steps.Length; i++)
        {
            if (!results[i].Flyable)
                return $"the +{steps[i]:F2} pull left the aircraft unflyable";
            if (results[i].MinNr < 0.88)
                return $"the +{steps[i]:F2} pull drooped Nr to {results[i].MinNr * 100:F0}% - " +
                       "that is not droop, that is losing the rotor";
        }

        if (droopPct < 1.5)
            return $"a firm collective pull droops Nr by {droopPct:F1}% - the governor is still hiding the rotor";
        if (droopPct > 8.0)
            return $"a firm collective pull droops Nr by {droopPct:F1}%, which is a rotor the pilot cannot get back";
        if (double.IsNaN(firm.Recovery) || firm.Recovery > 6.0)
            return $"Nr took {firm.Recovery:F1} s to recover from a firm pull - the governor is not recovering it";

        // Droop has to grow with how hard you pull, or it is a scripted dip rather than an
        // energy balance.
        double d0 = results[0].SettledNr - results[0].MinNr;
        double d2 = results[2].SettledNr - results[2].MinNr;
        if (d2 <= d0)
            return $"a +0.12 pull droops {d2 * 100:F1}% and a +0.04 pull droops {d0 * 100:F1}% - " +
                   "droop does not scale with the pull";

        return null;
    }

    // --------------------------------------------------- 2. a governor going bad

    /// <summary>
    /// A bench: the powerplant, a rotor's worth of inertia, and a load that behaves like a
    /// rotor does - torque rising with the square of speed, and with how much pitch is on
    /// the blades.
    ///
    /// Not a substitute for flying the aircraft; an instrument for one question. Governor
    /// authority in the game falls out of engine and fuel system health, and both of those
    /// also take power away, so a droop measured on a damaged aircraft is measuring two
    /// changes at once. Here the only thing that moves is the fuel control.
    /// </summary>
    sealed class GovernorBench
    {
        readonly Powerplant _engine;
        readonly double _nominal;
        readonly double _inertia;
        readonly double _hoverTorque;

        public double Omega;

        public GovernorBench(Airframe af)
        {
            _engine = new Powerplant(af.Engine);
            _nominal = af.MainRotor.NominalOmega;
            _inertia = af.MainRotor.RotorInertia;
            _hoverTorque = 0.72 * af.Engine.TransmissionTorqueLimit;
            Omega = _nominal;
            _engine.SetRunning();
        }

        public double NrFraction => Omega / _nominal;
        public double TorqueFraction { get; private set; }

        /// <summary>One step. <paramref name="pitch"/> is 1 for the hover, more for a pull.</summary>
        public void Step(double dt, double pitch, double authority)
        {
            double speedRatio = Omega / _nominal;
            double load = _hoverTorque * pitch * speedRatio * speedRatio;
            var o = _engine.Update(Omega, _nominal, load, 1.0, dt, 1.0, 1.0, authority);
            TorqueFraction = o.TorquePercent;
            Omega = Math.Max(1.0, Omega + (o.ShaftTorque - load) / _inertia * dt);
        }
    }

    /// <summary>
    /// What a failing fuel control costs, measured as the pilot experiences it: a deeper
    /// sag, a longer wait, and a needle that will not sit still.
    ///
    /// The ladder is deliberately not a cliff (D-004). At three quarters authority the
    /// aircraft is merely a bit soft; at a third the pilot is leading the collective and
    /// watching the tachometer hunt; below <c>GovernorFailAuthority</c> the governor is out
    /// of the loop altogether and the throttle is a control again, which
    /// <see cref="ManualThrottle"/> measures. At every rung the rotor comes back.
    /// </summary>
    public static string? Degraded()
    {
        var af = Airframe.Workhorse();
        Console.WriteLine("  governor test cell: a rotor's inertia and a rotor-shaped load, engine power held at 100%");
        Console.WriteLine("  the pull is a 25% step in blade pitch, held");
        Console.WriteLine();
        Console.WriteLine("   authority    steady Nr    min Nr    droop    recovered s    hunt pk-pk");

        var authorities = new[] { 1.00, 0.70, 0.40, 0.15 };
        var droop = new double[authorities.Length];
        var recovery = new double[authorities.Length];
        var hunt = new double[authorities.Length];

        for (int i = 0; i < authorities.Length; i++)
        {
            double q = authorities[i];
            var bench = new GovernorBench(af);

            for (double t = 0; t < 20; t += Dt) bench.Step(Dt, 1.0, q);
            double before = bench.NrFraction;

            double min = double.MaxValue, tMin = 0;
            var trace = new List<(double t, double nr)>();
            for (double t = 0; t < 40; t += Dt)
            {
                bench.Step(Dt, 1.25, q);
                double nr = bench.NrFraction;
                trace.Add((t, nr));
                if (nr < min) { min = nr; tMin = t; }
            }

            double lo = double.MaxValue, hi = double.MinValue, sum = 0; int n = 0;
            foreach (var (t, nr) in trace)
                if (t > 25) { lo = Math.Min(lo, nr); hi = Math.Max(hi, nr); sum += nr; n++; }
            double steady = sum / n;

            double rec = double.NaN;
            foreach (var (t, nr) in trace)
                if (t > tMin && nr >= steady - 0.003) { rec = t; break; }

            droop[i] = before - min;
            recovery[i] = rec;
            hunt[i] = hi - lo;

            Console.WriteLine($"  {q,9:F2} {steady * 100,12:F1} {min * 100,9:F1} {droop[i] * 100,8:F1} " +
                              $"{rec,14:F2} {hunt[i] * 100,13:F2}");
        }
        Console.WriteLine();
        Console.WriteLine($"  losing three fifths of the governor costs {(droop[2] - droop[0]) * 100:F1} points of extra droop " +
                          $"and {recovery[2] - recovery[0]:F1} s of extra recovery");

        if (droop[1] <= droop[0] || droop[2] <= droop[1])
            return $"droop does not worsen as the governor does: {droop[0] * 100:F1}, {droop[1] * 100:F1}, {droop[2] * 100:F1}%";
        if (recovery[2] <= recovery[0])
            return "a degraded governor recovers Nr as fast as a healthy one, which makes it not degraded";
        if (double.IsNaN(recovery[2]) || droop[2] > 0.16)
            return $"a governor at 0.40 authority droops {droop[2] * 100:F1}% - past forgiving (D-004)";
        if (hunt[2] <= hunt[0])
            return "a degraded governor holds Nr as steadily as a healthy one - nothing is hunting";
        if (hunt[2] > 0.06)
            return $"a degraded governor hunts {hunt[2] * 100:F1}% peak to peak, which is an oscillation, not a wobble";
        if (hunt[0] > 0.01)
            return $"a healthy governor hunts {hunt[0] * 100:F2}% peak to peak in the steady state";
        return null;
    }

    // ------------------------------------------------ 3. flying without a governor

    /// <summary>
    /// The governor gone: the throttle meters fuel and the pilot holds Nr himself.
    ///
    /// Two runs of the same pull. In the first the lever stays where it was, which is what
    /// happens to a pilot who has not noticed; in the second a simple proportional pilot
    /// coordinates throttle with collective, which is what the procedure is. The gap
    /// between them is the whole skill, and the fact that the second one works is what
    /// makes this a system to fly rather than a failure to survive.
    /// </summary>
    public static string? ManualThrottle()
    {
        double uncoordinatedMin = 0, coordinatedMin = 0;

        for (int pass = 0; pass < 2; pass++)
        {
            bool coordinate = pass == 1;
            var heli = MakeHeli();

            // The lever position that holds the hover is not known in advance - it depends
            // on the weight, the day and the aircraft - so the pilot finds it the way a
            // pilot does, by watching the tachometer and moving his hand. The same hand
            // stops moving at the pull in the uncoordinated pass.
            double throttle = 0.75;
            var r = MeasurePull(heli, 300, 0.08, 14, (h, t) =>
            {
                // Switched out after the trim solve, not before: solving a trim with the
                // governor already gone would be solving for a rotor speed nobody is
                // holding.
                h.Engine.GovernorSwitch = false;

                if (t < 0 || coordinate)
                    throttle = Math.Clamp(throttle + (1.0 - NrFraction(h)) * 2.5 * Dt, 0, 1);

                var c = h.Input;
                c.Throttle = throttle;
                h.Input = c;
            });

            if (pass == 0) uncoordinatedMin = r.MinNr; else coordinatedMin = r.MinNr;
            Console.WriteLine($"  {(coordinate ? "throttle coordinated" : "throttle frozen     ")}: " +
                              $"hover Nr {r.SettledNr * 100:F1}%, min Nr {r.MinNr * 100:F1}%, " +
                              $"steady {r.SteadyNr * 100:F1}%, lever {throttle:F2}, " +
                              $"{(r.Flyable ? "flyable" : "DEPARTED")}");
        }
        Console.WriteLine();
        Console.WriteLine($"  coordinating the throttle is worth {(coordinatedMin - uncoordinatedMin) * 100:F1} points of Nr");

        if (uncoordinatedMin > 0.97)
            return $"with no governor and a frozen throttle, a collective pull only cost " +
                   $"{(1 - uncoordinatedMin) * 100:F1}% of Nr - the throttle is still a switch";
        if (coordinatedMin <= uncoordinatedMin)
            return "coordinating the throttle did not help, so manual throttle is not flyable";
        if (coordinatedMin < 0.90)
            return $"even with the throttle coordinated Nr fell to {coordinatedMin * 100:F0}% - " +
                   "manual throttle is a loss, not a skill (D-004)";
        return null;
    }

    // ------------------------------------------------------------- 4. cold starts

    /// <summary>The story of one start, run through the whole aircraft.</summary>
    struct StartRun
    {
        public double PeakTot;
        public double N1AtFuel;
        public double SecondsToIdle;
        public double EngineHealthLost;
        public bool Lit;
        public bool ReachedIdle;
        public bool Overtemp;
    }

    /// <summary>
    /// Motor the engine, put the fuel in at a chosen N1, and watch the TOT gauge.
    ///
    /// <paramref name="fuelAtN1"/> is the entire procedure and the entire failure mode.
    /// <paramref name="abortAboveTot"/> is the pilot's hand on the fuel lever: cut it and
    /// the fire goes out, which is the only thing that stops a hot start.
    /// </summary>
    static StartRun RunStart(double fuelAtN1, double abortAboveTot = double.PositiveInfinity,
                             bool trace = false, double reaction = 1.0)
    {
        var heli = MakeHeli();
        heli.PlaceOnGround(running: false);
        heli.Damage.RepairAll();
        var e = heli.Engine;
        e.EngageStarter();

        var r = new StartRun { N1AtFuel = double.NaN };
        double startHealth = heli.Damage.Health(Component.Engine);
        double t = 0;
        double nextPrint = 0;
        double noticed = double.NaN;

        while (t < 90)
        {
            if (!e.FuelValveOpen && !r.ReachedIdle && e.N1 >= fuelAtN1 && double.IsNaN(r.N1AtFuel))
            {
                e.OpenFuelValve();
                r.N1AtFuel = e.N1;
            }
            // A pilot is not a comparator. He sees the needle go past the red line, and
            // then a second of being a human being happens before his hand gets there.
            if (double.IsNaN(noticed) && heli.Damage.TurbineOutletTempC > abortAboveTot) noticed = t;
            if (e.FuelValveOpen && !double.IsNaN(noticed) && t >= noticed + reaction) e.CloseFuelValve();

            heli.Input = new Controls { Collective = 0, Throttle = 0.2 };   // flight idle
            heli.Step(Dt);
            t += Dt;

            r.PeakTot = Math.Max(r.PeakTot, heli.Damage.TurbineOutletTempC);
            if (e.Lit) r.Lit = true;
            if (!r.ReachedIdle && e.State == EngineState.Running)
            {
                r.ReachedIdle = true;
                r.SecondsToIdle = t;
            }
            if (trace && t >= nextPrint)
            {
                nextPrint += 2.0;
                Console.WriteLine($"   {t,5:F1} s   N1 {e.N1 * 100,5:F1}%   TOT {heli.Damage.TurbineOutletTempC,6:F0} C   " +
                                  $"Nr {NrFraction(heli) * 100,5:F1}%   {(e.Lit ? "lit" : e.StarterEngaged ? "motoring" : "-")}" +
                                  $"{(heli.Damage.WarningPanel().Contains("TOT") ? "   TOT" : "")}");
            }
            if (r.ReachedIdle && t > r.SecondsToIdle + 10) break;
            if (!e.FuelValveOpen && e.State == EngineState.Off && t > 20) break;
        }

        r.Overtemp = r.PeakTot >= DamageState.TotLimitC;
        r.EngineHealthLost = startHealth - heli.Damage.Health(Component.Engine);
        return r;
    }

    /// <summary>
    /// A start done properly: motor the compressor until there is air to burn the fuel in,
    /// then introduce it, and watch the temperature peak and fall back as the gas generator
    /// accelerates.
    ///
    /// The test is that patience is free and costs nothing but seconds. A cold morning
    /// start should be a ritual with stakes, and a ritual whose correct performance has a
    /// price is not a ritual, it is a tax.
    /// </summary>
    public static string? ColdStart()
    {
        Console.WriteLine("  a start by the book: fuel at 15% N1");
        var good = RunStart(0.15, trace: true);
        Console.WriteLine();
        Console.WriteLine($"  lit at N1 {good.N1AtFuel * 100:F0}%, peak TOT {good.PeakTot:F0} C " +
                          $"(caution {DamageState.TotCautionC:F0}, red line {DamageState.TotLimitC:F0}), " +
                          $"idle at {good.SecondsToIdle:F0} s, engine health lost {good.EngineHealthLost * 100:F2}%");
        Console.WriteLine();

        Console.WriteLine("  the same start with the fuel introduced at a range of gas generator speeds");
        Console.WriteLine();
        Console.WriteLine("   fuel at N1    peak TOT C    over red line    engine health lost");
        foreach (double n1 in new[] { 0.04, 0.06, 0.08, 0.11, 0.15, 0.20 })
        {
            var s = RunStart(n1);
            Console.WriteLine($"  {s.N1AtFuel * 100,10:F0}% {s.PeakTot,13:F0} {(s.Overtemp ? "yes" : "no"),16} " +
                              $"{s.EngineHealthLost * 100,21:F1}%");
        }

        if (!good.ReachedIdle) return "a correct start never reached idle";
        if (good.SecondsToIdle < 8 || good.SecondsToIdle > 45)
            return $"a start takes {good.SecondsToIdle:F0} s, which is not a turbine start";
        if (good.PeakTot >= DamageState.TotLimitC)
            return $"a correct start peaks at {good.PeakTot:F0} C, over the {DamageState.TotLimitC:F0} C red line";
        if (good.EngineHealthLost > 0.001)
            return $"a correct start cost {good.EngineHealthLost * 100:F2}% of the engine";
        return null;
    }

    /// <summary>
    /// A hot start: fuel into an engine that is barely turning, so there is far too little
    /// air for it and the difference leaves as temperature.
    ///
    /// Three things have to be true for this to be the right kind of failure. It has to be
    /// visible - the TOT gauge is the only warning and it arrives in seconds. It has to
    /// cost real money, or waiting for N1 is not a decision. And it has to be abortable,
    /// because a failure with no action available is not a system, it is a punishment
    /// (D-004): the pilot who pulls the fuel off when the needle goes past the red line
    /// keeps most of his engine.
    /// </summary>
    public static string? HotStart()
    {
        Console.WriteLine("  fuel introduced at 5% N1, and left in");
        var hot = RunStart(0.05, trace: true);
        Console.WriteLine();
        Console.WriteLine($"  peak TOT {hot.PeakTot:F0} C, engine health lost {hot.EngineHealthLost * 100:F1}%");
        Console.WriteLine();

        Console.WriteLine("  the same start, with the fuel pulled off a second after TOT passes the red line");
        var aborted = RunStart(0.05, abortAboveTot: DamageState.TotLimitC);
        Console.WriteLine($"  peak TOT {aborted.PeakTot:F0} C, engine health lost {aborted.EngineHealthLost * 100:F1}%, " +
                          $"{(aborted.ReachedIdle ? "engine reached idle anyway" : "start aborted")}");
        Console.WriteLine();

        var good = RunStart(0.15);
        Console.WriteLine($"  for comparison, by the book: peak TOT {good.PeakTot:F0} C, " +
                          $"engine health lost {good.EngineHealthLost * 100:F2}%");

        if (!hot.Lit) return "the hot start never lit, so nothing was measured";
        if (hot.PeakTot < DamageState.TotLimitC + 100)
            return $"a hot start peaked at {hot.PeakTot:F0} C, barely over the {DamageState.TotLimitC:F0} C " +
                   "red line - there is nothing here for the pilot to see";
        if (hot.EngineHealthLost < 0.04)
            return $"a hot start cost {hot.EngineHealthLost * 100:F1}% of the engine, which is not a reason to wait for N1";
        if (hot.EngineHealthLost > 0.30)
            return $"a hot start cost {hot.EngineHealthLost * 100:F0}% of the engine in one go - that is an instant loss, not a mistake (D-004)";
        if (aborted.EngineHealthLost >= hot.EngineHealthLost)
            return "pulling the fuel off made no difference, so there is nothing the pilot can do about a hot start";
        if (good.EngineHealthLost > 0.001)
            return $"a correct start cost {good.EngineHealthLost * 100:F2}% of the engine";
        return null;
    }

    // --------------------------------------------------------- 5. hot and high

    /// <summary>What the day costs, in the units the pilot has on the panel.</summary>
    struct DayResult
    {
        public double DensityAltitude;
        public double PowerAvailableKw;
        public double HoverTorqueFrac;
        public double HoverCollective;
        public double ClimbRateFpm;
        public double ClimbLever;
        public double ClimbTorque;
        public string StoppedBy;
        public bool CanHover;
    }

    /// <summary>
    /// Hover the aircraft out of ground effect on a given day, then ask it for everything
    /// it has and see how fast it goes up.
    /// </summary>
    static DayResult MeasureDay(double isaDev, double altitude)
    {
        var r = new DayResult();
        var heli = MakeHeli(isaDev, groundElevation: altitude - 500);
        heli.PlaceInFlightTrimmed(altitude, 0);

        var ap = new Autopilot { CollectiveTrim = heli.Input.Collective };
        var hold = new AutopilotDemand { Altitude = altitude, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        for (int i = 0; i < (int)(25 / Dt); i++)
        {
            heli.Input = ap.Update(heli, hold, Dt);
            heli.Step(Dt);
        }

        r.DensityAltitude = heli.Telemetry.DensityAltitude;
        r.PowerAvailableKw = heli.Telemetry.PowerAvailable / 1000.0;
        r.HoverTorqueFrac = heli.Telemetry.TorquePercent / 100.0;
        r.HoverCollective = ap.CollectiveTrim;
        r.CanHover = Math.Abs(heli.State.Altitude - altitude) < 15 && ap.CollectiveTrim < 0.98;

        // Everything it has. Not the lever on the stop - that is an overpitch, and it
        // measures the rotor stalling rather than the aircraft climbing: full collective at
        // sea level put the torque on the ceiling, drooped Nr through the floor and
        // produced a measured climb rate of minus eight hundred feet a minute. Instead,
        // raise the lever slowly until the placard or the governor says stop, and then see
        // what the aircraft does with it.
        var climb = hold;
        climb.Altitude = null;
        double lever = ap.CollectiveTrim;
        bool atLimit = false;
        double vs = 0; int vsN = 0;
        int ramp = (int)(40 / Dt);
        for (int i = 0; i < ramp; i++)
        {
            if (!atLimit)
            {
                lever = Math.Min(1.0, lever + 0.02 * Dt);
                if (heli.Telemetry.TorquePercent >= 99.0) { atLimit = true; r.StoppedBy = "placard"; }
                else if (NrFraction(heli) < 0.97) { atLimit = true; r.StoppedBy = "Nr"; }
                else if (lever >= 1.0) { atLimit = true; r.StoppedBy = "lever"; }
            }
            climb.Collective = lever;
            heli.Input = ap.Update(heli, climb, Dt);
            heli.Step(Dt);
            if (i > ramp - (int)(6 / Dt)) { vs += -heli.State.Velocity.Z; vsN++; }
        }
        r.ClimbLever = lever;
        r.ClimbTorque = heli.Telemetry.TorquePercent / 100.0;
        r.ClimbRateFpm = vs / Math.Max(vsN, 1) * 196.85;
        return r;
    }

    /// <summary>
    /// Density altitude, measured as a difference in the aircraft rather than a number on a
    /// chart.
    ///
    /// The engine loses power with density, the rotor loses thrust with density, and the
    /// two compound: the hot high machine needs more collective to hold a hover and has
    /// less left over when it gets there. The point of this test is that the difference is
    /// large enough to change what the pilot can accept as a job - if a hot high day costs
    /// ten percent of the climb rate, nobody ever has to think about it.
    /// </summary>
    public static string? DensityAltitude()
    {
        Console.WriteLine("   day                          density alt m   power kW   hover torque   hover lever   climb at the placard");

        var days = new (string label, double isaDev, double alt)[]
        {
            ("sea level, ISA",              0,  0),
            ("sea level, ISA +20 C",       20,  0),
            ("1500 m, ISA",                 0,  1500),
            ("1500 m, ISA +20 C",          20,  1500),
            ("2500 m, ISA +20 C",          20,  2500),
        };

        var results = new DayResult[days.Length];
        for (int i = 0; i < days.Length; i++)
        {
            results[i] = MeasureDay(days[i].isaDev, days[i].alt);
            var r = results[i];
            Console.WriteLine($"  {days[i].label,-28} {r.DensityAltitude,13:F0} {r.PowerAvailableKw,10:F0} " +
                              $"{r.HoverTorqueFrac * 100,13:F0}% {r.HoverCollective,13:F3} " +
                              $"{r.ClimbRateFpm,15:F0} fpm at {r.ClimbTorque * 100:F0}% torque, " +
                              $"stopped by {r.StoppedBy ?? "-"}" +
                              $"{(r.CanHover ? "" : "   <-- cannot hold the hover")}");
        }
        Console.WriteLine();

        var sl = results[0];
        var worst = results[^1];
        Console.WriteLine($"  a 2500 m hover on a 20 degree day is {worst.DensityAltitude - sl.DensityAltitude:F0} m of " +
                          $"density altitude. {(1 - worst.PowerAvailableKw / sl.PowerAvailableKw) * 100:F0}% less power, " +
                          $"{(worst.HoverTorqueFrac - sl.HoverTorqueFrac) * 100:F0} more points of torque to hold the hover,");
        Console.WriteLine($"  and the climb goes from {sl.ClimbRateFpm:F0} fpm to {worst.ClimbRateFpm:F0}: " +
                          "the lever runs out of rotor before it runs out of gearbox, which is what hot and high is.");

        if (results[1].DensityAltitude - sl.DensityAltitude < 200)
            return $"twenty degrees above ISA is only worth {results[1].DensityAltitude - sl.DensityAltitude:F0} m " +
                   "of density altitude - the atmosphere is not doing its job";
        if (worst.PowerAvailableKw >= sl.PowerAvailableKw * 0.85)
            return $"a hot high day costs only {(1 - worst.PowerAvailableKw / sl.PowerAvailableKw) * 100:F0}% of the power";
        if (worst.HoverTorqueFrac <= sl.HoverTorqueFrac + 0.05)
            return $"holding a hover hot and high costs only {(worst.HoverTorqueFrac - sl.HoverTorqueFrac) * 100:F0} " +
                   "points of torque, so the day does not change the aircraft";
        if (worst.ClimbRateFpm > sl.ClimbRateFpm * 0.6)
            return $"a hot high day costs only {(1 - worst.ClimbRateFpm / sl.ClimbRateFpm) * 100:F0}% of the climb rate";
        if (sl.ClimbRateFpm < 500)
            return $"the aircraft climbs at {sl.ClimbRateFpm:F0} fpm at sea level with the lever on the stop";
        return null;
    }
}
