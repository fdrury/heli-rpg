using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The Upland Service: a man in a hut on a mast, and the only station in the world whose
/// audio does not come off the internet.
///
/// <para><b>Why this file exists.</b> <c>RadioDj</c> had 676 authored lines, twenty-eight
/// topics, a scheduler, a corpus guard and eleven tests, and <b>nothing in the running game
/// referenced it</b>. That is the failure mode <c>integration-debt.md</c> opens by naming: a
/// built system that is not wired in costs maintenance and returns nothing. This is the
/// wire.</para>
///
/// <para><b>What it does not do.</b> It does not speak. There is no text-to-speech here and
/// no recorded voice, so his lines arrive as timed captions paced by
/// <see cref="RadioDj.ReadSeconds"/> - the same duration the audio would have taken. That is
/// a real limitation and not a placeholder for one: the pacing, the gaps where the records
/// are, the reception gating and everything the announcer decides to say are all live, and
/// the only missing piece is a voice. Swapping captions for audio later changes this file
/// and nothing else.</para>
/// </summary>
public static class UplandService
{
    /// <summary>
    /// The knowledge id that puts him on the dial, in the same `freq.&lt;siteId&gt;` shape
    /// every other station uses. He is found, not given.
    /// </summary>
    public static string FrequencyId => $"freq.{Transmitter?.Id ?? -1}";

    private static Site? _transmitter;
    private static bool _resolved;
    private static int _ruleUsed = -1;

    /// <summary>
    /// The site his mast stands on, chosen by <see cref="RadioDj.SiteRules"/>.
    ///
    /// Resolved once and cached. Site placement is rejection sampling and can fail, which is
    /// why the rules are a chain rather than one rule; if every rule in the chain comes up
    /// empty the station does not exist, which is a legal outcome and not a crash.
    /// </summary>
    public static Site? Transmitter
    {
        get { Resolve(); return _transmitter; }
    }

    /// <summary>Which rule in the chain actually bound, for <c>--worldreport</c>. -1 if none.</summary>
    public static int RuleUsed { get { Resolve(); return _ruleUsed; } }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        for (int i = 0; i < RadioDj.SiteRules.Length; i++)
        {
            DjSiteRule rule = RadioDj.SiteRules[i];
            // RadioDj speaks in sim-side tags and the world is indexed by Godot-side enums.
            // The two pairs are declared in the same order on purpose - the same convention
            // Salvage uses for (SalvageSiteKind)(int)site.Kind - so the cast is the join.
            var wantRegion = (RegionKind)(int)rule.Region;
            var wantKind = (SiteKind)(int)rule.Kind;

            Region? region = WorldMap.Regions.FirstOrDefault(r => r.Kind == wantRegion);
            if (region is null) continue;

            // RawAt rather than At, for the reason StoryPlaces.Candidates gives: the graded
            // pad under a site is a levelling artefact and would flatten the very difference
            // "highest ground" sorts on. A mast wants the real hill.
            float Key(Site s) => rule.Pick switch
            {
                DjPick.BuildOrder => s.Id,
                DjPick.LowestGround => WorldHeight.RawAt(s.Position.X, s.Position.Y),
                DjPick.HighestGround => -WorldHeight.RawAt(s.Position.X, s.Position.Y),
                DjPick.FurthestFromCentre => -s.Position.DistanceTo(region.Centre),
                DjPick.NearestToCentre => s.Position.DistanceTo(region.Centre),
                _ => s.Id,
            };

            List<Site> ordered = WorldMap.Sites
                .Where(s => s.Region == wantRegion && s.Kind == wantKind)
                .OrderBy(Key).ThenBy(s => s.Id).ToList();

            if (ordered.Count < rule.Ordinal) continue;
            _transmitter = ordered[rule.Ordinal - 1];
            _ruleUsed = i;
            return;
        }
    }

    /// <summary>
    /// His entry on the dial: a transmitter with no stream behind it.
    ///
    /// The empty Url is the contract <see cref="RadioStation"/> documents - it is how
    /// <see cref="CockpitRadio"/> is told not to open a socket. Everything else about
    /// reception is identical to a broadcaster's, so he fades behind a ridge exactly the way
    /// the rest of the band does.
    /// </summary>
    public static RadioStation? Station
    {
        get
        {
            Site? t = Transmitter;
            if (t is null) return null;
            return new RadioStation(FrequencyId, RadioDj.StationName,
                                    t.Position.X, t.Position.Y,
                                    RadioDj.MastHeightM, RadioDj.PowerKm);
        }
    }

    /// <summary>For tests and reports. Forces a fresh resolve against the current world.</summary>
    public static void Forget() { _resolved = false; _transmitter = null; _ruleUsed = -1; }
}

