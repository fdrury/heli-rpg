using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The sun has to be in the right part of the sky and the weather has to stay inside the
/// envelope a helicopter can survive, for every minute of several days.
///
/// Both of these are the kind of thing that looks obviously fine in the source and is
/// obviously wrong the first time you watch a sunset happen in the north.
/// </summary>
public static class WeatherTests
{
    public static string? Solar()
    {
        var w = new Weather();
        Console.WriteLine($"  solar geometry at {w.LatitudeDeg:F0} deg north, day 172 (midsummer)");
        Console.WriteLine("    hour   elev   azimuth   state");

        const double day = 172 * 86400.0;
        string? failure = null;

        for (int hour = 0; hour < 24; hour += 3)
        {
            SunPosition s = w.Sun(day + hour * 3600.0);
            string state = s.IsDay ? "day" : s.IsTwilight ? "twilight" : "night";
            Console.WriteLine($"    {hour,4}  {s.ElevationDeg,6:F1}  {s.AzimuthDeg,7:F0}   " +
                              $"{state}  (daylight {s.DaylightFraction:F2})");
        }

        // Noon: the sun must be high and very nearly due south. A sign slip anywhere in
        // the spherical trig puts it in the north, which looks subtly wrong in every
        // single frame and is almost impossible to spot by reading the formula.
        SunPosition noon = w.Sun(day + 12 * 3600.0);
        if (noon.ElevationDeg < 50)
            failure ??= $"midsummer noon sun only {noon.ElevationDeg:F0} deg up";
        if (Math.Abs(noon.AzimuthDeg - 180) > 8)
            failure ??= $"noon sun bears {noon.AzimuthDeg:F0}, should be near due south";

        // Morning in the east, evening in the west.
        SunPosition morning = w.Sun(day + 7 * 3600.0);
        SunPosition evening = w.Sun(day + 18 * 3600.0);
        if (morning.AzimuthDeg is < 45 or > 135)
            failure ??= $"07:00 sun bears {morning.AzimuthDeg:F0}, should be easterly";
        if (evening.AzimuthDeg is < 225 or > 315)
            failure ??= $"18:00 sun bears {evening.AzimuthDeg:F0}, should be westerly";

        // Midnight is night, and midsummer days are longer than midwinter ones.
        if (!w.Sun(day + 0.5 * 3600.0).IsNight)
            failure ??= "00:30 is not night";

        double summer = DaylightHours(w, 172);
        double winter = DaylightHours(w, 355);
        Console.WriteLine($"  daylight: {summer:F1} h at midsummer, {winter:F1} h at midwinter");
        if (summer <= winter + 3)
            failure ??= $"seasons barely differ: {summer:F1} h vs {winter:F1} h";

        return failure;
    }

    private static double DaylightHours(Weather w, int dayOfYear)
    {
        int lit = 0;
        for (int m = 0; m < 24 * 60; m++)
            if (w.Sun(dayOfYear * 86400.0 + m * 60.0).IsDay) lit++;
        return lit / 60.0;
    }

