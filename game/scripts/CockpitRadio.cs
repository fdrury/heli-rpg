using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The radio somebody left in the aircraft, tuned to a station somebody is still
/// broadcasting.
///
/// D-058 put the music in diegetically rather than as a score. Fred's follow-up made the
/// source a live internet radio stream rather than files in the repository, and that is
/// the better object for this world by a distance: a cassette is somebody's old mixtape,
/// but a carrier still coming off a hill, with a person on the end of it, is the single
/// most hopeful thing in a post-collapse setting. It also means there is no music to
/// licence, ship, or lose.
///
/// Everything about it is a system that already existed:
///
/// <list type="bullet">
/// <item><b>It is a refit module.</b> <c>radio</c> in <see cref="Loadout"/>, nine kilos in
/// a bay on the kneeboard, one part to fit at a workshop, found by searching a site. No
/// set installed, no music - the node runs anyway and stays silent, which is also what
/// happens on a fresh save.</item>
/// <item><b>It runs off the avionics stack.</b> Not a private health number: literally
/// <c>Component.Avionics</c>, so a hit that takes the gyro takes the music, and the
/// avionics part that fixes one fixes the other.</item>
/// <item><b>Stations are the relay masts the player already tunes.</b> The "Tune the
/// mast" action in <c>SiteInteraction</c> has been writing <c>freq.&lt;siteId&gt;</c>
/// into the knowledge log - with the flavour text "Carrier present. Nobody answering, but
/// it is on." - since long before there was anything to hear on it. This is what makes
/// that line true, and not one character of that file had to change.</item>
/// </list>
///
/// <b>Self-contained, like <see cref="WarningPanel"/>.</b> Seven processes share this
/// checkout, so this node finds the aircraft, the play systems and its stations by walking
/// the tree and the filesystem rather than being wired into the scene assembly. The whole
/// of its installation is one line in <see cref="SceneMood.Apply"/>, next to the weather
/// audio, which is the other non-aircraft voice in the game.
///
/// <b>Where the sound comes from.</b> An <c>AudioStreamPlayer3D</c> sitting on the
/// aircraft, fed by an <c>AudioStreamGenerator</c> exactly the way
/// <see cref="HelicopterAudio"/> and <see cref="WeatherAudio"/> feed theirs - the PCM
/// comes from <see cref="RadioStream"/> instead of from an oscillator. Positional, not
/// ambient, because the set is a physical object in the cockpit: shut down, climb out and
/// walk away, and the music is behind you.
///
/// <b>The mix.</b> <see cref="RadioAgc"/> levels whatever station is tuned to a known
/// loudness, and <see cref="RadioMix"/> ducks it under the caution and warning tones. The
/// numbers, and how they were measured, are in those classes and in
/// <c>tools/simlab/RadioTests.cs</c>; the short version is that the radio at full volume
/// is held just under the rotor, and a warning drops it 15 dB so the horn is 22 dB clear.
///
/// <b>Not shippable as it stands, and that is written down on purpose.</b> A build that
/// plays somebody else's Icecast station is retransmitting their broadcast. For one person
/// on one machine that is nothing; the moment it goes to anybody else it is a licensing
/// question. See <c>docs/wiki/attribution.md</c>.
/// </summary>
public sealed partial class CockpitRadio : Node3D
{
    // ------------------------------------------------------------------- keys

    /// <summary>On/off. The only thing in the game that switches the radio off.</summary>
    public const Key PowerKey = Key.B;

    /// <summary>Next station along the band.</summary>
    public const Key NextKey = Key.N;

    /// <summary>Previous station along the band.</summary>
    public const Key PreviousKey = Key.V;

    /// <summary>Volume down and up: the - and = keys, unshifted.</summary>
    public const Key VolumeDownKey = Key.Minus;
    public const Key VolumeUpKey = Key.Equal;

    /// <summary>One press of a volume key. Eight steps from silent to wide open.</summary>
    private const double VolumeStep = 0.125;

    // ------------------------------------------------------------------ paths

