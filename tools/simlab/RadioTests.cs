using System;
using System.Collections.Generic;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The cockpit radio, measured.
///
/// Four things here are claims with numbers behind them rather than opinions, and each
/// one has a test that would fail if somebody quietly changed the model:
///
/// <list type="number">
/// <item><b>Reception is geometry.</b> Climbing extends the range of a station, distance
/// shortens it, and rain takes its cut. The interesting consequence is that flying low to
/// stay out of a threat envelope (D-010) is also what loses the music, and the numbers
/// say how much.</item>
/// <item><b>The set is kind.</b> An aircraft inside its own vibration caution never drops
/// out at all, and nothing in the model can move the volume knob or the on/off switch.
/// That is the difference between characterful and annoying and it is worth asserting
/// rather than intending.</item>
/// <item><b>Playlist selection is a pure function of the clock.</b> A station has been
/// playing whether or not anybody was listening, so tuning away and back lands you further
/// into its running order, not at the start of it.</item>
/// <item><b>The music ducks under the horn, by a measured margin.</b> The audio benchmark
/// says "three synths and a warning system all write into the master bus at fixed gains,
/// and nothing ducks anything", and names the missing test: render both and measure the
/// horn's band energy against the other's. That is <see cref="Ducking"/>.</item>
/// </list>
/// </summary>
public static class RadioTests
{
    private const int Rate = 44100;

    // ------------------------------------------------------------- measurement

