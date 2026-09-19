# ROTORWASH — status

*Last updated: 2026-09-18*

A single-player post-apocalyptic RPG about the last helicopter pilot in the world.
Godot 4.7.2 (.NET). `docs/wiki/00-vision.md` is what it is; `docs/wiki/decisions.md` is
every choice and why; `docs/wiki/benchmarks/` is where it was measured against the genre.

## Where it is now

**The core loop closes, people talk, and the machine levels up.** You can fly a
physically simulated Huey across a streamed 16 km world, find a named settlement, put
it down, shut down, talk to whoever lives there, and scavenge modules that bolt onto
the aircraft and change what it can do. Eight bays — attitude hold, RWR, chaff,
flares, exhaust suppressor, long-range tank, cargo hook, rescue hoist — each with real
mass at a real position, each felt in the flight model. Install an exhaust suppressor
and the drag rises; fit a long-range tank and the fuel capacity jumps but the hover
ceiling drops. The kneeboard shows every bay, fitted or empty, because you cannot want
a thing you do not know exists.

60 fps at 1600x900 on a GTX 1650 Ti, which is well under the GTX 1080 target.

## Run it

```
# the game
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game

# 29 headless tests (flight + loadout) - no engine needed
dotnet run --project tools/simlab -c Release -- all

# render a 126 s sortie to builds/audio/sortie.wav and listen to it
dotnet run --project tools/simlab -c Release -- audio

# verify the physics survives Godot's solver
godot --headless --path game -- --selftest

# fly the actual game loop and check it works
godot --headless --path game -- --looptest

# survey the generated world: terrain statistics, site placement, coverage
godot --headless --path game -- --worldreport

# measure how much cover the terrain actually gives against each emitter
godot --headless --path game -- --threatreport

# the standard screenshot set, for comparing the look over time
godot --path game -- --screenshot
```

Controls: `W`/`S` or throttle = collective · arrows or stick = cyclic · `A`/`D` or twist =
pedals · `TAB` kneeboard · `1`-`4` actions · `Z`/`X` chaff/flares · `C` camera ·
`F2` stability augmentation (requires module) · `R` respawn.

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
stitching rather than skirts; procedural rocks, dead trees and scrub; 144 named places
across 8 regions, placed against the terrain so relays sit on ridges and settlements sit
on flat sheltered ground.

**Audio** — synthesised live from the flight model. No samples. Verified spectrally: the
blade slap sits at the blade-pass frequency the physics predicts and is loudest in a
loaded turn.

**Threat** — five classes, each owning an altitude band, with real terrain masking: a ray
walked from every emitter to the aircraft through the actual height field. A SAM cannot see
you below 120 m AGL at all; the aerostat looks down and ignores the dead ground everything
else misses. Chaff takes a SAM from 7 hits to 0 and does nothing to a heat seeker; flares
the reverse. 45 s in two envelopes: everything damaged, 62% forced down, none deleted. The
RWR logs every emitter that paints you, so the sortie that nearly killed you pays out.

**Dialogue** — a baked corpus conditioned on state, with a selector that prefers the most
specific match, plus the local-model coda contract: gated on whether the spoken line covers
the measured latency, committed a whole sentence at a time, and validated against invented
quests, questions and anachronisms. NPCs remember across visits, which the research found is
where the perceived magic actually lives. The llama-server subprocess is wired up behind a
Windows Job Object for crash safety, the conversation UI draws in the HUD aesthetic with
word-by-word reveal, and everything degrades to the baked layer when the model is absent.

**Play** — refuel, repair, salvage, survey, tune a relay, ask around. Carried load is real
mass and is felt in the hover. The kneeboard records facts, not inferences, and shows the
empty bays.

**Refit** — eight modules on real hardpoints (D-011). Each is a MassItem plus drag and
fuel capacity, so the flight model feels every choice. Modules are found during salvage
at wrecks, airfields and depots (~15% of eligible sites), then installed at workshops or
airfields for a parts cost. Countermeasures (RWR, chaff, flares, exhaust suppressor)
and the attitude hold unit are no longer toggled with keys — they are things you find
and bolt on. The kneeboard's FITTED section reads from real loadout state, and empty
bays show the player what to look for.

## Next

5. On-foot: third person, Rotor Time
6. Weather, night, and the flying that goes with them

## Open questions for Fred

*(none blocking)*

- Working title is **ROTORWASH**. Keep?
- The aircraft is called **Hugh**. Confirm?
- Have a listen to `builds/audio/sortie.wav` when you get a moment — it is the one thing
  I cannot check for myself.