    public static string? Conditions()
    {
        var w = new Weather();
        Console.WriteLine("  weather over four days, sampled every six hours");
        Console.WriteLine("     t        sky        wind          vis     base    ISA");

        string? failure = null;
        var seen = new System.Collections.Generic.HashSet<SkyCondition>();

        for (double h = 0; h < 96; h += 6)
        {
            Weather.Conditions c = w.At(h * 3600.0);
            seen.Add(c.Sky);
            Console.WriteLine($"  {h,4:F0}h  {c.Sky,-9}  {c.WindSpeed * 1.94384,3:F0} kt/" +
                              $"{c.Gust * 1.94384,2:F0} from {c.WindFromRad * 180 / Math.PI,3:F0}  " +
                              $"{c.Visibility / 1000,5:F1} km  {c.CloudBase,5:F0} m  " +
                              $"{c.IsaDeviation,5:F1}");
        }

        // Nothing may leave the envelope at any point across a fortnight. Sampling every
        // ten minutes rather than every six hours, because the interesting failures are
        // exactly the ones that hide between coarse samples.
        double worstWind = 0, worstGust = 0, lowestBase = double.MaxValue, worstVis = double.MaxValue;
        for (double t = 0; t < 14 * 86400.0; t += 600)
        {
            Weather.Conditions c = w.At(t);
            worstWind = Math.Max(worstWind, c.WindSpeed);
            worstGust = Math.Max(worstGust, c.Gust);
            lowestBase = Math.Min(lowestBase, c.CloudBase);
            worstVis = Math.Min(worstVis, c.Visibility);

            if (c.WindSpeed < 0 || c.Visibility <= 0 || c.CloudBase <= 0)
                failure ??= $"nonsense conditions at {t / 3600:F1} h: {c.Describe()}";
            if (double.IsNaN(c.WindSpeed) || double.IsNaN(c.IsaDeviation))
                failure ??= $"NaN in conditions at {t / 3600:F1} h";
        }

        Console.WriteLine();
        Console.WriteLine($"  over a fortnight: wind peaks {worstWind * 1.94384:F0} kt, " +
                          $"gusts {worstGust * 1.94384:F0} kt, base down to {lowestBase:F0} m, " +
                          $"vis down to {worstVis / 1000:F1} km");

        // A Huey is out of its depth somewhere around 50 kt of wind. If the model can
        // produce more than that, the game has weather nobody can fly in.
        if (worstGust * 1.94384 > 50)
            failure ??= $"gusts reach {worstGust * 1.94384:F0} kt, which is unflyable";
        if (worstWind < 4)
            failure ??= "the wind never gets up at all";
        if (seen.Count < 3)
            failure ??= $"only {seen.Count} kind(s) of sky in four days";

        // When does it next rain in daylight? Anything that wants to LOOK at weather needs
        // a time to look at, and scanning for it beats guessing and re-rendering.
        for (double t = 0; t < 10 * 86400.0; t += 900)
        {
            Weather.Conditions wc = w.At(t);
            if (wc.Precipitation > 0.25 && w.Sun(t).ElevationDeg > 12)
            {
                Console.WriteLine($"  first good daylight rain at {t / 3600:F2} h " +
                                  $"(sun {w.Sun(t).ElevationDeg:F0} deg): {wc.Describe()}");
                break;
            }
        }

        // Wind must be continuous. A jump of several knots between consecutive frames is
        // a step input to the rotor and would feel like hitting something.
        double biggestJump = 0;
        for (double t = 0; t < 48 * 3600.0; t += 1.0)
        {
            Vec3 a = w.At(t).WindNed, b = w.At(t + 1.0).WindNed;
            biggestJump = Math.Max(biggestJump, (b - a).Length);
        }
        Console.WriteLine($"  largest wind change in any one second: {biggestJump:F4} m/s");
        if (biggestJump > 0.25)
            failure ??= $"wind steps by {biggestJump:F2} m/s in a second";

        return failure;
    }

    /// <summary>The wind has to actually reach the aircraft and change what it does.</summary>
    public static string? WindEffect()
    {
        double Hover(double windSpeed)
        {
            var env = new FlatEnvironment { SteadyWind = new Vec3(-windSpeed, 0, 0) };
            var h = new Helicopter(Airframe.Workhorse(), env) { Fuel = 500 };
            h.InvalidateMass();
            h.PlaceInFlightTrimmed(200);
            h.UseInternalGroundModel = false;
            TrimResult t = Trim.Solve(h, 200, 0);
            return t.Controls.Collective;
        }

        double calm = Hover(0);
        double breezy = Hover(12.0);       // ~23 kt on the nose

        Console.WriteLine($"  collective to hold a hover:  calm {calm:F3}, " +
                          $"23 kt headwind {breezy:F3}");

        // Translational lift is free performance: a hover held into a decent wind needs
        // measurably less power than the same hover in still air. If the wind is not
        // reaching the rotor these two are identical.
        if (Math.Abs(breezy - calm) < 0.004)
            return "wind makes no difference to hover power - it is not reaching the rotor";
        if (breezy >= calm)
            return $"a headwind made the hover need MORE collective ({breezy:F3} vs {calm:F3})";

        Console.WriteLine($"  translational lift saves {(calm - breezy) / calm:P1} of the lever");
        return null;
    }
}
