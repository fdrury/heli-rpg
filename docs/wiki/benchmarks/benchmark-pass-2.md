# Benchmark pass #2 — where ROTORWASH sits now

*Written 2026-09-19 against the current build (`STATUS.md`, all simlab tests passing, all
headless tests passing). The first benchmark round (2026-09-18) fed D-005a, D-003a/b/c
and D-006a. This pass re-evaluates the same comparator set across every dimension the game
now has systems for, identifies what has moved, and names the gaps that remain.*

---

## Bottom line

**The game has crossed a structural threshold that the first benchmarks could not have
predicted: it now has more working systems than most of its comparators had at the same
stage of development, and two of them — audio synthesis and physically grounded
progression — are genuinely novel in the genre.** But the gap the first round warned
about has not closed: there is no mission structure, no reason to go anywhere except
curiosity, and no arc. Every comparator that shipped successfully had some form of
directed purpose by the time it had this much machinery. The audio, the weather, the
combat, the save system, the map — all of it is infrastructure for a game that does not
yet have a plot.

The specific risk is not "will it be fun" — the flight model, the threat system and the
refit loop already produce emergent fun in testing. It is that **a player who launches the
game today has no answer to "what should I do next?"** after the first settlement visit.
Kenshi gets away with this by being a squad management sim where the sandbox *is* the
game. ROTORWASH is a narrative RPG about searching for someone; without that search,
the helicopter is a toy with nowhere important to go.

The other finding: **audio and visual feel have moved from "placeholder" to "competitive"
since the last benchmarks, and in one case (audio) to "ahead of the field."** This matters
because the first benchmarks were rightly sceptical about whether a solo project could
compete on presentation. It can, in the dimensions it has chosen — procedural, physical,
synthesised — and those happen to be the dimensions AAA is worst at faking.

---

## 0. Comparator set

The same games as the first round, plus STALKER 2 (shipped Nov 2024) and MSFS 2024
(shipped Nov 2024), both of which are now relevant.

| Game | Why it is here |
|---|---|
| **Fallout: New Vegas** | The tonal and structural target. Post-apocalypse, faction RPG, dry humour, meaningful choices |
| **S.T.A.L.K.E.R. 2** | Post-apocalypse, gear progression, hostile world, dynamic AI. Shipped since last benchmark |
| **Subnautica** | Knowledge + gear progression, exploration-driven, single vehicle, solo dev origin |
| **Death Stranding** | Vehicle-centric traversal, hostile terrain as the obstacle, infrastructure as progression |
| **Outer Wilds** | Knowledge-only progression, exploration as the game, no combat, handcrafted world |
| **Elite Dangerous** | Vehicle-is-the-game, refit progression, procedural world, mission boards |
| **Kenshi** | Emergent sandbox, no quests, gear progression, solo dev, hostile world |
| **Far Cry 2** | Open-world contract system, dynamic weather, vehicle traversal, hostile environment |
| **Metro Exodus** | Semi-open post-apocalypse, weather, scavenging, tight narrative over open spaces |
| **MSFS 2024** | Career mode over a flight sim. The only shipped game that answers "what if a flight sim had missions?" |
| **Skyrim** | The open-world RPG baseline. Radiant quests, use-based skills, enormous content volume |

---

## 1. Audio

This is where the project has moved the most relative to the field.

### 1.1 What the comparators do

| Game | Audio approach | Dynamic? | Rotor/engine sound |
|---|---|---|---|
| **New Vegas** | Samples + Wwise layers | Ambient reacts to location | n/a |
| **STALKER 2** | Samples + binaural 3D spatial | Ambient reacts to weather, anomalies | n/a |
| **Subnautica** | Samples + FMOD layers | Depth-reactive, creature proximity | Vehicle hum from samples |
| **Death Stranding** | Samples + bespoke score integration | Weather affects mix | Vehicle samples, cross-faded |
| **Elite Dangerous** | Samples + procedural layering | Thruster response, supercruise | Thruster cross-fades |
| **MSFS 2024** | Samples recorded from real aircraft | Per-aircraft sample packs | Sample-based, RPM-crossfade |
| **DCS UH-1H** | Extensive multi-sample layering | RPM, collective, speed bands | Best sample-based helo audio shipped |

