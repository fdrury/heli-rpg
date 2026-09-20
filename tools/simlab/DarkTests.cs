using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for story.md §6.3 "One place goes dark": post-ending ash expansion,
/// citadel guns silent, two Act II sites permanently fogged.
/// </summary>
public static class DarkTests
{
    /// <summary>FogOfWar.ContaminateCircle marks cells and counts correctly.</summary>
    public static string? FogContamination()
    {
        var fog = new FogOfWar();

        if (fog.ContaminatedCount != 0)
            return $"initial contaminated count should be 0, got {fog.ContaminatedCount}";

        // Contaminate a circle at origin (north=0, east=0) with 500 m radius.
        fog.ContaminateCircle(0, 0, 500f);

        if (fog.ContaminatedCount == 0)
            return "contamination at origin produced zero cells";

        // The centre cell must be contaminated.
        int cx = FogOfWar.WorldToCell(0);
        int cy = FogOfWar.WorldToCell(0);
        if (!fog.IsContaminated(cx, cy))
            return "centre cell not contaminated";

        // A cell far away must not be contaminated.
        int farX = FogOfWar.WorldToCell(5000);
        if (fog.IsContaminated(farX, cy))
            return "far cell should not be contaminated";

        Console.WriteLine($"  {fog.ContaminatedCount} cells contaminated at origin (r=500m)");
        return null;
    }

    /// <summary>Contamination persists through serialisation.</summary>
    public static string? FogSaveRoundTrip()
    {
        var fog = new FogOfWar();
        fog.ContaminateCircle(1000, 2000, 300f);

        int beforeCount = fog.ContaminatedCount;
        if (beforeCount == 0)
            return "contamination produced zero cells";

        byte[]? packed = fog.ContaminatedToBytes();
        if (packed is null)
            return "ContaminatedToBytes returned null with non-zero contamination";

        // Restore into a fresh instance.
        var fog2 = new FogOfWar();
        fog2.ContaminatedFromBytes(packed);

        if (fog2.ContaminatedCount != beforeCount)
            return $"count mismatch: {beforeCount} vs {fog2.ContaminatedCount}";

        // Spot-check a contaminated cell from the first grid.
        int cx = FogOfWar.WorldToCell(2000);
        int cy = FogOfWar.WorldToCell(-1000);
        if (fog.IsContaminated(cx, cy) != fog2.IsContaminated(cx, cy))
            return "specific cell mismatch after round-trip";

        Console.WriteLine($"  {beforeCount} contaminated cells survived save/load");
        return null;
    }

    /// <summary>Null bytes clears contamination (pre-existing save format).</summary>
    public static string? FogNullRestore()
    {
        var fog = new FogOfWar();
        fog.ContaminateCircle(0, 0, 200f);
        if (fog.ContaminatedCount == 0)
            return "contamination should be non-zero";

        fog.ContaminatedFromBytes(null);
        if (fog.ContaminatedCount != 0)
            return $"null restore should clear contamination, got {fog.ContaminatedCount}";

        Console.WriteLine("  null bytes clears contamination correctly");
        return null;
    }

    /// <summary>PlaceGoneDark and ContaminatedSites round-trip through SaveData.</summary>
    public static string? ProgressSaveRoundTrip()
    {
        var p = Progress.NewGame();

        // Simulate search completion + dark trigger.
        p.Search.Stage = SearchThread.Beats.Length;
        p.PlaceGoneDark = true;
        p.AddContaminatedSite(42);
        p.AddContaminatedSite(107);

        // Capture.
        var save = new SaveData();
        save.CaptureProgress(p);

        if (!save.PlaceGoneDark)
            return "PlaceGoneDark not captured";
        if (save.ContaminatedSites.Count != 2)
            return $"expected 2 contaminated sites, got {save.ContaminatedSites.Count}";

        // Serialise and deserialise.
        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        if (!p2.PlaceGoneDark)
            return "PlaceGoneDark not restored";
        if (!p2.IsSiteContaminated(42))
            return "site 42 not contaminated after load";
        if (!p2.IsSiteContaminated(107))
            return "site 107 not contaminated after load";
        if (p2.IsSiteContaminated(1))
            return "site 1 should not be contaminated";
        if (!p2.Search.Complete)
            return "search should still be complete after load";

        Console.WriteLine("  PlaceGoneDark + ContaminatedSites survived save/load");
        return null;
    }

    /// <summary>Old saves (no PlaceGoneDark field) load correctly as false.</summary>
    public static string? OldSaveCompat()
    {
        var p = Progress.NewGame();

        // Capture a save WITHOUT the dark fields set.
        var save = new SaveData();
        save.CaptureProgress(p);

        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        if (p2.PlaceGoneDark)
            return "PlaceGoneDark should be false on old save";
        if (p2.ContaminatedSites.Count != 0)
            return "contaminated sites should be empty on old save";

        Console.WriteLine("  old save loads with no contamination (correct)");
        return null;
    }
}
