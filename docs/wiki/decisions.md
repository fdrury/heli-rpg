# Decision log (ADRs)

Format: **D-nnn — Title** · *date* · Decision / Why / Reversibility.
Fred can veto any of these; entries marked **[LOCKED-IN BY FRED]** came from him directly.

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

## D-035 — On-foot combat: hitscan sidearm with called shots · 2026-09-19

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

## D-032 — Rain is drawn, and overcast daylight is flat rather than dark

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

## D-033 — A rotor strike is an event, not a state

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

## D-034 — Cockpit lighting belongs to the lights, not the airframe

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

## D-036 — The weather has a voice of its own

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

## D-039 — Kneeboard map with fog of war · 2026-09-19

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

## D-041 — Cockpit head-look with padlock · 2026-09-19

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

## D-042 — Building variety: five archetypes · 2026-09-19

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

## D-040 — Position hold, because speed hold never arrives

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
