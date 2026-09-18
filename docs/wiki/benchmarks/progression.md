# Benchmark — progression

*Comparative analysis of D-005 (gear-and-knowledge progression) and D-010 (air defence as the gating
system) against ten shipped games. Written 2026-09-18.*

---

## Bottom line

**The model is sound, it has better precedents than you probably realise, and it is currently
under-specified in exactly the two places that kill games like this: cadence and legibility.**
Gear-as-skill-tree is not exotic — S.T.A.L.K.E.R., Subnautica, Death Stranding and Elite Dangerous all
run on it — and Outer Wilds proves pure knowledge can carry an entire game, with its own designer
stating flatly that "Knowledge is the only gameplay reward" and listing experience points, new
abilities, items, upgrades and unlocked play areas as things her game deliberately does not contain
([Beachum, GDC 2021](https://media.gdcvault.com/GDC+2021/beachum_gdc_2021(1).pdf)). But every one of
those games pairs its rare, region-opening unlock with a dense substrate of small rewards arriving
every few minutes — a fragment, an artifact, a stash pin, a credit balance, a "like" — and every one of
them has **a single screen the player can open to see that they are further along than they were**: the
Subnautica PDA with its greyed-out recipes, the Outer Wilds rumour map with its orange "more to explore"
asterisk, the Death Stranding chiral network map, the Elite outfitting screen. Rotorwash as written in
D-005 and D-010 has the rare unlocks and has neither of the other two. The specific risk is not that the
game feels unrewarding at hour 30 — the threat-envelope spine is genuinely stronger for hour-30 payoff
than anything in Fallout — it is that it feels **inert between hours 2 and 8**, where New Vegas hands out
10–17 skill points every level and a perk every two, and Subnautica hands you a new craftable every few
minutes. Fix the microloop and build the progress surface and this is a better progression design than
seven of the ten games below. Leave them and it is worse than all ten.

---

## 0. Source confidence

The session's web-search budget was exhausted during this research. Claims are flagged:

- **[V]** verified against a cited source during this research.
- **[U]** unverified — stated from general knowledge or from a source that could not be re-fetched.
  Treat every **[U]** as a hypothesis.

Two structural caveats worth knowing: **no rigorous published minutes-per-level study exists for any of
the four XP games** — where this document gives pacing for them it is derived from the XP curve, which
is defensible, or from community anecdote, which is flagged. And the widely-announced GDC 2020 talk
"Curiosity-Driven Exploration: The Design of Outer Wilds" **does not exist as a recording** — GDC 2020
was cancelled; the citable Outer Wilds talk is Kelsey Beachum's GDC 2021 narrative session **[V]**.

---

## 1. How the comparison games actually structure progression

### 1.1 Summary

| Game | Progression currency | Cadence of a meaningful upgrade | What carries the sense of growth |
|---|---|---|---|
| **Fallout: New Vegas** | XP → levels → skill points + perks | Skill points every level; **perk every 2 levels** **[V]** | Passing a check you previously failed. Checks are **score-based, not dice** **[V]** — growth is legible as a yes/no |
| **Fallout 4** | XP → levels → one point → perk rank *or* SPECIAL **[V]** | **Every level, forever, no cap** **[V]** | The frequency itself, plus perk-gated crafting widening what you can build |
| **The Outer Worlds** | XP → 10 skill points/level + perk per 2 levels **[V]** | Tier unlocks at **20/40/60/80/100**, base skill only **[V]** | Tiers grant **verbs**: Tinker, Field Repair, pickpocketing, container preview **[V]** |
| **Skyrim** | Use → skill XP → level → 1 perk + 10 H/M/S **[V]** | Fast early, collapses late (see §1.2) | Doing the thing makes you better at it; Smithing 90 → Daedric **[V]** |
| **S.T.A.L.K.E.R.** | Money, suits, artifacts, toolkits. **No XP** **[V]** | **~4 armour purchases across a whole game** (5k→12k→25k→50k RU) **[V]** | Threat reclassification — a Bloodsucker goes from "run" to "manageable" |
| **Subnautica** | Blueprints from fragments; depth modules; O₂ | A craftable every few minutes early; a **depth tier** every few hours | **The depth number.** Progress is a coordinate, and it is a survival instrument first |
| **Death Stranding** | Likes → facility connection (5 stars) → blueprints **[V]** | A blueprint per star; infrastructure accumulates continuously | Terrain getting easier **because you made it easier** |
| **Elite Dangerous** | Credits, ranks, engineering materials | Module swap in minutes; hull class in tens of hours | The ship under you changes shape |
| **Kenshi** | Use-based skills, 33 stats, no XP pool **[V]** | Quadratically decaying; hundreds of in-game days at the top **[V]** | Surviving what previously dismembered you |
| **Outer Wilds** | **Knowledge only. Zero upgrades all game** **[V]** | A fact every few minutes; a loop-changing fact every 30–60 min | The rumour map filling in, and your own hands |

### 1.2 The four XP games

All three with published curves use a **linear** per-level cost with a ~20× ramp:

| | New Vegas | Fallout 4 | Skyrim |
|---|---|---|---|
| Formula | `25(3n+2)(n−1)` **[V]** | `ΔXP = 75n + 125` **[V]** | `(level + 3) × 25` **[V]** |
| L1→2 | 200 | 200 | 100 |
| Last "normal" level | 4,400 (29→30) | 3,875 (50→51) | 2,075 (80→81) |
| Ramp | ~22× | ~19× | ~21× |
| Cap | 30 base / **50** with four DLC **[V]** | none; crash at 65,535 **[V]** | 81; uncapped post-1.9 **[V]** |
| Per level | `10 + INT/2` skill points; perk per 2 levels **[V]** | one point → perk rank *or* SPECIAL **[V]** | 1 perk + 10 H/M/S **[V]** |
| Skills | 13 **[V]** | none | 18, use-based **[V]** |
| Perk inventory | 88 regular + 8 companion + 16 challenge + 18 special **[V]** | 70 perks / **275 ranks** **[V]** | 180 perks / **251 ranks** **[V]** |

**The lesson is not the math — the curves are nearly identical.** They feel different because of what
the level *buys*. Fallout 4 buys an indivisible, immediately usable choice every single level, which is
why it feels the most generous of the three despite having no skills at all.

Three transferable specifics:

**a) New Vegas ships a mechanic whose entire job is to rent a breakpoint you have not bought.** Skill
magazines give a temporary **+10 (+20 with Comprehension)** **[V]**. The designers knew
breakpoint-gated progression has dead zones and built a consumable to paper over them. **Rotorwash needs
a skill magazine.** See §3.1.

**b) Fallout 4's Idiot Savant** grants **3× XP at rank 1, 5× at rank 11**, on a proc whose chance is
*inversely* scaled to Intelligence (11% at INT 1, floor 1% at INT ≥ 11) **[V]**. That is a
variable-ratio schedule bolted onto a fixed-ratio one — an admission that the fixed one was not
producing enough small hits.

**c) Skyrim's late-game collapse is arithmetic, and instructive.** Character XP earned equals **the
level of the skill you just raised** **[V]**. So the Legendary reset (skill → 15, perks refunded)
technically removes the cap while making progress drastically less efficient: training one skill
15→100 yields **4,930 XP**, which takes you from level 1 to 17 — or from 194 to 195 **[V]**. When your
reward currency is also your progress meter, "more content" and "slower progress" become the same knob.

**d) The Outer Worlds converted numbers into verbs, and is the closest XP-game analogue to D-005.**
Points below 50 raise a whole group; at 50 the group locks and you specialise **[V]**. Tiers fire on
**base skill only** — companion and gear bonuses raise checks but never buy a tier **[V]**. And what a
tier grants is a capability, not a percentage: *Tinker*, *Field Repair*, pickpocketing, previewing
container contents. Six skills (Persuade, Lie, Intimidate, Hack, Lockpick, Engineering) have **only**
tier unlocks and no continuous passive at all **[V]**. That is the Rotorwash model, already shipped,
inside an XP game.

