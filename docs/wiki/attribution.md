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

## Libraries

| Library | Version | Licence | Redistributed? | Used by |
|---|---|---|---|---|
| [NLayer](https://github.com/naudio/NLayer) | 1.16.0 | **MIT** | Yes — ~80 KB DLL beside the game assembly | `game/scripts/RadioStream.cs` — decodes the cockpit radio's MP3 stream |

NLayer is a pure-managed MP3 decoder. It is here because Godot cannot decode MP3
incrementally: `AudioStreamMP3` wants a complete buffer and there is no streaming API, so
a live radio station cannot be played without an external decoder. NLayer decodes from a
`Stream`, which is exactly the shape a socket has, and being pure managed it needs no
native binary and no shelling out to ffmpeg.

MIT obliges us to ship the licence text with any binary distribution. If a build is ever
handed to anybody, `LICENSE` from the NLayer repository goes beside the executable.

## Music — and the thing to know about it

**No music ships with this game, and none ever has.** The cockpit radio (D-058) tunes a
**live internet radio station** — see `sim/src/Radio.cs`, `game/scripts/RadioStream.cs`
and `game/assets/radio/stations.txt`. Nothing is baked in, nothing is stored, nothing is
redistributed with the build.

**That is not the same as being in the clear, and this is the row that matters:**

> ### ⚠ Streaming a third-party station is fine for a private prototype and is NOT shippable.
>
> A build that connects to somebody's Icecast server and plays their broadcast to a
> player is **retransmitting** it. For Fred, alone, on his own machine, that is
> indistinguishable from opening the station in a browser and nobody cares. The moment a
> build goes to anybody else — a friend, an itch.io page, a Discord — it is a licensing
> question with a real answer, and the answer is not "we did not think about it".
>
> The original brief said to track anything that would matter if the game were ever
> shared. This is squarely that category, which is why it is in a box.
>
> **What shipping would actually require**, roughly in order of effort:
> 1. Licence music properly (CC BY / CC BY-SA tracks, or a library licence), bundle it,
>    and list every track in the table below; or
> 2. Get written permission from a station to retransmit; or
> 3. Ship with an empty `stations.txt` and let each player enter their own URL, which
>    moves the act from the developer to the listener. This is the cheapest option and it
>    is one line of config.

**The other thing, and it is not solved either:** real stations carry **adverts and DJ
chatter**, and they will arrive at the worst possible moment — a voice reading a phone
number over a forced landing. Nothing engineers around this. The only mitigation is the
station list: the defaults are **SomaFM**, which is listener-supported, runs no adverts,
and publishes direct MP3 stream URLs.

| Station | Operator | How it is funded | URL in `stations.txt` |
|---|---|---|---|
| Indie Pop Rocks | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/indiepop-128-mp3` |
| Left Coast 70s | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/seventies-128-mp3` |
| Metal Detector | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/metal-128-mp3` |
| Boot Liquor | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/bootliquor-128-mp3` |
| Underground 80s | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/u80s-128-mp3` |
| DEF CON Radio | SomaFM | Listener-supported, no adverts | `ice1.somafm.com/defcon-128-mp3` |

These are defaults in a config file, not assets. Edit `game/assets/radio/stations.txt` (or
drop a `radio_stations.txt` in the Godot user directory, which wins) to change the band.

**If music is ever bundled instead**, every track gets a row here — title, artist, licence,
source — and the credit text goes in **Credits owed** below. CC0, CC BY and CC BY-SA are
all acceptable. CC BY-NC is **not**: it forecloses ever selling the game, which is not a
decision to make by accident while dragging a folder around.

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

**Nothing bundled.** The only audio that is not generated by this project is the radio
stream, which is covered above.

Every sound the aircraft and the weather make is synthesised at runtime from simulation
state — `sim/src/RotorSynth.cs`, `WeatherSynth.cs`, `WarningSynth.cs`. There are no
recordings and no sample libraries, so there is nothing to licence. Blade slap runs at the
blade-pass frequency the sim computes, the turbine tone tracks the gas generator, and the
warning tones are generated from constants in the source. Verified spectrally against the
physics: `dotnet run --project tools/simlab -- audio` writes `builds/audio/sortie.wav`.

The one exception is the radio: it is the only audio in the project that is somebody
else's work, and it is the only part that is not shippable as it stands.

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

MP3 decoding
  NLayer, MIT — github.com/naudio/NLayer

Music
  Streamed live; nothing bundled. Stations are configured by the player.
  Default stations courtesy of SomaFM (somafm.com), listener-supported.
```

---

## Still to source

Tracked here so that a gap is visible rather than forgotten.

- Licensed music for the cockpit radio, **if this is ever shared with anybody**. The radio
  works now by streaming a live station, which is the right answer for a prototype and the
  wrong one for a build that leaves this machine. See the boxed note in the Music section.
- Helicopter airframe model (looking for CC0/CC-BY, Huey-like). Currently built from
  primitives in `AirframeBuilder.cs`.
- Vegetation and props, if the procedural ones ever stop being enough.
- A UI font with a bit more character than the Godot fallback.
- Site ambience — see the audio benchmark, which ranks "the world is silent" as the second
  biggest gap in the whole soundtrack. Likely CC0 field recordings, which will need rows
  here.
