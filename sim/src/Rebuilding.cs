using System;
using System.Collections.Generic;
using System.Linq;

namespace Rotorwash.Sim;

/// <summary>
/// What a place does after you have knocked it down.
///
/// <para><b>The question this answers.</b> If the player can level a building, something has
/// to decide whether it comes back, and the cheap answer - a respawn timer - is the one that
/// dissolves the world. A timer says nothing about who did the work or where they came from,
/// so the player learns that places are props that reset, and every place in the game becomes
/// less real at once. The expensive answer is that **regeneration is a fiction, not a timer**:
/// somebody rebuilds it, out of something, and when there is nobody left it stays down.</para>
///
/// <para><b>The currency is population.</b> The world already knows how many people live at
/// every site (<c>WorldMap.PopulationAt</c>: a settlement is 30-80, a farmstead 3-13, a relay
/// 0-2). That number is the labour force and the budget at the same time. Knock down a house
/// and the people who live there rebuild it. Kill the people and the house stays a ruin,
/// permanently, because there is nobody to lift anything - which is what gives violence
/// against a place real weight without needing a scripted consequence.</para>
///
/// <para><b>One at a time.</b> Work goes into the oldest ruin until it is finished, and only
/// then the next. That is what people actually do, and - more usefully for a game read at
/// 150 knots from five hundred feet - it means a levelled village looks like one house with
/// a new roof and eleven still flat, instead of twelve houses uniformly 8% rebuilt, which
/// from the air is indistinguishable from twelve ruins.</para>
///
/// <para><b>Time only passes when you are not watching.</b> The caller decides proximity and
/// simply stops feeding time in; see <see cref="Advance"/>. Nothing in here knows where the
/// player is.</para>
/// </summary>
public sealed class SiteDamage
{
    /// <summary>
    /// Person-days to put a building back up, by how big it is.
    ///
    /// These are game-days, and the game runs at 30x (SceneMood.TimeScale), so a game day is
    /// about 48 minutes of play. The tuning target is Fred's: fast enough that coming back
    /// shows you visible progress rather than a finished building or an unchanged ruin.
    /// A real house is about 120 person-days and the first pass used that, which measured
    /// out at seven game days a house and thirty for a levelled village - twenty-four hours
    /// of play, which is not "slow", it is permanent with extra steps. 90 is a scavenged
    /// single-storey place put back up by people who have done it before and are not
    /// fussing about it.
    /// </summary>
    public const double HouseWorkDays = 90.0;

    /// <summary>A shed, a tank, a fence. Cheap, and back within a game day or two.</summary>
    public const double SmallWorkDays = 25.0;

    /// <summary>
    /// A mast, a water tower, a hangar. Expensive, and the thing that makes levelling a
    /// relay a decision you live with rather than an inconvenience.
    /// </summary>
    public const double TallWorkDays = 420.0;

    /// <summary>Share of a site's people on the rebuild. The rest still have to eat.</summary>
    public const double WorkforceFraction = 0.55;

    private readonly Dictionary<int, double> _remaining = new();   // structure index -> person-days left
    private readonly List<int> _order = new();                     // oldest ruin first

    /// <summary>People killed here. Subtracted from the site's nominal head count.</summary>
    public int PopulationLost { get; private set; }

    /// <summary>Every structure currently down, oldest first.</summary>
    public IReadOnlyList<int> Ruins => _order;

    /// <summary>Nothing here has ever been touched.</summary>
    public bool Untouched => _order.Count == 0 && PopulationLost == 0;

    /// <summary>Knock a structure down. Idempotent: levelling rubble does nothing.</summary>
    public void Level(int structureIndex, double workDays)
    {
        if (_remaining.ContainsKey(structureIndex)) return;
        _remaining[structureIndex] = Math.Max(1.0, workDays);
        _order.Add(structureIndex);
    }

    /// <summary>Whether this structure is currently down or part-built.</summary>
    public bool IsRuined(int structureIndex) => _remaining.ContainsKey(structureIndex);

    /// <summary>
    /// How far along the rebuild is, 0 (flat) to 1 (finished and no longer a ruin).
    ///
    /// Only the oldest ruin is ever between 0 and 1; everything behind it in the queue reads
    /// 0 because nobody has started on it.
    /// </summary>
    public double RebuildFraction(int structureIndex, double workDays)
    {
        if (!_remaining.TryGetValue(structureIndex, out double left)) return 1.0;
        double total = Math.Max(1.0, workDays);
        return Math.Clamp(1.0 - left / total, 0.0, 1.0);
    }

    /// <summary>Somebody died here.</summary>
    public void LosePeople(int n) => PopulationLost = Math.Max(0, PopulationLost + n);

