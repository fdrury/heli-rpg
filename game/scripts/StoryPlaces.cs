using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rotorwash;

/// <summary>
/// The job a story beat needs some place to do. Not a place: a *role*.
/// </summary>
public enum StoryRole
{
    // Act I - tier 0
    MattieHome,         // The Pan, first Settlement - Mattie Sowerby, the correction
    DossHome,           // Long Acre, first Settlement - Doss Emery, the 06:40 broadcast
    LongAcreMast,       // Long Acre, the relay Mattie gives you the bearing to

    // Act II - tiers 1-2
    NellHome,           // Fenmoor, first Settlement - Nell Abergale, the manifest
    FenmoorField,       // Fenmoor, the departure field - leg 1 closes here
    BelHome,            // The Drowning, first Settlement - Bel Tiernan
    DrowningWreck,      // The Drowning, the drowned ferry - beats 8 and 9
    OsieHome,           // Cold Shoulder, first Settlement - Osie Crane
    ColdShoulderCairn,  // Cold Shoulder, the cairn on the overlook - beat 10
    FerrenOffice,       // Sawtooth Works, first Settlement - Halvard Ferren, the roster
    SawtoothRelay,      // Sawtooth Works, the works' own relay - beat 11, the long way

    // Act III - tier 3
    WrayHome,           // Ashmount, first Settlement - Sera Wray
    JunoPost,           // Ashmount, second Settlement - Juno Kessel, the window
    AshmountTether,     // Ashmount, the aerostat tether field
    SparrowCamp,        // The Scald, its single Settlement - Sparrow
    ScaldMagazine,      // The Scald, the sealed magazine - the blades
}

/// <summary>How a rule picks one site out of the candidates that match region and kind.</summary>
public enum Pick
{
    /// <summary>Build order, which is site id order. "The first Settlement in region R".</summary>
    BuildOrder,
    /// <summary>Lowest natural ground. "The lowest-lying Wreck in the wetland."</summary>
    LowestGround,
    /// <summary>Highest natural ground. A cairn goes on the bare ridge, not the shoulder.</summary>
    HighestGround,
    /// <summary>Furthest from the region centre. "The depot out in the ash."</summary>
    FurthestFromCentre,
    /// <summary>Nearest the region centre. The thing the region is built around.</summary>
    NearestToCentre,
}

/// <summary>What SiteKit should put on the ground that the generic site kind would not.</summary>
public enum StoryProp
{
    None,
    DrownedAircraft,    // a tail fin out of flat water, no scorching, a door open from inside
    Cairn,              // four courses, built by one person, and one course left blank
    SealedMagazine,     // a concrete revetment, ash drifted to the doors, undisturbed
    DepartureField,     // a loading apron with nothing on it
}

/// <summary>
/// One way of finding a site: a region, a kind, an ordering and an ordinal.
///
/// Deliberately data rather than a lambda, so a rule can be printed, diffed and argued
/// with without being run. No rule carries a coordinate and none ever will - the world is
/// seed-generated and a coordinate would be a lie the moment anything upstream moved.
/// </summary>
public sealed record SiteRule(RegionKind Region, SiteKind Kind, Pick Pick, int Ordinal = 1)
{
    public string Text
    {
        get
        {
            string order = Pick switch
            {
                Pick.BuildOrder => "in build order",
                Pick.LowestGround => "by lowest ground",
                Pick.HighestGround => "by highest ground",
                Pick.FurthestFromCentre => "furthest from the centre",
                Pick.NearestToCentre => "nearest the centre",
                _ => "?",
            };
            string nth = Ordinal switch { 1 => "first", 2 => "second", 3 => "third", _ => $"#{Ordinal}" };
            return $"{nth} {Kind} in {RegionName(Region)} {order}";
        }
    }

    public static string RegionName(RegionKind k)
    {
        foreach (Region r in WorldMap.Regions) if (r.Kind == k) return r.Name;
        return k.ToString();
    }
}

/// <summary>
/// A role, and the ordered list of rules that may satisfy it.
///
/// <see cref="Rules"/>[0] is the intent - the sentence story.md actually wrote. The rest
/// are declared, deliberate degradations, in descending order of how well they keep the
/// beat's meaning. They exist because site placement is rejection sampling and fails
/// silently: a region can ask for four settlements and get none. Without a fallback the
/// only options are a dead beat or a hardcoded coordinate, and both are worse than a
/// farmstead standing in for a village with the report saying loudly that it did.
/// </summary>
public sealed record RoleSpec(
    StoryRole Role,
    string Id,
    IReadOnlyList<SiteRule> Rules,
    bool ExpectUnique = false)
{
    public SiteRule Intent => Rules[0];
    public string Rule => Intent.Text + (ExpectUnique ? ", expected unique" : "");
}