**The standard approach across the entire field is sample-based.** Even DCS, the study sim,
cross-fades between recordings. No shipped game synthesises its helicopter audio from the
physics state.

### 1.2 Where ROTORWASH is

**Fully synthesised, zero samples.** The audio is generated live from the flight model's
own state variables:

- **Blade-pass frequency** tracks rotor speed. Measured at 10.73 Hz against 10.79 Hz
  expected, and it follows the rotor down through autorotation to 70% Nr (D-035).
- **Blade slap** is loudest in a loaded turn, which is physically correct — it emerges
  from the same aerodynamic state that produces the turn, not from a trigger.
- **Turboshaft** is driven by N1 and fuel flow, not by collective position.
- **The whole thing was verified spectrally** — the blade-pass sits at the frequency the
  physics predicts.

The simlab `audio` test renders a 126-second sortie to WAV and reports: peak 0.503,
RMS 0.0766, 0.00% clipping. Cold start, spool, hover, climb, cruise, loaded turn, engine
failure, autorotation — all from one continuous synthesis.

### 1.3 Assessment

**Ahead of the field, with a caveat.** No comparator synthesises vehicle audio from
physics. The approach eliminates listener fatigue from sample loops, makes every flight
sound different because it *is* different, and makes failure states (engine loss, rotor
decay) automatic rather than triggered. The caveat: no human has confirmed it sounds
*good* — spectral correctness is not the same as perceived quality. The sortie WAV is
waiting for Fred's ears, and that is the only test that matters.

**Score: 8/10** (marked down from 9 because unvalidated by a listener; marked up from the
field because the technical approach is genuinely novel and the spectral measurements land).

---

## 2. Graphics / world feel

### 2.1 What the comparators do

| Game | Weather | Time of day | Procedural variety | Art direction |
|---|---|---|---|---|
| **New Vegas** | Dust storms, rain (cut) | Full day/night, moonlight | None | Authored environments, iconic wasteland palette |
| **STALKER 2** | Dynamic storms, rain, fog, thunderstorms | Full day/night | Minimal; hand-placed | UE5 Lumen/Nanite, photogrammetry |
| **Subnautica** | Surface weather only | Day/night underwater light | Biome-based coral/flora | Stylised, readable, colour-coded depth |
| **Death Stranding** | Timefall (narrative weather), snow, rain | Full day/night | Procedural terrain deformation | Photorealistic, Decima engine |
| **Metro Exodus** | Sandstorms, thunderstorms, dynamic | Day/night per region | Minimal | Semi-open, cinematic |
| **Far Cry 2** | Dynamic fire propagation, storms | Full day/night | Grass/fire procedural | Realistic African savanna |
| **MSFS 2024** | Real-world weather injection | Real-world time | Satellite terrain + autogen | Photorealistic |

### 2.2 Where ROTORWASH is

- **Weather:** dynamic overcast, haze, variable visibility, storms with lightning, thunder,
  windscreen rain streaks. Rain intensifies with storm severity. Sky darkens to bruised
  green-grey in storms (D-047).
- **Time of day:** moving sun with dawn/dusk colour, full darkness.
- **Night lighting:** navigation lights (red/green/white), anti-collision beacon, landing
  light with ground illumination.
- **Procedural variety:** five building archetypes (D-042) with windows, doors, chimneys,
  ridge vents, loading docks, collapsed variants. Procedural rocks, dead trees, scrub,
  forest noise. 153 road segments, water surfaces.
- **Airframe wear:** object-space shader — sun-bleaching, grime, panel lines, repair
  patches, soot from damage. No UVs.
