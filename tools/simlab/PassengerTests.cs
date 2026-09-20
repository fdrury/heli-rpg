using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for the passenger system (D-090): Sera Wray boards the aircraft,
/// adds mass, and calls out torque/altitude/fuel during flight.
/// </summary>
public static class PassengerTests
{
    /// <summary>Passenger.Wray has the right mass and position for the right seat.</summary>
    public static string? WrayMass()
    {
        Console.WriteLine("  Wray's mass and position");

        var pax = Passenger.Wray;
        if (pax.Id != "wray") return $"id: {pax.Id}";
        if (pax.Name != "Wray") return $"name: {pax.Name}";
        if (Math.Abs(pax.Mass - 68.0) > 0.01) return $"mass: {pax.Mass}";

        // Right seat mirrors the pilot's left seat (Y is positive = right in FRD).
        // Pilot is at (2.00, -0.62, -0.25); Wray should be at (2.00, +0.62, -0.25).
        if (Math.Abs(pax.Position.X - 2.00) > 0.01) return $"position.X: {pax.Position.X}";
        if (Math.Abs(pax.Position.Y - 0.62) > 0.01) return $"position.Y: {pax.Position.Y}";
        if (Math.Abs(pax.Position.Z - (-0.25)) > 0.01) return $"position.Z: {pax.Position.Z}";

        Console.WriteLine($"  {pax.Name}: {pax.Mass} kg at ({pax.Position.X:F2}, {pax.Position.Y:F2}, {pax.Position.Z:F2})");

        // ById lookup
        if (Passenger.ById("wray") != pax) return "ById(wray) mismatch";
        if (Passenger.ById("nobody") is not null) return "ById(nobody) should be null";

        return null;
    }

    /// <summary>Adding a passenger shifts the CG measurably to the right.</summary>
    public static string? CgShift()
    {
        Console.WriteLine("  CG shift with passenger aboard");

        var af = Airframe.Workhorse();
        var cgBefore = af.Mass.CentreOfGravity;
        double massBefore = af.Mass.TotalMass;

        // Add Wray
        var pax = Passenger.Wray;
        af.Mass.Add("passenger", pax.Position, pax.Mass);

        var cgAfter = af.Mass.CentreOfGravity;
        double massAfter = af.Mass.TotalMass;

        Console.WriteLine($"  before: {massBefore:F0} kg, CG Y={cgBefore.Y:F4}");
        Console.WriteLine($"  after:  {massAfter:F0} kg, CG Y={cgAfter.Y:F4}");

        // Mass should increase by exactly 68 kg.
        double delta = massAfter - massBefore;
        if (Math.Abs(delta - 68.0) > 0.01)
            return $"mass delta {delta:F1}, expected 68.0";

        // CG should shift right (positive Y in FRD). The pilot at Y=-0.62 pulls
        // left; Wray at Y=+0.62 pulls right. With both aboard the lateral CG
        // should be closer to centre than with the pilot alone.
        double lateralShift = cgAfter.Y - cgBefore.Y;
        Console.WriteLine($"  lateral CG shift: {lateralShift:F4} m (positive = rightward)");
        if (lateralShift <= 0)
            return $"CG should shift rightward, got {lateralShift:F4}";

        return null;
    }

    /// <summary>Copilot calls torque when it exceeds 85%.</summary>
    public static string? CalloutTorque()
    {
        Console.WriteLine("  copilot callout: torque");

        var cc = new CopilotCallouts("Wray");

        // Advance past the minimum interval with no torque — should be silent.
        var quiet = new CopilotState(torque: 60, nr: 100, altAgl: 300, vs: 0, fuel: 0.8, threat: false);
        RadioMessage? msg = null;
        for (int i = 0; i < 100; i++)
            msg = cc.Update(0.1, quiet);
        if (msg is not null) return "should be quiet at 60% torque";

        // Now high torque after the minimum interval has passed.
        var high = new CopilotState(torque: 90, nr: 100, altAgl: 300, vs: 0, fuel: 0.8, threat: false);
        msg = cc.Update(0.1, high);
        if (msg is null) return "should call torque at 90%";
        if (msg.Kind != RadioMessageKind.Copilot) return $"wrong kind: {msg.Kind}";
        if (!msg.Text.Contains("Torque")) return $"expected torque call, got: {msg.Text}";
        if (msg.Speaker != "Wray") return $"wrong speaker: {msg.Speaker}";
        Console.WriteLine($"  \"{msg.Text}\"");

        return null;
    }

    /// <summary>Copilot calls Nr when the rotor droops below 95%.</summary>
    public static string? CalloutNr()
    {
        Console.WriteLine("  copilot callout: Nr");

        var cc = new CopilotCallouts("Wray");

        // Advance past minimum interval.
        var normal = new CopilotState(torque: 50, nr: 100, altAgl: 300, vs: 0, fuel: 0.8, threat: false);
        for (int i = 0; i < 100; i++) cc.Update(0.1, normal);

        // Low Nr
        var drooping = new CopilotState(torque: 50, nr: 92, altAgl: 300, vs: 0, fuel: 0.8, threat: false);
        var msg = cc.Update(0.1, drooping);
        if (msg is null) return "should call Nr at 92%";
        if (!msg.Text.Contains("Nr")) return $"expected Nr call, got: {msg.Text}";
        Console.WriteLine($"  \"{msg.Text}\"");

        return null;
    }

