using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Round-trip tests for the save/load system. Verify that every piece of state
/// survives serialisation to JSON and back without loss or corruption.
/// </summary>
public static class SaveTests
{
    /// <summary>Progress: clock, stock, knowledge, sites, journal all survive a round trip.</summary>
    public static string? ProgressRoundTrip()
    {
        var p = Progress.NewGame();
        p.Clock = 54321.0;
        p.Add(Stock.Scrap, 42);
        p.Add(Stock.Fuel, 3);
        p.Add(Stock.Medical, 2);
        p.Add(Stock.Ammunition, 50);
        p.Learn(new Knowledge(KnowledgeKind.Chart, "chart.basin", "The Basin", "Lowland with cover."));
        p.Learn(new Knowledge(KnowledgeKind.Frequency, "freq.12", "122.45 MHz", "Carrier present."));
        p.Learn(new Knowledge(KnowledgeKind.Contact, "contact.mattie", "Mattie", "At Millbrook."));
        p.Journal("Tested the round trip.");

        var site = p.Record(7);
        site.Visited = true;
        site.Surveyed = true;
        site.FuelRemaining = 340;
        site.SalvageRemaining = 2;
        site.LastVisitedAt = 50000;
        site.VisitCount = 3;
        p.MarkVisited(7); // updates VisitCount and LastVisitedAt

        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        if (Math.Abs(p2.Clock - 54321.0) > 0.01) return $"clock: {p2.Clock}";
        if (Math.Abs(p2.Amount(Stock.Scrap) - (12 + 42)) > 0.01) return $"scrap: {p2.Amount(Stock.Scrap)}";
        if (Math.Abs(p2.Amount(Stock.Fuel) - 3) > 0.01) return $"fuel: {p2.Amount(Stock.Fuel)}";
        if (Math.Abs(p2.Amount(Stock.Medical) - 2) > 0.01) return $"medical: {p2.Amount(Stock.Medical)}";
        if (Math.Abs(p2.Amount(Stock.Ammunition) - 50) > 0.01) return $"ammo: {p2.Amount(Stock.Ammunition)}";

        if (!p2.Knows("chart.basin")) return "missing chart.basin";
        if (!p2.Knows("freq.12")) return "missing freq.12";
        if (!p2.Knows("contact.mattie")) return "missing contact.mattie";
        // NewGame creates a rumour too
        if (!p2.Knows("rumour.the_name")) return "missing initial rumour";
        if (p2.Known.Count != 4) return $"knowledge count: {p2.Known.Count} (expected 4)";

        if (!p2.HasVisited(7)) return "site 7 not visited";
        var s2 = p2.Record(7);
        if (!s2.Surveyed) return "site 7 not surveyed";
        if (Math.Abs(s2.FuelRemaining - 340) > 0.01) return $"fuel remaining: {s2.FuelRemaining}";
        if (s2.SalvageRemaining != 2) return $"salvage remaining: {s2.SalvageRemaining}";
        if (s2.VisitCount < 3) return $"visit count: {s2.VisitCount}";

        if (p2.Journal_.Count < 2) return $"journal count: {p2.Journal_.Count}";
        bool foundTestEntry = false;
        foreach (var line in p2.Journal_)
            if (line.Contains("Tested the round trip")) foundTestEntry = true;
        if (!foundTestEntry) return "journal missing test entry";

        return null;
    }

    /// <summary>Damage: component health and log survive a round trip.</summary>
    public static string? DamageRoundTrip()
    {
        var d = new DamageState();
        d.Apply(Component.Engine, 0.3, DamageCause.Gunfire, "SAM hit");
        d.Apply(Component.TailRotor, 0.6, DamageCause.Fragment, "shrapnel");
        d.Apply(Component.Skids, 0.1, DamageCause.HardLanding, "heavy touchdown");

        var save = new SaveData();
        save.CaptureDamage(d);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var d2 = new DamageState();
        loaded.ApplyDamage(d2);

        if (Math.Abs(d2.Health(Component.Engine) - d.Health(Component.Engine)) > 1e-6)
            return $"engine health: {d2.Health(Component.Engine)} vs {d.Health(Component.Engine)}";
        if (Math.Abs(d2.Health(Component.TailRotor) - d.Health(Component.TailRotor)) > 1e-6)
            return $"tail rotor health: {d2.Health(Component.TailRotor)} vs {d.Health(Component.TailRotor)}";
        if (Math.Abs(d2.Health(Component.Skids) - d.Health(Component.Skids)) > 1e-6)
            return $"skids health: {d2.Health(Component.Skids)} vs {d.Health(Component.Skids)}";
        if (Math.Abs(d2.Health(Component.MainRotor) - 1.0) > 1e-6)
            return "undamaged rotor should be 1.0";

        if (d2.Log.Count != 3) return $"log count: {d2.Log.Count}";
        if (d2.Log[0].Cause != DamageCause.Gunfire) return $"log[0] cause: {d2.Log[0].Cause}";
        if (d2.Log[1].Component != Component.TailRotor) return $"log[1] component: {d2.Log[1].Component}";

        return null;
    }