### 1.3 S.T.A.L.K.E.R. — the closest structural relative

No XP, no levels, no skills. Power is the item you own plus the map in your head.

**The entire "level curve" is about four purchases.** Armour runs roughly **5,000 → 12,000 → 25,000 →
50,000 RU** **[V]**. Across a 15–25 hour game that is on the order of **a dozen discrete "you got
stronger" events** — versus hundreds of XP pops in an RPG. This is the closest published analogue to
Rotorwash's proposed cadence, and it is worth being clear-eyed that S.T.A.L.K.E.R. is a shorter game
than Rotorwash intends to be.

Four mechanisms it uses that Rotorwash should copy:

1. **The ladder is not monotonic.** In Shadow of Chernobyl the SEVA is the radiation king at **90% rad
   / 40% bulletproof**, while the Exoskeleton is **60% bullet / 90% impact but only 30% rad**, and in
   SoC/CS cannot sprint at all **[V]**. There is no "best suit", so old gear stays alive. D-011 already
   commits Rotorwash to this via mass and hover ceiling — **do not soften it.**
2. **Artifacts are a budget-allocation puzzle, not a stat stick.** Most generate radiation that
   penetrates any armour, so a protective artifact must be paid for with a rad sink like the Bubble
   (**−4 rad, no other effect, up to 24,000 RU**) **[V]**. Every benefit has a named cost worn on the
   same belt. This is the cleanest template available for how Rotorwash's hardpoints should feel.
3. **Toolkits are progression that is also a place.** Call of Pripyat's three upgrade tiers are gated
   by **basic / fine / calibration toolkits** (**1,000 / 1,200 / 1,500 RU** reward each), and
   **calibration tools exist only in Pripyat**, the last map **[V]**. Tier 3 is structurally
   end-game-only because of *where* it is, not *what level* you are. D-010 runs this in one direction
   (capability opens region); S.T.A.L.K.E.R. runs it in both, and both is better.
4. **A stash is a map pin, not an item.** In SoC, looting a dead stalker's PDA yields a stash *location*
   **[V]**. Knowledge is literally loot, with a drop rate. Rotorwash should steal this verbatim.

Also note the upgrade trees are **branch-exclusive and colour-coded** — CoP's Exoskeleton is three tiers
deep with a mutually exclusive choice at most nodes (Armor +20% **or** Durability +15%; Cuirass armour
+30% **or** impact −30%) **[V]**, and a unique tier-3 hydraulic booster that **enables sprinting** and
requires all three toolkits delivered to one specific technician **[V]**.

### 1.4 Elite Dangerous

Verified prices (hull plus default E-rated modules, which is what the shipyard charges):

| Ship | Cost (CR) | Rebuy (CR) | Gate |
|---|---|---|---|
| Sidewinder Mk I | **32,000** | 1,600 | — (replacement is free, the ship is not) **[V]** |
| Cobra Mk III | **349,718** | 17,486 | — |
| Asp Explorer | **6,661,153** | 333,058 | — |
| Python | **56,978,180** | 2,848,909 | — |
| Anaconda | **146,969,450** | 7,348,473 | — |
| Federal Corvette | **187,969,450** | 9,398,473 | **Rear Admiral** (Federal rank 12 of 14) **[V]** |
| Imperial Cutter | **208,969,451** | 10,448,473 | **Duke** (Imperial rank 12 of 14) **[V]** |

Three things matter for Rotorwash:

**a) The multiplier shrinks while the absolute gap explodes.** Sidewinder → Cobra is ~11×; Cobra → Asp
~19×; Asp → Python ~8.6×; Python → Anaconda ~2.6× — but that last step is **+90 million credits, nearly
3,000 Sidewinders** **[V]**. Progression that is priced in a single linear currency always ends up here.

**b) Rebuy means progression *raises* the stakes of every flight.** Losing a Cobra costs 17,486 CR;
losing an Anaconda costs 7,348,473 — **21 Cobras** **[V]**. This is directly relevant to pillar 1:
Rotorwash has exactly one airframe and losing it is the fail state, so the Elite dynamic — where
high-credit players fly cheap ships because they cannot face the rebuy — is a live risk. **If Hugh gets
too valuable, players will stop flying him anywhere interesting.**

**c) Better is not monotonic here either.** Module ratings run E→A, but **D is the lightest (exploration)
and B is the toughest (combat)** — A is not strictly best **[V]**. Engineering is a second currency you
cannot buy: 25 engineers, reputation tiers gated on **500k / 2M / 8M / 16M credits of net profit**,
one modification per module, and installing a new one **permanently overwrites** the old **[V]**.

The warning: Elite's intermediate rewards are numbers on an outfitting screen rather than changes in
what the ship can *do*. A 4A power plant instead of a 4D is not a story. **Rotorwash must never ship an
upgrade whose only expression is a number in a menu.**

### 1.5 Kenshi — the one that pays you for losing

33 stats across 7 categories, no XP pool, no level curve — the cost to advance depends only on your
total in that skill **[V]**. The decay is **quadratic**: `level multiplier = (1 − level/101)²` **[V]**.
At level 0 that is 1.00; at 50 it is ~0.255; at 80 it is ~0.044; at 95 it is ~0.0035. Training Strength
by walking encumbered "will take **hundreds of days** to reach 100" **[V]**.

But the reason Kenshi's brutal early game works is **Stronger Opponent Logic**, and it is the single
most interesting mechanic in this entire document:

- **+0.1× XP per level you are below your opponent, capped at +5× at 50 levels under** **[V]**
- **−0.04× per level you are above, floor 0.1×** **[V]**
- **Outnumbered bonus up to 1.666× at 1v8 — "max of 10× XP if facing 8+ enemies"** **[V]**
- **Toughness rises from damage taken**, and standing back up instead of playing dead gives "a huge XP
  boost" **[V]**
- A **failed** assassination attempt gives **3× the XP** of a successful one **[V]**

So the game pays you, mechanically and enormously, for being outmatched. Getting beaten half to death
and dragged off by slavers *is* the progression event. That is structurally the opposite of
S.T.A.L.K.E.R., where failure merely costs medkits.

**This is the mechanism Rotorwash is missing.** In the current design, a sortie that goes badly — you
got painted, you took a hit, you limped home on a rough engine — produces nothing but repair costs. See
§3.1 and §6.

Kenshi's research spine is also worth noting because it is D-010's idea in another costume: tech levels
1–6 gated by **Books → Ancient Science Books → AI Cores**, where AI Cores exist only in the most lethal
places on the map **[V]**. A spatial danger gate wearing a research costume.

### 1.6 Subnautica

See §2.2 for the full treatment. The headline numbers, all verified:

| Vehicle | Base | MK1 | MK2 | MK3 |
|---|---|---|---|---|
| Seamoth | 200 m | 300 m | 500 m | **900 m** |
| Prawn Suit | **900 m** | 1300 m | 1700 m | — |
| Cyclops | 500 m | **900 m** | 1300 m | **1700 m** |

Note the **interlock**: the Seamoth tops out at exactly the Prawn's base, and Cyclops MK1 equals Seamoth
MK3 **[V]**. The tiers hand off cleanly rather than overlapping, which is why no vehicle ever feels
redundant and why the player always knows precisely which one to take.

Fragment counts escalate structurally: **Seaglide 2 → Seamoth 3 → Prawn 4 → Cyclops 9** (three
sub-blueprints of three each, scattered across different biomes) **[V]**.

### 1.7 Death Stranding

Five connection-level stars per facility, raised by delivering; likes function as XP; each star may
grant a blueprint, a cosmetic, or **chiral bandwidth**, which is the budget for how much you may build
**[V]**. Sam's own Porter Grade has **five categories** (Bridge Link, Cargo Condition, Delivery Volume,
Delivery Time, Miscellaneous), rewarding **every 10 levels** — capacity **+5 kg** per tier, stamina,
balance, and, notably, **Miscellaneous is a likes-income multiplier**, i.e. an XP-gain stat **[V]**.

