namespace Rotorwash.Sim;

/// <summary>Why the set is not making a noise. Facts, not advice (D-005a).</summary>
public enum RadioSilence
{
    /// <summary>It is playing.</summary>
    None,
    /// <summary>No set is fitted to the aircraft.</summary>
    NotFitted,
    /// <summary>The player switched it off. The only reason on this list that is theirs.</summary>
    SwitchedOff,
    /// <summary>Avionics health is below the floor. It needs a part, not a knob.</summary>
    Unserviceable,
    /// <summary>Nothing tuned - no mast has been logged, or the band is empty.</summary>
    NoStation,
    /// <summary>Out of range, or the weather is sitting on the band.</summary>
    NoSignal,
    /// <summary>The set is shaking or sick and has lost it for a moment.</summary>
    Dropout,
    /// <summary>Carrier is there; the transmitter is not. Warming up.</summary>
    Connecting,
    /// <summary>The station has stopped transmitting. Nothing the pilot can do.</summary>
    OffAir,
}

/// <summary>What the transmitter end is doing, as far as the set can tell.</summary>
public enum RadioLink
{
    /// <summary>Not asked to receive anything.</summary>
    Idle,
    /// <summary>Carrier found, audio not flowing yet.</summary>
    Connecting,
    /// <summary>Audio is arriving.</summary>
    Live,
    /// <summary>It was transmitting and it is not any more, or it never was.</summary>
    Failed,
}

/// <summary>
/// One broadcaster, as configured. Where it transmits FROM is decided by the world.
/// </summary>
/// <param name="Url">What the game layer actually connects to.</param>
/// <param name="GainDb">
/// Manual level trim. Usually unnecessary - <see cref="RadioAgc"/> levels stations
/// automatically - but it is here for a station that is so far out that the AGC's limits
/// cannot reach it.
/// </param>
public readonly record struct RadioStreamDef(string Name, string Url, double GainDb);

/// <summary>
/// A broadcaster, transmitting from somewhere specific in the world.
///
/// The join between a URL in a config file and D-010's map: a station is not ambient, it
/// is a mast on a hill, and whether you can hear it is a question about where you are.
/// </summary>
/// <param name="X">World north, metres.</param>
/// <param name="Y">World east, metres.</param>
/// <param name="MastHeightM">Height of the antenna above its own ground.</param>
/// <param name="PowerKm">
/// Range at which the field reaches unity for a receiver at
/// <see cref="Radio.FullRangeAglM"/>. The one free parameter in <see cref="Radio.Signal"/>;
/// everything else in the reception model is geometry.
/// </param>
public readonly record struct RadioStation(
    string Id, string Name, string SiteName, string Url,
    double X, double Y, double MastHeightM, double PowerKm, double GainDb)
{
    /// <summary>
    /// A station with no stream behind it: a transmitter in the world whose audio comes
    /// from somewhere other than the internet.
    ///
    /// Kept as a distinct shape because it is a real case rather than a compatibility
    /// shim - <c>RadioDj</c> builds one for the Upland Service, which is a man with a
    /// microphone rather than an Icecast server. The reception model does not care where
    /// the audio comes from; only <see cref="CockpitRadio"/> does, and an empty
    /// <see cref="Url"/> is how it is told not to open a socket.
    /// </summary>
    public RadioStation(string id, string name, double x, double y,
                        double mastHeightM, double powerKm)
        : this(id, name, "", "", x, y, mastHeightM, powerKm, 0) { }

    /// <summary>For the panel: who is transmitting, and from where.</summary>
    public string Display => SiteName.Length > 0 ? $"{SiteName} — {Name}" : Name;
}

/// <summary>
/// Everything the set needs to know about the world this frame.
/// </summary>
/// <param name="Installed">A radio is bolted in (the <c>radio</c> refit module).</param>
/// <param name="AvionicsHealth">From <see cref="DamageState.Health"/> of <see cref="Component.Avionics"/>.</param>
/// <param name="VibrationIps">From <see cref="DamageState.VibrationIps"/>.</param>
/// <param name="SignalQuality">0..1 from <see cref="Radio.Quality"/> for the tuned station.</param>
/// <param name="Link">What the network end is doing.</param>
public readonly record struct RadioConditions(
    bool Installed,
    double AvionicsHealth,
    double VibrationIps,
    double SignalQuality,
    RadioLink Link);

