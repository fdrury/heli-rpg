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

## Menus

`ESC` pauses — it used to quit the game outright with no confirmation, which was the single
most hostile thing in here for anybody trying to test.

- **Title** on boot: Continue / New sortie / Settings / Quit. It is the paused game with a
  menu over it rather than a separate scene, so there is no loading wait when you start, and
  the camera orbits the aircraft while you read it.
- **Pause** (`ESC`): Resume / Save / Load / Settings / Flight controls / Quit. Quitting takes
  two deliberate choices and offers to save on the way out.
- **Settings**: master volume, radio volume, graphics tier, field of view, look sensitivity,
  invert cyclic pitch. Left/right changes a value; it saves itself to `user://settings.cfg`.

Saving is still parked-only by design (D-036) — the menu will tell you to land first rather
than failing silently.

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

**Controllers.** `ControllerPresets` recognises about forty device names - Xbox, PlayStation,
Switch Pro, T.16000M, Extreme 3D Pro, X52/X56, Warthog, VKB, VIRPIL, CH, Honeycomb, TWCS,
rudder pedals - and, importantly, builds a profile from **every** device plugged in rather
than just the first, because a real HOTAS is two USB devices and the collective belongs on
the throttle. A dedicated throttle always wins the collective; dedicated pedals always win
the yaw.

**If your hardware is odd, press `F8`.** That is the binding screen: up/down to pick an axis,
`ENTER` then move the control you want (it binds whatever moved furthest, so you do not need
to know axis numbers), `I` to invert, `L` to say an axis is a lever rather than a
self-centring stick, `S` to save, `R` to go back to auto-detection. Every axis draws a live
bar, which is the quickest way to spot one that is bound correctly and reading backwards.

`godot --headless --path game -- --inputreport` prints the table's verdict on thirty real
device names and what it would build from whatever is plugged into that machine. Worth
running on the test PC before you start - it will say what it thinks your hardware is.

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