- **Two lighting paths** (D-003): baked + realtime GI, both verified working.
- **LOD:** four levels, edge-stitched (no skirts).

### 2.3 Assessment

**Competitive for the scope, with the right trade-offs.** The weather system, airframe
wear shader and building variety are at or above the indie standard. The procedural
approach means variety scales without authoring cost. The gap is photorealism — STALKER 2
and Death Stranding are on another planet in raw rendering quality, but they are also
60-person-year art pipelines. The relevant comparison is solo/small-team games like Kenshi
and Subnautica, against which the visual quality is strong.

The building variety pass (D-042) deserves specific note: five archetypes with windows,
doors, chimneys, collapsed variants and sheds produce a settlement that reads as a place
from 500 m. This is better than Kenshi's building variety and comparable to early-access
Subnautica's base aesthetics.

**Score: 6/10** (competitive for solo dev; weather and procedural variety are strengths;
raw rendering fidelity is not, and that is the correct trade for this project).

---

## 3. Progression

### 3.1 What the first benchmark found

D-005a identified the gap: the rare region-opening unlocks were well designed, but
there was no dense substrate of small rewards and no progress screen. The kneeboard was
prescribed as the fix.

### 3.2 What has been built since

- **Kneeboard** with four pages (TAB, then D/E to cycle): flight instruments, threat
  status, fitted modules with empty bays shown, and a full map page.
- **Fog of war** on the map (128×128 grid, 500 m reveal radius, persisted through save).
- **Eight refit bays** — each a real module with mass, position, drag and capability.
  Empty bays are shown on the kneeboard, telling the player what to look for.
- **RWR logs every emitter that paints you** — the bad sortie pays out (D-005a §4).
- **Site discovery** through flight and dialogue — visited and learned sites appear on
  the map.
- **Save/load** preserves all progression state.

### 3.3 Comparison update

| | First benchmark (D-005a) | Now |
|---|---|---|
| Progress screen | **Missing** | Kneeboard MAP + FITTED pages |
| Small-reward cadence | **Missing** | Fog reveal (continuous), site discovery, RWR intel, module finds |
| Region-opening unlocks | Designed, not built | **Built**: RWR, chaff, flares, exhaust suppressor each open a threat class |
| "Am I further along?" | No signal | Map reveals, empty bays fill, threat circles visible |

**The cadence gap from D-005a is substantially closed.** Flying itself now produces
rewards: the fog lifts, sites appear, threat intel accumulates. The kneeboard shows it.
The refit system gives the map a reason to exist — you need to know where the wrecks are
to find the modules.

What remains missing from D-005a's prescription:
- **Contacts and frequencies as discoverable items** — designed but not yet populated
  beyond the first NPC.
- **The "instruments are items" concept** — the HUD does not yet physically grow as
  modules are installed.
- **Numeric deltas on acquisition** — "your hover ceiling just dropped 15 m" is not
  shown to the player.

