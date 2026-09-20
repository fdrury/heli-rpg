# ROTORWASH — status

*Last updated: 2026-09-20*

A single-player post-apocalyptic RPG about the last helicopter pilot in the world.
Godot 4.7.2 (.NET). `docs/wiki/00-vision.md` is what it is; `docs/wiki/decisions.md` is
every choice and why; `docs/wiki/benchmarks/` is where it was measured against the genre.

## Where it is now

**The core loop closes, people talk, the machine levels up, the map fills in, there is
somewhere to go and a reason to get there — and now a voice on the radio that knows what
you did, a world that speaks to you in flight, and a clock on the airframe that makes
every hour count.** You can fly a physically simulated Huey across a streamed archipelago,
find a named settlement, put it down, shut down, talk to whoever lives there, take a
contract from the board, fly it, and come back for the payout. The main search — 12
authored beats about finding Sera Wray, a flight engineer, and a set of matched blades —
gives the long-term pull; the contracts give the per-sortie purpose. The main rotor's
repair ceiling falls with flight hours, so the aircraft you fly in hour fifteen genuinely
shakes and genuinely will not hold a hot hover — and new blades are the one thing that
fixes it. Eight module bays, each felt in the flight model. The kneeboard's six pages show
aircraft condition, knowledge, journal, map, active jobs, and the search thread — the
four-leg ferry route that is the arc's spine.

**The world is a ring around a citadel** (D-087), 28.4 km across. Seven islands sit on a
10.5 km ring; **The Scald** is at the hub - Act III, the sealed magazine, the blades - and
it carries the radio transmitter, so the station covers the whole world and is the last
place the player can reach. Every inter-region leg is a committed crossing of 4.7 to 8.1 km
against a 2:1 glide that buys about a kilometre from 500 m; the home island (The Pan and
Long Acre joined) stays dry so there is somewhere to learn. Hexagonal geometry makes a hop
to the centre and a hop round the ring the same length, so the citadel is gated by air
defence rather than by distance. A dedicated defence ring (D-088) — 3 SAMs, 4 MANPADS,
3 guns, 2 search radars in concentric rings — makes the centre genuinely lethal at every
altitude band. A straight crossing produces 113 hits; the gun pod can dismantle it.

60 fps at 1600x900 on a GTX 1650 Ti, which is well under the GTX 1080 target.

## Run it

```
# the game
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game

# headless tests (flight + loadout + combat + encounter + save + contract + fog) - no engine needed
dotnet run --project tools/simlab -c Release -- all

# render a 126 s sortie to builds/audio/sortie.wav and listen to it
dotnet run --project tools/simlab -c Release -- audio

# verify the physics survives Godot's solver
godot --headless --path game -- --selftest

# fly the actual game loop and check it works
godot --headless --path game -- --looptest

# on-foot combat: land, dismount, shoot NPCs, called shots with Rotor Time
godot --headless --path game -- --combattest

# save at a site, trash state, load, verify round-trip
godot --headless --path game -- --savetest

# survey the generated world: terrain statistics, site placement, coverage
godot --headless --path game -- --worldreport

# measure how much cover the terrain actually gives against each emitter
godot --headless --path game -- --threatreport

# the standard screenshot set, for comparing the look over time
godot --path game -- --screenshot
```

Controls: `W`/`S` or throttle = collective · arrows or stick = cyclic · `A`/`D` or twist =
pedals · `TAB` kneeboard · `1`-`4` actions · `Z`/`X` chaff/flares · `LMB` gun (requires
module) · `RMB` Rotor Time · `C` camera · `F2` stability augmentation (requires module) ·
`R` respawn · `F5` save · `F9` load.

Cockpit look: middle mouse + drag or hat/D-pad or numpad 4/6/8/2 · numpad 5 or Home = centre ·
`L` padlock nearest threat/site · release = spring return to forward.

On foot: `WASD` move · `Shift` sprint · `LMB` fire · `RMB` Rotor Time · `R` reload ·
`F` board Hugh.

