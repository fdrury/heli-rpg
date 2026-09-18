# ROTORWASH — status

*Last updated: 2026-09-18*

A single-player post-apocalyptic RPG about the last helicopter pilot in the world.
Godot 4.7.2 (.NET). See `docs/wiki/00-vision.md` for what it is, `docs/wiki/decisions.md`
for every choice made and why.

## Where it is now

**It flies.** A blade-element helicopter simulation, validated against real UH-1 numbers,
driving a Godot rigid body over textured terrain with scattered rocks, dead trees and
scrub. HOTAS, gamepad and keyboard all work. 60 fps at 1600x900 on a GTX 1650 Ti, which is
well below the GTX 1080 target.

## Run it

```
# the game
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game

# physics test bench (no engine needed)
dotnet run --project tools/simlab -c Release -- all

# verify the physics survives the Godot solver
godot --headless --path game -- --selftest

# capture the standard screenshot set for visual comparison over time
godot --path game -- --screenshot
```

Controls: `W`/`S` or throttle lever = collective · arrows or stick = cyclic · `A`/`D` or
twist = pedals · `C` camera · `F1` debug · `F2` stability augmentation · `R` respawn.

## Done

- [x] Blade-element rotor with per-blade flapping, dynamic inflow, VRS, ground effect
- [x] Turboshaft with governor, torque limits, density lapse, **freewheel → autorotation**
- [x] Tail rotor, lifting surfaces, full inertia tensor from mass items
- [x] 15-scenario headless flight test bench, all passing
- [x] Godot bridge + headless self-test
- [x] Terrain with 4-way PBR blending and distance fade
- [x] Procedural rocks, dead trees, scrub (no asset pipeline, deterministic)
- [x] HUD: attitude, Nr, torque, fuel, control positions, warning captions
- [x] Quality tiers: baked path for a 1080, SDFGI/SSIL/volumetrics on Ultra

## Next

1. **Parametric airframe** — a lofted Huey built from cross-sections, which doubles as the
   foundation for the refit system
2. **World structure** — roads, ruins, settlements; the "everything in between"
3. **Refit system** — the block builder, as modules on real hardpoints (D-011)
4. **Fuel and parts economy** — the core loop (D-007)
5. **Dialogue** — baked corpus plus local SLM coda (D-006)
6. **Audio** — turbine, rotor slap, wind, transmission
7. **Threat envelopes and countermeasures** — the gating system (D-010)

## Open questions for Fred

*(none blocking — everything below is a preference, not a blocker)*

- Working title is **ROTORWASH**. Keep, or something else?
- The aircraft is called **Hugh**. Confirm?