    /// <summary>
    /// The station list, in the order searched. The first one that exists wins.
    ///
    /// <c>user://</c> first so a player can edit the band without touching the game files,
    /// and so an exported build has somewhere writable to look;
    /// <c>res://assets/radio/stations.txt</c> is the one that ships with the repository.
    /// </summary>
    private static readonly string[] StationLists =
        { "user://radio_stations.txt", "res://assets/radio/stations.txt" };

    /// <summary>Volume, on/off and what was tuned. A setting, so it lives beside them.</summary>
    private const string SettingsPath = "user://radio.cfg";

    // ------------------------------------------------------------------ audio

    private const int SampleRate = RadioStream.MixRate;

    // ------------------------------------------------------------------ state

    private readonly RadioSet _set = new();
    private readonly RadioMix _mix = new();
    private readonly RadioAgc _agc = new();
    private readonly CautionWarningSystem _cws = new();
    private readonly RadioStream _stream = new();

    private readonly List<RadioStreamDef> _broadcasters = new();
    private readonly List<RadioStation> _tuned = new();

    /// <summary>
    /// The Upland Service, if the player has found it. Null until the frequency is known.
    ///
    /// Seeded from the transmitter's site id rather than from a clock, so the man on the
    /// mast says the same things in the same order on a reloaded save. A station that
    /// reshuffles its whole personality when you quit and come back is not a place.
    /// </summary>
    private DjBroadcast? _dj;
    private ThreatWorld? _threats;
    private readonly List<DjRegionHeat> _heat = new();
    private readonly Dictionary<int, int> _salvageSeen = new();

    private AudioStreamPlayer3D? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _pcm = new float[8192];
    private float[] _voiceBuf = new float[4096];
    private Vector2[] _out = new Vector2[4096];
    private double _smoothedGain;
    private bool _onDj;

    private double _skidHeight = 2.0;
    private double _lastHeight;
    private bool _rotorUpToSpeed;
    private int _knownCount = -1;

    private HelicopterController? _heli;
    private LandingController? _landing;
    private SiteInteraction? _play;
    private DialoguePanel? _dialogue;

    private RadioReadout? _readout;
    private bool _quiet;
    private double _findPoll;
    private readonly HashSet<Key> _held = new();

    // ------------------------------------------------------------------- setup

    public override void _Ready()
    {
        foreach (string a in AllArgs())
            switch (a)
            {
                case "--selftest": case "--looptest": case "--foottest":
                case "--combattest": case "--savetest": case "--windingtest":
                case "--screenshot": case "--threatreport": case "--worldreport":
                case "--warntest": case "--djreport": case "--ringscan": case "--inputreport":
                    _quiet = true;
                    break;
            }

        LoadStations();
        LoadSettings();

        // Headless runs get no radio at all. They fly the aircraft to places a pilot would
        // not go, and none of them should be opening a socket to the internet to do it.
        if (_quiet) return;

        _player = new AudioStreamPlayer3D
        {
            Name = "Radio",
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = 0.30f },
            UnitSize = 7f,
            MaxDistance = 140f,
            // Zero, not Godot's default +3. The whole mix budget in RadioMix is quoted at
            // the player's own gain, and a silent 3 dB of close-range boost would put the
            // music over the rotor - which is the one thing the budget says it must never
            // be. The rotor keeps its +3; it is allowed to be the loudest thing.
            MaxDb = 0f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            VolumeDb = 0f,
            MaxPolyphony = 1,
        };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;

        _readout = new RadioReadout(this);
        var layer = new CanvasLayer { Name = "RadioReadout", Layer = 2 };
        layer.AddChild(_readout);
        CallDeferred(Node.MethodName.AddChild, layer);