/// <summary>
/// The radio, as a machine rather than as a music player.
///
/// <b>The design rule, and it is the important one.</b> A radio that fights the player is
/// not characterful, it is a nuisance. So:
/// <list type="bullet">
/// <item>The set never switches itself off. <see cref="PowerOn"/> and <see cref="Volume"/>
/// change when the player changes them and at no other time - not on a dropout, not on a
/// hit, not when the network dies, not on a load. Whatever the world does, when it stops
/// doing it the music comes back at the level you set. <c>RadioTests.VolumeStaysSet</c>
/// asserts exactly that against a sortie's worth of abuse.</item>
/// <item>A serviceable aircraft in ordinary flight never drops out at all. Dropouts need
/// vibration past <see cref="DamageState.VibrationCautionIps"/> - the threshold the
/// aircraft already lights a caution at - or an avionics stack below
/// <see cref="HealthFullyReliable"/>. Both mean something is already wrong, and both are
/// fixable with a part.</item>
/// <item>Every silence has a reason the panel can name, and every reason clears by itself
/// when its cause does. There is no state the player has to get the set out of.</item>
/// </list>
///
/// <b>Three independent ways to lose the music</b>, and they are deliberately kept apart,
/// because a pilot who cannot tell "I have flown out of range" from "the set is broken"
/// from "the station has gone off air" cannot do anything about any of them:
/// <list type="bullet">
/// <item><see cref="RadioSilence.NoSignal"/> — geometry. Climb, or fly back.</item>
/// <item><see cref="RadioSilence.Dropout"/> — the aircraft. Fix the vibration or the
/// avionics.</item>
/// <item><see cref="RadioSilence.OffAir"/> — the transmitter. Try another frequency.</item>
/// </list>
/// </summary>
public sealed class RadioSet
{
    // --- What the player set. Nothing in this file writes these except the setters. ---

    /// <summary>Is the set switched on. Player-owned.</summary>
    public bool PowerOn { get; private set; }

    /// <summary>Volume, 0..1. Player-owned. The ducker never touches it.</summary>
    public double Volume { get; private set; } = 0.55;

    /// <summary>Which station, by index into the tuned list. -1 when none.</summary>
    public int StationIndex { get; private set; } = -1;

    // --- What the machine is doing. ---

    /// <summary>True when sound should be coming out right now.</summary>
    public bool Playing => Why == RadioSilence.None;

    /// <summary>Why not, when not.</summary>
    public RadioSilence Why { get; private set; } = RadioSilence.SwitchedOff;

    /// <summary>
    /// 0..1. Below 1 the station is there but grainy: the level is trimmed back and the
    /// panel says so, which is what a weak FM signal actually does.
    /// </summary>
    public double Fidelity { get; private set; } = 1.0;

    /// <summary>Seconds left of the current dropout. Zero when the set is happy.</summary>
    public double DropoutRemaining { get; private set; }

    /// <summary>Counted for the panel and the tests. Facts, not verdicts.</summary>
    public int Dropouts { get; private set; }

    /// <summary>True on the frame the tuned station changed, so the game can reconnect.</summary>
    public bool StationChanged { get; private set; }

    /// <summary>
    /// True when the game layer should be holding a connection open.
    ///
    /// Not the same as <see cref="Playing"/>: the set wants the carrier up while it is
    /// switched on and in range, including through a dropout, because dropping the socket
    /// every time the airframe shakes would mean four seconds of re-buffering for every
    /// half second of vibration.
    /// </summary>
    public bool WantsStream { get; private set; }

    private readonly Random _rng;

    public RadioSet(int seed = 55123) => _rng = new Random(seed);

    // ------------------------------------------------------------ player input

    /// <summary>The switch. The only thing that turns the set off is this.</summary>
    public void SetPower(bool on) => PowerOn = on;

    public void TogglePower() => PowerOn = !PowerOn;

    /// <summary>The volume knob. Clamped, and never written by anything but this.</summary>
    public void SetVolume(double v) => Volume = Math.Clamp(v, 0, 1);

    public void NudgeVolume(double delta) => SetVolume(Volume + delta);

    /// <summary>Tune a station by index. -1 is off the band.</summary>
    public void Tune(int index)
    {
        if (index == StationIndex) return;
        StationIndex = index;
        StationChanged = true;
        DropoutRemaining = 0;
    }