/// <summary>
/// The authored contents of one site: what this place *is*, over and above what its
/// SiteKind makes it.
///
/// Small on purpose. This is the hook, not the content - it carries the fields the
/// consumers named in story.md 7.1 actually need (SiteKit.Build, AddSalvage,
/// GetOrCreateNpc) and nothing that would have to be invented twice when the beats land.
/// </summary>
public sealed record StorySite(
    StoryRole Role,
    int SiteId,
    string Alias,               // how people say it out loud: "the wreck out in the wet"
    StoryProp Prop,             // what SiteKit adds on top of the generic build
    string? NpcId = null,       // for GetOrCreateNpc; null means nobody authored lives here
    string? NpcName = null,
    string? GrantsOnSearch = null,  // knowledge id AddSalvage grants on a first search
    string? Reads = null);      // the D-003b 500 m line: what it is from across the valley

/// <summary>One role, resolved - or not, which is the interesting case.</summary>
public sealed record RoleBinding(RoleSpec Spec, Site? Site, SiteRule? Used, int RuleIndex, int Candidates)
{
    public bool Bound => Site is not null;
    /// <summary>Bound, but not by the rule the story actually wanted.</summary>
    public bool Degraded => Site is not null && RuleIndex > 0;
}

/// <summary>A thing wrong with the binding. ERROR stops a beat; WARN bends one.</summary>
public sealed record StoryProblem(bool Fatal, string Text)
{
    public string Severity => Fatal ? "ERROR" : "WARN ";
}

/// <summary>
/// The authored-site layer: the thing that lets the story say "this site is the drowned
/// ferry" about a world nobody authored.
///
/// The gap it closes (story.md 7.1): <see cref="SiteKit.Build"/> switches on
/// <see cref="SiteKind"/> alone and every place name comes out of WorldMap with a seed
/// under it. There has been no way to attach a meaning to a place, so no beat that
/// happens somewhere specific could be written at all.
///
/// Four rules, and they are the whole design:
///
///   1. ROLES RESOLVE BY RULE, NEVER BY COORDINATE. "The lowest-lying Wreck in the
///      wetland", "the single Overlook in Cold Shoulder", "the Depot in the ash furthest
///      from the centre". Change the seed and the story still lands somewhere that means
///      what the beat needs it to mean, because the rule describes the *sense* of a place
///      rather than its position.
///
///   2. TIES BREAK DETERMINISTICALLY. Every ordering sorts on a float key and then on
///      site id ascending, which is unique and stable. Same seed, same binding, every
///      launch. A story that moves between launches is worse than no story.
///
///   3. DEGRADATION IS DECLARED, NOT IMPROVISED. When the generator does not produce the
///      site the intent asked for, the role falls through a written list of lesser rules
///      - and the report says which one caught it and why that is not what was meant.
///
///   4. FAILURE IS LOUD. A role that runs out of rules is a content bug, not a missing
///      feature. It goes to GD.PushError at resolve time and to the top of the
///      story-places table in the world report. Nothing here silently skips.
///
/// Display keeps the procedural name. The player never sees "DrowningWreck"; they see
/// whatever the generator called the place, and the corpus refers to it the way people
/// refer to somewhere they live near - by the alias, not by a proper noun.
/// </summary>
public static class StoryPlaces
{
    // ------------------------------------------------------------------- the rules

    private static SiteRule At(RegionKind r, SiteKind k, Pick p, int ordinal = 1) => new(r, k, p, ordinal);

    private static RoleSpec Role(StoryRole role, string id, bool unique, params SiteRule[] rules) =>
        new(role, id, rules, unique);

