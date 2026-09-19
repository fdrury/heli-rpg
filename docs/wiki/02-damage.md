# Damage, fluids and the clocks they start

`sim/src/Damage.cs`. Checked by `tools/simlab/DamageCascadeTests.cs`:

```
dotnet run --project tools/simlab -c Release -- all
```

## The problem this solves

The first version of `DamageState` tracked a health value per component and turned each
one into a multiplier on something the flight model computed. That much was right and is
unchanged. What it could not do was any of the three things a game about keeping one
irreplaceable machine alive actually needs:

- **Nothing caused anything else.** A hit took 30% off a number, the number stayed there,
  and the gearbox had no idea the engine was sick.
- **Nothing got worse.** There was no clock, so a failure was a permanent tax rather than a
  decision. "Where is the nearest place I can put down" was not a question the aircraft
  ever asked.
- **Nothing was legible.** The only measure of how bad things were was the health value,
  which is a score, and D-005a says facts, not scores.

The fix for all three is the same: **split the slow permanent record from the fast
symptoms.** Health is the diagnosis and the player never sees it. Oil quantity, oil
pressure, oil temperature, TOT, hydraulic pressure and vibration are the symptoms, they are
what the gauges read, they move over minutes, and most of them recover when you land.

## The two layers

| | Health | System state |
|---|---|---|
| Moves | only down, in flight | both ways, over seconds to minutes |
| Restored by | a spanner | landing, shutting down, a top-up |
| Player sees | never | on every gauge |
| Saved | yes | no — see below |

The system state is deliberately **not** saved. D-036 makes saving parked-only, and a
parked aircraft is cold with whatever the mechanic put back into it. The leak itself lives
in the health number, so the clock restarts the moment the next sortie pulls pitch.

## The cascades, and why each one is real

1. **Gearbox oil → heat → the gearbox.** A cracked case loses oil. This machine has no oil
   quantity gauge, so the pilot's ladder is: the temperature needle starts to move as the
   cooler loses circulation → much later the pressure switch trips as the pump unports →
   the chip light comes on as the gears start making metal → unserviceable. Running a
   gearbox hot and dry *under load* destroys it. Running it hot and dry with the rotor
   stopped destroys nothing.
2. **Engine → collective → gearbox.** Not scripted. Gearbox heat is a fixed fraction of the
   power going *through* the mesh, and an engine that is short of power makes the rotor
   droop, which makes the pilot pull more pitch for the same thrust. Nothing in `Damage.cs`
   knows the engine is involved.
3. **Engine → its own hot section.** A tired turbine makes less power, so the same task
   needs a higher fraction of what is left, so TOT is higher, so the turbine gets tireder.
   A genuine self-reinforcing loop, with a real escape: ask for less power.
4. **Hydraulics: a cliff, not a slope.** The pump keeps up with the leak until the reservoir
   unports, then everything goes at once. The leak is the clock; the pressure gauge is the
   warning; slow actuators *and* no stability augmentation arrive together, because they are
   the same fluid. The pump is on the transmission accessory pad, so the leak only runs
   while the rotor turns.
5. **Gearbox → hydraulics.** Same accessory pad. A gearbox coming apart takes the pump drive
   with it. Applied as mechanical damage with a cause, not as a pressure fudge.
6. **Rotor → everything bolted to the airframe.** An out-of-track rotor shakes the machine at
   1/rev, and vibration is the classic killer of hydraulic lines, fuel lines and fittings.
   Slow: a few hundredths of health per ten minutes. Enough to make "get it home" the right
   call; not a death sentence (D-007).
7. **Fuel system → engine.** Below about a quarter health the boost pumps cannot keep the
   fuel control satisfied and available power falls, which feeds back into 2 and 3.
8. **Hours.** Every component also accrues plain rotor-turning wear on `Salvage`'s
   accelerating curve, *additional* to all of the above. A gearbox that cooked itself has
   both the heat damage and the hours on it.

## The clocks, measured

Load is the measured cruise: 485–500 kW through the gearbox at 36% torque, 80 kt, 300 m,
ISA. Times are minutes from the damage being taken.

| Gearbox health | first caption | temperature in caution | unserviceable |
|---|---|---|---|
| 0.85 (worn) | never | never | never |
| 0.50 (cracked case) | 5.8 | 7.1 | **14.2** |
| 0.35 (fragment) | 3.4 | 4.6 | **8.7** |

At the measured 100 kt cruise, 8.7 minutes is about 14 nautical miles of choices. That is
the D-007 promise expressed as a number.

| Hydraulic health | normal controls for | pressure below 1000 psi at | after |
|---|---|---|---|
| 0.80 | 1078 s | 1178 s | actuators 4.5x slow, SAS gone |
| 0.45 | 143 s | 157 s | actuators 4.5x slow, SAS gone |
| 0.20 | 67 s | 74 s | actuators 4.5x slow, SAS gone |

At the moment of the hit the controls are unchanged (2752 psi, 1.29x). That is the point.

| Engine | power | TOT | health lost per minute |
|---|---|---|---|
| 1.00 | 100% | 605 °C | 0 |
| 0.55 | 100% | 688 °C | 0.041 — about 10 minutes to unserviceable |
| 0.55 | 70% | 558 °C | 0 |

**Shutting down stops the clock.** A 0.50 gearbox flown for 9.4 minutes, then shut down,
loses 0.00095 more health over the next thirty minutes on the ground — and every bit of
that is the flight hours being booked, not ongoing damage. The consequential damage is
exactly zero. The oil cools from 128 °C to 57 °C. The chip light stays on, because a chip
light latches until somebody opens the gearbox.

