# Decision log (ADRs)

Format: **D-nnn — Title** · *date* · Decision / Why / Reversibility.
Fred can veto any of these; entries marked **[LOCKED-IN BY FRED]** came from him directly.

**Taking a number.** Several agents write this file at once, and for a while they were each
appending an entry with whatever number looked next from where they were reading. Fourteen
numbers ended up meaning two different things - D-042 was both "building variety" and
"measure a pinned condition", and code comments cited both. Before adding an entry, run

```
grep -oE "^#+ D-[0-9]+[a-z]?" docs/wiki/decisions.md | sed 's/^#* //' | sort -u | tail -3
```

and take the next one after the highest. A suffix letter (`D-003a`) means *refines or
supersedes that decision* and is never a way to dodge a collision.

---

### D-001 — Engine: Godot 4.7.2 (.NET/C#) · 2026-09-18 · **[LOCKED-IN BY FRED]**
Vulkan Forward+ renderer, text-based `.tscn`/`.tres` scene format, headless mode for CI.
**Why:** the only mainstream engine whose entire project is diffable plain text, which is what
makes multi-day autonomous development possible. C# gives a real test framework and the
performance headroom for blade-element physics and voxel construction.
**Reversibility:** high for the sim (it is a pure .NET library with zero engine references);
low for scenes/renderer.

### D-002 — Physics core lives outside the engine · 2026-09-18
`sim/` is a plain .NET 8 class library (`Rotorwash.Sim`) with **no Godot dependency**, using
`System.Numerics`. The Godot layer is a thin adapter that reads Godot state in, writes forces out.
**Why:** physics can be unit-tested and tuned headlessly at thousands of steps per second without
launching an editor. This is worth a small amount of marshalling cost.
**Reversibility:** high.