    /// <summary>Next station along the band, wrapping.</summary>
    public void Next(int stationCount)
    {
        if (stationCount <= 0) { Tune(-1); return; }
        Tune((StationIndex + 1) % stationCount);
    }

    /// <summary>Previous station along the band, wrapping.</summary>
    public void Previous(int stationCount)
    {
        if (stationCount <= 0) { Tune(-1); return; }
        Tune((StationIndex - 1 + stationCount) % stationCount);
    }

    // ----------------------------------------------------------------- machine

    /// <summary>Advance the set by <paramref name="dt"/> seconds.</summary>
    public void Update(double dt, int stationCount, in RadioConditions c)
    {
        bool changed = StationChanged;
        StationChanged = false;

        WantsStream = false;
        if (!c.Installed) { Stop(RadioSilence.NotFitted); return; }
        if (!PowerOn) { Stop(RadioSilence.SwitchedOff); return; }
        if (c.AvionicsHealth <= DamageState.AvionicsFloor) { Stop(RadioSilence.Unserviceable); return; }

        if (StationIndex < 0 || StationIndex >= stationCount) { Stop(RadioSilence.NoStation); return; }

        double q = Math.Clamp(c.SignalQuality, 0, 1);
        Fidelity = q;
        if (q <= 0)
        {
            // No point holding a socket open for a station the geometry says is not
            // reaching us - and letting go here is also what makes flying back over the
            // ridge sound like tuning in again rather than like nothing happened.
            Why = RadioSilence.NoSignal;
            DropoutRemaining = 0;
            return;
        }

        // In range: keep the carrier up even through a dropout.
        WantsStream = true;

        if (c.Link == RadioLink.Failed) { Why = RadioSilence.OffAir; DropoutRemaining = 0; return; }
        if (c.Link != RadioLink.Live) { Why = RadioSilence.Connecting; return; }

        if (DropoutRemaining > 0)
        {
            DropoutRemaining -= dt;
            if (DropoutRemaining > 0) { Why = RadioSilence.Dropout; return; }
            DropoutRemaining = 0;
        }

        // A station change does not get a dropout rolled against it on its first frame;
        // that is the tuning transient, and it is already covered by Connecting.
        if (!changed)
        {
            double rate = DropoutRatePerSecond(c.VibrationIps, c.AvionicsHealth);
            if (rate > 0 && _rng.NextDouble() < 1.0 - Math.Exp(-rate * dt))
            {
                DropoutRemaining = DropoutMinSeconds
                                 + _rng.NextDouble() * (DropoutMaxSeconds - DropoutMinSeconds);
                Dropouts++;
                Why = RadioSilence.Dropout;
                return;
            }
        }

        Why = RadioSilence.None;
    }

    private void Stop(RadioSilence why)
    {
        Why = why;
        DropoutRemaining = 0;
        Fidelity = 0;
        WantsStream = false;
    }

    // ------------------------------------------------------------- the dropout

    /// <summary>
    /// Avionics health at and above which the set is perfectly reliable. Between here and
    /// <see cref="DamageState.AvionicsFloor"/> it is a box with a loose connection.
    /// </summary>
    public const double HealthFullyReliable = 0.70;

    /// <summary>Dropouts per second at twice the vibration caution threshold.</summary>
    public const double DropoutRateAtDoubleVibration = 0.35;

    /// <summary>Dropouts per second with the avionics stack right on its floor.</summary>
    public const double DropoutRateAtFloorHealth = 0.20;

    public const double DropoutMinSeconds = 0.35;
    public const double DropoutMaxSeconds = 1.10;

    /// <summary>
    /// How often the set loses it, per second.
    ///
    /// Zero - exactly zero, not merely small - for an aircraft inside its vibration
    /// caution with a serviceable avionics stack. That is the promise in the class comment
    /// expressed as a number, and <c>RadioTests.KeepsPlaying</c> flies ten minutes against
    /// it.
    /// </summary>
    public static double DropoutRatePerSecond(double vibrationIps, double avionicsHealth)
    {
        double shake = Math.Max(0, vibrationIps - DamageState.VibrationCautionIps)
                     / DamageState.VibrationCautionIps;
        double sick = Math.Clamp(
            (HealthFullyReliable - avionicsHealth) / (HealthFullyReliable - DamageState.AvionicsFloor),
            0, 1);
        return shake * DropoutRateAtDoubleVibration + sick * DropoutRateAtFloorHealth;
    }

    // ----------------------------------------------------------- save / restore