    /// <summary>
    /// The role registry. Every rule resolves against today's WorldMap with no new world
    /// data, which is the constraint that made this affordable.
    ///
    /// The fallbacks are not hedging. Measured on seed 40404, the generator delivers no
    /// Settlement at all in Long Acre or Sawtooth Works and no Depot in The Scald - see
    /// the census in the world report - so three of the sixteen intents cannot be
    /// satisfied as written today. The fallback for a person is always "somewhere else
    /// people are": a farmstead, then a workshop, then the works, because a character has
    /// to stand somewhere the player can land and talk to them. The fallback for a thing
    /// is the nearest structural equivalent in the same region, because the region is the
    /// part of the beat that carries the meaning.
    /// </summary>
    public static readonly IReadOnlyList<RoleSpec> Specs = new List<RoleSpec>
    {
        // --- Act I, tier 0 -----------------------------------------------------------
        // Mattie's rule must stay identical to SiteInteraction's: first Settlement in the
        // Basin, in build order. Mattie does not move.
        Role(StoryRole.MattieHome, "place.mattie", false,
            At(RegionKind.Basin, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Basin, SiteKind.Farmstead, Pick.BuildOrder),
            At(RegionKind.Basin, SiteKind.Workshop, Pick.BuildOrder)),

        // Doss farms wheat that mostly does not come up. A farmstead is not a demotion.
        Role(StoryRole.DossHome, "place.doss", false,
            At(RegionKind.Farmland, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Farmland, SiteKind.Farmstead, Pick.BuildOrder),
            At(RegionKind.Farmland, SiteKind.Workshop, Pick.BuildOrder)),

        // The mast Mattie gives you a bearing to: the highest one, because it is the one
        // that can be seen from her pumps, which is why she has a bearing to give.
        Role(StoryRole.LongAcreMast, "place.long_mast", true,
            At(RegionKind.Farmland, SiteKind.Relay, Pick.HighestGround),
            At(RegionKind.Farmland, SiteKind.Overlook, Pick.HighestGround)),

        // --- Act II, tiers 1-2 -------------------------------------------------------
        Role(StoryRole.NellHome, "place.nell", false,
            At(RegionKind.Exurb, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Exurb, SiteKind.Workshop, Pick.BuildOrder),
            At(RegionKind.Exurb, SiteKind.Farmstead, Pick.BuildOrder)),

        // The departure field. The exurb gets one airfield and the ferry left from it.
        Role(StoryRole.FenmoorField, "place.field", true,
            At(RegionKind.Exurb, SiteKind.Airfield, Pick.BuildOrder),
            At(RegionKind.Exurb, SiteKind.Depot, Pick.NearestToCentre)),

        Role(StoryRole.BelHome, "place.bel", false,
            At(RegionKind.Wetland, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Wetland, SiteKind.Farmstead, Pick.BuildOrder),
            At(RegionKind.Wetland, SiteKind.Workshop, Pick.BuildOrder)),

        // "The lowest-lying of the four Wetland wrecks, half in the water, tail boom up."
        // Lowest natural ground is that sentence, written as a sort.
        Role(StoryRole.DrowningWreck, "place.wreck", false,
            At(RegionKind.Wetland, SiteKind.Wreck, Pick.LowestGround)),

        Role(StoryRole.OsieHome, "place.osie", false,
            At(RegionKind.Upland, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Upland, SiteKind.Farmstead, Pick.BuildOrder),
            At(RegionKind.Upland, SiteKind.Workshop, Pick.BuildOrder)),

        // "The region's single Overlook" - the kind that already carries Survey, so the
        // story beat and the chart unlock are the same act of landing.
        Role(StoryRole.ColdShoulderCairn, "place.cairn", true,
            At(RegionKind.Upland, SiteKind.Overlook, Pick.HighestGround),
            At(RegionKind.Upland, SiteKind.Relay, Pick.HighestGround)),

        // Ferren runs a shop floor. If the works has no village, the works itself will do.
        Role(StoryRole.FerrenOffice, "place.ferren", false,
            At(RegionKind.Industrial, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Industrial, SiteKind.Workshop, Pick.BuildOrder),
            At(RegionKind.Industrial, SiteKind.Depot, Pick.NearestToCentre),
            At(RegionKind.Industrial, SiteKind.Airfield, Pick.BuildOrder)),

        // Beat 11 has two routes in; this is the one that does not need Ferren's contract.
        Role(StoryRole.SawtoothRelay, "place.saw_relay", true,
            At(RegionKind.Industrial, SiteKind.Relay, Pick.HighestGround),
            At(RegionKind.Industrial, SiteKind.Overlook, Pick.HighestGround)),

        // --- Act III, tier 3 ---------------------------------------------------------
        Role(StoryRole.WrayHome, "place.wray", false,
            At(RegionKind.City, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.City, SiteKind.Workshop, Pick.BuildOrder),
            At(RegionKind.City, SiteKind.Farmstead, Pick.BuildOrder)),

        // Juno is the second settlement in build order, per story.md 3: she is tether
        // crew, and she is talked to where she sleeps rather than where she works.
        Role(StoryRole.JunoPost, "place.juno", false,
            At(RegionKind.City, SiteKind.Settlement, Pick.BuildOrder, 2),
            At(RegionKind.City, SiteKind.Workshop, Pick.BuildOrder),
            At(RegionKind.City, SiteKind.Farmstead, Pick.BuildOrder)),

        // The aerostat has to be winched to something. The city's one airfield.
        Role(StoryRole.AshmountTether, "place.tether", true,
            At(RegionKind.City, SiteKind.Airfield, Pick.NearestToCentre),
            At(RegionKind.City, SiteKind.Depot, Pick.NearestToCentre)),

        Role(StoryRole.SparrowCamp, "place.sparrow", true,
            At(RegionKind.Ashfield, SiteKind.Settlement, Pick.BuildOrder),
            At(RegionKind.Ashfield, SiteKind.Farmstead, Pick.BuildOrder)),

        // "The Depot in the Ashfield furthest from the region centre" - deep in the ash,
        // which is the only geometry the finale actually needs. Failing a depot, the
        // furthest hardened store there is; the ash and the distance are the beat.
        Role(StoryRole.ScaldMagazine, "place.magazine", false,
            At(RegionKind.Ashfield, SiteKind.Depot, Pick.FurthestFromCentre),
            At(RegionKind.Ashfield, SiteKind.FuelCache, Pick.FurthestFromCentre),
            At(RegionKind.Ashfield, SiteKind.Wreck, Pick.FurthestFromCentre)),
    };

    // ---------------------------------------------------------------- the contents

