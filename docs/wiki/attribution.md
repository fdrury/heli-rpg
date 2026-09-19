# Attribution register

**This is the canonical list of every third-party asset in ROTORWASH, its licence, where it
came from, and where it is used.** Nothing third-party ships without a row here.

Per D-058 and the original brief: free assets are fine, *including* copyleft and
attribution-required ones — but every one must be tracked, in case the game is ever shared.
Reconstructing provenance after the fact is miserable and sometimes impossible, so the rule
is that the row goes in when the file does, not later.

> The older `ATTRIBUTIONS.md` at the repository root is kept for now because other work
> references it. **This file is the one to update.** When they disagree, this one is right.

---

## How to add a row

1. Put the file in the repo.
2. Add a row to the right table below: what it is, where it came from (a URL, not "the
   internet"), the exact licence, and what it is used for.
3. If the licence requires attribution, make sure the credit text you owe is in the
   **Credits owed** section at the bottom — that section is what goes on a credits screen.

If you cannot answer "where did this come from", the asset cannot ship. That is not
pedantry: an asset with no provenance is the one that gets the whole project taken down.

---

## Engine and tooling

| Thing | Version | Licence | Redistributed? | Notes |
|---|---|---|---|---|
| [Godot Engine](https://godotengine.org) (.NET) | 4.7.2 | MIT | Exported games embed the engine | `tools/godot/` is gitignored |
| .NET SDK / runtime | 8.0 | MIT | Runtime ships with an export | Microsoft |

## Textures

All four terrain materials are from [ambientCG](https://ambientcg.com), released under
**CC0 1.0** (public domain dedication). No attribution is legally required; it is recorded
here anyway, and credited anyway, because the work deserves it.

| Asset | Source | Licence | Files | Used by |
|---|---|---|---|---|
| Grass005 | [ambientcg.com/view?id=Grass005](https://ambientcg.com/view?id=Grass005) | CC0 1.0 | `game/assets/terrain/grass_{col,nrm,rgh}.jpg` | `terrain.gdshader` — low ground |
| Ground054 | [ambientcg.com/view?id=Ground054](https://ambientcg.com/view?id=Ground054) | CC0 1.0 | `game/assets/terrain/dirt_{col,nrm,rgh}.jpg` | `terrain.gdshader` — worn dirt |
| Rock030 | [ambientcg.com/view?id=Rock030](https://ambientcg.com/view?id=Rock030) | CC0 1.0 | `game/assets/terrain/rock_{col,nrm,rgh}.jpg` | `terrain.gdshader` — steep faces |
| Gravel023 | [ambientcg.com/view?id=Gravel023](https://ambientcg.com/view?id=Gravel023) | CC0 1.0 | `game/assets/terrain/gravel_{col,nrm,rgh}.jpg` | `terrain.gdshader` — high bare ground |

## Music

**Nothing yet.** The radio system is built and the folder is empty; this table fills in as
tracks go in.

Music reaches the game through the cockpit radio (D-058) — `sim/src/Radio.cs` and
`game/scripts/CockpitRadio.cs`. The playlist comes from the folder layout; this register
and `game/assets/music/tracks.manifest` carry the licences.

| Track | Artist | Licence | Source | Tape (folder) | Credit required? |
|---|---|---|---|---|---|
| *(none yet)* | | | | | |

**How music gets added, in full:**

1. Put the tracks in `game/assets/music/<tape name>/` — one folder per cassette. Loose
   files directly in `game/assets/music/` become one more cassette. `.ogg` is preferred;
   `.mp3` and `.wav` also play.
2. Add one line per track to `game/assets/music/tracks.manifest`:
   `path | title | artist | licence | source | gain_db | seconds`
3. Add one row per track to the table above.

Steps 2 and 3 are the licensing; step 1 is all the game needs to make a noise. The radio
plays anything it finds and prints a warning at every start naming each track that has no
licence line, so an undeclared file is loud rather than silent.

**What is acceptable.** CC0, CC BY, CC BY-SA, and other free licences are all fine. CC
BY-NC is *not* — it forecloses ever selling the game, which is a decision nobody should
make by accident while dragging a folder around. Anything marked "free for
non-commercial", "free with credit for personal projects", or with no licence statement at
all is not free and does not go in.

**What is owed.** CC BY and CC BY-SA require credit in the work. That is what the
**Credits owed** section below is for. CC BY-SA additionally requires that derivatives of
*that track* be shared alike — the game as a whole is not a derivative of a track it plays
alongside, but a remix or an edit of one would be, so do not edit a BY-SA track.

## Fonts

| Font | Source | Licence | Used by |
|---|---|---|---|
| Godot fallback font (Open Sans subset) | Bundled with Godot | Apache 2.0 | `ThemeDB.FallbackFont` — HUD, kneeboard, warning panel, radio readout |

## Models, props and vegetation

**Nothing yet.** Every prop in the game is generated in code
(`game/scripts/ProceduralProps.cs`, `SiteKit.cs`, `AirframeBuilder.cs`) — no mesh has been
imported, so there is nothing here to licence. That is a deliberate position and it is the
reason this table is empty rather than out of date.

## Audio

**Nothing yet, apart from whatever goes in the Music table above.**

Every sound the aircraft and the weather make is synthesised at runtime from simulation
state — `sim/src/RotorSynth.cs`, `WeatherSynth.cs`, `WarningSynth.cs`. There are no
recordings and no sample libraries, so there is nothing to licence. Blade slap runs at the
blade-pass frequency the sim computes, the turbine tone tracks the gas generator, and the
warning tones are generated from constants in the source. Verified spectrally against the
physics: `dotnet run --project tools/simlab -- audio` writes `builds/audio/sortie.wav`.

The one exception, when it arrives, is the music: it is the only audio in the project that
is somebody else's work.

## Local language model (if it ships)

Not currently bundled. If the dialogue coda model (D-006a) is ever shipped with the game,
its weights are a third-party asset and get a row here. The benchmark
(`docs/wiki/benchmarks/local-llm-dialogue.md`) already did the licence analysis: **Qwen3
Apache-2.0 is redistributable; Llama 3.2 is disqualified by its licence**, not by quality.

## Reference material

Not assets, and nothing here is redistributed. Listed so the flight model's numbers can be
checked against their sources.

- Prouty, *Helicopter Performance, Stability and Control* — blade element formulation.
- Johnson, *Helicopter Theory* — empirical induced-velocity curve through vortex ring
  state; momentum theory branches.
- Padfield, *Helicopter Flight Dynamics* — dynamic inflow, flapping dynamics.
- Cheeseman & Bennett (1955) — ground effect.
- Drees (1949) — linear inflow distribution in forward flight.

---

## Credits owed

The text that must appear in the game's credits, verbatim-ish, to satisfy
attribution-required licences. Anything CC0 or MIT does not *have* to be here; it is here
because it should be.

```
Terrain materials
  Grass005, Ground054, Rock030, Gravel023 — ambientCG (ambientcg.com), CC0 1.0

Engine
  Godot Engine 4.7.2, MIT — godotengine.org
  Open Sans, Apache 2.0

Music
  (nothing yet)
```

---

## Still to source

Tracked here so that a gap is visible rather than forgotten.

- Rock music for the cockpit radio — **the system is built and waiting**; see the Music
  section above for exactly what to do with a folder of tracks.
- Helicopter airframe model (looking for CC0/CC-BY, Huey-like). Currently built from
  primitives in `AirframeBuilder.cs`.
- Vegetation and props, if the procedural ones ever stop being enough.
- A UI font with a bit more character than the Godot fallback.
- Site ambience — see the audio benchmark, which ranks "the world is silent" as the second
  biggest gap in the whole soundtrack. Likely CC0 field recordings, which will need rows
  here.
