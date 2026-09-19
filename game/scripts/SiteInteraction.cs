using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>One thing the player can do here, right now.</summary>
public sealed record SiteAction(string Label, string Detail, Func<bool> Perform, bool Available = true);

/// <summary>
/// What a place is FOR.
///
/// The loop this closes is the whole game in miniature: fuel is the reason to fly
/// somewhere, landing is the skill, damage is the cost, and salvage is how you pay for
/// it. Everything else - factions, story, threat envelopes - hangs off this.
///
/// Actions only appear when the aircraft is actually down and settled. That is deliberate:
/// D-003a concluded landing has to be the expensive act, so it must also be the act that
/// pays. Hovering over a fuel cache gets you nothing.
/// </summary>
public sealed partial class SiteInteraction : Node
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath SiteStreamerPath { get; set; } = "";
    [Export] public NodePath LandingControllerPath { get; set; } = "";

    private HelicopterController _heli = null!;
    private SiteStreamer _sites = null!;
    private LandingController _landing = null!;

    /// <summary>Set by Main after both nodes are created.</summary>
    public DialoguePanel? Dialogue { get; set; }
    public CodaServer? CodaServer { get; set; }
    public bool InDialogue => Dialogue?.IsOpen ?? false;

    public Progress Progress { get; } = Progress.NewGame();

    /// <summary>Actions available at this instant. Empty when airborne or away from a site.</summary>
    public IReadOnlyList<SiteAction> Actions => _actions;
    private readonly List<SiteAction> _actions = new();

    /// <summary>The place the aircraft is parked at, if any.</summary>
    public Site? Parked { get; private set; }

    /// <summary>Set while a timed action (refuelling, repairing) is in progress.</summary>
    public string? Busy { get; private set; }
    public float BusyProgress { get; private set; }

    private double _busyRemaining;
    private Action? _busyComplete;
    private readonly RandomNumberGenerator _rng = new();

    public event Action<string>? Notice;

    // --- NPC registry: persists across visits so memory accumulates ---
    private readonly Dictionary<int, (NpcMind npc, DialogueBank bank)> _npcs = new();
    private int _mattieSiteId = -1;

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
        _sites = GetNode<SiteStreamer>(SiteStreamerPath);
        _landing = GetNode<LandingController>(LandingControllerPath);
        _rng.Seed = 5150;

        Progress.Journalled += line => GD.Print($"[journal] {line}");

        // Mattie is at the first settlement in the Basin — the tutorial region.
        foreach (var site in WorldMap.Sites)
        {
            if (site.Kind == SiteKind.Settlement && site.Region == RegionKind.Basin)
            {
                _mattieSiteId = site.Id;
                GD.Print($"[dialogue] Mattie lives at {site.Name} (id {site.Id})");
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        // The world clock runs faster than real time; a sortie should feel like part of
        // a day, not all of it.
        Progress.Clock += delta * 12.0;

        if (_busyRemaining > 0)
        {
            _busyRemaining -= delta;
            BusyProgress = 1f - (float)Mathf.Clamp(_busyRemaining / Math.Max(_busyTotal, 1e-3), 0, 1);
            if (_busyRemaining <= 0)
            {
                _busyComplete?.Invoke();
                _busyComplete = null;
                Busy = null;
                BusyProgress = 0;
            }
            return;
        }

        UpdateParked();
        if (!InDialogue) RebuildActions();
        KeepMassInSync();
    }

    private double _busyTotal;

    /// <summary>
    /// Settled means: on the ground, not moving, and the rotor no longer flying the
    /// aircraft. You cannot pump fuel with the blades turning at full pitch.
    /// </summary>
    private bool Settled =>
        _landing.OnGround
        && _heli.LinearVelocity.Length() < 1.2f
        && _heli.Sim.Telemetry.RotorRpmPercent < 70;

    private void UpdateParked()
    {
        Site? here = _sites.CurrentSite;
        if (here is null || !_landing.OnGround) { Parked = null; return; }

        // Has to be inside the footprint, not merely in the neighbourhood.
        float d = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z).DistanceTo(here.Position);
        Parked = d < here.Radius + 25f ? here : null;

        if (Parked is not null && Settled && !Progress.HasVisited(Parked.Id))
        {
            Progress.MarkVisited(Parked.Id);
            Progress.Journal($"Put down at {Parked.Name}. {Describe(Parked)}");
            Notice?.Invoke($"{Parked.Name}");
        }
    }

    private static string Describe(Site s) => s.Kind switch
    {
        SiteKind.FuelCache => "Tanks, and a windpump that still turns.",
        SiteKind.Settlement => "Somebody has swept the street.",
        SiteKind.Workshop => "A shed with the doors shut and the tools still inside.",
        SiteKind.Wreck => "Whatever this was, it came down hard.",
        SiteKind.Relay => "The mast is intact. That is rarer than it sounds.",
        SiteKind.Depot => "Too big to have been emptied by hand.",
        SiteKind.Airfield => "Concrete. Acres of it, going back to grass.",
        SiteKind.Farmstead => "A house, a barn, and no reason anyone left in a hurry.",
        _ => "You can see a long way from here.",
    };

    // --------------------------------------------------------------- actions

    private void RebuildActions()
    {
        _actions.Clear();
        if (Parked is null || !Settled) return;
        Site site = Parked;
        SiteRecord rec = Progress.Record(site.Id);

        switch (site.Kind)
        {
            case SiteKind.FuelCache:
            case SiteKind.Airfield:
            case SiteKind.Depot:
                AddRefuel(site, rec);
                break;
        }

        if (site.Kind == SiteKind.Settlement) AddTalk(site);

        if (site.Kind is SiteKind.Workshop or SiteKind.Airfield or SiteKind.Settlement)
            AddRepairs(site);

        if (site.Kind is SiteKind.Wreck or SiteKind.Depot or SiteKind.Farmstead or SiteKind.Airfield)
            AddSalvage(site, rec);

        if (site.Kind == SiteKind.Relay) AddTuneRelay(site);
        if (site.Kind == SiteKind.Overlook) AddSurvey(site);
        if (site.Kind == SiteKind.Settlement) AddAskAround(site);
    }

    private void AddRefuel(Site site, SiteRecord rec)
    {
        if (rec.FuelRemaining < 0)
        {
            // How much is here is decided once, on first arrival, and remembered.
            _rng.Seed = (ulong)(site.Id * 104729 + 7);
            double baseline = site.Kind switch
            {
                SiteKind.Airfield => _rng.RandfRange(900, 2600),
                SiteKind.Depot => _rng.RandfRange(400, 1400),
                _ => _rng.RandfRange(0, 600),           // often dry - that is the point
            };
            rec.FuelRemaining = Math.Round(baseline);
        }

        double capacity = _heli.Sim.Airframe.FuelCapacity;
        double room = capacity - _heli.Sim.Fuel;

        if (rec.FuelRemaining < 1)
        {
            _actions.Add(new SiteAction("Refuel", "The tanks here are dry.", () => false, false));
            return;
        }
        if (room < 5)
        {
            _actions.Add(new SiteAction("Refuel", "Already full.", () => false, false));
            return;
        }

        double transfer = Math.Min(room, rec.FuelRemaining);
        _actions.Add(new SiteAction(
            $"Refuel  (+{transfer:F0} kg, {rec.FuelRemaining:F0} kg in the tanks)",
            "Hand pump. It takes as long as it takes.",
            () =>
            {
                BeginBusy("Refuelling", transfer / 18.0, () =>
                {
                    _heli.Sim.Fuel += transfer;
                    _heli.Sim.InvalidateMass();
                    rec.FuelRemaining -= transfer;
                    Progress.Journal($"Took on {transfer:F0} kg at {site.Name}. " +
                                     (rec.FuelRemaining < 1 ? "That was the last of it." : $"{rec.FuelRemaining:F0} kg left."));
                    Notice?.Invoke($"+{transfer:F0} kg fuel");
                });
                return true;
            }));
    }

    private void AddRepairs(Site site)
    {
        DamageState dmg = _heli.Sim.Damage;
        var worst = dmg.Worst();
        if (worst.Health > 0.995)
        {
            _actions.Add(new SiteAction("Repair", "Nothing to fix.", () => false, false));
            return;
        }

        double missing = 1.0 - worst.Health;
        double scrapCost = Math.Ceiling(missing * 18);
        double partsCost = missing > 0.5 ? Math.Ceiling(missing * 2) : 0;
        bool canPay = Progress.Amount(Stock.Scrap) >= scrapCost && Progress.Amount(Stock.Parts) >= partsCost;

        string cost = partsCost > 0 ? $"{scrapCost:F0} scrap, {partsCost:F0} parts" : $"{scrapCost:F0} scrap";
        _actions.Add(new SiteAction(
            $"Repair {worst.Component} ({worst.Health:P0})  -  {cost}",
            canPay ? "There is enough here to do the job properly." : "You do not have the materials.",
            () =>
            {
                if (!canPay) return false;
                Progress.Spend(Stock.Scrap, scrapCost);
                Progress.Spend(Stock.Parts, partsCost);
                BeginBusy($"Repairing {worst.Component}", 6 + missing * 30, () =>
                {
                    dmg.Repair(worst.Component, missing);
                    Progress.Journal($"Repaired the {worst.Component} at {site.Name}.");
                    Notice?.Invoke($"{worst.Component} serviceable");
                });
                return true;
            }, canPay));
    }

    private void AddSalvage(Site site, SiteRecord rec)
    {
        if (rec.SalvageRemaining < 0)
        {
            _rng.Seed = (ulong)(site.Id * 15485863 + 11);
            rec.SalvageRemaining = site.Kind switch
            {
                SiteKind.Depot => _rng.RandiRange(4, 9),
                SiteKind.Airfield => _rng.RandiRange(3, 8),
                SiteKind.Wreck => _rng.RandiRange(1, 4),
                _ => _rng.RandiRange(0, 3),
            };
        }

        if (rec.SalvageRemaining <= 0)
        {
            _actions.Add(new SiteAction("Search", "Stripped. You were not the first.", () => false, false));
            return;
        }

        _actions.Add(new SiteAction(
            $"Search  ({rec.SalvageRemaining} places left to look)",
            "Slow, and you have to shut down to do it.",
            () =>
            {
                BeginBusy("Searching", 14, () =>
                {
                    rec.SalvageRemaining--;
                    _rng.Seed = (ulong)(site.Id * 31 + rec.SalvageRemaining * 977);

                    double scrap = _rng.RandfRange(2, 11);
                    Progress.Add(Stock.Scrap, Math.Round(scrap));
                    string found = $"{scrap:F0} scrap";

                    if (_rng.Randf() < 0.30f) { Progress.Add(Stock.Parts, 1); found += ", a serviceable part"; }
                    if (_rng.Randf() < 0.18f) { Progress.Add(Stock.Fuel, 1); found += ", a full can"; }
                    if (_rng.Randf() < 0.12f) { Progress.Add(Stock.Medical, 1); found += ", a medical kit"; }

                    Progress.Journal($"Searched {site.Name}: {found}.");
                    Notice?.Invoke(found);
                });
                return true;
            }));
    }

    private void AddTuneRelay(Site site)
    {
        string id = $"freq.{site.Id}";
        if (Progress.Knows(id))
        {
            _actions.Add(new SiteAction("Tune the mast", "Already logged.", () => false, false));
            return;
        }
        _actions.Add(new SiteAction("Tune the mast",
            "Whatever is still transmitting on this hill is worth writing down.",
            () =>
            {
                BeginBusy("Sweeping the band", 10, () =>
                {
                    _rng.Seed = (ulong)(site.Id * 7919);
                    string freq = $"{_rng.RandfRange(118f, 152f):F2} MHz";
                    Progress.Learn(new Knowledge(KnowledgeKind.Frequency, id,
                        $"{freq} - {site.Name}",
                        "Carrier present. Nobody answering, but it is on."));
                    Notice?.Invoke($"Frequency logged: {freq}");
                });
                return true;
            }));
    }

    private void AddSurvey(Site site)
    {
        string id = $"chart.{site.Region}";
        if (Progress.Knows(id))
        {
            _actions.Add(new SiteAction("Survey", "This ground is already on the chart.", () => false, false));
            return;
        }
        _actions.Add(new SiteAction("Survey from here",
            "An hour with the map on the skid. Under a knowledge-based progression, this IS the level up.",
            () =>
            {
                BeginBusy("Surveying", 16, () =>
                {
                    Region region = WorldMap.RegionAt(site.Position);
                    Progress.Learn(new Knowledge(KnowledgeKind.Chart, id,
                        $"Chart: {region.Name}",
                        "Landing sites, dead ground, and what is worth a second look."));
                    Progress.Record(site.Id).Surveyed = true;
                    Notice?.Invoke($"Charted {region.Name}");
                });
                return true;
            }));
    }

    private void AddAskAround(Site site)
    {
        string id = $"rumour.{site.Id}";
        if (Progress.Knows(id))
        {
            _actions.Add(new SiteAction("Ask around", "You have had that conversation.", () => false, false));
            return;
        }
        _actions.Add(new SiteAction("Ask around",
            "People here have seen things. Some of them are even true.",
            () =>
            {
                BeginBusy("Asking around", 12, () =>
                {
                    _rng.Seed = (ulong)(site.Id * 2654435761);
                    Site? subject = WorldMap.Nearest(site.Position, SiteKind.Depot);
                    string detail = subject is not null
                        ? $"Somebody swears there is still fuel at {subject.Name}."
                        : "Nobody will say anything useful, but they say it at length.";
                    Progress.Learn(new Knowledge(KnowledgeKind.Rumour, id, $"Talk at {site.Name}", detail));
                    Notice?.Invoke("Heard something");
                });
                return true;
            }));
    }

    // ------------------------------------------------------------------ util

    private void BeginBusy(string label, double seconds, Action onComplete)
    {
        Busy = label;
        _busyTotal = seconds;
        _busyRemaining = seconds;
        _busyComplete = onComplete;
    }

    public bool Trigger(int index)
    {
        if (Busy is not null) return false;
        if (index < 0 || index >= _actions.Count) return false;
        SiteAction a = _actions[index];
        return a.Available && a.Perform();
    }

    /// <summary>
    /// Push what the player is carrying into the aircraft's mass properties, so a heavy
    /// load is felt in the hover rather than read off a screen. This is most of the point
    /// of having a load system at all.
    /// </summary>
    private void KeepMassInSync()
    {
        double carried = Progress.CarriedMass;
        if (Math.Abs(carried - _lastCarried) < 0.5) return;
        _lastCarried = carried;
        _heli.Sim.Airframe.Mass.Remove("cargo");
        if (carried > 0.1) _heli.Sim.Airframe.Mass.Add("cargo", new Vec3(-0.6, 0, -0.1), carried);
        _heli.Sim.InvalidateMass();
    }

    private double _lastCarried = -1;

    // --------------------------------------------------------------- dialogue

    private void AddTalk(Site site)
    {
        _actions.Add(new SiteAction("Talk", "People here.",
            () =>
            {
                if (Dialogue is null) return false;
                var (npc, bank) = GetOrCreateNpc(site);
                var ctx = BuildTalkContext(site, npc);

                bool firstConversation = npc.Meetings == 0;
                DialogueCorpus.RememberVisit(npc, ctx);

                if (firstConversation)
                {
                    Progress.Learn(new Knowledge(KnowledgeKind.Contact,
                        $"contact.{npc.Id}", npc.Name, $"At {site.Name}."));
                }

                npc.Standing = Math.Min(1.0, npc.Standing + 0.05);

                Dialogue.Open(npc, bank, ctx, CodaServer);
                return true;
            }));
    }

    private (NpcMind npc, DialogueBank bank) GetOrCreateNpc(Site site)
    {
        if (_npcs.TryGetValue(site.Id, out var existing)) return existing;

        NpcMind npc;
        DialogueBank bank;

        if (site.Id == _mattieSiteId)
        {
            npc = DialogueCorpus.Mattie();
            bank = DialogueCorpus.MattieLines();
        }
        else
        {
            npc = DialogueCorpus.Settler(site.Id, site.Name);
            bank = DialogueCorpus.SettlerLines();
        }

        _npcs[site.Id] = (npc, bank);
        return (npc, bank);
    }

    private TalkContext BuildTalkContext(Site site, NpcMind npc)
    {
        var sim = _heli.Sim;
        var worst = sim.Damage.Worst();
        double fuelFrac = sim.Fuel / Math.Max(sim.Airframe.FuelCapacity, 1);

        return new TalkContext
        {
            Now = Progress.Clock,
            SiteId = site.Id.ToString(),
            SiteName = site.Name,
            RegionName = WorldMap.RegionAt(site.Position).Name,
            FuelFraction = fuelFrac,
            WorstComponentHealth = worst.Health,
            WorstComponentName = worst.Component.ToString(),
            CarriedMass = Progress.CarriedMass,
            CarriedParts = (int)Progress.Amount(Stock.Parts),
            CarriedMedical = Progress.Amount(Stock.Medical) > 0,
            HoursSinceLastMeeting = npc.LastSeenAt < 0
                ? double.PositiveInfinity
                : (Progress.Clock - npc.LastSeenAt) / 3600.0,
            PreviousMeetings = npc.Meetings,
            ArrivedDamaged = worst.Health < 0.6,
            ArrivedLowOnFuel = fuelFrac < 0.25,
            ArrivedAtNight = false,      // TODO: day/night cycle
            Standing = npc.Standing,
        };
    }
}