    /// <summary>Loadout: installed and bag contents survive a round trip.</summary>
    public static string? LoadoutRoundTrip()
    {
        var l = new Loadout();
        l.Find("sas");
        l.Find("rwr");
        l.Find("chaff");
        l.Install("sas");  // moves from bag to installed

        var save = new SaveData();
        save.CaptureLoadout(l);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var l2 = new Loadout();
        loaded.ApplyLoadout(l2);

        if (!l2.IsInstalled("sas")) return "sas should be installed";
        if (l2.IsInstalled("rwr")) return "rwr should not be installed";
        if (!l2.InBag("rwr")) return "rwr should be in bag";
        if (!l2.InBag("chaff")) return "chaff should be in bag";
        if (l2.Has("flares")) return "flares should not be present";

        return null;
    }

    /// <summary>NPC mind: standing, meetings, memories survive a round trip.</summary>
    public static string? NpcRoundTrip()
    {
        var npc = new NpcMind
        {
            Id = "mattie", Name = "Mattie",
            Persona = "Runs things around here.",
            Standing = 0.35, Wants = "Parts",
            LastSeenAt = 40000, Meetings = 5,
        };
        npc.Notice("name", "pilot", 30000);
        npc.Notice("condition", "beat up", 38000);
        npc.Notice("cargo", "medical supplies", 40000);

        var bank = new DialogueBank();
        bank.Add(new DialogueLine { Id = "greet_1", Text = "Hey there.", Tags = { "greeting" } });
        // Simulate selecting a line so _lastUsed gets populated.
        bank.Select(new TalkContext { Now = 45000 }, "greeting", 45000);

        var saved = SaveData.CaptureNpc(42, npc, bank);
        string json = System.Text.Json.JsonSerializer.Serialize(saved);
        var restored = System.Text.Json.JsonSerializer.Deserialize<NpcSave>(json);
        if (restored is null) return "NPC deserialisation returned null";

        var npc2 = SaveData.RestoreNpcMind(restored);

        if (npc2.Id != "mattie") return $"id: {npc2.Id}";
        if (npc2.Name != "Mattie") return $"name: {npc2.Name}";
        if (Math.Abs(npc2.Standing - 0.35) > 1e-6) return $"standing: {npc2.Standing}";
        if (npc2.Meetings != 5) return $"meetings: {npc2.Meetings}";
        if (Math.Abs(npc2.LastSeenAt - 40000) > 1) return $"lastSeenAt: {npc2.LastSeenAt}";

        if (npc2.Memories.Count != 3) return $"memory count: {npc2.Memories.Count}";
        if (npc2.Recall("name") != "pilot") return $"recall name: {npc2.Recall("name")}";
        if (npc2.Recall("condition") != "beat up") return $"recall condition: {npc2.Recall("condition")}";
        if (npc2.Recall("cargo") != "medical supplies") return $"recall cargo: {npc2.Recall("cargo")}";

        if (restored.LineUsage.Count == 0) return "line usage should have at least one entry";
        if (!restored.LineUsage.ContainsKey("greet_1")) return "line usage missing greet_1";

        return null;
    }

