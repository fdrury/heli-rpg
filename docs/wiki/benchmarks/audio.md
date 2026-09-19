# Benchmark — audio

*Written 2026-09-19, against `sim/src/RotorSynth.cs`, `WeatherSynth.cs`, `WarningSynth.cs` and
the measurements in `tools/simlab/AudioTests.cs` and `WarningTests.cs`. Fourth of the
periodic benchmark passes; the others cover progression, world scale and the flight model.*

---

## Bottom line

Everything this game makes a sound with is **synthesised from simulation state**, and that is
an unusual position to be in. The rotor's blade-pass rate measures **10.73 Hz against 10.79
expected** and tracks rotor speed down to 70% Nr. No sample library can do that, because a
recording cannot follow a decaying rotor.

The gap is not fidelity. It is **coverage and space**: a handful of voices, all of them
attached to the aircraft or to the weather, in a world with no acoustics. The game currently
sounds like a helicopter in an anechoic chamber.

---

## Where it sits

| | Typical approach | ROTORWASH |
|---|---|---|
| **Arcade / action** | Two or three looping samples cross-faded on RPM | Passed long ago |
| **Milsim (Arma, DCS)** | Large recorded libraries, layered by state, some granular blending | Behind on *material*, ahead on *causality* |
| **Study sims (X-Plane, DCS Huey)** | Recorded, with elaborate state machines; some parametric wind | Comparable, different bet |
| **Parametric / procedural** | Rare in shipped games outside racing engines | **Here** |

The bet is that a synthesiser tied to the model beats recordings of somebody else's aircraft
at somebody else's power setting. The evidence so far supports it: rotor decay is audible
before the gauge moves, and pulling power is now measurably louder (RMS 0.052 → 0.096 from a
quarter torque to an overtorque).

## What is already good

- **The blade-pass rate is right and tracks.** 10.73 Hz measured, 10.79 predicted, correct
  at 100%, 85% and 70% Nr. This is the single number that decides whether it sounds like
  *this* machine.
- **It is tested at all**, which is rare. Audio is the one part of a game nobody can check
  in a screenshot, and a synthesiser can be silent, clipped, full of NaN or modulating at the
  wrong rate — all of which survive indefinitely if the only check is putting headphones on.
- **The warning voices are separated on two axes at once** — pitch *and* rhythm. The
  low-rotor horn is 400 Hz continuous at 100% duty; the master warning is 950 Hz pulsed at
  3.3 Hz and 47% duty. Each has roughly a million times more energy at its own frequency
  than at the other's. Two tones that differ only in pitch are much harder to tell apart
  under stress.
- **Weather has its own voice on its own player**, non-positional, and keeps going with the
  engine shut down. Standing beside a cold aircraft in the rain is not silent.

## The gaps, ranked

1. **There is no space.** No reverb, no reflection, no occlusion. Flying down a valley,
   into a hangar, or fifty feet over water should all sound different and none of them do.
   For a game whose whole subject is *where you are*, this is the biggest single miss —
   bigger than any amount of extra fidelity in the rotor voice. A cheap send reverb driven
   by the terrain height already under the aircraft would do most of the work.
2. **The world is silent.** Sites have no ambience. A settlement with three hundred people
   drinking from a pumped well makes noise; a wreck field does not. Landing somewhere and
   shutting down currently produces *nothing*, at exactly the moment the player is most
   receptive to being told where they are.
3. **No Doppler and no distance character.** The rotor player attenuates with distance but
   the timbre does not change. Real distant rotor noise loses its top end long before it
   loses volume, which is why you hear a helicopter minutes before you place it. D-010's
   whole threat premise is that *everyone knows you are coming* — that should be audible
   from the ground, and it is the kind of detail that sells the premise for free.
4. **Nothing for the on-foot half.** Footsteps, wind at head height, the aircraft ticking as
   it cools. The on-foot mode exists and is silent.
5. **No mix discipline.** Three synths and a warning system all write into the master bus at
   fixed gains. The low-rotor horn has to be audible over a rotor at full power *and* not
   deafening in a shut-down cockpit, and right now nothing ducks anything.

## What the tests should catch and do not

- **Nothing tests the audio in the game**, only in the sim. `HelicopterAudio` and
  `WeatherAudio` push buffers into `AudioStreamGenerator` and no headless check covers that
  path — precisely the gap that let a reversed flight control through in D-051.
- **No test asserts the mix.** "The horn is audible over the rotor" is a measurable claim —
  render both, measure the horn's band energy against the rotor's — and nobody measures it.

## Honest assessment

Audio is in better shape than the rest of the presentation layer, because it was built
parametrically from the start and because it got tested. The next hour spent on it should go
to **space and world ambience**, not to the aircraft: the machine already sounds like the
machine, and the world it flies through does not sound like anything at all.
