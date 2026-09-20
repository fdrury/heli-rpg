using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Does the man on the mast actually reach the player?
///
/// <para>The announcer is covered hard on the sim side - eleven simlab checks on what he
/// says, when he says it and what he is allowed to know. <b>None of that touches this
/// half.</b> The wiring is where the system can fail silently: a transmitter that binds to
/// nowhere, a station that gets a stream URL bolted to it after all, a signal that never
/// reaches the readable threshold from anywhere a player would fly, a deed feed that arrives
/// empty. Every one of those leaves a green simlab and a dead radio.</para>
///
/// <para>So this runs him for a simulated day from a fixed point in his own coverage, with a
/// deed ledger seeded the way a day's flying would seed one, and prints what came out.</para>
/// </summary>
public static class DjReport
{
    /// <summary>Half a second. Captions run to seconds, so a finer step buys nothing and
    /// this loop runs a simulated eighteen hours.</summary>
    private const double Dt = 0.5;

    public static void Run()
    {
        GD.Print("=== the Upland Service ================================================");

        UplandService.Forget();
        Site? mast = UplandService.Transmitter;

        if (mast is null)
        {
            GD.Print("  NO TRANSMITTER. Every rule in RadioDj.SiteRules came up empty:");
            foreach (DjSiteRule r in RadioDj.SiteRules) GD.Print($"      {r.Text}");
            GD.Print("  PROBLEM: the station does not exist in this world. 676 authored lines");
            GD.Print("  and nowhere to broadcast them from.");
            return;
        }

        float ground = WorldHeight.RawAt(mast.Position.X, mast.Position.Y);
        GD.Print($"  {RadioDj.HostName}, {RadioDj.StationName}");
        GD.Print($"  mast: #{mast.Id} \"{mast.Name}\" ({mast.Kind} in {mast.Region}), " +
                 $"ground {ground:F0} m, mast {RadioDj.MastHeightM:F0} m, " +
                 $"rule {UplandService.RuleUsed} of {RadioDj.SiteRules.Length}");
        if (UplandService.RuleUsed > 0)
            GD.Print($"  NOTE: degraded to \"{RadioDj.SiteRules[UplandService.RuleUsed].Text}\" - " +
                     $"the world had no {RadioDj.SiteRules[0].Text}.");

        if (UplandService.Station is not RadioStation station)
        {
            GD.Print("  PROBLEM: a transmitter with no station built from it.");
            return;
        }
        if (station.Url.Length > 0)
        {
            GD.Print($"  PROBLEM: the Upland Service has a stream URL ({station.Url}). It is a man");
            GD.Print("  with a microphone; a URL here puts somebody else's music over him.");
            return;
        }

        // --- coverage --------------------------------------------------------
        // The one thing a caption cannot show: whether a player flying normally is ever
        // inside his range at all.
        GD.Print("");
        GD.Print("  reception, at 150 m AGL:");
        var samples = new (string Label, float X, float Y)[]
        {
            ("at the mast", mast.Position.X, mast.Position.Y),
            ("5 km out", mast.Position.X + 5000, mast.Position.Y),
            ("15 km out", mast.Position.X + 15000, mast.Position.Y),
            ("30 km out", mast.Position.X + 30000, mast.Position.Y),
        };
        foreach ((string label, float x, float y) in samples)
        {
            double d = new Vector2(x, y).DistanceTo(mast.Position);
            double q = Radio.Quality(d, 150, station, 0, 0);
            GD.Print($"    {label,-14} {d / 1000.0,6:F1} km   quality {q:F2}" +
                     (q >= DjBroadcast.ReadableQuality ? "   readable" : "   too weak"));
        }

        // --- a day of him ----------------------------------------------------
        var progress = Progress.NewGame();
        progress.Clock = 172 * 86400 + 5 * 3600;     // sign-on, in summer (D-039)

        // A day's flying, as the deed hooks would have recorded it. Witness counts differ
        // on purpose: the whole point of D-074 is that they decide what gets talked about.
        progress.RecordDeed(DjDeedKind.Delivered, mast.Name, 40, witnesses: 30);
        progress.RecordDeed(DjDeedKind.Buzzed, mast.Name, 0, witnesses: 9);
        progress.RecordDeed(DjDeedKind.Salvaged, mast.Name, 6, witnesses: 0);   // nobody saw
        progress.Clock += 3 * 3600;                   // let word travel

        var dj = new DjBroadcast(mast.Id);
        var heat = new List<DjRegionHeat> { new(WorldMap.Regions[0].Name, 0.35) };
        var said = new List<(double Hour, DjTopic Topic, string Text)>();
        string? last = null;

        double start = progress.Clock;
        while (progress.Clock - start < 18 * 3600)
        {
            progress.Clock += Dt;
            Weather.Conditions wx = SceneMood.Weather.At(progress.Clock);
            var world = new DjWorld(progress.Clock, wx.Sky, wx.WindSpeed, wx.Gust,
                                    wx.Visibility, wx.CloudBase, wx.IsaDeviation,
                                    wx.Precipitation, wx.StormIntensity,
                                    heat, false, null, null, progress.Deeds);

            dj.Update(Dt, world, 1.0);
            if (dj.Caption is string c && c != last)
            {
                said.Add(((progress.Clock / 3600.0) % 24.0, dj.CaptionTopic ?? DjTopic.Filler, c));
                last = c;
            }
            else if (dj.Caption is null) last = null;
        }

        GD.Print("");
        GD.Print($"  eighteen hours on the air: {dj.Breaks} breaks, {said.Count} segments");

        var byTopic = said.GroupBy(x => x.Topic).OrderByDescending(g => g.Count()).ToList();
        GD.Print($"  {byTopic.Count} distinct topics; most-used: " +
                 string.Join(", ", byTopic.Take(5).Select(g => $"{g.Key} x{g.Count()}")));

        GD.Print("");
        GD.Print("  a sample of what a pilot would actually have heard:");
        foreach ((double hour, DjTopic topic, string text) in said.Take(6))
            GD.Print($"    {(int)hour:D2}:{(int)((hour % 1) * 60):D2}  [{topic}] {text}");

        // --- the checks -------------------------------------------------------
        var problems = new List<string>();

        if (said.Count == 0)
            problems.Add("he said nothing at all in eighteen hours of perfect signal");

        int deeds = said.Count(x => x.Topic == DjTopic.Deed);
        GD.Print("");
        GD.Print($"  deed lines: {deeds}");
        foreach ((_, DjTopic topic, string text) in said.Where(x => x.Topic == DjTopic.Deed))
            GD.Print($"      {text}");
        if (deeds == 0)
            problems.Add("the deed feed reached him and he never used it - D-074 is wired to nothing");

        // The unwitnessed salvage run must never be mentioned. This is the gate that makes
        // flying the quiet way round mean something, and it is invisible from the sim side
        // because the sim side never sees this ledger.
        if (said.Any(x => x.Text.Contains("wreck", StringComparison.OrdinalIgnoreCase) ||
                          x.Text.Contains("Stripping", StringComparison.Ordinal)))
            problems.Add("he talked about the salvage run nobody watched");

        double spoken = said.Sum(x => RadioDj.ReadSeconds(x.Text));
        GD.Print($"  {spoken / 60.0:F0} minutes of speech in 18 hours " +
                 $"({spoken / (18 * 3600) * 100:F1}% of the airtime)");
        if (spoken > 18 * 3600 * 0.35)
            problems.Add($"he is talking {spoken / (18 * 3600) * 100:F0}% of the time - " +
                         "this is a music station with an announcer, not a phone-in");

        GD.Print("");
        if (problems.Count == 0)
        {
            GD.Print("  PROBLEMS: none. He has a hill, a signal, an audience and something to");
            GD.Print("  talk about, and he is not talking about the thing nobody saw.");
        }
        else
        {
            foreach (string p in problems) GD.Print($"  !! {p}");
            GD.Print($"  PROBLEMS: {problems.Count}");
        }
        GD.Print("=======================================================================");
    }
}