    /// <summary>Put the set back exactly as the player left it. Save/load only.</summary>
    public void Restore(bool power, double volume, int stationIndex)
    {
        PowerOn = power;
        Volume = Math.Clamp(volume, 0, 1);
        StationIndex = stationIndex;
        StationChanged = true;
    }
}

/// <summary>
/// Automatic gain control, which is what a receiver has and why every station on a real
/// dial is about the same loudness.
///
/// It is here for a mechanical reason as much as a characterful one. The mix budget in
/// <see cref="RadioMix"/> guarantees the low-rotor horn a margin over the music, and that
/// guarantee is only worth anything if the music's level is known. It is not: a measured
/// SomaFM stream arrives at -14.0 dBFS RMS, another station will arrive at -9, and a
/// third at -20, and the player can type any URL they like into the station list. Without
/// AGC the promise "the horn is 22 dB above the music" silently becomes "the horn is 22 dB
/// above the one station this was measured against".
///
/// So the level is measured rather than assumed: a slow RMS follower drives a gain that
/// pulls whatever arrives towards <see cref="TargetDbfs"/>, bounded so it cannot boost a
/// silent stream into a roar of noise. <see cref="ReleaseSeconds"/> is long on purpose - a
/// fast AGC breathes on every quiet passage, which is the sound of cheap compression
/// rather than of a receiver - while <see cref="AttackSeconds"/> is short, because being
/// slow to turn something DOWN is the failure that matters here.
/// </summary>
public sealed class RadioAgc
{
    /// <summary>Where every station ends up, dBFS RMS. The measured level of a real one.</summary>
    public const double TargetDbfs = -14.0;

    /// <summary>The most it will lift a quiet station, dB.</summary>
    public const double MaxBoostDb = 9.0;

    /// <summary>The most it will hold back a loud one, dB.</summary>
    public const double MaxCutDb = -12.0;

    /// <summary>
    /// Time constant for LETTING A STATION GET LOUDER: the level falling, and the gain
    /// coming up. Slow, so a quiet passage in a record is not chased.
    /// </summary>
    public const double ReleaseSeconds = 2.5;

    /// <summary>
    /// Time constant for HOLDING A STATION BACK: the level rising, and the gain coming
    /// down. Much faster, and asymmetric on purpose.
    ///
    /// A symmetric 2.5 s AGC was measured leaving a hot station 1.9 dB above target five
    /// seconds after tuning it, which at full volume put the music 1.0 dB OVER the rotor -
    /// the one thing the mix budget says must never happen. Every limiter and every
    /// receiver AGC ever built is asymmetric for exactly this reason: being slow to turn
    /// something down is the failure that matters, and being slow to turn it up is the one
    /// that sounds good.
    /// </summary>
    public const double AttackSeconds = 0.50;

    /// <summary>Below this the input is silence, not a quiet passage. Do not chase it.</summary>
    public const double FloorDbfs = -60.0;

    /// <summary>Current correction, linear.</summary>
    public double Gain { get; private set; } = 1.0;

    public double GainDb => 20.0 * Math.Log10(Math.Max(Gain, 1e-6));

    /// <summary>Measured input level, dBFS. For the transcripts.</summary>
    public double InputDbfs { get; private set; } = TargetDbfs;

    private double _meanSquare = -1;

    public void Reset() { Gain = 1.0; _meanSquare = -1; InputDbfs = TargetDbfs; }

    private static double Coefficient(double seconds, double blockSeconds)
        => 1.0 - Math.Exp(-blockSeconds / seconds);

