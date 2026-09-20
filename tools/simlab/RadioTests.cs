using System;
using System.Collections.Generic;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The cockpit radio, measured.
///
/// The radio plays a live internet station (D-058, and Fred's follow-up), which means the
/// audio itself cannot be tested here - it is somebody else's transmitter and it is
/// different every time. Everything *around* it can be, and all of it is engine-free:
///
/// <list type="number">
/// <item><b>Reception is geometry.</b> Climbing extends a station's range, distance
/// shortens it, rain takes its cut. The interesting consequence is that flying low to stay
/// out of a threat envelope (D-010) is also what loses the music, and the numbers say by
/// how much.</item>
/// <item><b>The set is kind.</b> An aircraft inside its own vibration caution never drops
/// out at all, nothing in the model can move the volume knob or the on/off switch, and
/// every silence names its own cause and clears when that cause does. That is the
/// difference between characterful and annoying, and it is worth asserting rather than
/// intending.</item>
/// <item><b>The band is built out of the world.</b> A station is a relay mast the player
/// logged, bound to a broadcaster deterministically, and it degrades to silence at every
/// step: no station list, no frequencies logged, no set fitted.</item>
/// <item><b>An unknown station arrives at a known loudness.</b> The AGC is what makes the
/// mix budget a fact rather than a hope, because the URL in the config file could point at
/// anything.</item>
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
    /// A stand-in for what comes off the wire.
    ///
    /// The real thing was measured rather than guessed: 25 seconds of a live SomaFM
    /// Icecast stream, 128 kbps MP3, decoded through NLayer, came out at <b>-14.0 dBFS RMS
    /// with a peak of 0.935</b> — dense, limited, and about 10 dB hotter than a synth
    /// voice. That measurement is where <see cref="RadioAgc.TargetDbfs"/> comes from, and
    /// this generator reproduces its statistics: filtered noise with a backbeat and a bass
    /// line, soft-clipped so the crest factor is a record's rather than a sine's, then
    /// normalised to a stated level. It has energy at 400 Hz, which is the entire reason
    /// the horn needs protecting from it.
    /// </summary>
    private static float[] SurrogateMusic(double seconds, double targetDbfs = RadioAgc.TargetDbfs)
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
    /// Two gains, not one. <c>HelicopterAudio</c> plays the rotor through an
    /// <c>AudioStreamPlayer3D</c> at 0.75, and Godot clamps that player's distance
    /// attenuation at its default <c>max_db</c> of +3 - so close up, which is every moment
    /// the player is in the aircraft, the rotor arrives 3 dB hotter than its player gain
    /// suggests. Leaving that out is how a mix budget ends up 3 dB wrong in the direction
    /// that matters. The radio's own player is built with <c>MaxDb = 0</c> for the same
    /// reason, from the other side.
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

    private static RadioStation Mast(double powerKm = 10.0, double mastM = 40.0)
        => new("freq.1", "The Upland Service", "Hazel Ridge",
               "https://example.invalid/stream", 0, 0, mastM, powerKm, 0);

    // =========================================================== 1. reception

    /// <summary>Reception is geometry, and the geometry has consequences a player can learn.</summary>
    public static string? Reception()
    {
        RadioStation station = Mast();

        Console.WriteLine("  a 10 km mast, quality against distance and height AGL");
        Console.WriteLine("      km      50 m     150 m     300 m     600 m");

        double[] heights = { 50, 150, 300, 600 };
        double[] ranges = { 2.0, 5.0, 8.0, 12.0, 18.0 };
        var grid = new Dictionary<(double, double), double>();
        foreach (double km in ranges)
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
        foreach (double km in ranges)
            for (int i = 1; i < heights.Length; i++)
                if (grid[(km, heights[i])] < grid[(km, heights[i - 1])] - 1e-9)
                    return $"climbing made reception worse at {km} km";

        foreach (double h in heights)
        {
            double last = double.MaxValue;
            foreach (double km in ranges)
            {
                double q = grid[(km, h)];
                if (q > last + 1e-9) return $"flying away improved reception at {h} m";
                last = q;
            }
        }

        // The gameplay fact D-010 collides with, in the two places it bites. At 8 km the
        // difference between nap-of-the-earth and a normal cruise is the difference
        // between a station you can just about stand and a clean one; at 12 km it is the
        // difference between a station and no station. Flying low to stay out of a threat
        // envelope costs the music, and the player decides which they want.
        double low = grid[(8.0, 50.0)], high = grid[(8.0, 300.0)];
        Console.WriteLine($"  at  8 km: {low:F2} on the deck, {high:F2} at 300 m " +
                          $"({high / Math.Max(low, 1e-6):F1}x)");
        if (high < low * 2.0) return $"height barely matters at 8 km: {low:F2} -> {high:F2}";

        double far = grid[(12.0, 50.0)], farHigh = grid[(12.0, 600.0)];
        Console.WriteLine($"  at 12 km: {far:F2} on the deck, {farHigh:F2} at 600 m");
        if (far > 0.10) return $"the deck still receives at 12 km: {far:F2}";
        if (farHigh < 0.50) return $"600 m does not rescue a 12 km station: {farHigh:F2}";

        // Weather degrades a close station and finishes off a marginal one, which is the
        // right shape: a downpour does not take your local transmitter away, and it
        // absolutely takes away the one on the far ridge.
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

        // Right underneath the mast, nothing takes it away.
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
        const double dt = 1.0 / 60.0;
        const int steps = 60 * 600;   // ten minutes

        int Fly(double vib, double health, out int silentFrames)
        {
            var set = new RadioSet(seed: 99);
            set.SetPower(true);
            set.Tune(0);
            var c = new RadioConditions(true, health, vib, 1.0, RadioLink.Live);
            int quiet = 0;
            for (int i = 0; i < steps; i++)
            {
                set.Update(dt, 1, c);
                if (!set.Playing) quiet++;
            }
            silentFrames = quiet;
            return set.Dropouts;
        }

        Console.WriteLine("  ten minutes of a live station, at 60 Hz");
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
            if (quiet * dt > steps * dt * 0.35)
                return $"the set is silent {quiet * dt / (steps * dt):P0} of the time at " +
                       $"vib {vib:F2}, avionics {health:F2} - that is a nuisance, not a machine";
        }

        // Every dropout ends by itself. Hold the worst case and check the set recovers.
        var s2 = new RadioSet(seed: 7);
        s2.SetPower(true);
        s2.Tune(0);
        var bad = new RadioConditions(true, 0.40, 1.60, 1.0, RadioLink.Live);
        double longestSilence = 0, run = 0;
        for (int i = 0; i < 60 * 300; i++)
        {
            s2.Update(dt, 1, bad);
            if (s2.Playing) run = 0; else { run += dt; longestSilence = Math.Max(longestSilence, run); }
        }
        Console.WriteLine($"  worst case (vib 1.60, avionics 0.40): longest gap {longestSilence:F2} s, " +
                          $"{s2.Dropouts} dropouts in 5 min");
        if (longestSilence > RadioSet.DropoutMaxSeconds * 2.5)
            return $"a dropout lasted {longestSilence:F2} s - the set is not recovering on its own";

        // --- Every silence names its own cause, and clears when the cause does ---------
        var s3 = new RadioSet(seed: 3);
        var empty = Array.Empty<RadioStation>();

        (RadioConditions c, int count, RadioSilence want, string label)[] cases =
        {
            (new(false, 1.0, 0.1, 1.0, RadioLink.Live), 1, RadioSilence.NotFitted, "no set fitted"),
            (new(true, DamageState.AvionicsFloor - 0.01, 0.1, 1.0, RadioLink.Live), 1,
                RadioSilence.Unserviceable, "avionics below the floor"),
            (new(true, 1.0, 0.1, 1.0, RadioLink.Live), 0, RadioSilence.NoStation, "nothing logged"),
            (new(true, 1.0, 0.1, 0.0, RadioLink.Live), 1, RadioSilence.NoSignal, "out of range"),
            (new(true, 1.0, 0.1, 1.0, RadioLink.Connecting), 1, RadioSilence.Connecting, "warming up"),
            (new(true, 1.0, 0.1, 1.0, RadioLink.Failed), 1, RadioSilence.OffAir, "station is down"),
            (new(true, 1.0, 0.1, 1.0, RadioLink.Live), 1, RadioSilence.None, "playing"),
        };

        s3.SetPower(true);
        s3.Tune(0);
        foreach ((RadioConditions c, int count, RadioSilence want, string label) in cases)
        {
            s3.Update(dt, count, c);
            Console.WriteLine($"  {label,-24} -> {s3.Why,-14} \"{Radio.Readout(s3, empty)}\"");
            if (s3.Why != want) return $"{label} reports {s3.Why}, expected {want}";
            if (!s3.PowerOn) return $"{label} switched the set off - only the player may do that";
        }

        // The socket is only wanted when there is a point: in range, on, fitted, tuned.
        s3.Update(dt, 1, new RadioConditions(true, 1.0, 0.1, 0.0, RadioLink.Live));
        if (s3.WantsStream) return "the set holds a connection open while out of range";
        s3.Update(dt, 1, new RadioConditions(true, 1.0, 0.1, 0.6, RadioLink.Live));
        if (!s3.WantsStream) return "the set will not connect while in range";
        // ...and it holds the connection THROUGH a dropout rather than hanging up.
        var s4 = new RadioSet(seed: 12);
        s4.SetPower(true);
        s4.Tune(0);
        bool sawDropoutWithStream = false;
        for (int i = 0; i < 60 * 120; i++)
        {
            s4.Update(dt, 1, new RadioConditions(true, 0.45, 1.5, 1.0, RadioLink.Live));
            if (s4.Why == RadioSilence.Dropout && s4.WantsStream) sawDropoutWithStream = true;
        }
        if (!sawDropoutWithStream)
            return "the set hangs up the connection every time it drops out - " +
                   "that is four seconds of rebuffering for half a second of vibration";

        return null;
    }

    /// <summary>
    /// The volume you set stays set.
    ///
    /// The one promise that makes the rest of this system tolerable, so it is asserted
    /// against everything that could plausibly move it: dropouts, damage, the avionics
    /// stack dying and coming back, the signal going and returning, the station itself
    /// going off air, and a warning ducking the mix for a minute.
    /// </summary>
    public static string? VolumeStaysSet()
    {
        var set = new RadioSet(seed: 31);
        var mix = new RadioMix();

        set.SetPower(true);
        set.Tune(0);
        set.SetVolume(0.73);
        const double want = 0.73;
        const double dt = 1.0 / 60.0;
        int offAir = 0, noSignal = 0;

        for (int i = 0; i < 60 * 240; i++)
        {
            double t = i * dt;
            // A sortie's worth of abuse: the rotor goes out of track, the avionics take a
            // hit and are partly repaired, the aircraft flies in and out of range, and the
            // station's server falls over twice.
            double vib = 0.3 + 1.4 * Math.Max(0, Math.Sin(t / 37.0));
            double health = t > 60 && t < 140 ? 0.20 : 0.55;
            double signal = Math.Max(0, Math.Sin(t / 11.0));   // flies right out of range
            RadioLink link = Math.Sin(t / 23.0) > 0.85 ? RadioLink.Failed
                           : Math.Sin(t / 23.0) > 0.80 ? RadioLink.Connecting : RadioLink.Live;
            bool warning = Math.Sin(t / 19.0) > 0.7;

            set.Update(dt, 1, new RadioConditions(true, health, vib, signal, link));
            mix.Update(dt, warning, false);

            if (set.Why == RadioSilence.OffAir) offAir++;
            if (set.Why == RadioSilence.NoSignal) noSignal++;

            if (Math.Abs(set.Volume - want) > 1e-12)
                return $"volume moved to {set.Volume:F4} at t={t:F1} s";
            if (!set.PowerOn)
                return $"the set switched itself off at t={t:F1} s ({set.Why})";
        }

        Console.WriteLine("  four minutes of vibration, damage, range and server failures:");
        Console.WriteLine($"  volume still {set.Volume:F2}, power still on, {set.Dropouts} dropouts, " +
                          $"{offAir * dt:F0} s off air, {noSignal * dt:F0} s out of range");
        if (offAir == 0 || noSignal == 0) return "the abuse did not actually exercise both failures";

        // The duck itself must have come back, or "the volume stayed set" is a lie in
        // everything but the accounting.
        for (int i = 0; i < 60 * 5; i++) mix.Update(dt, false, false);
        Console.WriteLine($"  duck back at {mix.GainDb:F2} dB after the last warning");
        if (mix.Gain < 0.999) return $"the ducker never released: {mix.GainDb:F2} dB";

        return null;
    }

    // ============================================== 3. the band, and no band

    /// <summary>
    /// Build a band out of a config file and a world, and select on it.
    ///
    /// Also the degrade-to-silence cases, which are the ones the repository is actually
    /// in: no station list, nothing logged, no set fitted. Nothing anywhere may throw.
    /// </summary>
    public static string? Band()
    {
        // --- Nothing configured -----------------------------------------------
        foreach (string? nothing in new[] { null, "", "# only a comment\n\n", "not a url | x" })
        {
            List<RadioStreamDef> none = Radio.ParseStations(nothing);
            if (none.Count != 0) return $"parsed {none.Count} stations out of \"{nothing}\"";
        }
        var quiet = new RadioSet();
        quiet.SetPower(true);
        quiet.Tune(-1);
        quiet.Update(1.0 / 60, 0, new RadioConditions(true, 1, 0.1, 1, RadioLink.Idle));
        Console.WriteLine($"  empty band: \"{Radio.Readout(quiet, Array.Empty<RadioStation>())}\"");
        if (quiet.Why != RadioSilence.NoStation) return $"an empty band reports {quiet.Why}";

        // A station with no stream behind it is still a station; it just never opens a
        // socket. RadioDj's Upland Service is exactly this case.
        var offline = new RadioStation("freq.9", "The Upland Service", 1000, 2000, 60, 15);
        if (offline.Url.Length != 0) return "a station built without a URL has one";
        if (offline.Display != "The Upland Service") return $"bad display: {offline.Display}";

        // --- A config file ----------------------------------------------------
        string cfg = string.Join("\n", new[]
        {
            "# the band",
            "",
            "Indie Pop Rocks | https://ice1.somafm.com/indiepop-128-mp3",
            "Metal Detector  | https://ice1.somafm.com/metal-128-mp3 | -2.5",
            "https://ice1.somafm.com/bootliquor-128-mp3",          // bare URL
            "Not A Station   | ftp://example.invalid/nope",        // wrong scheme, dropped
            "   ",
        });
        List<RadioStreamDef> defs = Radio.ParseStations(cfg);
        Console.WriteLine($"  station list: {defs.Count} broadcasters");
        foreach (RadioStreamDef d in defs)
            Console.WriteLine($"    {d.Name,-20} {d.Url}  {d.GainDb:+0.0;-0.0;0}");

        if (defs.Count != 3) return $"expected 3 usable stations, got {defs.Count}";
        if (defs[1].GainDb != -2.5) return $"gain trim not parsed: {defs[1].GainDb}";
        if (defs[2].Name != "ice1.somafm.com")
            return $"a bare URL was not named after its host: '{defs[2].Name}'";

        // --- Masts carry broadcasters, deterministically -----------------------
        var band = new List<RadioStation>();
        (int id, string name)[] masts =
            { (11, "Hazel Ridge"), (47, "Sawtooth"), (92, "Long Acre"), (150, "Cold Shoulder") };
        foreach ((int id, string name) in masts)
            band.Add(Radio.BindStation(id, name, id * 137.0, id * 211.0, defs));

        Console.WriteLine("  the band, as the world built it:");
        foreach (RadioStation s in band)
            Console.WriteLine($"    {s.Display,-42} mast {s.MastHeightM,4:F0} m, {s.PowerKm,5:F1} km");

        foreach (RadioStation s in band)
        {
            if (s.Url.Length == 0) return $"{s.Id} carries no stream";
            if (s.MastHeightM is < 18 or > 70) return $"{s.Id} mast height {s.MastHeightM}";
            if (s.PowerKm is < 4.5 or > 14) return $"{s.Id} power {s.PowerKm}";
            RadioStation again = Radio.BindStation(int.Parse(s.Id[5..]), s.SiteName, s.X, s.Y, defs);
            if (again != s) return $"{s.Id} is not deterministic";
        }

        // --- Tuning ------------------------------------------------------------
        var set = new RadioSet(seed: 5);
        set.SetPower(true);
        var live = new RadioConditions(true, 1, 0.1, 1, RadioLink.Live);

        set.Tune(0);
        set.Update(1.0 / 60, band.Count, live);
        if (!set.StationChanged && set.StationIndex != 0) return "tuning did not take";

        var walked = new List<int>();
        for (int i = 0; i < band.Count + 1; i++) { set.Next(band.Count); walked.Add(set.StationIndex); }
        Console.WriteLine($"  next x{band.Count + 1} from 0: {string.Join(" -> ", walked)}");
        if (walked[^1] != walked[0]) return "the band does not wrap";

        set.Tune(0);
        set.Previous(band.Count);
        if (set.StationIndex != band.Count - 1) return "tuning back from the first does not wrap";

        // Tuning to the station already tuned is a no-op, because the game layer checks it
        // every frame and a reconnect per frame is a denial of service on somebody's server.
        set.Tune(2);
        set.Update(1.0 / 60, band.Count, live);
        set.Tune(2);
        if (set.StationChanged) return "retuning the same station asked for a reconnect";

        Console.WriteLine($"  readout while playing: \"{Radio.Readout(set, band)}\"");
        return null;
    }

    // ==================================================== 4. automatic gain

    /// <summary>
    /// An unknown station has to arrive at a known loudness, or the mix budget is fiction.
    ///
    /// The URL in the station list points at somebody else's transmitter and the player
    /// can change it to anything. The measured reference is a live SomaFM stream at -14.0
    /// dBFS; another station will be at -9, another at -22. Every claim
    /// <see cref="Ducking"/> makes about the horn's margin rests on this.
    /// </summary>
    public static string? Levelling()
    {
        const int block = 1024;
        const double blockSeconds = block / (double)Rate;

        Console.WriteLine("  stations arriving at different levels, 12 s each");
        Console.WriteLine("      arrives     settles at    AGC");

        foreach (double arriveDb in new[] { -6.0, -10.0, -14.0, -20.0, -26.0 })
        {
            float[] music = SurrogateMusic(12.0, arriveDb);
            var agc = new RadioAgc();
            double last = 1;
            double settledDb = 0;
            for (int i = 0; i + block <= music.Length; i += block)
            {
                last = agc.Update(music.AsSpan(i, block), blockSeconds);
                settledDb = Db(Rms(music) * last);
            }
            Console.WriteLine($"    {arriveDb,7:F1} dB   {settledDb,8:F1} dB   {agc.GainDb,+7:F1} dB");

            double reachable = Math.Clamp(RadioAgc.TargetDbfs - arriveDb,
                                          RadioAgc.MaxCutDb, RadioAgc.MaxBoostDb);
            double expect = arriveDb + reachable;
            if (Math.Abs(settledDb - expect) > 1.0)
                return $"a {arriveDb:F0} dBFS station settled at {settledDb:F1}, expected {expect:F1}";
        }

        // Inside its range everything lands on target.
        foreach (double arriveDb in new[] { -10.0, -14.0, -20.0 })
        {
            float[] music = SurrogateMusic(12.0, arriveDb);
            var agc = new RadioAgc();
            double g = 1;
            for (int i = 0; i + block <= music.Length; i += block)
                g = agc.Update(music.AsSpan(i, block), blockSeconds);
            double got = Db(Rms(music) * g);
            if (Math.Abs(got - RadioAgc.TargetDbfs) > 1.0)
                return $"a {arriveDb:F0} dBFS station did not reach target: {got:F1} dBFS";
        }

        // The transient, which is where this went wrong once and would again. A symmetric
        // 2.5 s AGC left a hot station 1.9 dB above target five seconds after tuning it,
        // and at full volume that put the music over the rotor - the one thing the budget
        // forbids. Measure the WORST block, not the settled value.
        foreach (double arriveDb in new[] { -3.0, -6.0, -10.0 })
        {
            float[] music = SurrogateMusic(8.0, arriveDb);
            var agc = new RadioAgc();
            double worstDb = -99;
            for (int i = 0; i + block <= music.Length; i += block)
            {
                double g = agc.Update(music.AsSpan(i, block), blockSeconds);
                worstDb = Math.Max(worstDb, Db(Rms(music) * g));
            }
            double atFullVolume = worstDb + Db(Radio.PlayerGain(1.0, 0.0));
            Console.WriteLine($"  tuning a {arriveDb:F0} dBFS station: worst block " +
                              $"{worstDb:F1} dBFS, {atFullVolume:F1} dBFS at full volume");
            // -23.1 dBFS is the measured rotor at the listener; the radio never beats it.
            if (atFullVolume > -23.1 + 1.0)
                return $"tuning a {arriveDb:F0} dBFS station puts the music at " +
                       $"{atFullVolume:F1} dBFS, over the rotor";
        }

        // Silence is not a quiet passage. An AGC that chases a dead stream turns a
        // reconnect into a bang.
        var quiet = new RadioAgc();
        var nothing = new float[block];
        double qg = 1;
        for (int i = 0; i < 400; i++) qg = quiet.Update(nothing, blockSeconds);
        Console.WriteLine($"  ten seconds of a dead stream: AGC {quiet.GainDb:F2} dB " +
                          $"(input {quiet.InputDbfs:F0} dBFS)");
        if (Db(qg) > 0.5) return $"the AGC boosted silence by {Db(qg):F1} dB";

        // Headroom, which is not paranoia. A live SomaFM stream decoded to a PEAK OF
        // 1.470 against an RMS of -13.9 dBFS: MP3 decoders overshoot full scale on a
        // brickwalled master as a matter of course. RadioStream clamps that to ±1, and the
        // gain chain then has to keep ±1 there - the AGC's boost limit and the reference
        // gain multiply, and nothing downstream is checking.
        double loudestChain = Math.Pow(10, RadioAgc.MaxBoostDb / 20.0) * Radio.PlayerGain(1.0, 0.0);
        Console.WriteLine($"  worst-case chain gain: AGC +{RadioAgc.MaxBoostDb:F0} dB x " +
                          $"reference {Radio.ReferenceGainDb:F0} dB = {loudestChain:F3} " +
                          $"({Db(loudestChain):F1} dB)");
        if (loudestChain > 1.0)
            return $"a full-scale sample through the loudest chain reaches {loudestChain:F2} - clipping";

        // And it must not breathe. A real record has quiet passages; an AGC that tracks
        // them is the sound of cheap compression rather than of a receiver.
        float[] dynamic = SurrogateMusic(20.0, -14.0);
        for (int i = 0; i < dynamic.Length; i++)
        {
            double t = i / (double)Rate;
            // 12 dB down for four seconds in the middle, like a breakdown.
            if (t > 8 && t < 12) dynamic[i] *= 0.25f;
        }
        var steady = new RadioAgc();
        double min = 99, max = -99;
        for (int i = 0; i + block <= dynamic.Length; i += block)
        {
            double g = steady.Update(dynamic.AsSpan(i, block), blockSeconds);
            double t = i / (double)Rate;
            if (t > 2) { min = Math.Min(min, Db(g)); max = Math.Max(max, Db(g)); }
        }
        Console.WriteLine($"  through a 12 dB breakdown: AGC moved {max - min:F1} dB " +
                          $"({min:F1} to {max:F1})");
        if (max - min > 6.0)
            return $"the AGC breathes {max - min:F1} dB through a quiet passage";

        return null;
    }

    // ============================================================= 5. the mix

    /// <summary>
    /// The one the audio benchmark asked for: does the horn still cut through.
    ///
    /// Three voices are rendered at the gains the game actually plays them at - the rotor
    /// through its 3D player, the horn through the warning panel's measured 0.68, and the
    /// surrogate stream through <see cref="RadioAgc"/> and <see cref="Radio.PlayerGain"/>
    /// with the volume knob at maximum, which is the worst case the mix has to survive.
    /// Then the horn is measured against everything else, broadband and in its own
    /// critical band, with the ducker off and on.
    /// </summary>
    public static string? Ducking()
    {
        const double seconds = 2.0;
        float[] music = SurrogateMusic(seconds);
        float[] horn = Horn(seconds, 0.68f);
        float[] rotor = Rotor(seconds, 0.75f);

        double musicFile = Rms(music);
        Console.WriteLine($"  source levels (RMS): stream after AGC {Db(musicFile):F1} dBFS, " +
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
        Console.WriteLine($"  duck settles at {Db(duck):F2} dB (target {RadioMix.WarningDuckDb:F1})");
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
        // and the measurement is the reason to believe that rather than assume it: rotor
        // and music contribute almost exactly equally in the horn's critical band, so the
        // radio costs the horn about 3 dB there and the duck gives all of it back. The
        // broadband number above is where the real risk was.
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

        // --- Worst case of all: the hottest station physics allows, knob wide open ---
        //
        // Everything above assumes the stream arrives at the measured -14 dBFS. The URL in
        // the station list points at somebody else's transmitter, so the guarantee has to
        // hold at the other end of what is possible too. It is bounded: a digital signal
        // cannot have an RMS above 0 dBFS, and a brickwalled modern master tops out near
        // -3, which the AGC's -12 dB cut brings to -15. So the worst realistic case is
        // actually QUIETER than nominal, and the margin only improves.
        const double HottestMasterDbfs = -3.0;
        float[] hot = SurrogateMusic(6.0, HottestMasterDbfs);
        var hotAgc = new RadioAgc();
        const int blk = 1024;
        double hotGain = 1;
        for (int i = 0; i + blk <= hot.Length; i += blk)
            hotGain = hotAgc.Update(hot.AsSpan(i, blk), blk / (double)Rate);

        double hotAfter = Db(Rms(hot) * hotGain);
        double hotOpen = Db(Rms(hot) * hotGain * gainOpen);
        double hotDucked = hotOpen + Db(duck);
        Console.WriteLine($"  hottest master worth worrying about ({HottestMasterDbfs:F0} dBFS): " +
                          $"AGC {hotAgc.GainDb:F1} dB -> {hotAfter:F1} dBFS, " +
                          $"open {hotOpen:F1}, ducked {hotDucked:F1} dBFS");
        if (hotOpen > rotorDb + 1.0)
            return $"the hottest station beats the rotor at full volume: " +
                   $"{hotOpen:F1} vs {rotorDb:F1} dBFS";
        if (hornDb - hotDucked < 12.0)
            return $"the hottest station leaves the horn only {hornDb - hotDucked:F1} dB clear";

        return null;
    }

    // ========================================================= 6. as equipment

    /// <summary>
    /// The radio is a refit module and the set is salvage. Neither is a special case, and
    /// this checks that by going through the systems that already exist.
    /// </summary>
    public static string? AsEquipment()
    {
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

        // Adding it must not have moved any other module's drag budget.
        foreach (ModuleDef m in Loadout.All)
            if (m.Id is not "suppressor" and not "gunpod"
                && (Math.Abs(m.DragDelta.X) + Math.Abs(m.DragDelta.Y)
                   + Math.Abs(m.DragDelta.Z)) > 0.001)
                return $"module '{m.Id}' has an unexpected drag delta";
        if (Loadout.All.Count != Loadout.Catalog.Count) return "catalogue and list disagree";

        // Sets are found by searching, deterministically, often enough to matter without
        // being everywhere - and never at a site that would not have one.
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

        for (int id = 0; id < 50; id++)
            if (Radio.SetAtSite(id, SalvageSiteKind.Wreck) != Radio.SetAtSite(id, SalvageSiteKind.Wreck))
                return "site rolls are not deterministic";

        // Adding the radio must not have disturbed the existing module distribution: that
        // pool is indexed by rng.Next(pool.Length), so a string added to it would silently
        // change which module every site in every existing save contains. The radio gets
        // its own roll on its own seed precisely so this stays true.
        var before = new Dictionary<int, string?>();
        for (int id = 0; id < 200; id++) before[id] = Loadout.ModuleAtSite(id);
        int changed = 0;
        foreach (KeyValuePair<int, string?> kv in before)
            if (kv.Value == Radio.ModuleId) changed++;
        Console.WriteLine($"  the radio appears in Loadout.ModuleAtSite {changed} times (must be 0)");
        if (changed != 0) return "the radio is in the shared module pool; it moves every other find";

        // The knowledge id a logged frequency writes is the one the band reads.
        if (Radio.FrequencyKnowledgeId(47) != "freq.47")
            return $"frequency id is {Radio.FrequencyKnowledgeId(47)}, not what SiteInteraction writes";

        // The set remembers what the player left it doing, across a save.
        var was = new RadioSet();
        was.SetPower(true);
        was.SetVolume(0.42);
        was.Tune(2);
        var now = new RadioSet();
        now.Restore(was.PowerOn, was.Volume, was.StationIndex);
        if (!now.PowerOn || Math.Abs(now.Volume - 0.42) > 1e-12 || now.StationIndex != 2)
            return "the set did not come back from a save the way it was left";
        Console.WriteLine($"  restored: power {now.PowerOn}, volume {now.Volume:F2}, " +
                          $"station {now.StationIndex}");

        return null;
    }
}