    /// <summary>
    /// How many people are left to do the work.
    ///
    /// <paramref name="nominal"/> is the site's undamaged head count. A place emptied out
    /// returns zero and never rebuilds anything again, which is the whole point.
    /// </summary>
    public int Workers(int nominal) => Math.Max(0, nominal - PopulationLost);

    /// <summary>
    /// Put <paramref name="gameSeconds"/> of work in, with <paramref name="workers"/> people.
    ///
    /// Call this only while the player is NOT nearby - the caller owns that decision (Fred's
    /// rule, and the usual one: nobody wants to hover and watch a wall grow). Returns the
    /// structure indices that were completed by this call, so the caller can rebuild meshes
    /// and, if it likes, have somebody mention it.
    /// </summary>
    public IReadOnlyList<int> Advance(double gameSeconds, int workers)
    {
        var finished = new List<int>();
        if (gameSeconds <= 0 || workers <= 0 || _order.Count == 0) return finished;

        // Not everybody is on the building site, but more of them than usual: this is a
        // place that has just had its houses knocked down, which is the one occasion
        // everybody who can lift something does. A third was the first guess and it made a
        // village take most of a campaign to stand back up.
        double personDays = gameSeconds / 86400.0 * workers * WorkforceFraction;

        while (personDays > 0 && _order.Count > 0)
        {
            int first = _order[0];
            double left = _remaining[first];
            if (personDays < left)
            {
                _remaining[first] = left - personDays;
                break;
            }
            personDays -= left;
            _remaining.Remove(first);
            _order.RemoveAt(0);
            finished.Add(first);
        }
        return finished;
    }

    /// <summary>Restore from a save.</summary>
    public void Restore(int populationLost, IEnumerable<(int Index, double Remaining)> ruins)
    {
        PopulationLost = Math.Max(0, populationLost);
        _remaining.Clear();
        _order.Clear();
        foreach ((int index, double remaining) in ruins)
        {
            if (_remaining.ContainsKey(index)) continue;
            _remaining[index] = Math.Max(0.01, remaining);
            _order.Add(index);
        }
    }

    /// <summary>For the save: the queue, in order, with work remaining.</summary>
    public IEnumerable<(int Index, double Remaining)> Snapshot()
        => _order.Select(i => (i, _remaining[i]));
}

/// <summary>
/// Who turns up after you have shot somebody.
///
/// <para>Fred's instinct - infinite brothers, sisters and cousins - is the right one and it
/// is funnier than it is cynical, because in a district of a few hundred people it is also
/// simply true: the person who takes over the workshop IS somebody's brother. The failure
/// mode is that it becomes a respawn with a joke on top, where the player learns that killing
/// people is free because the same role always refills.</para>
///
/// <para>So it is bounded by the same budget as the buildings. A replacement is drawn from
/// the site's remaining population, and when that runs out there is nobody to take over. The
/// ordinal is kept because it is the joke and because it is information: hearing that this is
/// the third Fincher this year tells the player exactly what they have been doing.</para>
/// </summary>
public static class Replacement
{
    /// <summary>
    /// How long before somebody else is doing that job, in game seconds.
    ///
    /// Four days. Long enough that it is not a respawn - you will have flown two or three
    /// sorties - and short enough that the district visibly closes over the gap, which is the
    /// unsettling part and the point.
    /// </summary>
    public const double DelaySeconds = 4 * 86400.0;

    /// <summary>Is somebody available to take the role over yet?</summary>
    public static bool Ready(double killedAtClock, double nowClock, int remainingPopulation)
        => remainingPopulation > 0 && nowClock - killedAtClock >= DelaySeconds;

    /// <summary>
    /// The new name: same family, different person.
    ///
    /// The surname is kept deliberately. It is what makes the replacement read as a place
    /// closing over a hole rather than as the game handing out a fresh NPC, and it is what
    /// lets the announcer say "another Fincher" without anything else having to track it.
    /// </summary>
    public static string NameFor(string previousFullName, int ordinal, IReadOnlyList<string> firstNames)
    {
        if (firstNames is null || firstNames.Count == 0) return previousFullName;

        int space = previousFullName.LastIndexOf(' ');
        string surname = space > 0 ? previousFullName[(space + 1)..] : previousFullName;

        // Deterministic in the surname and the ordinal, so a save reloads the same cousin.
        int h = surname.GetHashCode() ^ (ordinal * 0x9E3779);
        string first = firstNames[Math.Abs(h) % firstNames.Count];
        return $"{first} {surname}";
    }

    /// <summary>How the district refers to it, which is the whole joke (D-058).</summary>
    public static string Describe(string surname, int ordinal) => ordinal switch
    {
        <= 1 => $"{surname}'s brother has taken it on.",
        2 => $"Another {surname}. There are a lot of them.",
        3 => $"That is the third {surname} this year. Nobody has said anything about it.",
        _ => $"A {surname}. I have stopped keeping track and so has everybody else.",
    };
}
