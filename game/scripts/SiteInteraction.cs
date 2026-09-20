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
    public Loadout Loadout { get; } = new();

    /// <summary>Set by Main after ThreatWorld is created. Used for alert-aware contracts.</summary>
    public AlertState? Alert { get; set; }

    /// <summary>Set by Main. Radio messages from the search thread are pushed here.</summary>
    public RadioStrip? RadioStrip { get; set; }

    /// <summary>Raised when a module is found during salvage.</summary>
    public event Action<ModuleDef>? ModuleFound;
    /// <summary>Raised when a module is installed. Main wires the system-specific effects.</summary>
    public event Action<ModuleDef>? ModuleInstalled;
    /// <summary>Raised when a module is removed. Main wires the system-specific effects.</summary>
    public event Action<ModuleDef>? ModuleRemoved;
    /// <summary>Raised when a passenger boards via dialogue reward (D-090).</summary>
    public event Action<string>? PassengerBoarded;
    /// <summary>Raised when a sling load is attached via dialogue reward (D-091).</summary>
    public event Action<string>? SlingLoadAttached;

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

    /// <summary>
    /// Raise <see cref="Notice"/> from outside this class.
    ///
    /// C# only lets a type raise its own events, so the encounter code in Main could not
    /// call `Notice?.Invoke(...)` and would not compile. One method rather than making the
    /// event a plain delegate field, because a field can also be silently reassigned by any
    /// caller and lose every existing subscriber.
    /// </summary>
    public void Announce(string message) => Notice?.Invoke(message);

    // --- NPC registry: persists across visits so memory accumulates ---
    private readonly Dictionary<int, (NpcMind npc, DialogueBank bank)> _npcs = new();
    // --- contract boards: cached per settlement, refreshed on clock cycle ---
    private readonly Dictionary<int, (List<Contract> board, int cycle)> _boards = new();
    private const double BoardRefreshInterval = 7200; // 2 game-hours between board refreshes

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
        _sites = GetNode<SiteStreamer>(SiteStreamerPath);
        _landing = GetNode<LandingController>(LandingControllerPath);
        _rng.Seed = 5150;

        Progress.Journalled += line => GD.Print($"[journal] {line}");
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
        CheckSearchThread();
        CheckContractCompletion();
        CheckDirectedCalls();
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

        // D-096: ash-contaminated sites offer nothing. The fog does not lift.
        if (Progress.IsSiteContaminated(site.Id))
        {
            _actions.Add(new SiteAction("Ash", "Nothing here any more.", () => false, false));
            return;
        }

        SiteRecord rec = Progress.Record(site.Id);

        switch (site.Kind)
        {
            case SiteKind.FuelCache:
            case SiteKind.Airfield:
            case SiteKind.Depot:
                AddRefuel(site, rec);
                break;
        }

        // Talk at settlements (always) and at non-settlements where a story NPC lives
        // (story.md §7.5: Juno is at an airfield, Ferren may fall to a workshop or depot).
        if (site.Kind == SiteKind.Settlement || StoryPlaces.For(site.Id)?.NpcId is not null)
            AddTalk(site);

        if (site.Kind is SiteKind.Workshop or SiteKind.Airfield or SiteKind.Settlement)
            AddRepairs(site);

        if (site.Kind is SiteKind.Wreck or SiteKind.Depot or SiteKind.Farmstead or SiteKind.Airfield)
            AddSalvage(site, rec);

        if (site.Kind is SiteKind.Workshop or SiteKind.Airfield)
            AddRefit(site);

        if (site.Kind == SiteKind.Relay) AddTuneRelay(site);
        if (site.Kind == SiteKind.Overlook) AddSurvey(site);
        if (site.Kind == SiteKind.Settlement) AddAskAround(site);
        if (site.Kind == SiteKind.Settlement) AddContractBoard(site);
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
        var salvageKind = (SalvageSiteKind)(int)site.Kind;
        if (rec.SalvageRemaining < 0)
            rec.SalvageRemaining = Salvage.SearchesAt(salvageKind, site.Id);

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
                    // The search index counts down, so the first search is the highest index.
                    var find = Salvage.Search(salvageKind, site.Tier, site.Id, rec.SalvageRemaining);

                    string found = "";
                    if (find.Scrap > 0)
                    {
                        Progress.Add(Stock.Scrap, find.Scrap);
                        found = $"{find.Scrap:F0} scrap";
                    }
                    if (find.FuelCans > 0)
                    {
                        Progress.Add(Stock.Fuel, find.FuelCans);
                        found += (found.Length > 0 ? ", " : "") + $"{find.FuelCans} full can{(find.FuelCans > 1 ? "s" : "")}";
                    }
                    if (find.Medical > 0)
                    {
                        Progress.Add(Stock.Medical, find.Medical);
                        found += (found.Length > 0 ? ", " : "") + "a medical kit";
                    }
                    if (find.Food > 0)
                    {
                        Progress.Add(Stock.Food, find.Food);
                        found += (found.Length > 0 ? ", " : "") + $"{find.Food} ration{(find.Food > 1 ? "s" : "")}";
                    }
                    if (find.Ammunition > 0)
                    {
                        Progress.Add(Stock.Ammunition, find.Ammunition);
                        found += (found.Length > 0 ? ", " : "") + $"{find.Ammunition} rounds";
                    }
                    if (find.Part is SalvagePart part)
                    {
                        Progress.Cargo.Take(part);
                        found += (found.Length > 0 ? ", " : "") + part.ToString();
                    }

                    if (found.Length == 0) found = "nothing";

                    // Module discovery: certain sites yield a specific module (D-011).
                    string? modId = Loadout.ModuleAtSite(site.Id);
                    if (modId is not null && Loadout.Find(modId))
                    {
                        var mod = Loadout.Catalog[modId];
                        found += $", {mod.Name}!";
                        Progress.Learn(new Knowledge(KnowledgeKind.Schematic,
                            $"module.{modId}", mod.Name, mod.Description));
                        Progress.Journal($"Found hardware: {mod.Name}. It can be installed at a workshop.");
                    }

                    // Story sites grant knowledge on first search (hook 4).
                    var storySite = StoryPlaces.For(site.Id);
                    if (storySite?.GrantsOnSearch is string grantId && !Progress.Knows(grantId))
                    {
                        Progress.Learn(new Knowledge(KnowledgeKind.Rumour, grantId,
                            $"Found at {site.Name}", storySite.Reads ?? "Something worth knowing."));
                        Progress.Journal($"Found something at {site.Name}.");
                        Notice?.Invoke("The search moves");
                    }

                    Progress.Journal($"Searched {site.Name}: {found}.");
                    Notice?.Invoke(found);

                    // D-074: the district hears about salvage runs.
                    int witnesses = WorldMap.PopulationNear(site.Position.X, site.Position.Y);
                    Progress.RecordDeed(DjDeedKind.Salvaged, site.Name, 1, witnesses);
                });
                return true;
            }));
    }

    // --------------------------------------------------------------- refit (D-011)

    private void AddRefit(Site site)
    {
        // Install: show one action per module in the bag that the player can afford.
        foreach (var mod in Loadout.All)
        {
            if (!Loadout.InBag(mod.Id)) continue;
            bool canPay = Progress.Amount(Stock.Parts) >= mod.PartsCost;
            _actions.Add(new SiteAction(
                $"Install {mod.Name}  -  {mod.PartsCost} parts",
                canPay ? mod.Description : "You do not have the parts.",
                () =>
                {
                    if (!canPay) return false;
                    Progress.Spend(Stock.Parts, mod.PartsCost);
                    BeginBusy($"Installing {mod.Name}", 8 + mod.Mass * 0.3, () =>
                    {
                        var def = Loadout.Install(mod.Id);
                        if (def is null) return;

                        // Physics: mass, drag, fuel capacity
                        _heli.Sim.Airframe.Mass.Add($"mod_{def.Id}", def.Position, def.Mass);
                        _heli.Sim.Airframe.DragArea += def.DragDelta;
                        _heli.Sim.Airframe.FuelCapacity += def.FuelCapacityDelta;
                        _heli.Sim.InvalidateMass();

                        Progress.Journal($"Installed {def.Name} at {site.Name}.");
                        Notice?.Invoke($"{def.Name} fitted");
                        ModuleInstalled?.Invoke(def);
                    });
                    return true;
                }, canPay));
        }

        // Remove: show one action per installed module.
        foreach (var mod in Loadout.All)
        {
            if (!Loadout.IsInstalled(mod.Id)) continue;
            _actions.Add(new SiteAction(
                $"Remove {mod.Name}",
                "Back in the bag.",
                () =>
                {
                    BeginBusy($"Removing {mod.Name}", 6, () =>
                    {
                        var def = Loadout.Remove(mod.Id);
                        if (def is null) return;

                        // Reverse the physics
                        _heli.Sim.Airframe.Mass.Remove($"mod_{def.Id}");
                        _heli.Sim.Airframe.DragArea -= def.DragDelta;
                        _heli.Sim.Airframe.FuelCapacity -= def.FuelCapacityDelta;
                        _heli.Sim.InvalidateMass();

                        Progress.Journal($"Removed {def.Name} at {site.Name}.");
                        Notice?.Invoke($"{def.Name} removed");
                        ModuleRemoved?.Invoke(def);
                    });
                    return true;
                }));
        }
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
                    // Every other mast is a carrier with nothing behind it. One is not,
                    // and the log entry has to say so, or the player writes the Upland
                    // Service down as another dead relay and never tunes back.
                    bool manned = UplandService.Transmitter?.Id == site.Id;
                    Progress.Learn(new Knowledge(KnowledgeKind.Frequency, id,
                        $"{freq} - {site.Name}",
                        manned
                            ? $"A voice. {RadioDj.HostName}, reading out lost property to a " +
                              "district that may or may not be listening."
                            : "Carrier present. Nobody answering, but it is on."));
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

    // --------------------------------------------------------------- contracts

    private void AddContractBoard(Site site)
    {
        int cycle = (int)(Progress.Clock / BoardRefreshInterval);
        if (!_boards.TryGetValue(site.Id, out var cached) || cached.cycle != cycle)
        {
            // D-074: any contract from the old board that was never accepted is a decline.
            if (cached.board is not null)
            {
                foreach (var old in cached.board)
                {
                    if (old.Accepted) continue;
                    int pop = WorldMap.PopulationAt(site);
                    Progress.RecordDeed(DjDeedKind.Declined, site.Name, 0, pop);
                    break;  // one deed per board refresh, not one per contract
                }
            }

            var stubs = BuildSiteStubs();
            var board = ContractBoard.Generate(
                site.Id, site.Name, site.Position.X, site.Position.Y,
                stubs, Progress, Progress.Clock, cycle, Alert);

            // Story-specific contracts from named NPCs (D-092).
            string? npcId = StoryPlaces.For(site.Id)?.NpcId;
            var story = ContractBoard.StoryContract(
                npcId, site.Id, site.Name, site.Position.X, site.Position.Y,
                stubs, Progress, Loadout, Progress.Clock);
            if (story is not null) board.Add(story);

            _boards[site.Id] = (board, cycle);
            cached = (board, cycle);
        }

        foreach (var contract in cached.board)
        {
            if (contract.Accepted) continue;
            _actions.Add(new SiteAction(
                contract.Title,
                contract.Brief,
                () =>
                {
                    Progress.AcceptContract(contract);
                    Notice?.Invoke($"Job: {contract.Title}");
                    return true;
                }));
        }
    }

    private List<SiteStub> BuildSiteStubs()
    {
        var stubs = new List<SiteStub>();
        foreach (var s in WorldMap.Sites)
            stubs.Add(new SiteStub(s.Id, s.Name, (SiteKindTag)(int)s.Kind,
                s.Position.X, s.Position.Y, s.Tier, (int)s.Region));
        return stubs;
    }

    private double _lastSearchCheck;

    private void CheckSearchThread()
    {
        // Check every few seconds of game time, not every frame.
        // Beat 5 ("The voice") needs the 06:40 window, so check frequently enough
        // that the 40-minute window is not missed between checks.
        if (Progress.Clock - _lastSearchCheck < 30) return;
        _lastSearchCheck = Progress.Clock;

        var ctx = BuildThreadContext();
        var beat = Progress.Search.TryAdvance(Progress, ctx);
        if (beat is null) return;

        // Journal the clue.
        Progress.Journal(beat.Journal);

        // Learn the knowledge.
        if (beat.KnowledgeId is not null)
        {
            Progress.Learn(new Knowledge(
                KnowledgeKind.Rumour,
                beat.KnowledgeId,
                beat.KnowledgeLabel ?? "",
                beat.KnowledgeDetail ?? ""));
        }

        // If the beat's carrier is the radio strip, push the text there (D-085).
        if (beat.RadioText is not null && RadioStrip is not null)
        {
            RadioStrip.Enqueue("06:40", beat.RadioText, RadioMessageKind.Broadcast);
        }

        Notice?.Invoke($"The search: {beat.Name}");
        GD.Print($"[search] Stage {Progress.Search.Stage}: {beat.Name}");
    }

    private double _lastContractCheck;

    private void CheckContractCompletion()
    {
        if (Progress.Clock - _lastContractCheck < 30) return;
        _lastContractCheck = Progress.Clock;

        var completed = Progress.CheckContracts();
        foreach (var c in completed)
        {
            Notice?.Invoke($"Done: {c.Title}");
            GD.Print($"[contract] Completed: {c.Title}");

            // D-074: the district hears about deliveries.
            var target = WorldMap.SiteById(c.TargetSiteId);
            if (target is not null)
            {
                int witnesses = WorldMap.PopulationAt(target);
                Progress.RecordDeed(DjDeedKind.Delivered, target.Name,
                    c.Kind == ContractKind.Deliver ? c.CargoAmount : 1,
                    witnesses);
            }
        }

        // Prune old completed contracts after 1 game-day.
        Progress.PruneContracts(86400);
    }

    // ------------------------------------------------------------------ directed calls

    private int _prevRegionOrdinal = -1;
    private double _lastDirectedCheck;

    /// <summary>
    /// Directed radio calls: "Somebody raises you, because you tuned their mast
    /// and they have been listening since. Fires on entering a region, once."
    /// (story.md §4.3, carrier type 2)
    /// </summary>
    private void CheckDirectedCalls()
    {
        if (RadioStrip is null) return;

        // Check every few seconds of game time, not every frame.
        if (Progress.Clock - _lastDirectedCheck < 30) return;
        _lastDirectedCheck = Progress.Clock;

        // Must be airborne — directed calls are radio messages you hear in flight.
        if (_landing.OnGround) return;

        var pos = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        var region = WorldMap.RegionAt(pos);
        int ord = (int)region.Kind;

        if (ord == _prevRegionOrdinal) return;
        _prevRegionOrdinal = ord;

        // Check whether any relay in this region has been tuned.
        bool hasTunedRelay = false;
        foreach (var site in WorldMap.Sites)
        {
            if (site.Kind != SiteKind.Relay || site.Region != region.Kind) continue;
            if (Progress.Knows($"freq.{site.Id}"))
            {
                hasTunedRelay = true;
                break;
            }
        }
        if (!hasTunedRelay) return;

        var msg = Progress.DirectedCalls.TryFire(ord);
        if (msg is not null)
        {
            RadioStrip.Enqueue(msg);
            GD.Print($"[radio] Directed call from {msg.Speaker}");
        }
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

    // ----------------------------------------------------------- save / load

    /// <summary>Read the NPC registry for saving.</summary>
    public IReadOnlyDictionary<int, (NpcMind npc, DialogueBank bank)> AllNpcs => _npcs;

    /// <summary>Replace progress and loadout from a save file.</summary>
    public void RestoreState(Progress progress, Loadout loadout)
    {
        // Progress: replace the backing fields through the public object.
        // Clock, stock, knowledge, sites, journal — all handled by the caller
        // via RestoreXxx methods on Progress already. We just need to swap the
        // reference used for the clock tick, mass sync, and site record access.
        //
        // But Progress and Loadout are readonly properties (no setter). We need
        // to mutate the existing objects in-place instead of replacing them.
        //
        // Copy all state from the provided progress into our Progress.
        Progress.Clock = progress.Clock;

        // Stock: clear existing and copy over.
        foreach (Stock s in System.Enum.GetValues<Stock>())
            Progress.RestoreStock(s, progress.Amount(s));

        // Knowledge
        foreach (var k in progress.Known)
            Progress.RestoreKnowledge(k);

        // Sites
        foreach (var (id, rec) in progress.AllSites)
            Progress.RestoreSite(id, new SiteRecord
            {
                Visited = rec.Visited, Surveyed = rec.Surveyed, Cleared = rec.Cleared,
                FuelRemaining = rec.FuelRemaining, SalvageRemaining = rec.SalvageRemaining,
                LastVisitedAt = rec.LastVisitedAt, VisitCount = rec.VisitCount,
            });

        // Journal
        Progress.RestoreJournal(progress.Journal_);

        // Deeds (D-074): what the district is still talking about.
        foreach (var d in progress.Deeds)
            Progress.RestoreDeed(d);

        // Contracts
        Progress.RestoreContracts(progress.Contracts);
        Progress.ContractsCompleted = progress.ContractsCompleted;
        Progress.Search.Stage = progress.Search.Stage;

        // Loadout
        Loadout.Restore(loadout.Installed, loadout.Bag);

        _lastCarried = -1; // force mass resync
    }

    /// <summary>Replace the NPC registry from a save file.</summary>
    public void RestoreNpcs(Dictionary<int, (NpcMind npc, DialogueBank bank)> npcs)
    {
        _npcs.Clear();
        foreach (var (siteId, entry) in npcs)
            _npcs[siteId] = entry;
    }

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

        // Named story characters get their authored voice — 430 lines between nine people.
        var storySite = StoryPlaces.For(site.Id);
        if (storySite?.NpcId is string npcId)
        {
            var named = DialogueCorpus.Named(npcId);
            if (named is not null)
            {
                (npc, bank) = named.Value;
                _npcs[site.Id] = (npc, bank);
                return (npc, bank);
            }
        }

        // Generic settlers, contextualised to where they live so an Ashfield fuel cache
        // does not sound like a Basin farmstead.
        var region = (DialogueCorpus.RegionTag)(int)site.Region;
        var kind = (SiteKindTag)(int)site.Kind;
        npc = DialogueCorpus.Settler(site.Id, site.Name, region, kind);
        bank = DialogueCorpus.SettlerLines(region, kind);

        _npcs[site.Id] = (npc, bank);
        return (npc, bank);
    }

    private TalkContext BuildTalkContext(Site site, NpcMind npc)
    {
        var sim = _heli.Sim;
        var worst = sim.Damage.Worst();
        double fuelFrac = sim.Fuel / Math.Max(sim.Airframe.FuelCapacity, 1);

        var ctx = new TalkContext
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
            ArrivedAtNight = SceneMood.SunNow.IsNight,
            Standing = npc.Standing,
        };

        // Passenger and sling load state — lets dialogue gate on who is aboard and what is
        // on the hook (D-093).
        ctx.PassengerId = Progress.PassengerAboard;
        ctx.SlingLoadId = Progress.SlingLoadId;

        // Knowledge ids — unlocks every Knows()/Unknown() gate in the corpus (story.md §7.3).
        foreach (var k in Progress.AllKnown.Keys)
            ctx.KnownIds.Add(k);

        // Installed modules — lets dialogue gate on what is bolted to the aircraft.
        foreach (string id in Loadout.Installed)
        {
            ctx.FittedIds.Add(id);
            if (Loadout.Catalog.TryGetValue(id, out var def))
                ctx.VisibleFittings.Add(def.Name);
        }

        return ctx;
    }

    /// <summary>
    /// Execute a dialogue reward (D-089). Called by <see cref="DialoguePanel"/> when a
    /// line with a <see cref="DialogueReward"/> is delivered.
    ///
    /// Module rewards follow the same path as salvage discovery (D-011): the module goes
    /// into the bag (not installed), the player learns a Schematic, and the journal notes
    /// it. Knowledge rewards call <see cref="Progress.Learn"/> directly. Both paths are
    /// idempotent — a duplicate find or learn is a no-op.
    /// </summary>
    public void DeliverReward(DialogueReward reward)
    {
        switch (reward.Kind)
        {
            case DialogueRewardKind.Module:
                if (Loadout.Find(reward.Id) && Loadout.Catalog.TryGetValue(reward.Id, out var mod))
                {
                    Progress.Learn(new Knowledge(KnowledgeKind.Schematic,
                        $"module.{reward.Id}", mod.Name, mod.Description));
                    Progress.Journal($"Received: {mod.Name}.");
                    Notice?.Invoke($"Received: {mod.Name}");
                    GD.Print($"[reward] module: {reward.Id} ({mod.Name})");
                }
                break;

            case DialogueRewardKind.Knowledge:
                if (Progress.Learn(new Knowledge(reward.KnowledgeKind, reward.Id,
                        reward.Label ?? reward.Id, reward.Detail ?? "")))
                {
                    Notice?.Invoke($"Learned: {reward.Label ?? reward.Id}");
                    GD.Print($"[reward] knowledge: {reward.Id}");
                }
                break;

            case DialogueRewardKind.Passenger:
                if (Progress.PassengerAboard != reward.Id)
                {
                    Progress.PassengerAboard = reward.Id;
                    var pax = Passenger.ById(reward.Id);
                    if (pax is not null)
                    {
                        Progress.Journal($"{pax.Name} is aboard.");
                        Notice?.Invoke($"{pax.Name} is aboard");
                    }
                    PassengerBoarded?.Invoke(reward.Id);
                    GD.Print($"[reward] passenger: {reward.Id}");
                }
                break;

            case DialogueRewardKind.SlingLoad:
                if (Progress.SlingLoadId != reward.Id && Loadout.IsInstalled("hook"))
                {
                    Progress.SlingLoadId = reward.Id;
                    Progress.Journal("Load on the hook.");
                    Notice?.Invoke("Load attached");
                    SlingLoadAttached?.Invoke(reward.Id);
                    GD.Print($"[reward] sling load: {reward.Id}");
                }
                break;
        }
    }

    private ThreadContext BuildThreadContext()
    {
        // Resolve which story-role sites the player has visited, using the role ids
        // from StoryPlaces.Specs. This bridges the game→sim boundary: the sim layer
        // sees only string ids, never StoryPlaces or RegionKind.
        var visited = new HashSet<string>();
        foreach (var spec in StoryPlaces.Specs)
        {
            int sid = StoryPlaces.SiteId(spec.Role);
            if (sid >= 0 && Progress.HasVisited(sid))
                visited.Add(spec.Id);
        }

        return new ThreadContext(
            gameClock: Progress.Clock,
            airborne: !_landing.OnGround,
            parkedSiteId: Parked?.Id ?? -1,
            isNight: SceneMood.SunNow.IsNight,
            visitedRoles: visited);
    }
}