        GD.Print($"[radio] {_broadcasters.Count} station(s) configured" +
                 (_broadcasters.Count == 0
                     ? " - the band is empty; see assets/radio/stations.txt"
                     : $": {_broadcasters[0].Name}…"));
    }

    public override void _ExitTree() => _stream.Dispose();

    private static IEnumerable<string> AllArgs()
    {
        foreach (string a in OS.GetCmdlineArgs()) yield return a;
        foreach (string a in OS.GetCmdlineUserArgs()) yield return a;
    }

    /// <summary>Read the station list. A missing or empty file is silence, not an error.</summary>
    private void LoadStations()
    {
        foreach (string path in StationLists)
        {
            if (!FileAccess.FileExists(path)) continue;
            List<RadioStreamDef> parsed = Radio.ParseStations(FileAccess.GetFileAsString(path));
            if (parsed.Count == 0) continue;
            _broadcasters.AddRange(parsed);
            return;
        }
    }

    // --------------------------------------------------------------- settings

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;
        _set.Restore((bool)cfg.GetValue("radio", "power", false),
                     (double)cfg.GetValue("radio", "volume", 0.55),
                     (int)cfg.GetValue("radio", "station", -1));
    }

    private void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("radio", "power", _set.PowerOn);
        cfg.SetValue("radio", "volume", _set.Volume);
        cfg.SetValue("radio", "station", _set.StationIndex);
        cfg.Save(SettingsPath);
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        if (_quiet) return;

        _heli ??= FindFirst<HelicopterController>(GetTree().Root);
        _landing ??= FindFirst<LandingController>(GetTree().Root);
        _play ??= FindFirst<SiteInteraction>(GetTree().Root);
        _dialogue ??= FindFirst<DialoguePanel>(GetTree().Root);
        _threats ??= FindFirst<ThreatWorld>(GetTree().Root);
        if (_heli is null) return;

        GlobalPosition = _heli.GlobalPosition;

        if (_skidHeight <= 2.0001)
        {
            double skid = 0;
            Vec3 cg = _heli.Sim.CentreOfGravity;
            foreach (Vec3 c in _heli.Sim.Airframe.ContactPoints) skid = Math.Max(skid, c.Z - cg.Z);
            if (skid > 0.2) _skidHeight = skid;
        }

        ReadKeys();
        PollForFinds(delta);
        RefreshStations();

        // The ducker needs to know when a tone is sounding, and the authority on that is
        // the caution and warning system. This node runs its own instance off the same
        // conditioned telemetry the panel uses rather than reaching into the panel for it:
        // eleven filtered conditions is nothing, and a node that needs a hook added to
        // another process's file is a node that cannot be added at all.
        FlightTelemetry t = WarningPanel.Condition(_heli.Sim.Telemetry,
                                                   _landing?.OnGround ?? false,
                                                   _skidHeight, ref _rotorUpToSpeed);

        // Same teleport guard the panel has: a respawn or a fast travel is a different
        // flight, and carrying a latched warning across it would duck the music over an
        // aircraft that no longer exists.
        if (Math.Abs(t.HeightAgl - _lastHeight) > 50.0)
        {
            _cws.Reset();
            _mix.Reset();
            _rotorUpToSpeed = false;
        }
        _lastHeight = t.HeightAgl;

        _cws.Update(t, delta);

        // One chime trigger per raise, matching WarningSynth.Follow. A ducker that
        // retriggered every frame a caution was latched would hold the music down for as
        // long as the caution stayed up, which is not what a chime is.
        for (int i = 0; i < _cws.Raised.Count; i++)
            if (_cws.Raised[i].Severity >= WarningSeverity.Caution) _mix.TriggerCaution();

        _mix.Update(delta, _cws.LowRotorHorn || _cws.WarningTone, _dialogue?.IsOpen ?? false);

        // --- reception ---------------------------------------------------------
        double signal = 0;
        RadioStation station = default;
        bool haveStation = _set.StationIndex >= 0 && _set.StationIndex < _tuned.Count;
        if (haveStation)
        {
            station = _tuned[_set.StationIndex];
            Vector3 here = _heli.GlobalPosition;
            double d = new Vector2((float)station.X, (float)station.Y)
                       .DistanceTo(new Vector2(here.X, here.Z));
            Weather.Conditions wx = SceneMood.Now;
            signal = Radio.Quality(d, Math.Max(t.HeightAgl - _skidHeight, 0), station,
                                   wx.Precipitation, wx.StormIntensity);

            // ...and then the ground between here and there.
            //
            // Radio.Quality is pure geometry and weather: distance, how high the receiver
            // is, how hard it is raining. It has no idea there is a hill in the way, which
            // meant reception was identical in a gorge and on the ridge above it. Fred
            // asked for the station to go to static down in a canyon, and it is the same
            // question the threat system already answers for line of sight - so it is
            // answered the same way, by walking the height field along the path.
            signal *= TerrainShadow(here, station);
        }

        DamageState dmg = _heli.Sim.Damage;
        bool fitted = _play?.Loadout.IsInstalled(Radio.ModuleId) ?? false;

        _set.Update(delta, _tuned.Count,
                    new RadioConditions(fitted, dmg.Health(Component.Avionics),
                                        dmg.VibrationIps, signal, _stream.Link));

        // --- the socket --------------------------------------------------------
        // Opened only when the set actually wants a carrier: switched on, fitted,
        // serviceable, tuned, and in range. Flying out of range hangs up, which is both
        // the polite thing to do to somebody else's server and what makes flying back over
        // the ridge sound like tuning in rather than like nothing happened.
        if (_set.WantsStream && haveStation && station.Url.Length > 0)
        {
            _stream.Tune(station.Url);
            _stream.Poll(delta);
        }
        else if (_stream.Url.Length > 0)
        {
            _stream.Stop();
            _agc.Reset();
        }

        // --- the man on the mast -----------------------------------------------
        // Driven off the same station, signal and duck as everything else on the band, so
        // he fades behind a ridge and gets out of the way of a warning horn without
        // knowing that either thing exists.
        if (_dj is not null)
        {
            _onDj = haveStation && station.Url.Length == 0 && _set.Playing;
            if (!_onDj) _dj.Silence();
            else _dj.Update(delta, BuildDjWorld(), signal * _mix.Gain);
        }
        else _onDj = false;

        PushAudio(delta);
        _readout?.QueueRedraw();
    }

    /// <summary>
    /// Drain the decoder into the generator, at the level the mix says.
    ///
    /// Three gains, in order, and they are separate on purpose:
    /// <list type="number">
    /// <item><see cref="RadioAgc"/> - measured from the audio itself, so an unknown station
    /// arrives at a known loudness and the horn's margin is a fact rather than a hope.</item>
    /// <item><see cref="Radio.PlayerGain"/> - the volume knob and the reception trim. The
    /// knob is the player's and nothing else writes it.</item>
    /// <item><see cref="RadioMix.Gain"/> - the duck.</item>
    /// </list>
    /// The product is ramped across the block rather than stepped at its edge, because a
    /// dropout is a sudden thing but a discontinuity in a waveform is a click, and a click
    /// is the one artefact the player would hear every single time.
    /// </summary>
    private void PushAudio(double delta)
    {
        if (_playback is null) return;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        if (_pcm.Length < frames * 2) _pcm = new float[frames * 2];
        // Exactly sized: PushBuffer takes the whole array, so a larger one would push the
        // tail of the previous block as well.
        if (_out.Length != frames) _out = new Vector2[frames];

        int got = _set.WantsStream ? _stream.Read(_pcm, frames) : 0;
        for (int i = got * 2; i < frames * 2; i++) _pcm[i] = 0;

        // Voice from the announcer. Rendered into the same buffer the stream feeds,
        // so it goes through the same volume knob and duck path. Mono, spread to both
        // channels — a man on a radio is centred, not stereo.
        if (_onDj && _dj is not null && _dj.HasVoice)
        {
            if (_voiceBuf.Length < frames) _voiceBuf = new float[frames];
            _dj.RenderVoice(_voiceBuf.AsSpan(0, frames), frames);
            for (int i = 0; i < frames; i++)
            {
                _pcm[i * 2] += _voiceBuf[i];
                _pcm[i * 2 + 1] += _voiceBuf[i];
            }
        }

        double blockSeconds = frames / (double)SampleRate;
        double agc = got > 0 ? _agc.Update(_pcm.AsSpan(0, got * 2), blockSeconds) : _agc.Gain;

        double want = _set.Playing
            ? agc * Radio.PlayerGain(_set.Volume, GainDbOfTuned(), _set.Fidelity) * _mix.Gain
            : 0.0;

        double from = _smoothedGain;
        double to = from + (want - from) * (1.0 - Math.Exp(-blockSeconds / 0.030));
        _smoothedGain = to;

        for (int i = 0; i < frames; i++)
        {
            float g = (float)(from + (to - from) * (i / (double)frames));
            _out[i] = new Vector2(_pcm[i * 2] * g, _pcm[i * 2 + 1] * g);
        }
        _playback.PushBuffer(_out);
    }

    private double GainDbOfTuned()
        => _set.StationIndex >= 0 && _set.StationIndex < _tuned.Count
           ? _tuned[_set.StationIndex].GainDb : 0;

    // ------------------------------------------------------------------- input

    private bool Pressed(Key k)
    {
        bool down = Input.IsKeyPressed(k);
        bool edge = down && !_held.Contains(k);
        if (down) _held.Add(k); else _held.Remove(k);
        return edge;
    }

    private void ReadKeys()
    {
        if (_dialogue?.IsOpen ?? false) return;

        bool changed = false;

        if (Pressed(PowerKey))
        {
            _set.TogglePower();
            if (_set.PowerOn && _set.StationIndex < 0 && _tuned.Count > 0) _set.Tune(0);
            changed = true;
        }
        if (Pressed(NextKey)) { _set.Next(_tuned.Count); changed = true; }
        if (Pressed(PreviousKey)) { _set.Previous(_tuned.Count); changed = true; }
        if (Pressed(VolumeUpKey)) { _set.NudgeVolume(VolumeStep); changed = true; }
        if (Pressed(VolumeDownKey)) { _set.NudgeVolume(-VolumeStep); changed = true; }

        if (changed)
        {
            _readout?.Poke();
            SaveSettings();
        }
    }

    // -------------------------------------------------------- finding the gear

    /// <summary>
    /// Watch the search log for a radio set.
    ///
    /// Polling <see cref="Progress"/> rather than hooking the search action, because the
    /// search lives in <c>SiteInteraction</c> and this process does not own that file -
    /// and because polling a record that is already saved means a set found before this
    /// node existed is still found. A site's <c>SalvageRemaining</c> going down is a
    /// search having happened, which is the only event needed.
    /// </summary>
    private void PollForFinds(double delta)
    {
        if (_play is null) return;
        _findPoll += delta;
        if (_findPoll < 0.5) return;
        _findPoll = 0;

        Progress progress = _play.Progress;
        foreach (KeyValuePair<int, SiteRecord> kv in progress.AllSites)
        {
            int id = kv.Key;
            int left = kv.Value.SalvageRemaining;
            if (!_salvageSeen.TryGetValue(id, out int was)) { _salvageSeen[id] = left; continue; }
            _salvageSeen[id] = left;
            if (left >= was) continue;               // nothing was searched here

            if (_play.Loadout.Has(Radio.ModuleId)) continue;

            Site? site = WorldMap.SiteById(id);
            if (site is null) continue;
            if (!Radio.SetAtSite(id, (SalvageSiteKind)(int)site.Kind)) continue;
            if (!_play.Loadout.Find(Radio.ModuleId)) continue;

            ModuleDef def = Loadout.Catalog[Radio.ModuleId];
            progress.Learn(new Knowledge(KnowledgeKind.Schematic, $"module.{Radio.ModuleId}",
                                         def.Name, def.Description));
            progress.Journal($"Found a radio set at {site.Name}. Somebody kept it working.");
            _readout?.Poke($"Found: {def.Name}");
        }
    }

    /// <summary>
    /// Rebuild the band from the frequencies the player has logged.
    ///
    /// One logged mast is one station, bound to a broadcaster deterministically by site id
    /// so the same hill always carries the same thing. Nothing here invents a frequency:
    /// if the player has not climbed a relay and swept the band, the radio has nothing on
    /// it, which is the correct answer under D-005.
    /// </summary>
    private void RefreshStations()
    {
        if (_play is null) return;
        IReadOnlyCollection<Knowledge> known = _play.Progress.Known;
        if (known.Count == _knownCount) return;
        _knownCount = known.Count;

        string? wasTuned = _set.StationIndex >= 0 && _set.StationIndex < _tuned.Count
            ? _tuned[_set.StationIndex].Id : null;

        _tuned.Clear();
        foreach (Knowledge k in known)
        {
            if (k.Kind != KnowledgeKind.Frequency) continue;
            if (!k.Id.StartsWith("freq.", StringComparison.Ordinal)) continue;
            if (!int.TryParse(k.Id[5..], out int siteId)) continue;
            Site? site = WorldMap.SiteById(siteId);
            if (site is null) continue;

            // The Upland Service is not a broadcaster. It is a man with a microphone, and
            // binding it to an Icecast URL would put somebody else's music where he is
            // meant to be. RadioStation's no-stream constructor is the documented shape for
            // exactly this, and an empty Url is what tells the socket code to stay shut.
            if (UplandService.Station is RadioStation local && local.Id == k.Id)
            {
                _tuned.Add(local);
                _dj ??= new DjBroadcast(siteId, _quiet ? 0 : SampleRate);
                continue;
            }

            _tuned.Add(Radio.BindStation(siteId, site.Name, site.Position.X, site.Position.Y,
                                         _broadcasters));
        }
        _tuned.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        // Keep the player on the station they were on, wherever it landed in the new list.
        int at = -1;
        if (wasTuned is not null)
            for (int i = 0; i < _tuned.Count; i++) if (_tuned[i].Id == wasTuned) { at = i; break; }
        if (at >= 0) { if (at != _set.StationIndex) _set.Tune(at); }
        else if (_set.StationIndex >= _tuned.Count) _set.Tune(_tuned.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Everything the announcer is allowed to know this minute.
    ///
    /// Assembled here rather than held as live references because <c>DjWorld</c> is a
    /// snapshot by design - it is what stops <c>RadioDj</c> reaching into systems it does
    /// not own. Three of these fields are the whole character:
    ///
    ///   * <b>Heat</b> is <see cref="AlertState"/>, named. He only ever talks about a
    ///     region that has genuinely seen the aircraft, and the band decides how worried
    ///     the line sounds.
    ///   * <b>Deeds</b> is the player's own history (D-074). He applies his own
    ///     knowability gates to it; nothing is filtered on the way in, because deciding
    ///     what a radio station has heard about is the radio station's job.
    ///   * <b>TrackTitle</b> is null and stays null. He has no stream behind him, so he has
    ///     no track to back-announce, and <c>DjCorpus.UnknownTrack</c> is the fallback the
    ///     corpus already carries for exactly this.
    /// </summary>
    private DjWorld BuildDjWorld()
    {
        Weather.Conditions wx = SceneMood.Now;
        Progress? p = _play?.Progress;

        _heat.Clear();
        if (_threats?.Alert is AlertState alert)
            foreach (KeyValuePair<int, double> kv in alert.Raised(RadioDj.TalkAboutAbove))
                if (kv.Key >= 0 && kv.Key < WorldMap.Regions.Count)
                    _heat.Add(new DjRegionHeat(WorldMap.Regions[kv.Key].Name, kv.Value));

        return new DjWorld(
            p?.Clock ?? SceneMood.Clock,
            wx.Sky, wx.WindSpeed, wx.Gust, wx.Visibility, wx.CloudBase,
            wx.IsaDeviation, wx.Precipitation, wx.StormIntensity,
            _heat,
            p?.Search.Complete ?? false,
            null, null,
            p?.Deeds);
    }

    /// <summary>
    /// How much of the signal the ground in between takes out, 0 (blocked) to 1 (clear).
    ///
    /// VHF does not stop dead at a ridge - it diffracts over it, which is why you can still
    /// hear a station from behind a hill and why the sound is a degraded version rather than
    /// silence. So this measures how far the terrain intrudes ABOVE the straight line from
    /// the mast to the aircraft, as a fraction of the path, and attenuates on that rather
    /// than testing a single blocked/not-blocked boolean. Sitting in a gorge with three
    /// hundred metres of rock either side is most of the path obstructed and reads as
    /// static; being ten metres below a ridge line is a shade quieter.
    ///
    /// Sampled coarsely on purpose. This runs every frame the radio is on, the answer only
    /// has to be good enough to move a gain, and the terrain function is the most-called
    /// thing in the game already.
    /// </summary>
    private static double TerrainShadow(Vector3 here, in RadioStation station)
    {
        var from = new Vector2(here.X, here.Z);
        var to = new Vector2((float)station.X, (float)station.Y);
        float span = from.DistanceTo(to);
        if (span < 200f) return 1.0;

        // The mast top is the transmitting end; the aircraft is wherever it is.
        float mastTop = WorldHeight.At(to.X, to.Y) + (float)RadioDj.MastHeightM;

        const int steps = 24;
        double blocked = 0;
        for (int i = 1; i < steps; i++)
        {
            float f = i / (float)steps;
            Vector2 p = from.Lerp(to, f);
            float sight = Mathf.Lerp(here.Y, mastTop, f);
            float ground = WorldHeight.At(p.X, p.Y);

            // How far the ground pokes through the sight line, normalised against a
            // hundred metres - past that it is comprehensively in the way and more rock
            // makes no difference.
            if (ground > sight)
                blocked += Math.Min(1.0, (ground - sight) / 100.0);
        }

        double fraction = blocked / (steps - 1);

        // Floor at 0.05 rather than 0: a completely shadowed station is a carrier you can
        // just about tell is there, which is a more interesting sound than silence and is
        // also what actually happens.
        return Math.Clamp(1.0 - fraction * 1.6, 0.05, 1.0);
    }

    // ------------------------------------------------------------- for the panel

    internal string Line() => Radio.Readout(_set, _tuned);

    internal bool Installed => _play?.Loadout.IsInstalled(Radio.ModuleId) ?? false;

    internal double Volume => _set.Volume;

    internal bool Ducked => _mix.Gain < 0.7;

    /// <summary>What the announcer is saying this instant, or null. Rendered by the readout.</summary>
    internal string? Announcer => _dj?.Caption;

    internal int StationCount => _tuned.Count;

    internal int StationIndex => _set.StationIndex;

    internal string Bindings =>
        $"[{PowerKey}] on/off   [{PreviousKey}]/[{NextKey}] station   - = volume";

    private static T? FindFirst<T>(Node from) where T : Node
    {
        if (from is T hit) return hit;
        foreach (Node child in from.GetChildren())
            if (FindFirst<T>(child) is T found) return found;
        return null;
    }
}

/// <summary>
/// One line, bottom left, saying what is on.
///
/// It shows itself when something changes and fades out again, because a radio readout
/// that is permanently on screen is a HUD element and this is a piece of trim. The
/// bindings are printed with it for the few seconds after a key is pressed, which is the
/// same discovery trick <see cref="WarningPanel"/> uses for its acknowledge key: the
/// footer this process does not own cannot be relied on to teach them.
/// </summary>
public sealed partial class RadioReadout : Control
{
    private const float ShowSeconds = 5.0f;
    private const float FadeSeconds = 1.2f;

    private readonly CockpitRadio _radio;
    private Font _font = null!;
    private double _age = ShowSeconds + FadeSeconds;
    private string _flash = "";
    private double _flashAge = 99;

    public RadioReadout(CockpitRadio radio) => _radio = radio;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Something changed; show the readout for a few seconds.</summary>
    internal void Poke(string? flash = null)
    {
        _age = 0;
        if (flash is not null) { _flash = flash; _flashAge = 0; }
    }

    public override void _Process(double delta)
    {
        _age += delta;
        _flashAge += delta;
        if (_flash.Length > 0 && _flashAge > 6.0) _flash = "";
    }

    /// <summary>
    /// The announcer, wrapped, above the readout line.
    ///
    /// Two decisions worth naming. It is drawn <b>whether or not the readout is showing</b>
    /// - the readout is trim that fades after a few seconds, and a man talking is content
    /// that has to stay up for as long as he is talking. And it wraps to a measured width
    /// rather than a character count, because these lines run to forty words and a fixed
    /// column count set against one font breaks silently against another.
    /// </summary>
    private void DrawAnnouncer(Vector2 size, float x, float bottom)
    {
        string? caption = _radio.Announcer;
        if (string.IsNullOrEmpty(caption)) return;

        const int fontSize = 16;
        float maxWidth = Math.Min(size.X - x * 2, 780f);

        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (string word in caption.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (_font.GetStringSize(candidate, HorizontalAlignment.Left, -1, fontSize).X > maxWidth
                && line.Length > 0)
            {
                lines.Add(line.ToString());
                line.Clear().Append(word);
            }
            else
            {
                line.Clear().Append(candidate);
            }
        }
        if (line.Length > 0) lines.Add(line.ToString());

        const float lineHeight = 21f;
        float top = bottom - lines.Count * lineHeight;

        // A panel behind it, because this is text over a windscreen and the ground behind
        // the windscreen is any colour it likes.
        DrawRect(new Rect2(x - 10, top - 18, maxWidth + 20, lines.Count * lineHeight + 14),
                 new Color(0.04f, 0.05f, 0.06f, 0.62f));

        for (int i = 0; i < lines.Count; i++)
            DrawString(_font, new Vector2(x, top + i * lineHeight), lines[i],
                       HorizontalAlignment.Left, -1, fontSize,
                       new Color(0.90f, 0.88f, 0.78f, 0.96f));
    }

    public override void _Draw()
    {
        if (!_radio.Installed && _flash.Length == 0) return;

        Vector2 viewport = GetViewportRect().Size;
        DrawAnnouncer(viewport, 22, viewport.Y - 86);

        float alpha = _age < ShowSeconds ? 1f
                    : Math.Max(0f, 1f - (float)(_age - ShowSeconds) / FadeSeconds);

        Vector2 size = GetViewportRect().Size;
        float x = 22, y = size.Y - 40;

        if (_flash.Length > 0)
        {
            float fa = Math.Max(0f, 1f - (float)_flashAge / 6f);
            DrawString(_font, new Vector2(x, y - 46), _flash,
                       HorizontalAlignment.Left, -1, 17, new Color(1.00f, 0.86f, 0.42f, fa));
        }

        if (alpha <= 0.01f) return;

        // A dot that is lit when the set is playing and dim when it is ducked, then the
        // line itself. Shape before colour, same as the warning panel: the dot is the
        // state and the text is the detail.
        DrawCircle(new Vector2(x + 5, y - 5), 4.5f,
                   new Color(0.35f, 0.85f, 0.45f, alpha * (_radio.Ducked ? 0.30f : 1.0f)));
        DrawString(_font, new Vector2(x + 18, y), _radio.Line(),
                   HorizontalAlignment.Left, -1, 16, new Color(0.86f, 0.90f, 0.92f, alpha));

        // Volume, as a row of ticks rather than a number: it is a knob, not a gauge.
        int lit = (int)Math.Round(_radio.Volume * 8);
        for (int i = 0; i < 8; i++)
            DrawRect(new Rect2(x + 18 + i * 7, y + 8, 4, 5),
                     new Color(0.86f, 0.90f, 0.92f, alpha * (i < lit ? 0.9f : 0.18f)));

        if (_radio.StationCount > 1)
            DrawString(_font, new Vector2(x + 82, y + 13),
                       $"{_radio.StationIndex + 1}/{_radio.StationCount}",
                       HorizontalAlignment.Left, -1, 12,
                       new Color(0.70f, 0.76f, 0.80f, alpha * 0.8f));

        if (_age < 3.0)
            DrawString(_font, new Vector2(x + 130, y + 13), _radio.Bindings,
                       HorizontalAlignment.Left, -1, 12,
                       new Color(0.70f, 0.76f, 0.80f, alpha * 0.75f));
    }
}
