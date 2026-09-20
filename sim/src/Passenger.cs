namespace Rotorwash.Sim;

/// <summary>
/// A person in the aircraft. story.md §7.6: "There are no passengers" — the minimum
/// version is a MassItem at the co-pilot position, a dialogue bank that opens on
/// landing, and a flag in Progress. Her calling the torque in flight is the radio
/// strip in a different colour.
///
/// A passenger is NOT a module: they are a person who agreed to fly with you. Their
/// mass is real mass at a real seat position, felt in the hover and in the trim.
/// </summary>
public sealed record Passenger(string Id, string Name, double Mass, Vec3 Position)
{
    /// <summary>Sera Wray in the right seat, 68 kg. story.md §7.6.</summary>
    public static readonly Passenger Wray = new("wray", "Wray",
        68.0, new Vec3(2.00, 0.62, -0.25));

    /// <summary>Look up a passenger by id, or null if unknown.</summary>
    public static Passenger? ById(string id) => id switch
    {
        "wray" => Wray,
        _ => null,
    };
}