**Updated score: 7/10** (was 5/10 at first benchmark; the structure is now comparable to
Subnautica's PDA-driven progression, missing the content volume).

---

## 4. Story / mission structure

### 4.1 What the comparators do

| Game | Mission source | Structure | Arc |
|---|---|---|---|
| **New Vegas** | Hand-authored quests, faction reputation | ~130 quests, branching main quest | Courier's revenge → Hoover Dam |
| **STALKER 2** | Hand-authored + emergent events | Main quest + side contracts | Zone exploration → heart of Chornobyl |
| **Subnautica** | Environmental storytelling + radio messages | Breadcrumb trail of beacons | Crash → cure → escape |
| **Death Stranding** | Delivery contracts + main missions | Standard Orders (contracts) + story | Reconnect America |
| **Outer Wilds** | Self-directed exploration | No quests; rumour map tracks knowledge | Understand → prevent the supernova |
| **Elite Dangerous** | Procedural mission boards | Fetch/deliver/kill contracts | **None** (sandbox) |
| **Kenshi** | **None** | Emergent sandbox | **None** (make your own) |
| **Far Cry 2** | Faction contracts + buddy missions | ~30 main missions + side contracts | Assassinate the Jackal |
| **Metro Exodus** | Linear story across open zones | Scripted encounters in semi-open areas | Train journey east |
| **MSFS 2024** | Career mode: generated contracts | Cargo, flightseeing, medical, firefighting | Licence progression (student → airline captain) |
| **Skyrim** | Radiant quests + hand-authored guilds | ~270+ quests | Dragonborn prophecy + guild arcs |

### 4.2 Where ROTORWASH is

**There is no mission structure.** The player can fly, land, talk, scavenge, refit, and
fight. The core loop closes — but it closes in a circle, not along an arc. There is no
"go here because someone asked you to." There is no journal entry. There is no contract
board. There is no reason to visit a particular settlement except that it is there.

The vision document describes a search for a specific person — "you have a name, a partial
frequency, and a route they were last flying." None of this is implemented.

### 4.3 What the comparators teach

Three patterns emerge from the comparison set:

**Pattern 1: Authored quest lines (New Vegas, STALKER 2, Metro Exodus, Skyrim).** High
content cost. A solo developer cannot produce 130 branching quests. This is not the path.

**Pattern 2: Contract boards (Elite, MSFS 2024, Far Cry 2, Death Stranding).** Procedural
or semi-procedural task generation. Low per-mission authoring cost. Gives each flight a
purpose. **This is the pattern that fits ROTORWASH.** Specifically:

- **MSFS 2024's career mode** is the closest structural analogy: a flight sim with
  generated missions (cargo, medical, search). It proves the model works for vehicle-sim
  games. Its licencing progression (student → commercial → airline) maps loosely onto
  ROTORWASH's module-based capability growth.
- **Elite Dangerous's mission boards** show the minimum viable version: go here, bring
  this, kill that. Each takes one flight. It works because the *flying* is the game, and
  the mission is the reason to fly.
- **Far Cry 2's contract system** is the best fit tonally: a fixer gives you a target,
  you choose how to get there, and the world pushes back. The buddy system adds a second
  opinion on every job. 30 missions is achievable scope.
- **Death Stranding's Standard Orders** prove that delivery-as-gameplay can carry 40+
  hours if the traversal is good enough. The traversal here is good enough.

**Pattern 3: Emergent sandbox (Kenshi).** No authored content at all; the game generates
situations from interacting systems. Works for Kenshi because it is a squad management
sim where building and defending a base is the game. Does not work for a narrative RPG
about a search.

### 4.4 Assessment

**This is the critical gap. Score: 2/10.** The infrastructure for missions exists (sites,
NPCs, dialogue, save/load, map), but no mission system uses it. Every comparator that
shipped as a narrative game had directed purpose by this stage of development.

The minimum viable mission system for ROTORWASH, informed by the comparators:

1. **A contract board at settlements.** NPCs offer tasks: deliver cargo, survey a location,
   recover a part, relay a message. Each takes one sortie. Generated from site data and
   world state, not hand-authored.
2. **A breadcrumb for the main search.** Subnautica's radio messages are the model: an
   occasional signal, a bearing, a name, a clue that points the player to the next place.
   10-15 authored beats, spaced across the game.
3. **A journal page on the kneeboard.** Active tasks, completed tasks, the search thread.
   The kneeboard is already the progress screen; it needs a "what to do next" page.

This is the next thing to build, and it is where the project will live or die as a game
rather than a tech demo.

---

## 5. World size and content density

### 5.1 What the first benchmark found

D-003b shrunk the content envelope to 13 km (169 km²) inside a 16.4 km terrain grid.
124 named POIs at 0.73/km². Route inflation via threat envelopes measured at 1.63x.

### 5.2 What has been built since

- **144 named places** across 8 regions (exceeding the 124 target).
- **Threat envelopes** with five classes and terrain masking, measured and verified.
- **Route inflation** confirmed: a 14 km SAM between two points 29 km apart forces a
  47.2 km detour — 1.63x on flat ground, more with terrain.
- **Fog of war** makes the map itself a discovery system.
- **Roads** (153 segments connecting 80 sites) provide visual infrastructure.
- **Water surfaces** in the deepest valleys.
- **Forest** driven by moisture noise on lower ground.

### 5.3 Assessment

**On track. Score: 7/10.** The world-scale decisions from the first benchmark have been
executed. The terrain, sites, roads, water and forests are all in place. The threat system
provides the route inflation the first benchmark demanded. The missing pieces are content
depth at each site (most are interchangeable) and the mission system that gives the player
a reason to fly between them.

The content-hours-per-km² metric from the first benchmark cannot be re-evaluated until
missions exist, because flight time alone does not constitute content hours.

---

## 6. Combat

### 6.1 What the comparators do

| Game | On-foot combat | Vehicle combat | Slow-motion |
|---|---|---|---|
| **New Vegas** | Hitscan + projectile, VATS | Minimal (turrets) | VATS freezes time |
| **STALKER 2** | Projectile, cover, AI flanking | None | None |
| **Far Cry 2** | Hitscan, fire propagation, buddies | Vehicle-mounted weapons | None |
| **Metro Exodus** | Hitscan + projectile, stealth, scavenging | Minimal | None |
| **Kenshi** | Auto-combat, use-based skills | None | None (pause) |
| **Skyrim** | Melee + magic + ranged, perks | Dragon mounts (late) | **Slow Time shout** |

### 6.2 Where ROTORWASH is

- **On-foot:** hitscan sidearm (6-round revolver, 18 total), seven body zones per hostile
  NPC (head, torso, arms, legs, weapon), zone-specific effects (D-016).
- **Rotor Time:** world slows to 0.3x, HUD projects diamond markers on each zone with
  labels, charges during committed flight. One mechanic in two contexts (ground and air).
- **Threat system (air):** five threat classes, countermeasures, terrain masking. Not
  direct combat but threat avoidance — the game's equivalent of air combat.
- **Damage model:** nine aircraft components, multiplier-based effects felt in flight.

### 6.3 Assessment

**Functional, purposeful, appropriately scoped. Score: 6/10.** The called-shot system
with zone effects is more mechanically interesting than most comparators' on-foot combat.
Rotor Time as a single mechanic across air and ground is elegant. The threat system
as "air combat by avoidance" is the right design for a game about the last helicopter —
you don't dogfight, you survive.

What it lacks relative to the comparators: enemy variety (one hostile NPC type), stealth
options, and ranged engagement distance (the revolver is a sidearm, not a rifle). These
are content gaps, not systems gaps — the zone system supports any weapon and any enemy.

---

## 7. Save / load and state depth

### 7.1 What the comparators do

Most comparators save position, inventory, quest state and some world state. Few preserve
the kind of deep mechanical state ROTORWASH tracks.

### 7.2 Where ROTORWASH is

The save captures: fuel, damage (9 components), loadout (installed + bag), progress
(inventory, knowledge, site records, journal), NPC minds (memory, dialogue usage), combat
state (HP, ammo, Rotor Time charge), threat intel (RWR detection flags), fog of war grid,
and position. 14 properties verified through the headless `--savetest`.

### 7.3 Assessment

**Thorough. Score: 8/10.** The save system preserves more mechanical state than any
comparator except Elite Dangerous (which saves to a server). NPC memory surviving across
saves is above the standard. The state that is *not* saved — rotor RPM, engine
temperature, governor state — is correctly excluded because the game restores with the
engine off, so none of it exists.

---

## 8. Dialogue

### 8.1 What the comparators do

| Game | Dialogue approach | NPC memory | Dynamic content |
|---|---|---|---|
| **New Vegas** | Fully authored, branching trees | Per-quest flags | None |
| **STALKER 2** | Authored, contextual barks | Faction reputation | Location-aware barks |
| **Subnautica** | Radio messages, PDA logs | None | None |
| **Outer Wilds** | Authored, state-aware | Tracks what you know | Dialogue changes with knowledge |
| **Elite** | Template-filled mission text | None | Procedural names/locations |
| **Kenshi** | Minimal authored lines | None | None |

### 8.2 Where ROTORWASH is

- **Baked corpus** with state-conditioned selector (D-015). Seven arrival states produce
  seven different openings.
- **Local SLM coda** (Qwen3-1.7B, D-006a). The baked line answers; the coda notices
  (fuel, damage, loadout, time since last visit).
- **NPC memory** across visits, persisted through save/load.
- **Graceful degradation:** no model → baked lines only, nothing lost.
- **Validation:** 12/12 adversarial cases correctly filtered.

### 8.3 Assessment

**Structurally novel, content-light. Score: 7/10.** The architecture is better than any
comparator's — real memory, state-aware selection, validated local model. The problem is
corpus size: 25 lines and one named NPC. New Vegas has thousands of authored lines across
dozens of NPCs. The system can carry that volume; it does not yet have it.

---

## 9. Summary scorecard

| Dimension | First benchmark | Now | Trend | Critical? |
|---|---|---|---|---|
| **Audio** | not assessed | **8/10** | — | No (strength) |
| **Graphics / world feel** | not assessed | **6/10** | — | No (appropriate for scope) |
| **Progression** | 5/10 | **7/10** | ↑ | No (gap closing) |
| **Story / missions** | not assessed | **2/10** | — | **YES** |
| **World size** | 6/10 | **7/10** | ↑ | No (on track) |
| **Combat** | not assessed | **6/10** | — | No (appropriate) |
| **Save / load** | not assessed | **8/10** | — | No (strength) |
| **Dialogue** | 4/10 (architecture only) | **7/10** | ↑ | No (needs content, not systems) |
| **Flight model** | 8/10 (per separate benchmark) | **8/10** | → | No (strength) |

**Overall: the game is a 6.6/10 across nine dimensions, held down by a single 2/10.**
Fix the mission gap and the average rises to 7.4, which is competitive with early-access
games in the genre.

---

## 10. Recommendations

### 10.1 Build mission structure next

This is not optional. The benchmark unanimously identifies it as the only structural gap.
The contract-board model (Elite, MSFS 2024, Far Cry 2) fits the game's existing systems
and can be built from site data and world state without hand-authoring hundreds of quests.
See §4.4 for the minimum viable version.

### 10.2 Do not chase graphics

The project is correctly positioned: procedural variety, weather, and airframe wear
carry the visual identity. Chasing photorealism against STALKER 2 or Death Stranding is
a losing trade. The win is in consistency — the art direction should be *coherent*, which
procedural systems are good at, not *photorealistic*, which requires artists.

### 10.3 Audio needs one human listener

The spectral verification is strong, but it verifies *correctness*, not *quality*. The
sortie WAV needs Fred's ears. If it sounds good, this is the project's most defensible
differentiator. If it doesn't, the synthesis parameters need tuning, not the architecture.

### 10.4 Progression needs content, not more systems

The kneeboard, fog of war, refit system and threat intel together provide the
"am I further along?" signal that D-005a demanded. What's needed now is more things to
find (modules, contacts, frequencies, charts) and the mission system to direct the player
toward them. The progression infrastructure is complete; the progression *content* is not.

### 10.5 The dialogue system needs a larger corpus

25 lines and one named NPC is a tech demo. The architecture supports hundreds of NPCs and
thousands of lines. Building out the corpus is an authoring task, not an engineering task,
and it should follow the mission system (because NPCs need something to say about the
missions they offer).
