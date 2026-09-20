using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Plays the world's own voice at each nearby site.
///
/// A settlement has generators running, metal creaking, voices carrying. A wreck field
/// has wind through broken airframes. A relay mast hums with electrical load. An overlook
/// has nothing but wind — and that silence IS the sound, because the player just left a
/// place that had all of those things.
///
/// Each active site gets an <see cref="AudioStreamPlayer3D"/> fed by its own
/// <see cref="AmbientSynth"/>. The player is positional: approach a settlement from 300 m
/// and the generator hum builds in the correct ear. Sites beyond <see cref="AudibleRange"/>
/// are silenced to keep the mixer clean.
///
/// Added to the scene tree by <see cref="SceneMood.Apply"/>.
/// </summary>
public sealed partial class SiteAmbience : Node
{
    /// <summary>Sites within this range get audio.</summary>
    private const float AudibleRange = 600f;

    /// <summary>Sites beyond this are fully silent and their synth is paused.</summary>
    private const float SilentRange = 800f;

    private const int SampleRate = 44100;

    private SiteStreamer? _siteStreamer;
    private HelicopterController? _heli;

    private readonly Dictionary<int, SiteSound> _sounds = new();
    private Vector2 _lastRefreshAt = new(float.MaxValue, float.MaxValue);

    /// <summary>One active ambient emitter.</summary>
    private sealed class SiteSound
    {
        public int SiteId;
        public Site Site = null!;
        public AmbientSynth Synth = null!;
        public AmbientSynth.SiteVoice Voice;
        public AudioStreamPlayer3D Player = null!;
        public AudioStreamGeneratorPlayback? Playback;
        public float[] Block = new float[2048];
        public bool Primed;
    }

    public override void _Ready()
    {
        // Find dependencies lazily in _Process. This node is added by SceneMood.Apply,
        // which runs before the site streamer exists.
    }

    public override void _Process(double delta)
    {
        _siteStreamer ??= GetTree().Root.FindChild("Sites", true, false) as SiteStreamer;
        _heli ??= GetTree().Root.FindChild("Helicopter", true, false) as HelicopterController;
        if (_siteStreamer is null || _heli is null) return;

        Vector3 listenerPos = _heli.GlobalPosition;
        var listenerXZ = new Vector2(listenerPos.X, listenerPos.Z);

        // Refresh site list when the listener has moved significantly.
        if (listenerXZ.DistanceSquaredTo(_lastRefreshAt) > 100f * 100f)
        {
            _lastRefreshAt = listenerXZ;
            RefreshSites(listenerXZ);
        }

        // Feed audio to each active site.
        Weather.Conditions wx = SceneMood.Now;
        foreach (var kv in _sounds)
        {
            var ss = kv.Value;
            if (ss.Playback is null) continue;

            float dist = ss.Site.Position.DistanceTo(listenerXZ);
            if (dist > SilentRange) continue;

            int frames = ss.Playback.GetFramesAvailable();
            if (frames <= 0) continue;

            // Helicopter cooling tick: only when parked and shut down at this site
            double tick = 0;
            if (_siteStreamer.CurrentSite?.Id == ss.SiteId
                && _heli.Sim.Telemetry.RotorRpmPercent < 10
                && _heli.LinearVelocity.Length() < 1.0f)
            {
                tick = 0.6;
            }
            ss.Voice.CoolingTick = tick;

            if (!ss.Primed)
            {
                ss.Synth.Prime(ss.Voice, wx.WindSpeed, 0);
                ss.Primed = true;
            }

            if (ss.Block.Length < frames) ss.Block = new float[frames];
            ss.Synth.Render(ss.Block, frames, ss.Voice, wx.WindSpeed, 0, frames / (double)SampleRate);

            var buffer = new Vector2[frames];
            for (int i = 0; i < frames; i++) buffer[i] = new Vector2(ss.Block[i], ss.Block[i]);
            ss.Playback.PushBuffer(buffer);
        }
    }

    private void RefreshSites(Vector2 listenerXZ)
    {
        // Remove sounds for sites now out of range.
        var remove = new List<int>();
        foreach (var kv in _sounds)
        {
            if (kv.Value.Site.Position.DistanceTo(listenerXZ) > SilentRange)
                remove.Add(kv.Key);
        }
        foreach (int id in remove)
        {
            _sounds[id].Player.QueueFree();
            _sounds.Remove(id);
        }

        // Add sounds for sites now in range.
        foreach (Site site in WorldMap.Sites)
        {
            if (_sounds.ContainsKey(site.Id)) continue;
            if (site.Position.DistanceTo(listenerXZ) > AudibleRange) continue;

            var voice = AmbientSynth.SiteVoice.For((int)site.Kind);

            // Overlooks and fuel caches in calm weather are nearly silent — skip them
            // to save a synth instance and an audio player.
            if (voice.Activity < 0.01 && voice.Metal < 0.01 && voice.Electrical < 0.01)
                continue;

            var synth = new AmbientSynth(SampleRate, site.Id * 7919 + 31);
            var player = new AudioStreamPlayer3D
            {
                Name = $"Amb_{site.Id}",
                Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = 0.20f },
                UnitSize = 40f,
                MaxDistance = (float)AudibleRange,
                VolumeDb = Mathf.LinearToDb(0.45f),
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance,
                MaxPolyphony = 1,
                Position = site.Ground,
                Bus = EnvironmentAcoustics.BusName,
            };
            AddChild(player);
            player.Play();

            var ss = new SiteSound
            {
                SiteId = site.Id,
                Site = site,
                Synth = synth,
                Voice = voice,
                Player = player,
                Playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback(),
            };
            _sounds[site.Id] = ss;
        }
    }
}
