# Benchmark — world scale and content density

*Research date: 2026-09-18. Compiled against `docs/wiki/00-vision.md`, `decisions.md` and the
current code in `game/` and `sim/`.*

---

## Bottom line

**The map is already the right size — 16.384 km square is fine — but almost every other number
in the plan is wrong, and the biggest error is not the map, it is the aircraft.** The brief
assumes ~60 kt / 110 km/h; the Huey in `sim/` cruises at 100–116 kt with power to spare
(measured, below), which halves every crossing time: edge-to-edge is **4 min 49 s**, not nine.
That is almost exactly the measured time to fly across *GTA V* (4 min 48 s), a world universally
described as feeling small from the air. Second: the helicopter does **not** create a density
problem — moving fast makes you sweep more ground per second, so you *encounter* more, not less.
It creates a **duration** problem: you consume the world faster and can see all of it in the
first session. Third: 268 km² is 3.4× *Elden Ring*'s measured area for one developer, and terrain
is the cheap part — at a *Skyrim*-like density of ~9 POIs/km² this map would need ~2,400 authored
locations, which is roughly 150 person-years at Bethesda's own observed rate. The defensible
target is **16.4 km square, ~120 named POIs (≈0.45/km²), of which ~45 are hand-authored and ~75
are kit-assembled**, sitting on top of a few thousand unnamed procedural features that exist
purely to make the world read as inhabited from 300 m. The three things that actually buy felt
size are route inflation via threat envelopes (D-010 is the right spine and is load-bearing),
altitude-banded legibility, and making *landing*, not flying, the expensive act. Fuel (D-007)
cannot be a range constraint at this scale — one tank covers the map diagonal ~34 times — so it
must be honestly re-framed as an economy and load system, not a traversal gate.

---

## 0. Source confidence

Claims are flagged the same way as `progression.md`:

- **[M]** measured here, in this repo, today — the strongest class of evidence in this document.
- **[V]** verified against a source cited inline during this research.
- **[D]** **disputed** — a figure that circulates widely but whose derivation is unclear, or where
  credible sources disagree. Open-world area figures are unusually bad for this: most published
  numbers are marketing claims, pixel-measurements of a map *image* (which is not to scale with the
  world), or include ocean and unreachable space.
- **[U]** unverified — stated from general knowledge or a source that could not be re-fetched.
  Treat every **[U]** as a hypothesis.

The session's web-search budget was exhausted during this research, so some figures in §1 could not
be cross-checked against a second independent source. **Treat the whole of §1 as order-of-magnitude
guidance, not precision data.** The argument in §2–§5 is deliberately built so that it does not
depend on any single figure in §1 being exactly right — it depends on the *ratios*, which are robust
across every source consulted.

### Measurements taken from this project (primary, not web) **[M]**

These come from the repo and from running the test bench on 2026-09-18, and they override the
assumptions in the brief.

| Fact | Value | Source |
|---|---|---|
| World half-extent | 8,192 m | `game/scripts/WorldHeight.cs:24` |
| World side / area / diagonal | 16.384 km / **268.4 km²** / 23.17 km | derived |
| Huey `Vne` | 62.0 m/s = **120.5 kt** = 223 km/h | `sim/src/Airframe.cs:99` |
| Hover power | 743 kW | `dotnet run --project tools/simlab -- powercurve` |
| Minimum-power speed | **58 kt** @ 462 kW | same |
| Power at 116.6 kt | 670 kW of 946 kW available | same |
| Fuel capacity / SFC | 800 kg / 9.2e-8 kg·W⁻¹·s⁻¹ | `sim/src/Airframe.cs` |
| Endurance / still-air range at cruise | **3.88 h / ~790 km** | derived |
| Hover ceiling OGE | **~3,000 m** | `simlab ceiling` |

That ceiling matters for §4.1: the player's usable altitude band is 0–3,000 m and realistically
0–1,500 m loaded, which is exactly the band a tethered aerostat and medium SAMs cover. There is no
"just climb above it" escape, and that is a gift — altitude is a genuinely contested axis.

**The brief's "roughly 60 knots" is the minimum-power (best-endurance) speed, not cruise.** A real
UH-1H cruises around 110 kt and the model reproduces that. Players will not fly at 58 kt; they
will fly at whatever gets them there.

### Crossing times, corrected

| Leg | Distance | @58 kt (107 km/h) | @110 kt (204 km/h) | @120 kt (222 km/h) |
|---|---|---|---|---|
| Edge to edge | 16.38 km | 9 m 11 s | **4 m 49 s** | 4 m 25 s |
| Corner to corner | 23.17 km | 12 m 59 s | **6 m 49 s** | 6 m 15 s |
| Quarter-map mission leg | 4–6 km | 2 m 15 s – 3 m 22 s | **1 m 11 s – 1 m 46 s** | 1 m 5 s – 1 m 37 s |