## Legibility

`DamageState.Gauges()` returns name, value, unit and where the yellow and red marks are.
`WarningPanel()` returns the captions currently lit, each named after **what tripped it**,
never how bad it is: `XMSN OIL PRESS` means a switch opened below 30 psi, not "you are in
trouble". `XmsnOilTempTrendPerMin` and `XmsnMinutesToLimit` are the arithmetic a fuel
totaliser does — present reading, present rate, one division — and carry the same honesty
warning: it is what happens if nothing changes, and something always changes.

The test asserts the ladder the player has to be able to read:

| | captions lit | what the player sees |
|---|---|---|
| serviceable | 0 | everything in the green |
| worn (0.70 engine, 0.80 gearbox, 0.75 rotor) | **0** | TOT 32 °C higher, vibration 0.6 ips |
| a problem (0.50 gearbox, 7 min) | 2 | needle rising, a trend, a time |
| about to kill you | 5 | |

A worn aircraft lighting captions would teach the player to ignore the panel, and the panel
is the whole warning system. Wear must be *readable* without being *announced*.

## Four things that did not work

**A fixed oil cooler.** Heat into the oil is proportional to transmitted power, so a fixed
cooler makes the temperature gauge a power gauge in different units — 82 °C in the cruise,
130 °C at max continuous — and the player learns to read the needle as "how much collective
am I holding". Replaced with a thermostatic cooler, which is what real oil systems have:
the gearbox sits in the same narrow band all day whatever it is doing, and the needle moving
*at all* means something is wrong.

**Thermal capacity of the whole gearbox.** 75 kJ/K — oil plus the entire magnesium case —
meant a gearbox that had lost all its oil took **34 minutes** to reach the red line, by
which time the pressure switch had been the warning for twenty of them and the intended
ladder was exactly backwards. The case is cooled by 100 kt of air and is not part of the
oil's thermal inertia. 25 kJ/K.

**Hydraulic pressure as a function of health.** Giving health 55% of the pressure meant a
0.45 system read 2092 psi *the instant it was hit*, so the controls were a third slow before
a drop of fluid had left the reservoir, and the cliff was a slope again. The leak is the
mechanism; the pump is a footnote, and gets 15%.

**Booking flight hours every thirty seconds.** This one is worth recording carefully. The
hours-based wear tick ran from inside the flight loop and looked harmless: after a minute of
flying the main rotor was at 0.99985, a perturbation of 1.5 parts in ten thousand. It moved
the measured autorotation rate of descent from 3193 to **3859 fpm** — a fifth of the answer
— and shifted best glide from 50 kt to 70 kt. The autorotation equilibrium (D-041, D-045) is
knife-edged enough that a rounding error in blade condition relocates it, which is worth
knowing on its own account. It also meant `Trim.Solve` accrued wear, because trim solves by
stepping the aircraft, so how worn an aircraft was depended on how many iterations its trim
took.

Fixed by booking the hours **when the rotor stops** (`DamageState.AccrueFlightHours`, called
automatically on the transition, and callable by hand at a save point). That never perturbs
flight, never contaminates a trim solve, and puts the cost at exactly the moment D-003a says
the game should charge the player: when he lands.

## Two bugs the tests found in themselves

Both are recorded because the numbers they produced were plausible enough to believe.

**A `dt` clamp.** `UpdateSystems` clamped `dt` to 0.5 s as a guard against a pathological
frame. The bench steps it at one second to cover twenty minutes of flying, so every clock
ran at half speed: a gearbox that should have been unserviceable in thirteen minutes took
thirty-seven. Nothing here needs the guard — the lags are exponentials, stable at any step,
and the one Euler integration is stable to a step of about a hundred seconds.

**Reading an aliased gauge.** The bench took its cruise load from `Telemetry.PowerRequired`,
which is an instantaneous shaft torque times omega. A two-bladed rotor puts a violent 2/rev
into shaft torque (D-043), and the sample came back as 292 kW for a cruise the torque gauge
said in the same frame was 570 kW. Every clock built on it was twice as long as it should
have been. The rev-averaged breakdown (`MainRotorPower + TailRotorPower + DrivetrainPower`)
is the honest number, and the aircraft's own telemetry had already had to learn this lesson
once.

## What the in-flight test learned

`InFlightCascade` was first written to prove "a sick engine cooks the gearbox" by cruising
two aircraft that differed only in engine health. That is not how the coupling works. An
engine with power to spare does not change the collective at all — the rotor needs what the
rotor needs — so a 0.60 engine at 80 kt, wanting 500 kW out of 770 kW available, flies
indistinguishably from a new one. **The coupling is real but conditional**: it appears when
the engine is short, because then the rotor droops and the pilot must pull more pitch to
make the same thrust at lower Nr. Measured in an out-of-ground-effect hover, where the
engine has nowhere to hide, a 0.55 engine costs **+0.50 of collective and +22 points of
torque** through the gearbox.

The same test confounded itself a second way: it damaged the gearbox to 0.45 in *both*
aircraft, which drops the torque ceiling to 64%, which made both of them torque-limited and
neither of them a control.

## Integration points still open

- `Salvage.UnserviceableAt` duplicates the floors that are now named constants on
  `DamageState` (`MainRotorFloor` and friends, plus `DamageState.UnserviceableAt`). It
  should forward to them.
- `DamageState.AccrueFlightHours()` fires automatically when the rotor stops. The save path
  may also want to call it explicitly before writing, so that a save taken with the rotor
  still turning does not lose the Hobbs time.