    /// <summary>Copilot calls altitude when descending below 120 ft.</summary>
    public static string? CalloutAltitude()
    {
        Console.WriteLine("  copilot callout: altitude");

        var cc = new CopilotCallouts("Wray");

        // Advance past minimum interval, cruising.
        var cruise = new CopilotState(torque: 50, nr: 100, altAgl: 300, vs: 0, fuel: 0.8, threat: false);
        for (int i = 0; i < 100; i++) cc.Update(0.1, cruise);

        // Low, descending — should call altitude.
        var low = new CopilotState(torque: 50, nr: 100, altAgl: 25, vs: -2.0, fuel: 0.8, threat: false);
        var msg = cc.Update(0.1, low);
        if (msg is null) return "should call altitude at 25m descending";
        if (!msg.Text.Contains("feet")) return $"expected altitude call, got: {msg.Text}";
        Console.WriteLine($"  \"{msg.Text}\"");

        // Low but NOT descending — should be quiet.
        var cc2 = new CopilotCallouts("Wray");
        for (int i = 0; i < 100; i++) cc2.Update(0.1, cruise);
        var lowLevel = new CopilotState(torque: 50, nr: 100, altAgl: 25, vs: 0, fuel: 0.8, threat: false);
        msg = cc2.Update(0.1, lowLevel);
        if (msg is not null) return "should not call altitude when not descending";

        return null;
    }

    /// <summary>Copilot calls fuel at 25% and 10%, once each.</summary>
    public static string? CalloutFuel()
    {
        Console.WriteLine("  copilot callout: fuel");

        var cc = new CopilotCallouts("Wray");

        // Advance past minimum interval with good fuel.
        var good = new CopilotState(torque: 50, nr: 100, altAgl: 300, vs: 0, fuel: 0.5, threat: false);
        for (int i = 0; i < 100; i++) cc.Update(0.1, good);

        // Drop to 20% — should call quarter tank.
        var low = new CopilotState(torque: 50, nr: 100, altAgl: 300, vs: 0, fuel: 0.20, threat: false);
        var msg = cc.Update(0.1, low);
        if (msg is null) return "should call fuel at 20%";
        if (!msg.Text.Contains("Quarter")) return $"expected quarter tank call, got: {msg.Text}";
        Console.WriteLine($"  \"{msg.Text}\"");

        // Advance past cooldowns again.
        for (int i = 0; i < 700; i++) cc.Update(0.1, low);

        // Same fuel level — should NOT repeat (one-shot).
        msg = cc.Update(0.1, low);
        if (msg is not null && msg.Text.Contains("Quarter"))
            return "quarter tank warning should not repeat";

        return null;
    }

    /// <summary>Passenger state survives a save/load round trip.</summary>
    public static string? SaveRoundTrip()
    {
        Console.WriteLine("  passenger save/load round trip");

        var p = Progress.NewGame();
        p.PassengerAboard = "wray";

        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();
        if (p2.PassengerAboard != "wray")
            return $"passenger lost: got '{p2.PassengerAboard}'";

        Console.WriteLine($"  round trip: '{p2.PassengerAboard}' ✓");

        // Null passenger also survives.
        var p3 = Progress.NewGame();
        // p3.PassengerAboard is null by default
        var save2 = new SaveData();
        save2.CaptureProgress(p3);
        string json2 = save2.ToJson();
        var loaded2 = SaveData.FromJson(json2)!;
        var p4 = loaded2.ApplyProgress();
        if (p4.PassengerAboard is not null)
            return $"null passenger should survive, got '{p4.PassengerAboard}'";

        return null;
    }

    /// <summary>The boarding dialogue line exists, has the right reward, and fires under correct conditions.</summary>
    public static string? BoardingDialogue()
    {
        Console.WriteLine("  boarding dialogue line and reward");

        var pair = DialogueCorpus.Named("wray");
        if (pair is null) return "wray has no bank";

        var (npc, bank) = pair.Value;

        // Find the boarding line.
        DialogueLine? boardLine = null;
        foreach (var line in bank.Lines)
            if (line.Id == "wray.board") { boardLine = line; break; }
        if (boardLine is null) return "wray.board line not found";

        // Check reward.
        if (boardLine.Reward is null) return "wray.board has no reward";
        if (boardLine.Reward.Kind != DialogueRewardKind.Passenger)
            return $"reward kind is {boardLine.Reward.Kind}, expected Passenger";
        if (boardLine.Reward.Id != "wray")
            return $"reward id is '{boardLine.Reward.Id}', expected 'wray'";

        Console.WriteLine($"  wray.board: reward=Passenger(wray) ✓");

        // Verify the line fires under the right conditions.
        var ctx = new TalkContext
        {
            Now = 500000,
            SiteId = "ashmount",
            SiteName = "Ashmount",
            PreviousMeetings = 3,
            Standing = 0.6,
            FuelFraction = 0.5,
            WorstComponentHealth = 0.8,
        };
        ctx.KnownIds.Add(DialogueCorpus.Knows.Window);
        // Add all prerequisite knowledge
        foreach (var k in DialogueCorpus.Knows.All)
            ctx.KnownIds.Add(k);

        var selected = bank.Select(ctx, "talk", 500000);
        Console.WriteLine($"  selected with window+standing: {selected?.Id ?? "(nothing)"}");
        if (selected?.Id != "wray.board")
            return $"expected wray.board, got {selected?.Id}";

        // Without window knowledge, the boarding line should NOT fire.
        var noWindow = new TalkContext
        {
            Now = 500000,
            SiteId = "ashmount",
            SiteName = "Ashmount",
            PreviousMeetings = 3,
            Standing = 0.6,
            FuelFraction = 0.5,
            WorstComponentHealth = 0.8,
        };
        // Do NOT add Window knowledge
        bank.ForgetUsage();
        var without = bank.Select(noWindow, "talk", 500000);
        Console.WriteLine($"  selected without window: {without?.Id ?? "(nothing)"}");
        if (without?.Id == "wray.board")
            return "wray.board fired without window knowledge — gate is broken";

        return null;
    }
}
