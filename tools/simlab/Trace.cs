using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class Trace
{
    const double Deg = 180.0 / Math.PI;

    public static string? HoverTrace()
    {
        var env = new FlatEnvironment();
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = 500 };
        heli.InvalidateMass();
        heli.PlaceInFlight(200);
        heli.UseInternalGroundModel = false;
        var ap = new Autopilot { CollectiveTrim = 0.42 };
        var demand = new AutopilotDemand { Altitude = 200, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        double dt = 1.0 / 240.0;

        Console.WriteLine("    t     alt   roll  pitch    yaw   p     q     r    coll  cyc_x cyc_y  ped" +
                          "   thrust  a0deg  a1deg  b1deg   Mx     My     Mz");
        for (double t = 0; t < 12; t += dt)
        {
            heli.Input = ap.Update(heli, demand, dt);
            heli.Step(dt);
            var m = heli.LastMomentBody;
            if (Math.Abs(t % 0.5) < dt)
            {
                var st = heli.State; var tel = heli.Telemetry;
                Console.WriteLine($"  {t,4:F1} {st.Altitude,7:F1} {st.Orientation.Roll * Deg,6:F1} " +
                    $"{st.Orientation.Pitch * Deg,6:F1} {st.Orientation.Yaw * Deg,6:F1} " +
                    $"{st.AngularVelocity.X * Deg,5:F0} {st.AngularVelocity.Y * Deg,5:F0} {st.AngularVelocity.Z * Deg,5:F0} " +
                    $"{heli.Input.Collective,6:F2} {heli.Input.CyclicPitch,6:F2} {heli.Input.CyclicRoll,6:F2} {heli.Input.Pedal,5:F2} " +
                    $"{tel.Thrust,8:F0} {tel.Coning * Deg,6:F2} {tel.FlapBack * Deg,6:F2} {tel.FlapSide * Deg,6:F2} " +
                    $"{m.X,7:F0} {m.Y,6:F0} {m.Z,6:F0}");
            }
        }
        return null;
    }

    /// <summary>Open loop: fixed controls, no autopilot, no ground. What does it do on its own?</summary>
    public static string? FreeTrace()
    {
        var env = new FlatEnvironment();
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = 500 };
        heli.InvalidateMass();
        // From TRIM. Starting this trace wings-level with the stick centred meant it was
        // never showing the bare airframe dynamics at all - it was showing the response to
        // a large step input of uncancelled tail rotor thrust, which is a different and
        // much more alarming picture.
        heli.PlaceInFlightTrimmed(200);
        heli.UseInternalGroundModel = false;
        double dt = 1.0 / 240.0;

        Console.WriteLine("  fixed controls at trim, no feedback - the bare airframe dynamics");
        Console.WriteLine("    t     alt   roll  pitch    yaw   p     q     r   thrust  a0deg  a1deg  b1deg   Mx     My     Mz   Fy");
        for (double t = 0; t < 8; t += dt)
        {
            heli.Step(dt);
            var m = heli.LastMomentBody;
            var f = heli.LastForceBody;
            if (Math.Abs(t % 0.25) < dt)
            {
                var st = heli.State; var tel = heli.Telemetry;
                Console.WriteLine($"  {t,4:F2} {st.Altitude,7:F1} {st.Orientation.Roll * Deg,6:F1} " +
                    $"{st.Orientation.Pitch * Deg,6:F1} {st.Orientation.Yaw * Deg,6:F1} " +
                    $"{st.AngularVelocity.X * Deg,5:F0} {st.AngularVelocity.Y * Deg,5:F0} {st.AngularVelocity.Z * Deg,5:F0} " +
                    $"{tel.Thrust,8:F0} {tel.Coning * Deg,6:F2} {tel.FlapBack * Deg,6:F2} {tel.FlapSide * Deg,6:F2} " +
                    $"{m.X,7:F0} {m.Y,6:F0} {m.Z,6:F0} {f.Y,7:F0}");
            }
        }
        return null;
    }
}