    /// <summary>
    /// Feed the AGC a block of raw decoded samples and get back the gain to apply to it.
    ///
    /// Deliberately measures the block BEFORE the gain, so the loop is a measurement
    /// feeding a correction rather than a feedback path that can run away.
    /// </summary>
    public double Update(ReadOnlySpan<float> block, double blockSeconds)
    {
        if (block.Length == 0) return Gain;

        double sum = 0;
        for (int i = 0; i < block.Length; i++) sum += (double)block[i] * block[i];
        double ms = sum / block.Length;

        bool first = _meanSquare < 0;
        if (first) _meanSquare = ms;
        else _meanSquare += (ms - _meanSquare)
                          * Coefficient(ms > _meanSquare ? AttackSeconds : ReleaseSeconds,
                                        blockSeconds);

        double rms = Math.Sqrt(Math.Max(_meanSquare, 1e-12));
        InputDbfs = 20.0 * Math.Log10(Math.Max(rms, 1e-6));

        // A stream that has gone silent is not a stream that needs 9 dB of help.
        double wantDb = InputDbfs <= FloorDbfs
            ? 0.0
            : Math.Clamp(TargetDbfs - InputDbfs, MaxCutDb, MaxBoostDb);

        double want = Math.Pow(10.0, wantDb / 20.0);

        // The first block of a newly tuned station is set outright rather than ramped to.
        // There is nothing to click against - no audio has been heard yet - and ramping
        // instead means the first second or two of a loud station arrives at the wrong
        // level, which is precisely the moment the player is listening hardest.
        if (first) Gain = want;
        else Gain += (want - Gain)
                   * Coefficient(want < Gain ? AttackSeconds : ReleaseSeconds, blockSeconds);
        return Gain;
    }
}

/// <summary>
/// The one thing standing between the music and the low-rotor horn.
///
/// The audio benchmark's fifth gap is "no mix discipline - three synths and a warning
/// system all write into the master bus at fixed gains, and nothing ducks anything". A
/// fourth voice that is louder than all of them, comes off the open internet, and is
/// *chosen by the player* makes that gap a safety problem rather than an aesthetic one, so
/// the ducker ships with the radio rather than after it.
///
/// <b>Why there is a hold.</b> The master warning is pulsed at
/// <see cref="WarningSynth.WarningRepHz"/> = 3.3 Hz with a 45% duty, so it is silent for
/// 167 ms out of every 303. A ducker with a normal release lets the music swell back in
/// every one of those gaps: 3.3 Hz pumping, which is both audible and exactly the rate the
/// ear is most sensitive to as a rhythm. <see cref="HoldSeconds"/> is longer than that gap
/// on purpose, so the duck rides straight through the beeps and releases once, when the
/// warning is actually over.
///
/// <b>Why the numbers are what they are.</b> Measured, at 44.1 kHz:
/// <code>
///   rotor at the listener   -23.1 dBFS   (synth -23.6, player 0.75, Godot's +3 max_db clamp)
///   low-rotor horn          -16.7 dBFS   (synth -13.3, player gain 0.68)
///   a live Icecast stream   -14.0 dBFS   (SomaFM, 128 kbps MP3, 25 s decoded)
/// </code>
/// <see cref="RadioAgc"/> holds every station at that -14, and
/// <see cref="Radio.ReferenceGainDb"/> then puts it at -24 dBFS with the volume knob wide
/// open: just under the rotor, which is the loudest the radio is ever allowed to be. From
/// there the horn is only 7.3 dB up, and 7 dB is not a margin, it is a preference.
/// <see cref="WarningDuckDb"/> of -15 puts the music at -39 dBFS, a measured 22 dB below
/// the horn and 16 dB below the rotor: still there, in no danger of being mistaken for the
/// foreground. <c>RadioTests.Ducking</c> renders all three and measures it.
/// </summary>
public sealed class RadioMix
{
    /// <summary>How far the music drops for a warning-severity tone or the horn, dB.</summary>
    public const double WarningDuckDb = -15.0;

    /// <summary>How far it drops for a caution chime, dB. Shallower: the chime fires once.</summary>
    public const double CautionDuckDb = -9.0;

    /// <summary>How far it drops while somebody is talking to you, dB.</summary>
    public const double DialogueDuckDb = -12.0;

    /// <summary>Down fast. Slower than this and the first beep is over before the duck is.</summary>
    public const double AttackSeconds = 0.060;

    /// <summary>Back up slowly, so the return is not itself an event.</summary>
    public const double ReleaseSeconds = 0.450;

    /// <summary>
    /// Minimum time at the floor before release begins. Must exceed the 167 ms gap in a
    /// 3.3 Hz / 45% master warning, or the duck pumps at the beep rate.
    /// </summary>
    public const double HoldSeconds = 0.300;

    /// <summary>How long a caution chime is considered to be sounding, seconds.</summary>
    public const double CautionChimeSeconds = 1.10;

    /// <summary>Current duck, linear, 0..1. Multiplies the player's volume; never replaces it.</summary>
    public double Gain { get; private set; } = 1.0;

    /// <summary>Current duck in dB, for the transcripts.</summary>
    public double GainDb => 20.0 * Math.Log10(Math.Max(Gain, 1e-6));