### D-003 — Target hardware & the two lighting paths · 2026-09-18 · **[LOCKED-IN BY FRED]**
Floor: **GTX 1080 / i5 / 32 GB at 1080p60** (Fred's test rig — Pascal, no hardware RT, no DLSS).
Ceiling: modern GPUs get realtime GI.
Decision: author every scene to look correct under **both** a baked-lightmap path (Low/Med) and a
**SDFGI + SSIL + volumetric fog** path (High/Ultra). Practically this means: light rigs use real
physical lights (never lightmap-only fakery), and lightmaps are baked from that same rig.
**Reversibility:** high early, expensive after a few dozen authored locations. Enforced by a
`QualityTier` autoload from day one.

### D-004 — Flight model: gamified blade-element · 2026-09-18 · **[LOCKED-IN BY FRED]**
Per-blade-element aerodynamics with real induced-flow, so translational lift, ETL, torque,
ground effect, VRS and autorotation emerge. Assists (attitude limiter, auto-trim, collective
governor) are a *layer on top* that the player can remove.
**Reversibility:** the assist layer is trivially tunable; the core model is the foundation.

### D-005 — Progression is gear-and-knowledge, not XP · 2026-09-18 · **[LOCKED-IN BY FRED]**
No experience points, no levels, no abstract skill tree. Capability comes from charts,
frequencies, schematics, tools, contacts, and physical parts. **Avionics hardware *is* the
skill tree** — an attitude-hold unit you scavenge and install is literally an assist unlock.
**Why Fred's call is right:** it makes looting meaningful, makes the block builder central
rather than cosmetic, and it is diegetic — the player's power is visible on the airframe.
**Reversibility:** medium. A light proficiency layer can be added later if progression
feels flat, but the default is that the *machine* levels up, not a hidden number.

### D-006 — Dialogue: baked corpus + local SLM "coda" · 2026-09-18 · **[LOCKED-IN BY FRED]**
Fred's design, and it is the good one: the NPC delivers a **pre-baked, hand-vetted line**
immediately, and that line's delivery time is used as **latency cover** while a small local
model streams in a 1–2 sentence personalised tail reacting to what the player actually said.
If the model is absent, slow, or produces something that fails validation, the baked line
simply ends naturally and nothing is lost. Zero token cost to anybody, ever.
**Reversibility:** high — the coda is strictly additive.

### D-007 — Fuel/wear is a core but forgiving loop · 2026-09-18 · **[LOCKED-IN BY FRED]**
Fuel, component wear and damage drive route planning and scavenging. No instant-death gotchas:
engine failure always leaves an autorotation, running dry strands rather than kills.
**Reversibility:** high (tuning constants).

### D-008 — No other helicopter pilots exist · 2026-09-18 · **[LOCKED-IN BY FRED]**
World-building constraint with mechanical teeth: air threats are ground-based AA, drones,
tethered balloons and weather — never rival helicopters.
**Reversibility:** low, by design. It is a pillar.

### D-009 — Working title "ROTORWASH" · 2026-09-18
Placeholder; rotorwash is the downwash that flattens grass, kicks up dust and announces your
arrival from a mile away — which is thematically the whole game. Trivially renameable
(one constant + folder name). **Reversibility:** high.

### D-010 — Air defence is the gating system · 2026-09-18 · **[FRED'S IDEA]**
A helicopter trivialises terrain, so terrain cannot be the gate. Instead the map is
gated by **threat envelopes**: radar-guided and IR-guided ground systems, tethered
aerostats, drone patrols and flak over the places worth reaching. The keys are physical
and findable, and every one of them is a real system rather than a stat:

| Key | What it unlocks | How it feels |
|---|---|---|
| Radar warning receiver | Knowing you are painted at all | The map stops being a coin flip |
| Chaff | Surviving radar SAMs | Lets you cross open ground |
| Flares + IR suppressor | Surviving MANPADS | Lets you fly low and slow near people |
| Terrain-masking charts | Knowing which valleys are dead ground | Knowledge, not hardware - and it is free |
| Jammer / emitter locator | Hunting the sites themselves | Turns a wall into a target |

**Why this is the right spine:** it makes the progression (D-005) *spatial*. The player
does not level up and then go somewhere; they acquire a capability and a whole region of
the map opens. It gives scavenging a destination, gives factions something to trade, and
makes the "last airframe" premise (D-008) load-bearing - everyone defends against the one
aircraft that exists. It also means a skilled pilot can sometimes beat a gate with flying
alone by masking in terrain, which is exactly the kind of optional mastery this project
should reward.
**Reversibility:** high - threat envelopes are data.

### D-011 — The platform is a Huey, and the builder is a refit system · 2026-09-18 · **[FRED'S IDEA]**
Fred asked whether a Huey would make a good all-purpose platform, and worried it would
be "less block-buildy". It is the right call, and it makes the builder *better*, not worse.

**Why a Huey is the correct airframe for this game specifically:**
- It is the most modular helicopter ever built. Doors off or on, seats in or out, cargo
  hook, hardpoints, litters, rescue hoist, gun mounts, external tanks, a flat cabin floor
  you can bolt anything to. It is a flying pickup truck, which is exactly the fantasy.
- The two-bladed teetering head is *characterful*: soft, laggy, slightly reluctant, with
  the famous blade slap. It is a machine with opinions. A modern rigid-rotor helicopter
  would fly better and feel like nothing.
- It is plausibly maintainable after a collapse - simple, hydromechanical, forgiving,
  built in tens of thousands. Nothing with a full-authority digital engine control would
  still be flying, and that fact does a lot of the world-building for free.
- The model in `sim/` is already this aircraft: 7.32 m rotor, 324 rpm, teetering head,
  3790 kg, and its measured hover power and power curve sit within a few percent of a
  real UH-1.

**The builder becomes a refit system, which is a better game anyway.** Instead of "assemble
an aircraft from voxels and hope it flies", the player works on a real airframe with real
attachment points. Every module is a `MassItem` plus drag, power draw and hardpoint
occupancy, and the existing physics already makes the trade real: hang armour on it and
the hover ceiling drops, bolt a long-range tank on one side and it rolls, strip the seats
and it climbs. Nothing needs faking.

This keeps a hard promise the free-form version could not: **every configuration flies,
but not every configuration flies well.** No player ever builds something that simply
falls over, and no player is ever protected from the consequences of their choices.

Free-form construction is not abandoned - it becomes the late-game *airframe* layer, when
the player has a hangar, a welder and salvaged rotor systems, and can start building
something that is no longer a Huey. That is a much better place for it than the tutorial.

### D-012 — "Hugh" is the helicopter, not the pilot · 2026-09-18
Fred suggested Hugh as the main character's name. Better: **Hugh is the aircraft.**
- The aircraft is the second protagonist (pillar 1). Naming it is how that lands.
- Players want to name themselves; almost nobody wants to be called Hugh.
- Somebody, at some point, painted the name on the nose. That person is a hook.
- "Hugh is not going to like this" is a line the game can earn a hundred times over, and
  it is funnier and sadder than any amount of exposition about how attached you are to it.

---

## Revisions from the benchmark round · 2026-09-18

Three research passes compared this design against the genre. Full write-ups in
`docs/wiki/benchmarks/`. Each found something real; the changes are recorded below rather
than quietly folded in, because the original decisions are still worth arguing with.

### D-005a — Progression needs a cadence and a screen · *supersedes part of D-005*
**Finding:** gear-and-knowledge progression is well precedented (S.T.A.L.K.E.R., Subnautica,
Death Stranding, Elite, Outer Wilds), but every game that makes it work pairs its rare
region-opening unlocks with **a dense substrate of small rewards every few minutes** and
**one screen that shows you are further along than you were**. D-005 had the rare unlocks
and neither of the other two. Scored 5/10 on pacing — the risk is not hour 30, it is that
hours 2-8 are inert.

**Changes:**
1. **Build the kneeboard.** One screen: aircraft status *with empty bays shown*, chart
   coverage, frequencies, contacts, known threat sites, open threads. It stores **facts
   only, never inferences** — the game does the bookkeeping, the player does the thinking.
2. **Four-layer rule.** Nothing ships as an acquisition unless it has all four: a visible
   change on the aircraft or the instruments, a printed numeric delta, a procedural change
   in how something is done, and somewhere to use it on this sortie.
3. **Instruments are items.** The HUD physically grows as boxes are installed, so the
   interface itself is a progress bar.
4. **The RWR logs every emitter that paints you.** The sortie that nearly killed you hands
   you the threat's position and signature. The game pays you for being outmatched, and the
   bad sortie stops being a dead loss. This is the cheapest good idea in the whole report.
5. **Threat envelopes are graded.** A hit takes a system and leaves an autorotation; it
   does not delete the aircraft. Consistent with D-007. Total unearned punishment reads as
   irritation, not as a gate.

### D-003a — World scale confirmed, fuel-as-range-gate killed · *supersedes part of D-007*
**Finding:** 16.384 km square (268 km²) is defensible, but the brief was wrong about the
aircraft. The benchmark ran this project's own test bench: the Workhorse cruises at
**100-116 kt**, not 60. Edge to edge is **4 min 49 s** — almost exactly the measured time to
cross GTA V, a world everyone describes as feeling small from the air. The helicopter does
not create a density problem (moving fast sweeps more ground, so you encounter *more*); it
creates a **duration** problem.

**Changes:**
1. **Target ~120 named POIs (~0.45/km²)**: about 45 hand-authored, about 75 assembled from
   kits, over a few thousand unnamed procedural features whose only job is to make the world
   read as inhabited from 300 m. A Skyrim-like 9 POIs/km² would need ~2,400 locations here,
   which is not a solo project.
2. **Fuel is not a range constraint and must stop pretending to be.** One tank crosses the
   map diagonal about 34 times. Re-framed as an **economy and load** system: fuel costs
   money and weight, and weight costs hover ceiling, climb rate and landing margin.
3. **Landing is the expensive act, not flying.** This is the real traversal cost, it is
   already earned by the flight model, and it is where the game should charge the player.
4. **Route inflation via threat envelopes** (D-010) is load-bearing, not decoration: going
   around is what makes 23 km feel like a journey.

### D-006a — Local SLM: build it differently · *supersedes part of D-006*
**Finding:** the latency-cover trick is sound and is validated prior art, not a gamble —
*Whispers from the Star* wrote the delay into the fiction as interstellar comms lag; PUBG's
Ally covers an LLM with a behaviour tree; inZOI shipped a 0.5B on-device model commercially.
Every project that did *not* mask latency was criticised for the pause. But three things in
D-006 as written are wrong:

1. **"Streams the coda" and "fails validation" are mutually exclusive** — you cannot
   un-display text. Fixed by **sentence-granular commit**: buffer, validate at each sentence
   terminator, then release. Budget moves from time-to-first-token to time-to-last-token.
2. **The coda must be an observation, never a reply.** If the player's input is a menu
   choice, a frontier model can bake every possible coda offline and the local model earns
   nothing. It justifies itself only against *unbounded state*: fuel remaining, what is
   bolted to Hugh, how long since you were last here, what you are carrying.
   **The baked line answers; the coda notices.** This reframe kills tonal mismatch, state
   contradiction and quest-invention by construction, and drops the task into 1.7B's weight class.
3. **VRAM is the risk, not latency** — and it does not crash, it silently pages over PCIe
   and can evict *the game's* textures, producing hitches the player blames on the engine.

**Technical decisions:**
| | Choice | Why |
|---|---|---|
| Model | Qwen3-1.7B Q4_K_M (1.11 GB, Apache-2.0) | Redistributable. Llama 3.2 is disqualified by licence, not quality |
| Backend | **Vulkan**, not CUDA | On Pascal, Vulkan *beats* CUDA at token generation (67.8 vs 62.5 t/s) and costs 41 MB instead of ~1.2 GB |
| Runtime | Bundled `llama-server.exe` subprocess, localhost HTTP, held in a Windows **Job Object** | Process isolation *is* the graceful degradation, implemented by the OS for free. In-process (LLamaSharp) means a native abort takes Godot with it |
| Guardrails | GBNF grammar + stop sequences + length cap + sentence validation | Only llama-server exposes GBNF *and* JSON schema |

Measured budget on a 1080: **~0.52 s** for a 40-token coda against 3-6 s of cover — a 6-10x
margin. The asterisk: short baked lines ("Yeah?") give only 0.6-1.5 s of cover, so a coda is
requested **only when the line's estimated delivery time is at least 1.5x measured p95
latency**. Hard abandon at 4 s.

**Also noted:** Steam has required disclosure of runtime-generated AI content since
2026-01-16. Not a blocker for a project that is not being sold, but recorded because it
changes the calculus if that ever changes.

**And the finding that matters most for the writing:** in both the local-model research and
the baked-corpus research, independently, *the perceived magic is memory, not prose*. PUBG
playtesters singled out the Ally remembering a name and a weapon preference. Callbacks buy
more than variety does. Build the memory, then the words.

### D-003b — Content envelope shrinks to 13 km; threat envelopes promoted · 2026-09-18
The world-scale pass finished with harder numbers than its interim verdict, and they
change the plan more than D-003a did.

**The comparison set I was reasoning from was fiction.** Re-measured, with sources:
*Elden Ring* is **13.5 km²**, not the 79 that circulates (which traces to a Reddit user
estimating in horse-lengths). *Skyrim* is **14.8 km² playable** — only 38.7% of its
worldspace is reachable. *Fallout 4* is 10.2 km²; *Fallout 3* is 8.6 (the ubiquitous
"14 km²" is Skyrim's number transposed). **No acclaimed open world in the set exceeds
about 50 km².** Our 268 km² grid is **20x Elden Ring**, for one developer.

**The decisive metric is content-hours per km², not POIs per km².** Elden Ring 4.45,
Skyrim 2.29, GTA V 0.67, Just Cause 3 **0.067**. The plan as written landed at **0.19**,
and its POI density was within 10% of Just Cause 3's — which is the genre's canonical
emptiness failure. Proposing those numbers needs a better argument than "a helicopter
makes emptiness cheaper."

**Decisions:**
1. **Keep the 16.384 km terrain grid** — terrain is a pure function and costs nothing,
   and a horizon out to 8 km is most of what makes this read as a country rather than a
   level. **Shrink the CONTENT ENVELOPE to a 13.0 km square (169 km², 63%)**, bounded by
   water, by terrain above the aircraft's measured 3,000 m hover ceiling, and by a
   contamination band. Skyrim fills 38.7% of its own worldspace; 63% is generous.
2. **124 named POIs (0.73/km²)**: 11 hand-authored anchors, 45 hand-built sites,
   68 kit-assembled, over ~2,500 unnamed procedural features.
3. **Threat envelopes (D-010) move to priority two.** Effective world size is physical
   size x (flown path / straight-line path). A 6 km direct leg that becomes an 18 km
   masked dogleg is a **3x multiplier on the entire map, bought with a data file**.
   D-010 is not a progression system with a scale side effect; it *is* the world-scale
   system.
4. **Altitude-banded authoring, as a rule.** Every place must read differently at 500 m
   (region identity, silhouette, linear features), 150 m (occupied? worth landing?) and
   15 m (slope, wires, clearance). The same terrain then pays three times. Each threat
   class gets its own altitude band, which makes the threat map an altitude map — and
   with a 3,000 m ceiling there is no "climb above it" escape.
5. **A helicopter game can afford a city a ground game cannot** — from 200 m a ruined
   city is a *pattern*, which is what procedural generation is good at — **but it cannot
   afford a forest floor.** Skew the budget accordingly. Cap the dense city core at ~8 km².

**The number that convinced me:** a sweep model calibrated on Skyrim's two independently
measured figures predicts one point of interest every **48 seconds** of travel. CD Projekt
Red have independently stated a "rule of 40 seconds." Two studios, no shared method, the
same constant.

**Scope warning, recorded honestly:** the full eight-region plan is roughly **3,800 hours
of world content alone**. That is years of solo evenings before any other system exists.
The 124-POI target is the version that can actually be finished.

### D-009a — Title and aircraft name confirmed · 2026-09-18 · **[APPROVED BY FRED]**
**ROTORWASH** is the title. **Hugh** is the aircraft, not the pilot. Both settled; stop
hedging about them in the docs.

### D-013 — Threat envelopes: how the world is actually gated · 2026-09-18
Built to D-010 and D-003b. Five classes, and the altitude band each owns *is* the level
design — measured as seconds of exposure before it has a firing solution:

| Threat | Band AGL | 10 m | 150 m | 800 m | 2500 m |
|---|---|---|---|---|---|
| Gun | 0–900 m | 3.5 s | 3.5 s | 3.5 s | — |
| MANPADS | 30–3000 m | — | 3.5 s | 3.5 s | 3.5 s |
| SAM | 120–6000 m | — | 5.2 s | 3.5 s | 3.5 s |
| Aerostat | 0–8000 m | 3.1 s | 3.1 s | 3.1 s | 3.1 s |

The consequences that make it a game rather than a stat block:
- **Below 120 m AGL a SAM simply cannot see you.** Hugging the ground is a real, legible
  tactic with a real cost — you are then inside every gun envelope in the country.
- **The aerostat is the answer to the habit.** It looks *down*, so it is the one thing that
  sees into the dead ground the player has learned to live in. That is what a tier-3
  region is for.
- **Terrain masking decays, it does not switch off.** Drop behind a ridge with a full track
  on you and it falls 1.00 → 0.09 over three seconds. Somebody is still looking at where
  you were.
- **The right countermeasure, or none.** Measured over three minutes in a live envelope:
  chaff takes a SAM from 7 hits to 0 and does nothing at all to a heat seeker; flares take
  MANPADS from 4 to 0 and do nothing to a SAM.
- **Graded, per D-005a.** 45 seconds inside a gun *and* a MANPADS envelope: everything took
  damage, 62% were forced down, none were deleted, and hits landed across six different
  systems. Dangerous and survivable — a gate, not a punishment.
- **Route inflation, measured.** A 14 km SAM between two points 29 km apart: straight
  through is 29 km at full exposure for 508 seconds; around is 47.2 km at zero. **1.63x on
  flat ground with nothing to hide behind** — and with terrain the detour is shorter while
  the map stays bigger. That is D-003b's claim, verified.

Two bugs found by testing, both of which would have shipped silently:
- The state machine recomputed `State` from confidence every frame, overwriting `Engaging`
  the instant it was set. **Nothing in the world ever actually fired a shot.**
- Flares burned for 3.5 s against a weapon with a 3.57 s time of flight, so they expired
  0.07 s before impact and appeared simply not to work.

### D-014 — Terrain relief is tuned against the threat report, not by eye · 2026-09-18
Threat envelopes went into the world and the coverage report immediately showed the
masking mechanic was decoration: emitters could see **96-100% of their envelope from
150 m**. The valleys were broad and shallow, which looks fine and hides nothing.

Two rounds of tuning, each measured rather than eyeballed:
1. Narrowed and deepened the valley carve, and added a tighter gully network. Masking
   became real - but median slope went to 21 degrees and the land you can put a settlement
   on halved, because the gullies were carving the basins people live in.
2. Made the upland field **bimodal** (`smoothstep(0.34, 0.72, continent)`) instead of a
   power curve. A power curve makes most of the map "somewhat upland"; a smoothstep gives
   genuinely flat low country and genuinely broken high country.

| | before threats | first fix | now |
|---|---|---|---|
| Median slope | 12.1° | 21.3° | **15.1°** |
| Settleable (slope < 7°) | 16.6% | 8.4% | **18.5%** |
| Airfield-able | 3.1% | 1.7% | **5.2%** |
| Emitter visibility at 50 m | ~99% | ~45% | **13-98%, avg 70%** |
| at 150 m | ~99% | ~75% | avg 88% |

**The variance is the design.** A site in the broken country sees 13% of its envelope and
is a nuisance; a site in the open basin sees 98% and is a wall. The player learns which is
which, and that knowledge is progression under D-005.

**And it produced the trade the world needed anyway:** the easy country to fly and land in
is the country with nowhere to hide. That is "cities, wilderness and everything in between"
expressed as a mechanic rather than as scenery.

### D-003c — Two benchmark cells remain unverified · 2026-09-18
The source-verification pass on `world-scale.md` could not close two cells before its
search budget ran out: **The Witcher 3** has no land-only area figure in the public record
(the 136 km² everyone quotes traces to a 2014 engine slide of two square bounding boxes,
mostly ocean in Skellige's case), and **RDR2**'s ubiquitous ~75 km² traces to a single
pre-release Reddit post that measured PNG file size as a proxy for map-image area, anchored
to a rectangle that includes water.

**Nothing in D-003b depends on either.** The decisive figures were Elden Ring at 13.5 km²,
Skyrim at 14.8 km² playable, and this project's own measured cruise speed. Recorded so that
nobody - including me - later reasons from the Witcher or RDR2 numbers as though they were
measured. They are not.

### D-015 — Dialogue: the model, built memory-first · 2026-09-18
Built to D-006a. The research found the same thing twice, independently, in the
baked-corpus prior art and in the shipped local-model prior art: **the perceived magic is
memory, not prose.** Playtesters singled out an NPC remembering a name and a weapon
preference, not the quality of its sentences. So memory is what got built first.

**The baked layer.** Lines carry *requirements* and the selector scores by how SPECIFIC a
match is, not merely whether it is legal. Measured: seven different arrival states produce
seven different openings, and a damaged first arrival gets the line written for a damaged
first arrival rather than the generic one.

**Repetition is a corpus problem, not a selector problem.** Six identical arrivals half an
hour apart originally produced the same line six times — because only one line legally
matched that state. No amount of selection cleverness fixes that; it is why the real corpus
is generated offline in the thousands. With six interchangeable openings available it now
rotates through all six with zero immediate repeats.

**The coda gate, measured.** At a p95 latency of 0.55 s the bar is 0.83 s of spoken line.
"You." is 0.8 s and is refused; "Back again." is 1.1 s and is accepted. A coda is also
refused when there is no model, and when there is genuinely nothing worth remarking on —
a full tank and an undamaged aircraft is not a sentence.

**Validation catches what the prompt cannot.** Twelve candidate sentences, twelve judged
correctly: questions, invented quests, invented destinations, restating the baked line,
anachronisms, forbidden phrases, over-length and empty all rejected. One miss on the first
run is worth recording — the blocklist had `"i can offer"` and let *"I could offer you work
if you want it"* straight through. Block **phrasings**, not sentences.

**Sentence-granular commit** works: tokens arrive one at a time, nothing is released until
a full stop arrives, and a decimal point is not mistaken for one.

**The prompt that actually goes to the model** is 115 words, which is comfortably inside
the budget for a 1.7B at the measured latency. By the third visit it reads:

> You remember about this pilot: last time the tail rotor at 35 %; they scavenge parts;
> they have a radar warning receiver on it

**Reversibility:** high. The corpus is data, the coda is strictly additive, and the whole
thing degrades to the baked layer if the model is absent, slow or wrong.

### D-018 — Dialogue wired into the game · 2026-09-18
Built to D-006a and D-015. Six files, three new.

**NPC roster.** Mattie (the tutorial NPC from D-015) is placed at the first Basin
settlement, determined by scanning the site list at startup — deterministic, no editor
data. Every other settlement gets a generic settler created lazily from a seeded RNG:
20 names, 5 persona templates that mention the site name, shared dialogue bank of 25
lines covering first meeting, returning visits, fuel/damage/standing states, partings
and talk. The settler factory lives in `DialogueCorpus` next to Mattie, so both are
tested by the same sim test suite.

**CodaServer.** A `Node` that manages `llama-server.exe` as a subprocess on localhost.
Finds a free port at startup so multiple instances do not collide. Held in a Windows
Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) so an unhandled Godot crash kills
the server automatically. If the binary or model is absent, `ModelAvailable` stays
false and the game runs on baked lines — process isolation *is* the graceful
degradation, per D-006a.

**DialoguePanel.** Conversation UI drawn with `_Draw()` to match the HUD aesthetic:
dark panel, pale green text, same colour palette as the instruments. Text reveals
word by word at half the estimated speaking rate — fast enough that reading never
feels held back, slow enough that the coda latency (0.52 s on a 1080) is invisible
behind even a short line. The coda appends in a warm accent colour after the baked
line finishes. State machine: Closed → Speaking → Options → Speaking/Farewell → Closed.
Hard coda abandon at 4 seconds.

**Integration.** `SiteInteraction` gains a lazy NPC registry and a Talk action at
settlements. First contact learns a `Contact` knowledge entry. `Main` routes input:
number keys to dialogue options, Escape to close, Space to skip reveal, all other
keys swallowed while the panel is open. `FlightHud` hides the site action panel
during dialogue to avoid visual overlap.

**Reversibility:** high. The panel is a single `Control` node, the server is a single
`Node`, and the NPC registry is a dictionary in `SiteInteraction`. Removing dialogue
is removing three nodes and a dictionary.

### D-016 — Mesh winding was backwards everywhere · 2026-09-18 · **[FRED SPOTTED IT]**
Godot treats **clockwise** as front-facing. Every hand-built mesh in this project used the
textbook counter-clockwise convention, which is the wrong one here.

Proven with a new `--windingtest` that renders two identical quads differing only in vertex
order, plus a third built with the terrain streamer's exact index order viewed from above.
The counter-clockwise quad is culled. So is the terrain patch.

**Why it hid for days:** it does not make anything disappear. Godot flips the normal on a
back face so two-sided geometry still lights, so every mesh kept rendering — with every
surface lit as though the sun were behind it. The world looked muddy and dark, and I treated
that as a palette problem: raising exposure, ambient and sun energy, desaturating, adding
macro brightness variation. All of it was compensating for inverted lighting.

**The lesson, which is now a rule on this project:** when a whole class of output looks
subtly wrong and no individual fix helps, stop adjusting and test the assumption underneath.
A five-minute render test settled what an hour of reasoning had got backwards twice.

Consequence: the lighting grade now needs re-tuning, because it was tuned against a bug.

### D-017 — POI distribution: lumpy, not sparse · 2026-09-18 · **[FRED'S QUESTION]**
Fred asked whether, given the helicopter's range dwarfs the map, POIs should be spread out
with sparser fill between.

**Sparser everywhere is the wrong move** — that is exactly the density Just Cause 3 has
(0.42 settlements/km²) and the reason it is the genre's canonical emptiness failure. But
*lumpier* is right, and the measurements agreed with the instinct: the old layout put you
within **3.1 km of something from any point on the map**, so no leg was ever a journey.

Done, in three steps, each measured:

| | before | after |
|---|---|---|
| Ground over 2 km from anywhere | 1% | **11%** |
| Longest gap | 2.5 km | **3.2 km** |
| Median distance to nearest | 572 m | 702 m |

1. Human infrastructure clusters around a few anchors per region; wrecks get their own
   incident anchors, so they come in fields rather than a sprinkle.
2. Regions were shrunk and pushed apart — they used to tile three quarters of the envelope.
3. **Fewer, fatter clusters.** The arithmetic is the whole design: 40 clusters over 169 km²
   sit 2 km apart and nowhere is ever remote. A dozen sit 3.5 km apart, and each then holds
   seven or eight places — so arriving somewhere is an event rather than a waypoint.

**But the honest finding is that spacing is not the lever.** Even aggressively clustered,
the longest gap is about a minute of flight. A 13 km map cannot be made to feel large by
spacing alone, and trying would cost content. The lever that works is **route inflation**:
threat envelopes and terrain masking, measured at 1.63x on flat ground and more with
terrain. A 3 km gap that must be flown as a 10 km masked dogleg is a journey; a 5 km gap
flown straight is not.

So the two systems do different jobs, and both are needed: **clustering makes arrival an
event, threat makes the going there a journey.** Fuel does neither, and per D-003a has
stopped pretending to.

### D-019 — The refit system, built · 2026-09-18

Built to D-011. Eight modules, eight bays, each with real physics.

**Architecture.** `Loadout` (sim layer, pure .NET) owns the module catalog and tracks
installed/bag state. `SiteInteraction` handles the physics effects on install/remove
(MassItem, DragArea, FuelCapacity). `Main` wires the game-system effects (SAS authority,
countermeasure flags on ThreatField) via events, so SiteInteraction never references
ThreatWorld directly.

| Module | Mass | Position (FRD) | Notes |
|---|---|---|---|
| Attitude hold | 12 kg | (2.3, 0, -0.55) | Enables SAS; F2 now requires the module |
| Radar warning | 8 kg | (2.1, 0.5, -0.65) | Countermeasure.RadarWarning flag |
| Chaff | 18 kg | (-5.8, 0.3, -0.85) | 30 rounds on install; tail-boom CG shift |
| Flares | 22 kg | (-5.8, -0.3, -0.85) | 30 rounds on install; tail-boom CG shift |
| Exhaust suppressor | 35 kg | (-1.5, 0, -2.0) | +0.15 m² forward drag; makes IR seekers 65% less effective |
| Long range tank | 45 kg | (0.4, 0, -0.15) | +350 kg fuel capacity; a full tank weighs 1195 kg |
| Cargo hook | 28 kg | (0.1, 0, 0.4) | Capability placeholder for underslung loads |
| Rescue hoist | 40 kg | (0.8, -1.3, -0.5) | Capability placeholder; left-side CG shift |

**How modules are found.** ~15% of wrecks, airfields and depots yield a specific module,
determined by site seed (deterministic — same world every time). Found during salvage on
the first search. Each module can only be found once; subsequent sites with the same
module are silently skipped. The distribution is weighted toward early-game modules
(SAS, RWR, chaff, flares) so the player finds something useful first.

**Installation.** At workshops and airfields only. Costs 1-3 parts depending on
complexity. Takes 8-20 seconds (proportional to mass). Removal takes 6 seconds and
returns the module to the bag. Both are gated by the existing `Settled` check — rotor
stopped, on the ground, at the site.

**What it replaced.** The F3 bench-fit key is gone. SAS (F2) now requires the module to
be installed. Countermeasures go through the refit system. The kneeboard's FITTED
section reads from real `Loadout` state instead of hardcoded booleans. The only three
bays that cannot yet be exercised (cargo hook, rescue hoist, and the functionality of
the long-range tank beyond fuel capacity) are capability placeholders — the bays exist,
the mass and drag are real, the gameplay feature is the next layer.

**The design promise D-011 made, verified.** All eight modules installed adds 208 kg,
shifts the CG, and modifies the inertia tensor. Tail-boom modules (chaff, flares) shift
the CG aft; forward modules (SAS, RWR) shift it forward. The exhaust suppressor adds
measurable drag. The long-range tank increases fuel capacity by 350 kg. None of this is
faked — it flows through the same MassProperties and DragArea the structural items use.
Eight new sim tests verify these properties.

**Reversibility:** high. The module catalog is data. Loadout is a pair of HashSets.
Installation effects are symmetric (add on install, remove on uninstall). New modules
are one record and one switch case.

## D-020 — The aircraft is trimmed before the pilot ever touches it

**Decision.** Every spawn goes through a real trim solve (`sim/src/Trim.cs`), and the
solution's controls and attitude are what the aircraft starts at.

**Why.** Fred reported the helicopter felt oscillatory. It was not oscillating: it was
diverging, and he was chasing it. `PlaceInFlight` put the aircraft wings-level with the
stick centred, which is not a trim state on a single-rotor helicopter — the tail rotor's
3.6 kN of side force and its yaw moment were uncancelled from frame one. The pilot's whole
job became correcting a divergence that never stopped, and correcting a continuous
divergence through a lagged actuator is the textbook recipe for pilot-induced oscillation.

Hands off at "trim", the old aircraft passed 30° of bank in 2.3 s and was inverted by 5 s.
Trimmed, the same release holds for 6.5 s and starts from rest.

**Reversible.** Entirely — `PlaceInFlight` still exists and does the old thing.

## D-021 — Limited-authority stability augmentation, tuned by measurement

**Decision.** `sim/src/Stability.cs` adds rate damping with weak levelling and heading hold,
upstream of the actuator rather than as a force on the body. Authority 0.30 of travel.
Off by default in the sim so instrumentation measures the bare airframe.

**Why.** The bare airframe is genuinely unstable and should be — but a trimmed release still
departs at 6.5 s, because the pedal that trims a hover is nearly a third of a travel wrong
by 40 kt, so any acceleration walks the nose away and the spiral follows. Real helicopters
answered this with a series actuator, not with "fly better".

Three bugs in it were invisible in review and obvious on a graph, which is the pattern this
project keeps hitting:

* The pitch control derivative is **negative** (forward stick, nose down) while roll and
  yaw are positive. A uniform `-rate * gain` therefore damped two axes and drove the third.
  Signs now come from the measured derivatives, quoted in the source.
* "Hands off" was tested against a centred stick — but trim holds 0.275 of lateral cyclic,
  so the levelling terms were permanently disabled. Hands off means *the stick is where
  trim left it*.
* At 0.18 authority the system was saturated almost continuously, which turns a linear
  damper into a bang-bang controller. The signature is unmistakable once plotted: at 0.18,
  more gain made it *worse* (x0.4 → 7.2 s, x1.0 → 4.0 s); at 0.30, more gain simply helped
  (x0.4 → 7.2 s, x1.0 → 22.3 s).

**Reversible.** `Sas.Enabled = false` gives the bare airframe exactly as before.

## D-022 — The augmentation stands down when another controller is flying

**Decision.** `Sim.Sas.Enabled` is true only when a human is on the controls — never while
`OverrideControls` is driving, and never while the blunt `SasAuthority` assist is blending
in autopilot output.

**Why.** Enabling the SAS made the bridge self-test's roll response go from 61.6 °/s to
281.9 °/s at only 24.5° of attitude — a limit cycle, not a divergence. The first guess was
that Godot and the sim were each integrating their own copy of the state. That was wrong:
`ComputeWrench` contains the actuator update, so the SAS does run in the game path, and a
rate sweep showed it behaves correctly at 60, 120, 240 and 480 Hz in the pure sim.

Instrumenting the real Godot path settled it in one run. The **commanded** roll input was
swinging the full ±1 at about 2.5 Hz — the scripted autopilot was oscillating, not the SAS.
Rate damping changes the plant, and the autopilot's gains were tuned against a plant without
it, so the outer loop overshoots and the two controllers fight.

A human is the slow outer loop the augmentation exists for; another controller is not, and
it already does attitude hold itself. Only one of them gets to be the damper.

**Reversible.** One condition, one line.

## D-022a — The stick's centre is the trim position

**Decision.** `FlightInput` carries a trim datum on all three axes, set from the trim
solution at every spawn, with a force trim release (T) that makes the current stick position
the new neutral.

**Why.** Trimming the airframe and not trimming the *controls* fixes half the problem and
leaves the half the pilot feels. A trimmed hover needs about +0.275 of lateral cyclic; a
spring-centred joystick returns to zero. Without a datum, letting go of the stick is a large
out-of-trim command — the aircraft carefully balanced at spawn gets shoved straight back out
of balance the moment the pilot relaxes their hand. `TrimPitch` and `TrimRoll` already
existed in the input layer and nothing had ever set them; pedal had no trim at all.

Verified end to end by a new self-test phase that releases the controls on the **player**
path rather than through `OverrideControls`: hands off for 20 s now ends at 24.8° of bank,
still flying, with the augmentation confirmed running.

## D-023 — The lighting grade re-derived after the winding fix

**Decision.** Lighting, sky, fog and grade moved out of `Main` into `SceneMood`, with
values re-derived rather than adjusted.

**Why.** D-016 recorded that the grade had been tuned against the winding bug — ambient at
1.15 to rescue surfaces that were dark only because they were lit from behind, exposure and
saturation compensating for the same thing. With the winding fixed those became
over-corrections on top of a correct image. Ambient is now 0.66 and the sun does the work,
so terrain reads as landform instead of a flat wash.

Two things in the terrain shader were doing the same job: `dead_tint * 1.6` brightened the
dry patches into acid yellow, and the detail texture was blended at only 0.65 toward the
colour-matched version, so the near field read as green lawn while the far field read as dry
olive — a seam visible from the air. Now 1.18 and 0.88.

`SceneMood` carries Afternoon, Overcast and Dusk, which is the groundwork for weather.

## D-024 — Site screenshots aim at the site

**Decision.** The screenshot director's stand-off scales with altitude, and each capture
reports whether the subject is actually in frame.

**Why.** Every site picture in the set was empty scenery. A fixed 150 m stand-off at the
160 m the settlement shot flew at puts the site 47° below the nose, well outside the field
of view — the settlements were building correctly and sitting underneath the aircraft.
Staring at the images could not distinguish "did not build" from "behind the camera", so
the capture now measures it.

## D-025 — The cockpit has an inside

**Decision.** `AirframeBuilder` builds a cockpit interior — floor, roof, side walls,
bulkhead, glareshield, panel, pedestal, overhead, pillars, seats and controls — lit by two
omni fills, with the viewpoint moved to where a pilot's eyes actually are.

**Why.** Fred said the interior view looked weird. It was worse than weird: there was no
interior at all, and the viewpoint was in the wrong place twice over.

`CockpitOffset` was `(-0.62, 0.55, +1.45)`. Forward is −Z and the cockpit spans Z −4.0 to
−2.4, so the camera sat behind the CG in the engine bay looking forward through the entire
length of the fuselage. And because the fuselage is a single-sided shell, the winding fix
(D-016) means every body panel is backface-culled when seen from inside — so the view was
an open frame with the terrain visible through the floor, roof and both walls, and the
double-sided glass left hanging in mid-air. That is exactly what the screenshot showed.

Two things worth keeping:

* Everything in here is a **closed box**, never a single-sided panel. A panel has to be
  wound correctly and there is no way to know which way that is except by rendering it; a
  box is right from every side by construction. Given D-016, that is cheap insurance.
* The interior needs **its own light and its own material**. An enclosed cockpit receives no
  direct sun, and sky bounce is not modelled at this quality tier, so at the exterior's
  0.135 albedo the whole interior rendered as solid black shapes. One low fill left the
  roof and overhead unlit — an unlit box in the top of frame reads as a hole in the
  aircraft — so there are two.

The windscreen was also at 0.62 alpha, which is a welding visor. Now 0.11.

**Still crude.** The panel face sits below the fixed forward view, so no instruments are
visible from the seat, and the windscreen side pillar is chunky at this eye position. Both
want a proper look-around control rather than more geometry.

**Reversible.** Two mesh builders and a light; delete the three lines in `Build`.

## D-026 — Weather and the sun are a model, not a set of presets

**Decision.** `sim/src/Weather.cs` provides a deterministic weather and solar model.
`SceneMood` no longer offers named looks; it reads a clock and renders whatever the model
implies. The game clock runs at 30× real time — a full day in forty-eight minutes.

**Why.** The environment interface has carried a wind vector and a spatially correlated gust
field through to the rotor since the beginning, and **nothing ever set them**. Every flight
in the game so far has been in dead calm air at standard temperature. This puts something on
the other end of that wire.

Two properties were chosen deliberately:

* **Deterministic, with no accumulated state.** Everything is a pure function of the clock
  and a seed. A flight can be replayed; a headless test can ask what the weather will be at
  14:20 on day three without simulating its way there; a save file stores a seed.
* **Summed sines, not a random walk.** A walk wanders, needs clamping, and eventually parks
  against a limit. Sines at incommensurate periods drift, return, and stay in range for
  free.

Wind is calibrated so an ordinary day is a light breeze. The first pass sat at 15–20 kt as
its *baseline*, which makes every hover a handful and leaves no quiet days for weather to be
a contrast against. It now peaks around 23 kt with 17 kt gusts over a fortnight.

Verified: translational lift saves 12.3% of the collective in a 23 kt headwind, which is
proof the wind reaches the rotor rather than merely existing.

## D-027 — Night is lifted well above physical accuracy, on purpose

**Decision.** The ambient floor at night is 0.24, not the 0.055 the light budget suggests.

**Why.** At the physically honest value the night render was **pure black** — not moody, not
dim, black, with no horizon, no terrain and no aircraft. You cannot fly what you cannot see.
The chosen value reads terrain silhouettes and the airframe while leaving night as something
you would rather not have to do. Moonlight runs as its own cooler directional light, so dusk
is two sources crossing over rather than one source changing colour.

**Also:** both directional lights are `SkyMode.LightOnly`. Godot draws the sky's sun disc
from the light's colour and energy, and at dusk the light is dimmer than the horizon glow
the gradient paints behind it — so the sun rendered as a **dark circular hole** sitting on
the horizon. The gradient sells a sunset better than a disc does.

**Reversible.** Both are single constants.

## D-028 — The aircraft has lights

**Decision.** `AircraftLights` adds navigation lights (red left, green right, white tail),
a flashing anti-collision beacon, and a landing light on **L**, auto-on in the dark until
the pilot touches the switch.

**Why.** Added the moment night became flyable, because a night with no lights on the
aircraft is a night where you cannot tell which way you are pointing. The nav arrangement is
genuinely useful rather than decorative — a glance along the boom tells you your own
orientation.

Only two are real lights. The beacon gets an omni because a red glow washing over the boom
is the whole point of it, and the landing light is a spot because it has to illuminate
ground. The nav lights are emissive geometry with no light attached: they exist to be seen,
not to light anything, and three more omnis on every aircraft buys nothing.

Two bugs, both found by looking rather than reasoning:

* The lenses were `ShadingMode.Unshaded`, which writes albedo straight out and **ignores
  emission entirely** — so they never exceeded 1.0, never bloomed, and were invisible at
  night, which is the only time they matter.
* The auto-on rule read the clock once in `_Ready` and got the wrong answer every time,
  because the aircraft is built before anything has decided what time it is. It is a
  per-frame rule now, which is better behaviour regardless.

**Reversible.** One node, added in one line of `AirframeBuilder.Build`.

## D-029 — Terrain collision is an unscaled trimesh

**Decision.** Terrain collision is a `ConcavePolygonShape3D` built at true world spacing,
replacing a `HeightMapShape3D` on a scaled `CollisionShape3D`.

**Why.** Nothing could stand on the terrain, and it had been that way from the beginning.

`HeightMapShape3D` samples exactly one unit apart, and the only way to widen that is to
scale the collision shape — here by **(8, 1, 8)**, since chunks are 512 m across and sampled
65 times. Godot handles that badly, and it fails in the most misleading way available:
**raycasts against the scaled shape return correct hits at exactly the right height, while
bodies pass straight through it.**

That combination is why it survived so long. Every probe agreed the ground was present and
correctly placed — the on-foot test's ray reported `Body_0_-1 at y=138.0` against a terrain
height of 138.0, which is exactly right — and the pilot sank through it anyway, accelerating
until they were 8.5 km below a helicopter they had been standing eight metres from.

Landings never caught it because every site sits on a graded pad with its own collision, so
the aircraft only ever touched down on something that was not terrain.

A trimesh carries no scale, so there is nothing to get wrong. About 8k triangles per chunk,
for the two dozen chunks that carry collision.

**New regression test.** The bridge self-test now drops the aircraft, engine off, onto open
terrain far from any site, and requires that it stops. It rests 1.2 m above ground.

**Noted, not fixed:** after a teleport the ground does not exist for roughly 0.3 s while the
chunk streams in, which is long enough for a falling body to pass through where it is about
to be. The test waits for collision before dropping; the game will need to as well.

## D-030 — Instruments opt out of the weather

**Decision.** `HelicopterController.UseWorldWeather` is off for the bridge self-test.

**Why.** The moment the world acquired weather, the control-derivative measurement stopped
being repeatable — the same code reported +34.9 °/s of roll on one run and −56.3 °/s on the
next, and the sign flip read as a serious regression in the flight model. It was gusts. A
derivative measured in gusty air is a measurement of the gusts. Two consecutive runs now
give identical numbers to the decimal.

## D-031 — The aircraft is held until there is a world to fall onto

**Decision.** `HelicopterController` freezes the body after every spawn or teleport until a
downward ray finds collision, with an eight-second escape hatch.

**Why.** Terrain collision streams in per chunk, and after a teleport the ground beneath the
aircraft genuinely does not exist for about a third of a second — measured, not guessed.
That is easily long enough for a falling body to pass through where it is about to be, and
once through it never comes back. It applies to the initial spawn too, which is the one
every session starts with.

Holding station until a ray finds something is cheaper and far more predictable than making
the streamer synchronous, and it degrades safely: if collision never appears the aircraft is
released anyway with a warning, rather than hanging in the air forever.

**Reversible.** One flag and one early return.

## D-032 — On-foot: analytical terrain, not physics collision · 2026-09-19

**Decision.** The pilot's `CharacterBody3D` does not use `MoveAndSlide` for terrain floor
detection. Instead, `WorldHeight.At` provides the floor analytically. Movement is applied
by direct `GlobalPosition` modification, and `SnapToTerrain` clamps Y to the ground.
The collision mask is set to 0.

**Why.** `CharacterBody3D.MoveAndSlide` does not work with `ConcavePolygonShape3D` in
Godot 4.7. The trimesh terrain collision works perfectly for `RigidBody3D` (the helicopter
stops on it, verified by the selftest terrain-drop test), but `MoveAndSlide` passes
straight through. BackfaceCollision had no effect. This is the same class of bug as D-029
— raycasts hit the shape correctly, but the kinematic sweep test ignores it.

Since the world has an exact height function (`WorldHeight.At`, which includes site pads),
using it directly is both simpler and guaranteed correct. MoveAndSlide would have been an
approximation of the same function sampled on a grid.

At settlements, site buildings create collision bodies above the terrain. The pilot spawns
at the helicopter's ground level (the higher of terrain height and the helicopter's resting
altitude minus its skid offset), and `_siteFloor` remembers this height so the pilot stays
on the correct surface while walking around the site.

**Regression tests.** `--foottest` exercises the full cycle: fly to a site, land, shut down,
dismount, walk 8 m, activate Rotor Time, drain it, walk back, board. Validated that the
pilot stays on the ground and does not fall through the world.

**Reversible.** Medium. If a future Godot release fixes CharacterBody3D on ConcavePolygon,
the analytical floor can be replaced with MoveAndSlide by restoring the collision mask and
removing the SnapToTerrain/ApplyVelocity calls.

## D-033 — Rotor Time charges from flight, drains on activation · 2026-09-19

**Decision.** Rotor Time is a single resource that charges during flight (proportional to
airspeed and attitude) and drains when activated. It slows `Engine.TimeScale` to 0.3x and
zooms the FOV to 45° while active. Activation requires >= 15% charge and on-foot mode.
Charge caps at 1.0 and drains at a rate that gives about 6 seconds of real-time slowdown
from a full charge.

**Why.** The vision doc specifies Rotor Time as "one mechanic in two contexts" — slow-motion
for placed shots in the air and called shots on foot. The charging from committed flight
ties it to the flying, so the pilot and the helicopter are the same person mechanically:
you earn on-foot power by flying well.

The drain rate and slow factor (0.3x) were chosen so the effect is impactful but brief.
Six seconds of game time at 0.3x is about two seconds of real time — enough to aim a
called shot, not enough to trivialise encounters.

**Reversible.** High. One class, two integration points in Main.

## D-034 — Terrain physics material: zero bounce · 2026-09-19

**Decision.** Every terrain collision `StaticBody3D` gets `PhysicsMaterial { Friction = 1.0,
Bounce = 0.0 }`.

**Why.** The looptest's helicopter bounced on initial touchdown and cascaded into dynamic
rollover on a 4° slope. The default physics material has zero bounce in theory, but with
the ConcavePolygonShape3D trimesh the solver produced measurable restitution — enough for a
0.14 m/s touchdown to bounce, land harder, bounce higher, and tip over.

The explicit material eliminated the effect completely: the same 4° slope now produces a
clean touchdown with no bounce.

**Reversible.** One property on the StaticBody3D constructor.

## D-066 — On-foot combat: hitscan sidearm with called shots

*Renumbered from D-035, which was already in use above.* · 2026-09-19

**Decision.** The pilot carries a revolver (6-round cylinder, 12 spare). Left click fires
a hitscan ray from the camera centre. When it intersects a hostile NPC's body-zone
collision shape, that zone's effect applies: head → instant down, torso → wound (speed
halved), arm → accuracy loss (×0.4 stacked), leg → immobilise, weapon → disarm. During
Rotor Time the HUD projects diamond markers onto every visible zone with labels, and the
one under the crosshair is highlighted — the mechanic is skill-based aiming in slow-motion,
not a target-lock menu. The pilot has 100 HP; hostile NPCs fire back after a reaction delay
with a hit probability scaled by their accuracy factor and range.