    /// <summary>
    /// What each role's site contains once it is bound. Everything here is a fact about a
    /// place; nothing here is a beat. The beats live in the search thread and read this.
    /// </summary>
    private static StorySite Content(StoryRole role, int siteId) => role switch
    {
        StoryRole.MattieHome => new(role, siteId, "the pumps", StoryProp.None,
            "mattie", "Mattie Sowerby", null,
            "a water tower and a pump house still working, which is two more than the basin has anywhere else"),

        StoryRole.DossHome => new(role, siteId, "Doss's place", StoryProp.None,
            "doss", "Doss Emery", null,
            "wheat that mostly did not come up, and an aerial guyed to the chimney"),

        StoryRole.LongAcreMast => new(role, siteId, "the mast up on the rise", StoryProp.None,
            null, null, "search.band",
            "a lattice mast on the highest ground in the farmland, visible from eight kilometres"),

        StoryRole.NellHome => new(role, siteId, "the scrap yard", StoryProp.None,
            "nell", "Nell Abergale", null,
            "sorted heaps, which means somebody is still working it"),

        StoryRole.FenmoorField => new(role, siteId, "the field they left from", StoryProp.DepartureField,
            null, null, "search.field",
            "one runway, and a loading apron with nothing on it"),

        StoryRole.BelHome => new(role, siteId, "the stilt village", StoryProp.None,
            "bel", "Bel Tiernan", null,
            "houses standing out of the water on legs, and boats, and no road in"),

        StoryRole.DrowningWreck => new(role, siteId, "the wreck out in the wet", StoryProp.DrownedAircraft,
            null, null, "search.wreck",
            "a tail fin standing out of flat water where nothing vertical should be"),

        StoryRole.OsieHome => new(role, siteId, "the village under the shoulder", StoryProp.None,
            "osie", "Osie Crane", null,
            "slate roofs in a fold of the hill, out of the wind"),

        StoryRole.ColdShoulderCairn => new(role, siteId, "the cairn on the ridge", StoryProp.Cairn,
            null, null, "search.cairn",
            "a stone pile on a bare ridge with no building anywhere near it"),

        StoryRole.FerrenOffice => new(role, siteId, "the works", StoryProp.None,
            "ferren", "Halvard Ferren", null,
            "a rolling mill with smoke coming off it, which nothing else in the country does"),

        StoryRole.SawtoothRelay => new(role, siteId, "the works' own mast", StoryProp.None,
            null, null, "search.roster",
            "a mast on the ridge above the works, with a cable run down to it"),

        StoryRole.WrayHome => new(role, siteId, "the cut", StoryProp.None,
            "wray", "Sera Wray", "search.wray",
            "a flooded cut with a turbine in it, and three hundred people living off the head of water"),

        StoryRole.JunoPost => new(role, siteId, "the tether crew's billet", StoryProp.None,
            "juno", "Juno Kessel", null,
            "a terrace in the aerostat's shadow, close enough to the winch to walk it"),

        StoryRole.AshmountTether => new(role, siteId, "the tether field", StoryProp.None,
            null, null, "search.window",
            "a winch house, a mooring mast, and the aerostat above it if it is up"),

        StoryRole.SparrowCamp => new(role, siteId, "the camp in the ash", StoryProp.None,
            "sparrow", "Sparrow", null,
            "tarpaulins and a drum fire in grey country, and nobody standing in the open"),

        StoryRole.ScaldMagazine => new(role, siteId, "the magazine", StoryProp.SealedMagazine,
            null, null, "search.magazine",
            "a concrete revetment, intact, in a field of things that are not"),

        _ => new(role, siteId, role.ToString(), StoryProp.None),
    };

    // ------------------------------------------------------------------ resolution

    private static List<RoleBinding>? _bindings;
    private static Dictionary<StoryRole, Site>? _byRole;
    private static Dictionary<int, StorySite>? _bySite;
    private static List<StoryProblem>? _problems;

    /// <summary>Every role, bound or not, in registry order.</summary>
    public static IReadOnlyList<RoleBinding> Bindings { get { Resolve(); return _bindings!; } }

    /// <summary>
    /// Everything wrong with the current binding, in plain language. Anything fatal in
    /// here is a beat that cannot fire.
    /// </summary>
    public static IReadOnlyList<StoryProblem> Problems { get { Resolve(); return _problems!; } }

    /// <summary>The site playing this role, or null if the role failed to bind at all.</summary>
    public static Site? SiteFor(StoryRole role)
    {
        Resolve();
        return _byRole!.TryGetValue(role, out Site? s) ? s : null;
    }

    /// <summary>The site id playing this role, or -1. -1 is a bug, not a state to handle.</summary>
    public static int SiteId(StoryRole role) => SiteFor(role)?.Id ?? -1;

    /// <summary>The authored contents of a site, or null if it is just a place.</summary>
    public static StorySite? For(int siteId)
    {
        Resolve();
        return _bySite!.TryGetValue(siteId, out StorySite? s) ? s : null;
    }

    public static bool IsStorySite(int siteId) { Resolve(); return _bySite!.ContainsKey(siteId); }

    /// <summary>Does this site play this role? The cheap test for a beat gate.</summary>
    public static bool Is(int siteId, StoryRole role) => siteId >= 0 && SiteId(role) == siteId;