/// <summary>
/// One running broadcast: the announcer, a clock, and whatever he is part-way through saying.
///
/// <para>Deliberately not a <c>Node</c>. Everything in here is plain state driven by a
/// delta, which means the whole thing can be run headless at any speed by a test - and the
/// interesting failures of a radio station are all things that happen over hours, which is
/// not something you can check by flying around for a minute.</para>
/// </summary>
public sealed class DjBroadcast
{
    private readonly DjHost _host;
    private readonly Queue<DjSegment> _pending = new();

    private double _sinceBoundary;
    private double _captionLeft;
    private DjSegment? _current;

    /// <summary>
    /// How long a record lasts, which is how often he gets a chance to speak.
    ///
    /// He does not take every chance - <c>DjHost.OnTrackBoundary</c> rolls against its own
    /// link chance and its own minimum gap - so this is the tick rate of the opportunity,
    /// not of the talking.
    /// </summary>
    public double TrackSeconds { get; set; } = Radio.AssumedTrackSeconds;

    /// <summary>
    /// How clean the signal has to be before he will start a sentence.
    ///
    /// <see cref="Radio.Quality"/> is already 0 at the usable field and 1 at a clean one, so
    /// this is "comfortably readable" rather than "audible at all". Low on purpose: the
    /// point of the reception model is that the edge of his range is a place you can still
    /// just about hear him, and a high bar here would turn a gradient into a switch.
    /// </summary>
    public const double ReadableQuality = 0.15;

    public DjBroadcast(int seed) => _host = new DjHost(seed);

    /// <summary>What he is saying this instant, or null when a record is on.</summary>
    public string? Caption => _current?.Text;

    /// <summary>What the caption is about. For a report; nothing in the game reads it.</summary>
    public DjTopic? CaptionTopic => _current?.Topic;

    /// <summary>Breaks he has made since the set was switched on.</summary>
    public int Breaks => _host.Breaks;

    /// <summary>
    /// Advance by <paramref name="dt"/> seconds of game time.
    ///
    /// <paramref name="signal"/> is the reception quality from <see cref="Radio.Quality"/>.
    /// A break that starts while he is unreadable is not queued at all rather than being
    /// queued and hidden: coming back into range part-way through a sentence and hearing the
    /// end of it is right, but starting the sentence for an aircraft that could not hear the
    /// beginning is the station waiting for the player, which it must never do.
    /// </summary>
    public void Update(double dt, in DjWorld world, double signal)
    {
        if (dt <= 0) return;

        if (_current is not null)
        {
            _captionLeft -= dt;
            if (_captionLeft <= 0)
            {
                _current = _pending.Count > 0 ? _pending.Dequeue() : null;
                _captionLeft = _current?.Seconds ?? 0;
            }
            return;
        }

        _sinceBoundary += dt;
        if (_sinceBoundary < TrackSeconds) return;
        _sinceBoundary = 0;

        if (signal < ReadableQuality) return;

        DjBreak? link = _host.OnTrackBoundary(world);
        if (link is null) return;

        foreach (DjSegment seg in link.Segments) _pending.Enqueue(seg);
        if (_pending.Count == 0) return;

        _current = _pending.Dequeue();
        _captionLeft = _current.Value.Seconds;
    }

    /// <summary>Switched off, or tuned away. He carries on without you; you just stop hearing it.</summary>
    public void Silence()
    {
        _pending.Clear();
        _current = null;
        _captionLeft = 0;
    }
}
