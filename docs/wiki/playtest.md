# Playtest brief

*Written 2026-09-20, when Fred asked whether the game was worth sitting down with on a
machine that can render it. `visual-check.md` is the brief for **looking** at the game;
this is the brief for **playing** it.*

---

## Is it playable?

Yes, with one caveat that matters more than the rest: **the aircraft is unstable hands-off**
and always will be. Let go of the cyclic and it diverges in about six seconds. That is
correct for a Huey with no autopilot and it is the central skill of the game, but it means
there is no moment where you can take your hands off to read the kneeboard in flight. Land
first, or use the SAS module once you find one.

What works end to end, verified headlessly before this was written:

| | State |
|---|---|
| **Flight** | Solid. The most-tested thing in the repo. |
| **Core loop** | Passes: fly → find → land → shut down → talk → contract → fly it → paid. |
| **Landing & damage** | Works, and is where the game charges you (D-003a). |
| **Threats** | Live. Guns, MANPADS, radar SAMs, chaff/flares/RWR. |
| **On foot** | Works, less exercised than flight. |
| **Radio** | Streams real stations; the announcer talks about what you did. |
| **Save / load** | F5 / F9. Parked only, by design (D-036). |

## Start here

```
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game
```

Controls are printed along the bottom of the screen in both modes, so you do not need this
table — but for reference:

| | Flight | On foot |
|---|---|---|
| Move | `W`/`S` collective, arrows cyclic, `A`/`D` pedals | `WASD`, `Shift` sprint |
| Fire | `LMB` gun pod, `Z`/`X` chaff/flares | `LMB` fire, `RMB` Rotor Time, `R` reload |
| Other | `C` camera, `F` dismount, `1`-`4` site actions | `F` board Hugh |
| Always | `TAB` kneeboard · `M` acknowledge warning · `F5`/`F9` save/load | |
| Radio | `B` on/off · `N`/`V` station · `-`/`=` volume | |

Throttle and stick are supported and better than the keyboard for the collective, which
wants fine control rather than on/off.

## What to look for, in order

**1. Does the aircraft feel like an aircraft?** This is the only question that cannot be
answered from here. Specifically: does it settle into a hover without pilot-induced
oscillation, does translational lift arrive as a distinct event around 15-20 kt, and does
the tail feel like it is doing work in a crosswind. If any of those feel wrong, say so
before anything else — everything downstream is built on the flight model.

**2. Can you land without reading a manual?** The landing assessment is drawn while there is
still time to go around. Is it legible at the moment you need it, or only afterwards?

**3. Does a sortie have a shape?** Take a contract from a board, fly it, come back. Roughly
how long does that take in real minutes, and is the middle of it boring? An over-water leg is
*allowed* to be empty (D-059) — the question is whether it feels like a crossing or like
dead air.

**4. Is the world legible from the air?** You should be able to tell a settlement from a
depot from a wreck at 500 m, and see which buildings still have roofs at 150 m.

**5. Does the radio land?** The announcer is Hollis Kerr on the citadel mast. He should be
audible almost everywhere, fade at the edges, and — after a few sorties — start mentioning
things you actually did, with the numbers slightly wrong.

## Known-thin areas — no need to report these

- ~~**The citadel has no defences yet.**~~ Resolved by D-088. The Scald now has a dedicated
  defence ring (3 SAMs, 4 MANPADS, 3 guns, 2 search radars) that makes it lethal to overfly.
- **Destructible buildings are half-wired.** The model, the ruins and the rebuild-over-time
  are in; the weapon that levels them is being built as this is written.
- **The announcer's voice is a formant murmur, not speech** (D-078). The captions carry the
  words. That is deliberate for now, not a bug.
- **Two of seven overlooks fail to place.** No loot, no story role; recorded in
  `--worldreport` rather than hidden.

## Reporting

The thing that is most useful is a *measurement or a screenshot*, not an impression — that is
the rule the whole project runs on and it has been right every time. "The hover wanders" is
hard to act on; "the hover wanders about 3 m every 4 seconds with the stick centred, at
200 m, no wind" is a bug report that can be reproduced headlessly.
