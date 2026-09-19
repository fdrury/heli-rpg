using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The radio somebody left in the aircraft.
///
/// D-058 put the music in diegetically rather than as a score, and this is the node that
/// makes that a machine instead of a metaphor. Everything about it is a system that
/// already existed:
///
/// <list type="bullet">
/// <item><b>It is a refit module.</b> <c>radio</c> in <see cref="Loadout"/>, nine kilos in
/// a bay on the kneeboard, one part to fit at a workshop, found by searching a site. No
/// radio installed, no music - the node runs anyway and stays silent, which is also what
/// happens on a fresh save.</item>
/// <item><b>It runs off the avionics stack.</b> Not a private health number: literally
/// <c>Component.Avionics</c>, so a hit that takes the gyro takes the music, and the
/// avionics part that fixes one fixes the other.</item>
/// <item><b>Tapes and frequencies are salvage and knowledge.</b> A cassette is found by
/// searching, and recorded in <see cref="Progress"/> like any other find, so it rides the
/// existing save. A station is a mast the player already tuned - the "Tune the mast"
/// action has been writing <c>freq.&lt;id&gt;</c> into the log since before there was
/// anything to hear on it.</item>
/// </list>
///
/// <b>Self-contained, like <see cref="WarningPanel"/>.</b> Seven processes share this
/// checkout, so this node finds the aircraft, the play systems and its own music by
/// walking the tree and the filesystem rather than being wired into the scene assembly.
/// The whole of its installation is one line in <see cref="SceneMood.Apply"/>, next to the
/// weather audio, which is the other non-aircraft voice in the game.
///
/// <b>Where the sound comes from.</b> An <c>AudioStreamPlayer3D</c> sitting on the
/// aircraft, not a non-positional player, because the set is a physical object in the
/// cockpit: shut down, climb out and walk away, and the music is behind you. That is free
/// world ambience of exactly the kind the audio benchmark says the game has none of, and
/// it costs one node.
///
/// <b>The mix.</b> <see cref="RadioMix"/> ducks it under the caution and warning tones.
/// The numbers, and how they were measured, are in that class and in
/// <c>tools/simlab/RadioTests.cs</c>; the short version is that the radio at full volume
/// is held just under the rotor, and a warning drops it 15 dB so the horn is 22 dB clear.
/// </summary>
public sealed partial class CockpitRadio : Node3D
{
    // ------------------------------------------------------------------- keys

    /// <summary>On/off. The only thing in the game that switches the radio off.</summary>
    public const Key PowerKey = Key.B;

    /// <summary>Next track on a tape; next station on the band.</summary>
    public const Key NextKey = Key.N;

    /// <summary>Swap between the tape deck and the receiver.</summary>
    public const Key SourceKey = Key.V;

    /// <summary>Volume down and up: the - and = keys, unshifted.</summary>
    public const Key VolumeDownKey = Key.Minus;
    public const Key VolumeUpKey = Key.Equal;

    /// <summary>One press of a volume key. Eight steps from silent to wide open.</summary>
    private const double VolumeStep = 0.125;

    // ------------------------------------------------------------------ paths

    /// <summary>
    /// Where the music lives, in the order searched.
    ///
    /// <c>user://music</c> first because it is the one that works in an exported build and
    /// needs no editor; <c>res://assets/music</c> second because that is where a file
    /// dropped into the repo during development lands. Both are globalised and read with
    /// plain file I/O rather than <c>ResourceLoader</c>, which means a track that was never
    /// imported by the editor still plays - and "drop a folder in and it works" is the
    /// whole requirement.
    /// </summary>
    private static readonly string[] MusicRoots = { "user://music", "res://assets/music" };

    /// <summary>Licence table, read from the music root. Absent is fine; see the parser.</summary>
    private const string ManifestName = "tracks.manifest";

    /// <summary>Volume, on/off and what was selected. A setting, so it lives beside them.</summary>
    private const string SettingsPath = "user://radio.cfg";