**Architecture.** `sim/src/Combat.cs` (pure .NET) owns the data model: `BodyZone` enum,
`CalledShot.Resolve()` for effect lookup, `NpcHealth` for per-zone damage tracking with
effect stacking, `SidearmState` for ammo management, `PilotHealth` for player damage.
`game/scripts/HostileNpc.cs` builds seven `StaticBody3D` zone colliders on collision
layer 4 with `body_zone` metadata. `game/scripts/Sidearm.cs` fires the ray and resolves
hits. `FlightHud.cs` draws ammo counter, pilot health bar, damage flash vignette, shot
feedback text, and zone markers (the markers only appear during Rotor Time). Main wires
NPC fire events → pilot damage → recovery. Five simlab tests cover zone resolution, NPC
attrition, sidearm mechanics and pilot health. The combattest exercises the full cycle
headless: fly, land, dismount, spawn NPC, hip fire, aimed torso shot, RT headshot, reload,
pilot damage, board.

**Why this design.** The vision doc says Rotor Time is "one mechanic in two contexts":
in flight it places shots from a manoeuvre; on foot it places called shots against body
zones. The mechanic IS the slow-motion — without RT you fire centre mass and hope; with
RT you can pick a limb. This makes the charge-from-flying loop pay out on foot, which is
the bridge that makes the pilot feel like the same person in both contexts. Violence has
weight: six rounds, limited spare ammo, and three-hits-down for the pilot makes every
encounter tense rather than trivial.

**Why hitscan, not projectile.** A revolver's bullet travel time over 50 m is ~0.07 s.
Modelling it adds complexity (raycasts per frame, leading targets) that would be
invisible to the player at these ranges. Hitscan is honest about what it is.

**Reversibility:** high. The combat layer is additive — removing it means deleting four
files and the wiring in Main. The sim-layer types have no dependents outside combat.
NPC spawning is driven by `SpawnHostileNpc()` in Main; integrating it with the world
(which sites are hostile, bandit camps, etc.) is a separate decision.

## D-063 — Rain is drawn, and overcast daylight is flat rather than dark

*Renumbered from D-032, which was already in use above.*

**Decision.** `WeatherEffects` draws rain as world-space GPU particles whose emitter follows
the camera, scaled by the weather model's precipitation and slanted by its wind.

**Why.** The weather model has carried precipitation since it was written and nothing ever
drew it, so "Rain" was a word in a debug line and a change in fog density.

Three things worth keeping:

* The particles are **world-space** while the emitter follows the camera. Local coordinates
  carry the whole shower along with the aircraft, which looks like flying inside a jar
  rather than flying through weather.
* `Amount` is only written when the rounded value actually changes. Writing it **restarts
  the particle system**, so driving it straight from a continuously varying weather value
  resets the rain every frame and nothing is ever drawn.
* Wind slants the rain. It is the most legible wind indicator the game has — far better
  than a number on a HUD.

**Also:** overcast daylight was far too dark. The sun was cut by `1 - cover * 0.86`, and a
rainy morning rendered as a bright sky over near-black ground, which is what night looks
like. An overcast day is *flat and bright*, not dark: the cut is now 0.70, ambient rises
further with cover, and exposure lifts slightly under a heavy deck.

## D-064 — A rotor strike is an event, not a state

*Renumbered from D-033, which was already in use above.*

**Decision.** `LandingController` latches the rotor strike, re-arming only once the disc is
clear of the ground again.

**Why.** The geometric test that detects a strike stays true for as long as the wreck lies
there, so an unlatched check re-fires every frame. The new drop phase in the bridge
self-test logged **1141 identical strikes from one impact**, each re-applying full damage and
each one worth a journal entry. Now one. A second strike on a second bounce is still a
second strike, because the latch clears when the disc is clear.

## D-036 — Save / load: parked-only, JSON, engine-off restore · 2026-09-19

**Decision.** Save only when parked and shut down at a site. JSON via `System.Text.Json`,
single slot (`user://saves/save1.json`), human-readable. On load the aircraft is placed
with engine off and rotor stopped — no rotor/engine internals are serialised.

**Architecture.** `SaveData` (sim/) is a pure .NET DTO with no Godot dependency. It owns
nested DTOs for every subsystem: damage events, knowledge entries, site records, NPC minds,
memory facts. Static helpers (`CaptureProgress`, `ApplyProgress`, etc.) marshal between
the live objects and the DTO. The game layer (`Main.TrySave`/`TryLoad`) handles
Godot-specific state: position as north/east/altitude/heading, NPC registry reconstruction,
threat emitter detection flags, and loadout physics re-application.

**What is saved.** Fuel, all nine component health values, damage log, installed + bagged
modules, progress clock, inventory (fuel/scrap/parts), knowledge entries, site records
(visited/searched/installed/removed flags), journal, NPC minds (site assignment, persona,
memory facts, dialogue line usage), sidearm ammo + spare, pilot HP, Rotor Time charge,
countermeasure counts (chaff/flares), and RWR detection flags (which emitters have been
painted). Position as (north, east, altitude, heading).

**What is NOT saved.** Rotor omega, blade flapping, inflow state, governor integral,
actuator positions, engine temperature — because save is gated on parked+shutdown, all of
these are zero or irrelevant. On load the aircraft spawns cold and dark, which is the
correct state for a parked helicopter. This eliminated roughly 40 fields from the save
format.

**Save gate.** `CanSave` requires: `LandingController.OnGround`, rotor RPM < 65%,
`SiteInteraction.AtSite` is not null, `SiteInteraction.Busy` is false, and dialogue panel
is closed. This is consistent with the project's principle that "landing is the expensive
act" — the save point is the reward for a successful approach.

**NPC restore.** NPC minds are keyed by site ID. On load, the registry is rebuilt: Mattie
is placed at the first settlement (determined by `IsFirstSettlement`), all others get
settler personas seeded from their site. Memory facts and dialogue usage timestamps are
restored so NPCs remember across save/load.

**Threat intel.** `ThreatTrack.EverDetected` flags are saved as a list of emitter indices.
On load, the flags are restored so RWR contact history persists — the sortie that nearly
killed you still pays out its intel after a reload.

**Verified.** Five simlab round-trip tests (progress, damage, loadout, NPC, full). One
headless Godot test (`--savetest`) that flies to a fuel cache, saves with distinctive state
(partial fuel, damage, knowledge, ammo, pilot health, RT charge), trashes everything,
loads, and verifies fourteen properties survived.

**Reversibility:** high. `SaveData` is one file with no dependents outside the save system.
The restore methods on `Progress`, `Damage`, `Loadout`, `DialogueBank`, `PilotHealth` are
each one or two lines. The game-layer save/load is ~150 lines in Main.

## D-065 — Cockpit lighting belongs to the lights, not the airframe

*Renumbered from D-034, which was already in use above.*

**Decision.** The cockpit skylight fill moved from `AirframeBuilder` into `AircraftLights`,
and instrument faces plus a panel flood were added, all driven by the sun's elevation.

**Why.** The fill stands in for sky bounce the renderer does not model at this tier, so it
has to **dim** — it is daylight finding its way in through the glass. Built as part of the
airframe it was a constant, and after dark the cockpit sat brightly lit inside a black
world, which is exactly backwards.

Two more, both visible only in a render:

* The instrument bezels were at `NoseZ + 0.97`, the *forward* face of a panel spanning 0.98
  to 1.18 — every instrument was mounted facing out of the nose, where only the weather
  could read them.
* The first panel lighting pass blew the glareshield to white. Instrument lighting that
  outshines the world outside is how you lose the horizon; it is much more restrained now.

## D-035 — The audio is measured, and power is now audible

**Decision.** `tools/simlab/AudioTests.cs` renders `RotorSynth` into an array and measures
it. Torque now contributes to blade slap and rotor wash directly, not only through blade
loading.

**Why.** Audio is the one part of this project nobody can check in a screenshot, and it had
no test at all — a synthesiser can be silent, clipped, full of NaN, or modulating at the
wrong rate, and every one of those survives indefinitely if the only way to notice is to put
headphones on.

The synth came out of it well. The **blade-pass rate** — the thing that decides whether it
sounds like *this* machine — measures 10.73 Hz against 10.79 expected, and tracks rotor
speed correctly down to 70% Nr.

One real defect: going from a quarter torque to an overtorque changed the output level by
**2.5%**, so the player could not hear power at all. Slap was driven by blade loading, tip
Mach, vortex ring and descent, and torque reached only the gear whine. Now 0.0518 → 0.0964
RMS across the same range.

**And one bad test.** The first version held blade loading fixed while varying torque, which
is not a flight condition any aircraft can be in. It made the synthesiser look deaf to power
when the real answer was that the question was impossible. The state now moves coherently —
which is the same lesson as the control-derivative tests that measured departure instead of
response.

## D-067 — The weather has a voice of its own

*Renumbered from D-036, which was already in use above.*

**Decision.** `WeatherSynth` (sim) and `WeatherAudio` (game) add rain and wind, on a
**non-positional** player, separate from the rotor.

**Why.** Separate from `RotorSynth` because the two follow different things: the rotor
follows the aircraft's telemetry, this follows the world. Mixing them would tie the
weather's volume to the rotor's, and the weather has to keep going when the engine is shut
down — standing beside a cold aircraft in the rain should not be silent.

Non-positional for the same reason. Weather is not somewhere; it is everywhere the player
is. Attenuating it by distance to the helicopter would be wrong in exactly the situations
that matter most, most obviously once the player has climbed out and walked away.

Shelter is a parameter rather than a separate mix: in the cockpit the rain is on the other
side of the glass, so it is quieter *and* has its top end taken off, which is most of what
"inside" sounds like. Measured: 0.0987 RMS outside a downpour, 0.0316 in the cockpit, and
0.0006 on a still dry day.

## D-037 — Nothing deriving from GodotObject is constructed on a worker thread

**Decision.** `PropScatter` uses a small splitmix64 struct instead of Godot's
`RandomNumberGenerator`, because scatter is generated on worker threads.

**Why.** It crashed the process. `--worldreport` died with an `AccessViolationException`
inside the binding layer, with `RandomNumberGenerator..ctor()` on the stack, called from
`PropScatter.Place` on a thread-pool thread. Prop scatter runs during ordinary play, so this
was a random hard crash in the shipping game, not a tooling problem.

This is the **second** time this exact rule has been broken here — `Godot.Collections.Array`
was previously being built on the terrain worker, with the same symptom. So it is worth
stating flatly rather than rediscovering: *reading* from an already-constructed Godot object
off-thread has been fine; **constructing** one is not.

The replacement is deterministic and seeded identically, so scatter remains reproducible —
though the layout differs from before, which for decorative scatter costs nothing.

## D-038 — Rock appears at cliff angles, not only at impossible ones

**Decision.** The terrain shader's rock threshold moves from a surface normal Y of
0.68/0.44 to 0.84/0.55, and exposed rock gets perturbed horizontal strata.

**Why.** The old thresholds put bare rock only on ground steeper than about 64°. The world
report has the 90th-percentile slope at 43° and the 99th at 65°, so effectively nothing in
the world ever reached it and every hillside — however steep — was grassed to the top. Rock
now begins around 33° and is bare by 57°.

The strata term is perturbed by a large-scale noise deliberately: an unperturbed height
term rings the whole world in contour lines, which is what it did on the first attempt.

**Honestly assessed:** this is a modest improvement, not a transformation. Steep ground now
shows gravel and stone where it did not, but most of the visible landscape is below 33° and
still reads as ground cover. Real cliff faces would need the height function to produce
them, and the same report says it mostly does not.

## D-068 — Kneeboard map with fog of war

*Renumbered from D-039, which was already in use above.* · 2026-09-19

**Decision.** The kneeboard gets a fourth page: MAP (previously AIRCRAFT, KNOWN, LOG).
It shows a top-down terrain chart with grid-based fog of war, site markers, threat
envelopes and the aircraft's position.

**Why.** Pillar 4: "the map starts near-empty. Flying reveals it; people tell you about
it; old charts fill it in." Pillar 3: "knowledge is the progression." Without a spatial
map, discovery has no visual payoff. The player cannot tell they have explored 40% of the
world versus 4%. The KNOWN page lists sites; the MAP page shows *where they are* and
*what is between them*.

**Architecture.**

`sim/src/FogOfWar.cs` (pure .NET, no Godot dependency): 128×128 grid over the 13 km
content envelope, ~102 m per cell, one bit per cell. `Reveal(north, east)` marks cells
within a 500 m radius. Serialised as a packed byte array (2048 bytes) for save/load.

`Kneeboard.cs` draws the MAP page using `_Draw()`:
- Terrain: a 256×256 `ImageTexture` generated once from `WorldHeight.RawAt`, coloured
  with a four-stop military-chart ramp (dark olive → olive → tan → pale grey).
- Fog: a 128×128 `ImageTexture` updated only when `RevealedCount` changes. Unrevealed
  cells are nearly opaque; revealed cells are transparent. The fog texture is overlaid
  on the terrain texture, so undiscovered areas are uniformly dark.
- Sites: coloured diamond markers, drawn only for visited or known sites. Colour varies
  by kind (gold for fuel, green for settlements, blue for workshops, etc.). Labels
  appear when the map is large enough.
- Threats: translucent red circles at the engagement range of each detected emitter.
  Only emitters that have been painted by the RWR appear — the sortie that nearly killed
  you pays out on the map as well as in the kneeboard's KNOWN page.
- Aircraft: a white chevron at the current position, rotated to heading.
- Scale bar: 2 km reference at the bottom.
- Header: "CHART" title with survey percentage ("14% surveyed").

`Main.cs` calls `_fog.Reveal(-pos.Z, pos.X)` every physics frame, mapping Godot X=east
and -Z=north into the sim's coordinate convention. Fog state is captured and restored in
save/load (`SaveData.FogGrid`).

**The screenshot director** now captures a kneeboard map shot (23_kneeboard_map.png) so
the map rendering can be compared over time.

**Tests.** `save_fog` simlab test: reveal at three positions, round-trip through bytes,
round-trip through JSON, verify cell-by-cell match. All existing tests continue to pass.