    /// <summary>FogOfWar: reveal pattern survives a byte-array round trip.</summary>
    public static string? FogRoundTrip()
    {
        var fog = new FogOfWar();

        // Reveal at a few positions (sim coords: north, east)
        fog.Reveal(0, 0);           // centre
        fog.Reveal(3000, -2000);    // northeast
        fog.Reveal(-5000, 4000);    // southwest

        int before = fog.RevealedCount;
        if (before == 0) return "nothing revealed";

        // Check a known-revealed cell
        int cx0 = FogOfWar.WorldToCell(0);       // centre east
        int cy0 = FogOfWar.WorldToCell(0);       // centre south (negated north=0 → south=0)
        if (!fog.IsRevealed(cx0, cy0)) return "centre cell not revealed";

        // Round trip through bytes
        byte[] bytes = fog.ToBytes();
        if (bytes.Length != (FogOfWar.GridSize * FogOfWar.GridSize + 7) / 8)
            return $"byte array size: {bytes.Length}";

        var fog2 = new FogOfWar();
        fog2.FromBytes(bytes);

        if (fog2.RevealedCount != before)
            return $"count mismatch: {fog2.RevealedCount} vs {before}";

        // Check all cells match
        for (int y = 0; y < FogOfWar.GridSize; y++)
            for (int x = 0; x < FogOfWar.GridSize; x++)
                if (fog.IsRevealed(x, y) != fog2.IsRevealed(x, y))
                    return $"cell ({x},{y}) mismatch";

        // Round trip through JSON (SaveData)
        var save = new SaveData { FogGrid = fog.ToBytes() };
        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "JSON round trip: null";
        if (loaded.FogGrid is null) return "FogGrid null after JSON";

        var fog3 = new FogOfWar();
        fog3.FromBytes(loaded.FogGrid);
        if (fog3.RevealedCount != before)
            return $"JSON round trip count: {fog3.RevealedCount} vs {before}";

        return null;
    }

    /// <summary>Full SaveData: serialise everything, deserialise, verify key fields.</summary>
    public static string? FullRoundTrip()
    {
        var save = new SaveData
        {
            North = 1234.5,
            East = -5678.9,
            Altitude = 45.0,
            HeadingRad = 1.57,
            Fuel = 250.0,
            PilotHealth = 65,
            SidearmRounds = 3,
            SidearmSpare = 6,
            RotorTimeCharge = 0.45f,
            ChaffRemaining = 12,
            FlaresRemaining = 8,
            DetectedEmitters = { 1, 5, 12, 33 },
        };

        // Populate with real data.
        var progress = Progress.NewGame();
        progress.Clock = 99000;
        progress.Add(Stock.Scrap, 100);
        save.CaptureProgress(progress);

        var damage = new DamageState();
        damage.Apply(Component.Engine, 0.25, DamageCause.Heat, "overheat");
        save.CaptureDamage(damage);

        var loadout = new Loadout();
        loadout.Find("sas"); loadout.Install("sas");
        loadout.Find("rwr");
        save.CaptureLoadout(loadout);

        string json = save.ToJson();
        if (string.IsNullOrEmpty(json)) return "JSON is empty";
        if (json.Length < 100) return $"JSON suspiciously short: {json.Length} chars";

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        if (loaded.Version != 1) return $"version: {loaded.Version}";
        if (Math.Abs(loaded.North - 1234.5) > 0.01) return $"north: {loaded.North}";
        if (Math.Abs(loaded.East - -5678.9) > 0.01) return $"east: {loaded.East}";
        if (Math.Abs(loaded.Fuel - 250.0) > 0.01) return $"fuel: {loaded.Fuel}";
        if (Math.Abs(loaded.PilotHealth - 65) > 0.1) return $"pilotHealth: {loaded.PilotHealth}";
        if (loaded.SidearmRounds != 3) return $"sidearmRounds: {loaded.SidearmRounds}";
        if (loaded.ChaffRemaining != 12) return $"chaff: {loaded.ChaffRemaining}";
        if (loaded.DetectedEmitters.Count != 4) return $"detected: {loaded.DetectedEmitters.Count}";

        // Verify round trip of nested data.
        var p2 = loaded.ApplyProgress();
        if (Math.Abs(p2.Clock - 99000) > 0.01) return $"progress clock: {p2.Clock}";
        if (Math.Abs(p2.Amount(Stock.Scrap) - 112) > 0.01) return $"scrap: {p2.Amount(Stock.Scrap)}"; // 12 from NewGame + 100

        var d2 = new DamageState();
        loaded.ApplyDamage(d2);
        if (Math.Abs(d2.Health(Component.Engine) - 0.75) > 0.01) return $"engine: {d2.Health(Component.Engine)}";

        var l2 = new Loadout();
        loaded.ApplyLoadout(l2);
        if (!l2.IsInstalled("sas")) return "sas not installed after load";
        if (!l2.InBag("rwr")) return "rwr not in bag after load";

        return null;
    }
}