    // ------------------------------------------------------------------ state

    private readonly RadioSet _set = new();
    private readonly RadioMix _mix = new();
    private readonly CautionWarningSystem _cws = new();

    private RadioLibrary _library = RadioLibrary.Silent();
    private readonly List<RadioStation> _tuned = new();
    private readonly Dictionary<int, int> _salvageSeen = new();

    private string _root = "";
    private AudioStreamPlayer3D? _player;
    private int _streamed = -1;         // track index currently loaded into the player
    private double _smoothedGain;
    private double _skidHeight = 2.0;
    private double _lastHeight;
    private bool _rotorUpToSpeed;
    private int _knownCount = -1;

    /// <summary>
    /// Seconds the station has been on the air, counted in REAL time.
    ///
    /// Not <see cref="SceneMood.Clock"/>, which runs at 30x so a sortie covers a day. A
    /// station driven by the compressed clock would change track every seven seconds of
    /// listening, which is not a radio station, it is a fault.
    /// </summary>
    private double _stationClock = 3600.0;

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
                case "--warntest":
                    _quiet = true;
                    break;
            }

        LoadLibrary();
        LoadSettings();

        if (_quiet) return;

        _player = new AudioStreamPlayer3D
        {
            Name = "Radio",
            UnitSize = 7f,
            MaxDistance = 140f,
            // Zero, not Godot's default +3. The whole mix budget in RadioMix is quoted at
            // the player's own gain, and a silent 3 dB of close-range boost would put the
            // music over the rotor - which is the one thing the budget says it must never
            // be. The rotor keeps its +3; it is allowed to be the loudest thing.
            MaxDb = 0f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            VolumeDb = -80f,
            MaxPolyphony = 1,
        };
        AddChild(_player);

        _readout = new RadioReadout(this);
        var layer = new CanvasLayer { Name = "RadioReadout", Layer = 2 };
        layer.AddChild(_readout);
        CallDeferred(Node.MethodName.AddChild, layer);

        GD.Print($"[radio] {_library.Tracks.Count} track(s) on {_library.Tapes.Count} tape(s)" +
                 (_root.Length > 0 ? $" from {_root}" : " - no music folder found"));
        if (_library.Undeclared.Count > 0)
            GD.PushWarning($"[radio] {_library.Undeclared.Count} track(s) have no licence in " +
                           $"{ManifestName}; add them there and in docs/wiki/attribution.md " +
                           $"before this game is shared. First: {_library.Undeclared[0]}");
    }

    private static IEnumerable<string> AllArgs()
    {
        foreach (string a in OS.GetCmdlineArgs()) yield return a;
        foreach (string a in OS.GetCmdlineUserArgs()) yield return a;
    }

    // ----------------------------------------------------------- the music folder

    /// <summary>
    /// Find the music and build the library.
    ///
    /// Folder is tape: every subdirectory of the root is a cassette and its audio files
    /// are its tracks. Files loose in the root are one more tape. The manifest, if there
    /// is one, supplies the title, artist, licence, source and level trim; anything it
    /// does not mention still plays and is reported as undeclared.
    /// </summary>
    private void LoadLibrary()
    {
        foreach (string root in MusicRoots)
        {
            if (!DirAccess.DirExistsAbsolute(root)) continue;
            var files = new List<string>();
            Walk(root, "", files, 0);
            if (files.Count == 0 && !FileAccess.FileExists($"{root}/{ManifestName}")) continue;

            string? manifest = FileAccess.FileExists($"{root}/{ManifestName}")
                ? FileAccess.GetFileAsString($"{root}/{ManifestName}") : null;

            _root = root;
            _library = RadioLibrary.Build(files, manifest);
            if (!_library.Empty) return;
        }

        if (_root.Length == 0) _library = RadioLibrary.Silent();
    }

    private static void Walk(string root, string prefix, List<string> into, int depth)
    {
        using DirAccess? dir = DirAccess.Open(prefix.Length == 0 ? root : $"{root}/{prefix}");
        if (dir is null) return;

        dir.ListDirBegin();
        for (string name = dir.GetNext(); name.Length > 0; name = dir.GetNext())
        {
            if (name.StartsWith(".")) continue;
            string rel = prefix.Length == 0 ? name : $"{prefix}/{name}";
            // Two levels is a folder of tapes and the tracks in them. Deeper than that is
            // somebody's music library, and importing an arbitrary tree is not the job.
            if (dir.CurrentIsDir()) { if (depth < 1) Walk(root, rel, into, depth + 1); }
            else into.Add(rel);
        }
        dir.ListDirEnd();
    }

    /// <summary>
    /// Turn a track into something the engine can play.
    ///
    /// Loaded from the file rather than through <c>ResourceLoader</c>, which is what lets
    /// an un-imported file work. Returns null on anything unreadable, and the caller
    /// treats that the same way it treats a missing tape: silence, and a line in the log.
    /// </summary>
    private AudioStream? LoadStream(in RadioTrack track)
    {
        string abs = ProjectSettings.GlobalizePath($"{_root}/{track.Path}");
        try
        {
            if (abs.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                return AudioStreamOggVorbis.LoadFromFile(abs);
            if (abs.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                return AudioStreamMP3.LoadFromFile(abs);
            if (abs.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                return AudioStreamWav.LoadFromFile(abs);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[radio] cannot play {track.Path}: {ex.Message}");
        }
        return null;
    }

    // --------------------------------------------------------------- settings

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;

        _set.Restore(
            (bool)cfg.GetValue("radio", "power", false),
            (double)cfg.GetValue("radio", "volume", 0.55),
            (RadioSourceKind)(int)cfg.GetValue("radio", "source", 0),
            (int)cfg.GetValue("radio", "tape", -1),
            (int)cfg.GetValue("radio", "station", -1),
            -1, 0);
    }

    private void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("radio", "power", _set.PowerOn);
        cfg.SetValue("radio", "volume", _set.Volume);
        cfg.SetValue("radio", "source", (int)_set.Source);
        cfg.SetValue("radio", "tape", _set.TapeIndex);
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
        if (_heli is null) return;

        GlobalPosition = _heli.GlobalPosition;

        if (_skidHeight <= 2.0001)
        {
            double skid = 0;
            Vec3 cg = _heli.Sim.CentreOfGravity;
            foreach (Vec3 c in _heli.Sim.Airframe.ContactPoints) skid = Math.Max(skid, c.Z - cg.Z);
            if (skid > 0.2) _skidHeight = skid;
        }

        _stationClock += delta;
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

        _mix.Update(delta, _cws.LowRotorHorn || _cws.WarningTone,
                    _dialogue?.IsOpen ?? false);

        DamageState dmg = _heli.Sim.Damage;
        bool fitted = _play?.Loadout.IsInstalled(Radio.ModuleId) ?? false;

        double signal = 1.0;
        if (_set.Source == RadioSourceKind.Station && _set.StationIndex >= 0
            && _set.StationIndex < _tuned.Count)
        {
            RadioStation s = _tuned[_set.StationIndex];
            Vector3 here = _heli.GlobalPosition;
            double d = new Vector2((float)s.X, (float)s.Y).DistanceTo(new Vector2(here.X, here.Z));
            Weather.Conditions wx = SceneMood.Now;
            signal = Radio.Quality(d, Math.Max(t.HeightAgl - _skidHeight, 0), s,
                                   wx.Precipitation, wx.StormIntensity);
        }

        _set.Update(delta, _library,
                    new RadioConditions(fitted, dmg.Health(Component.Avionics),
                                        dmg.VibrationIps, signal),
                    _stationClock);

        PushAudio(delta);
        _readout?.QueueRedraw();
    }

    /// <summary>
    /// Keep the engine's player in step with what the set says it is doing.
    ///
    /// The gain is smoothed with a 25 ms one-pole. A dropout is a sudden thing and should
    /// sound like one, but a hard zero on a waveform is a click, and a click is the one
    /// artefact the player would hear every single time.
    /// </summary>
    private void PushAudio(double delta)
    {
        if (_player is null) return;

        if (_set.Track != _streamed || (_set.TrackChanged && _set.Track >= 0))
        {
            _streamed = _set.Track;
            AudioStream? stream = _set.Track >= 0 && _set.Track < _library.Tracks.Count
                ? LoadStream(_library.Tracks[_set.Track]) : null;
            _player.Stream = stream!;
            if (stream is not null) _player.Play((float)_set.Position);
            else _player.Stop();
        }
        else if (_set.Playing && _player.Stream is not null && !_player.Playing)
        {
            // The file ran out before the model thought it would - a real duration that
            // beat the assumed one. Let the model roll on to the next track.
            _player.Play(0f);
        }

        double want = _set.Playing
            ? Radio.PlayerGain(_set.Volume,
                               _set.Track >= 0 ? _library.Tracks[_set.Track].GainDb : 0,
                               _set.Fidelity) * _mix.Gain
            : 0.0;

        _smoothedGain += (want - _smoothedGain) * (1.0 - Math.Exp(-delta / 0.025));
        _player.VolumeDb = _smoothedGain <= 1e-4 ? -80f : Mathf.LinearToDb((float)_smoothedGain);
    }

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
            if (_set.PowerOn) EnsureSomethingSelected();
            changed = true;
        }
        if (Pressed(SourceKey))
        {
            if (_set.Source == RadioSourceKind.Tape)
            {
                if (_tuned.Count > 0) _set.SelectStation(Math.Max(_set.StationIndex, 0));
            }
            else _set.SelectTape(_set.TapeIndex >= 0 ? _set.TapeIndex
                                                     : (_library.Tapes.Count > 0 ? 0 : -1));
            changed = true;
        }
        if (Pressed(NextKey)) { _set.Next(_library, _tuned.Count); changed = true; }
        if (Pressed(VolumeUpKey)) { _set.NudgeVolume(VolumeStep); changed = true; }
        if (Pressed(VolumeDownKey)) { _set.NudgeVolume(-VolumeStep); changed = true; }

        if (changed)
        {
            _readout?.Poke();
            SaveSettings();
        }
    }

    private void EnsureSomethingSelected()
    {
        if (_set.Source == RadioSourceKind.Tape && _set.TapeIndex < 0 && _library.Tapes.Count > 0)
            _set.SelectTape(0);
        if (_set.Source == RadioSourceKind.Station && _set.StationIndex < 0 && _tuned.Count > 0)
            _set.SelectStation(0);
    }

    // -------------------------------------------------------- finding the gear

    /// <summary>
    /// Watch the search log for radio hardware.
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

            Site? site = WorldMap.SiteById(id);
            if (site is null) continue;
            var kind = (SalvageSiteKind)(int)site.Kind;

            if (Radio.SetAtSite(id, kind) && !_play.Loadout.Has(Radio.ModuleId)
                && _play.Loadout.Find(Radio.ModuleId))
            {
                ModuleDef def = Loadout.Catalog[Radio.ModuleId];
                progress.Learn(new Knowledge(KnowledgeKind.Schematic, $"module.{Radio.ModuleId}",
                                             def.Name, def.Description));
                progress.Journal($"Found a radio set at {site.Name}. It still has a tape in it.");
                _readout?.Poke($"Found: {def.Name}");
            }

            int tape = Radio.TapeAtSite(id, _library.Tapes.Count);
            if (tape >= 0)
            {
                string tid = Radio.TapeKnowledgeId(tape);
                if (!progress.Knows(tid))
                {
                    string name = _library.Tapes[tape].Name;
                    progress.Learn(new Knowledge(KnowledgeKind.Schematic, tid,
                                                 $"Tape: {name}",
                                                 $"{_library.Tapes[tape].Tracks.Count} tracks. " +
                                                 $"Somebody's, once."));
                    progress.Journal($"Found a cassette at {site.Name}: {name}.");
                    _readout?.Poke($"Found a tape: {name}");
                }
            }
        }
    }

    /// <summary>
    /// Rebuild the band from the frequencies the player has logged.
    ///
    /// The relay mast action has been writing <c>freq.&lt;siteId&gt;</c> into the knowledge
    /// log since long before there was a radio to hear it on, and the flavour text it
    /// writes is "Carrier present. Nobody answering, but it is on." This is what makes
    /// that true.
    /// </summary>
    private void RefreshStations()
    {
        if (_play is null) return;
        IReadOnlyCollection<Knowledge> known = _play.Progress.Known;
        if (known.Count == _knownCount) return;     // nothing new logged
        _knownCount = known.Count;

        _tuned.Clear();
        foreach (Knowledge k in known)
        {
            if (k.Kind != KnowledgeKind.Frequency) continue;
            if (!k.Id.StartsWith("freq.", StringComparison.Ordinal)) continue;
            if (!int.TryParse(k.Id[5..], out int siteId)) continue;
            Site? site = WorldMap.SiteById(siteId);
            if (site is null) continue;
            _tuned.Add(Radio.StationAt(siteId, site.Name, site.Position.X, site.Position.Y));
        }
        _tuned.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        if (_set.StationIndex >= _tuned.Count) _set.SelectStation(_tuned.Count > 0 ? 0 : -1);
    }

    // ------------------------------------------------------------- for the panel

    internal string Line() => Radio.Readout(_set, _library, _tuned);

    internal bool Installed => _play?.Loadout.IsInstalled(Radio.ModuleId) ?? false;

    internal double Volume => _set.Volume;

    internal bool Ducked => _mix.Gain < 0.7;

    internal string Bindings =>
        $"[{PowerKey}] on/off   [{NextKey}] next   [{SourceKey}] tape/band   - = volume";

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
    private double _flashAge;

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

    public override void _Draw()
    {
        if (!_radio.Installed && _flash.Length == 0) return;

        float alpha = _age < ShowSeconds ? 1f
                    : Math.Max(0f, 1f - (float)(_age - ShowSeconds) / FadeSeconds);
        if (alpha <= 0.01f && _flash.Length == 0) return;

        Vector2 size = GetViewportRect().Size;
        float x = 22, y = size.Y - 40;

        if (_flash.Length > 0)
        {
            float fa = Math.Max(0f, 1f - (float)_flashAge / 6f);
            DrawString(_font, new Vector2(x, y - 46), _flash,
                       HorizontalAlignment.Left, -1, 17,
                       new Color(1.00f, 0.86f, 0.42f, fa));
        }

        if (alpha <= 0.01f) return;

        // A dot that is lit when the set is playing and dim when it is not, then the line
        // itself. Shape before colour, same as the warning panel: the dot is the state and
        // the text is the detail.
        var body = new Color(0.86f, 0.90f, 0.92f, alpha);
        DrawCircle(new Vector2(x + 5, y - 5), 4.5f,
                   new Color(0.35f, 0.85f, 0.45f, alpha * (_radio.Ducked ? 0.30f : 1.0f)));
        DrawString(_font, new Vector2(x + 18, y), _radio.Line(),
                   HorizontalAlignment.Left, -1, 16, body);

        // Volume, as a row of ticks rather than a number: it is a knob, not a gauge.
        int lit = (int)Math.Round(_radio.Volume * 8);
        for (int i = 0; i < 8; i++)
            DrawRect(new Rect2(x + 18 + i * 7, y + 8, 4, 5),
                     new Color(0.86f, 0.90f, 0.92f, alpha * (i < lit ? 0.9f : 0.18f)));

        if (_age < 3.0)
            DrawString(_font, new Vector2(x + 90, y + 13), _radio.Bindings,
                       HorizontalAlignment.Left, -1, 12,
                       new Color(0.70f, 0.76f, 0.80f, alpha * 0.75f));
    }
}
