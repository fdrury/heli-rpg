# The story — ROTORWASH

*Written 2026-09-19, against the code as it stands at `a7ff22c` plus the uncommitted
contract-board and search-thread work in the tree.*

This is the narrative spine the benchmark pass said was missing (D-048, mission structure
2/10). It does not re-open the premise: no other helicopter pilots (D-008), one specific
search, fog of war as the gate (pillar 4), gear-and-knowledge progression with no XP
(D-005), mature but not grim-dark.

It is written to be built. Every beat below names the site kind it lives on, the trigger
that fires it, and the carrier that delivers it. Section 7 lists — honestly — what this
design asks for that the engine cannot do today, and what the smallest working version of
each is. **Section 7.0 is a correction to code that is already in the working tree and
should be read first by whoever owns it.**

**The one-line spine:** you are flying a machine that is running out of the one part you
cannot make, looking for the one person who knows how to make it last; you find her four
hours' flight away, alive, and she will trade you the rotor for the flight she has needed
somebody to make for six years.

---

## 0. The rule this document obeys

D-005a set the rule for the kneeboard: **facts, not inferences**. The story obeys the same
rule. Nothing in ROTORWASH tells the player how they feel, what they have achieved, or how
far through they are. The game records what was found, where, when, and in whose
handwriting. The arc is a sequence of *things the player now knows*, held in
`Progress.Known` (`sim/src/Progress.cs`), which is already the save file and already the
character sheet.

That constraint is not a limitation to be worked around. It is the reason this story can be
told at all by one person with no cutscene system.

---

## 1. What the search is for

### 1.1 The person: Sera Wray, flight engineer

The vision doc says you have *"a name, a partial frequency, and a route they were last
flying."* Under D-008 she cannot be a pilot, and she should not be: the premise in
`00-vision.md` is that a helicopter needs **a living supply chain — fuel, bearings, seals,
blade balance, a person who understands it** — and you are flying because you never stopped
feeding it. The person who taught you how to feed it is the correct object of the search.

**Sera Wray** signed Hugh's logbook every twenty-five hours for nine years. She painted the
name on the nose (D-012 flagged that painter as a hook; this cashes it). At the collapse
she was riding out on a fixed-wing freighter — callsign **SIERRA-FOUR-THREE** — on a
four-leg ferry that never closed its flight plan. You were airborne elsewhere. You have
flown alone for six years.

The three things you start the game with, exactly as the vision states them, made concrete:

| You have | What it actually is | What it becomes |
|---|---|---|
| **A name** | "Wray." On the nose, in the logbook, in the margin of every inspection page. | The thing every NPC is asked about. |
| **A partial frequency** | Not digits — a *band*. Airband, 118–152 MHz, a paired relay channel. Thirty-four candidates. | Every mast you tune with **Tune the mast** narrows it. The existing generator already rolls `RandfRange(118, 152)`. The search is a literal search of a frequency band. |
| **A route** | Four legs in her handwriting on the last page of the logbook. You have leg one. | The act structure. Each leg's evidence hands you the next. |

### 1.2 The object: a matched pair of main rotor blades

A name is a reason to care. It is not, on its own, a reason to fly to specific places in a
specific order. The search needs a physical object with a clock on it, and the airframe
supplies one for free.

`sim/src/Airframe.cs` gives Hugh a two-bladed teetering head: `Radius = 7.32`,
`BladeMass = 145.0`. Rotor blades are the one component on a helicopter that is
**life-limited, unfakeable and unweldable**. You can scavenge an engine. You can rebuild a
gearbox with hand tools and swearing. You cannot make a rotor blade, and a mismatched pair
will shake an airframe apart. Two blades, matched, tracked, by somebody who knows how.

So the search is for Wray *and*, through her, for the blades — because she wrote the
inventory that says where the last serviceable stock is, and she is the only person left
who can tell a serviceable blade from a written-off one and track the pair once it is on.

### 1.3 The clock: the rotor ceiling

This is the mechanical spine, and it is one number.

**Proposal.** `Progress.RotorHours` accumulates whenever the engine is running, at the same
12× rate as `Progress.Clock` (`SiteInteraction._Process`). `DamageState` gains a **repair
ceiling** on `Component.MainRotor`:

```
MainRotorCeiling = clamp(1.00 - 0.0019 * RotorHours, 0.55, 1.00)
```

`Repair` can restore the main rotor *up to the ceiling and no further*. The ceiling falls
0.45 over 240 rotor hours ≈ **20 real hours with the rotor turning**, then floors at 0.55.

Why this is the right mechanic and not a bolted-on quest timer:

- **It is already felt.** `DamageState.RotorImbalance = (1 - Health) * 0.09` and
  `RotorThrustFactor = 0.80 + 0.20 * Health` are wired into the flight model today. A
  falling ceiling means the aircraft you fly in hour fifteen genuinely shakes and genuinely
  will not hold a hot out-of-ground-effect hover. Nobody has to write a line of fiction
  about it.
- **It is a fact, not an inference.** The AIRCRAFT page of the kneeboard reads
  `MAIN ROTOR  ceiling 82%  ·  94 h since last track`. No progress bar, no percentage of
  story completed.
- **It is forgiving, per D-007.** The floor at 0.55 is a flyable, unpleasant aircraft:
  thrust factor 0.89, imbalance 0.041. It never kills you and it never locks you out. It
  makes every later sortie harder, which is exactly the pressure a search needs.
- **It has a payoff with a number on it.** New blades set `RotorHours = 0`. The ceiling
  goes back to 1.00. That is the only event in the game that does this.