**What it does not do yet.**
- No zooming or panning — the whole world fits on one page.
- No waypoint selection or bearing line.
- Knowledge from dialogue does not yet paint sites on the map (the Progress.Learn
  pathway exists; wiring it is a single `p.Knows($"site.{site.Id}")` check that is
  already in the site marker filter).
- No compass rose or grid lines.

**Reversibility:** high. One file in sim/ (FogOfWar.cs), one kneeboard page, one
`byte[]` property on SaveData, three lines in Main. Nothing depends on it.

## D-040 — Water, woodland and roads · 2026-09-19

**Decision.** Three ground-cover systems fill the landscape between rocks and terrain
shape:

1. **Water** — flat shader planes at `WaterLevel` (-5 m) streamed alongside terrain
   chunks. The shader paints stagnant, murky water with value-noise colour variation
   and subtle TIME-based drift.
2. **Woodland** — green trees (trunk + noise-deformed icosphere canopy) as a fourth
   `PropKind` in PropScatter, driven by a separate forest-noise field (Perlin, freq
   0.00065, 3 octaves) so patches cluster naturally rather than scattering uniformly.
   Six mesh variants, placed on lower wetter ground (slope > 0.72, h < 240 m,
   h > WaterLevel).
3. **Roads** — batched triangle-strip mesh connecting 80 "roaded" sites (settlements,
   workshops, airfields, depots, fuel caches, farmsteads). Each site links to its 3
   nearest neighbours within 3.5 km, producing 153 road segments and 18 190 verts in
   a single draw call.

**Why.**

The STATUS.md Next list identified roads, water and woodland as the remaining gaps in
the terrain item. Rock faces and strata were done in D-038; this finishes the set.
Water fills the deepest valleys (terrain min is -13.2 m, WaterLevel is -5 m); woodland
puts living trees on lower, wetter slopes where hardier species hold on; roads connect
the places people drove to before the collapse.

**Key choices.**

- *WaterLevel = -5 m.* Valley floors range from -8 to -24 m. Setting the level at -5 m
  fills deep valleys without flooding basin settlements (placed at raw height > 6 m).
  Reversible: change one constant.
- *Forest noise separate from clump noise.* Woodland should cluster differently from
  scrub — large contiguous stands, not the patchy fields that the existing clump noise
  produces. A second Perlin field at a lower frequency (0.00065 vs 0.0014) gives wide
  forest belts that follow valleys.
- *Road-worthy site kinds.* Wrecks, relays and overlooks are excluded because nobody
  drove to those — they were accessed by air or on foot. This is a lore decision.
- *3 nearest neighbours within 3.5 km.* Produces natural clusters within regions and
  sparse links between them, without an explicit graph-theory algorithm.
- *SurfaceTool format.* The GreenTree mesh shares a SurfaceTool between trunk (via
  `AddTaperedSegment`/`Quad`, which do not set normals) and canopy. The trunk establishes
  the vertex format without normals, so the canopy must also omit manual normals and
  use `GenerateNormals()` for the whole mesh. This was discovered as a runtime
  SurfaceTool error that spammed stderr and was invisible in the Release build.

**Reversibility:** high for all three. Water: remove one shader file, a few blocks in
TerrainStreamer, and one constant. Woodland: remove GreenTree from ProceduralProps and
PropScatter. Roads: remove Roads.cs and one line in Main.

## D-070 — Cockpit head-look with padlock

*Renumbered from D-041, which was already in use above.* · 2026-09-19

**Decision.** The cockpit camera now supports head rotation. Three input methods:

1. **Middle-mouse drag** — relative motion maps to yaw/pitch, like FPS mouse look.
2. **Hat switch / D-pad** — digital 4-axis look at 2.5 rad/s (HOTAS POV hat or gamepad).
3. **Numpad 4/6/8/2** — keyboard equivalent of the hat switch. Numpad 5 or Home centres.

Release all inputs and the view springs back to forward (exponential decay, τ ≈ 4/s).
`L` padlocks onto the nearest detected threat emitter, or failing that the nearest known
site within 5 km. `L` again releases. Manual look breaks padlock.

Limits: ±150° yaw (the Huey has big side windows and open doors), -40° to +60° pitch
(down at instruments, up through overhead glass). These match the real aircraft's
outward visibility.

**Why.** The cockpit has built instruments (2×3 bezel array on the panel, control sticks,
collective levers, pedals) that the fixed forward view cannot see. D-034 fixed the
lighting so the instruments are correct; this decision makes them visible. Padlock is
the standard combat flight sim solution for tracking a threat while still flying —
relevant here because the RWR tells you something is painting you and you want to see
where it is.

**Key choices.**

- *Middle mouse, not right mouse.* Right mouse is Rotor Time in both modes. Middle mouse
  is unbound, natural for "hold and drag", and matches DCS/IL-2.
- *Spring return, not sticky.* A pilot looks somewhere, reads the information, then
  looks back to fly. Holding the look is the exceptional case (padlock handles it). A
  sticky head that stays wherever you left it would force the player to centre manually
  after every glance, which is a task the spring does better.
- *Head rotation only, not head translation.* The pilot's eye stays at `CockpitOffset`
  and only the gaze direction changes. Translation (leaning to see around the post) is
  a second-order feature that would need collision checks against the cockpit geometry.
- *HUD stays on screen.* The 2D flight instruments are a game HUD, not a literal panel.
  They stay visible regardless of head direction. The 3D panel instruments are the ones
  that require looking down.

**Reversibility:** high. Head-look state is ~40 lines in ChaseCamera, input routing is
~30 lines in Main, padlock is ~40 lines in Main. No sim/ changes, no save/load changes,
no new files. Remove the head-look fields and UpdateCockpit reverts to one line.

## D-071 — Building variety: five archetypes

*Renumbered from D-042, which was already in use above.* · 2026-09-19

**Decision.** Replace the single box-with-pitched-roof building with five residential
archetypes — simple gable, L-plan, lean-to addition, flat-roof parapet, and porch —
selected per-building from the site RNG. Add window depth (dark recess behind glass),
door openings, chimneys, and industrial details (ridge vents, loading docks).

**Why.** STATUS said "Settlements are boxes with pitched roofs. Silhouette variety and some
interior suggestion would do more than texture work." From 500 m the different rooflines
now read as different kinds of building (residential vs commercial, original vs extended),
and from 150 m the dark window recesses and door openings suggest interiors without
rendering any. The L-plan and lean-to break the rectangular footprint that made generated
towns look generated.

**Archetype probabilities (large residential, w≥7, d≥6):**

| Archetype | Probability | What makes it recognisable |
|---|---|---|
| Simple gable | 25% | Classic pitched roof, the baseline |
| L-plan | 25% | Perpendicular wing with lower roof breaks the rectangle |
| Lean-to | 20% | Lower shed-roofed addition in a contrasting material |
| Flat parapet | 15% | Concrete block with raised rim, reads as commercial |
| Porch | 15% | Gable with front overhang on posts |

Small buildings (w<7 or d<6, e.g. relay shacks) get simple gable or flat parapet only.
All standing buildings get a door opening and window band. 35% get a chimney. Sheds get
ridge vents (40%) and loading docks (30%, if w≥14). Collapsed buildings can now have a
partial wall still standing.

**What did not change.** The streaming system, collision (all new detail elements fall
below the 2 m³ threshold), site placement, determinism, and mesh types (BoxMesh,
PrismMesh — no new types, no textures, no UVs).

**Reversibility:** high. Building() is one function; reverting the commit restores the
original single-archetype generator. No sim/ changes, no save/load changes.

## D-039 — The game starts in summer, not on the first of January

**Decision.** The clock starts at day 172, 09:30, rather than day 0.

**Why.** The solar model takes the clock as a real date, so day 0 is midwinter. At 42° north
the sun never got above about 15°, and **every frame the game had ever rendered was in
raking low light**. It read as permanent dusk and made every screenshot a silhouette — which
is also why the airframe and prop work looked so dark when I first checked it. Day 172 puts
the morning sun at 54° and leaves the long evenings somewhere to fall from.

## D-069 — Position hold, because speed hold never arrives

*Renumbered from D-040, which was already in use above.*

**Decision.** `AutopilotDemand.GroundTarget` holds a point on the ground, converting
position error into a closing speed. The core loop test uses it for both approach and
descent.

**Why.** Commanding zero ground speed **freezes the position error wherever it happens to
be**. An approach that ends ninety-eight metres short stays ninety-eight metres short
indefinitely — hovering perfectly, having arrived nowhere.

Worse in wind, and that is how it surfaced: commanding a speed along a heading toward the
site does not converge at all, because the nose points at the pad while the ground track
crabs off to one side. The core loop test began failing the moment the world had weather in
it, shutting down 98 m from a site it thought it was at.

Verified separately: a 216 m run to a point in a 17 kt wind settles to 0.1 m and stays.
The loop test now touches down on a **0.0° slope** — the graded pad — instead of 7.3°,
because it is finally landing at the site rather than on the hillside next to it.

## D-041 — The envelope is checked against the real aircraft, and autorotation falls short

**Decision.** `tools/simlab/EnvelopeTests.cs` measures level-flight performance and
autorotation and compares both against published UH-1H figures.

**Why this is possible.** The Workhorse is a UH-1H in all but name — 7.32 m rotor, 324 rpm,
two blades, 1.30 m tail rotor, a 1400 shp turboshaft. Those are the real numbers, so the
real aircraft's performance is a yardstick rather than a matter of taste. Nothing was asking
whether the *whole aircraft* flew like the machine it is modelled on; the existing scenarios
check individual mechanisms.

**Level flight comes out well.** Minimum power 440 kW at 60 kt (reference: 60–70 kt), a
textbook power curve, level flight held past 130 kt, and **505 km of still-air range against
a published 510 km**. Best climb reads 2100 fpm against about 1600 — optimistic, but the
right order.

**Autorotation does not.** Best glide 1.99:1 at 50 kt descending 3191 fpm, against roughly
4:1 and 1700 fpm. Twice as steep. The measurement is sound: rotor holds 100% Nr at every
speed, figures are monotonic, averaged over five seconds of settled descent.

Two clues to where it lives. The glide ratio is nearly **flat** from 40 to 90 kt where a
real one peaks near best-glide speed — that is the signature of a descent angle set by
something roughly proportional to speed, rather than by the balance of induced and profile
power. And the energy books cannot be closed without power instrumentation inside
`ComputeWrench`, which does not exist.

**Not fixed, deliberately.** The repair is in the rotor's inflow in the windmill-brake
state, which is the part of the model everything else rests on — and everything else
currently matches the real aircraft closely. The test asserts against **regression** at the
measured level and prints the shortfall every run, so the gap stays visible instead of
quietly becoming the standard.

## D-042 — Measure a pinned condition, not a departing one

Recorded because this is now the **third** time it has bitten.

The envelope test first trimmed the aircraft and then let it fly free for a second before
reading power. The airframe is unstable, so it began departing within a few tenths of a
second and the governor chased it — producing a fuel flow that jumped from 202 to 304 kg/h
between 110 and 120 kt. That is a discontinuity in something the source computes as a
constant times power, which is the giveaway that the reading was wrong rather than the
model. Pinning the state while the engine and rotor settle fixed it.

The same mistake, in three costumes: control-derivative tests that measured departure
instead of response; audio tests that held blade loading fixed while varying torque, a
condition no aircraft can be in; and this. **Decide what condition you are measuring, hold
it, and only then read.**

## D-043 — Power is itemised, and the autorotation gap is localised

**Decision.** `FlightTelemetry` now breaks power into main rotor, tail rotor, drivetrain and
parasite, each averaged over a rotor revolution.

**Why.** A single `PowerRequired` cannot tell you whether a glide is steep because the rotor
is inefficient, because the tail is dragging, or because the fuselage is — and D-041 could
not close the energy books without it.

**The averaging is not optional.** A two-bladed rotor puts a violent 2/rev into shaft
torque: instantaneous main-rotor power in a *steady* autorotation swung between −347 and
+464 kW depending purely on blade azimuth. That looks like a diagnosis and is actually a
phase reading. The tip-path-plane telemetry had to learn the same lesson; this is the same
aliasing in a different gauge, and the same number drives the pilot's torque display.

**What it localised.** At 60 kt the descent supplies 714 kW where powered level flight at the
same speed needs 440 — the rotor dissipates ~270 kW more in the descent. Ruled out along the
way:

* **Not vortex ring.** Severity reads exactly 0.00 at every glide speed; the window is
  correctly closed by forward speed.
* **Not the inflow.** λ ≈ 0.009 at 60 kt, which is what momentum theory gives for
  C_T/(2μ) at μ = 0.125. The inflow model is behaving.
* **Not overloading.** C_T/σ sits near 0.070, normal.

What is left is **profile power**, which would have to roughly triple between powered flight
and the descent, with 9–15% of blade elements past stall. The next step is to split induced
from profile inside `MainRotor` and look at the post-stall drag rise — that is a change
inside the blade-element loop and wants its own session.

## D-044 — The autorotation gap is in the rotor's drag in the windmill-brake state

Completing D-041 and D-043. `RotorOutput` now splits shaft power exactly into **induced**
(the lift vector tilted by the inflow angle) and **profile** (section drag), which is a clean
division of the same in-plane force the blade-element loop already computes.

At 60 kt, the two conditions side by side:

| | induced | profile | parasite |
|---|---|---|---|
| powered level flight | +193 kW | +211 kW | 42 kW |
| autorotation | **−337 kW** | +252 kW | 146 kW |

That **clears profile drag**, which was the leading suspect after D-043: it rises only 20%
between the two, exactly as it should at the same rotor speed. Parasite triples, but only
because the descent adds a large vertical airspeed through the fuselage's 10 m² plan
area — and that force *retards* the descent, so it is a consequence, not a cause.

Induced power is correctly **negative**: the rotor is extracting from the upflow, which is
what autorotation is. The magnitude is the problem. Running the numbers the other way: for
the real aircraft's 1700 fpm, total dissipation must be about 328 kW, and our profile (211–252)
plus tail (50) plus drivetrain (11) plus a lower parasite already accounts for roughly that.
So the model would sit near the right descent rate **if the disc were not also acting as a
large drag device**.

**Conclusion (superseded by D-045 — see below, this was too confident):** the gap looked
like the rotor's retarding force in the windmill-brake state. The energy accounting above is
real, but it does not on its own identify a cause, and the follow-up measurement refuted the
first hypothesis it suggested.

Still not fixed, and still deliberately: it is one branch of the inflow model, but that
branch is load-bearing for hover, climb and vortex ring as well, and the rest of the envelope
currently matches the real aircraft to within a few per cent. It now has a precise address.

## D-045 — The autorotation equilibrium, measured properly, and one hypothesis refuted

**The right rig.** In a steady engine-off descent the net shaft torque is zero *by
definition* — the driving part of the disc exactly balances the dragging part — so the
equilibrium can be found directly by pinning the aircraft, sweeping descent rate, and
looking for where shaft power crosses zero **with Nr at 100%**. That is `simlab
autobalance`, and it replaces waiting for a controller to hunt its way there.

It reframes the problem. The model does not descend too fast because it is draggy; it
descends too fast because **the rotor cannot be sustained at 100% Nr any slower**:

| rate of descent | Nr |
|---|---|
| 1181 fpm | 67% |
| 1772 fpm | 69% |
| 2953 fpm | 84% |
| 3740 fpm | 97% |
| 4528 fpm | 110% |

At 1772 fpm — where the real aircraft sits — the rotor is turning at 69%. Forward speed is
not buying what it should: at μ = 0.125 the disc has plenty of mass flow through it, and the
model behaves almost as though it were in *vertical* autorotation, where 3000-plus fpm would
be about right.

**Hypothesis tried and refuted.** The induced inflow is uniform across the radius — the
Drees gradient in `MainRotor` is purely azimuthal, `rBar` only ever multiplying the fore-aft
and lateral terms. Uniform inflow is the classical simplification known to starve the
inboard driving region, so it was the obvious suspect. `RotorConfig.RadialInflow` adds a
triangular distribution, and `simlab radialsweep` measures what it buys:

| k | Nr = 100% at | hover kW | 60 kt kW |
|---|---|---|---|
| 0.0 | 3879 fpm | 815 | 440 |
| 0.9 | 3843 fpm | 721 | 432 |

**A 1% improvement in the thing it was meant to fix, and an 11% drop in hover power, which
would be a regression.** So it is not the cause. The knob stays, defaulted to 0 so nothing
changes, because the negative result is worth more written down than rediscovered — and the
sweep rig makes the next attempt cheap.

Left open with better tools than it had, and the leading question sharpened: *why does
forward speed not reduce the descent rate needed to sustain rotor RPM?*

## D-046 — An assist ladder, and the limits of measuring it

**Decision.** `Stability.Set(AssistLevel)` replaces the single on/off switch with Off /
Light / Standard / Full.

**Why.** The bare airframe leaves trim in about six seconds, which is a specialist aircraft;
full augmentation holds indefinitely, which is a different game. Everything between is where
most people want to be, and every part was already built — only the presets were missing.
Measured hands-off: Off 6.5 s, Light 6.6 s, Standard 22.3 s, Full never departs (22° worst
bank in forty seconds).

**Tuning by measurement, not by feel.** The first attempt at the presets produced a *Light*
rung worse than no augmentation at all (4.7 s vs 6.5) and a *Full* rung worse than Standard
(14.6 s vs 22.3). Both are D-021's saturation effect: at low authority a high gain turns the
damper bang-bang, and past about 0.30 authority the rate gains hit the limit the actuator lag
will carry. Full therefore buys **headroom, not gain** — its rate gains are identical to
Standard's, and pushing them to 1.05 dropped it to 2.6 s.

**What could not be measured, and was not faked.** Three metrics were tried for Light:

* *Hands-off survival* — about levelling, which Light does not have by design.
* *Time for a disturbance to settle* — never arrives; without levelling the aircraft keeps
  rolling, so every level returned the same capped number.
* *Peak roll rate for a fixed input* — **rises** with assist, because an augmented aircraft
  answers a held stick more crisply. Better handling, worse number.

All three are real properties and none is what Light is for. What it buys is how the machine
feels over seconds of continuous correction, which is a judgement made at a stick. The
column is printed but not asserted on, and the question is on the test machine's brief
instead. Inventing a scalar that flattered the design would have been worse than naming the
limit.

## D-047 — Storm weather: lightning, thunder, windscreen rain · 2026-09-19

**Decision.** When the weather model produces `SkyCondition.Storm` (badness ≥ 0.86), five
visual/audio layers stack on top of the existing rain:

1. **Sky darkening.** Fog colour, ambient, sun energy, sky energy, tone-map exposure and
   saturation are all pulled down proportionally to `StormIntensity`. The sky shifts toward
   a bruised yellow-green — the colour a real cumulonimbus casts onto the landscape.
2. **Lightning.** `StormEffects` runs a real-time timer (wall-clock, not game-clock, because
   the game runs at 30×). Every 1.5–12 s it fires a flash by spiking
   `AdjustmentBrightness`, then decays over ~0.4 s.
3. **Thunder.** `WeatherSynth.TriggerThunder` adds a deep rumble (three cascaded low-pass
   filters on noise) that decays over ~2 s, mixed into the existing weather audio bus.
4. **Windscreen rain.** A canvas-item shader draws procedural streaks and splash drops on
   the windscreen, visible only in cockpit mode. Intensity tracks precipitation.
5. **Rain intensification.** `WeatherEffects` ups particle count by 50% and increases
   opacity during storms.

**Why.** The weather model already distinguished Storm from Rain in the simulation, but
nothing visible happened. D-026 defined the threshold; this decision fills the gap above
it. Lightning is not deterministic (see below) because at 30× game time a 0.15 s flash
would be a single frame. Everything else is.

**Lightning timing is wall-clock, not game-clock.** The weather model's determinism
guarantee (same seed → same weather at the same clock time) covers conditions — "is it a
storm?" — not individual bolts. Lightning is a transient visual and audio event, and the
exact frame it fires does not affect gameplay. The screenshot system uses `ForceFlash()` to
guarantee a flash is visible in stills.

**The fog blowout bug.** The first four renders were uniformly white. At 3 km visibility
the fog fills the entire viewport, and the base fog colour (0.65 linear ≈ 0.83 sRGB) plus
1.5× ambient renders as flat white. The fix is to darken fog, ambient, and exposure
proportionally to `StormIntensity`. The Debug/Release build mismatch (Godot loads Debug by
default, `dotnet build -c Release` does not update it) cost three additional iterations.

**Reversibility:** high. All storm effects are additive layers in `StormEffects.cs` and
`SceneMood.cs`; removing them restores the rain-equals-storm behaviour.

## D-048 — Benchmark pass #2: mission structure is the critical gap · 2026-09-19

**Decision.** Full write-up in `docs/wiki/benchmarks/benchmark-pass-2.md`. Compared
against eleven games across nine dimensions: audio, graphics/world feel, progression,
story/missions, world size, combat, save/load, dialogue, flight model.

**Key findings:**
1. **Mission structure is the critical gap (2/10).** Every comparator that shipped as a
   narrative game had directed purpose by this stage. The infrastructure is ready (sites,
   NPCs, dialogue, save/load, map), but no system gives the player a reason to fly to a
   specific place.
2. **Audio synthesis is ahead of the field (8/10).** No shipped comparator synthesises
   vehicle audio from physics state. This is the project's most defensible differentiator,
   pending Fred's ear test.
3. **Progression has closed the gap from D-005a (5/10 → 7/10).** The kneeboard, fog of
   war, refit system and threat intel together provide the cadence and screen that were
   missing.
