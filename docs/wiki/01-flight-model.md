# Flight model

`sim/` is a plain .NET 8 library with **no engine dependency**. It can be stepped
thousands of times per second from a console, which is how every number below was
measured. Run the bench with:

```
dotnet run --project tools/simlab -c Release -- all
```

## Axes

The simulation uses aerospace conventions throughout, and converts exactly once.

| Frame | X | Y | Z |
|---|---|---|---|
| Body (FRD) | forward | right | **down** |
| World (NED) | north | east | **down** |
| Godot | right | **up** | back |

Altitude is `-Position.Z`. The Godot bridge is the only place the flip happens.

## What is actually modelled

**Main rotor — individual blade element.** Each blade is its own flapping oscillator,
integrated in substeps around the azimuth, with blade-element aerodynamics evaluated at
ten radial stations. Cyclic is applied as a commanded *disc tilt*, and the 90-degree
phase lag between blade pitch and blade flap is derived from the rotation direction
rather than hard-coded, so a clockwise rotor gets correct controls for free.

Consequences that emerge rather than being scripted: coning, blowback, translational
lift, retreating blade stall, reverse flow inboard on the retreating side, cross-coupling,
tip-loss, and compressibility drag rise on the advancing tip.

**Inflow.** Momentum theory solved by damped fixed-point iteration, with a first-order
dynamic-inflow lag. Three special regimes:
- *Vortex ring state* — the classical empirical fit through the turbulent-wake region,
  with asymmetric formation (0.7 s) and decay (2.6 s) time constants so a developed ring
  outlives the flight condition that caused it.
- *Ground effect* — Cheeseman-Bennett, washed out with forward speed.
- *Windmill brake* — the branch that makes autorotation work.

**Powerplant.** Turboshaft with a governor (proportional + integral + load feed-forward),
spool lag, transmission torque limit, and density-altitude power lapse. The **freewheel
unit** is the important part: torque only ever flows engine to rotor, so the moment the
engine quits the rotor is living on the air coming up through it.

**Airframe.** Per-axis parasite drag, rotor download, horizontal stabiliser and vertical
fin as general lifting surfaces (the stabiliser sits in the rotor wake at low speed),
fuselage static moments, and a full inertia tensor built from individual mass items via
the parallel axis theorem — which is what will let the block builder matter.

## Measured performance — "Workhorse" (Huey class, 3790 kg)

| Quantity | Model | Real UH-1 class |
|---|---|---|
| Disc loading | 220.8 N/m² | ~230 N/m² |
| Tip speed | 248 m/s | ~248 m/s |
| Hover induced velocity | 11.0 m/s | ~10 m/s |
| Hover power (OGE, SL) | 743 kW | ~700 kW |
| Minimum power speed | 58 kt @ 462 kW | 60-70 kt |
| Hover fuel flow | 262 kg/h | ~250-300 kg/h |
| Figure of merit | 0.68 | 0.65-0.75 |
| Collective to hover | 7.5° | 8-10° |
| Hover ceiling OGE | ~3000 m | ~3500 m |
| Autorotation | 100% Nr, 2586 fpm @ 64 kt | ~2000 fpm |
| ETL climb on hover collective | 638 fpm @ 51 kt | real and pronounced |
| VRS thrust deficit | 59%, 99 m lost | matches the training scenario |

Cost: **10.6 µs per step at 240 Hz — 0.34% of one core** for a complete aircraft.

## Bugs found and fixed during bring-up

Recorded because each one was invisible until a test measured it, and each would have
been misdiagnosed as "the flight model feels wrong" if found in the game.

1. **Anti-torque cancellation.** The shaft-axis moment substitution was applied to the
   *combined* rotor moment, which deleted the tail rotor's yaw authority. The aircraft
   spun up regardless of pedal input.
2. **Inverted pedal sense.** On an anticlockwise rotor, *left* pedal adds tail rotor
   thrust. Reversed, the yaw loop saturated and the aircraft departed.
3. **Mast tilt sign.** A forward-tilted mast was pushing the aircraft backwards.
4. **CG 0.4 m behind the mast**, producing a permanent 24 kN·m nose-up moment.
5. **Unseeded inflow**, so a rotor spawning at speed produced 2.5x hover thrust for the
   first tenth of a second and launched the aircraft.
6. **No actuator rate limit**, letting a controller command full cyclic in one tick.
7. **Two-blade harmonic aliasing** in the tip-path-plane telemetry.