    private static double Rms(IReadOnlyList<float> x)
    {
        double sum = 0;
        for (int i = 0; i < x.Count; i++) sum += (double)x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(x.Count, 1));
    }

    private static double Db(double linear) => 20.0 * Math.Log10(Math.Max(linear, 1e-12));

    /// <summary>
    /// Energy between two frequencies, by a swept Goertzel.
    ///
    /// Swept over a range rather than measured at a single bin, because masking is a
    /// critical-band phenomenon: what hides a 400 Hz tone is not energy at exactly 400 Hz,
    /// it is everything inside roughly a third of an octave of it. 355-450 Hz is that
    /// band. Measuring the single bin instead flatters the horn by about 10 dB and answers
    /// a question nobody's ear is asking.
    /// </summary>
    private static double BandEnergy(float[] x, double loHz, double hiHz, double stepHz = 5.0)
    {
        double total = 0;
        for (double hz = loHz; hz <= hiHz; hz += stepHz)
        {
            double coeff = 2.0 * Math.Cos(2.0 * Math.PI * hz / Rate);
            double s1 = 0, s2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double s0 = x[i] + coeff * s1 - s2;
                s2 = s1; s1 = s0;
            }
            total += (s1 * s1 + s2 * s2 - coeff * s1 * s2) / x.Length;
        }
        return total;
    }

    /// <summary>A third of an octave either side of the low-rotor horn.</summary>
    private const double HornBandLo = 355.0;
    private const double HornBandHi = 450.0;

    // ------------------------------------------------------------- the sources

    /// <summary>
    /// A stand-in for a rock track, because there are no music files in this repo and
    /// inventing some would be worse than approximating one.
    ///
    /// What matters for a mix test is not the tune, it is the statistics: a mastered rock
    /// record is dense, broadband, heavily limited - so it has a crest factor near 1.5
    /// rather than a sine's 1.41 or an impulse train's 8 - and it sits around -10 dBFS
    /// RMS. This is filtered noise with a backbeat, soft-clipped, then normalised to
    /// exactly that. It has energy at 400 Hz, which is the entire reason the horn needs
    /// protecting from it.
    /// </summary>
    private static float[] SurrogateMusic(double seconds, double targetDbfs = -10.0)
    {
        int n = (int)(Rate * seconds);
        var x = new float[n];
        var rng = new Random(4242);
        double lp = 0, hp = 0, dt = 1.0 / Rate;

        for (int i = 0; i < n; i++)
        {
            double noise = rng.NextDouble() * 2 - 1;
            lp += (noise - lp) * 0.35;             // roll the top off
            hp += (lp - hp) * 0.004;               // and the bottom
            double body = lp - hp;

            // A backbeat at 2 Hz plus a bass note, so it behaves like music rather than
            // like hiss under an envelope follower.
            double t = i * dt;
            double beat = 0.72 + 0.28 * Math.Abs(Math.Sin(Math.PI * t * 2.0));
            double bass = Math.Sin(2 * Math.PI * 82.4 * t) * 0.30;
            double gtr = Math.Sin(2 * Math.PI * 329.6 * t) * 0.12;

            x[i] = (float)Math.Tanh((body * 2.4 + bass + gtr) * beat * 1.6);
        }

        double have = Rms(x), want = Math.Pow(10.0, targetDbfs / 20.0);
        float k = (float)(want / Math.Max(have, 1e-9));
        for (int i = 0; i < n; i++) x[i] *= k;
        return x;
    }

    /// <summary>The low-rotor horn, rendered at the gain the game actually plays it at.</summary>
    private static float[] Horn(double seconds, float playerGain)
    {
        var synth = new WarningSynth(Rate);
        int n = (int)(Rate * seconds);
        var all = new float[n];
        const int block = 512;
        for (int i = 0; i < n; i += block)
        {
            int m = Math.Min(block, n - i);
            synth.Render(all.AsSpan(i, m), m, true, false, (double)m / Rate);
        }
        for (int i = 0; i < n; i++) all[i] *= playerGain;
        return all;
    }

    /// <summary>
    /// The aircraft, hovering, at the level it actually reaches the listener at.
    ///
    /// Two gains, not one. <see cref="HelicopterAudio"/> plays the rotor through an
    /// <c>AudioStreamPlayer3D</c> at 0.75, and Godot clamps that player's distance
    /// attenuation at its default <c>max_db</c> of +3 - so close up, which is every moment
    /// the player is in the aircraft, the rotor arrives 3 dB hotter than its player gain
    /// suggests. Leaving that out is how a mix budget ends up 3 dB wrong in the direction
    /// that matters.
    /// </summary>
    private static float[] Rotor(double seconds, float playerGain)
    {
        var af = Airframe.Workhorse();
        var s = new SynthState
        {
            RotorOmega = af.MainRotor.NominalOmega,
            NominalOmega = af.MainRotor.NominalOmega,
            MainBlades = af.MainRotor.NumBlades,
            TailBlades = af.TailRotor.NumBlades,
            TailGearRatio = af.TailRotor.GearRatio,
            Collective = 0.625,
            TorqueFraction = 0.75,
            Airspeed = 0,
            N1 = 0.9,
            EngineRunning = true,
            BladeLoading = 0.0748,
            TipMach = 0.62,
            TailRotorHealth = 1.0,
        };
        var synth = new RotorSynth(Rate);
        synth.Prime(s);
        int n = (int)(Rate * seconds);
        var all = new float[n];
        const int block = 512;
        for (int i = 0; i < n; i += block)
        {
            int m = Math.Min(block, n - i);
            synth.Render(all.AsSpan(i, m), m, s, (double)m / Rate);
        }
        const float MaxDbClamp = 1.41254f;   // +3 dB, Godot's AudioStreamPlayer3D default
        for (int i = 0; i < n; i++) all[i] *= playerGain * MaxDbClamp;
        return all;
    }

    private static RadioLibrary Library(params string[] files)
        => RadioLibrary.Build(files, null);

    // =========================================================== 1. reception

    /// <summary>
    /// Reception is geometry, and the geometry has consequences the player can learn.
    /// </summary>
    public static string? Reception()
    {
        var station = new RadioStation("freq.1", "Hill mast", 0, 0, 40, 10.0);

        Console.WriteLine("  a 10 km mast, quality against distance and height AGL");
        Console.WriteLine("      km      50 m     150 m     300 m     600 m");

        double[] heights = { 50, 150, 300, 600 };
        var grid = new Dictionary<(double, double), double>();
        foreach (double km in new[] { 2.0, 5.0, 8.0, 12.0, 18.0 })
        {
            string row = $"    {km,4:F0}  ";
            foreach (double h in heights)
            {
                double q = Radio.Quality(km * 1000, h, station, 0, 0);
                grid[(km, h)] = q;
                row += $"{q,8:F2}  ";
            }
            Console.WriteLine(row);
        }

        // Monotonic in both variables - this is the whole claim.
        foreach (double km in new[] { 2.0, 5.0, 8.0, 12.0, 18.0 })
            for (int i = 1; i < heights.Length; i++)
                if (grid[(km, heights[i])] < grid[(km, heights[i - 1])] - 1e-9)
                    return $"climbing made reception worse at {km} km";

        foreach (double h in heights)
        {
            double last = double.MaxValue;
            foreach (double km in new[] { 2.0, 5.0, 8.0, 12.0, 18.0 })
            {
                double q = grid[(km, h)];
                if (q > last + 1e-9) return $"flying away improved reception at {h} m";
                last = q;
            }
        }

        // The gameplay fact D-010 collides with, in the two places it bites.
        //
        // At 8 km the difference between nap-of-the-earth and a normal cruise is the
        // difference between a station you can just about stand and a clean one; at 12 km
        // it is the difference between a station and no station. Flying low to stay out of
        // a threat envelope costs you the music, and the player gets to decide which they
        // want. That is a design claim, so here are the numbers for it.
        double low = grid[(8.0, 50.0)], high = grid[(8.0, 300.0)];
        Console.WriteLine($"  at  8 km: {low:F2} on the deck, {high:F2} at 300 m " +
                          $"({high / Math.Max(low, 1e-6):F1}x)");
        if (high < low * 2.0) return $"height barely matters at 8 km: {low:F2} -> {high:F2}";

        double far = grid[(12.0, 50.0)], farHigh = grid[(12.0, 600.0)];
        Console.WriteLine($"  at 12 km: {far:F2} on the deck, {farHigh:F2} at 600 m");
        if (far > 0.10) return $"the deck still receives at 12 km: {far:F2}";
        if (farHigh < 0.50) return $"600 m does not rescue a 12 km station: {farHigh:F2}";

        // Weather takes a real bite. It degrades a close station and it finishes off a
        // marginal one, which is the right shape: a downpour does not take your local
        // transmitter away, and it absolutely takes away the one on the far ridge.
        double dry = Radio.Quality(4000, 300, station, 0, 0);
        double wet = Radio.Quality(4000, 300, station, 1.0, 0);
        double storm = Radio.Quality(4000, 300, station, 1.0, 1.0);
        Console.WriteLine($"  4 km at 300 m:  dry {dry:F2}   downpour {wet:F2}   storm {storm:F2}");
        if (dry < 0.99) return $"a close station at height is not clean: {dry:F2}";
        if (storm >= wet || wet > dry) return "weather is not monotonic on the band";
        if (storm > 0.80) return $"a storm barely touches a close station: {storm:F2}";
        if (storm < 0.20) return $"a storm kills a station 4 km away: {storm:F2}";

        double edgeDry = Radio.Quality(8000, 150, station, 0, 0);
        double edgeStorm = Radio.Quality(8000, 150, station, 1.0, 1.0);
        Console.WriteLine($"  8 km at 150 m:  dry {edgeDry:F2}   storm {edgeStorm:F2}");
        if (edgeDry < 0.3) return $"a station 8 km away at 150 m is already gone: {edgeDry:F2}";
        if (edgeStorm > 0.01) return $"a storm does not finish off a marginal station: {edgeStorm:F2}";

        // Right underneath it, nothing takes it away.
        double overhead = Radio.Quality(300, 20, station, 1.0, 1.0);
        Console.WriteLine($"  300 m from the mast in a storm at 20 m: {overhead:F2}");
        if (overhead < 0.99) return $"the mast is unusable from underneath it: {overhead:F2}";

        return null;
    }

    // ===================================================== 2. it keeps playing

    /// <summary>
    /// The set must be a machine in a ruined world without being a nuisance.
    ///
    /// Ten minutes of ordinary flight, and then the same ten minutes with the rotor out of
    /// track and the avionics shot. The first must be uninterrupted; the second must be
    /// interrupted and must recover on its own every time.
    /// </summary>
    public static string? KeepsPlaying()
    {
        var lib = Library("tape_a/01 one.ogg", "tape_a/02 two.ogg", "tape_a/03 three.ogg");
        const double dt = 1.0 / 60.0;
        const int steps = 60 * 600;   // ten minutes

        int Fly(double vib, double health, out int silentFrames)
        {
            var set = new RadioSet(seed: 99);
            set.SetPower(true);
            set.SelectTape(0);
            var c = new RadioConditions(true, health, vib, 1.0);
            int quiet = 0;
            for (int i = 0; i < steps; i++)
            {
                set.Update(dt, lib, c, i * dt);
                if (!set.Playing) quiet++;
            }
            silentFrames = quiet;
            return set.Dropouts;
        }

        Console.WriteLine("  ten minutes of tape, at 60 Hz");
        Console.WriteLine("     vib ips   avionics   dropouts   silent");

        foreach ((double vib, double health) in new[]
                 { (0.20, 1.00), (0.55, 1.00), (0.60, 0.85), (0.95, 1.00), (1.20, 0.50) })
        {
            int drops = Fly(vib, health, out int quiet);
            Console.WriteLine($"      {vib,6:F2}    {health,6:F2}     {drops,6}   {quiet * dt,6:F1} s");

            bool ordinary = vib <= DamageState.VibrationCautionIps
                         && health >= RadioSet.HealthFullyReliable;
            if (ordinary && drops != 0)
                return $"a serviceable aircraft dropped out {drops} times " +
                       $"(vib {vib:F2}, avionics {health:F2})";
            if (!ordinary && drops == 0)
                return $"a sick set never dropped out at all (vib {vib:F2}, avionics {health:F2})";
            // Even at its worst it must be music with gaps, not gaps with music.
            if (quiet * dt > steps * dt * 0.35)
                return $"the set is silent {quiet * dt / (steps * dt):P0} of the time at " +
                       $"vib {vib:F2}, avionics {health:F2} - that is a nuisance, not a machine";
        }

        // Every dropout ends by itself. Hold the worst case and check the set recovers.
        var s2 = new RadioSet(seed: 7);
        s2.SetPower(true);
        s2.SelectTape(0);
        var bad = new RadioConditions(true, 0.40, 1.60, 1.0);
        double longestSilence = 0, run = 0;
        for (int i = 0; i < 60 * 300; i++)
        {
            s2.Update(dt, lib, bad, i * dt);
            if (s2.Playing) run = 0; else { run += dt; longestSilence = Math.Max(longestSilence, run); }
        }
        Console.WriteLine($"  worst case (vib 1.60, avionics 0.40): longest gap {longestSilence:F2} s, " +
                          $"{s2.Dropouts} dropouts in 5 min");
        if (longestSilence > RadioSet.DropoutMaxSeconds * 2.5)
            return $"a dropout lasted {longestSilence:F2} s - the set is not recovering on its own";

        // Below the avionics floor it is off, and stays off, and is honest about why.
        var s3 = new RadioSet();
        s3.SetPower(true);
        s3.SelectTape(0);
        s3.Update(dt, lib, new RadioConditions(true, DamageState.AvionicsFloor - 0.01, 0.1, 1.0), 0);
        Console.WriteLine($"  avionics below the floor: {s3.Why}");
        if (s3.Why != RadioSilence.Unserviceable) return $"a dead avionics stack reports {s3.Why}";
        if (!s3.PowerOn) return "an avionics failure switched the set off - the player did not";

        // And with no set fitted at all it is simply absent.
        var s4 = new RadioSet();
        s4.SetPower(true);
        s4.SelectTape(0);
        s4.Update(dt, lib, new RadioConditions(false, 1.0, 0.1, 1.0), 0);
        if (s4.Why != RadioSilence.NotFitted) return $"an aircraft with no radio reports {s4.Why}";

        return null;
    }

    /// <summary>
    /// The volume you set stays set.
    ///
    /// The one promise that makes the rest of this system tolerable, so it is asserted
    /// against everything that could plausibly move it: dropouts, damage, the avionics
    /// stack dying and coming back, the signal going and returning, and a warning ducking
    /// the mix for a minute.
    /// </summary>
    public static string? VolumeStaysSet()
    {
        var lib = Library("mix/01 a.ogg", "mix/02 b.ogg");
        var set = new RadioSet(seed: 31);
        var mix = new RadioMix();

        set.SetPower(true);
        set.SelectTape(0);
        set.SetVolume(0.73);
        const double want = 0.73;
        const double dt = 1.0 / 60.0;

        for (int i = 0; i < 60 * 240; i++)
        {
            double t = i * dt;
            // A sortie's worth of abuse: the rotor goes out of track, the avionics take a
            // hit and are partly repaired, and a warning comes and goes.
            double vib = 0.3 + 1.4 * Math.Max(0, Math.Sin(t / 37.0));
            double health = t > 60 && t < 140 ? 0.20 : 0.55;
            double signal = 0.5 + 0.5 * Math.Sin(t / 11.0);
            bool warning = Math.Sin(t / 19.0) > 0.7;

            set.Update(dt, lib, new RadioConditions(true, health, vib, signal), t);
            mix.Update(dt, warning, false);

            if (Math.Abs(set.Volume - want) > 1e-12)
                return $"volume moved to {set.Volume:F4} at t={t:F1} s";
            if (!set.PowerOn)
                return $"the set switched itself off at t={t:F1} s ({set.Why})";
        }

        Console.WriteLine($"  four minutes of vibration, damage, signal loss and warnings:");
        Console.WriteLine($"  volume still {set.Volume:F2}, power still on, {set.Dropouts} dropouts");
        Console.WriteLine($"  duck is back at {mix.GainDb:F2} dB after the last warning");

        // The duck itself must have come back, or "the volume stayed set" is a lie in
        // everything but the accounting.
        for (int i = 0; i < 60 * 5; i++) mix.Update(dt, false, false);
        if (mix.Gain < 0.999) return $"the ducker never released: {mix.GainDb:F2} dB";

        return null;
    }

    // ================================================== 3. library and playlist

    /// <summary>
    /// Build a library out of a folder listing, and select out of it.
    ///
    /// Also the degrade-to-silence case, which is the one the repo is actually in: no
    /// music files at all, and nothing anywhere may throw or hang because of it.
    /// </summary>
    public static string? Playlist()
    {
        // --- Nothing installed ------------------------------------------------
        var empty = RadioLibrary.Build(Array.Empty<string>(), null);
        var quiet = new RadioSet();
        quiet.SetPower(true);
        quiet.SelectTape(0);
        quiet.Update(1.0 / 60, empty, new RadioConditions(true, 1, 0.1, 1), 0);
        Console.WriteLine($"  empty library: {Radio.Readout(quiet, empty, Array.Empty<RadioStation>())}");
        if (!empty.Empty) return "an empty listing did not produce an empty library";
        if (quiet.Why != RadioSilence.NoSource) return $"empty library reports {quiet.Why}";
        if (Radio.TapeAtSite(12345, empty.Tapes.Count) >= 0)
            return "a cassette is findable in a game with no music in it";

        // --- A folder of files, plus a manifest -------------------------------
        string[] files =
        {
            "readme.txt",                       // ignored
            "side_b/02 the other one.ogg",
            "side_b/01 first one.ogg",
            "a_long_drive/03 third.mp3",
            "a_long_drive/01 first.mp3",
            "a_long_drive/02 second.mp3",
            "loose one.wav",
        };
        string manifest = string.Join("\n", new[]
        {
            "# comment, ignored",
            "",
            "a_long_drive/01 first.mp3 | Sawmill | The Undercards | CC BY 4.0 | https://example.invalid/1 | -2.5 | 184",
            "a_long_drive/02 second.mp3 | Dust Off The Panel | The Undercards | CC BY 4.0 | https://example.invalid/2 | 0 | 233",
            "side_b/01 first one.ogg | Nothing On The Band | Reeve | CC0 1.0 | https://example.invalid/3",
        });

        var lib = RadioLibrary.Build(files, manifest);
        Console.WriteLine($"  {lib.Tracks.Count} tracks on {lib.Tapes.Count} tapes, " +
                          $"{lib.TotalSeconds / 60:F1} min total");
        foreach (RadioTape tape in lib.Tapes)
            Console.WriteLine($"    {tape.Name,-16} {tape.Tracks.Count} tracks");

        if (lib.Tracks.Count != 6) return $"expected 6 audio files, got {lib.Tracks.Count}";
        if (lib.Tapes.Count != 3) return $"expected 3 tapes, got {lib.Tapes.Count}";
        if (lib.Tapes[^1].Name != "Loose tape")
            return $"the loose tape is not last: {lib.Tapes[^1].Name}";

        // Metadata arrived where it was meant to, and the filename stood in where it did not.
        RadioTrack named = lib.Tracks[lib.Tapes[0].Tracks[0]];
        Console.WriteLine($"  first track: {named.Display}  [{named.Licence}]  {named.PlaySeconds:F0} s");
        if (named.Artist != "The Undercards") return $"manifest artist not applied: '{named.Artist}'";
        if (Math.Abs(named.GainDb + 2.5) > 1e-9) return $"manifest gain not applied: {named.GainDb}";
        if (Math.Abs(named.Seconds - 184) > 1e-9) return $"manifest duration not applied: {named.Seconds}";

        RadioTrack unlisted = lib.Tracks[lib.Tapes[0].Tracks[2]];
        if (unlisted.Title != "third") return $"filename title is '{unlisted.Title}'";
        if (Math.Abs(unlisted.PlaySeconds - Radio.AssumedTrackSeconds) > 1e-9)
            return "an unknown duration did not fall back to the assumed one";

        // The licensing register's safety net: what is playing with no licence recorded.
        Console.WriteLine($"  undeclared: {lib.Undeclared.Count} of {lib.Tracks.Count}");
        foreach (string p in lib.Undeclared) Console.WriteLine($"    {p}");
        if (lib.Undeclared.Count != 3)
            return $"expected 3 undeclared tracks, got {lib.Undeclared.Count}";

        // A round trip through the manifest writer keeps everything the manifest carried.
        var again = RadioLibrary.Build(files, Radio.WriteManifest(lib));
        if (again.Tracks.Count != lib.Tracks.Count) return "manifest round trip lost tracks";
        if (again.Tracks[0].Licence != lib.Tracks[0].Licence) return "manifest round trip lost a licence";

        // --- A tape plays through and wraps -----------------------------------
        var set = new RadioSet(seed: 5);
        set.SetPower(true);
        set.SelectTape(0);                              // a_long_drive: 184 + 233 + 215 s
        var c = new RadioConditions(true, 1, 0.1, 1);
        var seen = new List<int>();
        for (double t = 0; t < 700; t += 0.5)
        {
            set.Update(0.5, lib, c, t);
            if (set.TrackChanged) seen.Add(set.Track);
        }
        Console.WriteLine($"  a tape side, 700 s: track changes {string.Join(" -> ", seen)}");
        if (seen.Count < 4) return $"a 632 s tape did not wrap in 700 s ({seen.Count} changes)";
        if (seen[0] != lib.Tapes[0].Tracks[0]) return "a tape did not start at its first track";
        if (seen[3] != seen[0]) return "a tape did not wrap back to its first track";

        // --- A station has been playing without you ---------------------------
        (int trackA, double offA) = Radio.StationNow(lib, 0, 1000.0);
        (int trackB, double offB) = Radio.StationNow(lib, 0, 1000.0);
        if (trackA != trackB || Math.Abs(offA - offB) > 1e-9)
            return "the station schedule is not a pure function of the clock";

        (int trackC, double offC) = Radio.StationNow(lib, 0, 1060.0);
        Console.WriteLine($"  station 0 at t=1000 s: track {trackA} +{offA:F0} s; " +
                          $"at t=1060 s: track {trackC} +{offC:F0} s");
        if (trackC == trackA && Math.Abs(offC - offA - 60.0) > 1e-6)
            return "a station did not advance with the clock";

        // Two stations are not the same jukebox.
        bool differ = false;
        for (double t = 0; t < lib.TotalSeconds; t += 37)
            if (Radio.StationNow(lib, 0, t).Track != Radio.StationNow(lib, 1, t).Track) { differ = true; break; }
        if (!differ) return "every station plays the same track at the same moment";

        // Tuning away and back lands further in, not at the start.
        var s = new RadioSet(seed: 11);
        s.SetPower(true);
        s.SelectStation(0);
        s.Update(1, lib, c, 1000);
        (int wasTrack, double wasOff) = (s.Track, s.Position);
        s.SelectTape(0);
        s.Update(1, lib, c, 1000);
        s.SelectStation(0);
        s.Update(1, lib, c, 1000 + 90);
        Console.WriteLine($"  tuned away and back 90 s later: track {wasTrack} +{wasOff:F0} s " +
                          $"-> track {s.Track} +{s.Position:F0} s");
        if (s.Track == wasTrack && s.Position <= wasOff)
            return "a station restarted where it was left rather than where it got to";

        return null;
    }

    // ============================================================= 4. the mix

    /// <summary>
    /// The one the audio benchmark asked for: does the horn still cut through.
    ///
    /// Three voices are rendered at the gains the game actually plays them at - the rotor
    /// through its 3D player, the horn through the warning panel's measured 0.68, and the
    /// surrogate music through <see cref="Radio.PlayerGain"/> with the volume knob at
    /// maximum, which is the worst case the mix has to survive. Then the horn's energy at
    /// 400 Hz is measured against everything else's, with the ducker off and on.
    /// </summary>
    public static string? Ducking()
    {
        const double seconds = 2.0;
        float[] music = SurrogateMusic(seconds);
        float[] horn = Horn(seconds, 0.68f);
        float[] rotor = Rotor(seconds, 0.75f);

        double musicFile = Rms(music);
        Console.WriteLine($"  source levels (RMS): surrogate master {Db(musicFile):F1} dBFS, " +
                          $"horn at the player {Db(Rms(horn)):F1} dBFS, " +
                          $"rotor at the listener {Db(Rms(rotor)):F1} dBFS");

        // --- The radio's own gain staging -------------------------------------
        double gainOpen = Radio.PlayerGain(1.0, 0.0);
        double openDb = Db(musicFile * gainOpen);
        double rotorDb = Db(Rms(rotor));
        Console.WriteLine($"  radio wide open: {openDb:F1} dBFS " +
                          $"({openDb - rotorDb:+0.0;-0.0} dB against the rotor)");
        if (openDb > rotorDb + 1.0)
            return $"the radio at full volume is louder than the aircraft: " +
                   $"{openDb:F1} vs {rotorDb:F1} dBFS";

        // --- Hold the duck down and measure ------------------------------------
        var mix = new RadioMix();
        const double dt = 1.0 / 60.0;
        for (int i = 0; i < 60; i++) mix.Update(dt, true, false);   // one second of horn
        double duck = mix.Gain;
        Console.WriteLine($"  duck settles at {Db(duck):F2} dB " +
                          $"(target {RadioMix.WarningDuckDb:F1})");
        if (Db(duck) > RadioMix.WarningDuckDb + 0.5)
            return $"the duck did not reach its target in a second: {Db(duck):F2} dB";

        double hornDb = Db(Rms(horn));
        double duckedDb = Db(musicFile * gainOpen * duck);
        Console.WriteLine($"  horn over music:  {hornDb - openDb:F1} dB undacked, " +
                          $"{hornDb - duckedDb:F1} dB ducked");
        Console.WriteLine($"  music ducked to {duckedDb:F1} dBFS — " +
                          $"{rotorDb - duckedDb:F1} dB below the rotor");
        if (hornDb - duckedDb < 12.0)
            return $"the horn is only {hornDb - duckedDb:F1} dB over the ducked music";

        // --- Who is actually in the horn's way, measured ------------------------
        //
        // The premise going in was that music would bury the horn at 400 Hz. It does not,
        // and the measurement is the reason to believe that rather than the reason to
        // assume it: the rotor sets the floor in the horn's critical band and always has.
        // What ducking buys is broadband headroom - the music is otherwise the loudest
        // continuous thing in the cabin after the horn, at a margin of about 7 dB, and 7 dB
        // over a dense limited master is not a foreground.
        var bedOpen = new float[horn.Length];
        var bedDucked = new float[horn.Length];
        var musicOpen = new float[horn.Length];
        float gOpen = (float)gainOpen, gDuck = (float)(gainOpen * duck);
        for (int i = 0; i < horn.Length; i++)
        {
            musicOpen[i] = (float)(music[i] * gOpen);
            bedOpen[i] = (float)(rotor[i] + musicOpen[i]);
            bedDucked[i] = (float)(rotor[i] + music[i] * gDuck);
        }

        double hornBand = BandEnergy(horn, HornBandLo, HornBandHi);
        double rotorBand = BandEnergy(rotor, HornBandLo, HornBandHi);
        double musicBand = BandEnergy(musicOpen, HornBandLo, HornBandHi);
        double openBand = BandEnergy(bedOpen, HornBandLo, HornBandHi);
        double duckBand = BandEnergy(bedDucked, HornBandLo, HornBandHi);

        double Ratio(double a, double b) => 10 * Math.Log10(a / Math.Max(b, 1e-30));
        double snrOpen = Ratio(hornBand, openBand);
        double snrDuck = Ratio(hornBand, duckBand);

        double rotorOnly = Ratio(hornBand, rotorBand);
        Console.WriteLine($"  {HornBandLo:F0}-{HornBandHi:F0} Hz, the horn's critical band:");
        Console.WriteLine($"     rotor alone          {rotorOnly,6:F1} dB below the horn");
        Console.WriteLine($"     music alone, open    {Ratio(hornBand, musicBand),6:F1} dB below the horn");
        Console.WriteLine($"     horn over rotor+music, no ducking  {snrOpen,6:F1} dB");
        Console.WriteLine($"     horn over rotor+music, ducking     {snrDuck,6:F1} dB " +
                          $"({snrDuck - snrOpen:+0.0;-0.0} dB)");
        Console.WriteLine($"     radio switched off entirely        {rotorOnly,6:F1} dB");

        if (snrDuck < snrOpen)
            return "ducking made the horn's band worse, which should not be possible";
        if (snrDuck < 15.0)
            return $"the horn does not own its own band with the radio ducked: {snrDuck:F1} dB";

        // The claim worth asserting, because it is the one a player would notice going
        // wrong: with the ducker working, having the radio ON costs the horn nothing. Its
        // margin with music ducked must equal its margin with no radio in the aircraft at
        // all, to within half a decibel.
        if (rotorOnly - snrDuck > 0.5)
            return $"the radio costs the horn {rotorOnly - snrDuck:F1} dB even ducked " +
                   $"({snrDuck:F1} dB with, {rotorOnly:F1} dB without)";

        // --- No pumping on a pulsed master warning ------------------------------
        // The master warning is silent 167 ms out of every 303. A ducker that releases in
        // those gaps pumps the music at 3.3 Hz, which is worse than not ducking at all.
        var pulse = new RadioMix();
        double period = 1.0 / WarningSynth.WarningRepHz;
        double worst = 0;
        for (int i = 0; i < (int)(6.0 / dt); i++)
        {
            double t = i * dt;
            bool on = (t % period) < period * WarningSynth.WarningDuty;
            pulse.Update(dt, on, false);
            if (t > 1.0) worst = Math.Max(worst, pulse.Gain);
        }
        double ripple = Db(worst) - RadioMix.WarningDuckDb;
        Console.WriteLine($"  six seconds of pulsed master warning: worst recovery " +
                          $"{Db(worst):F2} dB, ripple {ripple:F2} dB");
        if (ripple > 1.5)
            return $"the duck pumps {ripple:F1} dB at the master warning's {WarningSynth.WarningRepHz} Hz";

        // --- And it lets go afterwards ------------------------------------------
        double halfWay = -1, full = -1;
        for (int i = 0; i < (int)(4.0 / dt); i++)
        {
            pulse.Update(dt, false, false);
            double t = (i + 1) * dt;
            if (halfWay < 0 && Db(pulse.Gain) > RadioMix.WarningDuckDb / 2) halfWay = t;
            if (full < 0 && Db(pulse.Gain) > -1.0) { full = t; break; }
        }
        Console.WriteLine($"  after the warning clears: half back in {halfWay:F2} s, " +
                          $"within 1 dB of the level you set in {full:F2} s");
        if (full < 0) return $"the music never came back: {Db(pulse.Gain):F2} dB after 4 s";
        if (halfWay < RadioMix.HoldSeconds)
            return "the duck released before its hold expired - it will pump on a pulsed tone";

        // --- The chime and a conversation duck less than a warning --------------
        var chime = new RadioMix();
        chime.TriggerCaution();
        for (int i = 0; i < 30; i++) chime.Update(dt, false, false);
        double chimeDb = Db(chime.Gain);

        var talk = new RadioMix();
        for (int i = 0; i < 30; i++) talk.Update(dt, false, true);
        Console.WriteLine($"  caution chime {chimeDb:F1} dB, dialogue {Db(talk.Gain):F1} dB, " +
                          $"warning {RadioMix.WarningDuckDb:F1} dB");
        if (!(chimeDb > RadioMix.WarningDuckDb && Db(talk.Gain) > RadioMix.WarningDuckDb))
            return "a chime or a conversation ducks as hard as a warning does";

        return null;
    }

    // ========================================================= 5. as equipment

    /// <summary>
    /// The radio is a refit module and its tapes are salvage. Neither is a special case,
    /// and this checks that by going through the systems that already exist.
    /// </summary>
    public static string? AsEquipment()
    {
        // It is in the catalogue, it has mass, it fits in a bay.
        if (!Loadout.Catalog.TryGetValue(Radio.ModuleId, out ModuleDef? def))
            return $"no '{Radio.ModuleId}' module in the refit catalogue";
        Console.WriteLine($"  module: {def.Name}, {def.Mass:F0} kg at {def.Position}, " +
                          $"{def.PartsCost} parts to fit");
        if (def.Mass <= 0) return "the radio weighs nothing";

        var loadout = new Loadout();
        if (loadout.Install(Radio.ModuleId) is not null) return "fitted a radio nobody found";
        if (!loadout.Find(Radio.ModuleId)) return "a found radio did not reach the bag";
        if (loadout.Install(Radio.ModuleId) is null) return "a radio in the bag would not fit";
        if (!loadout.IsInstalled(Radio.ModuleId)) return "the radio is not installed after fitting";
        if (loadout.Remove(Radio.ModuleId) is null) return "the radio cannot be taken back out";

        // Adding it must not have moved any other module's mass or drag budget.
        foreach (ModuleDef m in Loadout.All)
            if (m.Id != "suppressor" && (Math.Abs(m.DragDelta.X) + Math.Abs(m.DragDelta.Y)
                                       + Math.Abs(m.DragDelta.Z)) > 0.001)
                return $"module '{m.Id}' has an unexpected drag delta";

        // Sets and tapes are found by searching, deterministically, and often enough to
        // matter without being everywhere.
        int sites = 0, sets = 0;
        for (int id = 0; id < 400; id++)
        {
            var kind = (SalvageSiteKind)(id % 9);
            bool eligible = kind is SalvageSiteKind.Wreck or SalvageSiteKind.Settlement
                                 or SalvageSiteKind.Farmstead or SalvageSiteKind.Depot
                                 or SalvageSiteKind.Airfield;
            if (!eligible)
            {
                if (Radio.SetAtSite(id, kind)) return $"a radio turned up at a {kind}";
                continue;
            }
            sites++;
            if (Radio.SetAtSite(id, kind)) sets++;
        }
        Console.WriteLine($"  radio sets: {sets} at {sites} eligible sites " +
                          $"({(double)sets / sites:P0})");
        if (sets == 0) return "no radio exists anywhere in the world";
        if (sets < sites / 12 || sets > sites / 3)
            return $"radio sets are at {(double)sets / sites:P0} of eligible sites";

        // Determinism: the same world hands out the same hardware twice.
        for (int id = 0; id < 50; id++)
            if (Radio.SetAtSite(id, SalvageSiteKind.Wreck) != Radio.SetAtSite(id, SalvageSiteKind.Wreck))
                return "site rolls are not deterministic";

        // Tapes scale with how much music is installed.
        var whereAtThree = new List<int>();
        foreach (int count in new[] { 0, 1, 3, 8 })
        {
            int found = 0;
            for (int id = 0; id < 400; id++)
            {
                int t = Radio.TapeAtSite(id, count);
                if (t >= 0) { found++; if (count == 3) whereAtThree.Add(id); }
                if (t >= count) return $"tape index {t} with only {count} tapes installed";
            }
            Console.WriteLine($"  {count} tapes installed: {found} findable across 400 sites");
            if (count == 0 && found != 0) return "cassettes exist with no music installed";
            if (count > 0 && found == 0) return $"no cassette is findable with {count} tapes installed";
        }

        // Adding music must not move the cassettes that were already out there - the roll
        // for "is there a tape here" happens before the roll for "which tape", so dropping
        // a fourth folder in does not relocate the first three.
        var whereAtEight = new List<int>();
        for (int id = 0; id < 400; id++) if (Radio.TapeAtSite(id, 8) >= 0) whereAtEight.Add(id);
        if (whereAtThree.Count != whereAtEight.Count)
            return "adding music moved where the cassettes are";
        for (int i = 0; i < whereAtThree.Count; i++)
            if (whereAtThree[i] != whereAtEight[i]) return "adding music moved where the cassettes are";

        // The set remembers what the player left it doing, across a save.
        var before = new RadioSet();
        before.SetPower(true);
        before.SetVolume(0.42);
        before.SelectStation(2);
        var after = new RadioSet();
        after.Restore(before.PowerOn, before.Volume, before.Source,
                      before.TapeIndex, before.StationIndex, before.Track, before.Position);
        if (!after.PowerOn || Math.Abs(after.Volume - 0.42) > 1e-12
            || after.Source != RadioSourceKind.Station || after.StationIndex != 2)
            return "the set did not come back from a save the way it was left";
        Console.WriteLine($"  restored: power {after.PowerOn}, volume {after.Volume:F2}, " +
                          $"{after.Source} {after.StationIndex}");

        // The refit catalogue must not have drifted out of step with Radio.ModuleId.
        if (Loadout.All.Count != Loadout.Catalog.Count) return "catalogue and list disagree";

        return null;
    }
}