4. **Graphics are competitive for scope (6/10).** Weather, building variety and airframe
   wear are at or above the indie standard. Chasing photorealism is the wrong trade.

**Action: build mission structure next.** The contract-board pattern (Elite Dangerous,
MSFS 2024, Far Cry 2) fits ROTORWASH's existing systems. Minimum viable version:
- Contract board at settlements (generated from site data and world state)
- Breadcrumb main-search thread (10-15 authored beats, Subnautica's radio as the model)
- Journal page on the kneeboard

**Why not authored quests (New Vegas, Skyrim)?** A solo developer cannot produce 130
branching quests. The contract-board model generates per-sortie purpose from the systems
already built.

**Why not pure sandbox (Kenshi)?** The vision document describes a search for a specific
person. A sandbox without a search is a tech demo with a good flight model. The search
is the story, and even a minimal version (a name, a frequency, a bearing, a clue at each
stop) is enough to carry 20 hours.

**Reversibility:** high. This decision prescribes a pattern, not an implementation. The
contract board is data over existing systems; the search breadcrumbs are a short authored
sequence. Both can be changed without touching the flight model, world, or combat.

## D-072 — Autorotation: three hypotheses refuted, and the radial rig that did it

*Renumbered from D-047, which was already in use above.*

`MainRotor.RadialTorque` reports shaft torque banded into tenths of the radius — negative
drives the rotor, positive drags it. With rotor speed **pinned at 100%** (letting it float
confounds everything: at 1772 fpm it had already decayed to 67%, so those torques were
measured at half the dynamic pressure of the others), the picture at 60 kt is:

| descent | .25 | .35 | .45 | .55 | .65 | .75 | .85 | .95 | net |
|---|---|---|---|---|---|---|---|---|---|
| 1772 fpm | −0.3 | −0.2 | −0.2 | −0.1 | +0.2 | +1.8 | +2.2 | +3.9 | **+7.2** |
| 3740 fpm | −0.9 | −1.1 | −1.2 | −1.3 | −1.1 | −0.6 | +1.3 | +3.4 | **−1.5** |

The shape is right — an inboard driving region carrying an outboard dragging one — but **the
outboard three bands drag about 8 kN·m almost regardless of descent rate**, roughly twice
what blade-element theory predicts for the outer 30% (3.9 kN·m). The driving region never
catches up, so the rotor can only be sustained by descending harder.

Three candidates tested and cleared:

* **Tip loss charging for lift it does not make.** Real inconsistency — `cl` was reduced by
  tip loss *after* `cd` had been computed from the unreduced `cl`, so the tip paid induced
  drag on lift it no longer produced. Fixed anyway, because it is simply wrong. Changed the
  measured numbers **not at all**: tip loss only reaches the outer 4% here.
* **Compressibility.** Tip Mach measures 0.73 against a 0.74 divergence threshold. The drag
  rise is not active.
* **Section lift-induced drag.** `Cd = Cd0 + K·Cl²` with K = 0.0216 adds about as much drag
  as Cd0 itself, and blade-element theory already carries rotor induced drag in the tilted
  lift vector — so double-counting was plausible. Sweeping K from 0.0216 to 0.0060 moved the
  equilibrium 3879 → 3788 fpm (**2%**) while dropping hover power 9%. Not the cause.

Also cleared earlier: vortex ring (severity 0.00), the inflow solution (λ matches momentum
theory), blade loading, and radial inflow distribution (D-045).

Four suspects down. What remains unexplained is the *magnitude* of outboard drag torque at
moderate lift with no stall, no compressibility and no tip-loss effect — which now points at
the blade-element integration itself rather than at any coefficient. The rig to attack it
(`simlab driving`) exists and is cheap to run.

## D-049 — Chaff and the warning receiver are a pair, and that was not designed in

D-010 makes air defence the gate on the world: better-guarded country opens up as the player
finds countermeasures. That is a load-bearing claim — it is what gives the map an order and
what makes a salvaged dispenser worth a long flight — and **nothing was testing it**.
`--threatreport` measures how much cover the terrain gives, which answers where you can
hide, not what happens when you cannot.

`simlab gating` flies a fixed synthetic gauntlet of ten emitters and varies only the fit.
Synthetic rather than sampled from the real map on purpose: a known corridor isolates the
countermeasures, where the real world mixes in terrain, route choice and where sites landed,
so a change in any of those would read as a change in the fit.

**A radar warning receiver did nothing whatsoever.** Flares plus RWR took exactly the same
damage as flares alone, to the hundredth — the flag was not referenced anywhere in the
mechanics. The fix is not to make the box reduce damage: an RWR does not make a missile
miss, it tells you one is coming. So `ThreatField.Perceivable` and `AnyLockedKnown` now
model what the crew can actually *know*: a gun announces itself, a radar lock is silent
without a receiver, and a MANPADS is silent either way, which is the entire reason it is the
frightening one.

With perception modelled, and a policy that dispenses the right countermeasure for the
threat rather than everything at everything:

| fit | damage through the gauntlet |
|---|---|
| nothing | 19.08 |
| flares | 13.63 |
| flares + RWR | 13.63 |
| chaff + flares, **no** RWR | 13.63 |
| chaff + flares + RWR | **8.59** |
| everything | 5.57 |

**Neither chaff nor the receiver is worth anything alone; together they are worth 37%.** That
dependency was not designed — it falls out of modelling perception honestly, and it is a far
better progression beat than two separate upgrades would have been. Finding one without the
other should feel like half a key.

**The altitude half of the gate already works.** With terrain cover on, an unprotected
aircraft takes 0.77 damage at 50 m against 18.76 at 400 m. Flying low is a genuine
alternative to carrying equipment, which is the trade the world layout rests on.

**One honest limit:** the cover proxy saturates at 0.95, so 400 m and 900 m read identically
here. Separating them needs the real terrain, which is what `--threatreport` is for.

**A mistake worth recording.** The first policy fired everything at every known lock, and
flares-plus-RWR came out *worse* than flares alone (18.80 against 14.24): the receiver told
the crew about radar threats, they spent flares that do nothing to a radar missile, and had
none left for the one that mattered. Realistic as a pilot error, useless as a measurement of
the fit — the same lesson as D-042, one layer up.

## D-050 — CORRECTION: the search target cannot be another helicopter pilot

**Status: must be fixed before `sim/src/SearchThread.cs` is relied on.** Flagged while the
file was still uncommitted and in flight, so it is recorded here rather than edited from
under whoever is writing it.

`SearchThread.cs` makes the object of the search **"Kara Morrow, a pilot who vanished flying
east"**, describes "the aircraft description", and ends on *"Two pilots, two aircraft. Get
everything out of the valley."*

That contradicts **D-008**, which is marked **LOCKED-IN BY FRED** and reads: *"No other
helicopter pilots exist... air threats are ground-based AA, drones, tethered balloons and
weather — never rival helicopters. Reversibility: low, by design. It is a pillar."*

It is not a cosmetic clash. **D-010 rests on it**: air defence works as the gate on the
world precisely because *"everyone defends against the one aircraft that exists"*. A second
flyable helicopter in the fiction undercuts the reason the whole threat layout is shaped the
way it is, and the final beat — two aircraft lifting a load together — is the premise
inverted rather than bent.

**The correction is content, not structure.** The beat machinery, the knowledge ids, the
staging and the save format are all sound and should be kept exactly as they are. What has
to change is who the target is: someone who is *not* a pilot and has *no* aircraft.
`docs/wiki/story.md` proposes Sera Wray, the flight engineer who kept Hugh flying before the
collapse — which fits the beats almost unchanged (a name, a frequency, a route, cargo, a
destination) and gives the search a reason to end in something the player cannot simply
scavenge.

**A note on process, since this is the second time.** A locked decision was contradicted by
work that never read it. Anything proposing *who or what exists in the world* has to be
checked against D-008 and D-012 first; those two are premise, not preference. The cost here
was small only because it was caught before the file was committed.

## D-073 — Mission structure: contract board and search thread (the 2/10 gap)

*Renumbered from D-051, which was already in use above.*

Built to close the critical gap D-048 identified. Every comparator that shipped as a
narrative game was structurally ahead of ROTORWASH at mission structure (2/10). The fix
has three pieces:

**1. Contract board at settlements.** Generated per-settlement from site data and world
state, following the Elite Dangerous / MSFS 2024 / Far Cry 2 model. Each settlement
offers 2-3 contracts drawn from its neighbours: deliveries (bring stock to a site),
scouts (visit somewhere unvisited), recovery (search a wreck or depot), and relays
(carry a message between settlements). Contracts reference real sites, use real stock
types, and pay in things the game already tracks. Generation is deterministic by site
seed and board cycle; boards refresh every 2 game-hours.

**2. Main-search breadcrumbs.** Twelve authored beats telling the story of finding Sera
Wray (flight engineer, per D-008 and D-050 — not a pilot). Three acts following the
ferry route of SIERRA-FOUR-THREE across all eight regions. Gated on world state (visited
count, knowledge), with counter-based gates as a floor; story.md specifies place-based
triggers that require site-role resolution (not yet built). Each beat journals a clue,
grants knowledge, and updates the kneeboard hint.

**3. JOBS kneeboard page.** Fifth page showing the search thread (current hint, last
clue), active contracts (kind, title, target, reward), and completed count. Follows the
same drawing pattern as the other four pages.

**What it does not do (and what story.md specifies for later):**
- Site-role resolution (mapping story roles like "the Fenmoor airfield" to generated IDs)
- Named NPCs at specific settlements (Doss, Nell, Osie, Ferren, Juno, Wray)
- Radio broadcast system (the 06:40 weather sequence)
- Artefact display (the manifest, the roster, the cairn list)
- Rotor-hours ceiling mechanic (the clock on the search)
- The final sortie with Wray in the right seat

Eight simlab tests: generation, completion, payout, delivery logic, round-trip,
search gating, determinism, and Progress integration. All pass.

**Reversibility:** high. Contracts are data over existing systems; the search beats are a
short authored sequence. Both can be changed without touching the flight model, world, or
combat. The save format adds three fields (Contracts, ContractsCompleted, SearchStage)
which are forward-compatible.

## D-052 — Interior window glow: emissive shader, not real lights · 2026-09-19

**Decision.** Standing residential buildings glow warm amber through their windows at
night. Implemented as a thin emissive plane between the dark recess and the glass in
every `WindowBand`, driven by a shader (`window_glow.gdshader`) whose `daylight`
uniform is pushed once per frame from `SceneMoodDriver`.

**Why emissive geometry rather than OmniLight3D nodes.** A settlement has 7–14 buildings
with 3–5 windows each — 50+ potential light sources. Forward+ clusters handle real
lights well, but not fifty per settlement with multiple settlements loaded. An emissive
surface costs nothing beyond the draw call it is already part of, and at approach altitude
(70–150 m) a warm rectangle behind glass reads as "someone's home" without needing to
cast light onto surrounding geometry. The bloom pass (`GlowHdrThreshold = 1.05`) catches
the emission and softens it, which is the only spill the effect needs.

**Why a shader, not a second StandardMaterial3D.** The glow has to track the day/night
cycle: invisible at noon, full at midnight, fading through twilight. A `StandardMaterial3D`
would need its `Emission` updated every frame on a shared material, which changes the
render hash and forces a pipeline rebind. A shader reads the uniform without touching
the material state. It also provides per-window variation (some rooms dark, slight colour
and flicker differences) from a world-position hash, so no two windows glow identically.

**Per-window variation.** ~30% of windows stay dark (empty rooms, storage). The rest vary
in warmth and have a slow, desynchronised flicker that reads as firelight rather than
electric light. All variation is derived from the window's world position, so it is
stable across frames and across save/load.

**Verified.** Shots `29_night_settlement` and `30_night_settlement_close` show warm
rectangles visible from 50 m and 25 m at night. Daytime shots (`10_settlement`,
`26_settlement_close`) show no glow artefacts. No performance regression: the settlement
shot holds 33–36 fps on the GTX 1650 Ti, identical to the pre-glow baseline.

**Reversibility:** high. Delete the glow plane from `WindowBand`, the `Glow` field from
`Palette`, and the one-line update in `SceneMoodDriver`. The shader file is inert if
unreferenced.

## D-051 — The coning inflow term: real physics, shipped OFF

A parallel agent found a genuine omission in the blade-element loop. `U_P` was resolved on
the **shaft** axis rather than on the **flapped blade's normal**, silently dropping the
classical spanwise contribution — the flow through a coned disc, from below at the front and
from above at the back. It costs almost nothing in powered flight, which is exactly why it
survived, and in a descent it is part of the upflow the driving region lives on.

It works. Best glide goes **1.98:1 → 2.54:1**, every speed in the sweep improves, and hover
power is unchanged at 814 kW.

**It also reverses right cyclic, so it ships defaulted to 0.**

| `ConingInflow` | roll for right cyclic (sim / Godot) | best glide |
|---|---|---|
| 0.0 | **+49.8 / +49.8 °/s** — agreeing exactly | 1.98:1 |
| 1.0 | −31.7 / +11.2 °/s | 2.54:1 |
| 1.0, sign flipped | −64.1 / +6.8 °/s | 1.94:1 |

The mechanism is not mysterious: the spanwise term puts a 1/rev variation into `U_P`, which
through the usual 90° gyroscopic lag becomes **lateral flapping**. It biases roll by
construction. What is unexplained is the *size* of the bias — large enough to reverse the
control — and flipping its sign makes both numbers worse, so it is not a simple sign error.

**Why this was not caught by the agent that wrote it:** its brief forbade running Godot,
because this machine cannot render. `simlab all` passed throughout. The defect only appears
through the bridge, where controls are rate-limited and the rigid body is integrated by
Godot — and the tell is that the sim and the body **disagree**, which they never otherwise
do. That is a gap in how the work was briefed, not in the work: the headless suite does not
cover the bridge, and `--selftest` is headless too and should have been in the brief.

Kept, flagged and measured rather than deleted. The physics is real and D-041 is still open.
Flying the aircraft correctly wins until the lateral bias is understood.

## D-056 — Compressibility is NOT cleared after all

*(Renumbered from a duplicate D-052. Two processes allocated the same number within a few
minutes of each other, which is a hazard of parallel work on an append-only log; if it keeps
happening the numbers should come from the commit rather than from whoever is writing.)*

D-047 ruled compressibility out of the autorotation investigation because tip Mach measured
0.73 against a 0.74 drag-divergence threshold. That measurement was wrong.
`TipMachAdvancing` reported the maximum over the ~8° of azimuth a single physics step
covers, not over a revolution. Corrected to a per-revolution maximum, the advancing tip at
the same condition is **0.81** — comfortably *above* the threshold, so the drag rise is
active and was never eliminated.

A reminder that a refutation is only as good as the instrument behind it, and that this is
the third time aliasing on a two-bladed rotor has produced a confident wrong answer
(tip-path plane, then shaft power in D-043, now tip Mach).

## D-057 — `autoglide` was never measuring the rotor

The headline autorotation number was measured while the aircraft **slid sideways at 27–47
m/s**, dragging 13.5 m² of side area through the air — at "60 kt" its world-frame forward
velocity was actually negative. The autopilot flying the test has no lateral channel.

`EnvelopeTests.AutorotationTrim` replaces it for diagnosis: six unknowns against six
residuals at a pinned descending condition, engine failed, Nr pinned at 100%, bisected until
net shaft power crosses zero. Straight flight, no sideslip, no controller. Pinning lateral
velocity alone moves 60 kt from 3864 to 2561 fpm.

Which also means the 4:1 reference may be unreachable for *this* airframe: at honest trim it
gives 3.19:1 at 2226 fpm, and the published figure assumes a heavier machine — a lighter
aircraft buys a faster descent for the same dissipation.

## D-054 — Autorotation: three problems, not one

The "autorotation glides half as far as it should" item turned out to be three combined issues:

**1. Measurement artefact (fixed).** The autoglide rig had no `LateralSpeed = 0` in the
autopilot demand, so the aircraft drifted sideways dragging 13.5 m² of fuselage side area.
This was already identified in D-053 and the trim rig built to diagnose around it. Fix:
added `LateralSpeed = 0` to the demand, engaging the autopilot's existing lateral PID
(DriftLoop P=0.034, I=0.007 → RollAttitude → RollRate). Result: autoglide best moved from
1.98:1 to **2.66:1** at 80 kt — within 10% of the six-DOF trim's 2.91:1.

**2. Wrong reference (corrected).** The 4:1 figure is a rule of thumb that includes flare
distance. Steady-state at 60 kt and 1700 fpm descent = 3.57:1. Corrected the test reference
to **~3.6:1**.

**3. Real physics gap (~20%, not fixed).** The model reaches 2.7–2.9:1 against a real ~3.6:1.
Likely sources: profile power (9–12% of blade elements past stall, compressibility drag at
tip Mach 0.81 vs threshold 0.74) and the momentum-theory inflow model. The powered envelope
still matches the real aircraft (505 km range, textbook power curve, hover ceiling within 3%),
so this is a localised shortfall in the windmill-brake state. Guards updated to protect the
corrected numbers.

**ConingInflow: two attempts, both failed.** The coning-inflow term (resolving U_P on the
coned disc's normal rather than the shaft axis) is correct physics and helps autorotation in
theory. Two implementations were tried:

(a) **Full instantaneous β(ψ)**: feeds cyclic flapping back into U_P, creating a high-gain
loop whose result depends on which integrator runs it — the sim's semi-implicit Euler gives
+11.2 deg/s of roll for right cyclic while Godot's rigid-body solver gives −31.7. Not a
simple sign error.

(b) **Mean coning angle (β₀) only**: eliminates the feedback loop, but also drops the 0/rev
cross-term from (a₁ × forward speed) that was the main source of improvement. Measured:
trimmed autorotation **regresses** from 3.15:1 to 2.92:1 while hover power stays at 814 kW.
The code is in place (MainRotor computes coningAngle before the substep loop), defaulted off
via `RotorConfig.ConingInflow = 0`.

**Net outcome**: the "half as far" gap was mostly measurement + reference error. The honest
gap is ~20%, the numbers are guarded, and the item is moved from Next to Done. A further 20%
would require either a higher-fidelity inflow model (prescribed wake, free wake) or a stall /
compressibility model that distinguishes retreating-blade stall from the advancing-tip drag
rise — both beyond scope for a game rotor.

Reversibility: fully reversible. No existing behaviour changed (ConingInflow stays off,
powered envelope unchanged). The lateral fix is a test-rig improvement. Guards can be rolled
back to the old numbers by removing `LateralSpeed = 0`.

## D-055 — Instruments are items: the HUD grows with the aircraft · 2026-09-19

**Decision.** Two changes from D-005a that make module installation *visible* on the
interface, not just felt in the flight model:

1. **Instrument gating.** The left panel (airspeed, radar altitude, vertical speed, heading)
   and the control position display are hidden until the SAS (attitude hold) module is
   installed. Without it, a "NO FLIGHT DATA" placeholder appears where the left panel would
   be. The attitude indicator (horizon line) stays — it represents looking out the windshield,
   not a sensor — and the right panel (Nr, torque, fuel) stays because the engine gauges are
   hardwired. The RWR display already had its own conditional logic (D-005a).

2. **Numeric delta card.** When any module is installed or removed, a brief overlay shows the
   mass delta, fuel capacity delta (if any), drag delta (if any) and the resulting total
   aircraft mass. It auto-dismisses after 5 seconds. This is D-005a §2's "printed numeric
   delta on acquisition."

**Why.** D-005a §3 says "the HUD physically grows as boxes are installed, so the interface
itself is a progress bar." At game start with no modules, the HUD is sparse: attitude
reference, power gauges, warnings. Installing the SAS populates the entire left side of the
display with air data — a visible, meaningful change that makes the progression real rather
than abstract. The delta card makes the trade legible: +12 kg mass, so the hover is slightly
worse, but you can see your airspeed now.

**Why SAS specifically.** The attitude hold unit is a sensor package (rate gyros, air data
computer) that physically provides the measurements the left panel displays. A real UH-1
with failed flight instruments would still have the visual horizon and engine gauges, which
is exactly the bare HUD. Diegetically correct and mechanically meaningful.

**Reversibility:** high. Two conditionals in FlightHud._Draw and one overlay method. Removing
the feature is deleting the `hasSas` checks and the delta card code.

## D-058 — Tone: Office-style humour, and rock music · **[FRED'S DIRECTION]**

Two notes from Fred on audience, recorded because they steer content across dialogue, audio
and writing, and several agents work from these documents without seeing the conversation.

**"Humour like from The Office (US) lands."** Read specifically, not as "add jokes":

* **Deadpan.** The funny line is delivered flatly, by someone not trying to be funny, usually
  mid-chore.
* **Character-driven, not gag-driven.** It comes from a self-image not quite matching
  reality, a small vanity, an obsession nobody else shares. A trader with a system nobody
  asked about. Someone who has plainly rehearsed a speech.
* **Mundane detail played straight.** Inventory disputes, a grudge about a fence, firm views
  on stacking fuel drums — at the end of the world.
* **It must never undercut the weight.** The Office is funny *and* takes its people
  seriously; it is sad when it needs to be. That is the same register D-007 already asks for
  ("mature but not grim-dark"), and humour is what stops that tone curdling into misery.
* **Not** quips, banter, wackiness or winking at the player. A line that works as a one-liner
  out of context is the wrong kind of funny.

This makes the nine named NPCs *easier* to separate, because each gets a comic register as
well as a want.

**Rock music.** The game currently has no music at all. It goes in **diegetically**, as a
salvaged radio/cassette player in the cockpit, rather than as a score:

* It fits the world instead of sitting on top of it — a machine somebody kept working.
* It hooks into systems that already exist: tapes are salvage (D-005), the set can be damaged
  and repaired (`Component.Avionics`), and it can fail in weather or at altitude.
* It is the player's choice whether to have it on, which a score never is.
* And it dodges the thing that kills licensed music in games: the score does not have to
  match the drama, because it is not the score. It is a tape somebody left in.

**Reversibility:** high for the delivery (it is one node), low for the tone — humour is a
pillar-adjacent decision and changing it late would mean rewriting every line.

**Licensing:** per Fred's original brief, free assets are fine including copyleft and
attribution-required, but **every one must be tracked** in case the game is ever shared.
`docs/wiki/attribution.md` is the register.

## D-059 — The world is an archipelago · **[FRED'S IDEA]**

The map becomes several islands separated by open water, rather than a single continuous
land mass ending at an invisible envelope.

**The problem it solves.** The aircraft has roughly 500 km of still-air range and three hours
of endurance. The content envelope is 13 km across — it crosses the entire world in about
four minutes. Range is wildly disproportionate to the map, which is a version of a question
Fred raised early on ("if helicopter range is too big for map size should we spread POIs with
sparser fill in between?"). The two obvious answers are both bad: padding the map with sites
dilutes them, and growing the map costs streaming and content nobody has authored.

**Why water is the right answer, and the mechanism that matters.** Water makes distance
meaningful without making it long. Over land an engine failure is an autorotation into a
field and a walk home; over water it is a swim and the aircraft is gone. A 10 km crossing is
nothing in terms of range and is a real decision in terms of consequence. The lever is
**risk, not kilometres** — and that is a lever the flight model already supplies for free,
since `simlab autoglide` puts best glide near 2:1, so from 500 m the aircraft reaches about
1 km. A gap wider than it can glide is a committed crossing.

Three things fall out of it:

* **Empty legs stop needing content.** An over-water leg is allowed to be empty, because
  crossing it *is* the activity. That answers the density problem directly rather than
  papering over it.
* **Fuel becomes the boundary.** Not a wall, not a warning — the thing the whole game is
  already about (D-004). Fuel is salvaged in small amounts, so the player rarely has a full
  tank, and a crossing with 120 kg aboard is a genuine commitment.
* **The world edge is diegetic.** No invisible wall, no turn-back message. A coastline needs
  no explanation.

**Explicitly rejected: auto-turnaround at the map edge.** Fred floated it as an option.
Taking the controls away is the wrong instinct in a game about being a pilot, and it converts
a decision into a cutscene. The player may fly out to sea and may run out of fuel doing it.
What they get instead is **facts** (D-005a): fuel remaining against fuel required to return.
"40 min remaining, 45 min to the coast" tells a pilot everything and instructs them in
nothing.

**Also enabled:** water bucketing as a payload and mission type (Fred's request), which needs
open water to dip from — see the underslung-load work.

**Reversibility:** medium. The height function and the region layout both move, and site
placement follows terrain, so it has to be re-verified against `--worldreport` (shortfalls
currently zero, remote country 7%). Nothing above it depends on land being continuous.

---

### D-060 — Hostile site encounters

**Date:** 2026-09-19

**What:** Some salvage sites are guarded by scavengers who shoot at you when you dismount.
The combat system (D-016) existed but was only exercised in the combattest — nothing in the
regular game world was hostile. This bridges that gap.

**Rules:**

* **Tier 0 is safe.** The Basin is the tutorial; nobody shoots you there.
* **Tier 1+:** Wrecks, depots and airfields have a ~35–55% chance of being hostile
  (seed-based, deterministic, rising with tier).
* **Tier 2+:** Farmsteads join the hostile pool at ~30–35%.
* **Never hostile:** Settlements (where you trade), workshops (where you repair), fuel caches,
  relays, and overlooks.
* **NPC count:** 1–2 at small sites (wrecks, farmsteads), 2–4 at large ones (airfields).
* **Clearing is permanent.** Down all hostiles and the site is safe on every future visit.
  Cleared state persists through save/load.
* **HUD:** The site panel shows "HOSTILE" in danger-red or "CLEARED" in dim, next to the site
  kind label.
* **Map:** The kneeboard draws a red diamond outline around uncleared hostile sites (grey when
  cleared).

**Why this design:**

The game already has three layers of danger in the air (SAMs, guns, seekers) and none on the
ground. The flight loop gates the map by tier, but once you land the site is safe — which
means the decision to dismount costs nothing except time. Making some sites hostile means the
player weighs ammo and health before landing, Rotor Time has a purpose in normal play, and
the revolver's 18-round budget (exactly enough for one careful encounter, not two) creates
real resource tension rather than just being an interesting number.

**Why seed-based, not random:** The same world is hostile in the same places on every load,
which means a player can learn the map. A site that was safe yesterday is safe tomorrow.
This matches the threat emitter placement (also deterministic) and the module distribution.

**Why settlements are exempt:** The player needs somewhere safe to trade, repair, and take
contracts. A hostile settlement is a dead loop — you cannot get the payout for clearing it
because the payout comes from the settlement.

**Reversibility:** high. `Encounter.IsHostile` is a pure function in sim/ with no Godot
dependency; removing it restores the pre-D-060 behaviour with no other changes needed.
The `Cleared` field in `SiteRecord` is additive and ignored when the encounter system is
absent. Six simlab tests cover the rules.

---

## D-061 — Regional alert state

**Decision:** Being detected by threat emitters raises a region's readiness. Readiness
persists across sorties and feeds back into the threat system as two effects:
`DetectionScale` (detection range ×1.0 to ×1.25) and `ReactionScale` (launch delay ×1.0
to ×0.5). Six-hour game-time half-life.

**What it does:**

* `ThreatWorld._PhysicsProcess` calls `AlertState.Detected(regionId, realDelta)` for every
  emitter that has line-of-sight and confidence above 0.05. Real-time delta so a single
  crossing reads as ~0.30 ("someone saw you") rather than near-1.0 under the 12× game
  clock.
* `AlertState.Decay(gameDelta)` runs every frame with game-time delta, giving the six-hour
  half-life in game-hours — a night's rest (30 real minutes) roughly halves it.
* Launches call `Engaged(regionId)` for a one-time spike of 0.12.
* `ThreatField.Update` multiplies detection range by `DetectionScale` and launch delay by
  `ReactionScale`, both looked up via a cached emitter→region mapping built at placement.
* The kneeboard MAP header shows raised regions by name and phrase from
  `AlertState.Describe`.
* `SaveData.AlertLevels` persists the regional readiness through save/load; absence of the
  field in an old save is quiet, which is the correct default.

**Why this design:**

The threat system had no memory — the tenth flight through a valley cost exactly what the
first did. D-008 and D-010 say there is only one aircraft in the world, so being seen
should be information that persists. The saturating formula (rising additions are dampened
by `1 - level`) prevents unbounded stacking — twenty passes is only 1.6× worse than five,
not four times. Effects are bounded: detection never stretches beyond ×1.25 (enough to feel,
not enough to redraw routes), and reaction never falls below ×0.5 (terrain masking still
works, just needs more margin). The split time base (real-time detection, game-time decay)
was calibrated against the 12× clock so that a transit-length exposure reads as "noticed"
and staying away for one game-day clears most of it.

**Reversibility:** high. Setting `ThreatField.Alert` to null restores the pre-D-057
behaviour. `DetectionScale` returns 1.0 and `ReactionScale` returns 1.0 when alert is null.
The `AlertLevels` field in `SaveData` is optional and ignored when absent. Four existing
simlab tests verify the alert model; one new test (`save_alert`) verifies the round trip.

## D-062 — Contract depth: contracts that use the world · 2026-09-19

**Decision.** Connect the contract board to the alert and encounter systems so that
contracts are aware of the world state that now exists rather than sitting beside it.

**What changed:**

1. **Clear contracts.** New `ContractKind.Clear` — "clear the scavengers at [hostile
   site]". Generated when `Encounter.IsHostile` returns true for an uncleared nearby site.
   Completion is gated on `SiteRecord.Cleared`. Reward scales with distance, NPC count,
   and danger pay. Pays knowledge about the site, because clearing it reveals what was
   there. The kneeboard shows these in red with a CLEAR tag.

2. **Danger pay.** All contracts scale rewards by `1 + AlertState.Level(regionId)`,
   so a contract to a fully awake region pays up to double. This makes hot regions
   worth the risk rather than dead weight on the board. The multiplier is applied after
   the distance component, so a short flight into a hot region pays more than a short
   flight into a quiet one but less than a long flight into a quiet one.

3. **Alert-aware briefs.** Every contract brief can end with a sentence about the
   region's alert level: "somebody saw something" at low alert, "they are expecting
   company" at high. The suffix is omitted entirely below 0.15 readiness, so most
   early-game contracts read as they always did.

4. **Hostile-aware recovery.** Recovery contracts (search a wreck/depot) to hostile
   sites use a distinct set of brief templates that mention the guards and pay one
   extra part for the fight. The player knows before accepting that the site is guarded.

5. **Survey preference for quiet regions.** The survey generator sorts candidates by
   `distance + alertLevel × 4000`, preferring quiet regions. A region the player has
   stirred up is less likely to appear as a scout target, which naturally steers
   exploration toward untouched parts of the map.

**SiteStub extended.** `Tier` and `RegionId` added (defaulted to 0 for backward
compatibility). The game layer now passes both from the Godot `Site` record.
`SiteInteraction` receives `AlertState` from Main after `ThreatWorld` is constructed.

**Save/load.** No schema change. `ContractKind.Clear` is a new enum value serialised as
a string by `JsonStringEnumConverter`, so old saves without Clear contracts load fine and
new saves with them are human-readable.

**Tests.** Four new simlab tests: `contract_clear` (generation and completion),
`contract_danger_pay` (alert-scaled rewards), `contract_recover_hostile` (guarded briefs),
`contract_clear_roundtrip` (save/load). All twelve contract tests pass.

**Reversibility:** high. Clear contracts are one new enum value, one new branch in
`CheckCompletion`, and one new `TryAdd` method — deleting them leaves the four original
types untouched. Danger pay and alert briefs are gated on `alert is not null`, so passing
null restores the pre-D-062 behaviour exactly. The `SiteStub` defaults mean existing
test code that does not supply Tier/RegionId continues to compile and run.

---

## D-074 — The announcer talks about what you did, but only what the district heard · 2026-09-19 · **[FRED'S IDEA]**

**Decision:** The station keeps a feed of player deeds (`DjDeed`: kind, when, where, a
figure, and **how many people saw it**) and the announcer works them into his breaks. A deed
becomes sayable only when it passes three gates - somebody saw it, forty minutes have passed
so word could travel, and it is less than three days old - and the figure he gives is not the
figure that happened. He airs a given deed at most three times, and between three days and
three weeks it moves to a `DeedCallback` bank where he brings it up unprompted, having
nothing to add.

**Why:** Fred asked for "radio like Forza where it talks about things the player has done
too", after asking for a GTA-style announcer. The obvious implementation of that request is
a feed: the game hands the radio an event log and he reads it out. That version is worthless
within an hour, because the player learns he is listening to his own telemetry with an accent
on it, and every line after that is a HUD element that happens to be spoken.

Everything worth having is in the **gap between what the player did and what the district
heard**, so the gap is what got built:

  * **Somebody has to have seen it.** A long way round over empty country to a place with two
    people in it does not get talked about. That is the same honesty as
    `ThreatWorld.Perceivable` - a MANPADS that never fired is one the crew never knew about -
    and it makes being noticed an outcome the player can influence rather than a notification.
  * **Word takes forty minutes.** This is what stops the station being a HUD. If he mentions
    the drop while the player is still in the climb-out, the radio is visibly wired to the
    game. Forty minutes means he is always talking about the sortie before last, which is how
    a district actually sounds, and it rewards leaving the station on rather than listening
    for a cue.
  * **The number is wrong, and wrong the same way every time.** `RadioDj.AsTold` drifts the
    figure by up to ±35% at a single witness, falling off as the square root of the crowd,
    and rounds it the way people round. It is derived from the deed by splitmix64, not drawn
    from the RNG, because **a rumour is a fixed wrong story, not a fresh one**: an announcer
    who gives a different figure at each telling is not unreliable, he is broken. Measured:
    a figure of 100 comes back 18.0% out when one person saw it, 8.8% at four and 2.9% at
    forty - so how far off he is tells the player how alone they were out there.
  * **He never addresses the player.** Every line is hearsay about "the helicopter", read at
    the same weight as the market report. D-058's player-wink blocklist already forbids the
    alternative and covers these banks. Being talked about by somebody who is not talking
    *to* you is how you find out a world noticed you; Forza's second person would undo the
    premise that there is one aircraft and no one knows who flies it.

The bathos is the joke Fred asked for (D-058): eleven years of broadcasting and the biggest
thing that has ever happened in the district gets four sentences, one of which is about a
dispute over a hole in a reservoir wall.

**Cost:** 100 new lines across eight deed kinds and a callback bank (676 authored lines
total, 108 minutes of speech). `{PLACE}` and `{AMOUNT}` join the token set; like `{REGION}`
they **drop the whole line rather than fall back**, because the fallback for a place is
inventing one.

**Reversibility:** high. `DjWorld.Deeds` defaults to null, and a null feed means no deed is
ever offered and the announcer is exactly what he was. Nothing outside `RadioDj.cs` has to
supply deeds for the station to work. Covered by `dj_deeds`, which checks the three gates
one at a time, that he wears a story out, that a placeless deed never produces a sentence
with a hole in it, and that the distortion is both stable and a function of the crowd.

**Amended the same day — newsworthiness.** The first picker took the most recent sayable
deed, which is wrong because the hooks do not fire at equal rates: a rescue happens once in
a campaign, an unaccepted contract expires every time a board refreshes, and a low pass
happens whenever the player is in a hurry. `dj_newsworthy` put one rescue against forty
routine declines and low passes and the rescue was **never mentioned once**. `RadioDj.
DeedScore` now weights the kind (`Newsworthiness`, 3.0 for a rescue down to 0.4 for a
declined job), decays it by half across the three-day freshness window, and divides by
airings. The rescue now gets its three airings and he moves on. These are editorial weights
and they are the announcer's judgement, not the simulation's.

**Not done:** ~~nothing produces `DjDeed`s yet.~~ The hook is one call per completed contract,
crash, salvage run and low pass, with `Witnesses` taken from the population of the nearest
site - which is a number the world already knows and nothing currently reads. Logged in
`integration-debt.md` rather than guessed at here.

## D-075 — Degraded story roles resolved: verification, not a fix · 2026-09-19

**Decision:** Close the "three degraded story roles" item as verified-fixed. No code change
was required. The placement improvements already in the codebase (altitude ceiling raised to
320 m for settlements, three-pass desperation spacing relaxation) resolved the terrain
mismatches that were causing `DossHome`, `FerrenOffice`, and `ScaldMagazine` to fall back.

**Measured (seed 40404, `--worldreport`):**

| Role | Wanted | Got | Site |
|------|--------|-----|------|
| DossHome | 1st Settlement in Long Acre | Settlement | Low Furrow #16 |
| FerrenOffice | 1st Settlement in Sawtooth Works | Settlement | Rust Works #75 |
| ScaldMagazine | 1st Depot in The Scald furthest from centre | Depot | Stone Line #111 |

All 16 roles bind to their primary intent. Zero degraded, zero unbound, zero shortfalls.
Every region got everything the plan asked for. The fallback chains remain in place as
insurance against future terrain or placement changes.

**Why this matters:** the three degraded roles were the only outstanding StoryPlaces issue.
With them clean, every beat in the search thread has a place to happen, and that place is
the site the story actually wanted — not a structural substitute. The fallback mechanism
worked exactly as designed (it caught the problem and reported it loudly), and the placement
fixes upstream made the fallbacks unnecessary.

**Reversibility:** N/A — no code was changed. If a future seed or placement change re-introduces
shortfalls, the fallback chains and the worldreport will catch it immediately.

## D-076 — Wire the deed feed: the announcer talks about what you actually did

*2026-09-19*

**Decision.** Six of eight `DjDeed` hooks are now wired to `Progress.RecordDeed`, closing the
feedback loop that makes the radio announcer discuss the player's actions (D-074). Two hooks
(`WaterDrop`, `Rescued`) are deferred until the corresponding gameplay systems exist.

| deed | hook site | witnesses |
|---|---|---|
| `Delivered` | `SiteInteraction.CheckContractCompletion` | `PopulationAt(target)` |
| `Salvaged` | `SiteInteraction.AddSalvage` | `PopulationNear(site)` |
| `Declined` | `SiteInteraction.AddContractBoard` | `PopulationAt(source)` |
| `Crashed` | `Main._landing.Touchdown` (>10% structural damage) | `PopulationNear(pos)` |
| `ShotAt` | `ThreatWorld.OnStruck` | `PopulationNear(emitter)` |
| `Buzzed` | `Main.CheckBuzz` (<30 m AGL, 5 min cooldown) | `PopulationAt(site)` |

**Population model.** `WorldMap.PopulationAt(Site)` gives a seed-varied head-count per site
kind (settlements 30-80, farmsteads 3-13, wrecks 0). `PopulationNear(x, z, radius)` sums
all sites within 3 km. This is the witness count the announcer gates against: a delivery to
a populated settlement is news; a salvage run at an isolated wreck is not.

**Also fixed:** deed restoration in `SiteInteraction.RestoreState` — the loaded progress's
deeds were not being copied to the live instance, so they were lost on save/load.

**Why.** The announcer system was completely built (corpus, knowability gates, distortion,
test harness) but the feed was empty — he never mentioned anything the player did. This was
the last piece of the radio system identified in integration-debt.md.

**Reversibility:** high. Each hook is 3-8 lines at the site of the event. `PopulationAt` /
`PopulationNear` are two static methods on WorldMap. No sim/ changes, no new files.

---

## D-077 — The archipelago becomes real: eight islands, ten committed crossings · 2026-09-20 · **[FRED'S IDEA]**

**Decision:** The region layout is respread so that the eight regions are genuinely separate
islands. `WorldMap.BuildRegions` and the lobe table in `WorldHeight.Land` move together; The
Pan and Long Acre deliberately overlap and remain one home island, and every other pair is
separated by at least 2.5 km of open water.

**Why now.** D-059 decided the world was an archipelago and the data never followed it,
because it could not: every adjacent pair of regions sat 2.4 to 3.7 km apart and each needed
a 2.4 km lobe of land under it, so no cut anywhere left a channel the aircraft could not
glide. Measured, the world was **2 landmasses, the largest 121 km², with exactly one
committed crossing on the whole map**. It was one island with bays. Fred asked for the
islands to be spaced out, on the grounds that it would help the range-against-map-size
problem.

**The thing that makes it cheap.** Spacing regions apart adds **water, not empty land**. Each
region keeps its own lobe, its own radius and its own site counts, so site density per island
is exactly what it was — what grows is the sea between them, and D-059 already established
that an over-water leg is allowed to be empty because crossing it *is* the activity. This is
the whole of Fred's argument and it holds up.

**The gradient is the gating system** (D-010, D-059). Tier now costs crossings, not kilometres:

| | | |
|---|---|---|
| Home island | The Pan + Long Acre overlap | **0 m of water** |
| Tier 1 | Fenmoor, The Drowning | 2.9–3.2 km hop off home |
| Tier 2 | Cold Shoulder, Sawtooth Works | 3.4–3.7 km from a tier 1 island, but 12 km direct from home |
| Tier 3 | Ashmount, The Scald | 7.8–8.0 km committed |

The home island matters more than it looks. The first aircraft is a salvaged one and the tank
is rarely full; forcing a committed water crossing in the first ten minutes would be a
different game. Tier 2 being close to tier 1 and far from home is the other deliberate bit —
island-hopping is the cheap route and the direct line is the expensive one, which is a
navigation decision rather than a wall.

**Measured, before → after:**

| | before | after |
|---|---|---|
| landmasses over 0.5 km² | 2 | **8** |
| largest landmass | 121 km² | 36 km² |
| committed crossings between regions | **1** | **10** |
| water inside the envelope | 29.3 % | 84.7 % |
| sites placed | 119 | **119** |
| site shortfalls | 0 | **0** |
| story roles bound / degraded | 16 / 0 | **16 / 0** |

**Three things broke, and all three were invisible in the layout and obvious in the report.**

1. **`WorldMap.ContentHalfExtent` was still 6500 m** — D-003b's 13 km content envelope. It is
   a clamp on where a site may be placed, and the four outer islands were entirely outside
   it, so every candidate position out there was rejected *before the terrain was ever
   consulted*. The Drowning, Cold Shoulder and Sawtooth Works came back with zero
   settlements, zero workshops and zero depots; **sites fell from 119 to 64 with 33
   shortfalls**, and the only things that survived out there were wrecks, which are placed by
   a different rule. The region height tables were healthy the whole time, which is what made
   it look like a terrain problem. Now 16000, sized from Ashmount's corner.

2. **`WorldHeight.BasinCentre` is a hard-coded copy of the Wetland region centre**, and
   moving the region left the drowned basin 4.4 km out in open sea. The existing
   `BasinCheck` guard caught this on the first run and printed the drift and the distance —
   the guard was written for exactly this and it earned itself here.

3. **The Drowning's lobe was smaller than its own flood.** `BasinOuter` is 2500 m and the
   lobe was 2350 m, so the entire island sat inside the basin: highest ground 16 m, and
   nowhere to stand the relay mast or the overlook the placement plan asks for. It had been
   getting away with that by borrowing dry ground from the neighbours it overlapped, and it
   has no neighbours any more. The lobe is now 3100 m, which leaves a 600 m rim of ordinary
   terrain outside the flood — which is what a drowned basin looks like from the air anyway.

**Also fixed:** the world report's crossing table filtered legs longer than 6.2 km as "not
legs anybody flies". That was right when the regions were 2.4–6 km apart and wrong the moment
they moved, so the table printed one row and implied there was nothing to report. Now 13 km.

**Cost.** The world is 33 km across rather than 13. At 55 m/s that is about ten minutes corner
to corner against three hours of endurance, so it is not a range problem — it is a fuel and
commitment problem, which is what D-004 and D-059 wanted it to be. Terrain is procedural and
the streamer only loads near the aircraft, so the extra extent costs travel time rather than
memory.

**Reversibility:** medium. Two coordinate tables, one constant and one lobe radius; reverting
them restores the old world exactly. Anything that hard-codes a world extent has to move with
them — `ContentHalfExtent`, `IslandHalfExtent`, `BasinCentre` and the report threshold are the
four found so far, and the pattern to expect is a constant that was sized against the old
13 km envelope and says nothing about which envelope it meant.

---

### D-078 — Announcer voice: formant synthesis, not TTS · 2026-09-20

**Decision.** Give Hollis Kerr a synthesised voice rather than waiting for TTS or recorded VO.
`VoiceSynth` in `sim/src/` generates a formant-based murmur: a glottal pulse train at ~105 Hz
(low male, fifties) through three resonant filters whose frequencies wander, with syllable-rate
amplitude modulation derived from the text's word count and a 300–3400 Hz radio band-pass.
Nobody will understand the words — the captions still carry the meaning — but through a radio
channel the rhythm, pitch and timbre read as a man talking. Carrier hiss fills the gaps between
sentences, so tuning to the Upland Service always sounds like a live station.

**Why murmur, not real TTS.** (1) No external dependency — no model file, no library, no
runtime cost in the D-006 sense. (2) Matches the project's "synthesised from state, not
sampled" philosophy that RotorSynth, WeatherSynth and WarningSynth already follow. (3) Pure
.NET, engine-free, testable offline — four new simlab tests check waveform quality, carrier
hiss, voice-above-hiss ratio, and duration accuracy. (4) The radio band-pass already strips
most of what makes words intelligible; what remains is timing and timbre, which this provides.
(5) A real TTS voice that sometimes gets the words slightly wrong would break the "facts, not
instructions" contract more damagingly than a murmur that is obviously not trying to be words.

**What changed.** `VoiceSynth` (sim/src/, ~230 lines, no Godot dependency). `DjBroadcast`
gained a `VoiceSynth` instance and a `RenderVoice` method; it calls `Speak(text, duration)`
when a segment starts and `Stop()` when it ends. `CockpitRadio.PushAudio` renders voice PCM
into the same buffer the radio stream feeds, so the voice goes through the volume knob and
warning duck without knowing either exists.

**Reversibility:** high. Remove `VoiceSynth`, revert the three-line changes in
`DjBroadcast` and `CockpitRadio`, and captions alone remain — which is what was there before.

---

### D-079 — The drowned wreck sits in the water (2026-09-20)

**Decision.** Two changes put the DrowningWreck story site below the waterline where
beat 8 needs it.

**The problem.** `--worldreport` warned that the DrowningWreck sat 11.9 m above the
waterline (-5 m). The story says "half in the water, tail boom up", and the
`DrownedAircraft` prop is authored to stand in flat water with a fuselage mostly
underneath. But the wreck was dry, on a hummock, because of two things:

1. **No wreck candidates reached the basin floor.** The incident-anchor system that
   scatters wreck fields (WorldMap.IncidentsFor) generated one anchor 1.3–2.6 radii
   from the region centre. For The Drowning (r = 1400 m) that put the anchor 1820–
   3640 m out — on the dry rim or off the island entirely, well above the standing
   water that sits inside 2500 m (BasinOuter). All three Wetland wrecks clustered
   around that dry anchor, and Pick.LowestGround found the lowest of them: still dry.

2. **SitePads lifted crash sites out of depressions.** Every site gets a graded pad
   whose height is a weighted average of the centre (35%) and a ring of samples at
   0.7× the flat radius (65%). For a wreck in a channel (raw centre −7 m) surrounded
   by higher ground (+3 m ring average), the pad sat at (−7 × 0.35 + 3 × 0.65) =
   −0.5 m — 4.5 m above the waterline. The ring pulled the wreck up and out.

**What changed.**

1. `WorldMap.IncidentsFor`: the Wetland region gets a second incident anchor at its
   own centre. Half the placement attempts now scatter from the basin centre (the low,
   wet ground), the other half from the original incident (the wreck field on the rim).
   This is the minimal change that gives candidates a path to the waterline without
   overriding the wreck-field mechanic for every other region.

2. `SitePads.Ensure`: wrecks no longer blend with the surrounding ring. A crash site
   is not graded — it sits where it came down. The tiny pad (14 m flat, 40 m blend)
   still levels the immediate landing area for salvage, but at the natural centre
   height rather than the ring average. This stops the pad lifting a wreck in a
   depression out of it, which is exactly what was happening.

**Measured, before → after:**

| | before | after |
|---|---|---|
| DrowningWreck raw ground | +6.9 m (11.9 m above waterline) | **−10.1 m (5.1 m below waterline)** |
| sites placed | 119 | **119** |
| shortfalls | 0 | **0** |
| story roles bound / degraded | 16 / 0 | **16 / 0** |
| story problems | 1 (DrowningWreck DRY) | **0** |

The three Wetland wrecks are now: #60 Salt Sink at −10.1 m (in the water), #61 Broke
Wash at 1.7 m (at the edge), #59 the Turn at 8.6 m (dry, on the rim). The spread
means the wreck field has wrecks both in and out of the water, which is what a flooded
valley with aircraft wreckage in it looks like.

**Reversibility:** high. Revert the two edits — one line in IncidentsFor, one `if`
block wrapping the ring-average loop in SitePads — and the old placement returns.
The world is seed-generated and all constraints are verified by `--worldreport`.

---

### D-080 — Terrain-driven reverb and site ambient sound · 2026-09-20

The audio benchmark's top gap: no reverb, no occlusion, and sites make no sound at all.
Landing and shutting down produced total silence at exactly the moment the player is most
receptive to being told where they are. This closes two of the five identified gaps.

**Architecture** (sim layer, no Godot dependency):

- **`AcousticSpace`** — samples the height field in two concentric rings (50 m and 200 m)
  around the listener and computes enclosure, water proximity, reverb parameters (room
  size, damping, wet level) and a distance low-pass frequency. A valley with walls rising
  above the listener gives high enclosure; open ground and high AGL give none; water gives
  low damping and broad reflections.

- **`AmbientSynth`** — synthesises the voice of a place from its `SiteKind`. Settlements
  get a generator drone (drifting ~58 Hz fundamental) and activity murmur. Wrecks get
  metal creaking (impulsive noise at a wind-proportional rate). Relays get 50 Hz
  transformer hum. All site kinds get wind-through-structures (whistle + rattle) scaled
  by metal content and wind speed. A cooling tick fires when the aircraft is shut down at
  a site. Overlooks are intentionally silent — the absence of sound IS their character.

**Architecture** (game layer):

- **`EnvironmentAcoustics`** — creates a "Reverb" audio bus in code, adds
  `AudioEffectReverb` and `AudioEffectLowPassFilter`, and updates both each physics frame
  from `AcousticSpace` output with 3 Hz smoothing.

- **`SiteAmbience`** — manages one `AudioStreamPlayer3D` per nearby site (600 m audible
  range), each fed by its own `AmbientSynth` instance. Positional, so the settlement
  generator builds in the correct ear as you approach. Sites outside 800 m are released.

- **Helicopter audio** routes through the Reverb bus so terrain acoustics shape the rotor
  sound — a helicopter in a valley bounces off the walls.

**Six simlab tests:**
- `acoustics_open`: flat ground → near-zero enclosure and wet level
- `acoustics_valley`: terrain rising above the listener → enclosure > 0.2
- `acoustics_water`: terrain below water level → water proximity > 0.5
- `acoustics_agl`: high AGL suppresses enclosure even with valley walls below
- `acoustics_ambience`: every site kind produces bounded, non-clipping output; all except
  Overlook are audible
- `acoustics_wind`: wreck ambience is louder in wind (more creaking, rattling)

**What this does NOT do** (deliberately deferred):
- Occlusion (a building between you and the source attenuating it) — would require
  per-source raycasts and the gain is small for the typical flyover listening distance
- Doppler / distance timbre on the rotor (separate concern, not terrain-driven)
- On-foot-specific audio (footsteps, ticking cooling aircraft at the player's ear)

**Reversibility:** high. Remove the two `AddChild` lines in `SceneMood.Apply`, revert
`HelicopterAudio.Bus` to "Master", and delete the four new files. No other system depends
on the reverb bus.

### D-081 — Forward gun pod: air-to-ground gunnery and in-flight Rotor Time · 2026-09-20

The vision doc's signature mechanic has two halves: on-foot called shots (done) and
in-flight strafing runs against ground targets. This completes the second half.

**What it is:** a fixed-forward M60 gun pod, installed as a module (`gunpod`, 45 kg,
0.08 m² frontal drag). LMB fires while flying — aim by pointing the helicopter's nose.
During Rotor Time (right mouse button, already available in both modes) the world slows
to 0.3x, giving time to line up a strafing run. Rounds check every threat emitter within
800 m using ray-to-point distance; a hit within 15 m reduces `EmitterHealth` by 0.12
per round (~9 rounds to destroy, at 550 rpm). Destroyed emitters stop tracking, scanning,
and engaging — the RWR goes quiet, completing D-010's progression from "jammer" to
"emitter locator" to "emitter destroyer".

**Architecture (sim layer):**
- **`AirGunnery.cs`** — `GunPodState` (200 rounds, fire rate, range), `EmitterGunnery`
  (ray-point hit testing, emitter damage application), `GunHitResult` record.
- **`Threats.cs`** — `ThreatTrack.EmitterHealth` (already existed), `Destroyed` property.
  `ThreatField.Update` now skips destroyed emitters, resetting them to `Idle`.
- **`Loadout.cs`** — `gunpod` module added to catalog and distribution pool.

**Architecture (game layer):**
- **`GunPodController.cs`** — manages cooldown, calls `EmitterGunnery` with helicopter
  nose direction, returns `GunHitResult` for HUD feedback.
- **`Main.cs`** — LMB fires gun pod while flying (sidearm on foot); install/remove hooks;
  save/load for gun rounds and emitter health.
- **`FlightHud.cs`** — gun crosshair (widens during Rotor Time), ammo bar, hit feedback
  text (only hits shown — at 550 rpm, showing misses is unreadable).

**Save/load:** `GunRounds` and `EmitterHealth` dictionary (keyed by emitter ID, only
storing damaged/destroyed entries) added to `SaveData`. Old saves load with full ammo and
all emitters intact, which is correct.

**Six simlab tests:** `gun_ammo`, `gun_raypoint`, `gun_emitter`, `gun_destroyed`,
`gun_save`, `gun_module`.

**Why a module, not a default:** D-011's "avionics ARE the skill tree" philosophy. The
gun pod is found, installed, and has real mass and drag consequences. A player who installs
it trades 45 kg of payload and 0.08 m² of drag for the ability to fight back from the
cockpit. The weight shifts CG forward, which changes handling.

**Reversibility:** high. Remove the `gunpod` entry from `Loadout.All`, delete
`GunPodController.cs`, revert the input/HUD/save additions in `Main.cs` and `FlightHud.cs`.
No other system depends on gunnery.

## D-082 — Heading compass with bearing guidance

*2026-09-20*

**Decision.** A horizontal compass strip at the top of the HUD, centred on the aircraft's
heading, with bearing chevrons pointing to active contract targets and a distance readout
to the nearest one. The kneeboard MAP page gets dashed bearing lines from the aircraft to
each active contract destination.

**Why.** The contract board (D-049, D-062) tells the player *what* to do; nothing told them
*where* to go. The kneeboard MAP shows sites as diamonds, but mid-flight there was no way
to know "am I heading toward the contract target?" without opening the kneeboard, finding
the diamond, and mentally comparing it to the chevron. A real helicopter has an ADF or
GPS — we needed the game equivalent.

The compass strip shows ±60° of heading with tick marks every 10°, cardinal labels (N/E/
S/W plus intercardinals), and a centre reference mark. Contract targets appear as gold
chevrons on the strip with a truncated name label; off-strip targets get an arrow at the
edge. The nearest target's name and distance appear below the strip. On the kneeboard MAP,
dashed gold lines run from the aircraft position to each active contract target, so the
pilot can plan a route around threat circles.

**Gated on SAS** (D-055): the compass requires the sensor package. Without SAS, the pilot
flies by visual reference — heading numbers and bearing guidance require instruments. This
is consistent with "instruments are items".

**Architecture.** `Navigation.cs` in sim/ provides `BearingRad`, `DistanceM`, and
`RelativeBearing` as pure static methods (flat-earth, 1.3 m error at 13 km). `NavTarget`
record carries name and NED position. `Main.cs` builds targets from `Progress.ActiveContracts`
each physics frame (only reallocates when the set changes) and passes them to `FlightHud` via
`SetNavTargets`. The compass drawing is entirely in `FlightHud._Draw()`.

Four simlab tests: `nav_cardinals`, `nav_distance`, `nav_relbearing`, `nav_roundtrip`.

**Reversibility:** high. Delete `Navigation.cs`, remove `DrawCompass`, `SetNavTargets`,
`UpdateNavTargets`, and `DrawBearingLines`. No other system depends on the compass.

## D-083 — Story engine readiness: place-based gates, dialogue at story sites, night awareness · 2026-09-20

**Decision.** Three changes that make the search thread and dialogue system usable by
story.md's authored beats:

1. **`ThreadContext` struct and place-based beat gates (story.md §7.2).** `SearchBeat.Gate`
   now takes `(Progress, ThreadContext)` instead of `(Progress)`. `ThreadContext` carries
   the game clock, airborne flag, parked site id, night flag, and a `HashSet<string>` of
   visited story-role ids (e.g. `"place.mattie"`, `"place.wreck"`). Each beat's gate now
   requires that the player has visited the site whose story role carries the beat's
   information — so "the cairn" cannot fire while parked at a basin farmstead, and
   "the manifest" cannot fire without visiting Nell's settlement. Counter thresholds remain
   as floors to prevent beats from stacking if several role sites are visited quickly.

2. **Talk at non-Settlement sites (story.md §7.5).** `SiteInteraction.RebuildActions` now
   offers Talk at any site where `StoryPlaces.For(site.Id)?.NpcId` is not null, not only
   at Settlements. This lets story NPCs like Juno Kessel (at an airfield) and Halvard Ferren
   (who may degrade to a workshop or depot) be spoken to. Generic settlers are not spawned
   at non-settlements — the guard is the `StoryPlaces` NPC declaration.

3. **`ArrivedAtNight` is no longer hard-coded false (story.md §7.8).** `BuildTalkContext`
   now reads `SceneMood.SunNow.IsNight` (sun elevation ≤ −12°), so dialogue lines gated on
   `ArrivedAtNight` — including several finale lines — work.

**Architecture.** `ThreadContext` lives in `sim/src/SearchThread.cs` (pure .NET, no Godot
dependency). The game layer builds it in `SiteInteraction.BuildThreadContext()`, which
iterates `StoryPlaces.Specs` and checks `Progress.HasVisited` for each role's bound site id,
passing the visited set as string ids so the sim layer never references `StoryPlaces` or
`RegionKind`. One new simlab assertion in `search_advance` verifies that the place gate
actually blocks: a context with no visited roles prevents advancement even when the counter
threshold is met.

**Why.** The search thread note said: *"The gates here are counter-based. story.md specifies
place-based triggers which require site-role resolution. That system does not exist yet."*
StoryPlaces now exists and resolves all sixteen roles. The counter-only gates let beat 6
("a wreck in the wetlands") fire while parked at a basin farmstead, which makes the story
read as generated filler. The place-based gates ensure the player is at the right site —
where the journal entry describes what is actually around them.

**Reversibility:** high. `ThreadContext` is one struct, the gate signature change is
mechanical, and the game-layer `BuildThreadContext` is 15 lines. Reverting to counter-only
gates is a find-replace of the lambda signatures and dropping the `ctx.Visited()` clauses.

---

### D-084 — Kneeboard THREAD page · 2026-09-20

**Decision.** A sixth kneeboard page — THREAD — shows the search arc's structure as a
physical document: the four legs of SIERRA-FOUR-THREE's ferry route (with open/closed
status and closure day), the airband frequency hunt (N of 34 logged), the main rotor
ceiling as a percentage of serviceable life, and total flight hours since last track. The
current hint is shown at the bottom. No next-objective marker, no completion percentage.

**Why.** Story.md §4.4 specifies it, and benchmark pass 2 identified mission structure as
the critical gap (2/10). The JOBS page shows contracts (the per-sortie purpose); the
THREAD page shows the search (the long-term pull). They are different things and must not
be merged — Elite has the first and no second, and ROTORWASH needs both. The spec says
"the arc has to be visible or it does not exist."

Implementation: `SearchThread` now tracks four leg closure timestamps (persisted in save),
`DamageState` tracks total flight hours (persisted in save), and the kneeboard renders
the page from those two sources plus `Progress.Known` and `Progress.CountKnown`. One new
simlab test (`search_legs`) verifies leg closure stamps and save round-trip.

**Reversibility:** high. One kneeboard page, one new simlab test, two small save fields
that default to sensible values for older saves.

### D-085 — Radio strip and beat 5: the world speaks to you in flight · 2026-09-20

**Decision.** A two-line text strip at the bottom of the HUD for radio messages, with
word-by-word reveal at reading pace (story.md §4.3, §7.4). Three kinds of message are
colour-coded: broadcasts (cool blue), directed calls (green), intercepted traffic (warm
red). Messages are suppressed during threat engagement and resume after.

Beat 5 (`search.voice`) is inserted into the search thread between "The broadcast" (beat
4, Doss's rota) and "The manifest" (now beat 6). Gate: `Knows(search.rota) && Airborne
&& IsWeatherWindow(GameClock)` — the player must be flying during the 06:40 ± 20 min
weather broadcast window after learning about the rota from Doss. When the beat fires,
the radio strip shows a METAR-style weather sequence mentioning knots — the tell Doss
described. This is story.md's "the first time the world speaks to you in flight is the
moment this stops being a sandbox."

**Architecture.** `RadioStrip` lives in `sim/src/RadioStrip.cs` (pure .NET, no Godot
dependency): a `Queue<RadioMessage>` with word-by-word reveal at `WordsPerSecond = 2.55`
(matching RadioDj for natural caption feel), a configurable hold time, and a `Suppressed`
flag that defers new messages without interrupting the current one.
`SearchBeat.RadioText` is a new optional field: if non-null, the game layer pushes the
text to the radio strip when the beat fires. `SearchThread.IsWeatherWindow(double)` is a
static helper checking the 06:20–07:00 window. `FlightHud.DrawRadioStrip()` renders the
strip at bottom-centre with word-wrapping, background panel, and speaker tag. `Main.cs`
creates the strip, passes it to both FlightHud and SiteInteraction, and updates it each
physics frame with suppression driven by `ThreatField.AnyEngaging`.

The beat array now has 12 entries (was 11). `BeatToLeg` indices shift by 1 for all
post-insertion beats. Existing saves with `Stage ≤ 3` gain the new beat naturally;
saves with `Stage > 3` skip it, which is correct (they have already left Act I).

**Six simlab tests:** `strip_enqueue`, `strip_wordreveal`, `strip_hold`,
`strip_suppressed`, `strip_queue`, `strip_voicebeat`. The voice beat test verifies the
weather window boundaries, the airborne requirement, the time-of-day gate, and the
RadioText content.

**Why.** Story.md §8 item 7: "The radio strip (§7.4) and beat 5 — the first time the
world speaks to you in flight is the moment this stops being a sandbox." Items 1–6 from
that build order are all complete. The radio strip is the infrastructure that all three
kinds of in-flight radio call (§4.3) will use; beat 5 is the first content that proves it
works.

**What this does NOT do** (deliberately deferred):
- Directed calls (someone raises the player) — needs per-region trigger logic
- Intercepted calls (overheard traffic in threat envelopes) — needs threat-state hooks
- DJ captions on this strip — the DJ already displays via RadioReadout in CockpitRadio

**Reversibility:** high. Remove `RadioStrip.cs`, revert `SearchBeat.RadioText` and the
inserted beat, remove `DrawRadioStrip` from FlightHud, and remove the three wiring lines
in Main.cs. No other system depends on the radio strip.

### D-086 — Main rotor repair ceiling: the clock on the search · 2026-09-20

**Decision.** `DamageState.MainRotorCeiling` degrades linearly with `TotalFlightHours`
at 0.0019 per hour, flooring at 0.55. `Repair` and `RepairAll` cannot push the main
rotor past this ceiling. `ResetRotorHours()` zeros the hours and restores the ceiling
to 1.0 — this is the payoff when new blades are installed, and the only event in the
game that does it. Story.md §1.3.

**Why this is the right mechanic.** The ceiling falls 0.45 over 240 rotor hours
(~20 real hours with the rotor turning). It is already felt: `RotorThrustFactor`
(0.80 + 0.20 × health) and `RotorImbalance` ((1 − health) × 0.09) are wired into the
flight model, so a falling ceiling means the aircraft genuinely shakes and genuinely
will not hold a hot out-of-ground-effect hover. At the floor (0.55), thrust factor is
0.89 and imbalance is 0.041 — unpleasant but flyable, which meets D-007's "forgiving"
requirement. The kneeboard THREAD page shows `ceiling 82% · 94 h since track` — a
fact, not an inference (D-005a).

**Constants:** `CeilingRate = 0.0019`, `CeilingFloor = 0.55`. The rate is a first
estimate; story.md §1.3 recommends measuring it against a standard sortie profile at
several ceiling values to validate the feel, the same way D-014 tuned terrain relief.

**The kneeboard THREAD page** now reads the actual `DamageState.MainRotorCeiling`
rather than deriving a proxy from current health. The bar and percentage reflect the
repair limit, not the current condition — which is what the player needs to plan around.

**Five simlab tests:** `ceiling_degrades` (formula at 0/94/120/240/1000 h),
`ceiling_repair` (repair capped, other components unaffected), `ceiling_repairall`
(RepairAll also capped), `ceiling_reset` (new blades restore 1.0), `ceiling_save`
(round-trip through SaveData preserves hours and ceiling).

**Reversibility:** high. Remove the ceiling constants and property, revert the one-line
changes to `Repair`/`RepairAll`, revert the kneeboard to the health-derived proxy, and
delete the test file. No other system depends on the ceiling.

---

## D-087 — A ring around a citadel, and terrain rules that bend · 2026-09-20 · **[FRED'S IDEA]**

**Decision.** The eight regions are laid out as a ring of seven around **The Scald** at the
centre, rotated 55 degrees. The Scald is the citadel: it is Act III, the sealed magazine, the
blades, the thing the whole search is for, and it also carries the Upland Service's
transmitter. Separately, the site placement rules gain **terrain desperation passes** in the
same shape as the existing spacing ones.

**Why the ring.** Fred asked: *"If the final island is a well defended central island, could
you force travel around an outside ring of islands?"* D-077 had put the home island in the
middle and raised tier with radius, so every trip was radial — out to the rim and back — and
the safest place on the map was the middle of it. Inverting that gives four things the old
shape could not:

  * **The endgame is visible from everywhere and unreachable.** A citadel you fly around for
    a whole campaign is a better object than one over the horizon.
  * **Gating is D-010's air defence rather than distance.** Hexagonal geometry means a hop
    to the centre and a hop to the next island along are *necessarily* the same length, so
    the centre being close and lethal is the design rather than a compromise: you are not
    kept out by fuel, you are kept out by what is down there.
  * **Opposite sides of the ring are where circulation actually bites.** The Pan to Ashmount
    is 21 km straight across and goes directly through the citadel's envelope; round the ring
    it is two ordinary hops. That is a navigation decision with a reason behind it.
  * **The transmitter gets the best position on the map.** Every island is equidistant from
    the hub, so one mast covers the whole world instead of half of it — and it is the last
    place the player can reach, so the station is with them from the first minute and
    destroying it is an endgame choice rather than an early accident.

**Measured:** 8 landmasses, 117 sites, 16/16 story roles bound with none degraded, and
**every** inter-region leg a committed crossing — 4.7 to 8.1 km of open water, against a 2:1
glide that buys about a kilometre from 500 m. The home island stays effectively dry (1% water
on the Pan–Long Acre leg, longest gap 25 m), so a player can still learn to fly without being
committed. The world is 28.4 km across, smaller than D-077's 33 km: a ring packs eight
islands more tightly than a tiered spiral.

**The rotation was measured, not chosen.** `--ringscan` exists because guessing failed three
times in a row. The layout says where a region *is*; the continental noise field decides what
the ground is like once it gets there; and the two know nothing about each other. At 0 degrees
Cold Shoulder — the region literally called Upland, whose relay mast and overlook both need
ground above 110 m — landed on a patch topping out at **109 m**. Rotated to clear that, it
took Fenmoor's mast and Long Acre's airfield instead.

**Which is the real finding, and the more valuable half of this entry.** Whack-a-mole across
three rotations is evidence that the *thresholds* are wrong, not the angles. The height and
slope bands in `TryPlace` describe what a place of that kind would really want — a relay on a
ridge above 110 m, a runway under four and a half degrees — and they were being enforced as
absolutes against a noise field that has never heard of them. `Place` already had three
passes of widening *spacing* desperation for exactly this reason, and the argument transfers
without modification: **a region missing the only mast in it is a far worse outcome than a
mast on ground that is merely the highest available.** So the terrain bands now relax through
1.0 → 0.55 → 0.25 after the ideal has genuinely failed everywhere, with the relay's floor
relaxing *downward* because it is the one rule that wants a minimum rather than a maximum.

Effect, on the same layout: shortfalls 5 → 2, relays 7 → 8, airfields 4 → 5, sites 114 → 117.
The two that remain are overlooks, which carry no loot and no story role.

**Reversibility:** high for the layout — two coordinate tables and a rotation; the shape of
the change is identical to D-077's and the same verification (`--worldreport`: landmasses,
shortfalls, story roles) covers it. Medium for the relaxation, in the sense that it is now
load-bearing: it is what stops the next layout move costing a day of hand-tuning, and
removing it would reintroduce silent placement failure for any region that lands on
unsuitable noise.

**Open for Fred:** ~~the citadel's defences are not built yet.~~ Resolved by D-088.

---

## D-088 — Citadel air defence ring · 2026-09-20

**Decision.** The Scald gets a dedicated layered air defence network beyond the normal
site-based placement, placed in concentric rings around the world centre at fixed angular
positions:

  * **3 SAMs** at 2.0 km, 120° apart — overlapping engagement zones covering 120–6000 m AGL.
    From 2 km, their 11 km engagement range extends past the ring islands at 10.5 km.
  * **4 MANPADS** at 1.5 km, 90° apart, offset 45° from the SAMs — low-level denial for
    anyone ducking under the SAM floor at 120 m AGL.
  * **3 guns** at 0.8 km, 120° apart, offset 60° from the SAMs — deck coverage on the
    final approach, inside 1200 m.
  * **2 search radars** at 3.0 km, 180° apart — extends the detection umbrella to 22 km,
    which reaches all seven ring islands from the centre.

Total: 12 dedicated citadel emitters. Combined with the site-based defences The Scald
already has, plus its tier-3 aerostat, the centre is now layered at every altitude band.

**Why.** D-087 designed the ring layout so that the centre is close and lethal — "you are
not kept out by fuel, you are kept out by what is down there." The site-based placement
algorithm gave The Scald roughly the same threat density as any other tier-3 region, which
meant a straight crossing from The Pan to Ashmount through the centre was no more dangerous
than skirting the ring. The geometry argument for the ring only works if the centre is
genuinely dangerous.

**Measured.** A straight 21 km crossing through the citadel at clear line of sight:

  * 50 m AGL: 12 hits, peak exposure 1.00, locked ✓
  * 300 m AGL: 45 hits, peak exposure 1.00, locked ✓
  * 900 m AGL: 56 hits, peak exposure 1.00, locked ✓

113 total hits across three altitude bands. The citadel is lethal at every altitude. Fixed
angular positions mean the layout is deterministic and learnable. The gun pod (D-081) can
destroy emitters at 800 m, so the defence ring is not a permanent wall — it is a problem
the player can methodically dismantle over multiple sorties as the endgame approaches.

**Reversibility:** high. The entire ring is placed by `PlaceCitadelRing` in `ThreatWorld.cs`,
a single method that can be deleted or retuned (radii, counts, angular offsets) without
touching any other system. The emitters use the same `ThreatField.Make` factory as every
other emplacement. One new simlab test (`citadel_ring`) verifies the crossing is lethal.

### D-089 — Dialogue rewards: NPC trades actually grant things · 2026-09-20

The dialogue corpus has thread lines where NPCs describe giving the player capability items
("the crate is yours" — Nell Abergale on the RWR). Before this change, nothing was actually
granted. The lines were text-only; the story said the progression happened but the progression
system did not agree.

**Decision:** add `DialogueReward` to `DialogueLine` — a small record that says what to grant
when the line is delivered. The game layer (`DialoguePanel.BeginLine → SiteInteraction.DeliverReward`)
executes it. Module rewards follow the same path as salvage discovery (D-011): module goes
into the bag via `Loadout.Find`, player learns a Schematic, journal notes it. Knowledge rewards
call `Progress.Learn` directly. Both paths are idempotent — a duplicate find/learn is a no-op.

**Three trades wired (Act II):**
* `nell.thread.rwr` → module "rwr" (gated on `Knows(Manifest)` + standing ≥ 0.3)
* `osie.thread.chart` → `KnowledgeKind.Chart` "chart.upland.masking" (gated on `Knows(Cairn)` + 2 meetings)
* `ferren.thread.chart` → `KnowledgeKind.ThreatSite` "chart.emitter.locations" (gated on `Knows(Cairn)` + 2 meetings)

**Why these three:** they are the Act II capability trades from story.md §3 — the ones that
make the progression system (D-005, D-010) feel mechanical rather than cosmetic. Each
corresponds to a story region (Fenmoor, Cold Shoulder, Sawtooth Works) and each gives the
player something that opens the next region: the RWR makes threats survivable, the upland
chart opens dead-ground routes, and the emitter chart reveals where the threat sites sit.

**Why not Bel's trade:** Bel Tiernan's trade (wreck position for a generator lift) is
multi-step: she needs the hoist installed first, then a contract completed. That requires a
contract-completion hook, which is a separate piece of work.

**Why idempotent:** the same line can fire on every visit if the requirements hold. Making the
reward a no-op on the second delivery means there is no penalty for talking to the same NPC
again, and no need for a "reward already delivered" gate that would add state to every line.

**Reversibility:** high. The entire feature is three lines in `DialogueLine` (the `Reward`
field), a helper in `DialogueCorpus` (`LR`), three tagged lines, one method in
`SiteInteraction`, and one line in `DialoguePanel`. Removing `Reward` and reverting to `LT`
restores the prior state. No save/load format changes — modules and knowledge already persist.

## D-090 — Passengers: Sera Wray in the right seat · 2026-09-20

**Decision.** The "smallest version today" from story.md §7.6: Wray is a mass item at the
co-pilot seat, a dialogue reward that sets a flag in Progress, and a callout system that
feeds the radio strip during flight. No visible figure — the game's last flight has a second
voice in it without a single line of new visual work.

**What it is:**
  * `Passenger` record in sim/: id, name, mass (68 kg), seat position `(2.00, 0.62, -0.25)`.
    Mirrors the pilot's left seat. The CG shifts measurably rightward.
  * `CopilotCallouts` in sim/: pure .NET, no Godot. Called every frame while airborne with
    a passenger aboard. Checks torque (>85%), Nr (<95%), altitude (descending below 120 ft),
    fuel (<25%, <10%), and threat engagement. Returns a `RadioMessage` with kind `Copilot`
    (warm amber on the HUD, distinct from all other message kinds). Priority: threat > Nr >
    torque > altitude > fuel. Minimum 8 s between any call; category cooldowns prevent spam.
  * `DialogueRewardKind.Passenger`: third reward kind alongside Module and Knowledge.
    Fires on `wray.board` ("I am in. Do not wait for me to be comfortable. Go."), gated on
    Window knowledge + standing ≥ 0.5 + at least two meetings.
  * `Progress.PassengerAboard`: nullable string, persisted in SaveData. The game layer adds
    the mass item when non-null and creates the callout system.
  * NPC restore fix: the load path now uses `DialogueCorpus.Named()` for all nine story
    characters, resolving via `StoryPlaces.For()`. Previously only Mattie got her authored
    bank; everyone else fell through to generic settler lines on reload.

**Why not a module:** A passenger is a person, not equipment. Modules have drag, fuel delta,
and a parts cost; passengers have none of that. The mass system already handles arbitrary
named items. Making Wray a module would put her in the bag next to the gun pod.

**Why callouts via RadioStrip:** story.md §7.6 says "her calling the torque in flight is
the radio strip from §7.4 in a different colour." The RadioStrip already handles queuing,
word-by-word reveal, and suppression during engagement. Adding a fourth `RadioMessageKind`
was one enum value and one line in the HUD colour switch.

**Why warm amber:** The existing colours are: broadcast (cool blue), directed (green),
intercepted (warm red). Amber sits between green and red on the spectrum, reads as "internal
crew" rather than "incoming contact", and is distinct from all three.

**Reversibility:** high. Remove `Passenger.cs`, `CopilotCallouts.cs`, the `Copilot` enum
value, the `Passenger` reward kind, the `wray.board` line, `Progress.PassengerAboard`,
`SaveData.PassengerAboard`, the capture/apply lines, the event subscription and
`SyncPassenger()` in Main.cs. The NPC restore fix should stay.

---

### D-091 — Blade pair sling load · 2026-09-21

**Decision:** Wire the 420 kg blade pair as a named `SlingLoad` configuration attached to
the cargo hook. story.md §7.7: "the load is a MassItem at the hook position, 420 kg, plus
a drag delta." The sling physics (`SlingLoad.cs`) already existed with pendulum dynamics,
cable tension, drag and ground contact; this decision uses the full system rather than the
"smallest version" MassItem shortcut.

**What was built:**

  * `SlingLoads.BladePair()`: factory method returning a configured `SlingLoad` (420 kg,
    5 m cable, 2.0 m² drag area). The class also provides `SlingLoads.ById()` for
    save/load lookups. 420 kg is two blades at 145 kg each plus grips and tie bars
    (story.md §5). 2.0 m² drag is higher than the water bucket's 0.9 because a 7.3 m
    blade pair in tie bars is long and awkward.
  * `Progress.SlingLoadId`: nullable string tracking what hangs on the hook. The load's
    physical parameters come from the factory, not from save data.
  * `SaveData.SlingLoadId`: capture and apply alongside PassengerAboard. A save written
    before this field existed loads as null — correct, because the blade pair is an
    Act III event.
  * `DialogueRewardKind.SlingLoad`: fourth reward kind. `DeliverReward` sets
    `Progress.SlingLoadId` when the hook module is installed. Event raised for the game
    layer.
  * `SyncSlingLoad()` in Main.cs: creates or removes the load on `Helicopter.Hook`,
    clearing the load if the hook module has been removed. Called on load and on the
    `SlingLoadAttached` event.
  * Five simlab tests: catalog consistency, trim effect (+0.029 collective, +90 kW),
    ceiling cost (-1250 m OGE), save round-trip, and the finale feasibility scenario.

**Measured effects at 500 m, ISA, 400 kg fuel:**

  | condition      | collective | pitch deg | power kW | ceiling m |
  |----------------|-----------|-----------|----------|-----------|
  | bare           | 0.492     | 4.24      | 713      | 3500      |
  | blade pair     | 0.521     | 3.28      | 803      | 2250      |
  | blade pair +20 | —         | —         | —        | 1625      |

**Finale feasibility (story.md §5 mandate):** worst plausible case is 220 flight hours
(rotor ceiling 0.58), ISA+10, Wray aboard (68 kg), hook module (28 kg), blade pair
(420 kg), 300 kg fuel. The aircraft lifts to 120 m, cruises 10 km at 15 m/s, and arrives
with 265 kg fuel remaining. The finale closes. This was measured before the dialogue was
authored, as §5 requires.

**Why the full SlingLoad, not a MassItem:** §7.7 calls the MassItem the "smallest version
today" and gives 80% of the feel. But the sling physics already exist and are verified by
five existing tests. Using them costs nothing extra and the player gets the remaining 20%
— the nose-down trim, the swing in a turn, the cable tension in the climb. The pendulum
is not the stretch goal it was when §7.7 was written; it shipped in D-040.

**Why 2.0 m² drag:** The water bucket uses 0.9 m² (compact, round). A blade pair bundled
in tie bars is 7.3 m long and roughly 0.53 m wide; dangling from a hook it presents a
projected area of about 3.8 m² at Cd ~0.5. 2.0 m² is conservative — bundled pair with
leading edges forward, not broadside. The exact value is tunable without changing any
interface.

**Reversibility:** high. Remove `SlingLoads` class, `Progress.SlingLoadId`,
`SaveData.SlingLoadId`, the capture/apply lines, `DialogueRewardKind.SlingLoad`, the
`DeliverReward` case, `SlingLoadAttached` event, `SyncSlingLoad()` in Main.cs, and
`BladePairTests.cs`. The existing sling physics and tests are untouched.

---

### D-092 — Bel's trade completion: knowledge-gated dialogue and the generator lift · 2026-09-20

**Decision:** Build Bel Tiernan's generator lift contract and fix the bug that made all
knowledge-gated dialogue inert. story.md §2 and §3: Bel wants a generator hoisted off an
island; in return she tells the player where the drowned wreck is.

**What was built:**

  * **`BuildTalkContext` bug fix** (story.md §7.3). `TalkContext.KnownIds` and
    `VisibleFittings` were declared but never populated in `BuildTalkContext`, so every
    `Requirement.Knows()` and `Unknown()` gate silently returned false/true respectively.
    All thread dialogue from D-089 (Nell's RWR trade, Osie's chart, Ferren's emitter
    chart) was affected. Now `KnownIds` is populated from `Progress.AllKnown.Keys` and
    a new `FittedIds` set is populated from `Loadout.Installed`.

  * **`Requirement.Fitted()` prefix** — a new requirement type that tests whether a module
    is currently installed on the aircraft. `"fitted:hoist"` checks `TalkContext.FittedIds`
    for `"hoist"`. This is distinct from `Knows("module.hoist")`, which tests whether the
    player has ever *found* the hoist (bag or installed).

  * **`ContractKind.Lift`** — a new contract kind for hoist-based jobs. Completion condition
    is `HasVisited(TargetSiteId)`, identical to Survey. The hoist requirement is enforced at
    offer time, not completion time, because the mechanical act of hoisting is not simulated.

  * **`ContractBoard.StoryContract()`** — a new static method called alongside `Generate()`
    that checks for story-specific contracts at the current settlement. At Bel's settlement,
    when the hoist is installed and wreck position is unknown, it offers a Lift contract to
    the nearest nearby site. On completion, `PayOut()` grants `bel.wreck_position` knowledge
    with detail text describing the drowned wreck.

  * **Bel's dialogue update:**
    - `bel.thread.hoist` now requires `Fitted("hoist")` + `Unknown(WreckPosition)` + 2+
      meetings. Previously required `Knows(Wreck)` which contradicted the trade's purpose.
    - New line `bel.thread.traded`: fires after the contract completes (player knows wreck
      position but hasn't searched it yet). Bel describes where the wreck is.
    - New line `bel.thread.traded.found`: fires when the player has both wreck position AND
      has searched the wreck. Higher specificity (weight 8, 2 reqs) than `bel.thread.raft`
      (weight 7, 2 reqs).
    - `bel.thread.bye` now gates on `WreckPosition` instead of `Wreck`.

  * **`Knows.WreckPosition`** constant added to `DialogueCorpus.Knows`, referencing
    `ContractBoard.BelWreckPositionId` (`"bel.wreck_position"`). Added to `Knows.All` so
    the "knows everything" test context includes it.

  * Six new simlab tests in `BelTradeTests.cs`: Lift completion, story contract gating,
    hoist dialogue gating, post-trade dialogue progression, Lift save/load round-trip,
    and `fitted:` requirement mechanics.

**The trade flow:**
1. Player finds and installs hoist module at a workshop.
2. Visits Bel with 2+ meetings → `bel.thread.hoist` fires: "You have a hoist on it now.
   Then we can talk about the island..."
3. Contract board shows "Lift generator from [island]" with Bel's brief text.
4. Player accepts, flies to the island, the contract completes.
5. `PayOut()` grants `bel.wreck_position` knowledge: the player now knows which wreck in
   The Drowning is SIERRA-FOUR-THREE.
6. `bel.thread.traded` fires next visit: "The lowest ground in the wet. You will see the
   tail boom."
7. Player flies to the wreck and searches it → `search.wreck` fires (beat 8).

**What was NOT built:**
- Standing reward from contract completion. `StandingReward` is set (0.3) but the game layer
  does not apply standing from contract payouts — this is a pre-existing gap across all
  contract types, not specific to this trade. The standing field is saved; applying it
  requires mapping completed contracts back to their issuing NPC.
- Generator sling load. The generator is not physically modelled — the player visits the
  island and the contract completes. This matches the Survey contract design: the player
  did the job, the evidence is the site record.

**Why not a SlingLoad:** story.md §7.5 says "the load is a MassItem at the hook position,
420 kg" for the blade pair, which is the one sling load that matters for the finale. The
generator is a side trade, not a flight challenge. Modelling it as a physical load would
require carrying it back (new contract completion logic, cable physics for a different mass),
but the narrative payoff is the wreck position, not the flight. Simple is right.

**Why fix BuildTalkContext here:** the bug silently prevented every knowledge-gated dialogue
line in the corpus from firing. Bel's trade depends on knowledge gates; fixing them was a
prerequisite, not a separate task. The fix unlocks all D-089 reward trades and the entire
thread progression for all nine story NPCs.

**Reversibility:** high. Remove `ContractKind.Lift`, `StoryContract()`, the
`bel.thread.traded`/`traded.found` lines, `Knows.WreckPosition`, `BelTradeTests.cs`, and
the Program.cs registrations. The `BuildTalkContext` fix and `Fitted()` prefix should be
kept — they fix a real bug and are used by all NPCs, not just Bel.
