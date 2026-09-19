# Visual check — brief for the test machine

This repository is developed on a laptop that cannot comfortably render it. Everything
headless is verified here; **anything that has to be looked at is verified on the test
machine**. This page is the brief for whoever (or whatever) is doing the looking.

The rule this project runs on: *every real bug so far was invisible in code review and
obvious in a measurement or a render.* So the job is not "does it look nice". The job is to
catch the thing that is wrong and that nobody can see from here.

## Running it

```
# Headless — these should already pass, but run them first so a render failure is
# never confused with a logic failure.
dotnet run --project tools/simlab -c Release -- all
godot --headless --path game -- --selftest
godot --headless --path game -- --looptest
godot --headless --path game -- --worldreport

# The render set. Writes builds/screenshots/*.png and prints the conditions of each shot.
godot --path game --resolution 1600x900 -- --screenshot
```

The screenshot run prints a line per shot giving the in-game date, sun elevation and full
weather. Quote those when reporting — "dark" means something different at 54° and at 5°.

## What each shot is for

| Shot | What it is checking |
|---|---|
| `01`–`02` hover / cruise chase | General read. Airframe against terrain, haze, scale. |
| `03` cockpit | Interior geometry, glass clarity, instruments visible at the bottom. |
| `04` low level | Ground detail and prop density at 35 m. |
| `05`, `07` orbit | **The airframe close up.** Panel lines, sun bleaching, dirt streaks, repair patches. |
| `06` high cruise | Aerial perspective and the far-field terrain palette. |
| `08`–`09` brownout | Dust, and whether it obscures without turning the screen to soup. |
| `10`–`13` sites | Settlements, airfields, relays, depots — is the place legible from the air. |
| `14`–`17` dawn / dusk / night | Time of day, and whether night is dark but flyable. |
| `18`–`19` rain | Rain density and slant, and the wet-weather grade. |
| `20`–`22` high country | The steep terrain. Rock on slopes, strata banding, valleys. |

## Open questions I cannot answer from here

These are the things most likely to be wrong. Please look specifically.

1. **Airframe weathering at close range** (`05`, `07`). It was made to be seen from about
   25 m. Is the panel-line spacing plausible, or does it read as a grid drawn on the
   outside? Do the repair patches read as replaced panels or as stains?
2. **Prop tinting** (`04`, `05`). Trees are one mesh per variant with a per-instance colour.
   Does a hillside read as woodland, or still as repeated blobs?
3. **Rock on steep ground** (`20`–`22`). Rock now starts around 33°. Too much, too little,
   or in the wrong places?
4. **Night** (`16`, `17`). Dark enough to be night, light enough to fly? The landing light
   and instrument glow should be useful without washing the horizon out.
5. **Rain** (`18`, `19`). Density, and whether the slant reads as wind.
6. **Frame rate.** The per-shot line reports fps. Anything under 30 at 1600×900 is worth
   naming, with which shot.

## One thing that needs a stick, not a screen

`Stability` now has an assist ladder — **Off / Light / Standard / Full** (`Sas.Set(...)`).
Three rungs are measurable and measured: Standard and Full hold trim hands-off for 22 s and
indefinitely, against 6.5 s bare.

**Light cannot be measured from here, and I stopped trying.** It is rate damping with a
light hand on heading and deliberately *no* attitude levelling — so it cannot win on
hands-off survival (that is levelling's job), a disturbance never "settles" because the
aircraft keeps rolling, and peak roll rate for a fixed input actually *rises* with assist
because an augmented aircraft answers a held stick more crisply. All three of those are real
properties, and none of them is what Light is for.

What Light is for is how the aircraft feels over seconds of continuous correction: fewer
over-corrections, less chasing. That is a judgement someone makes at a stick.

**So: fly a minute of low-level manoeuvring at each of Off, Light and Standard, and say
whether Light earns its place** — or whether the ladder should just be Off / Standard / Full.
A useless rung is worse than no rung, because it implies a choice that does not exist.

## How to report back

Facts, with the shot name and the conditions line. "18_rain at D173 16:45, sun 26°: the
drops nearest the camera are so long they read as scratches" is actionable. "Rain looks
wrong" is not.

If something is clearly broken rather than merely ugly, say so plainly and include the
console output — a shader that fails to compile, a missing mesh, or a crash all print.