    private double _hold;
    private double _chime;

    /// <summary>A caution chime just fired. Same trigger discipline as <see cref="WarningSynth.TriggerCaution"/>.</summary>
    public void TriggerCaution() => _chime = CautionChimeSeconds;

    public void Reset() { Gain = 1.0; _hold = 0; _chime = 0; }

    /// <summary>
    /// Advance the ducker.
    /// </summary>
    /// <param name="warning">The horn or the master warning tone is sounding.</param>
    /// <param name="dialogue">A conversation is open.</param>
    public void Update(double dt, bool warning, bool dialogue)
    {
        if (_chime > 0) _chime = Math.Max(0, _chime - dt);

        double targetDb = 0;
        if (warning) targetDb = Math.Min(targetDb, WarningDuckDb);
        if (_chime > 0) targetDb = Math.Min(targetDb, CautionDuckDb);
        if (dialogue) targetDb = Math.Min(targetDb, DialogueDuckDb);

        double target = Math.Pow(10.0, targetDb / 20.0);

        // The hold is armed by the SIDECHAIN, not by the gain movement. Arming it only
        // while the gain was still travelling downwards was the bug the pumping test
        // caught: once the duck had settled on the floor the hold expired, the next 167 ms
        // gap between beeps released 7.3 dB of it, and the music pumped at exactly the
        // master warning's 3.3 Hz. It measured as a fault in the tone, not in the radio,
        // which is how it would have survived a listen.
        if (targetDb < 0) _hold = HoldSeconds;

        if (target < Gain - 1e-9)
        {
            Gain += (target - Gain) * (1.0 - Math.Exp(-dt / AttackSeconds));
        }
        else
        {
            if (_hold > 0) { _hold = Math.Max(0, _hold - dt); return; }
            Gain += (target - Gain) * (1.0 - Math.Exp(-dt / ReleaseSeconds));
        }

        Gain = Math.Clamp(Gain, 0, 1);
    }
}

/// <summary>
/// The static half of the radio: the station list format, the reception model, and where
/// a set turns up in the world.
///
/// Diegetic music, per D-058, and per Fred's follow-up the music is not a file in the
/// repository - it is a station somebody is still broadcasting. That is a better object
/// for this world than a cassette, and it means there is no music to licence, ship or lose.
///
/// <b>It hangs off systems that already existed.</b> The set is a refit module like any
/// other (D-011): absent, found, fitted, shot, repaired with an avionics part. The
/// stations are the relay masts the player already tunes - the "Tune the mast" action has
/// been writing <c>freq.&lt;siteId&gt;</c> into the knowledge log, with the flavour text
/// "Carrier present. Nobody answering, but it is on.", since long before there was
/// anything to hear on it. Nothing in <c>SiteInteraction</c> had to change for a logged
/// frequency to become a station.
///
/// <b>Two things to know, written here rather than solved.</b>
/// <list type="number">
/// <item><b>Retransmitting somebody else's stream is fine for a private prototype and is
/// not shippable.</b> A build that plays a third-party Icecast station is redistributing
/// their broadcast. For Fred, alone, on his own machine, that is nothing. The moment it
/// goes to anybody else it is a licensing question with a real answer, and the answer is
/// not "we did not think about it". See <c>docs/wiki/attribution.md</c>.</item>
/// <item><b>Real stations carry adverts and DJ chatter</b>, and they will arrive at the
/// worst possible moment. Nothing here engineers around that. Picking listener-supported,
/// advert-free stations in the station list is the whole mitigation.</item>
/// </list>
/// </summary>
public static class Radio
{
    /// <summary>
    /// How long a track is assumed to last when the source does not say.
    ///
    /// A live stream has no track boundaries to read - that is the whole difference between
    /// streaming a station and playing a file - so anything that needs to know when one
    /// song ends and the next begins has to assume. Three and a half minutes is the middle
    /// of the range for the kind of music this is, and it exists so the announcer has
    /// somewhere plausible to speak: between tracks, not over them.
    /// </summary>
    public const double AssumedTrackSeconds = 210.0;

    /// <summary>The refit module id. Must match the entry in <see cref="Loadout.All"/>.</summary>
    public const string ModuleId = "radio";