## Done

**Flight** — blade-element rotor with per-blade flapping, dynamic inflow, vortex ring
state, ground effect; turboshaft with governor, torque limits, density lapse and a
freewheel that makes autorotation emerge rather than be scripted; tail rotor; lifting
surfaces; full inertia tensor from mass items. Hover power, power curve, autorotation and
hover ceiling all land within a few percent of a real UH-1.

**Landing** — site assessment (slope, roughness under the skid footprint, rotor clearance,
surface), touchdown grading against real skid-gear limits, dynamic rollover from pure
geometry, rotor strike detection, and brownout that blinds you in the last ten metres and
clears the moment you fly out of your own dust.

**Damage** — nine components, every effect a multiplier on something the flight model
already computes. A 55% engine with a 70% gearbox cannot hold an out-of-ground-effect
hover. Losing the tail rotor is two full turns in six seconds.

**World** — 16.4 km of streamed terrain at four levels of detail, LOD seams fixed by edge
stitching rather than skirts; procedural rocks, dead trees and scrub; green woodland
on lower wetter ground driven by forest noise; 153 road segments connecting 80 sites
in a single batched mesh; water surfaces in the deepest valleys; 144 named places
across 8 regions, placed against the terrain so relays sit on ridges and settlements sit
on flat sheltered ground.

**Audio** — synthesised live from the flight model. No samples. Verified spectrally: the
blade slap sits at the blade-pass frequency the physics predicts and is loudest in a
loaded turn. The announcer's voice is a formant-based murmur through a radio band-pass
(D-078): nobody understands the words, but the rhythm and timbre read as a man on the
radio. Carrier hiss between sentences. Five synth engines total: rotor, weather, warnings,
voice, ambient. Terrain-driven reverb sizes the space (valley, open, over water) and
every site kind has its own voice — generators at settlements, creaking metal at wrecks,
transformer hum at relays — so landing and shutting down tells you where you are (D-080).

**Threat** — five classes, each owning an altitude band, with real terrain masking: a ray
walked from every emitter to the aircraft through the actual height field. A SAM cannot see
you below 120 m AGL at all; the aerostat looks down and ignores the dead ground everything
else misses. Chaff takes a SAM from 7 hits to 0 and does nothing to a heat seeker; flares
the reverse. 45 s in two envelopes: everything damaged, 62% forced down, none deleted. The
RWR logs every emitter that paints you, so the sortie that nearly killed you pays out.
The Scald carries a dedicated citadel defence ring (D-088): 12 emitters in three concentric
layers — SAMs at 2 km, MANPADS at 1.5 km, guns at 0.8 km, search radars at 3 km — that
make the centre lethal to overfly at every altitude band. Fixed angular positions mean the
layout is deterministic and learnable; the gun pod (D-081) can destroy them one at a time.

**Dialogue** — a baked corpus conditioned on state, with a selector that prefers the most
specific match, plus the local-model coda contract: gated on whether the spoken line covers
the measured latency, committed a whole sentence at a time, and validated against invented
quests, questions and anachronisms. NPCs remember across visits, which the research found is
where the perceived magic actually lives. The llama-server subprocess is wired up behind a
Windows Job Object for crash safety, the conversation UI draws in the HUD aesthetic with
word-by-word reveal, and everything degrades to the baked layer when the model is absent.
**Dialogue rewards** (D-089): when an NPC says "the crate is yours", the module or knowledge
is actually granted. Nell Abergale trades the RWR at standing ≥ 0.3, Osie Crane gives the
upland terrain-masking chart freely, Halvard Ferren trades the emitter-location chart. Module
rewards follow the salvage path (bag, then install at a workshop); knowledge rewards call
`Progress.Learn`. Both are idempotent and persist through save/load. One simlab test verifies
reward attachment, condition gating, catalog references, and idempotency.
**Knowledge-gated dialogue now works** (D-092): `BuildTalkContext` populates `KnownIds` from
`Progress.AllKnown` and `FittedIds` from `Loadout.Installed`, so every `Requirement.Knows()`,
`Unknown()`, and the new `Fitted()` prefix correctly gate dialogue on what the player knows
and what is bolted to the aircraft. Previously these fields were unpopulated and all
knowledge-gated dialogue silently failed to fire.