The constant `0.0019` is a first guess and should be **measured, not eyeballed** — the
right rig is a simlab run that flies a standard sortie profile at ceilings 1.00, 0.85,
0.70 and 0.55 and reports hover power margin, vibration amplitude and best glide, the way
D-014 tuned terrain relief against the threat report rather than by eye.

### 1.4 What the payoff is

You find her in Act III with about an hour of play left, not at the credits. That is
deliberate: the last hour of the game is spent **with her in the right seat**, which is the
first time Hugh has carried a crew since the first minute of the game. The search pays off
in a sortie, not in a speech.

---

## 2. The three-act spine, keyed to the eight regions

The regions are the ones in `game/scripts/WorldMap.cs`, with their real names and tiers.
Straight-line leg times at the measured 100–116 kt cruise (D-003a) are given because they
are the actual pacing budget; the threat-inflated figure (D-013 measured 1.63× on flat
ground, more with terrain) is what it really costs.

| Act | Region | Kind | Tier | What it holds | What it gives |
|---|---|---|---|---|---|
| I | **The Pan** | Basin | 0 | Mattie. The correction. | The search stops being a frequency and becomes a callsign. |
| I | **Long Acre** | Farmland | 0 | Doss Emery. The daily broadcast. | A live carrier with a human rota on it. |
| II | **Fenmoor** | Exurb | 1 | Nell Abergale. The departure field. | The manifest, the tail number, and the RWR. Leg 1 closed. |
| II | **The Drowning** | Wetland | 1 | Bel Tiernan. The aeroplane, in the water. | No bodies. A raft gone. The hoist. Leg 2. |
| II | **Cold Shoulder** | Upland | 2 | Osie Crane. The cairn. | The list of the dead — and the three names not on it. Leg 3. |
| II | **Sawtooth Works** | Industrial | 2 | Halvard Ferren. The roster. | Alive four years after the crash, and she left on purpose. Leg 4. |
| III | **Ashmount** | City | 3 | Sera Wray. Juno Kessel. | Her. And the price. |
| III | **The Scald** | Ashfield | 3 | Sparrow. The sealed magazine. | The blades. |

Direct legs: The Pan→Long Acre 3.7 km (1.1 min) · →Fenmoor 3.8 km · →The Drowning 4.0 km ·
The Drowning→Cold Shoulder 9.6 km (2.8 min) · →Sawtooth Works 6.4 km · Sawtooth→Ashmount
11.4 km (3.3 min) · Ashmount→The Scald 8.7 km (2.6 min). None of these is long. **What
makes them journeys is that from Fenmoor onward you cannot fly them straight** — threat
envelopes are the pacing system (D-010, D-013), not distance.

---

### ACT I — "So you are real." (The Pan, Long Acre · tier 0)

*Target: 90 minutes. No threats exist below tier 1 —`ThreatWorld.Place` skips `Tier == 0`
outright — so this act is where the player learns to land, talk, survey and tune a mast
without being shot at. The story's job in Act I is to convert two abstractions the player
was handed at the title screen into two things they can actually do.*

**Beat 1 — The logbook.** New game. `Progress.NewGame()` already seeds
`rumour.the_name`: *"A name, and a partial frequency. Neither of them is a place."* Add
`search.logbook` alongside it: the last page, four legs, the first one legible. The
kneeboard's THREAD page (§4.4) starts here with four lines and none of them struck through.

**Beat 2 — Mattie's correction.** First conversation at the first settlement in The Pan
(where `SiteInteraction._mattieSiteId` already puts her). She has heard you on the radio
twice. When the name comes up she corrects you, flatly, and the correction *is* the beat:

> *"That is not a frequency. That is the back half of a callsign. Sierra four three. You
> have been listening for a number for six years and it was a name all along."*

This costs nothing to build — it is a baked line gated on a knowledge id — and it re-frames
everything the player already has, which is the cheapest kind of story beat there is. She
also tells you the one thing she will not elaborate on: she signed a fuel chit for a woman
with that handwriting, once, and she has kept it, and she does not want to talk about why.

**Beat 3 — The band.** Mattie sends you up the Long Acre mast. The existing
**Tune the mast** action (`AddTuneRelay`) becomes the act's verb: every mast logged is one
of thirty-four candidates eliminated. The kneeboard's KNOWN page already counts
`KnowledgeKind.Frequency`; the THREAD page reads `AIRBAND CANDIDATES  6 of 34 logged`.

**Beat 4 — The broadcast.** Doss Emery, first settlement in Long Acre, has listened to the
same carrier at 06:40 every morning for six years: a weather sequence, read aloud, by a
rota of four readers he can tell apart and has given names to. One of them, he says,
*"says the wind speeds in knots. Nobody says knots."*

**Act I ends** the first time the radio speaks to the player *in flight*: the 06:40
sequence, arriving as text across the HUD strip (§4.3), read by somebody who says knots.
The player does not know it is her. The journal records only what was heard.

---

### ACT II — "The route." (Fenmoor, The Drowning, Cold Shoulder, Sawtooth Works · tiers 1–2)

*Target: 10–14 hours, which is most of the game. This is where the search becomes a map
problem and D-010's spatial progression does the work: each region holds one leg of the
ferry route and one capability, and the capability is what makes the next region
enterable.*