Two design facts that matter here more than the numbers:

**a) The real progression is infrastructure at a coordinate.** The terrain does not get easier because
a stat rose; it gets easier because there is a zipline (**300 m at Lv1, 350 m at Lv2**) or a road
(**2,000 metals + 2,000 ceramics + 300 chiral crystals per auto-paver segment, 52 segments in the
central region**) **[V]**. **This is the closest analogue in any shipped game to Rotorwash's charts and
fuel caches.**

**b) Kojima did not trust it alone.** On top of the infrastructure loop sit blueprints, five stat
ladders, a title ladder, and the likes system. And the honest criticism is worth quoting: as the network
builds out, "the minor jobs become almost humdrum milk runs and some vital value in the journey is
diminished" ([Coles, 2025](https://affectionatediscourse.substack.com/p/death-stranding-the-definitive-review)).
**A progression system that removes its own core activity eats itself.** Rotorwash's charts have exactly
this failure mode: perfect route knowledge makes flying trivial.

---

## 2. Outer Wilds and Subnautica — how knowledge actually becomes power

### 2.1 Outer Wilds

The designer says it outright. Kelsey Beachum's GDC 2021 deck carries a two-column slide **[V]**:

> **Rewards in Outer Wilds:** Knowledge
> **Rewards NOT in Outer Wilds:** Experience points · New abilities or gameplay mechanics · New items ·
> Upgrades · Vanity/customization options · New play areas unlocked · Improved or changed relationships
> with NPCs · Scores, rankings, or leader boards · Collectibles

and, with the parenthetical intact: "Knowledge is the only gameplay reward… It's also how you progress
in the game. *(Better hope your players REALLY like knowledge!)*"

Microsoft's launch post put it in one sentence: "You can't level up, upgrade your spaceship, or find new
power-ups… but you *can* learn about a secret entrance or how to avoid a deadly hazard"
([Xbox Wire](https://news.xbox.com/en-us/2019/05/30/outer-wilds-available-today-xbox-game-pass/)) **[V]**.

The full kit — jetpack suit, signalscope, scout launcher, translator, flashlight, ship, ship log — is
available in minute one and never changes **[V]**. The loop is **22 minutes**, scored so that the final
two minutes are the track "End Times": the clock is **audio, not UI** **[V]**. Main story is about
**16 hours** ≈ **44 loops** **[V]**.

**Five things that are easy to miss:**

**a) The log stores facts. The player supplies the inference.** Beachum, verbatim **[V]**:

> "Keeping track of what they've learned should not be the challenging part of the game."
> "Entries are only what we're 100% certain the player knows from reading that piece of found text.
> **NO leaps of logic, no inferences, etc.**"
> "We rely A LOT on inference — the player has to gather the available information, and then think about
> how it fits together… the player MUST be able to do this to progress."

This is a razor-sharp division of labour, and it is the thing most imitators get wrong. **The game does
the bookkeeping; the player does the thinking.** If Rotorwash's kneeboard ever writes "therefore the
valley at 340/12 is safe", it has taken the game away from the player. It should write what the source
said, and no more.

**b) The "more to explore" marker is the whole design, and it is a data field.** Entries carry Rumour
Facts and Explore Facts; facts carry a `SourceID` that literally draws the connecting line between
entries; big ideas are flagged `IsCuriosity` and get their own colour; and the more-to-explore asterisk
has explicit suppression flags (`IgnoreMoreToExplore`) so designers can mark facts as non-essential
**[V]**. Undiscovered entries appear as **orange "?" rumours with no photo and an empty description**;
new unread text gets a **green exclamation mark**; and Rumour Mode can be **switched off in the Options
menu** as a difficulty lever **[V]**.

That is a complete, implementable spec for a progress surface, and it fits Rotorwash almost unchanged.

**c) There is no gating whatsoever, and that is stated as a goal.** Beachum's slides: "enables them to
freely explore in search of answers — **no gating; nothing is off-limits**" and "**Zero gating = no
funnel points**" **[V]**. Asked whether a first-time player could beat the game in one loop: "**Yep!**
That is theoretically not impossible. It's just extremely unlikely. There is a tremendous amount of
design in place to ensure it doesn't." **[V]**

D-010 already says a skilled pilot can sometimes beat a gate by flying. **Protect that sentence with
your life.** The moment any threat envelope is implemented as `if (!hasChaff) deny`, the model is dead.

**d) The canonical example of knowledge-as-key is a piece of text about a corridor.** Beachum's own
worked illustration is the Nomai note reading "the lab currently can only be accessed from the path from
the Sunless City" **[V]**. Nothing physical blocks the route. The only thing between the player and the
High Energy Lab is not knowing the route exists. Her taxonomy names the tier explicitly: hidden content
is "**Places a player needs specific info to get to**" **[V]**.

**e) They built a safety net and measured the stuck player.** "Average time from game start to players
investigating Nomai text ranges from **30 minutes to 2–3 hours**" **[V]**. The Travellers' dialogue menus
route lost players by *mood*, not by objective — "I want to go somewhere no one's gone before" → the
Interloper or Dark Bramble; "I think I'll start with something small" → the Attlerock **[V]**. And
Verneau puts the cost at "perhaps **thousands of hours of playtesting**"
([Game Developer](https://www.gamedeveloper.com/design/live-die-repeat-how-i-outer-wilds-i-piques-curiosity-in-an-ambivalent-solar-system)) **[V]**.

**The scaling problem, stated plainly.** Outer Wilds delivers a fact every few minutes across ~16 hours
using 22-minute loops as a metronome. Rotorwash proposes knowledge progression across a 40–60 hour open
world — **roughly four times the runway on the same fuel**, with no metronome. §3.1 is the answer.

One more Beachum line that is directly a Rotorwash design note: "if you want to make people curious, you
can't expect someone to be curious about something specific unless they're already familiar with
everything in their immediate vicinity" **[V]**. A prototype failure taught them this — a thing falling
from space "super didn't work at all because **players weren't familiar with the village yet**" **[V]**.
**Hugh's home field must be thoroughly known before the first chart means anything.**

### 2.2 Subnautica

**a) The progress meter is a survival instrument.** Crush depth is a hard number checked against a hard
number, and the player is already staring at the depth gauge because they are frightened. Oxygen does
the same job with a viciously legible penalty: **1 unit/sec above 100 m, 3 units/sec at 100–200 m,
5 units/sec below 200 m**, against a reserve that runs 45 → 75 → 135 → 225 as tanks improve, with the
Rebreather flattening the penalty back to 1/sec at any depth **[V]**. Free-diving past 200 m is
effectively impossible without the Rebreather — **a soft gate layered underneath the hard crush-depth
gates**, expressed entirely in numbers the player already watches.

**Rotorwash has three of these lying unused: fuel remaining, endurance, and hover ceiling.** A refit
that raises hover ceiling by 400 m should print that number, because the player is already watching
density altitude to stay alive.

**b) The crafting tree is a map of the future.** The PDA shows locked entries greyed out, "allowing
players to track progression toward advanced technologies visually" **[V]**. You know the Cyclops exists
long before you can build it. Sharper still: the **Neptune Escape Rocket blueprint is obtained very
early**, in the Aurora's Captain's Quarters, and remains unbuildable for most of the game **[V]**. The
goal is visible almost throughout.

This is the *endowed progress effect* — Nunes & Drèze's car-wash study found **34% redemption on a
10-stamp card pre-stamped with 2, versus 19% on an 8-stamp card requiring identical purchases**, with the
important caveat that **the effect vanished when no reason was given for the head start** **[V]**.
**Rotorwash's refit screen must show empty hardpoints and unfilled avionics bays from hour one**, and
each empty socket must be labelled with what belongs there.

