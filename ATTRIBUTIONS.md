# Attributions

> **Moved.** The canonical register is now **[`docs/wiki/attribution.md`](docs/wiki/attribution.md)**,
> which adds music, fonts and the credits text that attribution-required licences oblige
> us to print. Add new rows there. This file is left in place because other documents link
> to it, and it will be trimmed to a pointer once they stop.

Everything third-party used in this project, with its licence. Kept current because the
project may eventually be shared, and reconstructing provenance after the fact is
miserable. Nothing here is commercial-licensed; nothing here requires payment.

## Engine and tooling

| Thing | Licence | Notes |
|---|---|---|
| Godot Engine 4.7.2 (.NET) | MIT | Not redistributed in this repo; `tools/godot/` is gitignored |
| .NET 8 SDK / runtime | MIT | Microsoft |

## Textures

| Asset | Source | Licence | Used for |
|---|---|---|---|
| Grass005 | [ambientCG](https://ambientcg.com/view?id=Grass005) | CC0 1.0 | Terrain — low ground |
| Ground054 | [ambientCG](https://ambientcg.com/view?id=Ground054) | CC0 1.0 | Terrain — worn dirt patches |
| Rock030 | [ambientCG](https://ambientcg.com/view?id=Rock030) | CC0 1.0 | Terrain — steep faces |
| Gravel023 | [ambientCG](https://ambientcg.com/view?id=Gravel023) | CC0 1.0 | Terrain — high bare ground |

ambientCG materials are released under CC0 1.0 (public domain dedication). No attribution
is legally required; it is recorded here anyway because the work deserves credit.

## Reference material used for the flight model

Not assets — these informed the physics and are listed so the numbers can be checked.

- Prouty, *Helicopter Performance, Stability and Control* — blade element formulation.
- Johnson, *Helicopter Theory* — the empirical induced-velocity curve through the vortex
  ring state, and momentum theory branches.
- Padfield, *Helicopter Flight Dynamics* — dynamic inflow, flapping dynamics.
- Cheeseman & Bennett (1955) — ground effect.
- Drees (1949) — linear inflow distribution in forward flight.

## Still to source

- Helicopter model (looking for CC0/CC-BY Huey-like airframe)
- Vegetation and props
- Audio: turbine, rotor slap, wind, transmission
- UI font

## Audio

All aircraft audio is **synthesised at runtime from the flight model** - see
`sim/src/RotorSynth.cs`. There are no recordings and no sample libraries, so there is
nothing here to licence. Blade slap runs at the blade-pass frequency the sim computes,
the turbine tone tracks the gas generator, and the slap gets violent when the disc loads
up. Verified spectrally against the physics: `dotnet run --project tools/simlab -- audio`
writes `builds/audio/sortie.wav`.