**Fenmoor (tier 1) — the departure field.** The ferry left from the airfield here. Nell
Abergale kept the load manifest because her brother's name is on it. The manifest gives you
the tail number, the routing, and the fact that the aircraft was **420 kg over gross** —
which is why it did not make Cold Shoulder. Capability: the **radar warning receiver**,
which Nell has in a crate and will not simply hand over. Beat: the first time you are
painted, the RWR logs the emitter (D-005a's cheapest good idea) and the map stops being a
coin flip.

**The Drowning (tier 1) — the aeroplane.** The wreck is real and findable: the lowest-lying
of the four Wetland wrecks, half in the water, tail boom up. Bel Tiernan's people treat it
as a grave and will not take you to it until standing is up. When you get there and search
it, three facts come out in order — the tail number matches; the cabin is empty; **the
liferaft is gone and the door was opened from the inside.** Capability: the **rescue
hoist**, because there is nowhere within 200 m to put skids down. This is the beat that
turns the game from an investigation into a search for a living person, and it lands at
roughly the one-third mark, which is where it belongs.

**Cold Shoulder (tier 2) — the walkers.** Seven people came up out of the water and walked
into the uplands in November. Osie Crane buried four of them and can still recite their
names, which he hates. The cairn is at the region's single Overlook — the site kind that
already carries the **Survey** action, so the story beat and the chart unlock are the same
act of landing. The list has four names on it. Wray's is not one of them. Capability:
terrain-masking charts for the uplands — knowledge, not hardware, and free, exactly as
D-010's table promises.

**Sawtooth Works (tier 2) — the reveal.** Three walkers reached the works and were taken
in, fed and put to work, which Halvard Ferren does not apologise for. The roster in the
shop office has her name against a four-year span and a leaving date. **She was alive four
years after the crash and she left on her own legs, heading for Ashmount.** Ferren will
trade the roster, and the emitter-location chart that makes tier 3 enterable, for a
delivery to Ashmount he will not describe and which is plainly a debt being collected. The
player can refuse and get the same information the long way, off the works' own relay, at
the cost of two more sorties. Neither path is the clean one; that is the point, and it is
the most New Vegas thing in this document.

**The sting Act II ends on** is not that she is dead. It is that she has been four hours'
flight away for six years, she did not come, and the reason is in the next region.

---

### ACT III — "The price." (Ashmount, The Scald · tier 3)

*Target: 2–3 hours, of which the last hour is §5.*

**Ashmount** is a tier-3 city under an aerostat and a SAM belt — the region where D-013's
design bites hardest, because the aerostat *is the answer to the habit* the player has
spent ten hours forming. You cannot valley-crawl into Ashmount.

She is at the first settlement inside the city. She is sixty-one, she has a bad hip, and
she pumps water for about three hundred people out of a flooded cut using a turbine she
rebuilt from a ventilation fan. She did not come because for the first two years she
believed Hugh had burned — the last anyone heard of that tail number was on a field that
had been strafed — and by the time she heard the sound go over, she was the reason three
hundred people had water.

She will not leave. She will come **for two days.**

The price is the flight she has needed somebody to make for six years: the blade stock she
inventoried is in a hardened magazine at a depot deep in **The Scald**, in the ash, under
the second aerostat, behind a door that has been sealed since it burned. There are
serviceable blades in there. She has known exactly where they are for six years and has had
no way on earth to reach them.

Juno Kessel, who runs the Ashmount tether crew under duress, sells you the other half: the
aerostat comes down for ninety minutes every eighth night to swap its gas bag. That is the
window, and it is the purest expression of D-005 in the game — **the last gate in ROTORWASH
is opened by knowing a time of day.**

---

### The beat list, as a build spec

Fourteen beats. Each is a `Knowledge` entry with a `search.` id, a trigger predicate over
`Progress` and world state, a carrier and a journal line. Nothing else.

| # | Id | Trigger | Carrier | Grants |
|---|---|---|---|---|
| 1 | `search.logbook` | new game | kneeboard THREAD page | the four legs |
| 2 | `search.callsign` | talk to Mattie, meetings ≥ 1 | dialogue | SIERRA-FOUR-THREE |
| 3 | `search.band` | any relay tuned | journal + THREAD counter | the 34-candidate band |
| 4 | `search.rota` | talk to Doss, `Knows(search.band)` | dialogue | four readers, one says knots |
| 5 | `search.voice` | first flight after beat 4, world clock 06:40 ± 20 min | **radio** | the broadcast, heard |
| 6 | `search.manifest` | talk to Nell, standing ≥ 0.3 | dialogue + artefact | tail number, 420 kg over |
| 7 | `search.field` | visit the Fenmoor airfield, `Knows(search.manifest)` | place | leg 1 closed |
| 8 | `search.wreck` | search the Drowning wreck | place + artefact | it is the right aircraft |
| 9 | `search.raft` | search the same wreck a second time | place | opened from the inside |
| 10 | `search.cairn` | survey the Cold Shoulder overlook | place + artefact | four names, hers absent |
| 11 | `search.roster` | Ferren's contract complete **or** Sawtooth relay tuned | dialogue **or** radio | alive +4 years, left for Ashmount |
| 12 | `search.wray` | talk at the first Ashmount settlement | dialogue | her |
| 13 | `search.magazine` | `Knows(search.wray)` | dialogue | where the blades are |
| 14 | `search.window` | talk to Juno, `Knows(search.magazine)` | dialogue | the ninety-minute window |

Beats 3, 5 and 11 have two routes in. That is the whole of the branching, and it is enough:
the player who does everything and the player who cuts corners arrive at the same place
having done different work, which is all any comparator in `benchmarks/benchmark-pass-2.md`
achieves outside of New Vegas.

---

## 3. The people

Nine, including Mattie. Each is anchored by a rule that resolves against today's
`WorldMap` with no new data — **"the first Settlement in region R, in build order"**, which
is exactly the rule `SiteInteraction._Ready` already uses for Mattie. The site plan in
`WorldMap.BuildSites` guarantees each of these regions has enough settlements: The Pan 3,
Long Acre 4, Fenmoor 4, The Drowning 2, Cold Shoulder 2, Sawtooth Works 2, Ashmount 4, The
Scald 1. Standing figures are starting `NpcMind.Standing`.

---

### Mattie Sowerby — The Pan, first Settlement · standing 0.2 · **exists, do not rewrite**

Fuel-cache keeper in her sixties. Dry, unimpressed, secretly glad of the company. Her
existing corpus in `DialogueCorpus.MattieLines()` is good and should not be touched; she
gains a second bank of thread-gated lines and nothing else.

- **Wants:** the pumps kept running, and for this pilot not to die stupidly.
- **Knows:** that "43" is a callsign, not a frequency. That a woman with that handwriting
  signed a fuel chit at her pumps, once, a long time ago. She has kept it in a tin.
- **Trades:** fuel, the correction, and the bearing to the Long Acre mast. Later, at
  standing ≥ 0.6, the chit itself.
- **Her arc:** she is the one character who is *deliberately not hopeful*, and the ending
  pays her off — when the net comes up (§6.2), Ashmount answers on her set, and she does
  not say anything about it at all, which is the line.
- **Sample:** *"I kept it because it was the only one anybody ever signed with a flourish.
  That is the whole reason. Do not make it a sign."*

### Doss Emery — Long Acre, first Settlement · standing 0.1

Late sixties, wheat that mostly does not come up now, an airband receiver he has never once
turned off. Talks too much because nobody has listened for years.

- **Wants:** someone to confirm he has not been imagining the 06:40 broadcast.
- **Knows:** the schedule to the minute. The four readers, distinguishable by habit — one
  clears his throat, one reads the pressure first, one gets the dates wrong, one *says the
  wind speeds in knots.*
- **Trades:** everything he has, immediately, for free. He is the one NPC who gives more
  than he asks, and that should feel slightly uncomfortable.
- **Sample:** *"Six years, near enough. I could not tell you what it is for. I can tell you
  the pressure at Ashmount on the morning my wife died."*

### Nell Abergale — Fenmoor, first Settlement · standing −0.1

Forties. Ground crew at the field before, scrap yard now, with a crate of avionics she has
never sold because she does not need money, she needs a reason.

- **Wants:** to know what happened to her brother, whose name is on the manifest. Failing
  that, to stop wondering.
- **Knows:** the tail number. The load. That the aircraft was 420 kg over gross, that she
  watched them load it, and that she said nothing.
- **Trades:** the **radar warning receiver** and the manifest, for two runs of parts — or,
  if you have already been to The Drowning, for the truth, which she pays for and
  immediately regrets. Her brother is a name on the cairn.
- **Sample:** *"I signed the loadsheet. Every day since, I have known exactly how much I
  signed for."*

### Bel Tiernan — The Drowning, first Settlement · standing 0.0

Thirties, runs a stilt village and an eel trade, decides who gets taken where by boat.

- **Wants:** the drowned aircraft left alone; it is a grave and her people have decided so.
  Once she changes her mind, she wants the **hoist** used for something of hers: a
  generator on an island that has been unreachable for three years.
- **Knows:** exactly where the wreck is. That a raft came ashore two days after, with people
  in it, who walked north. She will not say the second part until standing ≥ 0.4.
- **Trades:** the wreck's position for the generator lift. This is the first beat that
  requires a specific module, and it should be the player's first *forced refit decision*.
- **Sample:** *"You can hover over it all you like. You will not find it from the air, and I
  am not going to be the one who tells you where it is so you can take photographs of my
  uncle."*

### Osie Crane — Cold Shoulder, first Settlement · standing 0.3

Seventies. Buried four strangers in November six years ago and has been the village's
unofficial rememberer ever since, a job he never asked for.

- **Wants:** to stop being the person who holds the names. Genuinely, literally wants to
  hand them to somebody who will write them down.
- **Knows:** the four names. That seven came up out of the water and three walked on. That
  one of the three could name every part of an engine and argued with him about a pump.
- **Trades:** the list, and the upland terrain-masking chart, for nothing. He asks only that
  you carry the names. Under D-005 the chart is a real unlock; under the tone rules the
  transaction is not a transaction.
- **Sample:** *"I will say them once. You write them down and then they are yours and I can
  stop."*

### Halvard Ferren — Sawtooth Works, first Settlement · standing −0.2

Fifties, runs the shop floor, keeps two hundred people fed by keeping a rolling mill alive,
and took in three strangers and put them to work and does not think he did wrong.

- **Wants:** the works to stay useful, because a works that is not useful is a village that
  starves. Is not a villain. Is not sorry.
- **Knows:** the roster. Her four years. Her leaving date. That she went to Ashmount
  deliberately and that he told her not to.
- **Trades:** the roster and the **emitter-location chart** for a delivery to Ashmount he
  will not describe. The player can decline and spend two extra sorties getting the same
  from the works' relay.
- **Sample:** *"You want me to feel something about it. I fed her for four years. You can
  decide what that was."*

### Sera Wray — Ashmount, first Settlement · standing 0.5 on first meeting

Sixty-one. Flight engineer. Bad hip. Rebuilt a ventilation fan into a water turbine and has
run it for six years. Painted the name on Hugh's nose in half-inch brush strokes because
that was the widest brush in the hangar.

- **Wants:** three hundred people to keep having water. Will not leave permanently and must
  never be talked into it; a version of this story where she abandons them is a worse story.
- **Knows:** everything. How to track a two-bladed head. Where the blade stock is and what
  condition it was in when she wrote the inventory. What the nose art is for.
- **Trades:** two days of her life, in the right seat, for the flight to The Scald.
  Afterwards: the aircraft is maintained by somebody who knows how, permanently.
- **Her first line must not be a reunion.** It is a maintenance observation. She hears the
  rotor before she sees it and says what is wrong with it.
- **Sample:** *"Your track is out. I could hear it from the cut. How long has it been doing
  that?"*

### Juno Kessel — Ashmount, second Settlement · standing −0.4

Late twenties, runs the aerostat tether crew because the alternative was worse, wants out
and cannot get out.

- **Wants:** to not be the person who winches up the thing that shoots people.
- **Knows:** the gas-bag swap schedule. Ninety minutes, every eighth night, and a maximum
  of four more swaps before the bag is beyond patching.
- **Trades:** the window, for a lift out afterwards — which is a promise the player makes
  and the ending has to honour or visibly not honour.
- **Sample:** *"I am not selling you a way in. I am selling you an hour and a half of
  nobody looking down. What you do with it is not my business and I would like it kept
  that way."*

### "Sparrow" — The Scald, its single Settlement · standing −0.6

No other name given. Runs a scavenger crew in the ash, is visibly dying of it, and knows
it. Is **a voice on a guarded frequency first** — the player hears them for hours of play
before ever meeting them — and then a person standing in front of the aircraft with a rifle.

- **Wants:** not to be found. Then: medical stock, which `Stock.Medical` already tracks and
  which nothing in the game currently consumes for anything that matters.
- **Knows:** the magazine is sealed, why it was sealed, and that the last crew who opened a
  door in that depot did not come back out.
- **Trades:** passage, for medical. The exchange can also be resolved with the sidearm, and
  the game must not tell the player which was correct.
- **Sample:** *"You can come in. I am not going to stop you and I would not enjoy trying.
  Put the tin on the drum and walk where I tell you."*

---

## 4. How it is told

There are no cutscenes, there will be no cutscenes, and none of the following needs one.
Five carriers, in descending order of how much narrative weight they take.

### 4.1 The place you land at

**This is the primary carrier and it should stay that way.** D-003b's altitude-banded
authoring rule — 500 m silhouette, 150 m occupancy, 15 m landing problem — is already
implemented in `SiteKit` and is *also* a complete narrative grammar. Every story location
tells its beat three times as you descend:

| Beat | 500 m | 150 m | 15 m |
|---|---|---|---|
| The wreck in The Drowning | a tail fin standing out of flat water where nothing vertical should be | no scorching, no debris field — it *landed* | the cabin door standing open, from the inside |
| The cairn at Cold Shoulder | a stone pile on a bare ridge with no building near it | four courses, built carefully, by one person | names scratched with a nail, three courses deep, and one course left blank |
| The magazine at The Scald | a concrete revetment, intact, in a field of things that are not | ash drifted to the doors, undisturbed for six years | the blades laid out in racks, in their grease, in rows |

No text, no voice, no trigger volume. The player flies down and understands. This is the
cheapest storytelling in the project by an order of magnitude and it is also the best,
because it is the only kind that uses the flight model.

### 4.2 The journal line

`Progress.Journal` already exists, already timestamps, already saves, and is already shown
on the kneeboard's LOG page. Story beats write to it. The rule, which must be enforced in
review: **the journal is the pilot's handwriting and records only what was observed.**

> `[D14 09:12] Searched the wetland wreck: tail matches. Cabin empty. Raft cradle empty, strap cut clean.`

Not *"I felt a surge of hope."* The player supplies that. And because the journal is the
pilot's hand, the game has exactly one enormous card to play with it: **the last entry in
the game is in somebody else's handwriting** (§6.1).

### 4.3 The radio

The one new channel, and it must stay cheap. ROTORWASH synthesises all audio and ships no
samples (`RotorSynth`, `WeatherSynth`); there is no voice acting and there will not be. So
the radio is **text, at reading pace, on a two-line strip at the bottom of the HUD, with
synthesised carrier hiss under it** — which the existing weather-audio machinery can
produce as band-limited noise gated to the length of the text.

Three kinds of call, and the distinction matters:

1. **The scheduled broadcast.** Same world-clock time every day. This is what makes
   `Progress.Clock` matter narratively and is the only reason a player will ever plan a
   sortie around the time of day. Subnautica's radio, with a timetable.
2. **The directed call.** Somebody raises *you*, because you tuned their mast and they have
   been listening since. Fires on entering a region, once.
3. **The intercepted call.** You overhear traffic not meant for you — Sparrow's crew, the
   Ashmount tether net — when you are inside a threat envelope and being tracked. Overheard
   traffic is a reward for being somewhere dangerous, which pairs with D-005a's rule that
   the bad sortie must pay out.

**The player cannot reply in flight, ever.** No dialogue wheel at five hundred feet.
Replying means going there and putting the skids down, which preserves D-003a's principle
that landing is the expensive act. It also makes the radio dirt cheap to build: a one-way
text queue with a trigger predicate.

### 4.4 The kneeboard

A fifth page: **THREAD**, after MAP. `Kneeboard.Pages` is a four-element array and `_Draw`
is a switch on `_page`; a fifth case is trivial. Contents, facts only:

```
THREAD

  WRAY, SERA — flight engineer
  SIERRA-FOUR-THREE, four legs, last plan filed 11 March

  LEG 1   Fenmoor field   -->   north                    closed  D9
  LEG 2   north           -->   the wetland              closed  D14
  LEG 3   the wetland     -->   uplands                  closed  D21
  LEG 4   uplands         -->   ---                      open

  AIRBAND CANDIDATES      11 of 34 logged
  MAIN ROTOR              ceiling 82%   ·   94 h since track
```

No "next objective" marker. No percentage. The legs close themselves as evidence arrives.
The contract board now in `sim/src/Contract.cs` gets its own listing here or its own page —
**the search thread and the contract board are different things and must not be merged.**
Contracts are the substrate that gives every sortie a reason; the thread is the arc. Elite
has the first and no second; ROTORWASH needs both.

### 4.5 The people, and what the coda is allowed to do

The baked corpus carries the dialogue, exactly as `DialogueCorpus` does today. **One change
to `Requirement` unlocks the entire story layer**: a `knows` key that tests
`Progress.Knows(id)`.

With that single addition, every NPC in the game can have lines gated on exactly what the
player has learned; the selector's existing most-specific-match rule does the sequencing for
free; and the story is delivered by the dialogue system that already exists. This is the
highest-leverage two hours of work in the document, and §7.3 says how.

**The coda's limits are unchanged and must stay unchanged.** D-006a is explicit: *the baked
line answers; the coda notices.* The local model never advances the thread, never mentions
Wray unless the baked line did first, and never invents a beat — which the validator
already enforces via `NpcMind.Forbidden` and the anachronism checks. Add `"Wray"`,
`"Sierra four three"` and `"the blades"` to the `Forbidden` list of every character who is
not supposed to know about them. The story lives entirely in the baked layer and in
`Progress`; the SLM only ever remarks on state.

### 4.6 Objects with handwriting

Six physical artefacts — the logbook page, Mattie's fuel chit, Nell's loadsheet, the
wreck's data plate, the cairn list, the Sawtooth roster. Each is read at the moment of
salvage or conversation through the existing `Notice` and journal channels, as a short
block of monospaced text in the kneeboard aesthetic. No new system: a text asset and a
display mode.

They matter because they are the only place in the game where **somebody else's words
appear verbatim**, and that is what makes a search for a person feel like a search for a
person rather than a fetch quest with a name attached.

---

## 5. The last hour

One sortie, at night, with a passenger. It is the whole game compressed, and every system
in `STATUS.md` gets used once.

**Minutes 0–8 · Ashmount, shut down, the deal.** Wray in the dialogue panel. She wants the
cargo hook fitted and the long-range tank taken off — the first time the game has ever told
the player what to *remove*, and `AddRefit` already supports removal. She walks around the
aircraft and tells you three things that are wrong with it that you did not know.

**Minutes 8–12 · The wait.** The window is the eighth night. A player who arrives on the
wrong night waits, and the world clock at 12× makes that a real decision: sit on the pad or
fly a contract and come back. Dead time is not dead if the player chose it.

**Minutes 12–24 · The crossing.** Ashmount → The Scald, 8.7 km direct, which you cannot
fly. Aerostat down, SAM belt live, guns at the depot. Suppressor and flares, or valley
discipline; D-013 measured 45 seconds in two envelopes as *everything damaged, 62% forced
down, none deleted*, which is exactly the right shape for a finale. Wray is in the right
seat and talks — not encouragement, *readings*. She calls the torque, because that is what
a flight engineer does, and because it means the game's last flight has a second voice in
it without a single line of new systems work.

**Minutes 24–32 · The landing.** Ash. Slope. Night. Brownout, which already blinds the last
ten metres and clears the moment you fly out of your own dust. Touchdown grading against
skid limits, dynamic rollover from geometry, rotor clearance. This is the hardest landing in
the game and it should be, because landing is the expensive act and this is the expensive
landing.

**Minutes 32–45 · On foot.** Dismount, sidearm, Sparrow's crew in the dark. They can be
paid in medical kits or shot; Rotor Time and called shots exist and the player will use them
either way. The magazine door, the racks, the grease, the rows. Wray identifies the pair.
Two blades at `BladeMass = 145` kg each, plus grips and tie bars: **a 420 kg sling load** —
the same 420 kg the ferry was over gross by, which is the only deliberate symmetry in this
document and it earns its place.

**Minutes 45–58 · Home heavy.** The load under the hook, the aircraft out of ground effect
at a rotor ceiling of maybe 0.58, at night, in ash, with a climb you may not have. This leg
must be **measured in simlab before it is authored** — a `finale` scenario that flies the
profile at the worst plausible ceiling, worst plausible fuel and full load, and reports
whether it closes with margin. If it does not close, the load comes down or the ceiling
floor comes up. This repo does not guess at numbers, and this number decides whether the
ending is physically possible.

**Minute 58 · The pad at Ashmount.** Put it down. Shut down. That is the end of the
gameplay.

**What the player does not do:** fight a boss, pick a dialogue option that determines an
ending, or watch anything. There is no final choice, because the choices were the sorties.

---

## 6. What changes about the world

Three deltas, all cheap, all diegetic, and one of them makes the world worse — because the
tone is melancholy and not triumphant, and because an ending in which everything improves
is an ending that says the collapse was a puzzle.

### 6.1 The rotor clock resets, and stops being a clock

`RotorHours = 0`. The ceiling returns to 1.00. The AIRCRAFT page line changes permanently:

```
  before   MAIN ROTOR    ceiling 58%   ·   221 h since track
  after    MAIN ROTOR    tracked D63 by S. Wray   ·   in limits
```

And the mechanism that produced the whole arc — the ceiling — never appears again, because
somebody is maintaining the aircraft now. **The payoff for a gear-and-knowledge game is a
number on a maintenance page**, and that is exactly right for this project.

The final journal entry is not in the pilot's hand. It is a maintenance entry, and it is
the first and only time the log is written by somebody else:

> `MR blades, 2 ea, installed from store. Tracked and balanced, 2 runs. In limits. S. Wray`

### 6.2 The net comes up

Wray gets the Ashmount relay transmitting on the paired channel. Every `freq.<siteId>` the
player ever logged converts from *"carrier present. Nobody answering, but it is on"* into a
live `KnowledgeKind.Contact`. Concretely: **settlements begin calling you.** The contract
board stops being a thing you walk to and becomes a thing you hear, in flight, on the radio
strip built in §4.3.

The game's own mission system arriving in its final form as the ending's reward costs
almost nothing to implement, and is the single best argument for building the contract board
and this story in the same quarter.

Mattie's set picks up Ashmount. She does not say anything about it.

### 6.3 One place goes dark

The Scald depot's guns are gone — the crew left, or Sparrow's crew did, and nobody will say.
But the ash has moved. The contamination band grows by one ring on the kneeboard map: two
sites you could land at in Act II become sites you cannot, and the fog over them does not
lift again. The country is still ending. You bought yourself another decade of flying it.

### 6.4 And afterwards

The game continues. D-011 promised free-form airframe construction as a late-game layer —
*"when the player has a hangar, a welder and salvaged rotor systems, and can start building
something that is no longer a Huey."* That is what the post-ending state is for, and Wray is
the character who makes it legal: you now have an engineer.

---

## 7. What the engine cannot do, and the smallest version that works today

Nothing above assumes new tech except where listed here.

### 7.0 A search thread already shipped, and its story contradicts D-008

**Read this first if you own `sim/src/SearchThread.cs`.** While this document was being
written, a parallel workstream added `SearchThread`, `Contract`, `ContractBoard` and the
`Progress.Search` property to the tree. The *machinery* is good and this document adopts it
wholesale — `SearchBeat` with a gate predicate, a journal line, a hint and a knowledge
grant, advanced by `TryAdvance(Progress)`, is precisely the shape §7.2 would otherwise have
had to specify.

The *content* has two problems that should be fixed before it is built on.

**1. The premise violates D-008.** The shipped beats make the target **Kara Morrow, a
pilot**, and the twelfth beat reads:

> *"Kara's offer: work together. Two aircraft can carry what one cannot… Two pilots, two
> aircraft."*

D-008 is **[LOCKED-IN BY FRED]**, is listed in `00-vision.md` as a pillar, and its stated
reversibility is *"low, by design."* A second flying helicopter and a second pilot in the
final beat removes the structural uniqueness that makes every faction's interest in the
player legible without exposition, and it removes the reason the "last airframe" premise is
load-bearing at all. This is not a tone note; it is the one constraint the design says
cannot be traded.

**The fix is a content swap, not a rewrite.** Keep the class, the twelve slots, the gate
signature, the save format (`Stage` is an int and persists as one). Replace the target with
Wray, the object with the blades, and the ending with §5. The `search.` knowledge id
namespace is already the one this document uses.

**2. The gates are counters, not places.** Every shipped gate is of the form
`p.VisitedCount >= 18` or `p.CountKnown(KnowledgeKind.Chart) >= 4`. That fires beats on a
*tally*, which means beat 6 — *"A wreck field in the uplands… someone scratched coordinates
into the panel"* — can fire while the player is parked at a farmstead in a basin, having
never seen a wreck. The story then describes a place the player is not at, which is the
single most reliable way to make a narrative layer read as generated filler.

**The fix:** gate on *where the player is and what they have learned*, as the table in §2
does — `progress.HasVisited(StoryPlaces.DrowningWreck)`, `progress.Knows("search.manifest")`
— with counters used only as a floor to stop beats from stacking. `TryAdvance` already takes
`Progress`; it needs no signature change, only better predicates and the site-role
resolution in §7.1.

### 7.1 There is no authored-site layer — **this is the big one**

`SiteKit.Build(Site)` switches on `SiteKind` alone (`game/scripts/SiteKit.cs:65`) and
`WorldMap.NameFor` generates every place name from a seed. There is **no mechanism to say
"this specific site is the drowned ferry and contains these things."** Every story location
in §2 — the wreck, the cairn, the magazine, the departure field — needs authored contents,
authored props, authored salvage results and a stable identity, and the world generator has
nowhere to put any of it. Beat 8 cannot be written until this exists.

**Smallest version that works today:** a `StoryPlaces` static that resolves *roles* to site
ids at load time using only existing lookups and deterministic tie-breaks —
"the lowest-lying Wetland `Wreck`", "the single Upland `Overlook`", "the `Depot` in the
Ashfield furthest from the region centre", "the first `Settlement` in region R" — plus a
`Dictionary<int, StorySite>` consulted by `SiteKit.Build`, by `AddSalvage` and by
`GetOrCreateNpc`. Display keeps the procedural name; `AddAskAround` already interpolates
procedural names into prose successfully, so the corpus refers to places the way people do:
*"the wreck out in the wet"*, not a proper noun. An optional alias field can come later.
Perhaps 200 lines, and it unblocks everything else here.

### 7.2 Beat triggers need world state the sim layer cannot see

`SearchBeat.Gate` takes a `Progress`, which is correct for knowledge and visits but cannot
express *"world clock is 06:40 ± 20 min and the aircraft is airborne"* (beat 5) or
*"tonight is a gas-bag night"* (beat 14).

**Smallest version today:** widen the gate to take a small `ThreadContext` struct — clock,
airborne flag, current region, parked site id, rotor hours — populated by
`SiteInteraction.CheckSearchThread`, which already exists and already runs every frame. It
stays pure .NET and stays testable in simlab. Do not push these predicates into the Godot
layer; the whole value of `sim/` is that the story can be driven headlessly through all
fourteen beats in a test.

### 7.3 `Requirement` cannot test knowledge

`Requirement.Holds` knows six keys (`sim/src/Dialogue.cs:102`) and none of them is "has the
player learned X". Without it, no baked line can be gated on the story, and §4.5 does not
work.

**Smallest version today:** the record struct is `(string Key, double Min, double Max)`, so
there is no room for a second string. Add `KnownIds` (a `HashSet<string>`) to `TalkContext`,
populate it in `BuildTalkContext` from `Progress.AllKnown.Keys`, and add a `Knows(string id)`
requirement that carries the id in `Key` behind a prefix — `"knows:search.callsign"` — with
a `StartsWith` case in `Holds`. Ugly in one line, invisible everywhere else, and it needs no
change to the selector, the save format, or the coda. Two hours. **Do this first.**

### 7.4 There is no radio

Nothing pushes text to the player in flight except the `Notice` event, and the HUD has no
strip for it.

**Smallest version today:** reuse `Notice` and add a two-line queue to `FlightHud` that
reveals at reading pace, with the `WeatherSynth` noise generator supplying carrier hiss.
Fire only in level flight or on the ground, never during a threat engagement. No new audio
pipeline, no samples, no voice.

### 7.5 Dialogue only happens at Settlements

`RebuildActions` adds **Talk** only for `SiteKind.Settlement`. Sparrow is at The Scald's
single settlement so he is fine; Juno at the Ashmount airfield, and any relay-side
character, are not.

**Smallest version today:** extend the `AddTalk` condition to `Settlement | Airfield |
Workshop`, guarded by whether a `StorySite` declares an NPC there, so the generic settler
generator is not suddenly populating every workshop in the world. One line plus the guard.

### 7.6 There are no passengers

`MassProperties` takes arbitrary items, and `Airframe.Default` already places `"pilot"` at
`Vec3(2.00, -0.62, -0.25)`, 90 kg — the left seat, correct for a Huey aircraft commander.

**Smallest version today:** Wray is `Mass.Add("wray", new Vec3(2.00, 0.62, -0.25), 68)` plus
a dialogue bank that opens on landing and a flag in `Progress`. Her *calling the torque* in
flight is the radio strip from §7.4 in a different colour. A visible figure in the right
seat is a nice-to-have and explicitly not required for the ending to work.

### 7.7 The cargo hook does not sling anything

`"hook"` in `Loadout.All` is mass, position and a parts cost. There is no external load, no
pendulum, no cable.

**Smallest version today:** the load is a `MassItem` at the hook position, 420 kg, plus a
drag delta, with no pendulum dynamics. The aircraft is heavy, nose-down in trim and short of
power, which is eighty per cent of the feel. The swinging load is a stretch goal, and §5
must be authored so it does not depend on one.

### 7.8 Two smaller things, noted for honesty

- **`TalkContext.ArrivedAtNight` is hard-coded `false`** in `BuildTalkContext`, with a TODO,
  even though the day/night cycle exists. The finale is at night and several lines want it.
  One line to fix.
- **The frequency generator rolls random decimals.** If the fiction ever claims the channel
  ends in ".43", `AddTuneRelay` must be made to honour that for story relays — two lines.
  The design above deliberately avoids needing it by making the fragment a *callsign* and
  the frequency a *band*, which today's generator already supports exactly as written.

---

## 8. What to build first

In this order, because each unblocks the next and the first five together produce a playable
Act I:

1. **Re-point `SearchThread` at this spine** (§7.0). Content swap, same class, same save
   format. Doing this before anything is built on the shipped beats is much cheaper than
   doing it after.
2. **The `knows` requirement** (§7.3). Two hours. Nothing in §4.5 works without it.
3. **`ThreadContext` gates** (§7.2), with simlab tests that drive a synthetic `Progress`
   through all fourteen beats in order and assert that none fires early.
4. **Kneeboard THREAD page** (§4.4). The arc has to be visible or it does not exist.
5. **`StoryPlaces` role resolution and `StorySite`** (§7.1). The expensive one.
6. **Mattie's and Doss's thread banks.** Act I is now playable end to end.
7. **The radio strip** (§7.4) and beat 5 — the first time the world speaks to you in flight
   is the moment this stops being a sandbox.
8. Everything else, region by region, in tier order.

The rotor-ceiling clock (§1.3) can land any time after step 3 and should land early, because
it is the thing that makes the first ten hours feel like they are going somewhere.

---

## 9. Continuity rules, for whoever writes the corpus

- **Nobody is a chosen one and nobody says so.** `NpcMind.Forbidden` already carries this
  for Mattie and the settlers. Keep it on every new character.
- **No character explains the collapse.** Nobody knows. People who claim to know are wrong,
  and other characters say so.
- **The aircraft is "Hugh", and only the player and Wray call it that.** Everyone else says
  "it", "that thing", or "the aircraft". Wray calls it Hugh in her first line, before the
  player has told her anything, and that is how the player knows.
- **Nobody thanks you emotionally.** People pay in parts, information, or a chair by a fire.
  Mattie's *"Kettle is on. Do not read anything into it"* is the register for the whole game.
- **Violence is never a joke and never a spectacle** (`00-vision.md`). Sparrow's crew have
  names among themselves; the game does not supply them, because the player does not learn
  them.
- **Weather is not a mood.** `Weather.cs` is a model, per D-026. Never write a line that
  implies the sky is reacting to events.
- **The story never tells the player what to do next.** Every beat states a fact. The
  kneeboard records it. The player decides where to fly. That is the whole design.
