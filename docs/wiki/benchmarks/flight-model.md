# Benchmark — the flight model

*Written 2026-09-19, against the measured envelope in `tools/simlab` (`envelope`, `autoglide`,
`autobalance`, `trim`, `handsoff`, `sassweep`) and D-020 / D-021 / D-041 / D-045.*

---

## Bottom line

The model sits further toward the study-sim end than anything else in this genre, and the
measurements now support that rather than merely asserting it: **505 km of still-air range
against a published 510 km**, minimum power at 60 kt, a textbook power curve, exact trim
solutions from 0 to 120 kt.

The gap that matters is not fidelity. It is that **the model is more faithful than the game
around it can currently show**, and one measured defect (autorotation, D-045) sits in the
one manoeuvre a player will meet at the worst possible moment.

---

## Where it sits

| | What it models | Where ROTORWASH is |
|---|---|---|
| **Arcade** (GTA, Just Cause) | Velocity-driven, no rotor at all. Collective is "up". | Not comparable. |
| **Semi** (Arma 3, Battlefield) | Lift from a thrust vector; torque and translational lift faked as curves. | Passed some time ago. |
| **Advanced** (Take On Helicopters, MSFS default rotorcraft) | Rotor disc as a unit: thrust, a torque reaction, some retreating-blade limit. Inflow usually a lookup. | Comparable on feel, ahead on cause. |
| **Study** (DCS UH-1H, X-Plane) | Blade elements, flapping, inflow dynamics. | **Here**, with gaps. |

What puts it in the last row is that nothing is scripted on top: retreating blade stall, ETL,
ground effect, vortex ring, and autorotation are all *consequences* of per-blade elements,
flapping oscillators and a dynamic inflow. The freewheel is a real freewheel, which is why
autorotation exists at all rather than being a mode.

## What the comparators do that we do not

**DCS-class study sims — the drivetrain and the systems.** Ours models rotor, freewheel,
governor and fuel. Theirs model the whole aircraft as a machine: hydraulic pressure, bleed
air, generator load, starting sequences that can go wrong. This is a deliberate non-goal for
ROTORWASH — but the *damage* model already reaches for it (D-030 couples the augmentation to
hydraulics), and that is the seam where more depth would pay for itself without becoming a
procedures trainer.

**X-Plane — the airframe, not just the rotor.** Their fuselage aerodynamics are computed
from geometry. Ours is three drag areas and two static-stability coefficients. This shows up
directly in a measurement: parasite power tripled in a steep descent purely because
`DragArea.Z` is 10 m² of plan area, which is a guess rather than a shape.

**Take On Helicopters — the pilot's difficulty ladder.** Theirs is graduated: assists that
can be peeled back one at a time. Ours has a single switch (`Sas.Enabled`). Given D-021's
augmentation is already limited-authority and degrades with hydraulic damage, the ladder is
nearly free to build and is the single cheapest way to widen who can fly this.

**Everyone — trim as a control the pilot operates.** We have a trim *datum* (D-022a) and a
force trim release, which is the hard half. What is missing is the beeper: fine trim
adjustment without re-datuming, which is how a real pilot actually flies a long leg.

## Where we are ahead

- **Trim is solved, not tuned.** A real 6-DOF Newton solve (D-020) rather than a hand-placed
  starting attitude. Most games in this space spawn you approximately trimmed and let the
  autopilot hide the rest.
- **The augmentation is honest.** Limited authority, saturates, and running out of authority
  is a real event the aircraft can be flown past (D-021). Most implementations are either
  invisible or absolute.
- **The aircraft is checked against the real one.** `simlab envelope` compares against
  published UH-1H figures every run. This is not common even at the study end.
- **Audio is derived from the same state.** Blade-pass measured at 10.73 Hz against 10.79
  expected, tracking rotor speed down to 70% Nr (D-035). Sample-based competitors cross-fade
  between recordings and cannot follow a decaying rotor.

## The gaps, ranked

1. **Autorotation, D-045.** Best glide 1.99:1 at 3191 fpm against roughly 4:1 at 1700. The
   rig now exists to attack it and one hypothesis is already refuted. This is the top item
   because engine failure is the moment the flight model is most exposed, and a 2:1 glide
   quietly makes much of the map unsurvivable.
2. **A difficulty ladder.** One switch is not enough, and the pieces are already built.
3. **Fuselage aerodynamics from geometry.** Three drag areas is the weakest remaining part
   of an otherwise strong model, and D-043's power split has already caught it behaving
   oddly in descent.
4. **Trim beeper.** Small, and it is what makes a long leg comfortable.
5. **Rotor RPM as something the pilot manages.** The governor hides it almost entirely.
   Given the whole game is about a machine being kept alive, a governor that can be
   degraded — and a throttle that then matters — is thematically free.

## What this does not say

Nothing here argues for more fidelity as an end. The measured envelope is already closer to
the real aircraft than the game's presentation can currently convey — a player cannot see
505 km of range or a textbook power curve. Items 2 and 5 above are about making the existing
fidelity *legible*, which is worth more right now than adding more of it.
