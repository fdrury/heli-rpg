using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class LandingTests
{
    const double Deg = 180.0 / Math.PI;

    public static string? Geometry()
    {
        var af = Airframe.Workhorse();
        var heli = new Helicopter(af) { Fuel = 500 };
        heli.InvalidateMass();

        double critical = Landing.CriticalRollAngle(af, heli.CentreOfGravity) * Deg;
        Console.WriteLine($"  centre of gravity      {heli.CentreOfGravity}");
        Console.WriteLine($"  critical roll angle    {critical,6:F1} deg   (real utility helicopters: 25-35)");

        // Raising the centre of gravity must make it roll over sooner. This is the single
        // most important consequence of the refit system: put mass on the roof and the
        // aircraft becomes harder to land, without anybody writing a rule that says so.
        af.Mass.Add("roof_cargo", new Vec3(0, 0, -2.0), 400);
        heli.InvalidateMass();
        double raised = Landing.CriticalRollAngle(af, heli.CentreOfGravity) * Deg;
        Console.WriteLine($"  with 400 kg on the roof {raised,5:F1} deg   ({raised - critical:+0.0;-0.0} deg)");
        af.Mass.Remove("roof_cargo");
        heli.InvalidateMass();

        if (critical < 20 || critical > 45) return $"critical roll angle {critical:F0} deg is not plausible";
        if (raised >= critical) return "raising the centre of gravity did not reduce the rollover angle";
        return null;
    }

    public static string? Touchdowns()
    {
        var af = Airframe.Workhorse();
        var heli = new Helicopter(af) { Fuel = 500 };
        heli.InvalidateMass();
        Vec3 cg = heli.CentreOfGravity;

        Console.WriteLine("   descent m/s  ground m/s  roll  slope   damage   verdict");
        var cases = new (double vs, double gs, double roll, double slope)[]
        {
            (0.4, 0.2, 1, 0),
            (1.5, 0.5, 2, 2),
            (2.5, 0.8, 3, 3),
            (4.0, 1.0, 4, 4),
            (7.5, 1.5, 5, 5),
            (1.0, 4.0, 9, 2),
            (0.8, 0.3, 26, 8),
            (0.8, 0.3, 34, 8),
        };

        double prev = -1;
        foreach (var (vs, gs, roll, slope) in cases)
        {
            var r = Landing.Evaluate(af, cg, vs, gs, roll / Deg, 0, slope / Deg, false);
            Console.WriteLine($"  {vs,11:F1} {gs,11:F1} {roll,5:F0} {slope,6:F0} {r.StructuralDamage,8:F2}   {r.Summary}");
            if (vs <= 7.5 && gs <= 1.5 && roll <= 5 && r.StructuralDamage < prev)
                return "a heavier landing did less damage than a lighter one";
            if (vs <= 7.5 && gs <= 1.5 && roll <= 5) prev = r.StructuralDamage;
        }

        var gentle = Landing.Evaluate(af, cg, 0.4, 0.2, 0.02, 0, 0, false);
        var heavy = Landing.Evaluate(af, cg, 7.5, 1.5, 0.08, 0, 0.08, false);
        var over = Landing.Evaluate(af, cg, 0.8, 0.3, 34 / Deg, 0, 8 / Deg, false);
        var marginal = Landing.Evaluate(af, cg, 0.8, 0.3, 26 / Deg, 0, 8 / Deg, false);

        if (gentle.StructuralDamage > 0.01) return "a gentle landing caused damage";
        if (heavy.StructuralDamage < 0.5) return "a 7.5 m/s arrival was not treated as a crash";
        if (!over.Rollover) return "34 degrees of bank on an 8 degree slope did not roll it over";
        if (marginal.Rollover) return "26 degrees of bank rolled it over, inside the critical angle";
        return null;
    }

    public static string? Brownout()
    {
        var af = Airframe.Workhorse();
        double r = af.MainRotor.Radius;
        Console.WriteLine("   surface     30 m   15 m    8 m    4 m    1 m    (hovering, 14 m/s downwash)");

        foreach (SurfaceKind s in new[] { SurfaceKind.Sand, SurfaceKind.Loose, SurfaceKind.Grass, SurfaceKind.Concrete })
        {
            Console.Write($"  {s,-10}");
            foreach (double h in new[] { 30.0, 15.0, 8.0, 4.0, 1.0 })
                Console.Write($" {Landing.Brownout(s, h, r, 14.0, 0.0),6:F2}");
            Console.WriteLine();
        }

        double hoverDust = Landing.Brownout(SurfaceKind.Sand, 3.0, r, 14.0, 0.0);
        double movingDust = Landing.Brownout(SurfaceKind.Sand, 3.0, r, 14.0, 8.0);
        Console.WriteLine($"  hovering over sand at 3 m: {hoverDust:F2}");
        Console.WriteLine($"  same height doing 16 kt:   {movingDust:F2}  (fly out of your own dust)");

        if (hoverDust < 0.5) return $"hovering over sand produced only {hoverDust:F2} brownout";
        if (movingDust > hoverDust * 0.5) return "moving forward did not clear the dust";
        if (Landing.Brownout(SurfaceKind.Concrete, 3.0, r, 14.0, 0) > 0.25)
            return "concrete produced a dust cloud";
        return null;
    }

    public static string? DamageEffects()
    {
        var af = Airframe.Workhorse();
        var heli = new Helicopter(af, new FlatEnvironment()) { Fuel = 500 };
        heli.InvalidateMass();

        // Baseline hover.
        heli.PlaceInFlight(200);
        var ap = new Autopilot { CollectiveTrim = 0.55 };
        var demand = new AutopilotDemand { Altitude = 200, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
        for (int i = 0; i < (int)(40 / Scenarios.Dt); i++)
        {
            heli.Input = ap.Update(heli, demand, Scenarios.Dt);
            heli.Step(Scenarios.Dt);
        }
        double healthyCollective = heli.Actual.Collective;
        double healthyPower = heli.Telemetry.PowerRequired;
        Console.WriteLine($"  serviceable:  collective {healthyCollective:F3}, power {healthyPower / 1000:F0} kW, " +
                          $"ceiling margin {(heli.Telemetry.PowerAvailable - healthyPower) / 1000:F0} kW");

        // Same aircraft with a tired engine and a damaged gearbox.
        var hurt = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        hurt.Damage.Apply(Component.Engine, 0.45, DamageCause.Wear, "hours");
        hurt.Damage.Apply(Component.Transmission, 0.30, DamageCause.Overtorque, "abused");
        hurt.InvalidateMass();
        hurt.PlaceInFlight(200);
        var ap2 = new Autopilot { CollectiveTrim = 0.55 };
        for (int i = 0; i < (int)(40 / Scenarios.Dt); i++)
        {
            hurt.Input = ap2.Update(hurt, demand, Scenarios.Dt);
            hurt.Step(Scenarios.Dt);
        }
        Console.WriteLine($"  damaged:      collective {hurt.Actual.Collective:F3}, power {hurt.Telemetry.PowerRequired / 1000:F0} kW, " +
                          $"available {hurt.Telemetry.PowerAvailable / 1000:F0} kW");
        Console.WriteLine($"                {hurt.Damage}");
        Console.WriteLine($"                altitude held: {hurt.State.Altitude:F1} m of 200");

        // The interesting consequence is not the number, it is that a tired engine cannot
        // hold an out-of-ground-effect hover any more. The pilot finds out about worn
        // components by discovering the aircraft will no longer do something it used to.
        bool couldNotHold = hurt.State.Altitude < 150;
        Console.WriteLine($"                verdict: {(couldNotHold ? "CANNOT hold an OGE hover" : "still holds the hover")}");

        if (hurt.Telemetry.PowerAvailable >= heli.Telemetry.PowerAvailable * 0.95)
            return "engine damage did not reduce available power";
        if (!couldNotHold)
            return "a 45% engine and a 30% gearbox still held a 200 m hover";

        // Losing the tail rotor entirely must cost yaw control.
        var noTail = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        noTail.InvalidateMass();
        noTail.PlaceInFlight(300);
        var ap3 = new Autopilot { CollectiveTrim = 0.55 };
        for (int i = 0; i < (int)(30 / Scenarios.Dt); i++)
        {
            noTail.Input = ap3.Update(noTail, demand, Scenarios.Dt);
            noTail.Step(Scenarios.Dt);
        }
        noTail.Damage.Apply(Component.TailRotor, 1.0, DamageCause.Impact, "tail rotor strike");

        // Accumulate the turn rather than reading the heading: the aircraft goes round
        // several times, and a wrapped Euler angle would report almost nothing.
        double turned = 0, peakRate = 0;
        for (int i = 0; i < (int)(6 / Scenarios.Dt); i++)
        {
            noTail.Input = ap3.Update(noTail, demand, Scenarios.Dt);
            noTail.Step(Scenarios.Dt);
            turned += Math.Abs(noTail.State.AngularVelocity.Z) * Scenarios.Dt;
            peakRate = Math.Max(peakRate, Math.Abs(noTail.State.AngularVelocity.Z));
        }
        Console.WriteLine($"  tail rotor loss: {turned * Deg:F0} deg of yaw in 6 s " +
                          $"({turned * Deg / 360:F1} turns), peak {peakRate * Deg:F0} deg/s, " +
                          $"with the autopilot fighting it and full pedal available");

        if (turned * Deg < 90) return $"losing the tail rotor only produced {turned * Deg:F0} deg of yaw in six seconds";
        return null;
    }
}