**Passengers** — Sera Wray in the right seat (D-090, story.md §7.6). `Passenger` is a
record in sim/ holding identity, mass and seat position; `CopilotCallouts` generates
contextual radio messages based on flight state. Wray is 68 kg in the right seat at
`(2.00, 0.62, -0.25)`, mirroring the pilot; the CG shifts measurably and the trim changes.
She calls torque (>85%), Nr (<95%), altitude (descending below 120 ft), fuel (<25%, <10%),
and threat engagement — short diagnostic readings, not encouragement, because that is what
a flight engineer does. Callouts appear on the radio strip in warm amber, distinct from all
other message kinds. Boarding is triggered by a `DialogueRewardKind.Passenger` reward on the
`wray.board` dialogue line, gated on Window knowledge + standing ≥ 0.5 + at least two
meetings. `Progress.PassengerAboard` persists through save/load. The NPC restore path now
uses `DialogueCorpus.Named()` for all nine story characters, not just Mattie — a pre-existing
bug that would have given named NPCs generic settler dialogue after load. Eight simlab tests
verify mass, CG shift, all five callout categories, save round-trip, and dialogue gating.

**Blade pair sling load** — the 420 kg finale load on the cargo hook (D-091, story.md
§7.7). `SlingLoads.BladePair()` creates a named SlingLoad configuration: 420 kg (two
blades at 145 kg each plus grips and tie bars), 5 m cable, 2.0 m² drag area. The game
layer tracks the load via `Progress.SlingLoadId`, persisted through save/load;
`SyncSlingLoad()` in Main.cs attaches/detaches the load on the `Helicopter.Hook` when
triggered by a `DialogueRewardKind.SlingLoad` reward or on load. The hook module must be
installed. The measured cost: +0.029 collective, +90 kW power, -1250 m hover ceiling at
sea level. The **finale feasibility scenario** flies the worst plausible case — 220 h
rotor (ceiling 0.58), ISA+10, Wray aboard (68 kg), hook module (28 kg), blade pair
(420 kg), 300 kg fuel — from sea level to 120 m, cruises 10 km at 15 m/s, and arrives
with 265 kg fuel remaining. The finale closes. Five simlab tests: catalog, trim, ceiling,
save round-trip, and the finale scenario.

**Play** — refuel, repair, salvage, survey, tune a relay, ask around, contract board.
Carried load is real mass and is felt in the hover. The kneeboard records facts, not
inferences, and shows the empty bays.

**Refit** — eight modules on real hardpoints (D-011). Each is a MassItem plus drag and
fuel capacity, so the flight model feels every choice. Modules are found during salvage
at wrecks, airfields and depots (~15% of eligible sites), then installed at workshops or
airfields for a parts cost. Countermeasures (RWR, chaff, flares, exhaust suppressor)
and the attitude hold unit are no longer toggled with keys — they are things you find
and bolt on. The kneeboard's FITTED section reads from real loadout state, and empty
bays show the player what to look for.

**Weather & night** — dynamic weather (overcast, haze, variable visibility, storms), a
moving sun with dawn/dusk, full darkness with landing light and navigation lights (nav,
anti-collision beacon, landing light). Storms darken the sky to a bruised green-grey,
intensify rain, fire lightning flashes with synthesised thunder, and draw procedural rain
streaks on the windscreen from inside the cockpit. Two lighting paths (baked + realtime GI)
per D-003.

**On-foot** — third-person character controller (WASD + mouse look, sprint) with
dismount/board transitions at shut-down helicopters. Rotor Time: the vision doc's
slow-motion mechanic charges during committed flight and drains on activation, slowing
the world to 0.3x for called shots while the pilot moves at full speed. `F` to
dismount/board, `Q` to activate RT. Terrain floor detection is analytical (WorldHeight.At)
because CharacterBody3D.MoveAndSlide does not work with ConcavePolygonShape3D in
Godot 4.7. The foottest exercises the full cycle: fly, land, shut down, dismount, walk,
activate/drain Rotor Time, walk back, board.