**c) Fragments are deliberate partial credit — and a scanner that never wastes a scan.** Two to four
fragments per blueprint is the norm, with the Cyclops at nine across three separate hunts in different
biomes **[V]**. Scanning a fragment for a blueprint you already own **converts it to 2 Titanium**, so
the action is never null **[V]**. **Rotorwash should almost never gate a capability behind a single
findable object**, and a duplicate part should always convert into something.

**d) There are six different ways to acquire a blueprint, and that is not an accident** **[V]**:
scanning fragments; data boxes (single pickup, no scanning); pre-installed in the PDA; **progressive
unlocks from the act of crafting** (crafting a High Capacity O₂ Tank unlocks the Rebreather blueprint);
story events (the Aurora explosion grants the Radiation Suit); and alien data terminals. Six acquisition
*verbs* is what keeps a 29-hour game from feeling like one repeated errand.

**e) The gate and the key are the same journey.** The Aurora's radiation stops you — **675 m radius
before the explosion, growing to 950 m after** **[V]** — and the Aurora contains the **Seamoth Depth
Module MK1 and the Prawn Suit fragments** **[V]**. The thing that blocks you is the thing that equips
you for the next tier. **This is the single most important pacing trick in the game and it maps directly
onto D-010: put the chaff dispenser on the wrong side of a radar-covered valley you can just barely
survive one crossing of.**

**f) The final gate is not equipment at all.** Disabling the Quarantine Enforcement Platform requires a
Purple Tablet **and** that the player has **cured themselves of the Kharaa bacterium** — and the player
can only know they are infected because they scanned themselves after the Disease Research Facility
**[V]**. Subnautica's last lock is a state change in the player's own body that they had to learn about
first.

**g) The radio is a progression-aware drip feed disguised as ambient fiction.** Distress calls are
"triggered by specific in-game actions like crafting items, spending time, or entering certain biomes"
**[V]**. And the first thing the repaired radio does is promise rescue in "9…9…9…9…9… hours" — 99,999
hours, about 11.4 years **[V]** — a joke that is also a design statement: *no one is coming; the only
progression is yours.*

Charlie Cleveland, on why there is no progress indicator at all **[V]**:

