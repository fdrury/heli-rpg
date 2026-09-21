# Playtest findings

Results from actually playing the game, as opposed to measuring it. `playtest.md` is the
brief; this is what came back.

---

## 2026-09-20 — first human playtest

**Fred, Xbox controller, on the GTX 1080 box.** The first time a person has sat down with
this. Reported verbatim below, with annotation marked *(note)* where there is context worth
carrying — the report itself is not edited.

Nothing here has been fixed. Work stopped after this session; these are the starting
priorities for the next one.

### Blocking / high

1. **"Very difficult to control heli."**

   *(note)* This is the finding that matters most, and it directly contradicts the
   headless measurement taken hours earlier: `--playprobe` reported the aircraft held to
   11 degrees of tilt and called it flyable, and `--flysweep` scored the shipping tuning
   "solid". **Both were robots.** A machine pilot that re-presses a key every frame is the
   best possible case for a digital control and the worst possible proxy for a person, and
   `FlySweep`'s own doc comment says so. The sweep infrastructure is in place and can be
   pointed at whatever the real problem turns out to be — but the first job is finding out
   *what* is difficult: attitude wandering, over-control, the pedals, the collective, or
   the lag between input and response. A measurement that disagrees with the player is
   wrong about the thing the player cares about.

   Note also that this was an **Xbox controller**, not the keyboard. Every keyboard number
   in this repo is irrelevant to it. The pad path — `ControllerPresets`, the profile built
   from axis bindings, expo and deadzone — has never been measured at all, by robot or
   person.

2. **"Buildings have no colliders."** Walk straight through them on foot.

3. **"Can't see my character."** On foot, the pilot is not visible.

   *(note)* `PlayProbe` asserts `_pilot.Visible` is true and passes, so whatever is wrong
   is not the flag. Camera placement, draw order, scale, or the model itself.

4. **"Warnings and tones for no reason?"** Cautions and audio firing without an apparent
   cause.

   *(note)* Worth checking whether these are the latched warnings from an earlier event
   still showing — the UI shot taken this session had ROTOR RPM, SINK RATE and VORTEX RING
   all displaying `LATCHED` long after a normal landing, which would read exactly like
   "warnings for no reason" to a player who did not cause them and cannot clear them.
   `M` acknowledges.

### Controller

5. **"Xbox controller doesn't work in menu."** Menu navigation is keyboard-only.

6. **"No Xbox button to exit heli?"** Dismount is bound to `F` on the keyboard with no
   pad equivalent.

   *(note)* These two are the same gap: the pad is wired into flight controls and into
   nothing else. Menus, dismount, site actions and the kneeboard all assume a keyboard.

7. **"When I click away and back from window, the mouse stays visible and looking around
   becomes limited."** Focus loss does not restore mouse capture.

### World and presentation

8. **"Visuals/world is ugly."**

   *(note)* Deliberately left unqualified here rather than guessed at. `visual-check.md`
   asks specific questions about weathering, prop tinting, rock placement and night; none
   of those were the complaint. Needs a follow-up conversation about what specifically
   reads badly before anyone changes a shader.

9. **"Can't enter what looks like a truck."** A prop reads as enterable and is not.

   *(note)* Either the prop should not look enterable or it should be. Also a general
   question about which scenery is interactive, and how a player is meant to tell.

10. **"No gun sound."** The sidearm fires silently.

11. **"No menu music — maybe just use the radio station at 100% clarity?"**

    *(note)* Fred's own suggestion, recorded as given. `RadioStream` already streams a
    live station and `CockpitRadio` already applies the radio-band degradation, so the
    pieces exist; the menu would want the clean signal rather than the in-cockpit one.

### What this session did NOT find

Worth stating plainly: the session before this playtest fixed the interface drawing off
the edge of the screen, a fatal crash on quit, an unreadable cockpit panel and an assist
ladder that was never wired up. **None of those are in the list above.** Every item here
is something no headless check was ever going to catch, which is the argument for doing
this far earlier and far more often than once.