    /// <summary>True if every role bound. Warnings do not fail it; unbound roles do.</summary>
    public static bool Verify() { Resolve(); return !_problems!.Any(p => p.Fatal); }

    /// <summary>Throw the resolution away. Only tests need this.</summary>
    public static void Invalidate() { _bindings = null; _byRole = null; _bySite = null; _problems = null; }

    private static void Resolve()
    {
        if (_bindings is not null) return;

        var bindings = new List<RoleBinding>();
        var byRole = new Dictionary<StoryRole, Site>();
        var bySite = new Dictionary<int, StorySite>();
        var problems = new List<StoryProblem>();

        foreach (RoleSpec spec in Specs)
        {
            Site? chosen = null;
            SiteRule? used = null;
            int usedIndex = -1, usedCount = 0, intentCount = 0;

            for (int i = 0; i < spec.Rules.Count; i++)
            {
                SiteRule rule = spec.Rules[i];
                List<Site> ordered = Candidates(rule);
                if (i == 0) intentCount = ordered.Count;
                if (ordered.Count < rule.Ordinal) continue;

                chosen = ordered[rule.Ordinal - 1];
                used = rule;
                usedIndex = i;
                usedCount = ordered.Count;
                break;
            }

            if (chosen is null)
            {
                bindings.Add(new RoleBinding(spec, null, null, -1, intentCount));
                problems.Add(new StoryProblem(true,
                    $"{spec.Role}: NO SITE. Wanted the {spec.Rule}; the world has {intentCount} of those, " +
                    $"and all {spec.Rules.Count} rule(s) for this role came up empty. " +
                    "This beat has nowhere to happen."));
                continue;
            }

            bindings.Add(new RoleBinding(spec, chosen, used, usedIndex, usedCount));
            byRole[spec.Role] = chosen;

            if (bySite.TryGetValue(chosen.Id, out StorySite? clash))
                problems.Add(new StoryProblem(true,
                    $"{spec.Role}: COLLISION. Bound to site #{chosen.Id} \"{chosen.Name}\", which is already " +
                    $"{clash.Role}. Two roles cannot share a place - one of them will read as a coincidence."));
            else
                bySite[chosen.Id] = Content(spec.Role, chosen.Id);

            if (usedIndex > 0)
                problems.Add(new StoryProblem(false,
                    $"{spec.Role}: DEGRADED. Wanted the {spec.Rule}; the world has {intentCount} of those, " +
                    $"so it fell back to the {used!.Text} and took #{chosen.Id} \"{chosen.Name}\". " +
                    "The beat still has somewhere to happen, but the prose must not promise the intent."));

            if (spec.ExpectUnique && usedCount > 1)
                problems.Add(new StoryProblem(false,
                    $"{spec.Role}: AMBIGUOUS. The fiction says there is one {used!.Kind} in " +
                    $"{SiteRule.RegionName(used.Region)}; there are {usedCount}. The tie-break took #{chosen.Id}, " +
                    "which is stable, but \"the\" is now the wrong article."));
        }

        SenseChecks(byRole, problems);

        _bindings = bindings;
        _byRole = byRole;
        _bySite = bySite;
        _problems = problems;

        // Loud, at resolve time, in the game as well as in the report. A role that did not
        // bind is a beat that cannot fire, and finding that out in a playtest is three
        // hours later than finding it out here.
        foreach (StoryProblem p in problems)
        {
            if (p.Fatal) GD.PushError($"[story] {p.Text}");
            else GD.PushWarning($"[story] {p.Text}");
        }
        int fatal = problems.Count(p => p.Fatal);
        if (problems.Count > 0)
            GD.PrintErr($"[story] {fatal} error(s) and {problems.Count - fatal} warning(s) binding story " +
                        "roles - run --worldreport for the table");
    }

    /// <summary>
    /// Candidates for one rule, in the order the rule sorts them.
    ///
    /// Region and kind filter; nothing else, ever - a rule that starts filtering on
    /// position is a coordinate with extra steps. The sort key is a float and the
    /// tie-break is site id ascending, which is unique and stable, so the ordering is
    /// total and the binding cannot move between launches.
    /// </summary>
    private static List<Site> Candidates(SiteRule rule)
    {
        Region region = WorldMap.Regions.First(r => r.Kind == rule.Region);
        float Key(Site s) => rule.Pick switch
        {
            Pick.BuildOrder => s.Id,
            // RawAt, not At: the graded pad under a site is a levelling artefact and would
            // flatten exactly the difference these rules sort on.
            Pick.LowestGround => WorldHeight.RawAt(s.Position.X, s.Position.Y),
            Pick.HighestGround => -WorldHeight.RawAt(s.Position.X, s.Position.Y),
            Pick.FurthestFromCentre => -s.Position.DistanceTo(region.Centre),
            Pick.NearestToCentre => s.Position.DistanceTo(region.Centre),
            _ => s.Id,
        };
        return WorldMap.Sites
            .Where(s => s.Region == rule.Region && s.Kind == rule.Kind)
            .OrderBy(Key)
            .ThenBy(s => s.Id)
            .ToList();
    }

