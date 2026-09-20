using System.Diagnostics;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class Scenarios
{
    public const double Dt = 1.0 / 240.0;
    const double Deg = 180.0 / Math.PI;
    const double Kt = 1.94384;   // m/s -> knots
    const double Fpm = 196.85;   // m/s -> feet per minute

    static Helicopter MakeHeli(double fuelKg = 500, double isaDev = 0)
    {
        var env = new FlatEnvironment();
        env.Atmosphere.IsaDeviation = isaDev;
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = fuelKg };
        heli.InvalidateMass();
        return heli;
    }

    /// <summary>Fly the aircraft under autopilot for a while and return the final telemetry.</summary>
    static FlightTelemetry Fly(Helicopter heli, Autopilot ap, AutopilotDemand demand,
                               double seconds, Action<Helicopter>? onStep = null)
    {
        int steps = (int)(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            heli.Input = ap.Update(heli, demand, Dt);
            heli.Step(Dt);
            onStep?.Invoke(heli);
        }
        return heli.Telemetry;
    }

    // ------------------------------------------------------------------ mass

    public static string? MassProperties()
    {
        var heli = MakeHeli();
        var mass = heli.TotalMass;
        var cg = heli.CentreOfGravity;
        var inertia = heli.Inertia;

        Console.WriteLine($"  gross mass        {mass,8:F0} kg");
        Console.WriteLine($"  centre of gravity {cg}");
        Console.WriteLine($"  Ixx/Iyy/Izz       {inertia.M00,7:F0} / {inertia.M11,7:F0} / {inertia.M22,7:F0} kg.m2");
        Console.WriteLine($"  disc loading      {mass * Atmosphere.Gravity / heli.Airframe.MainRotor.DiscArea,8:F1} N/m2");
        Console.WriteLine($"  solidity          {heli.Airframe.MainRotor.Solidity,8:F4}");
        Console.WriteLine($"  tip speed         {heli.Airframe.MainRotor.NominalOmega * heli.Airframe.MainRotor.Radius,8:F1} m/s");

        if (mass < 2000 || mass > 6000) return $"gross mass {mass:F0} kg is not a medium helicopter";
        if (Math.Abs(cg.X) > 1.0) return $"CG {cg.X:F2} m from datum longitudinally - the aircraft would be untrimmable";
        double tip = heli.Airframe.MainRotor.NominalOmega * heli.Airframe.MainRotor.Radius;
        if (tip < 180 || tip > 250) return $"tip speed {tip:F0} m/s is outside the sane 190-240 band";
        return null;
    }

    // ----------------------------------------------------------------- hover

    public static string? HoverTrim()
    {
        var heli = MakeHeli();
        heli.PlaceInFlight(50);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 50, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };

        Fly(heli, ap, demand, 40);

        double altErr = heli.State.Altitude - 50;
        var t = heli.Telemetry;
        double vi = heli.Rotor.Inflow.Lambda0 * heli.RotorOmega * heli.Airframe.MainRotor.Radius;

        Console.WriteLine($"  altitude error    {altErr,8:F2} m");
        Console.WriteLine($"  collective        {heli.Input.Collective,8:F3}  ({t.CollectivePitchDeg:F1} deg)");
        Console.WriteLine($"  attitude          pitch {heli.State.Orientation.Pitch * Deg,6:F1}  roll {heli.State.Orientation.Roll * Deg,6:F1} deg");
        Console.WriteLine($"  pedal             {heli.Input.Pedal,8:F3}");
        Console.WriteLine($"  thrust            {t.Thrust,8:F0} N   (weight {heli.TotalMass * Atmosphere.Gravity:F0} N)");
        Console.WriteLine($"  induced velocity  {vi,8:F1} m/s");
        Console.WriteLine($"  power required    {t.PowerRequired / 1000,8:F0} kW of {t.PowerAvailable / 1000:F0} kW");
        Console.WriteLine($"  torque            {t.TorquePercent,8:F0} %");
        Console.WriteLine($"  Nr                {t.RotorRpmPercent,8:F1} %");
        Console.WriteLine($"  coning            {t.Coning * Deg,8:F2} deg");
        Console.WriteLine($"  fuel flow         {t.FuelFlow * 3600,8:F0} kg/h");

        if (Math.Abs(altErr) > 3) return $"could not hold altitude ({altErr:F1} m error)";
        if (heli.Input.Collective > 0.97) return "collective saturated - the rotor cannot lift the aircraft";
        if (vi < 8 || vi > 22) return $"hover induced velocity {vi:F1} m/s is not physical for this disc loading";
        if (t.PowerRequired < 200e3 || t.PowerRequired > t.PowerAvailable)
            return $"hover power {t.PowerRequired / 1000:F0} kW is implausible";
        if (Math.Abs(t.RotorRpmPercent - 100) > 4) return $"governor failed to hold Nr ({t.RotorRpmPercent:F1}%)";
        if (t.Coning * Deg < 1 || t.Coning * Deg > 12) return $"coning angle {t.Coning * Deg:F1} deg is wrong";
        return null;
    }

    // ----------------------------------------------------------- power curve

    public static string? PowerCurve()
    {
        Console.WriteLine("   IAS kt   power kW   collective   pitch deg   Nr %   blade loading");
        var results = new List<(double speed, double power)>();

        for (double speed = 0; speed <= 60; speed += 5)
        {
            var heli = MakeHeli();
            heli.PlaceInFlight(300, speed);
            var ap = new Autopilot { CollectiveTrim = 0.55 };
            var demand = new AutopilotDemand { Altitude = 300, ForwardSpeed = speed, LateralSpeed = 0, Heading = 0 };
            Fly(heli, ap, demand, 45);

            // Average over the last two seconds so the 2/rev ripple does not alias.
            double power = 0; int n = 0;
            for (int i = 0; i < (int)(2.0 / Dt); i++)
            {
                heli.Input = ap.Update(heli, demand, Dt);
                heli.Step(Dt);
                power += heli.Telemetry.PowerRequired; n++;
            }
            power /= n;
            var t = heli.Telemetry;
            results.Add((speed, power));
            Console.WriteLine($"  {speed * Kt,7:F1} {power / 1000,10:F0} {heli.Input.Collective,12:F3} " +
                              $"{heli.State.Orientation.Pitch * Deg,11:F1} {t.RotorRpmPercent,6:F1} {t.BladeLoading,15:F3}");
        }

        double hoverPower = results[0].power;
        var best = results.OrderBy(r => r.power).First();
        Console.WriteLine($"  minimum power at {best.speed * Kt:F0} kt ({best.power / 1000:F0} kW), hover {hoverPower / 1000:F0} kW");

        if (best.speed < 15 || best.speed > 35)
            return $"minimum-power speed {best.speed * Kt:F0} kt should be 30-65 kt - the power bucket is in the wrong place";
        if (best.power > hoverPower * 0.85)
            return "no meaningful power bucket: forward flight should be much cheaper than hovering";
        if (results[^1].power < best.power * 1.15)
            return "power does not rise again at high speed - parasite drag is too low";
        return null;
    }

    // ------------------------------------------------------ translational lift

    public static string? TranslationalLift()
    {
        // Hold a steady hover, then push the nose over with collective frozen. If
        // translational lift is modelled, the aircraft climbs as it accelerates.
        var heli = MakeHeli();
        heli.PlaceInFlight(200);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 200, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        Fly(heli, ap, demand, 40);

        double frozenCollective = heli.Input.Collective;
        double startAlt = heli.State.Altitude;
        Console.WriteLine($"  hover collective frozen at {frozenCollective:F3}, altitude {startAlt:F1} m");
        Console.WriteLine("    t s    IAS kt   alt m   ROC fpm   thrust N   lambda_i");

        double maxRoc = -999, rocAtEtl = 0, speedAtMaxRoc = 0;
        for (double t = 0; t < 30; t += Dt)
        {
            var d2 = new AutopilotDemand { PitchAttitude = -8 * Math.PI / 180, RollAttitude = 0, Heading = 0 };
            var c = ap.Update(heli, d2, Dt);
            c.Collective = frozenCollective;      // the whole point of the test
            heli.Input = c;
            heli.Step(Dt);

            double roc = -heli.State.Velocity.Z;
            if (roc > maxRoc) { maxRoc = roc; speedAtMaxRoc = heli.Telemetry.AirspeedTrue; }
            if (Math.Abs(heli.Telemetry.AirspeedTrue - 13) < 0.1 && rocAtEtl == 0) rocAtEtl = roc;

            if (Math.Abs(t % 3.0) < Dt)
                Console.WriteLine($"  {t,5:F1} {heli.Telemetry.AirspeedTrue * Kt,9:F1} {heli.State.Altitude,8:F1} " +
                                  $"{roc * Fpm,9:F0} {heli.Telemetry.Thrust,10:F0} {heli.Rotor.Inflow.Lambda0,10:F4}");
        }

        Console.WriteLine($"  peak rate of climb {maxRoc * Fpm:F0} fpm at {speedAtMaxRoc * Kt:F0} kt on hover collective");

        if (maxRoc < 1.0)
            return "no translational lift: accelerating from the hover on fixed collective produced no climb";
        if (speedAtMaxRoc * Kt < 20 || speedAtMaxRoc * Kt > 90)
            return $"translational lift peaks at {speedAtMaxRoc * Kt:F0} kt, which is not where effective translational lift lives";
        return null;
    }

    // --------------------------------------------------------- ground effect

    public static string? GroundEffect()
    {
        double CollectiveToHoverAt(double agl)
        {
            var heli = MakeHeli();
            heli.PlaceInFlight(agl);
            var ap = new Autopilot { CollectiveTrim = 0.55 };
            var demand = new AutopilotDemand { Altitude = agl, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
            Fly(heli, ap, demand, 45);
            return heli.Input.Collective;
        }

        double inGround = CollectiveToHoverAt(3.0);
        double outOfGround = CollectiveToHoverAt(120.0);
        Console.WriteLine($"  collective IGE (3 m)   {inGround:F4}");
        Console.WriteLine($"  collective OGE (120 m) {outOfGround:F4}");
        Console.WriteLine($"  saving                 {(outOfGround - inGround) * 100:F1} % of lever travel");

        if (inGround >= outOfGround)
            return "hovering in ground effect costs no less than out of ground effect";
        if (outOfGround - inGround < 0.005)
            return "ground effect is present but negligible";
        return null;
    }

    // -------------------------------------------------------- control signs

    public static string? CyclicResponse()
    {
        // Measure a proper control derivative: run the same trimmed aircraft twice, once
        // with +delta on the axis and once with -delta, and take the difference. A bare
        // helicopter departs within a second of the controls being frozen, so a one-sided
        // test measures that departure rather than the control response. Differencing
        // two runs from an identical state cancels it exactly.
        (double plus, double minus) Perturb(Func<Controls, double, Controls> apply,
                                            Func<Vec3, double> rate, double delta)
        {
            double Run(double d)
            {
                var heli = MakeHeli();
                heli.PlaceInFlight(300);
                var ap = new Autopilot { CollectiveTrim = 0.55 };
                var demand = new AutopilotDemand { Altitude = 300, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };

                // Trim, then average the autopilot output over two seconds: a single
                // sample catches the controller mid-oscillation.
                Fly(heli, ap, demand, 40);
                Controls trim = default; int n = 0;
                for (int i = 0; i < (int)(2.0 / Dt); i++)
                {
                    var c = ap.Update(heli, demand, Dt);
                    heli.Input = c;
                    heli.Step(Dt);
                    trim.Collective += c.Collective; trim.CyclicPitch += c.CyclicPitch;
                    trim.CyclicRoll += c.CyclicRoll; trim.Pedal += c.Pedal; n++;
                }
                trim.Collective /= n; trim.CyclicPitch /= n; trim.CyclicRoll /= n; trim.Pedal /= n;
                trim.Throttle = 1.0;

                double sum = 0; int m = 0;
                for (double t = 0; t < 0.7; t += Dt)
                {
                    heli.Input = apply(trim, d);
                    heli.Step(Dt);
                    if (t > 0.25) { sum += rate(heli.State.AngularVelocity); m++; }
                }
                return sum / m;
            }
            return (Run(delta), Run(-delta));
        }

        string? Check(string name, Func<Controls, double, Controls> apply,
                      Func<Vec3, double> rate, double expectSign, double delta = 0.25)
        {
            var (plus, minus) = Perturb(apply, rate, delta);
            double derivative = (plus - minus) * 0.5 * Deg;
            bool ok = Math.Sign(derivative) == Math.Sign(expectSign) && Math.Abs(derivative) > 1.5;
            Console.WriteLine($"  {name,-30} {derivative,8:F1} deg/s per {delta:F2} stick   {(ok ? "ok" : "WRONG")}");
            return ok ? null : $"{name}: {derivative:F1} deg/s, expected sign {expectSign:+0;-0}";
        }

        Console.WriteLine("  control derivatives (symmetric difference about the hover trim)");
        var errs = new List<string?>
        {
            Check("cyclic fore/aft -> pitch rate", (c, d) => { c.CyclicPitch += d; return c; }, w => w.Y, -1),
            Check("cyclic left/right -> roll rate", (c, d) => { c.CyclicRoll += d; return c; }, w => w.X, +1),
            Check("pedal -> yaw rate", (c, d) => { c.Pedal += d; return c; }, w => w.Z, +1, 0.30),
        };
        return errs.FirstOrDefault(e => e != null);
    }

    public static string? PedalAuthority()
    {
        var heli = MakeHeli();
        heli.PlaceInFlight(300);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 300, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        Fly(heli, ap, demand, 40);

        double trimPedal = heli.Input.Pedal;
        double trimCollective = heli.Input.Collective;
        Console.WriteLine($"  trimmed pedal in the hover {trimPedal:+0.000;-0.000} " +
                          $"(tail rotor pitch {heli.Tail.Update(Vec3.Zero, Vec3.Zero, heli.RotorOmega, 0, 1.225, 340, Dt).PitchUsed * Deg:F1} deg at zero command)");

        // Right pedal must yaw the nose right. Accumulate the per-step delta rather than
        // differencing the endpoints: three seconds of pedal takes the nose through more
        // than half a turn, and a single WrapPi of the total reads that back as a large
        // LEFT yaw. The bug is in the measurement, not the aircraft.
        double yawed = 0;
        double yawPrev = heli.State.Orientation.Yaw;
        for (double t = 0; t < 3; t += Dt)
        {
            heli.Input = new Controls { Collective = trimCollective, Pedal = trimPedal + 0.4, Throttle = 1.0 };
            heli.Step(Dt);
            double yawNow = heli.State.Orientation.Yaw;
            yawed += Airfoil.WrapPi(yawNow - yawPrev) * Deg;
            yawPrev = yawNow;
        }
        Console.WriteLine($"  right pedal for 3 s -> {yawed:F1} deg of yaw ({yawed / 3:F0} deg/s mean)");

        // With no pedal at all, main rotor torque must yaw the nose the other way.
        var heli2 = MakeHeli();
        heli2.PlaceInFlight(300);
        var ap2 = new Autopilot { CollectiveTrim = 0.55 };
        Fly(heli2, ap2, demand, 40);
        double trimColl2 = heli2.Input.Collective;
        double torqueYaw = 0;
        double yawPrev2 = heli2.State.Orientation.Yaw;
        for (double t = 0; t < 2.5; t += Dt)
        {
            heli2.Input = new Controls { Collective = trimColl2, Pedal = -1.0, Throttle = 1.0 };
            heli2.Step(Dt);
            double yawNow2 = heli2.State.Orientation.Yaw;
            torqueYaw += Airfoil.WrapPi(yawNow2 - yawPrev2) * Deg;
            yawPrev2 = yawNow2;
        }
        Console.WriteLine($"  full left pedal for 2.5 s -> {torqueYaw:F1} deg of yaw ({torqueYaw / 2.5:F0} deg/s mean)");

        if (yawed < 10) return $"right pedal produced only {yawed:F1} deg of right yaw";
        if (torqueYaw > -10) return $"left pedal produced {torqueYaw:F1} deg - the anti-torque sense is wrong";
        return null;
    }

    // ------------------------------------------------------- autorotation

    public static string? Autorotation()
    {
        var heli = MakeHeli();
        heli.PlaceInFlight(600, 30);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 600, ForwardSpeed = 30, LateralSpeed = 0, Heading = 0 };
        Fly(heli, ap, demand, 40);

        Console.WriteLine($"  cruise established: {heli.Telemetry.AirspeedTrue * Kt:F0} kt, Nr {heli.Telemetry.RotorRpmPercent:F0} %");
        heli.Engine.Fail();
        ap.RotorRpmLoop.Reset();
        Console.WriteLine("  *** engine failure ***");
        Console.WriteLine("    t s    Nr %   collective   ROD fpm   IAS kt   alt m   freewheel");

        double minNr = 999, settledNr = 0, settledRod = 0;
        for (double t = 0; t < 25; t += Dt)
        {
            // Pilot response: get the collective down inside a second, then fly the
            // rotor - hold Nr at 100 % with the lever and 60 kt with the cyclic.
            AutopilotDemand d2;
            if (t < 1.0)
                d2 = new AutopilotDemand { Collective = Math.Max(0.02, heli.Actual.Collective - Dt * 0.9),
                                           ForwardSpeed = 31, LateralSpeed = 0, Heading = 0 };
            else
                d2 = new AutopilotDemand { RotorRpmFraction = 1.0, ForwardSpeed = 31, LateralSpeed = 0, Heading = 0 };
            heli.Input = ap.Update(heli, d2, Dt);
            heli.Step(Dt);

            minNr = Math.Min(minNr, heli.Telemetry.RotorRpmPercent);
            if (t > 15) { settledNr = heli.Telemetry.RotorRpmPercent; settledRod = -heli.State.Velocity.Z; }

            if (Math.Abs(t % 2.5) < Dt)
                Console.WriteLine($"  {t,5:F1} {heli.Telemetry.RotorRpmPercent,7:F1} {heli.Input.Collective,11:F3} " +
                                  $"{-heli.State.Velocity.Z * Fpm,10:F0} {heli.Telemetry.AirspeedTrue * Kt,8:F0} " +
                                  $"{heli.State.Altitude,8:F0} {(heli.Telemetry.Autorotating ? "open" : "driving"),10}");
        }

        Console.WriteLine($"  lowest Nr {minNr:F1} %, settled {settledNr:F1} % at {-settledRod * Fpm:F0} fpm descent");

        if (minNr < 55) return $"rotor decayed to {minNr:F0} % - autorotation is not sustaining the rotor";
        if (settledNr < 70 || settledNr > 130) return $"autorotative Nr settled at {settledNr:F0} %, which is not survivable";
        if (settledRod > -3) return "the aircraft is not descending in autorotation, which means it is making lift from nothing";
        if (-settledRod * Fpm > 4000) return $"autorotative descent {-settledRod * Fpm:F0} fpm is far too fast";
        return null;
    }

    // ---------------------------------------------------- vortex ring state

    public static string? VortexRingState()
    {
        // The claim under test is not "the aircraft cannot recover" - it can, by falling
        // through into the windmill state, and that is real. The claims that matter are
        // that the rotor loses a large slice of its thrust, that the collective stops
        // being an effective lever while it lasts, that it costs real altitude, and that
        // flying forward is the escape.
        var heli = MakeHeli();
        heli.PlaceInFlight(400);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 400, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        Fly(heli, ap, demand, 40);
        double hoverCollective = heli.Actual.Collective;
        double hoverThrust = heli.Telemetry.Thrust;

        // Reference: how much extra thrust does +0.06 collective buy in a stable hover?
        double hoverGain;
        {
            var h2 = MakeHeli();
            h2.PlaceInFlight(400);
            var a2 = new Autopilot { CollectiveTrim = 0.55 };
            Fly(h2, a2, demand, 40);
            double before = h2.Telemetry.Thrust;
            for (double t = 0; t < 1.5; t += Dt)
            {
                var c = a2.Update(h2, new AutopilotDemand { PitchAttitude = 0, RollAttitude = 0, Heading = 0 }, Dt);
                c.Collective = hoverCollective + 0.06;
                h2.Input = c; h2.Step(Dt);
            }
            hoverGain = h2.Telemetry.Thrust - before;
        }

        Console.WriteLine($"  hover: {hoverThrust:F0} N at collective {hoverCollective:F3}; " +
                          $"+0.06 lever buys {hoverGain:F0} N");
        Console.WriteLine();
        Console.WriteLine("  entering a vertical descent, then doing the wrong thing about it");
        Console.WriteLine("    t s   ROD fpm   VRS   collective   thrust N   alt m");

        double maxSeverity = 0, minThrustInVrs = double.MaxValue, startAlt = heli.State.Altitude;
        double lowestAlt = startAlt, thrustBeforePull = 0, thrustAfterPull = 0;
        double rodAtPull = 0, worstRodAfterPull = double.MaxValue, vrsPersistedUntil = 6.0;
        for (double t = 0; t < 26; t += Dt)
        {
            var c = ap.Update(heli, new AutopilotDemand { PitchAttitude = 0, RollAttitude = 0, Heading = 0 }, Dt);
            c.Collective = t < 6 ? hoverCollective - 0.10 : hoverCollective + 0.06;
            heli.Input = c;
            heli.Step(Dt);

            maxSeverity = Math.Max(maxSeverity, heli.Telemetry.VrsSeverity);
            if (heli.Telemetry.VrsSeverity > 0.5) minThrustInVrs = Math.Min(minThrustInVrs, heli.Telemetry.Thrust);
            lowestAlt = Math.Min(lowestAlt, heli.State.Altitude);
            if (Math.Abs(t - 5.9) < Dt) { thrustBeforePull = heli.Telemetry.Thrust; rodAtPull = -heli.State.Velocity.Z; }
            if (t > 6.0 && t < 12.0) worstRodAfterPull = Math.Min(worstRodAfterPull, -heli.State.Velocity.Z);
            if (t > 6.0 && heli.Telemetry.VrsSeverity > 0.3) vrsPersistedUntil = t;
            if (Math.Abs(t - 7.4) < Dt) thrustAfterPull = heli.Telemetry.Thrust;

            if (Math.Abs(t % 3.0) < Dt)
                Console.WriteLine($"  {t,5:F1} {-heli.State.Velocity.Z * Fpm,9:F0} {heli.Telemetry.VrsSeverity,6:F2} " +
                                  $"{heli.Actual.Collective,12:F3} {heli.Telemetry.Thrust,10:F0} {heli.State.Altitude,8:F0}");
        }

        double deficit = 1.0 - minThrustInVrs / hoverThrust;
        double vrsGain = thrustAfterPull - thrustBeforePull;
        double altitudeLost = startAlt - lowestAlt;
        Console.WriteLine();
        Console.WriteLine($"  peak severity        {maxSeverity:F2}");
        Console.WriteLine($"  thrust deficit       {deficit * 100:F0} % below hover thrust at the worst of it");
        Console.WriteLine($"  descent at the pull  {-rodAtPull * Fpm:F0} fpm, worsening to " +
                          $"{-worstRodAfterPull * Fpm:F0} fpm AFTER the collective came back up");
        Console.WriteLine($"  (reference: the same lever movement buys {hoverGain:F0} N in a stable hover)");
        Console.WriteLine($"  ring persisted       {vrsPersistedUntil - 6.0:F1} s after the collective came back up");
        Console.WriteLine($"  altitude lost        {altitudeLost:F0} m before recovery");

        // The escape: identical entry, but fly forward instead of pulling.
        var heli2 = MakeHeli();
        heli2.PlaceInFlight(400);
        var ap2 = new Autopilot { CollectiveTrim = 0.55 };
        Fly(heli2, ap2, demand, 40);
        double hoverColl2 = heli2.Actual.Collective;
        double startAlt2 = heli2.State.Altitude, lowest2 = startAlt2;
        for (double t = 0; t < 20; t += Dt)
        {
            var d2 = t < 6
                ? new AutopilotDemand { PitchAttitude = 0, RollAttitude = 0, Heading = 0 }
                : new AutopilotDemand { PitchAttitude = -12 * Math.PI / 180, RollAttitude = 0, Heading = 0 };
            var c = ap2.Update(heli2, d2, Dt);
            c.Collective = t < 6 ? hoverColl2 - 0.10 : hoverColl2;
            heli2.Input = c;
            heli2.Step(Dt);
            lowest2 = Math.Min(lowest2, heli2.State.Altitude);
        }
        Console.WriteLine($"  flying forward out:  {heli2.Telemetry.AirspeedTrue * Kt:F0} kt, " +
                          $"{-heli2.State.Velocity.Z * Fpm:F0} fpm, VRS {heli2.Telemetry.VrsSeverity:F2}, " +
                          $"{startAlt2 - lowest2:F0} m lost");

        if (maxSeverity < 0.5) return $"vortex ring state never developed (peak severity {maxSeverity:F2})";
        if (deficit < 0.12) return $"only {deficit * 100:F0} % thrust lost in VRS - not enough to trap anybody";
        // The signature of the trap: raising the collective back to hover power does not
        // arrest the descent, it deepens it. A pilot who reacts to sink by pulling up
        // digs the hole faster, which is precisely why the recovery is counter-intuitive.
        // The signature of the trap: restoring hover power does not promptly fix it. The
        // pilot pulls, and for several seconds nothing good happens.
        if (vrsPersistedUntil - 6.0 < 1.5)
            return $"the ring cleared {vrsPersistedUntil - 6.0:F1} s after the pull - too forgiving to be a trap";
        if (altitudeLost < 40) return $"only {altitudeLost:F0} m lost - VRS is not costing the player anything";
        if (heli2.Telemetry.VrsSeverity > 0.2) return "flying forward did not clear the vortex ring state";
        return null;
    }

    // --------------------------------------------------------- hover ceiling

    public static string? ServiceCeiling()
    {
        Console.WriteLine("   alt m   density alt m   collective   power kW   available kW   Nr %");
        double ceiling = 0;
        for (double alt = 0; alt <= 6000; alt += 750)
        {
            var heli = MakeHeli();
            heli.PlaceInFlight(alt);
            var ap = new Autopilot { CollectiveTrim = 0.6 };
            var demand = new AutopilotDemand { Altitude = alt, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
            Fly(heli, ap, demand, 40);
            var t = heli.Telemetry;
            bool holding = Math.Abs(heli.State.Altitude - alt) < 8 && heli.Input.Collective < 0.985;
            if (holding) ceiling = alt;
            Console.WriteLine($"  {alt,6:F0} {t.DensityAltitude,15:F0} {heli.Input.Collective,12:F3} " +
                              $"{t.PowerRequired / 1000,10:F0} {t.PowerAvailable / 1000,14:F0} {t.RotorRpmPercent,6:F1}" +
                              (holding ? "" : "   <-- cannot hold"));
        }
        Console.WriteLine($"  hover ceiling OGE approximately {ceiling:F0} m");
        if (ceiling < 1000) return $"hover ceiling {ceiling:F0} m is too low to be a useful aircraft";
        if (ceiling >= 6000) return "the aircraft hovers at 6000 m, so power is not falling off with altitude";
        return null;
    }

    // ------------------------------------------------------------ performance

    public static string? Performance()
    {
        var heli = MakeHeli();
        heli.PlaceInFlight(300, 30);
        var ap = new Autopilot();
        var demand = new AutopilotDemand { Altitude = 300, ForwardSpeed = 30, LateralSpeed = 0, Heading = 0 };
        Fly(heli, ap, demand, 10);

        const int steps = 60000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            heli.Input = ap.Update(heli, demand, Dt);
            heli.Step(Dt);
        }
        sw.Stop();

        double usPerStep = sw.Elapsed.TotalMilliseconds * 1000.0 / steps;
        double realtimeFactor = steps * Dt / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  {steps} steps in {sw.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  {usPerStep:F1} us per physics step at {1 / Dt:F0} Hz");
        Console.WriteLine($"  {realtimeFactor:F0}x realtime for one aircraft");
        Console.WriteLine($"  budget at 240 Hz: {usPerStep * 240 / 1000:F2} ms of every second, " +
                          $"{usPerStep * 240 / 10000:F2} % of one core");

        if (usPerStep > 60) return $"{usPerStep:F0} us per step is too expensive to run at 240 Hz in a game";
        return null;
    }
}