    /// <summary>
    /// Player gain applied before the player's own volume, dB.
    ///
    /// Sized against <see cref="RadioAgc.TargetDbfs"/>, so it is a statement about the
    /// mix rather than about any one station: AGC delivers -14 dBFS, this puts it at -24
    /// dBFS with the knob wide open, and the rotor at the listener measures -23.1 dBFS.
    /// The radio is never allowed to be louder than the aircraft, because the aircraft is
    /// the instrument the pilot flies by ear.
    /// </summary>
    public const double ReferenceGainDb = -10.0;

    public static double ReferenceGain => Math.Pow(10.0, ReferenceGainDb / 20.0);

    /// <summary>
    /// Final linear gain for the music, before <see cref="RadioMix.Gain"/> and after
    /// <see cref="RadioAgc.Gain"/>.
    /// </summary>
    public static double PlayerGain(double volume, double stationGainDb, double fidelity = 1.0)
        => ReferenceGain
           * Math.Clamp(volume, 0, 1)
           * Math.Pow(10.0, Math.Clamp(stationGainDb, -24, 12) / 20.0)
           // A weak station is quieter as well as grainier; the limiter in a real receiver
           // gives up before the signal does.
           * (0.55 + 0.45 * Math.Clamp(fidelity, 0, 1));

    // -------------------------------------------------------- the station list

    /// <summary>
    /// Parse the station list.
    ///
    /// One station per line, fields separated by <c>|</c>, blank lines and <c>#</c>
    /// comments ignored:
    /// <code>
    ///   name | url | gain_db
    /// </code>
    /// A pipe-delimited table rather than JSON because a person edits it by hand to change
    /// what the radio plays, and a missing brace is a worse failure than a missing column.
    /// Only the URL is required; a station with no name is named after its host.
    /// </summary>
    public static List<RadioStreamDef> ParseStations(string? text)
    {
        var list = new List<RadioStreamDef>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] f = line.Split('|');
            string Field(int i) => i < f.Length ? f[i].Trim() : "";

