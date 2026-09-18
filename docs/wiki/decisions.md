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