    /// <summary>
    /// A rule can bind and still be nonsense. These are the checks for nonsense, and they
    /// are the kind of thing that is invisible in code and obvious in a number.
    /// </summary>
    private static void SenseChecks(Dictionary<StoryRole, Site> byRole, List<StoryProblem> problems)
    {
        if (byRole.TryGetValue(StoryRole.DrowningWreck, out Site? wreck))
        {
            float h = WorldHeight.RawAt(wreck.Position.X, wreck.Position.Y);
            if (h > WorldHeight.WaterLevel + 8f)
                problems.Add(new StoryProblem(false,
                    $"DrowningWreck: DRY. #{wreck.Id} \"{wreck.Name}\" sits at {h:F1} m, " +
                    $"{h - WorldHeight.WaterLevel:F1} m above the waterline ({WorldHeight.WaterLevel:F0} m). " +
                    "It is the lowest wreck in the wetland, so the rule is right and the ground is wrong: " +
                    "The Drowning has no standing water in it. Beat 8 says half in the water."));
        }

        if (byRole.TryGetValue(StoryRole.ColdShoulderCairn, out Site? cairn))
        {
            // "A stone pile on a bare ridge with no building near it."
            Site? neighbour = WorldMap.Sites
                .Where(s => s.Id != cairn.Id &&
                            s.Kind is SiteKind.Settlement or SiteKind.Farmstead or SiteKind.Workshop)
                .OrderBy(s => s.Position.DistanceTo(cairn.Position))
                .FirstOrDefault();
            if (neighbour is not null && neighbour.Position.DistanceTo(cairn.Position) < 250f)
                problems.Add(new StoryProblem(false,
                    $"ColdShoulderCairn: CROWDED. #{cairn.Id} \"{cairn.Name}\" is " +
                    $"{neighbour.Position.DistanceTo(cairn.Position):F0} m from \"{neighbour.Name}\". " +
                    "The 500 m read is \"no building anywhere near it\"."));
        }

        if (byRole.TryGetValue(StoryRole.ScaldMagazine, out Site? mag) &&
            byRole.TryGetValue(StoryRole.WrayHome, out Site? wray))
        {
            // The finale is a night crossing under a SAM belt with a 420 kg load. If the
            // two ends are next door to each other there is no crossing and no finale.
            float d = mag.Position.DistanceTo(wray.Position);
            if (d < 3000f)
                problems.Add(new StoryProblem(false,
                    $"ScaldMagazine: the finale leg is only {d / 1000f:F1} km (Ashmount to the magazine). " +
                    "Story.md budgets 8.7 km for the crossing; under 3 km there is no sortie in it."));
        }
    }

    // --------------------------------------------------------------------- geometry

    /// <summary>
    /// The authored element on top of the generic site build.
    ///
    /// Called from <see cref="SiteKit.Build"/> with the site root already positioned at
    /// the site's ground point. Safe to call for every site in the world: it does nothing
    /// unless the site plays a role that declares a prop.
    ///
    /// These are silhouettes, in the D-003b sense - the 500 m read, the thing that makes
    /// the place identifiable from across the valley before anybody has said a word about
    /// it. The 150 m and 15 m layers belong with the beats and are not here yet.
    /// </summary>
    public static void Decorate(Node3D root, Site site)
    {
        StorySite? story = For(site.Id);
        if (story is null || story.Prop == StoryProp.None) return;

        var rng = new RandomNumberGenerator { Seed = (ulong)(site.Id * 2654435761L + 101) };
        switch (story.Prop)
        {
            case StoryProp.DrownedAircraft: DrownedAircraft(root, site, rng); break;
            case StoryProp.Cairn: Cairn(root, site, rng); break;
            case StoryProp.SealedMagazine: SealedMagazine(root, site, rng); break;
            case StoryProp.DepartureField: DepartureField(root, site, rng); break;
        }
    }

    /// <summary>Ground-relative position inside a site root, snapped to the terrain.</summary>
    private static Vector3 Local(Site site, float dx, float dz)
    {
        float wx = site.Position.X + dx, wz = site.Position.Y + dz;
        return new Vector3(dx, WorldHeight.At(wx, wz) - site.Ground.Y, dz);
    }