            string name = Field(0), url = Field(1);
            // Tolerate a bare URL on a line by itself, which is what somebody pasting one
            // in will produce.
            if (url.Length == 0 && name.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            { url = name; name = ""; }
            if (url.Length == 0) continue;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;

            double.TryParse(Field(2), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double gain);

            if (name.Length == 0) name = HostOf(url);
            list.Add(new RadioStreamDef(name, url, gain));
        }
        return list;
    }

    /// <summary>Host part of a URL, for naming a station somebody only pasted a link for.</summary>
    public static string HostOf(string url)
    {
        int start = url.IndexOf("//", StringComparison.Ordinal);
        if (start < 0) return url;
        start += 2;
        int end = url.IndexOf('/', start);
        return end < 0 ? url[start..] : url[start..end];
    }

    /// <summary>
    /// Put a broadcaster on a mast.
    ///
    /// Deterministic by site id, so the same world always has the same band: tune the same
    /// hill twice and the same station is on it. Mast height and power vary because a
    /// hilltop repeater and a town transmitter are not the same thing, and that variation
    /// is what makes one logged frequency worth more than another.
    /// </summary>
    public static RadioStation BindStation(int siteId, string siteName, double x, double y,
                                           IReadOnlyList<RadioStreamDef> broadcasters)
    {
        var rng = new Random(siteId * 21001 + 4133);
        double mast = 18 + rng.NextDouble() * 52;          // 18-70 m
        double power = 4.5 + rng.NextDouble() * 9.5;       // 4.5-14 km nominal

        RadioStreamDef def = broadcasters.Count > 0
            ? broadcasters[rng.Next(broadcasters.Count)]
            : new RadioStreamDef("dead air", "", 0);

        return new RadioStation($"freq.{siteId}", def.Name, siteName, def.Url,
                                x, y, mast, power, def.GainDb);
    }

    // ------------------------------------------------------------- reception

    /// <summary>Field strength at or above which the station is clean.</summary>
    public const double CleanField = 1.30;

    /// <summary>Field strength below which there is nothing there but noise.</summary>
    public const double UsableField = 0.55;

    /// <summary>Altitude at which a receiver gets the station's full quoted range, m AGL.</summary>
    public const double FullRangeAglM = 300.0;

    /// <summary>
    /// Relative field strength at the aircraft. 1.0 is the station's nominal edge.
    ///
    /// Two terms, and both are real rather than tuned:
    ///
    /// <b>Height.</b> VHF is line of sight, so range goes with the square root of the
    /// receiver's height. On the deck a set reaches 45% of its nominal range; at 300 m it
    /// reaches all of it; higher still it keeps improving to a clamp. That single fact is
    /// the interesting one, because D-010 spends the whole game teaching the player to fly
    /// low to stay out of threat envelopes - and flying low is exactly what loses the
    /// music. The player decides which they want, every sortie, and neither answer is
    /// wrong.
    ///
    /// <b>Distance.</b> Inverse, as field strength is (power is inverse square; this is
    /// amplitude).
    ///
    /// Weather takes its cut on top: rain absorbs, and a storm puts static across the
    /// band, which is the other thing that takes the music away.
    /// </summary>
    public static double Signal(double distanceM, double receiverAglM, in RadioStation station,
                                double precipitation, double stormIntensity)
    {
        double reachKm = station.PowerKm
                       * (0.45 + 0.55 * Math.Clamp(Math.Sqrt(Math.Max(receiverAglM, 0) / FullRangeAglM), 0, 1.6));
        double dKm = Math.Max(distanceM / 1000.0, 0.35);
        double field = reachKm / dKm;

        double wx = (1.0 - 0.30 * Math.Clamp(precipitation, 0, 1))
                  * (1.0 - 0.45 * Math.Clamp(stormIntensity, 0, 1));
        return Math.Max(0, field * wx);
    }

    /// <summary>Field strength as a 0..1 quality: 1 clean, 0 unusable, a real band between.</summary>
    public static double Quality(double field)
        => Math.Clamp((field - UsableField) / (CleanField - UsableField), 0, 1);

    /// <summary>Convenience: signal straight to quality.</summary>
    public static double Quality(double distanceM, double receiverAglM, in RadioStation station,
                                 double precipitation, double stormIntensity)
        => Quality(Signal(distanceM, receiverAglM, station, precipitation, stormIntensity));

    // ------------------------------------------------------------ what is found

    /// <summary>
    /// Whether a radio set is lying in this site, waiting to be searched out.
    ///
    /// Its own roll on its own seed rather than an entry in <see cref="Loadout.ModuleAtSite"/>,
    /// which matters for a boring reason worth writing down: that pool is indexed by
    /// <c>rng.Next(pool.Length)</c>, so adding one string to it would silently change which
    /// module every site in every existing save contains.
    ///
    /// Generous on purpose - roughly one eligible site in six. The radio is not a gate and
    /// nothing downstream depends on it; it is the small reward that makes the second hour
    /// different from the first, which is what D-005a asks acquisitions to be.
    /// </summary>
    public static bool SetAtSite(int siteId, SalvageSiteKind kind)
    {
        switch (kind)
        {
            case SalvageSiteKind.Wreck:
            case SalvageSiteKind.Settlement:
            case SalvageSiteKind.Farmstead:
            case SalvageSiteKind.Depot:
            case SalvageSiteKind.Airfield:
                break;
            default:
                return false;
        }
        var rng = new Random(siteId * 15485863 + 2749);
        return rng.NextDouble() < 0.17;
    }

    /// <summary>Knowledge id for a logged frequency, matching what the relay mast writes.</summary>
    public static string FrequencyKnowledgeId(int siteId) => $"freq.{siteId}";

    // ------------------------------------------------------------- the readout

    /// <summary>
    /// One line for the panel. Facts only, per D-005a - what is playing, or what is in the
    /// way of it playing. Never advice.
    /// </summary>
    public static string Readout(RadioSet set, IReadOnlyList<RadioStation> tuned)
    {
        string who = set.StationIndex >= 0 && set.StationIndex < tuned.Count
            ? tuned[set.StationIndex].Display : "band";

        return set.Why switch
        {
            RadioSilence.NotFitted => "NO SET",
            RadioSilence.SwitchedOff => "RADIO OFF",
            RadioSilence.Unserviceable => "RADIO U/S — avionics",
            RadioSilence.NoStation => tuned.Count == 0
                                      ? "RADIO ON — no frequencies logged"
                                      : "RADIO ON — nothing tuned",
            RadioSilence.NoSignal => $"{who} — no signal",
            RadioSilence.Connecting => $"{who} — …",
            RadioSilence.OffAir => $"{who} — off air",
            RadioSilence.Dropout => "—",
            _ => set.Fidelity >= 0.85 ? who
               : set.Fidelity >= 0.40 ? $"{who}  (weak)"
                                      : $"{who}  (marginal)",
        };
    }
}