For comparison, *How Big Is The Map* measured **4 m 48 s** to fly across *GTA V*
([source](https://howbigisthemap.com/gta-v-fly-across-the-map/)), **1 h 50 m 16 s** to walk across
*Skyrim* ([source](https://howbigisthemap.com/skyrim-walk-across-the-map/)), **7 h 51 m 08 s** to
walk across *Just Cause 3* ([source](https://howbigisthemap.com/just-cause-3-walk-across-the-map/)),
and **34 m 32 s** to walk / **18 m 59 s** to drive across one *Far Cry 2* map
([walk](https://howbigisthemap.com/far-cry-2-map-2-walk-across-the-map/),
[drive](https://howbigisthemap.com/far-cry-2-map-2-drive-across-map/)).

**So: Rotorwash at 16.4 km, flown properly, has the same edge-to-edge traversal time as flying
across Los Santos.** That is the single most useful sentence in this document.

### Fuel cannot gate range

At 107 kt the model burns 0.057 kg/s. 800 kg of fuel is **3.88 hours** and **~790 km** of still-air
range — about **34 full diagonal crossings of the map on one tank**. No amount of tuning fixes this
without making the fuel system absurd (a real Huey really does have ~2.5–3.9 h of endurance, and
the map really is only 23 km across). D-007 is still a good system, but it is an *economy and
load-management* system — fuel costs money, fuel is heavy, fuel is scarce at the pump, a full tank
kills your hover ceiling — not a *range* system. The document should say so rather than implying
the player will ever be navigating by fuel state over 6 km.

---

## 2. The central problem, analysed properly

### 2.1 It is not the problem the brief thinks it is

The brief frames fast traversal as a *density* problem: the player crosses the map quickly,
therefore the world feels empty, therefore we need more stuff per km². That is backwards.

Model the encounter rate directly. A player at speed `v` with an identification corridor of width
`w` (how far to either side you can recognise a place as *worth going to*, not merely see it)
sweeps `v·w` of ground per second. Time between POIs is `1 / (v·w·ρ)` for POI density `ρ`.

| Speed | Corridor | One POI every 40 s needs | …every 60 s | …every 120 s |
|---|---|---|---|---|
| 58 kt | 1.0 km | 0.84/km² (225 on our map) | 0.56/km² (150) | 0.28/km² (75) |
| 58 kt | 1.5 km | 0.56/km² (150) | 0.37/km² (100) | 0.19/km² (50) |
| 110 kt | 1.0 km | **0.44/km² (119)** | 0.30/km² (79) | 0.15/km² (40) |
| 110 kt | 1.5 km | **0.30/km² (79)** | 0.20/km² (53) | 0.10/km² (26) |

Flying *faster* lowers the density you need, because you cover more area per unit time. And
flying *higher* widens the corridor, lowering it further. The helicopter is, in raw
encounter-rate terms, the easiest traversal mode in games to feed content to.

The 40-second figure is CD Projekt Red's stated rule for *The Witcher 3* — "the rule of 40
seconds", that the player should stumble on something interesting every 40 seconds of
exploration — examined at length in
[Jaber, *The 40 Seconds Rule and Points of Interest in The Witcher 3*, Uppsala University](https://uu.diva-portal.org/smash/get/diva2:1569059/FULLTEXT01.pdf).
A 60–120 s band is the more commonly recommended working range
([StraySpark, open-world pacing](https://www.strayspark.studio/blog/open-world-design-pacing-player-freedom) —
a studio blog, not a shipped-game measurement; treat as a heuristic).

**What the helicopter actually destroys is duration.** A world contains a fixed amount of
content. Speed is a divisor on how long that content lasts, and — worse — a helicopter removes
every natural ordering constraint. In *Skyrim*, mountains decide what you meet and when. In
Rotorwash, nothing does. The failure mode is not "this world feels empty"; it is **"I saw the
whole map in the first ninety minutes and nothing surprises me any more."** That is the same
failure as *Just Cause 3*, whose wingsuit-and-grapple traversal reaches anything, and which
reviewers described as a world where "a lot of the game feels like filler to justify its massive
size" and "even the interesting ruins scattered around the map are empty and pointless"
([Screen Rant](https://screenrant.com/just-cause-3-review/)); the community complaint that "the
entire top half of the world map in JC3 is empty" is the same wound
([Steam discussion](https://steamcommunity.com/app/225540/discussions/0/487876474228084266)).

The two correct responses are therefore **(a) more time spent at each place** and **(b) gating so
the map cannot be consumed in one sitting** — *not* more POIs per km². Both are already in the
plan: (b) is D-010 and pillar 4; (a) is where landing, the refit system and the on-foot layer have
to carry weight they are not currently asked to carry.

### 2.2 How each fast-traversal game handled it

| Game | Fast traversal | How it handles the consequences | Does it work? |
|---|---|---|---|
| **Just Cause 3** | Wingsuit + grapple, effectively unlimited, over a 1,000 km² island chain | Almost nothing. The same liberation loop repeated across near-identical settlements | **No.** The canonical failure case, and the closest structural analogue to Rotorwash's risk |
| **GTA V** | Planes and helicopters; 4 m 48 s corner to corner | Aircraft are *late and optional*. The main game is on roads, in a city whose density is the point. Flight is a reward and a mission verb, never the default | Yes — but by demoting flight |
| **Mad Max (2015)** | Fast car across a large desert | Fuel scarcity, threat (convoys, snipers), and a stronghold-upgrade economy that forces return trips. Regions gate on story progress | Partly. Widely seen as repetitive; the desert is honest but the activity vocabulary is thin |
| **Death Stranding** | Deliberately the slowest traversal in the genre | The opposite strategy: make *movement itself* the content — balance, load, terrain reading — then let the player **earn** speed (ropes, roads, zip-lines) as progression | Yes, and it is the most instructive case here |
| **Elden Ring** | Torrent — fast, double-jumps, spirit-springs — plus fast travel from almost anywhere | Density of *consequence* rather than density of markers. No icon vomit; the world is read visually. Heavy vertical layering (Siofra, Ainsel, Deeproot) multiplies content per km² of footprint | Yes |
| **Breath of the Wild** | Paraglider + climb-anything; one glide from a tower crosses a lot of map | The "triangle rule": terrain shaped so obstacles both *block sight* and *offer a route over*, at three scales — landmark, sight-blocker, tempo-changer | Yes — the best pure-terrain answer |
| **Microsoft Flight Simulator** | The fastest traversal in games, over the whole planet | Concedes the density fight entirely. ~37,000 procedurally generated airports with a few dozen **hand-crafted** ones plus photogrammetry cities. The content is the *aircraft* and the *procedure*, not the ground | Yes — but it is a different genre, and one we cannot copy |
| **Far Cry 2** | Vehicles; ~19 min to drive across a map | Persistent hostility: checkpoints respawn, so the journey is content by force. Five bus stops per map as a pressure valve | Mixed. The respawning checkpoints are its single most-hated feature |

Sources: [BotW triangle rule, Nintendo Life](https://www.nintendolife.com/news/2017/10/zelda_breath_of_the_wilds_ingenious_design_is_all_about_triangles_apparently)
· [BotW design lessons, Game Developer](https://www.gamedeveloper.com/design/5-design-lessons-learned-from-i-the-legend-of-zelda-breath-of-the-wild-i-)
· [Elden Ring sightline design](https://medium.com/@Jamesroha/world-design-lessons-from-fromsoftware-78cadc8982df)
· [Miyazaki interview, GamesRadar](https://www.gamesradar.com/elden-ring-fromsoftware-hidetaka-miyazaki-interview/)
· [MSFS hand-crafted airports](https://flight.wiki.gg/wiki/Microsoft_Flight_Simulator_(2020)/List_of_hand-crafted_airports)
· [What "hand-crafted" means in MSFS](https://flyawaysimulation.com/ask/answers/handcrafted-airports-microsoft-flight-simulator/)
· [Death Stranding traversal as content](https://harrygboulton.medium.com/slow-walking-and-world-wandering-in-death-stranding-9130bf61981)
· [Far Cry 2 fast travel](https://www.wholebeangames.com/blog/thoughts_on_games/the-lack-of-fast-travel-in-far-cry-2/)

### 2.3 The techniques that actually work, ranked for our case

**1. Route inflation — make the straight line unavailable.**
Effective world size is physical size multiplied by the ratio of *flown* path to *Euclidean* path.
This is the only lever that multiplies felt size at near-zero authoring cost, and it is exactly
what D-010 already proposes. A 6 km direct leg that becomes an 18 km dogleg down a river valley
at 200 ft to stay under a radar horizon is a **3× multiplier applied to the entire map**,
purchased with a data file. Nothing else in this document has that leverage. D-010 is not a
progression system with a pleasant side effect on scale; **it is the primary world-scale system**,
and it should be built before the second region is authored, not seventh on the STATUS list.

**2. Altitude-banded legibility — sell the same ground three times.**
A helicopter reads the world at three distinct scales, and a well-built world says something
different at each (see §4). Threat forces the player down; being down narrows the corridor `w`,
which raises effective density *and* turns navigation into a skill. This is the mechanism by
which D-010 converts into felt size, and it is why the aerostat / radar-SAM / MANPADS taxonomy
matters: each one should own a different altitude band, so the threat map is also an altitude map.

**3. Make arrival, not travel, the expensive act.**
*Death Stranding*'s lesson, transposed. Flying 4 km is ninety seconds. Finding a usable landing
site, judging slope and surface, clearing wires and obstructions, confirming you have the power
margin to get out again with cargo aboard, and putting it down without a dynamic rollover is
60–120 seconds of real skill under real consequence. If landing is a genuine skill check, 120
POIs become 120 *events* rather than 120 map markers, and the flight between them stops being the
unit of pacing. This is also the cheapest content in the project, because it is physics that
already exists in `sim/`.

**4. Withhold the map (pillar 4), and mean it.**
The strongest anti-consumption device available. A helicopter can reach anywhere; it cannot reach
what the player does not know exists. Chart acquisition is the ordering constraint that terrain
would otherwise supply — it is *the* substitute for mountains.

**5. Vertical layering.** *Elden Ring* multiplies content per km² of footprint with its
underground regions. Our equivalents: a flooded metro, a mine, a multi-storey car-park stack, a
dam's inspection galleries, high-rise interiors, a hospital. Every one of these is content a
helicopter structurally cannot skip, which is exactly why they are worth the cost.

**6. Weather and time of day as a size multiplier.** Visibility *is* the corridor width `w` in
§2.1. A 1 km-visibility morning makes the map four times bigger for free, and it is the most
honest difficulty slider a flight game has. Night plus low cloud plus a MANPADS envelope is a
whole different world over identical terrain.

**7. Anti-technique, to avoid: hostility everywhere.** *Far Cry 2*'s respawning checkpoints made
the journey content by force and are its most criticised feature. Threat envelopes should shape
routes, not tax every kilometre. Empty air between threats is what makes threatened air mean
anything.
---

## 3. Recommendation — world dimensions and POI count

### 3.1 Is 16 × 16 km right?

**Keep 16.384 km. Do not grow it. Do not shrink it yet.** The reasoning cuts both ways and it is
worth being explicit about both sides.

**It is too big by conventional density standards.** 268 km² is roughly 3.4× *Elden Ring*, 3.5×
*GTA V*, 1.9× *The Witcher 3*'s land area and 7× *Skyrim* — built by one person. At *Skyrim*'s
observed ~9 POIs/km² it would need ~2,400 authored locations; at *Elden Ring*'s it would need
~1,600. Both are an order of magnitude beyond anything one person can author (§3.4).

**It is not too big by traversal standards, and shrinking it would hurt.** At realistic cruise the
map is 4 m 49 s edge to edge — the same as flying across *GTA V*, which is the low end of
acceptable. Going to 12 km would put edge-to-edge at 3 m 32 s and quarter-map legs under a
minute, at which point the world stops reading as a country and starts reading as a level.

**The thing that resolves the contradiction is that emptiness is cheap when traversal is fast.**
This is the one genuine gift the premise gives us. Five kilometres of nothing is a fifty-minute
walk and a ninety-second flight. A helicopter is the only traversal mode in games that makes
large deliberately-empty country an *asset* — legible, navigable, atmospheric, thematically
correct for a collapsed world — rather than a tax on the player's patience. *Death Stranding*
spent enormous effort making empty terrain interesting because you had to walk it. We do not have
to, and we should not pretend we do.

So: **plan for ~35% of the map to carry content and ~65% to be honest, beautiful, empty ground.**

**Fallback rule if authoring slips.** Do not thin the density to cover the same area. If the POI
count looks like landing below ~80, pull the *playable envelope* in with a soft boundary (weather,
a fuel-range excuse, a no-go threat band) to 12–13 km and keep the density. A smaller dense world
beats a large thin one every time, and the terrain function makes this a one-constant change
(`WorldHeight.WorldHalfExtent`).

### 3.2 Minimum POI density that avoids "wide as an ocean"

Two separate densities matter, and conflating them is the usual mistake.

| Layer | What it is | Target density | Count on our map | Why |
|---|---|---|---|---|
| **Named POIs** | Map-markable places worth flying to and landing at | **0.40–0.50/km²** | **110–135** | From §2.1: at 110 kt with a 1.0–1.5 km identification corridor, this gives a point of interest every ~40–60 s of transit |
| **Absolute floor** | — | 0.25/km² | 67 | Below this the transit band goes quiet and the map reads as terrain with things on it |
| **Unnamed human features** | Wrecks, roadblocks, pylon runs, silos, blown bridges, sheds, fence lines, burnt copses | **8–15/km²** | **2,100–4,000** | This is what makes the world read as *inhabited* from 300 m. It is free (scatter rules) and it matters more than POI count for atmosphere |

**Recommendation: 124 named POIs (0.46/km²) and ~3,000 unnamed procedural features (11/km²).**

Note the asymmetry the brief did not anticipate: we need roughly **one twentieth** of *Skyrim*'s
POI density, and about **ten times** its density of unnamed environmental texture. That is the
correct shape for a game viewed from 300 m at 200 km/h.

### 3.3 Handcrafted vs procedural

| Tier | Count | Share | What it is | Where the content lives |
|---|---|---|---|---|
| **Anchor** | 11 | 9% | A settlement or major site: exterior layout, 3–8 interiors, 4–10 NPCs, dialogue, a quest chain, a difficult landing site | Hours of play each |
| **Site** | 45 | 36% | Fuel depot, SAM site, crashed airliner, drowned mall, fire lookout, rail bridge, dam gatehouse. Exterior plus at most one small interior, a hazard, loot, maybe one NPC | 15–40 min each |
| **Kit POI** | 68 | 55% | Assembled from a reusable kit, hand-placed, hand-named, one line of written flavour | 3–10 min each |
| **Scatter** | ~3,000 | — | Unnamed, rule-placed, never marked on the map | 0 — it is scenery and navigation |

**45% handcrafted by count, but ~85% of the player's on-the-ground time should be in handcrafted
places.** For contrast, *Microsoft Flight Simulator* is roughly 0.1% hand-crafted by count — a few
dozen hand-built airports out of ~37,000
([source](https://flight.wiki.gg/wiki/Microsoft_Flight_Simulator_(2020)/List_of_hand-crafted_airports)) —
and it works because the content is the flying, not the ground. We are an RPG and cannot go
anywhere near that ratio. Generic open-world guidance suggests 80/20 procedural/hand-placed
([StraySpark](https://www.strayspark.studio/blog/open-world-design-pacing-player-freedom)); our
55/45 is more handcrafted than that, correctly, because our absolute POI count is an order of
magnitude lower than a conventional open world's.

### 3.4 Realistic authoring cost — the honest part

Anchor figure from industry: **eight level designers built 150 dungeons for *Skyrim***
([TheGamer](https://www.thegamer.com/skyrim-development-trivia-stories/)) over roughly three
years of production — about **0.16 person-years, or ~300 hours, per dungeon**, *with a dedicated
art team supplying the modular kit* and a mature toolchain
([Burgess, GDC 2013, on Skyrim's modular level design](http://blog.joelburgess.com/2013/04/skyrims-modular-level-design-gdc-2013.html)).

Estimates for a solo developer with an AI assistant, at deliberately lower fidelity:

| Item | Unit cost | Count | Total |
|---|---|---|---|
| Anchor location | 120–250 h (call it 185 h) | 11 | **~2,035 h** |
| Site | 10–30 h (call it 20 h) | 45 | **~900 h** |
| Reusable POI kit (buildings + rules + variants) | 20–50 h (call it 35 h) | 10 | ~350 h |
| Kit POI instance (place, name, dress, one line) | 1–3 h (call it 2 h) | 68 | ~136 h |
| Scatter systems (wrecks, wires, roadblocks, pylons) | — | — | ~150 h |
| Per-region terrain, biome and landmark pass | ~30 h | 8 | ~240 h |
| **World content total** | | | **~3,800 h** |

**At 15 h/week that is 4.9 years. At 25 h/week, 2.9 years. At 40 h/week, 1.8 years — and this is
world content only**, excluding the refit system, the economy, the dialogue engine, audio, threat
systems, UI, the on-foot layer and the writing. The full plan as specified is a four-to-six-year
solo project, and it would be dishonest to write it down as anything else.

**Where the AI assistant actually helps, and where it does not.** Be specific, because the
difference decides whether these numbers hold:

| Genuinely accelerated (2–5×) | Barely accelerated (1.0–1.3×) |
|---|---|
| Systems code, data schemas, serialisation, tests | Layout and composition judgement |
| Placement, scatter and validation *tooling* | Sourcing and licence-clearing free assets (D-003 / ATTRIBUTIONS) |
| Dialogue first drafts and barks at volume | Lighting bakes and per-location tuning under **two** lighting paths |
| Procedural kit rules and parameterisation | Playtesting, feel, pacing |
| Documentation, naming, flavour text | Audio |

**The hidden tax nobody has costed yet: D-003.** Authoring every location to look correct under
both the baked-lightmap path *and* the SDFGI/SSIL path is roughly a 1.5–2× multiplier on lighting
work for every interior, and lighting is a large share of an interior's cost. It is the right
call for the target hardware, but it is *charged per POI*, and it is the single strongest argument
for the exterior-heavy, interior-light world shape recommended in §4.4.

### 3.5 The scope cut that should be planned for now, not later

Order the regions so that **the cheapest content ships first and the city ships last** (§5). Then
a scope cut removes the most expensive third of the project rather than gutting the tutorial.

| Cut | Regions | POIs | Area reached | World-content hours | At 15 h/wk |
|---|---|---|---|---|---|
| **v0.5 vertical slice** | Tidewater only | 18 | 32 km² | ~800 h | ~1.0 year |
| **v1.0 — recommended ship target** | Acts 1–2 (4 regions) | 62 | 145 km² | ~2,150 h | ~2.8 years |
| **v1.5** | + Act 3 (6 regions) | 90 | 217 km² | ~2,900 h | ~3.7 years |
| **Full plan** | All 8 regions | 124 | 268 km² | ~3,800 h | ~4.9 years |

v1.0 at 62 POIs over 145 km² is 0.43/km² — *effectively the full plan's density*, because the
unreached regions are behind threat gates that the fiction already justifies. A player who
finishes Acts 1–2 has played a complete game. That is the whole point of building D-010 early.
---

## 4. Verticality and the aerial view as a design resource

### 4.1 The three altitude bands

Almost every open world is authored for one viewing distance: eye height, 1.7 m, looking roughly
horizontally. Rotorwash has three, and they are genuinely different design problems. Treat them
as three separate authoring passes with three separate acceptance criteria.

| Band | Altitude | What the player is doing | What the world must supply | Feature scale that reads |
|---|---|---|---|---|
| **Transit** | 300–1,000 m AGL | Macro navigation, route planning, threat avoidance | Region identity, global landmarks, linear features to follow, an honest silhouette of where you are going | > 100 m. Rivers, ridgelines, motorways, city blocks, a burn scar, a lake |
| **Survey** | 60–300 m AGL | "Is that place worth landing at? Is anyone home?" | POI identity and *state* — occupied vs abandoned, hostile vs not, intact vs burnt | 10–100 m. Roof shapes, vehicle clusters, smoke, lit windows, fresh tracks, cleared ground |
| **Arrival** | 0–60 m AGL | Choosing a landing site and surviving it | Slope, surface, obstructions, wires, wind shelter, rotor clearance, escape path | 1–10 m. Wires, poles, fences, debris, canopy, dust/spray surface |

This banding is the most important structural idea in this document after route inflation,
because it means **one piece of terrain pays three times**, and because the threat system can be
built directly on top of it: each threat class owns a band, so the threat map is also an altitude
map, and altitude is a control the player holds continuously.

### 4.2 What reads from the air, and what does not

**Reads well (build the world out of these):**

- **Silhouette against sky** — cooling towers, chimneys, cranes, masts, water towers, pylons,
  church spires, a bridge span, a stadium bowl, a standing high-rise. These are the *only* things
  visible from 8 km, and they are what a pilot navigates by in reality.
- **Linear features** — motorways, rail lines, canals, rivers, powerline cuts, runways, pipelines,
  levees, firebreaks. Pilots follow lines. A line is a free quest hook, a free route inflation
  device (follow the valley to stay masked) and a free region boundary.
- **Albedo and texture fields** — water, salt, burnt ground, ploughed vs fallow, forest vs scrub,
  concrete vs soil, snow line. From 500 m, colour blocks *are* the map.
- **Large geometric footprints** — car parks, reservoirs, quarries, solar farms, rail yards,
  airfields, container stacks, tank farms, cemeteries. Regularity reads as "human" instantly and
  at very long range.
- **Dynamic signals** — smoke, steam, fire, lit windows, moving vehicles, a beacon, dust from a
  convoy, birds off a roof. These are visible far beyond the range at which geometry resolves,
  and they are the cheapest possible "somebody is there" signal.
- **Negative space** — a cleared circle, a swept LZ, a break in a treeline, a drained reservoir.
  Absence reads as intent.
- **Shadow** — at low sun, a 20 m structure throws a 100 m shadow. Long shadows are an aerial
  detail multiplier and cost nothing but a time-of-day choice.

**Reads badly (do not spend money here for the aerial view):**

- Interior detail, doorways, furniture, anything under a roof or canopy.
- Props under ~3 m — litter, crates, small rocks, individual bodies.
- Signage and text of any kind; NPC faces; small animations.
- Subtle terrain relief. From 300 m a 5 m bank is invisible; from 10 m it will roll your aircraft.
- Anything that relies on being approached from one specific direction. A helicopter arrives from
  an arbitrary bearing and an arbitrary altitude. There is no "front door" to a location.
- Ground-level set dressing intended to be read while walking past it at 1.4 m/s.

**The single most important consequence: a helicopter game can afford a city that a ground game
cannot, and cannot afford a forest floor that a ground game can.** A ruined city read from 200 m
is a *pattern of blocks, canyons, standing towers and collapse* — exactly what procedural
generation is good at, with hand authoring reserved for a dozen landmark silhouettes and the
handful of places you actually land. A forest read at 10 m from a hovering aircraft is a mess of
individual trunks, wires and clearances — expensive, and it will fight the flight model. Skew the
budget accordingly.

### 4.3 Which games understand this

| Game | Verdict |
|---|---|
| **Microsoft Flight Simulator** | Completely. Its POI list is selected for aerial silhouette, and it is explicit that photogrammetry alone does not make a location "hand-crafted" — the *shape* has to be built ([source](https://flyawaysimulation.com/ask/answers/handcrafted-airports-microsoft-flight-simulator/)) |
| **Elden Ring** | Yes, at the global scale. The Erdtree is a single landmark visible from nearly everywhere, and Miyazaki describes it explicitly as a navigation device as well as an identity ([GamesRadar](https://www.gamesradar.com/elden-ring-fromsoftware-hidetaka-miyazaki-interview/)). Sightline-driven navigation is the whole method ([analysis](https://medium.com/@Jamesroha/world-design-lessons-from-fromsoftware-78cadc8982df)) |
| **Breath of the Wild** | Yes. The triangle rule is literally a rule about silhouettes and sight-blocking, and the game's signature moment is *gliding from a high place and choosing a destination by eye* ([Nintendo Life](https://www.nintendolife.com/news/2017/10/zelda_breath_of_the_wilds_ingenious_design_is_all_about_triangles_apparently)) |
| **GTA V** | Yes, by instinct. Vinewood sign, Maze Bank, the Alamo Sea, the LS river, the airport, Mount Chiliad — every one is an aerial landmark, and the road network is a legible linear grammar from 500 m. It is the best existing model for our *transit* band |
| **RDR2** | No, and it does not need to be — there is no aircraft. Its detail budget is all in the survey/arrival bands. A useful negative control: it proves how much of a beloved world is invisible from 300 m |
| **Skyrim** | No. It works from the ground and becomes an unreadable maze from above; the *tes5edit*-flying experience is famously disappointing. Its landmarks (Throat of the World, the Whiterun palace) are good, but the connective terrain is not designed to be seen |
| **Fallout 4** | A direct cautionary tale — it has the closest existing thing to our premise (the Vertibird), and the ride is the moment the Commonwealth's repetition and flatness becomes obvious. *(My assessment, not a sourced claim.)* If we do nothing else, we must not ship a world whose flaws are exposed by its own core verb |
| **Just Cause 3** | Terrain reads beautifully from the air; *content* does not. Settlements are indistinguishable from 300 m, which is precisely the failure the reviews describe |

### 4.4 Concrete authoring rules that fall out of this

1. **Every region must have at least one silhouette landmark visible from 6 km** and at least
   three visible from 2 km. If a player cannot orient without the map, the region is not finished.
2. **Every POI must be identifiable by roof-and-footprint alone.** The test: render it from 200 m
   with no labels; can the player tell what it is and whether it is occupied? If not, add a
   silhouette element or a dynamic signal, not more ground detail.
3. **Author the landing site before the building.** For every POI, decide where the aircraft goes
   and what makes that hard — slope, wires, trees, a hot-and-high approach, a one-way-in canyon.
   That constraint should come *first*, because it is the content.
4. **Wires, poles and masts are a first-class world-building system**, not decoration. They are
   the main hazard of real low-level rotary flight, they read as civilisation from 300 m, they are
   almost free to generate procedurally along road and rail splines, and they make every low
   approach a decision.
5. **Budget the interior/exterior ratio at roughly 1:4.** Interiors are where the cost is (see
   §3.4) and where the aerial view earns nothing.
6. **D-003's dual lighting path is charged per authored location.** Every interior must be
   approved under both the baked-lightmap and SDFGI paths. This is a real and compounding tax on
   POI count — see §3.4 — and is the strongest argument for keeping interiors few and exteriors
   many.
---

## 5. Proposed region breakdown

Eight regions on the existing 16.384 km square. Coordinates are Godot world axes as used in
`WorldHeight.cs` — **X east, Z south, origin at map centre, ±8,192 m**. Boundaries are soft:
they are biome-blend zones and threat-envelope edges, never walls.

The ordering is chosen so that **cost rises monotonically with act**. Act 1 regions are roads,
sheds and water; Act 4 is a city and a military complex. If the project runs short of time, the
cut lands on the expensive end and the game still has an ending.

| # | Region | Bounds (X, Z metres) | Area | Act | POIs (A/S/K) | Gate — the key that opens it |
|---|---|---|---|---|---|---|
| 1 | **Tidewater** | X −8192…−3000, Z +2000…+8192 | 32.2 km² | 1 | **18** (2/6/10) | None. Start here |
| 2 | **The Interchange** | X −3000…+2500, Z +2000…+8192 | 34.1 km² | 1 | **20** (2/7/11) | None. Small arms and technicals only |
| 3 | **The Weal** | X −3000…+2500, Z −4500…+2000 | 35.8 km² | 2 | **14** (1/5/8) | **MANPADS.** Flares + IR suppressor, *or* fly the hedgerows with terrain-masking charts |
| 4 | **The Pines** | X −8192…+3500, Z −8192…−4500 | 43.3 km² | 2 | **10** (1/4/5) | **Weather and a mobile IR team.** RWR to find it; charts to route round it |
| 5 | **Cold River** | X +2500…+8192, Z −4500…+2000 | 37.1 km² | 3 | **16** (1/6/9) | **Fixed radar SAM ring.** RWR + chaff |
| 6 | **Saltback Flats** | X +2500…+8192, Z +2000…+8192 | 35.3 km² | 3 | **12** (1/4/7) | **Long-range radar over ground with no masking.** Chaff + jammer, or night and low visibility |
| 7 | **Grayling Range** | X +3500…+8192, Z −8192…−4500 | 17.4 km² | 4 | **8** (1/4/3) | **Emitter locator.** The source of every countermeasure; the reason everything else is defended |
| 8 | **Ashmount** | X −8192…−3000, Z −4500…+2000 | 33.8 km² | 4 | **26** (2/9/15) | **Tethered aerostat + flak + drone patrol.** Jammer, then kill the aerostat. Endgame |
| | **Total** | | **269 km²** | | **124** (11/45/68) | |

*(A = Anchor, S = Site, K = Kit POI — tiers defined in §3.3.)*

### Region detail

**1 · Tidewater** — 32 km², 0.56 POI/km², Act 1, no gate.
A silted river mouth and the drowned suburbs behind it. Tidal flats, half-submerged cul-de-sacs,
a marina with boats on their sides, a causeway that floods twice a day, sunken car roofs breaking
the surface. The starting settlement is a marina and boatyard that has become a town, because the
only working infrastructure left is a slipway and a fuel bowser.
*Aerial signature:* water. Albedo does the work — the whole region reads as a pale delta from
6 km, with the causeway as its one strong line. *Landmark:* a bascule bridge stuck half-raised.
*Landing pressure:* soft ground, tide state, and a limited number of surfaces that will take the
skids. This is the tutorial for "arrival is the hard part" and it costs almost nothing to build.
*Anchors:* the marina town; the lock-keeper's compound.

**2 · The Interchange** — 34 km², 0.59 POI/km², Act 1, essentially no gate.
Pillar 5 made literal: the ragged country between things. A collapsed four-level stack
interchange, ribbon development, a strip mall, a distribution warehouse the size of a village, a
retail park, a caravan site, a light-industrial estate, a scrapyard, a car auction. **The highest
POI density in the game and the cheapest per POI**, because roads, sheds, car parks and pylons are
the ideal kit content and they tile convincingly.
*Aerial signature:* geometry. Car parks, roof grids, the interchange spiral, powerline cuts. From
500 m this region is the most obviously *human* place on the map, which makes its emptiness land.
*Landmark:* the stack interchange, with one deck fallen through the others.
*Anchors:* a settlement inside the distribution warehouse; a scrapyard that is the first real
parts source for the refit system.

**3 · The Weal** — 36 km², 0.39 POI/km², Act 2, MANPADS gate.
Farm belt and low hill country. Hedgerows, silos, a grain terminal, an overgrown airstrip, a
village, wind-thrown orchards, a reservoir. The gate is IR-guided man-portable missiles held by
people who live there — which means the region is passable at *very* low level along hedge lines
and stream cuts, and lethal at 300 m. This is the region that teaches terrain masking, and it is
the first place the player learns that altitude is a decision rather than a comfort.
*Aerial signature:* field pattern. Regular, coloured, with hedge lines as a navigation grid.
*Landmark:* a grain elevator and a lone church tower. *Anchor:* the village.

**4 · The Pines** — 43 km², 0.23 POI/km², Act 2, weather + mobile IR gate.
Forest, ridges and a hydroelectric dam. **Deliberately the sparsest region on the map**, and the
one that proves empty country can be good: ridge lines you follow, valleys that are dead ground,
cloud that sits on the tops, a fire lookout on every third summit that doubles as a chart source.
The threat is one mobile IR team that relocates, so the region is never *cleared*, only read.
*Aerial signature:* relief and canopy. Almost no geometry; all silhouette and shadow. The cheapest
region to build and the most valuable per hour spent.
*Landmark:* the dam wall and its spillway. *Anchor:* the dam and its inspection galleries — the
first vertical-layered interior, and one a helicopter cannot skip.

**5 · Cold River** — 37 km², 0.43 POI/km², Act 3, radar SAM gate.
A river valley full of the machinery that used to run the country: a refinery with a tank farm, a
rail yard, a coal-fired power station with two cooling towers, a lift bridge, a barge terminal.
Defended by fixed radar SAMs, because this is where the fuel is — which ties the gate directly to
D-007's economy.
*Aerial signature:* the best in the game. Cooling towers and stacks visible from 8 km; the tank
farm's circles; the rail yard's parallel lines; the river as the one safe low route.
*Landmark:* the cooling towers. *Anchor:* the refinery, which is the endgame fuel source.

**6 · Saltback Flats** — 35 km², 0.34 POI/km², Act 3, long-range radar gate.
Salt pan, playa, a dry lake bed, a decommissioned airfield with hangars and a long runway, a solar
farm, a wind farm with most of the blades gone, a prison. **Nothing to hide behind.** This is the
region that cannot be beaten by flying skill — the only answers are chaff, a jammer, or weather
and darkness. Every other region rewards terrain masking; this one deliberately removes it, so
that the countermeasure hardware has a place where it is the *only* answer.
*Aerial signature:* albedo and scale. Blinding white, a runway as a single enormous line, the
solar farm as a grid, turbine towers as the only vertical objects for kilometres.
*Landmark:* the airfield control tower and a single standing turbine. *Anchor:* the airfield —
the natural home for the hangar, the late-game free-form airframe work in D-011, and the best
place in the game to keep an aircraft.

**7 · Grayling Range** — 17 km², 0.46 POI/km², Act 4, emitter-locator gate.
The smallest region and the densest in consequence: an army training area and depot in the
north-east corner. Ranges, bunkers, a magazine, an AA school, hardened shelters, and the stores
that every faction's air-defence hardware came out of. It answers the question the whole map has
been asking — *why is a dead country still defended?*
*Aerial signature:* the pattern of military land. Range fans, target arrays, berms, arrow-straight
perimeter road, hardened arches.
*Landmark:* a radar tower on the high ground. *Anchor:* the depot.

**8 · Ashmount** — 34 km² of which a **~10 km² dense core**, 0.77 POI/km², Act 4, aerostat gate.
The city. High-rise core, a stadium, a hospital with a rooftop helipad, a flooded metro, a
multi-storey car park stack, a cathedral, a river through the middle with four bridges in four
states of collapse. Defended by a tethered aerostat with look-down radar, flak, and drone patrols
— the only place where a helicopter is genuinely unwelcome at *every* altitude.

This is the most expensive region in the project by a wide margin and it must be built with that
understood. **Recommended construction:** a procedural block-and-canyon generator with a modest
kit and a hand-authored skyline — roughly a dozen landmark silhouettes, three authored districts,
and interiors at only five to eight specific places. This is the section of §4.2 that pays for
itself: a ruined city read from 200 m is a *pattern*, and patterns are what procedural generation
is good at. Ten km² of dense core is already generous; for scale, downtown Los Angeles is about
14 km² and Manhattan is 59 km². Do not build 30 km² of city.
*Aerial signature:* canyons and standing towers, a street grid readable from 3 km, the river as
the one legal low-level route, and the aerostat itself — a visible, hateable object hanging over
the whole region from the moment the player first sees the skyline in Act 1.
*Landmark:* the tallest standing tower, and the aerostat. *Anchors:* the hospital and the
under-stadium settlement.

### What this buys

- **A 4-act spatial progression** in which every act opens a region *and* teaches a new relationship
  with altitude: Act 1 you fly where you like; Act 2 you learn to fly low; Act 3 you learn that low
  is not always available; Act 4 you learn to take the sky back.
- **A cost curve that rises with act**, so the scope cut is pre-planned.
- **Every D-010 key has exactly one region where it is the only answer**, which is what makes each
  one feel like a key rather than a stat bump.
- **Density that varies 3× across the map** (0.23 in the Pines to 0.77 in Ashmount) — sparse
  regions create the breathing room that makes dense ones read as dense.

### What to build first

1. **Threat-envelope data and the RWR** — §2.3 technique 1. It is the world-scale system, and
   every region's identity depends on it. It is currently seventh on the STATUS list; it should be
   second.
2. **Landing-site evaluation and the wire/obstruction system** — §2.3 technique 3, and nearly free
   given the existing flight model.
3. **Tidewater as a full vertical slice** — 18 POIs, both lighting paths, one anchor finished to
   ship quality, so the per-POI cost estimates in §3.4 can be replaced with measured ones before
   they are committed to seven more regions.
---

## 6. Summary of recommendations

| # | Recommendation | Confidence | Reversibility |
|---|---|---|---|
| 1 | Keep the world at **16.384 km square (268 km²)**. Do not grow it | High | High — one constant |
| 2 | Correct the design assumption from 60 kt to **~110 kt cruise**; re-derive every timing estimate that depends on it | Very high — measured | n/a |
| 3 | Target **124 named POIs (0.46/km²)**: 11 Anchors, 45 Sites, 68 Kit POIs | Medium-high | High early |
| 4 | Target **~3,000 unnamed procedural features (≈11/km²)** — this matters more for atmosphere than POI count | High | High |
| 5 | Plan **~65% of the map as deliberately empty**. Emptiness is cheap when traversal is fast; it is the one gift the premise gives us | High | High |
| 6 | Promote **threat envelopes (D-010) to the second thing built**, not the seventh. It is the world-scale system, not a progression system | High | High |
| 7 | Build **landing-site evaluation and a wire/obstruction system** early. It converts POI count into event count at near-zero cost | High | High |
| 8 | Re-frame **fuel (D-007) as economy and load, not range**. One tank is 34 map diagonals | Very high — measured | n/a |
| 9 | Hold the **interior : exterior ratio near 1 : 4**. Interiors are where the money goes and where the aerial view earns nothing | Medium-high | Medium |
| 10 | Build **Ashmount procedurally** with a hand-authored skyline and 5–8 interiors. Cap the dense core at ~10 km² | High | Medium |
| 11 | Order regions so **cost rises with act**, so a scope cut removes the city rather than the tutorial | High | High |
| 12 | Cost **D-003's dual lighting path per POI** and decide explicitly whether the POI target survives it | High | Low (D-003 is locked) |

### The three things that matter most

1. **Route inflation via threat envelopes.** Effective size = physical size × (flown path ÷
   straight-line path). This is the only lever that multiplies the whole map at near-zero authoring
   cost, and it is already designed. Build it early.
2. **Altitude-banded legibility.** Author every place to read differently at 500 m, 150 m and 15 m,
   and give each threat class its own altitude band. The same terrain then pays three times, and
   descending becomes an act of discovery rather than a transition.
3. **Landing as the expensive act.** Flying 4 km takes ninety seconds; arriving well takes real
   skill under real consequence. This is the cheapest possible content in the project — the physics
   already exists — and it is what turns a fast world into a slow game.

### The three biggest risks in the current plan

1. **Authoring throughput.** ~3,800 hours of world content alone. Everything else in this document
   is downstream of whether that is true, which is why the Tidewater vertical slice should exist
   before the second region is started — to replace the estimate with a measurement.
2. **The city.** Ashmount is the single most expensive object in the project and the one most
   likely to be built at the wrong scale. Cap it, generate it, and hand-author only the skyline and
   the places the player lands.
3. **Consumption speed.** Without D-010 working properly, the player sees the entire map in the
   first session and the game is over before the story starts. This is *Just Cause 3*'s failure and
   it is the failure this project is most structurally exposed to.