**Combat** — hitscan sidearm (revolver, 6 rounds, 18 total) with called shots through
Rotor Time (D-016). Seven body zones (head, torso, arms, legs, weapon) as StaticBody3D
collision shapes on each hostile NPC; a ray from the camera determines what was hit.
Head → instant down, torso → wound, arm → accuracy loss, leg → immobilise, weapon →
disarm. During Rotor Time the HUD projects diamond markers onto each zone with labels,
highlighting whichever is under the crosshair. The pilot has 100 HP (three hits and
you're down, recover at Hugh). Hostile NPCs detect, face and fire back with a reaction
delay; zone effects stack (two arm hits make them nearly useless, one leg hit pins them
in place). Ammo counter, pilot health bar, red vignette damage flash, and shot-feedback
text all draw in the existing HUD aesthetic. The combattest exercises the full cycle:
fly, land, shut down, dismount, spawn NPC, hip fire, aimed torso shot, Rotor Time
headshot, reload, pilot damage, board.

**Air gunnery** — the vision doc's signature mechanic in flight (D-081). A fixed-forward
M60 gun pod, installed as a module (45 kg, 0.08 m² drag, 3 parts). Aim by pointing the
helicopter's nose; LMB fires at ~550 rpm. During Rotor Time the world slows to 0.3x for
lined-up strafing runs. Each round checks every threat emitter within 800 m using
ray-to-point distance; hits within 15 m reduce emitter health by 0.12 per round (~9 hits
to destroy). A destroyed emitter stops tracking, scanning, and engaging — the RWR goes
quiet. Turns the threat layer from a wall into a target. The gun pod appears in site
distribution and is installed at workshops like any other module. HUD shows a crosshair
(widened during RT), ammo bar, and hit/destroyed feedback. Gun rounds and emitter health
persist through save/load. Six simlab tests: ammo, ray geometry, emitter damage,
destroyed-stops-tracking, save round-trip, module catalog.

**Hostile site encounters** — combat integrated into the world (D-060). Wrecks, depots
and airfields in tier 1+ have a seed-based chance of being guarded by scavengers;
farmsteads join the hostile pool at tier 2+. Tier 0 (the Basin) is always safe —
settlements, workshops, relays and overlooks are never hostile. Dismounting at an
uncleared hostile site spawns 1–4 NPCs in a ring around the site; downing all of them
clears the site permanently. The HUD site panel shows "HOSTILE" or "CLEARED" next to the
site kind, and the kneeboard map marks hostile sites with a red diamond outline (grey when
cleared). Cleared state persists through save/load. `Encounter` lives in sim/ as a pure
.NET class; six simlab tests verify tier-0 safety, safe kinds, hostile rate, NPC counts,
determinism, and cleared round-trip.

**Save / load** — F5 saves, F9 loads. JSON via `System.Text.Json`, single slot,
human-readable. Save is gated: on ground, shut down, at a site, not in dialogue. The
save captures everything that matters — fuel, damage, loadout (installed + bag), progress
(inventory, knowledge, site records, journal), NPC minds (memory, dialogue usage), combat
state (pilot HP, sidearm ammo, Rotor Time charge), threat intel (RWR detection flags),
and position — and restores it with the engine off and rotor stopped, so none of the
complex rotor/engine internal state needs serialising. `SaveData` lives in sim/ as a pure
.NET DTO with no Godot dependency; the game layer handles position, NPC registry and
threat state. Five simlab round-trip tests (progress, damage, loadout, NPC, full) and a
headless Godot test (--savetest) that flies to a site, saves distinctive state, trashes
everything, loads, and verifies fourteen properties survived the round trip.

**Map** — fourth kneeboard page (TAB, then E to page 3). Fog of war reveals the map as
you fly; sites appear as coloured diamonds when visited or learned about through dialogue.
Threat envelopes for detected emitters are drawn as red translucent circles. Aircraft
position and heading shown as a white chevron. Terrain is a topo-coloured heightmap,
matching the military-chart aesthetic. The fog grid is 128×128 (~102 m cells), reveal
radius 500 m, persisted through save/load. Scale bar and survey-percentage readout.
`FogOfWar` lives in sim/ (pure .NET, no Godot dependency) with byte-array serialisation;
the game layer generates terrain and fog textures and draws markers via `_Draw()`.
Dashed bearing lines from the aircraft to each active contract target let the pilot
plan routes around threat circles.

**Navigation** — heading compass with bearing guidance (D-082). A horizontal compass strip
at the top of the HUD, centred on the aircraft's heading, showing ±60° of the compass rose
with tick marks every 10° and cardinal/intercardinal labels. Active contract targets appear
as gold chevrons on the strip with name and distance. Off-strip targets get an edge arrow
pointing in their direction. Requires SAS module (D-055: instruments are items). `Navigation`
lives in sim/ (pure .NET, flat-earth geometry); four simlab tests verify the bearing and
distance math.

**Mission structure** — contract board at settlements and main-search breadcrumbs (D-049).
Settlements generate 2-3 contracts from their neighbours: deliveries, scout missions,
recovery jobs, and relay messages. Contracts reference real sites, use real stock types,
and pay in things the game already tracks. The main search is 11 authored beats gated on
world state, telling the story of finding Sera Wray — a flight engineer who signed Hugh's
logbook for nine years and knows where the last serviceable rotor blades are.
Progress, contracts and search stage survive save/load. A fifth kneeboard page (JOBS)
shows active contracts, the search thread's current hint, and completed count. Eight
simlab tests verify generation, completion, payout, delivery logic, round-trip, search
gating, determinism, and full integration.

**Story engine readiness** — place-based beat gates, dialogue at story sites, night
awareness (D-083). Beat gates now check that the player has visited the site whose story
role carries the beat's information (`ThreadContext.Visited`), so "the wreck" cannot fire
while parked at a basin farmstead. Counters remain as floors to prevent stacking. Talk is
available at any site where StoryPlaces declares an NPC, not only at Settlements — story
NPCs at airfields, workshops and depots can be spoken to. `ArrivedAtNight` reads the sun
elevation so night-gated dialogue lines work. `ThreadContext` lives in sim/ (pure .NET);
the game layer populates it from StoryPlaces role bindings.

**Kneeboard THREAD page** — the search arc made visible (D-084). A sixth kneeboard page
shows the four-leg ferry route of SIERRA-FOUR-THREE with open/closed status and closure
day stamps, the airband frequency hunt (N of 34 logged), and the main rotor ceiling as a
percentage of serviceable life with total flight hours since last track. The current hint
is shown at the bottom. No next-objective marker, no completion percentage — facts, not
inferences. `SearchThread` tracks leg closure timestamps; `DamageState.TotalFlightHours`
accumulates in `AccrueFlightHours`. Both persist through save/load. One simlab test
(`search_legs`) verifies leg closure stamps and save round-trip.

**Radio strip** — in-flight text for radio messages (D-085). A two-line word-by-word
reveal strip at the bottom of the HUD carries four kinds of radio call: scheduled
broadcasts (cool blue), directed calls (green), intercepted traffic (warm red), and
co-pilot callouts (warm amber). Messages are suppressed during threat engagement. Beat 5
(`search.voice`) fires the first time the player is airborne during the 06:40 weather
window after learning about the rota — a METAR-style broadcast mentioning knots, the tell
Doss described. This is the moment the game stops being a sandbox: the world speaks to you
in flight. `RadioStrip` lives in sim/ (pure .NET, no Godot dependency); six simlab tests
verify queue management, word-reveal timing, suppression, and the beat 5 gate.

**Rotor ceiling** — the clock on the search (D-086). `MainRotorCeiling` degrades with
total flight hours (0.0019/h, floor 0.55) and caps how far `Repair` can restore the main
rotor. At 94 h the ceiling is 82%; at 240 h it floors at 55% — flyable but rough. New
blades (`ResetRotorHours`) reset it to 1.0, and that is the only event in the game that
does. The kneeboard THREAD page shows the real ceiling and hours since track. Five simlab
tests verify the formula, repair cap, RepairAll cap, reset, and save round-trip.

**Look-around** — cockpit head-look (D-070). In cockpit mode the pilot can look around
inside the airframe: middle-mouse drag, hat switch / D-pad, or numpad 4/6/8/2 slew the
view ±150° yaw, -40° to +60° pitch. Release springs back to forward. `L` padlocks onto
the nearest detected threat emitter or known site. The 3D cockpit instruments (2×3 bezel
array, control sticks, collective levers, pedals) are now visible by looking down; the
side doors and terrain are visible by looking left/right. Two screenshot shots
(`24_cockpit_left`, `25_cockpit_panel`) verify the feature across builds.

**Building variety** — five residential archetypes (D-071): simple gable, L-plan,
lean-to addition, flat-roof parapet, and porch. Each produces a distinct silhouette
visible from 500 m. Windows have dark recesses behind the glass for interior suggestion.
Door openings and chimneys (~35%) appear on standing houses. Sheds gain ridge vents and
loading docks. Collapsed buildings can have a partial wall still standing. Small
buildings (relay shacks) get simple gable or flat parapet only.

**Interior lighting** — windows glow warm amber at night (D-052). An emissive shader
plane behind the glass fades in through twilight, driven by the same daylight fraction
that controls the sun. ~30% of windows stay dark; the rest vary in warmth and flicker
like firelight. No real lights — the emissive surface plus bloom is enough to read as
inhabited from approach altitude. Zero performance cost on a GTX 1650 Ti.

**Airframe wear** — procedural object-space shader on the aircraft body. Sun-bleaching
(upward-facing surfaces fade), grime (undersides darken), panel lines, dirt streaking,
repair patches in a slightly wrong shade, and soot damage driven by the damage model.
No UVs — the wear is computed from position and normal in object space so it stays fixed
to the hull.

**Instruments are items** — the HUD physically grows as modules are installed (D-055,
closing D-005a §2–3). Without the SAS (attitude hold) module, the left panel (airspeed,
radar altitude, vertical speed, heading) and control position display are hidden; a "NO
FLIGHT DATA" placeholder shows instead. Installing SAS populates the entire left side of
the display. When any module is installed or removed, a numeric delta card briefly shows
mass, fuel capacity, drag changes and resulting total weight, so the trade is legible. The
attitude indicator and right panel (Nr, torque, fuel) are always visible.

**Alert state** — regional readiness driven by the threat field (D-061). Being detected
by an emitter raises that region's readiness; being engaged spikes it; readiness decays
with a six-hour game-time half-life. Two effects feed back into the threat system:
`DetectionScale` stretches emitter detection range by up to 25% and `ReactionScale`
halves launch delay at full readiness, so a region you have been through before reacts
faster without redrawing routes the player has already learned. The kneeboard MAP page
shows raised regions by name and phrase. Alert levels persist through save/load. One
new simlab test (`save_alert`) verifies the round trip.

**Contract depth** — contracts that use the world (D-062). The board was sitting beside
the alert and encounter systems rather than using them. Now: Clear contracts ask the
player to remove scavengers from hostile sites (gated on `SiteRecord.Cleared`). Danger
pay scales all rewards by `1 + alertLevel`, so a contract to a hot region pays up to
double. Recovery contracts to hostile sites mention the guards and pay extra. Survey
generation prefers quiet regions, naturally steering exploration toward untouched country.
Brief text appends an alert sentence when the destination region is above 0.15 readiness.
Four new simlab tests: clear generation/completion, danger pay scaling, hostile recovery
briefs, and clear round-trip. The kneeboard shows CLEAR contracts in red.

**Autorotation investigation closed** (D-054). The "glides half as far" gap was three
problems: (1) the autoglide test rig drifted sideways (no lateral channel in the autopilot
demand), inflating drag by 13.5 m² of side area; (2) the 4:1 reference is a rule-of-thumb
including flare — steady-state is ~3.6:1; (3) a real ~20% physics gap remains (profile power
at 9–12% stall fraction, compressibility at tip Mach 0.81). With the rig fixed and reference
corrected, best autoglide is 2.66:1 and trimmed glide is 2.91:1 — 20% short, not 50%.
ConingInflow (U_P on the coned disc) was tried two ways and both failed: full β(ψ) diverges
between integrators, mean β₀ drops the beneficial cross-term and regresses the trim.

**Benchmark pass #2** — `docs/wiki/benchmarks/benchmark-pass-2.md`. Compared against
eleven games across nine dimensions. Audio synthesis (8/10) and save state depth (8/10)
are ahead of the field. Progression (7/10, up from 5) and dialogue (7/10) are competitive.
**Mission structure is the critical gap (2/10)** — the only dimension where every
comparator that shipped as a narrative game is structurally ahead. Recommended a
contract-board model (Elite/MSFS 2024/Far Cry 2) as the minimum viable fix.

## Where things get verified

This repo is developed on a laptop that cannot comfortably render it. **Headless checks run
here; anything that has to be looked at runs on the test machine.** The brief for that is
`docs/wiki/visual-check.md` — what to run, what each shot is for, and the specific questions
that cannot be answered without a screen.

Headless, always run before committing:

```
dotnet run --project tools/simlab -c Release -- all
godot --headless --path game -- --selftest
godot --headless --path game -- --looptest
godot --headless --path game -- --djreport
```

> **Godot runs the Debug assembly.** `dotnet build game/Rotorwash.csproj -c Release`
> succeeds, changes nothing Godot loads, and the next headless run executes the *previous*
> build with no warning at all - a new `--flag` simply does not match and the game boots
> normally instead, which looks like a bug in your argument handling rather than a stale
> binary. Build `-c Debug` before any `godot --headless` run. Cost an hour once.

## Next

Story build order item 8: "Everything else, region by region, in tier order."

0. ~~**Dialogue rewards.**~~ **DONE** (D-089).
1. ~~**Passengers.**~~ **DONE** (D-090). Sera Wray in the right seat: 68 kg mass, co-pilot
   callouts (torque, Nr, altitude, fuel, threat) via RadioStrip in warm amber, boarding
   reward, save/load.
2. ~~**Cargo hook sling load**~~ **DONE** (D-091). 420 kg blade pair on the cargo hook:
   SlingLoads.BladePair() factory, Progress.SlingLoadId persistence, SyncSlingLoad() in
   game layer, DialogueRewardKind.SlingLoad for story triggers. Finale feasibility
   measured: closes with 265 kg fuel margin at worst ceiling (0.58), ISA+10, full load.
3. ~~**Bel's trade completion**~~ **DONE** (D-092). `ContractKind.Lift` for hoist-based
   contracts, `ContractBoard.StoryContract()` injects the generator lift at Bel's settlement
   when the hoist is installed, `Requirement.Fitted()` gates dialogue on installed modules,
   and `BuildTalkContext` now populates knowledge and fitting ids — fixing a bug where all
   `Knows()`/`Unknown()` dialogue gates were inert. Completing the generator lift grants
   wreck position knowledge (`bel.wreck_position`). Six new simlab tests.
4. **Act III NPC dialogue depth** — Wray, Juno, Sparrow thread lines need reward tags
   once the engine features they depend on exist (passengers, sling load, medical trade).

## Open questions for Fred

*(none blocking)*

- Working title is **ROTORWASH**. Keep?
- The aircraft is called **Hugh**. Confirm?
- Have a listen to `builds/audio/sortie.wav` when you get a moment — it is the one thing
  I cannot check for myself.