> "You'll work it out in the end. **We don't tell you anything. You can see there are no quests like you
> would see in most games, no progress indicator of where you are in the story.**"
> — [PCGamesN, 23 Jan 2018](https://www.pcgamesn.com/subnautica/subnautica-10-launch-survival-interview)

And on why the intrinsic-reward gamble is expensive but pays: "if they did get over that learning period
they would get to the point where they **internalized that activity as pleasurable on its own**" **[V]**.

Worth noting against the temptation to strip systems out: early Subnautica builds had **no hunger or
thirst**; they were added after criticism, and the team found they **helped orient players during early
gameplay** **[V]**. Survival pressure was retrofitted as a *pacing and direction* device. D-007's fuel
and wear loop is the equivalent, and it should be treated as navigational furniture, not just tension.

### 2.3 What would have to be true of Rotorwash

| # | Requirement | Currently satisfied? |
|---|---|---|
| 1 | Every chart, frequency and schematic changes what the player **does next sortie**, not what they know about the world | **No** — not stated anywhere |
| 2 | A persistent surface showing what is known **and where there is more to know** | **No** — the single biggest gap |
| 3 | The surface stores **facts only**; the player supplies the inference | **No** — unspecified |
| 4 | Capabilities assembled from **multiple parts**, so most scavenging yields partial credit | **No** |
| 5 | Every gate physically real, so a good pilot beats it early | **Yes** — explicit in D-010 |
| 6 | The refit screen shows **empty sockets** before they can be filled | **No** |
| 7 | A number the player already watches for survival that refits visibly move | **Partly** — the sim produces hover ceiling, endurance, torque margin; nothing surfaces them as progress |
| 8 | The gate and its key on the **same journey** | **No** — unspecified |

Two of eight. That is the honest score on the knowledge half of D-005 today.

---

## 3. The hard problems, stated plainly

### 3.1 Without XP, what supplies the frequent small rewards?

**Why XP bars work, with real citations rather than folk wisdom.** John Hopson's "Behavioral Game
Design" is the canonical source: *ratio* schedules produce "a long pause, then a steady burst of
activity"; *variable ratio* produces "the highest overall rates of activity"; and the governing principle
is that "activity level is a function of how soon the participant expects a reward to occur"
([Game Developer, 2001](https://www.gamedeveloper.com/design/behavioral-game-design)) **[V]**. A visible
XP bar's real job is to convert an invisible schedule into a legible one — it tells you how close the
next reward is. Add the endowed-progress/goal-gradient effect (§2.2b) and Clark et al.'s near-miss work,
which found near-misses were rated **less pleasant than full misses yet increased desire to continue**,
recruiting win-related striatal circuitry, and only when the subject had a sense of personal control
([*Neuron* 61(3), 2009](https://pmc.ncbi.nlm.nih.gov/articles/PMC2658737/)) **[V]**.

That last qualifier matters: **the near-miss only works where the player believes their skill was
involved.** A near-miss in Rotorwash — the SAM that misses, the tail rotor you keep by six inches — is
therefore *more* motivating than a slot machine's, not less. The design has this for free and should
lean on it.

The counterweight, and it should be in the wiki too. Jonathan Blow, MIGS 2007: "**Rewards are a way of
lying to the player so they feel good and continue to play the game**", and his challenge to the
industry's scheduled-reward best practice — "Would they still want to play our game if we removed the
scheduled rewards?"
([Game Developer report](https://www.gamedeveloper.com/pc/migs-2007-jonathan-blow-on-the-i-wow-i-drug-meaningful-games)) **[V]**.
Ian Bogost on points systems: "Organizations ask for loyalty, but they reciprocate that loyalty with
shams, counterfeit incentives that neither provide value nor require investment"
([Exploitationware, 2011](https://www.gamedeveloper.com/design/persuasive-games-exploitationware)) **[V]**.
D-005 is on the right side of this argument. The task is to be on the right side of it *and* keep the
player's hands busy.

**The honest framing of Rotorwash's problem.** New Vegas rewards roughly every 30–60 minutes and gives a
*choice* every ~2 hours. Fallout 4 gives a choice every level indefinitely. Subnautica gives a craftable
every few minutes early. An authored-parts model naturally produces a reward every *several hours* and,
unlike an XP curve, **cannot be tuned with a constant** — you would have to author more content.

**How the non-XP games solve it:**

| Game | The microloop |
|---|---|
| S.T.A.L.K.E.R. | Stashes as map pins, money, ammo scarcity, artifacts, repair costs. Every corpse is a transaction |
| Subnautica | Fragments (partial credit, never null), resources, the tree filling in, radio drip feed |
| Death Stranding | Likes, Porter Grade increments every 10 levels, every structure you place persisting |
| Elite | Credits ticking, module swaps, engineering material grades (caps 300/250/200/150/100) **[V]** |
| Kenshi | Skill ticks — and it pays 6–10× for losing **[V]** |
| Outer Wilds | A log entry every few minutes; the 22-minute loop as metronome |

**Recommendation — five microloops, none of which are XP:**

1. **Consumables as the heartbeat.** Fuel, ammunition, chaff, flares, hydraulic fluid, blade tape,
   filters. Forty litres of Jet-A is a small, frequent, unambiguously good event, and D-007 already makes
   fuel core. Cheapest and strongest fix available.
2. **Partial credit on everything** (§2.2c). "RWR antenna set — 2 of 4" is a progress bar made of
   objects. Duplicates must convert into something, as Subnautica's scanner does.
3. **Wear reversal as reward.** Because components degrade, *restoring* is a capability you get back —
   and it is the only reward channel that can fire arbitrarily often **without authoring new content**.
4. **Rentable knowledge — the New Vegas skill magazine.** A one-use weather window, a single day of a
   rotating radar schedule, a smuggler's one-time escort, a stolen IFF code good for one pass. This is
   what keeps the map from feeling locked while the real key is three hours away.
5. **A Kenshi-style payoff for the bad sortie.** Getting painted, getting hit, limping home — these
   should *produce something*. The obvious diegetic form: the RWR logs the emitter it detected, so the
   sortie that nearly killed you hands you the threat's location and signature. **Failure becomes
   survey data.** This single mechanic converts Rotorwash's worst moments from pure cost into
   progression, exactly as SOL does for Kenshi.

**Target cadence — a constraint that does not currently exist anywhere in the wiki:**

| Tier | Interval | Example |
|---|---|---|
| Micro | 8–15 min | fuel, ammo, a part fragment, a wear repair, an emitter logged, a landmark named |
| Small | 45–90 min | an instrument, a tool, a frequency, a local chart, a minor refit |
| Medium | 3–5 h | an assist box, a countermeasure, an aux tank, a contact |
| Large | 8–12 h | a region opens: engine, RWR + chaff, night capability, the hangar |

### 3.2 Signposting progress with no number

Daniel Cook's skill-atom model names the failure mode precisely: **burnout**, which occurs when a
mastered skill has no further interesting application — "After a short period of experimentation with no
interesting results, the player stopped pressing the jump button entirely" — and burnout *cascades*,
because an early dead end blocks later content
([The Chemistry of Game Design](https://www.gamedeveloper.com/design/the-chemistry-of-game-design)) **[V]**.
The same piece is reassuring about grind: "Players have enormous patience. They are willing to exercise a
basic skill atom thousands of times" — *provided the feedback loop closes.*

Four devices, all shipped somewhere:

**a) The aircraft itself.** This is D-005's strongest claim and it is underexploited. **Mandate: every
acquisition above Micro must produce an exterior mesh change or a new cockpit instrument.** A screenshot
from hour 2 and one from hour 40 must not look like the same machine. No invisible upgrades, ever.

**b) The cockpit as the HUD.** Make instruments themselves items. Start with a broken airspeed indicator
and no radar altimeter, so the HUD literally grows. This is free legibility — a new needle appears and
it is immediately useful — and it solves the tutorial problem, because a cockpit with three working
gauges is one a new player can read.

**c) The map's ink.** Make the *rendering style* carry the state: hand-drawn scrawl where you have only
flown; printed chart where you hold the sectional; annotated overlay where you have terrain-masking
data; red domes where you have the emitter survey. One glance shows your whole progression.

**d) The kneeboard.** The Outer Wilds ship log and the Subnautica PDA, merged, built to the spec in
§2.1b: aircraft status with **empty bays shown**; chart coverage; known frequencies, contacts and threat
sites; and **open threads** with a more-to-explore marker ("Marker Hill — emitter located, type
unknown"). Facts only, no inferences (§2.1a). This is the single missing artefact in the design.

### 3.3 Preventing lethal sequence-breaking when the player can fly anywhere

Terrain cannot gate a helicopter and D-010 is right that threat envelopes are the answer. The failure
modes are visible in shipped games:

- **Far Cry 6** implements airspace denial with anti-aircraft sites that destroy your aircraft after a
  short warning, the counterplay being "fly very low" **[V]**. It is experienced as an irritation rather
  than a gate, because the punishment is total, instant and unearned.
- **Elden Ring** soft-gates by lethality, and the player's internal question is "**I could be here, but
  should I be here?**" — with the observation that the freedom to walk away "is also a clever piece of
  misdirection that makes the player believe that they are in control"
  ([Gamers With Glasses](https://www.gamerswithglasses.com/features/eldenring-openworld)) **[V]**.
- **Breath of the Wild** puts nothing at all in your way, "not even an invisible wall"
  ([The Ringer](https://www.theringer.com/2023/05/12/video-games/the-legend-of-zelda-breath-of-the-wild-legacy-nintendo)) **[V]**.
- The best statement of the principle comes from Tunic's Andrew Shouldice: "**If you leave a door open
  just a crack, so people, if people are really intent on exploring every nook and cranny, can pry that
  door open**"
  ([Game Developer, 2022](https://www.gamedeveloper.com/design/designing-content-for-no-one-an-interview-with-the-team-behind-tunic)) **[V]**.

Note for the wiki's own rigour: **"soft gating" is community vocabulary, not established design
literature** — the formal writing uses "natural barriers", "difficulty gating", "soft walls" **[V]**.
The most useful formal statement is Kellman's: "Natural obstacles include mountains, bodies of water,
locked gates… **Natural barriers are the best, but invisible barriers are a necessary evil**"
([Game Design Skills](https://gamedesignskills.com/game-design/open-world/)) **[V]**.

Given that losing the aircraft is the real fail state (pillar 1), an instant-kill SAM is unacceptable.
Six mechanisms, in order of preference:

1. **Graded lethality.** A hit should almost never destroy Hugh. It should take the tail rotor, the
   hydraulics, or start a fuel leak — leaving an autorotation and a walk home, exactly as D-007 already
   requires for engine failure. The punishment is a long, expensive, *interesting* recovery, not a
   reload.
2. **Warning before commitment.** Envelopes must be announceable from outside — which is why the RWR
   should be early, and arguably *given*. Gating information behind the same currency as capability is
   precisely what creates coin-flip deaths.
3. **People tell you.** "Don't go north of the river, they've got something up there that took down a
   drone." Free, in tone, and it makes contacts load-bearing.
4. **Range as the honest soft gate.** Fuel is the one constraint a helicopter cannot ignore. Distant
   regions reachable only with the aux tank or a cache you established gates by **preparation**, not
   permission, and it is already in the sim.
5. **Density altitude.** The measured **~3,000 m OGE hover ceiling** **[V, from `01-flight-model.md`]**
   means high terrain is genuinely gated by what you are carrying, on a hot day. A real physical gate
   that costs nothing to build.
6. **At least one gate beatable by flying alone at hour 5.** That single fact converts the whole system
   from "locks" into "challenges" in the player's mind.

### 3.4 Avoiding "found a part, nothing changed"

This is the most likely failure mode, because it fails *quietly*. The relevant design literature is the
Metroidvania "new verb" argument: Josh Bycer's requirement that "**in order for the game to be a
metroidvania there must be game-changing upgrades**", and his named pitfall — **"keys"**, upgrades usable
in exactly one place ([Game Wisdom](https://game-wisdom.com/critical/metroidvania-design)) **[V]**. Note
also that the canonical genre definition explicitly includes acquiring "special items, tools, weapons,
abilities, **or knowledge**" **[V]** — knowledge is already in the definition.

**The rule: no acquisition ships without all four of these.**

| Layer | Requirement |
|---|---|
| **Physical** | Exterior mesh change or a new cockpit instrument. Visible from the chase camera or from the seat |
| **Numeric** | A printed before/after on a number the player already watches — hover ceiling, endurance, Vne, torque margin, lock-on time |
| **Procedural** | A new thing the player's hands do, or an old thing they no longer have to do |
| **Immediate** | Somewhere to use it *this sortie*, ideally visible from where it was found (§2.2e) |

**Anti-patterns to ban outright:**
- Any upgrade whose entire effect is a percentage the player cannot perceive in one sortie.
- Any chart that reveals map the player would have revealed by flying anyway.
- Any part strictly better than what it replaces with no trade. D-011 is right — armour costs hover
  ceiling, a tank costs balance — and the trade is what makes the acquisition a *decision*, which is what
  makes it memorable. S.T.A.L.K.E.R.'s SEVA-versus-Exo and Elite's D-versus-A-versus-B ratings are the
  proof that non-monotonic ladders keep old gear alive.
- Any upgrade that is a **key** in Bycer's sense — useful once, then dead.

---

## 4. A concrete progression ladder

36 acquisitions over a ~50-hour main path. **Sim field** names are real fields in `sim/` and
`game/scripts/`; this ladder is built to be implementable against the code as it stands today.

Four interleaved tracks: **AVI** (avionics/instruments), **SURV** (survivability), **LIFT**
(power/range/load), **KNOW** (charts, frequencies, contacts — gained by flying and talking, not by
scavenging).

### Act 0 — A machine you barely trust (0–2 h)

| # | h | Acquisition | Track | Sim / system change | What visibly changes |
|---|---|---|---|---|---|
| 1 | 0.0 | **Hugh, as found** | — | `SasAuthority = 0`; 400 of 1100 kg fuel; HUD = attitude, Nr, torque only | Baseline. Hand-flown, wandering, no airspeed reference |
| 2 | 0.3 | **Airspeed indicator overhaul** (needs safety-wire pliers) | AVI | IAS tape on HUD | You can hold the measured 58 kt minimum-power speed instead of guessing. Endurance improves immediately because you stop flying draggy |
| 3 | 0.6 | **Chip detector + magnetic sump plug** | AVI | Transmission wear surfaces ~4 min before failure | Wear stops being an ambush |
| 4 | 1.0 | **1:250,000 sectional, home sector** | KNOW | Reveals one region's terrain and obstructions | The map stops being blank. You plan instead of wander |
| 5 | 1.3 | **Two 20 L jerry cans + rack** | LIFT | `+32 kg` MassItem aft; CG shifts; +~7 min endurance | Hugh sits nose-high on the skids. You reach the next valley |
| 6 | 1.8 | **Survival radio, guard frequencies** | KNOW | Distress beacons audible; 3 POIs appear | The world starts talking to you unprompted |

### Act 1 — Making Hugh a usable aircraft (2–12 h)

| # | h | Acquisition | Track | Sim / system change | What visibly changes |
|---|---|---|---|---|---|
| 7 | 2.5 | **Door removal pins** | LIFT | −28 kg; `DragArea.Y` up slightly; enables side weapons and fast egress | Exterior change. Colder, louder, quicker out |
| 8 | 3.0 | **SAS box (rate gyro pack), worn** | AVI | `SasAuthority = 0.35`, rate loops only | The aircraft stops wandering. You can look at the map for three seconds |
| 9 | 4.0 | **Contact: a trader's net frequency** | KNOW | Prices and stock readable before you launch; landing clearance | A settlement becomes a service, not a place |
| 10 | 5.0 | **Cargo hook / external load rig** | LIFT | Slung MassItem below CG; pendulum mode; effective Vne ~60 kt loaded | New verb: move heavy things. New failure: load swing |
| 11 | 6.0 | **Radar altimeter + low-alt bug** | AVI | AGL readout and audio warning | You can fly at 15 feet and live. **Prerequisite for every terrain-masking play in the game** |
| 12 | 7.0 | **Terrain-masking chart, corridor A** | KNOW | A drawn low route with dead-ground annotation; halves detection along it | A wall becomes a door, and it cost you a conversation, not a part |
| 13 | 8.0 | **Attitude-hold / 3-axis autopilot** | AVI | `AutopilotDemand.PitchAttitude / RollAttitude`; `SasAuthority = 0.8` | Genuine hands-off for ten seconds. Binoculars in flight |
| 14 | 9.0 | **Aux fuel bladder (internal)** | LIFT | `FuelCapacity 1100 → 1560 kg`; hover ceiling −~400 m; cabin space lost | Range +45%, and now you cannot carry passengers or hover at the high mine |
| 15 | 10.0 | **Contact: a mechanic with a shop** | LIFT | Component overhaul restores wear; trades parts | Wear stops being one-way. **This is the reward-cadence workhorse** |
| 16 | 11.0 | **Blade balance kit (tool)** | AVI | Removes 2/rev vibration: control precision restored, Rotor Time recharge +20% | The screen stops shaking and you realise how bad it had been |

### Act 2 — The threat envelope (12–30 h)

| # | h | Acquisition | Track | Sim / system change | What visibly changes |
|---|---|---|---|---|---|
| 17 | 12 | **Radar warning receiver** — head unit + antennas + loom + threat library (4 parts) | SURV | Threat strobes and audio; emitters appear on HUD and map; **detections are logged permanently** | *"The map stops being a coin flip."* And every frightening sortie now leaves survey data behind |
| 18 | 13 | **Emitter survey / SIGINT log** | KNOW | Known sites plotted with range rings | The gate becomes a geometry problem you can solve on the ground |
| 19 | 14 | **Chaff dispenser + cartridges** | SURV | Breaks radar lock; consumable; exterior box | You can cross one open valley per sortie, and you count cartridges |
| 20 | 16 | **IR exhaust suppressor** | SURV | MANPADS lock time 2 s → 6 s; ~35 kW hover power cost | Big visible stack. You can fly low near people; you climb worse |
| 21 | 17 | **Flare dispenser + flares** | SURV | Defeats an IR shot inside the lock window | You survive the shot you did not see coming |
| 22 | 18 | **Armour: floor and seat plate** | SURV | `+80 kg` MassItem; small-arms protection; hover ceiling −~250 m | You stop fearing rifles and start fearing hot-and-high. The trade is the point |
| 23 | 19 | **Door gun mount + gun** | SURV | Hardpoint occupancy, `+52 kg`, suppressive fire abeam | New verb in the air; forces you to fly the pass rather than hover |
| 24 | 21 | **Doppler / inertial nav set** | AVI | Groundspeed, drift, waypoint steering; dead reckoning with no landmarks | You can fly in cloud and still arrive |
| 25 | 23 | **Fixed forward guns + reflector sight** | SURV | The air-to-ground Rotor Time weapon | Rotor Time in the air stops being decorative and becomes the reason to fly the wingover |
| 26 | 25 | **Emitter locator / jammer pod** | SURV | Triangulates a site over two passes and marks it | *"Turns a wall into a target."* Sites become raidable objectives |
| 27 | 27 | **NVG tubes + compatible cockpit lighting** | AVI | Night flight viable; ground detection roughly halved at night | An entire second world: the map you already know, at night, safer |
| 28 | 28 | **Seasonal wind and density-altitude atlas** | KNOW | Forecast hover ceiling and wind at destination | You stop being surprised by a hot afternoon at 2,000 m. Pure knowledge, no part |

### Act 3 — A different aircraft (30–50 h)

| # | h | Acquisition | Track | Sim / system change | What visibly changes |
|---|---|---|---|---|---|
| 29 | 30 | **Higher-rated turbine** | LIFT | `MaxContinuousPower 1.1 → 1.4 MW` | More power — and you are now torque-limited by a transmission you did not upgrade. **The upgrade creates a problem** |
| 30 | 32 | **Transmission upgrade** | LIFT | `TransmissionTorqueLimit 48 → 58 kN·m` | The engine you already own suddenly works. A deliberate two-part reward: the second half makes the first legible |
| 31 | 34 | **Wide-chord blades / improved head** | LIFT | `RotorConfig.Chord 0.53 → 0.68`; `RotorInertia` up | Better autorotation margin, higher Vne, different blade slap. **Hugh sounds different** |
| 32 | 36 | **External stores pylons** | LIFT | Two hardpoints; asymmetric mass; drag | The classic trade made explicit: range or teeth |
| 33 | 38 | **The completed frequency** (story) | KNOW | The search target's net; critical path advances | The reason you have been flying for 38 hours |
| 34 | 40 | **A hangar** (contact + place) | LIFT | Field overhaul, part fabrication, airframe-level work | D-011's free-form builder opens. Hugh can stop being a Huey |
| 35 | 44 | **Salvaged rotor system + welder** | LIFT | Free-form airframe construction | The endgame builder, correctly placed at the end |
| 36 | 48 | **High-altitude kit: particle separator + dual hydraulics** | LIFT | Reduced density lapse; hover ceiling +~600 m | The mountains open. The last region on the map |

**Cadence check.** 36 acquisitions over 50 hours is one every ~83 minutes, which is the **Small** tier
in §3.1 and nothing else. **The five microloops are not decoration** — without them this ladder has
80-minute gaps filled with nothing. For comparison: S.T.A.L.K.E.R. runs a similar count over a much
shorter game and fills the gaps with money, ammo scarcity and stash pins.

**Ordering notes.**
- Items **17–21** are the D-010 spine and should be the least re-orderable.
- Items **2, 3, 11, 24, 27** are the instrument ladder — the cheapest, most legible rewards in the
  game. If budget is tight, build these first.
- Item **17** carries an extra clause on purpose: the RWR logging its own detections is what implements
  the Kenshi payoff (§3.1.5). It should be in the spec from day one, not added later.
- Items **29–30** are deliberately a split reward. Elite's A-versus-D-versus-B and S.T.A.L.K.E.R.'s
  branch-exclusive trees both show that the interesting part of an upgrade is the constraint it exposes.

---

## 5. Scores against the genre

| Dimension | Score | Justification |
|---|---|---|
| **Pacing** | **5 / 10** | Large-unlock pacing (a region every ~8–12 h) is competitive with Subnautica's depth tiers and better than Elite's ~19× credit jumps. But nothing in D-005 or D-010 specifies a cadence, there is no equivalent of New Vegas's 10–17 points per level or Fallout 4's guaranteed per-level choice, and an authored-parts model **cannot be tuned with a constant**. S.T.A.L.K.E.R. gets away with ~12–20 growth events because it is a 15–25 hour game; Rotorwash is proposing 40–60. Scores 5 because the fix is cheap and mostly implied by D-007, not because the spec is adequate |
| **Legibility** | **6 / 10** | Gear-as-skill-tree is inherently more legible than an abstract tree — an attitude-hold box does something you feel in one second, which beats "+3 Persuade" — and the sim already computes hover ceiling, endurance and torque margin. But there is no progress surface specified, no rule that acquisitions must be visible, and "acquired a chart" is currently invisible. Subnautica's depth gauge and greyed-out tree, and Outer Wilds' rumour map with its orange asterisk and `IgnoreMoreToExplore` flags, are both complete, shipped specs that Rotorwash has not written down |
| **Build variety** | **7 / 10** | The mass/CG/drag/power model makes trades **real** rather than tabular: armour genuinely costs hover ceiling, an asymmetric tank genuinely rolls the aircraft. Almost no RPG can claim that, and it puts Rotorwash structurally alongside S.T.A.L.K.E.R.'s SEVA-vs-Exo and Elite's D-vs-B ratings rather than alongside a linear tier ladder. Held back by one airframe, one weapon context, and a mostly linear ladder — Fallout 4's 275 perk ranks or Elite's 45 hulls produce far more divergence. Would be 8–9 with mutually exclusive branches |
| **Sense of growth** | **7 / 10** | The spatial payoff is the design's best feature: D-010 makes progression *geographic*, which is a stronger felt reward than any level-up in Fallout, and it is the same structure as Kenshi's AI-Core gate and S.T.A.L.K.E.R.'s calibration tools. Add the genuine growth of the player's own flying, which is Outer Wilds' trick and is real here. Loses points because between region-openings there is currently nothing, and because the pilot never changes at all — an axis every game in this list uses except Outer Wilds, which compensates with a 22-minute metronome Rotorwash does not have |
| **Replay value** | **4 / 10** | The weakest dimension by a distance. A fixed authored ladder, one airframe, one story target, no build archetypes, no mutually exclusive faction rewards. New Vegas replays on four faction endings and radically different builds; Kenshi replays because it is a sandbox; S.T.A.L.K.E.R. replays on different routes and artifact belts. Rotorwash as specified replays the same 36 acquisitions in roughly the same order. Either fix it deliberately or accept it as a single-playthrough game and design accordingly |

**Mean: 5.8 / 10** — "a good idea that has not yet been turned into a system."

---

## 6. The three changes that matter most

### 1. Build the kneeboard, and make every acquisition obey the four-layer rule

One screen: aircraft status with **empty bays shown** (endowed progress, §2.2b), chart coverage,
frequencies, contacts, threat sites, and **open threads with a more-to-explore marker** (§2.1b). It
stores **facts only** — no inferences, no conclusions, no "therefore" (§2.1a). Plus the hard rule from
§3.4: nothing ships without a visible physical change, a printed numeric delta, a procedural change, and
somewhere to use it within one sortie.

Cheap corollary with a high return: **make the instruments themselves items**, so the HUD physically
grows as the game proceeds.

### 2. Commit to the four-tier cadence and build the microloop — including a payoff for the bad sortie

Write the cadence table from §3.1 into `02-progression.md` as a constraint, and implement all five
microloops. The fifth is the one nobody else in this genre has and it is nearly free: **make the RWR log
every emitter that paints you.** The sortie that nearly killed you then hands you the threat's location
and signature, which is Kenshi's Stronger Opponent Logic expressed in avionics — the game pays you for
being outmatched. It also solves §3.3's information problem in the same stroke.

### 3. Make threat envelopes graded, and give the RWR away

No SAM should destroy Hugh outright — it should take a system and leave an autorotation, per D-007's own
principle. Far Cry 6 is the counter-example: total, instant, unearned punishment reads as an irritation
rather than a gate. And threat *information* must not be gated by the same currency as threat
*protection*: a player who cannot see the envelope cannot make a decision, and a death they could not
have anticipated reads as unfairness. Give the RWR early or make it the mandatory first find, then let
chaff, flares and the suppressor be the earned part. Keep D-010's promise that a good pilot can beat a
gate by flying — leave the door open a crack.

**Honourable mention — the replay problem.** Score 4 is the outlier and it has an obvious fix worth
considering before the ladder is locked: make Acts 2 and 3 **branching and mutually exclusive**, the way
S.T.A.L.K.E.R.'s upgrade nodes and Elite's one-mod-per-module rule are. A heavy armoured gunship, a
stripped long-range machine and a quiet night-flying airframe cannot all be the same aircraft, because
mass is real and the sim already enforces it. Three viable Hughs is three playthroughs, and it costs
almost nothing beyond restraint about what the player may bolt on at once.

---

## Sources

**Progression systems**
- [Fallout: New Vegas — Level](https://fallout.wiki/wiki/Level_(Fallout:_New_Vegas)) · [Skills](https://fallout.wiki/wiki/Fallout:_New_Vegas_skills) · [Perks](https://fallout.wiki/wiki/Fallout:_New_Vegas_perks) · [Implants](https://fallout.wiki/wiki/Fallout:_New_Vegas_implants)
- [Fallout 4 — Level](https://fallout.wiki/wiki/Level_(Fallout_4)) · [Perks](https://fallout.wiki/wiki/Fallout_4_perks) · [Idiot Savant](https://fallout.wiki/wiki/Idiot_Savant)
- [Skyrim — Leveling (UESP)](https://en.uesp.net/wiki/Skyrim:Leveling) · [Skills](https://en.uesp.net/wiki/Skyrim:Skills) · [Smithing](https://en.uesp.net/wiki/Skyrim:Smithing)
- [The Outer Worlds — Skills (Fextralife)](https://theouterworlds.wiki.fextralife.com/Skills) · [Perks](https://theouterworlds.wiki.fextralife.com/Perks) · [Skills (Gamer Guides)](https://www.gamerguides.com/the-outer-worlds/guide/character-creation/help/skills)
- [S.T.A.L.K.E.R. — Tools](https://stalker.fandom.com/wiki/Tools) · [Technicians in Call of Pripyat](https://stalker.fandom.com/wiki/Technicians_in_Call_of_Pripyat) · [Artifacts](https://stalker.fandom.com/wiki/Artifacts) · [Exoskeleton](https://stalker.fandom.com/wiki/Exoskeleton) · [SEVA suit](https://stalker.fandom.com/wiki/SEVA_suit) · [Stash](https://stalker.fandom.com/wiki/Stash) · [Call of Pripyat (Wikipedia)](https://en.wikipedia.org/wiki/S.T.A.L.K.E.R.:_Call_of_Pripyat)
- [Kenshi — Guide to Training Statistics](https://kenshi.fandom.com/wiki/Guide_to_Training_Statistics) · [Statistics](https://kenshi.fandom.com/wiki/Statistics) · [Tech Level](https://kenshi.fandom.com/wiki/Tech_Level_(Tech)) · [Robot Limbs](https://kenshi.fandom.com/wiki/Robot_Limbs)
- [Elite Dangerous — Outfitting](https://elite-dangerous.fandom.com/wiki/Outfitting) · [Engineers](https://elite-dangerous.fandom.com/wiki/Engineers) · [Materials](https://elite-dangerous.fandom.com/wiki/Materials) · [Federation ranks](https://elite-dangerous.fandom.com/wiki/Federation/Ranks) · [Empire ranks](https://elite-dangerous.fandom.com/wiki/Empire/Ranks) · ship pages for [Cobra Mk III](https://elite-dangerous.fandom.com/wiki/Cobra_Mk_III), [Asp Explorer](https://elite-dangerous.fandom.com/wiki/Asp_Explorer), [Python](https://elite-dangerous.fandom.com/wiki/Python), [Anaconda](https://elite-dangerous.fandom.com/wiki/Anaconda), [Federal Corvette](https://elite-dangerous.fandom.com/wiki/Federal_Corvette), [Imperial Cutter](https://elite-dangerous.fandom.com/wiki/Imperial_Cutter)
- [Subnautica — Seamoth](https://wiki.subnautica.com/sn/Seamoth) · [Prawn Suit](https://wiki.subnautica.com/sn/Prawn_Suit) · [Cyclops](https://wiki.subnautica.com/sn/Cyclops) · [Fragments](https://wiki.subnautica.com/sn/Fragments_(Subnautica)) · [Blueprints](https://wiki.subnautica.com/sn/Blueprints) · [Oxygen](https://wiki.subnautica.com/sn/Oxygen) · [Radiation](https://wiki.subnautica.com/sn/Radiation) · [Aurora](https://wiki.subnautica.com/sn/Aurora) · [Quarantine Enforcement Platform](https://wiki.subnautica.com/sn/Quarantine_Enforcement_Platform) · [Radio](https://wiki.subnautica.com/sn/Radio) · [Walkthrough](https://wiki.subnautica.com/sn/Walkthrough)
- [Death Stranding — facility level rewards (PowerPyx)](https://www.powerpyx.com/death-stranding-all-facility-level-rewards-unlocks/) · [connection level](https://samurai-gamers.com/death-stranding/connection-level-and-chiral-communication-traffic/) · [Porter Grade](https://www.inverse.com/article/60767-death-stranding-likes-how-to-get-porter-grade) · [structures](https://samurai-gamers.com/death-stranding/structures/) · [roads](https://screenrant.com/build-roads-guide-death-stranding/)
- [Outer Wilds (Wikipedia)](https://en.wikipedia.org/wiki/Outer_Wilds) · [Xbox Wire launch post](https://news.xbox.com/en-us/2019/05/30/outer-wilds-available-today-xbox-game-pass/) · [ship log data model (New Horizons docs)](https://nh.outerwildsmods.com/guides/ship-log/)

**Designer commentary**
- Kelsey Beachum, *Sparking Curiosity-Driven Exploration Through Narrative in Outer Wilds*, GDC 2021 — [slides](https://media.gdcvault.com/GDC+2021/beachum_gdc_2021(1).pdf) · [video](https://www.youtube.com/watch?v=QaGu9tGCNbI)
- Alex Beachum — [Road to the IGF](https://www.gamedeveloper.com/design/road-to-the-igf-alex-beachum-s-i-outer-wilds-i-) · [Live, die, repeat](https://www.gamedeveloper.com/design/live-die-repeat-how-i-outer-wilds-i-piques-curiosity-in-an-ambivalent-solar-system)
- Charlie Cleveland — [PCGamesN interview, Jan 2018](https://www.pcgamesn.com/subnautica/subnautica-10-launch-survival-interview) · *The Design of Subnautica*, GDC 2019 ([GDC Vault](https://www.gdcvault.com/play/1025745/The-Design-of-Subnautica))
- Andrew Shouldice & Eric Billingsley on Tunic — [Game Developer, Mar 2022](https://www.gamedeveloper.com/design/designing-content-for-no-one-an-interview-with-the-team-behind-tunic)

**Design theory**
- John Hopson, [Behavioral Game Design](https://www.gamedeveloper.com/design/behavioral-game-design), Gamasutra 2001
- Daniel Cook, [The Chemistry of Game Design](https://www.gamedeveloper.com/design/the-chemistry-of-game-design), 2007
- Jonathan Blow, Design Reboot, MIGS 2007 — [report](https://www.gamedeveloper.com/pc/migs-2007-jonathan-blow-on-the-i-wow-i-drug-meaningful-games) · [video](https://www.youtube.com/watch?v=K0kup_anLeU)
- Ian Bogost, [Persuasive Games: Exploitationware](https://www.gamedeveloper.com/design/persuasive-games-exploitationware), 2011
- Clark, Lawrence, Astley-Jones & Gray, "Gambling near-misses enhance motivation to gamble and recruit win-related brain circuitry", *Neuron* 61(3), 2009 — [PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC2658737/)
- Nunes & Drèze, "The Endowed Progress Effect", *JCR* 32(4), 2006 — [summary](https://loyaltyrewardco.com/loyalty-psychology-series-endowed-progress-effect/)
- Jamie Madigan, [The Zeigarnik Effect and Quest Logs](https://www.psychologyofgames.com/2013/03/the-zeigarnik-effect-and-quest-logs/) — note the 2025 meta-analytic caveat via [Ovsiankina effect](https://en.wikipedia.org/wiki/Ovsiankina_effect)
- Josh Bycer, [The 3 Essential Elements of Metroidvania Design](https://game-wisdom.com/critical/metroidvania-design) · [Metroidvania (Wikipedia)](https://en.wikipedia.org/wiki/Metroidvania) · [Sequence breaking](https://en.wikipedia.org/wiki/Sequence_breaking)
- Nathan Kellman, [Open-world design](https://gamedesignskills.com/game-design/open-world/) · Gemma Ellison, [Invisible Walls](https://www.wayline.io/blog/invisible-walls-guiding-freedom-open-world-games) · Nathan Schmidt, [Elden Ring's open world](https://www.gamerswithglasses.com/features/eldenring-openworld) · Lewis Gordon, [The Boundless Legacy of Breath of the Wild](https://www.theringer.com/2023/05/12/video-games/the-legend-of-zelda-breath-of-the-wild-legacy-nintendo)
- [Far Cry 6 anti-aircraft sites](https://farcry.fandom.com/wiki/Anti-Aircraft_Sites) — airspace-denial precedent

**Real hardware for the avionics ladder**
- [AN/APR-39 and helicopter defensive avionics (FAS)](https://fas.org/man/dod-101/sys/ac/equip/aec-pam-9811/sec6a.html) · [AN/ASN-128 Doppler navigation set (BAE)](https://www.baesystems.com/en-us/product/an-asn-128-doppler-navigation-set)

**Not verified this session** — Elite engineering grade mechanics beyond the reputation tiers; Kenshi
squad limits and income curve; S.T.A.L.K.E.R. weapon price ladder, detector tiers, Clear Sky gating
mechanism, and all of S.T.A.L.K.E.R. 2; Death Stranding per-star unlocks for the zipline, Roadster and
PCC Lv.3; the exact Aurora explosion timer (sources conflict between "day 4" and a 46–80 minute
real-time timer); minutes-per-level for the four XP games; all HowLongToBeat figures (relayed
second-hand only).