    private static void DrownedAircraft(Node3D root, Site site, RandomNumberGenerator rng)
    {
        // The read at 500 m is one vertical thing in flat water. Everything else about
        // this wreck is below the surface and only resolves when you are low over it.
        float yaw = rng.Randf() * Mathf.Tau;
        Vector3 at = Local(site, 0, 0);
        Material metal = SiteKit.Materials.Metal, dark = SiteKit.Materials.Dark;

        // Tail boom, up out of the water at an angle, and the fin on the end of it.
        var boom = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.3f, 1.3f, 11f) },
            MaterialOverride = metal,
            Position = at + new Vector3(0, 1.6f, 0),
        };
        boom.RotateY(yaw);
        boom.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-24f));
        root.AddChild(boom);

        var fin = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 5.2f, 3.4f) },
            MaterialOverride = metal,
            Position = at + new Vector3(Mathf.Sin(yaw) * 5.2f, 5.0f, Mathf.Cos(yaw) * 5.2f),
        };
        fin.RotateY(yaw);
        root.AddChild(fin);

        // The fuselage, mostly under. No scorching and no debris field: it landed.
        var hull = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(3.1f, 3.0f, 19f) },
            MaterialOverride = dark,
            Position = at + new Vector3(Mathf.Sin(yaw) * -9f, -0.9f, Mathf.Cos(yaw) * -9f),
        };
        hull.RotateY(yaw);
        root.AddChild(hull);

        // The cabin door, standing open. The 15 m read, and the whole of beat 9.
        var door = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.75f, 0.85f) },
            MaterialOverride = metal,
            Position = at + new Vector3(Mathf.Sin(yaw) * -5f + Mathf.Cos(yaw) * 1.7f, 0.5f,
                                        Mathf.Cos(yaw) * -5f - Mathf.Sin(yaw) * 1.7f),
        };
        door.RotateY(yaw + Mathf.DegToRad(62f));
        root.AddChild(door);
    }

    private static void Cairn(Node3D root, Site site, RandomNumberGenerator rng)
    {
        // Four courses, built carefully, by one person - and one course left blank. The
        // blank course is the beat; it is built here so the geometry says it before any
        // text does.
        Material stone = SiteKit.Materials.Concrete;
        Vector3 at = Local(site, 0, 0);
        float y = 0f;
        for (int course = 0; course < 5; course++)
        {
            float w = 1.55f - course * 0.22f;
            float t = course == 4 ? 0.22f : 0.30f;   // the blank course is a thin capstone
            var m = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, t, w) },
                MaterialOverride = stone,
                Position = at + new Vector3(rng.RandfRange(-0.04f, 0.04f), y + t * 0.5f,
                                            rng.RandfRange(-0.04f, 0.04f)),
            };
            m.RotateY(rng.RandfRange(-0.12f, 0.12f));
            root.AddChild(m);
            y += t;
        }
    }

    private static void SealedMagazine(Node3D root, Site site, RandomNumberGenerator rng)
    {
        // A concrete revetment, intact, in a field of things that are not. Intact is the
        // silhouette: straight edges where nothing else has any.
        Material concrete = SiteKit.Materials.Concrete;
        Material steel = SiteKit.Materials.Metal;
        float yaw = rng.Randf() * Mathf.Tau;
        Vector3 at = Local(site, 0, 0);

        var mound = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(22f, 6.0f, 14f) },
            MaterialOverride = concrete,
            Position = at + new Vector3(0, 3.0f, 0),
        };
        mound.RotateY(yaw);
        root.AddChild(mound);

        // Blast walls funnelling in to the door, which has not been opened in six years.
        for (int s = -1; s <= 1; s += 2)
        {
            var wall = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.9f, 4.2f, 9f) },
                MaterialOverride = concrete,
                Position = at + new Vector3(Mathf.Cos(yaw) * 4.4f * s + Mathf.Sin(yaw) * 11f, 2.1f,
                                            -Mathf.Sin(yaw) * 4.4f * s + Mathf.Cos(yaw) * 11f),
            };
            wall.RotateY(yaw);
            root.AddChild(wall);
        }

        var door = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(4.6f, 3.9f, 0.45f) },
            MaterialOverride = steel,
            Position = at + new Vector3(Mathf.Sin(yaw) * 7.1f, 1.95f, Mathf.Cos(yaw) * 7.1f),
        };
        door.RotateY(yaw);
        root.AddChild(door);
    }

    private static void DepartureField(Node3D root, Site site, RandomNumberGenerator rng)
    {
        // A loading apron with nothing on it. The story of this place is an absence, so
        // the only authored thing is the marks of what used to be here.
        Material dark = SiteKit.Materials.Dark;
        float yaw = rng.Randf() * Mathf.Tau;
        for (int i = 0; i < 3; i++)
        {
            float dx = Mathf.Cos(yaw) * (i - 1) * 7f, dz = Mathf.Sin(yaw) * (i - 1) * 7f;
            var m = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.10f, 2.4f) },
                MaterialOverride = dark,
                Position = Local(site, dx, dz) + new Vector3(0, 0.05f, 0),
            };
            m.RotateY(yaw);
            root.AddChild(m);
        }
    }

    // ----------------------------------------------------------------------- report

    /// <summary>
    /// Every role, what it bound to, and what is wrong with the result.
    ///
    /// This is the deliverable that makes the layer trustworthy. If it prints clean,
    /// every beat in story.md has a place to happen and that place is the same place on
    /// the next launch. If it does not print clean, the thing that is broken is named,
    /// with the number that proves it.
    ///
    ///     godot --headless --path game -- --worldreport
    /// </summary>
    public static void Report()
    {
        Resolve();
        List<RoleBinding> all = _bindings!;
        List<StoryProblem> problems = _problems!;
        int bound = all.Count(b => b.Bound);
        int degraded = all.Count(b => b.Degraded);
        int fatal = problems.Count(p => p.Fatal);

        GD.Print($"story places: {all.Count} roles, {bound} bound ({degraded} degraded), " +
                 $"{all.Count - bound} UNBOUND");

        // The failures first. A role with nowhere to happen is the only thing on this
        // page that stops work, so it does not go at the bottom under the tables.
        if (fatal > 0)
        {
            GD.Print("");
            GD.Print($"  !!! {fatal} ROLE(S) COULD NOT BIND. THESE ARE CONTENT BUGS, NOT WARNINGS !!!");
            foreach (StoryProblem p in problems.Where(p => p.Fatal)) GD.Print($"  !!! {p.Text}");
        }

        GD.Print("");
        GD.Print("  role                site                  id    region            kind         ground    pick   rule");
        foreach (RoleBinding b in all)
        {
            string flag = b.Degraded ? "!" : " ";
            if (b.Site is Site s)
            {
                float h = WorldHeight.RawAt(s.Position.X, s.Position.Y);
                GD.Print($" {flag}{b.Spec.Role,-18}  {Trim(s.Name, 20),-20}  #{s.Id,-4} {SiteRule.RegionName(s.Region),-16}  " +
                         $"{s.Kind,-11} {h,7:F1} m  {b.Used!.Ordinal}/{b.Candidates,-3}  " +
                         $"{(b.Degraded ? "FELL BACK TO " + b.Used.Text : b.Spec.Rule)}");
            }
            else
            {
                GD.Print($" !{b.Spec.Role,-18}  *** NO SITE ***       --    {SiteRule.RegionName(b.Spec.Intent.Region),-16}  " +
                         $"{b.Spec.Intent.Kind,-11}       -    -/{b.Candidates,-3}  {b.Spec.Rule}");
            }
        }

        GD.Print("");
        GD.Print("  contents: what the beat finds when it puts the skids down");
        GD.Print("  role                alias                       prop              person            grants");
        foreach (RoleBinding b in all)
        {
            if (b.Site is not Site s) continue;
            StorySite c = For(s.Id)!;
            GD.Print($"  {b.Spec.Role,-18}  {Trim(c.Alias, 26),-26} {c.Prop,-16}  {c.NpcName ?? "-",-16}  " +
                     $"{c.GrantsOnSearch ?? "-"}");
        }

        GD.Print("");
        // Build every authored prop into a throwaway node and count what came out. The
        // SiteKit hook is one line in a file this layer does not own, so the only honest
        // way to hand that patch over is to have already run the code it calls.
        GD.Print("  props, built dry (this is the code SiteKit.Build would call)");
        foreach (RoleBinding b in all)
        {
            if (b.Site is not Site s) continue;
            StorySite c = For(s.Id)!;
            if (c.Prop == StoryProp.None) continue;
            var probe = new Node3D();
            Decorate(probe, s);
            GD.Print($"  {b.Spec.Role,-18}  {c.Prop,-16} {probe.GetChildCount(),2} mesh(es)   {c.Reads}");
            probe.Free();
        }

        GD.Print("");
        // The pacing budget in story.md 2 is quoted against these distances, and a seed
        // change is allowed to move them. Printing them is how anyone finds out that it did.
        GD.Print("  the route, as flown (straight line, and minutes at 55 m/s)");
        (StoryRole from, StoryRole to, string what)[] legs =
        {
            (StoryRole.MattieHome, StoryRole.DossHome, "the pumps -> Long Acre"),
            (StoryRole.DossHome, StoryRole.LongAcreMast, "Doss -> the mast"),
            (StoryRole.DossHome, StoryRole.NellHome, "Long Acre -> Fenmoor"),
            (StoryRole.NellHome, StoryRole.FenmoorField, "Nell -> the field"),
            (StoryRole.FenmoorField, StoryRole.DrowningWreck, "the field -> the wreck"),
            (StoryRole.DrowningWreck, StoryRole.ColdShoulderCairn, "the wreck -> the cairn"),
            (StoryRole.ColdShoulderCairn, StoryRole.FerrenOffice, "the cairn -> the works"),
            (StoryRole.FerrenOffice, StoryRole.WrayHome, "the works -> Ashmount"),
            (StoryRole.WrayHome, StoryRole.ScaldMagazine, "Ashmount -> the magazine"),
        };
        foreach ((StoryRole from, StoryRole to, string what) in legs)
        {
            Site? a = SiteFor(from), c2 = SiteFor(to);
            if (a is null || c2 is null) { GD.Print($"  {what,-28} -- (unbound)"); continue; }
            float d = a.Position.DistanceTo(c2.Position);
            GD.Print($"  {what,-28} {d / 1000f,6:F1} km  {d / 55f / 60f,5:F1} min");
        }

        GD.Print("");
        int warnings = problems.Count - fatal;
        if (problems.Count == 0)
        {
            GD.Print("  PROBLEMS: none. Every role bound to the site its own rule asked for, and every");
            GD.Print("  sense check passed. The story has sixteen places and they are the right ones.");
        }
        else
        {
            GD.Print($"  problems: {fatal} error(s), {warnings} warning(s)");
            foreach (StoryProblem p in problems) GD.Print($"  {p.Severity}  {p.Text}");
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";
}
