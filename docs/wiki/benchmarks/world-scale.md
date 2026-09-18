# Benchmark — world scale and content density

*Research date: 2026-09-18. Compiled against `docs/wiki/00-vision.md`, `decisions.md` and the
current code in `game/` and `sim/`.*

---

## Bottom line

**The world is roughly 60% too big, the aircraft is twice as fast as the plan assumes, and almost
every open-world area figure the plan was reasoning from is wrong by a factor of three to six.**
Start with the aircraft: the brief assumes ~60 kt / 110 km/h, but the Huey in `sim/` cruises at
100–116 kt with power to spare (measured below), halving every crossing time — edge-to-edge is
**4 min 49 s**, not nine, which is the measured time to fly across *GTA V*. Then the comparison
set, re-measured: *Elden Ring* is **13.5 km²**, not 79; *Skyrim* is **14.82 km²** playable, not
38.3; *Fallout 4* is **10.16 km²**; *Death Stranding* is **21 km²**; *GTA V*'s land is 48 km².
**No acclaimed open world in the set exceeds ~50 km², and most are under 25.** Our 268 km² grid is
**20× Elden Ring** — for one developer. The decisive number is not POIs per km² but **content-hours
per km²**, where *Elden Ring* scores 4.45, *GTA V* 0.67 and *Just Cause 3* — the genre's canonical
failure — 0.067; the plan as written lands at **0.19**, and its POI density of 0.46/km² is
near-identical to *Just Cause 3*'s 0.42. The recommendation is therefore to **keep the 16.384 km
terrain grid** (terrain is a pure function and costs nothing, and the horizon is worth having) but
**shrink the content envelope to a 13.0 km square — 169 km² — ringed by water, terrain above the
measured 3,000 m hover ceiling, and a contamination band**, holding **124 named POIs (0.73/km²)**:
11 deep hand-authored anchors, 45 hand-built sites, 68 kit-assembled POIs, over ~2,500 unnamed
procedural features that make the world read as inhabited from 300 m. That is ~0.31 content-hours
per km², which is speed-normalised parity with *GTA V*. Note that *Skyrim* fills only **38.7%** of
its own worldspace, so a 63% envelope is generous, not timid. The helicopter does **not** create a
density problem — moving fast makes you sweep more ground per second, so you encounter *more*, not
less; it creates a **duration** problem, which is solved by gating and by depth, not by more
markers. The three things that buy felt size are route inflation via threat envelopes (D-010 is the
right spine, it is load-bearing, and it should be built second rather than seventh), altitude-banded
legibility, and making *landing* rather than flying the expensive act. Two honesty notes: fuel
(D-007) cannot be a range constraint — one tank covers the map diagonal ~34 times — so it must be
re-framed as an economy and load system; and the full eight-region plan is **~3,800 hours of world
content alone**, which is four to six years of solo evenings before any other system is built.

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

## 1. The comparison set

### 1.1 Read this first — nearly every published figure is wrong, and wrong in one direction

Open-world area figures are among the least reliable numbers in games writing, and this research
found the situation far worse than expected. **Where a game has both a marketing/folk figure and a
rigorous measurement, the real playable area is consistently 17–30% of the quoted one.**

| Game | Commonly quoted | Actually measured | Real fraction |
|---|---|---|---|
| Elden Ring | 79 km² | **13.5 km²** | **17%** |
| S.T.A.L.K.E.R.: SoC | 30 km² (dev claim) | **~9.2 km²** | **~30%** |
| Just Cause 3 | 1,000 km² (dev claim) | ~274 km² land (est.) | ~26% |
| Death Stranding | 595 km² | **~21 km²** | **3.5%** |
| Skyrim | 38.3 km² | **14.82 km²** playable | **38.7%** |
| Fallout 3 | "~14 km²" | **8.56 km²** | 61% — and the 14 is a myth |

Four compounding failure modes produce this:

1. **Terrain allocated ≠ terrain reachable.** *Skyrim*'s worldspace is a 119 × 94 cell rectangle —
   38.3 km², the figure everyone quotes — but only **4,326 of 11,186 cells are reachable (38.7%)**.
2. **Water and unwalkable cliff counted as world.** *Elden Ring*'s famous 79 km² measures the whole
   map bitmap, most of which is sea.
3. **Marketing claims are not measurements**, and several are design targets from years before
   release. GSC's "18 levels, more than 30 km²" for *STALKER* traces to a 2004 preview; no shipped
   level is even 2 km across.
4. **"Number of locations" is undefined.** Marked icons, named cells, quest markers and wiki article
   counts differ by up to **6×** for the same game.

Three specific findings worth recording permanently, because they will keep resurfacing:

> **Elden Ring's "79 km²" is the weakest famous number in games journalism.** Its author described
> the method himself: *"my edible hit harder than expected so I calculated the size of the entire
> map, used the size of my horse and a bridge to estimate lengths."* Two independent rigorous
> analyses — one by YouTuber Addypalooza using map-editor scale calibration and traced navigable
> boundaries, one Japanese analysis starting from 88 km² and cutting away unreachable ocean and
> terrain — **converge on ~13.5 km² of walkable surface** (~15 km² including interiors, ~20.5 km²
> with *Shadow of the Erdtree*). That convergence of two unrelated methods is the single strongest
> result in this benchmark.
> ([PC Gamer via inkl](https://www.inkl.com/news/elden-ring-geographer-tests-rigorous-calculation-against-weed-fueled-horse-math-to-determine-the-exact-size-of-the-lands-between) ·
> [NeverAwakeMan analysis](https://note.com/neverawakeman/n/n5725ff38d5bc?hl=en) ·
> [the 79 km² claim](https://screenrant.com/elden-ring-open-world-map-how-big/))

> **The widely-cited "~14 km²" for Fallout 3 is a myth.** No source asserting it could be found.
> Its likely origin is transposition — **14.82 km² is Skyrim's** playable figure, from the same
> modder post that measures Fallout 3. Fallout 3's actual playable area is **8.56 km²**.

> **Discard the entire listicle family of map-size articles.** TheGamer's *"Every Fallout Game,
> Ranked By Total Map Size"* prints "8,462 sq mi" for Fallout 3 — the same digits that appear
> elsewhere as **8.462 km²**. It took km² values, relabelled them square miles, and dropped the
> decimal: a ~2.6-million-fold error. Its numbers are in any case real-world geographic footprints
> of the depicted region, not game area.
> ([TheGamer](https://www.thegamer.com/every-fallout-map-size/) ·
> [the km² original](https://steamcommunity.com/app/22380/discussions/0/2561864094348475078))

Confidence flags per §0: **[M]** measured here · **[V]** verified against a fetched source ·
**[D]** disputed · **[U]** unverified.

### 1.2 The master table

Hours are HowLongToBeat values **pulled 2026-09-18** — HLTB figures drift materially, so they are
date-stamped. Where a game has no credible measured area, that is stated rather than guessed.

| Game | **Playable km²** | POIs / named locations | **POIs per km²** | Main story h | **Content h per km²** | Dominant traversal |
|---|---|---|---|---|---|---|
| **S.T.A.L.K.E.R.: SoC** | **~9.2** [V] *(bounding boxes; true walkable lower)* | 18 separately-loaded levels; 217–321 stashes; 27 artifacts | — | **15.1** | **1.64** | Walk ~2 m/s. **Not a seamless world** |
| **Fallout 3** | **8.56** [V] | **163** marked (224 w/ DLC) | **19.0** | no HLTB figure found | — | Walk + map fast travel |
| **Fallout: New Vegas** | **8.2–10.0** [D] *(dev bounding box, not a measurement)* | **190** marked (343 w/ DLC; 730 "named", 91 unvisitable) | **~21** | ~30 [U] | ~3.3 | Walk + fast travel |
| **Fallout 4** | **10.16** [V] *(2,965 navmesh cells — best-evidenced in the set)* | **~248** marked | **24.4** | 33 | 3.25 | Walk, fast travel, **Vertibird** |
| **Elden Ring** | **13.5** [V] surface · ~15 w/ interiors · ~20.5 w/ DLC | **~308** Sites of Grace (414 w/ DLC); **135** dungeon entrances | **22.8** grace / **10.0** dungeons | **60.1** | **4.45** — highest in the set | Torrent, fast; grace fast-travel |
| **Skyrim** | **14.82** [V] playable · 38.3 rectangle [D] | **341** map markers; 197 clearable dungeons | **23.0** | 34 | 2.29 | Walk/horse; **49 m 12 s to run across** [V] |
| **Death Stranding** | **~21** [V] (E 2.5 + C 18 + W 0.5) | **38** connectable network nodes; 6 Knot Cities | **1.81** | **40.5** | **1.93** | **Walking, deliberately slow ~2 m/s** |
| **Horizon Zero Dawn** | **no credible figure found** — Guerrilla deliberately withholds it | 22 main + ~22 side + ~14 errands; 5 Tallnecks, 4 Cauldrons, 6 camps, 11 corrupted zones, 60 collectibles | — | 22.5 | — | Run/mount |
| **Mad Max (2015)** | **no credible figure found** | 4 strongholds; 30 minefields; 6 Top Dog camps; rest unpublished | — | 20.1 | — | Car; **13 m 05 s to drive across** [V] |
| **GTA V** | **48.15 land** [D — method unpublished] · 75.84 total incl. water | 40 LS districts + 3 Blaine towns; 245 collectible locations | **~5.9** | 32.1 | **0.67** | Car ~30 m/s; **4 m 48 s to fly across** [V] |
| **The Witcher 3** | **136 disputed** [D] — a *pre-release fan estimate*, not a measurement | **1,074** markers; 228 signposts; **521 "?" POIs** | ~7.9 *if* 136 holds | 50–52 | 0.37 | Walk/horse/boat |
| **RDR2** | **no credible figure found** — the ubiquitous 75 km² traces to no methodology | 50 journal POIs; 67 landmarks + 64 shacks (MapGenie) | — | 50 | — | Horse; **~16 min corner to corner** [V] |
| **Just Cause 3** | **~274 land [U]** of 1,048 total — a single unsourced wiki edit | **~114** liberatable settlements (79 bases + 35 towns); 227 collectibles | **0.42** | 18.3 | **0.067** | Wingsuit/grapple, unlimited |
| **MSFS 2020** | Earth | ~37,000 airports; **~30 hand-crafted at launch** | 0.00007 | unbounded | — | Aircraft 150–900 km/h |
| **ROTORWASH — current plan** | **268.4 grid** [M] | 124 recommended | 0.46 | ~50 target | **0.19** | **Helicopter ~57 m/s** |
| **ROTORWASH — recommended** | **~169 content envelope** [M] (§3.1) | **124** | **0.73** | ~50 target | **0.31** | 3 m 49 s across the envelope |

Sources for the measured figures: Bethesda games via cell math — **1 cell = 4,096 units = 58.52 m
= 3,425 m²** ([GECK wiki Units](https://geckwiki.com/index.php/Units) ·
[fallout.wiki Creation Kit/Cell](https://fallout.wiki/wiki/Resource:Creation_Kit/Cell) ·
[archived cell-count analysis](https://web.archive.org/web/20180614074653id_/http://www.gamesas.com/fallout-map-size-anolysis-t393192.html) ·
[FO4Edit navmesh filter](https://steamcommunity.com/app/377160/discussions/0/350543319567332507/)).
Elden Ring per the two analyses above. STALKER derived from `bound_rect` values in the shipped
`game_maps_single.ltx`, byte-identical across two independent repositories
([OpenXRay](https://github.com/OpenXRay/xray/blob/d7b23596a70374d8a7ffda0e98852f93ce985182/trunk/resources/config/game_maps_single.ltx) ·
[ixray](https://github.com/ixray-team/ixray-1.0-stsoc/blob/a11547a2e4e6426b77ebe4ee19550ab1cab7fdef/src/resources/config/game_maps_single.ltx)).
Death Stranding per [Beyond Satire](https://www.beyondsatire.com/investigations/death-stranding-map-size/),
which is the only figure in the set with a fully published methodology (Odradek distance readout
as a calibrated baseline, then traced area). GTA V via
[KeWiS's land/water split](https://ipsnews.net/business/2020/07/25/gta-ranking-the-maps-in-order-of-size/).
Location counts: [FO3](https://fallout.fandom.com/wiki/Fallout_3_locations) ·
[FNV](https://fallout.fandom.com/wiki/Fallout:_New_Vegas_locations) ·
[Skyrim map markers](https://elderscrolls.fandom.com/wiki/Map_(Skyrim)/Locations) ·
[Elden Ring grace sites](https://game-checklists.com/elden-ring/all-sites-of-grace/) ·
[Elden Ring dungeons](https://lootmap.gg/elden-ring/guides/dungeons-caves-and-catacombs/) ·
[JC3 military bases](https://justcause.fandom.com/wiki/Military_bases_in_Medici) ·
[JC3 towns](https://justcause.fandom.com/wiki/Towns_in_Medici) ·
[Death Stranding nodes](https://mapgenie.io/death-stranding/maps/world) ·
[Witcher 3 markers](https://mapgenie.io/witcher-3) ·
[MSFS hand-crafted airports](https://flight.wiki.gg/wiki/Microsoft_Flight_Simulator_(2020)/List_of_hand-crafted_airports).
HLTB: [Elden Ring](https://howlongtobeat.com/game/68151) ·
[GTA V](https://howlongtobeat.com/game/4064) · [SoC](https://howlongtobeat.com/game/8038) ·
[JC3](https://howlongtobeat.com/game/26404) · [Death Stranding](https://howlongtobeat.com/game/38061) ·
[Mad Max](https://howlongtobeat.com/game/17610) · [HZD](https://howlongtobeat.com/game/26784).

**Caveats that matter.** GTA V's 48.15 km² land figure is stable across three independent secondary
sources but its methodology was never published and the primary forum thread is inaccessible —
treat it as the best available, not as measured. *Just Cause 3*'s 274 km² land figure is a **single
unsourced wiki edit** whose only credibility is that its total lands within 1% of the developer
claim; do not present it as measured. *Witcher 3*'s 136 km² is explicitly a pre-release NeoGAF
estimate ([GamingBolt, Apr 2015](https://gamingbolt.com/witcher-3-map-size-compared-to-gta5-skyrim-far-cry-4-new-screens-show-different-visual-settings)),
covering Novigrad + Velen + Skellige with **no land-only Skellige figure obtainable**, so the
"Velen+Novigrad only vs all regions" dispute remains **unresolved**. The Bethesda unit conversion
itself is contested by ±21% (the GECK documents 9/16 inch per unit; an in-game yardstick prop
implies 1/2 inch) — the GECK value is used here. *Fallout 4* is **not** bigger than *Skyrim*,
contrary to the common claim: it is ~70% of Skyrim's playable area.

### 1.3 What the data actually shows

**Finding 1 — no acclaimed open world in this set exceeds ~50 km² of measured playable space, and
most are under 25 km².**

> Fallout 3 **8.56** · STALKER **~9.2** · Fallout 4 **10.16** · Elden Ring **13.5** ·
> Skyrim **14.82** · Death Stranding **~21** · GTA V land **48.15**

**Rotorwash's 268 km² grid is 20× Elden Ring, 18× Skyrim, 26× Fallout 4 and 5.6× GTA V's land
area — proposed by one developer.** This is the most important correction in this document after
the cruise speed, and it is considerably more alarming than the figures the brief was working from.

**Finding 2 — POI density in walking games clusters tightly at ~19–24 per km².**

| Game | POIs/km² |
|---|---|
| Fallout 3 | 19.0 |
| Fallout: New Vegas | ~21 |
| Elden Ring (grace sites) | 22.8 |
| Skyrim | 23.0 |
| Fallout 4 | 24.4 |

Five games, three studios, fourteen years, and a spread of 28%. That is a genuine design constant
for foot-speed worlds. At *Skyrim*'s density our 268 km² grid would need **6,173 POIs**.

**Finding 3 — the §2.1 sweep model reproduces that constant from first principles, which is the
strongest evidence in this document that the recommendation in §3 is the right order of magnitude.**

Take the two independently measured *Skyrim* figures — **23.0 POIs/km²** and **49 m 12 s to run
corner to corner** across a worldspace rectangle of 6.95 × 5.50 km (diagonal 8.86 km):

- Average speed on that run: 8,860 m ÷ 2,952 s = **3.0 m/s**
- Identification corridor on foot, given Skyrim's draw distance and fog: call it **300 m**
- Swept area: 3.0 × 300 = 900 m²/s
- Encounter rate: 0.0009 km²/s × 23.0 POIs/km² = **one point of interest every 48 seconds**

Forty-eight seconds — against CD Projekt Red's independently stated **"rule of 40 seconds"** for
*The Witcher 3* ([Jaber, Uppsala University](https://uu.diva-portal.org/smash/get/diva2:1569059/FULLTEXT01.pdf)).
Two studios, different genres, different decades, no shared methodology, same answer. **The 40–50
second encounter interval is a real constant of the genre, and the sweep model recovers it.**

Run the model forwards for a helicopter — 57 m/s, a 1.0–1.5 km identification corridor from the
air — and a 48-second interval needs **0.29–0.44 POIs/km²**. Against Skyrim's 23.0. The ~50× gap is
*exactly* what the ~19× speed ratio and ~4× corridor ratio predict. **A helicopter world at
0.4–0.7 POIs/km² will feel as busy in transit as Skyrim does on foot.**

**Finding 4 — but POI spacing is the wrong thing to worry about. Content-hours per km² is where
worlds actually fail.**

| Game | Content h/km² | Reception |
|---|---|---|
| Elden Ring | **4.45** | Generational |
| Fallout 4 / New Vegas | ~3.3 | Strong |
| Skyrim | 2.29 | Generational |
| Death Stranding | 1.93 | Divisive but respected |
| S.T.A.L.K.E.R.: SoC | 1.64 | Cult classic |
| GTA V | 0.67 | Generational |
| The Witcher 3 (if 136 km²) | 0.37 | Generational |
| **Just Cause 3** | **0.067** | **"Feels like a chore to be completed"** |

*Elden Ring* packs roughly **66× more main-story time per km²** than *Just Cause 3*.

And here is the uncomfortable part, stated plainly rather than buried: **the current plan —
268 km², ~50 hours — lands at 0.19 h/km², which is nearer Just Cause 3 than GTA V.** Worse, the
plan's POI density of 0.46/km² is almost identical to *Just Cause 3*'s **0.42 settlements/km².
That is not a coincidence to wave away; it is the single strongest objection to the plan as
written, and §3.1 changes the recommendation because of it.

Two things separate us from that fate if we act on them. First, *Just Cause 3*'s failure was
**repetition, not spacing** — *"Liberating your 50th location from enemy control feels much the
same as liberating your first"*
([GamesRadar, 3/5](https://www.gamesradar.com/just-cause-3-review/)). Second, and more usefully,
content-hours per km² is only comparable between games at comparable traversal speed. Normalising
*GTA V*'s 0.67 h/km² by our 1.9× speed advantage gives a like-for-like target of **~0.35 h/km²** —
which is reachable, but only on a smaller content envelope. That single calculation is what drives
the revised recommendation in §3.1.

**Finding 5 — two studios independently concluded the world should be smaller, and said so.**

- **Guerrilla scaled Horizon Zero Dawn's world down during development** *"after they realised they
  could not fill the entire map with content"*
  ([Wikipedia](https://en.wikipedia.org/wiki/Horizon_Zero_Dawn)). Their art director refuses to
  publish a size at all: *"We never say that, because it will destroy the illusion. If people knew
  how big it really was, they'd be disappointed"* — and the world is a roughly **10× vertical
  miniature**: *"If a mountain is 3000 m high we make it 300 m. So it works, and you can go through
  it rapidly, but it feels big"*
  ([GamingBolt](https://gamingbolt.com/horizon-zero-dawn-heres-why-guerrilla-didnt-revealed-the-actual-map-size-it-will-destroy-the-illusion)).
- **Avalanche, on Mad Max:** *"the game world is scaled according to gameplay density and
  frequency; the development team emphasized creating a world with choices and distractions,
  rather than focusing on size"* ([Wikipedia](https://en.wikipedia.org/wiki/Mad_Max_(2015_video_game))).
  The same studio's *Just Cause 3* director had already said the map was staying the same size
  because *"this time keep it more on density"*
  ([archived Inquisitr](https://web.archive.org/web/20211013010927/https://www.inquisitr.com/1847980/just-cause-3-director-discusses-the-map-size-of-the-games-new-setting/))
  — and shipped the least dense world in the comparison set anyway. Intent is not enough.

**Finding 6 — Death Stranding proves traversal time, not area, is what players perceive as size.**
Its measured ~21 km² is smaller than most AAA open worlds, yet walking across its Central region
takes **1 h 37 m** — 7.5× *Mad Max*'s 13-minute drive across a map several times larger
([DS walk](https://howbigisthemap.com/death-stranding-walk-across-the-map-map-2/) ·
[Mad Max drive](https://howbigisthemap.com/mad-max-drive-across-map/)). **Its reputation as a huge
world is entirely an artifact of friction.** For a design benchmark, traversal time is a more
honest axis than km² — and for a game whose traversal is a 200 km/h aircraft, it is the *only*
honest axis.

**Finding 7 — nobody has solved this at our point on the curve.**
POI density tracks traversal speed inversely and almost perfectly: walking 19–24/km²; riding and
driving 0.4–8/km²; wingsuit and aircraft 0.4/km² and below. There is no shipped example of a
*roleplaying* game whose *default* traversal is a 200 km/h aircraft. *Fallout 4*'s Vertibird is the
closest and it is a taxi, not the world's organising principle. Genuinely novel design space — the
exciting part, and also no template and no safety net.

**Finding 8 — the best-regarded worlds are not the biggest.** *Elden Ring* (13.5 km²) and *Skyrim*
(14.82 km²) are the two most-played open worlds of their respective generations. *Just Cause 3*
(~274 km²) is the least well regarded in the set. Size never was the variable.

---

## 2. The central problem, analysed properly

### 2.1 It is not the problem the brief thinks it is

The brief frames fast traversal as a *density* problem: the player crosses the map quickly,
therefore the world feels empty, therefore we need more stuff per km². That is backwards.

Model the encounter rate directly. A player at speed `v` with an identification corridor of width
`w` (how far to either side you can recognise a place as *worth going to*, not merely see it)
sweeps `v·w` of ground per second. Time between POIs is `1 / (v·w·ρ)` for POI density `ρ`.

Counts in brackets are for the **169 km² content envelope** recommended in §3.1.

| Speed | Corridor | One POI every 40 s needs | …every 60 s | …every 120 s |
|---|---|---|---|---|
| 58 kt | 1.0 km | 0.84/km² (142 POIs) | 0.56/km² (94) | 0.28/km² (47) |
| 58 kt | 1.5 km | 0.56/km² (94) | 0.37/km² (63) | 0.19/km² (31) |
| 110 kt | 1.0 km | **0.44/km² (75)** | 0.30/km² (50) | 0.15/km² (25) |
| 110 kt | 1.5 km | **0.30/km² (50)** | 0.20/km² (33) | 0.10/km² (17) |

The recommendation of **124 POIs (0.73/km²)** sits comfortably above every cell in this table,
which is deliberate: POIs will not be uniformly distributed, several regions are intentionally
sparse, and encounter-rate is the *floor* on density, not the target.

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

### 3.1 Is 16 × 16 km right? — **keep the terrain, shrink the content envelope to ~13 km**

This is the recommendation the §1 data changed. The first draft of this section said "keep
16.384 km, plan for 65% of it to be empty". The measured figures do not support that, and the
reason is Finding 4: **at 268 km² and ~50 hours of content the plan sits at 0.19 content-hours per
km², which is nearer *Just Cause 3* (0.067) than *GTA V* (0.67)** — and its POI density of
0.46/km² is almost identical to *Just Cause 3*'s 0.42 settlements/km². Proposing the same numbers
as the genre's canonical failure case and expecting a different outcome requires an argument, and
"emptiness is cheap when traversal is fast" is only half of one.

**The recommendation, therefore:**

> **Keep the 16.384 km terrain grid. Reduce the *content envelope* to a ~13.0 km square
> (≈169 km², 63% of the grid) by ringing it with ~1.7 km of water, high ground and exclusion.
> Put all 124 POIs inside that envelope.**

Why this is the right shape rather than either extreme:

**Why not simply shrink `WorldHalfExtent`.** Terrain is genuinely free — it is a pure function
(`WorldHeight.cs`) and the streamer already handles it. Keeping the full grid costs nothing and
buys a real horizon: from 500 m over the envelope's edge you see terrain running out to 8 km, which
is most of what "this is a country, not a level" is made of. Cutting the grid throws that away for
no saving.

**Why the envelope must nonetheless be smaller than the grid.** Content-hours per km² is the metric
on which worlds actually fail (Finding 4), and it is only comparable across games at comparable
traversal speed. Normalising *GTA V*'s 0.67 h/km² by our 1.9× speed advantage gives a like-for-like
target of **~0.35 h/km²**. Fifty hours of content at 0.35 h/km² buys **143 km²**. Fifty-three hours
buys 151 km². A 13.0 km envelope at 169 km² and ~0.31 h/km² is a *slightly* generous rounding of
that, and it is the largest number this data supports.

**Why ~13 km specifically, and not 10 or 16.** Three constraints bracket it:

| Constraint | Implies |
|---|---|
| Content-hours per km² ≥ ~0.3 (Finding 4, speed-normalised against GTA V) | **≤ ~170 km² → ≤ 13.0 km** |
| Edge-to-edge transit long enough to read as a country, not a level (≥ ~3.5 min at cruise) | **≥ ~12 km** |
| POI density in the 0.4–0.7/km² band the sweep model predicts (Finding 3) | 124 POIs → **12–17 km** |
| Authoring cost (§3.4) | smaller is always better |

13.0 km satisfies all four; 16.4 km fails the first.

**Bounding the envelope diegetically, using physics we already have.** This is where the measured
hover ceiling earns its place. The border ring should be built from three things, none of which is
an invisible wall:

1. **Water** on the south and west — a coast and an estuary. Absolute, obvious, and free.
2. **Terrain above 3,000 m** on the north — the aircraft's *measured* OGE hover ceiling. A ridge
   the Huey physically cannot climb over is a border made of aerodynamics rather than fiat, and it
   is exactly the kind of constraint this project should prefer.
3. **A permanent threat or contamination band** on the east, which the fiction already supports.

This is not a compromise; it is what *Skyrim* does. Bethesda allocated 38.3 km² of worldspace and
made 14.82 km² reachable — **38.7%**. Our 63% is considerably more generous than the most
successful open world of its generation.

**What is still true from the first draft:** emptiness *is* cheaper when traversal is fast, and the
helicopter is the only traversal mode that makes large empty country an asset rather than a tax on
patience. That argument survives — it is why ~169 km² with 124 POIs is defensible where the same
numbers on foot would be absurd. It just does not stretch to 268 km².

**Fallback rule if authoring slips.** Do not thin the density to cover the same area. If the POI
count looks like landing below ~80, pull the envelope in further — to 10.5–11 km — and keep the
density. A smaller dense world beats a large thin one every time, and every game in §1 agrees.

### 3.2 Minimum POI density that avoids "wide as an ocean"

Two separate densities matter, and conflating them is the usual mistake. Densities below are
against the **169 km² content envelope**, not the 268 km² grid.

| Layer | What it is | Target density | Count in the envelope | Why |
|---|---|---|---|---|
| **Named POIs** | Map-markable places worth flying to and landing at | **0.65–0.80/km²** | **110–135** | Comfortably above the 0.29–0.44/km² the sweep model requires for a 48-second encounter interval (Finding 3), with headroom for deliberately sparse regions |
| **Absolute floor** | — | 0.45/km² | 76 | Below this the transit band goes quiet and the map reads as terrain with things on it |
| **Content-hours** | Playable time per km² of envelope | **≥ 0.30 h/km²** | ≥ 51 h | The metric worlds actually fail on (Finding 4). Speed-normalised parity with *GTA V* |
| **Unnamed human features** | Wrecks, roadblocks, pylon runs, silos, blown bridges, sheds, fence lines, burnt copses | **10–18/km²** | **1,700–3,000** | What makes the world read as *inhabited* from 300 m. Free (scatter rules), and it matters more for atmosphere than POI count does |

**Recommendation: 124 named POIs (0.73/km²) and ~2,500 unnamed procedural features (≈15/km²),
inside a 169 km² envelope, delivering ≥50 hours (≥0.30 h/km²).**

Note the asymmetry the brief did not anticipate: we need roughly **one thirtieth** of *Skyrim*'s
POI density, and something like **ten times** its density of unnamed environmental texture. That is
the correct shape for a world viewed from 300 m at 200 km/h — few things worth landing at, and a
great many things worth seeing on the way.

### 3.3 Handcrafted vs procedural

| Tier | Count | Share | What it is | Where the content lives |
|---|---|---|---|---|
| **Anchor** | 11 | 9% | A settlement or major site: exterior layout, 3–8 interiors, 4–10 NPCs, dialogue, a quest chain, a difficult landing site | Hours of play each |
| **Site** | 45 | 36% | Fuel depot, SAM site, crashed airliner, drowned mall, fire lookout, rail bridge, dam gatehouse. Exterior plus at most one small interior, a hazard, loot, maybe one NPC | 15–40 min each |
| **Kit POI** | 68 | 55% | Assembled from a reusable kit, hand-placed, hand-named, one line of written flavour | 3–10 min each |
| **Scatter** | ~3,000 | — | Unnamed, rule-placed, never marked on the map | 0 — it is scenery and navigation |

**Does that add up to a game?** The content-hours target in §3.2 is the thing worth checking, so
here it is explicitly:

| Tier | Count | Time per place | Total |
|---|---|---|---|
| Anchor | 11 | ~2.5 h | 27.5 h |
| Site | 45 | ~25 min | 18.8 h |
| Kit POI | 68 | ~6 min | 6.8 h |
| **Place-content subtotal** | **124** | | **~53 h** |

53 hours over a 169 km² envelope is **0.31 content-hours per km²** — speed-normalised parity with
*GTA V* (§1, Finding 4) — before counting flight time, the critical path, the refit and economy
loops, or repeat visits. It is a 50–60 hour game, which is the right size, and it is achieved with
one thirtieth of *Skyrim*'s POI density.

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
| **v0.5 vertical slice** | Tidewater only | 18 | 20 km² | ~800 h | ~1.0 year |
| **v1.0 — recommended ship target** | Acts 1–2 (4 regions) | 62 | 89 km² | ~2,150 h | ~2.8 years |
| **v1.5** | + Act 3 (6 regions) | 90 | 136 km² | ~2,900 h | ~3.7 years |
| **Full plan** | All 8 regions | 124 | 169 km² | ~3,800 h | ~4.9 years |

v1.0 at 62 POIs over 89 km² is 0.70/km² — *the full plan's density*, because the
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

Eight regions inside the **13.0 km content envelope** recommended in §3.1 — i.e. **X and Z within
±6,500 m** of the map centre, with the remaining ~1.7 km ring out to ±8,192 m given over to water,
terrain above the 3,000 m hover ceiling, and a contamination band. Coordinates are Godot world axes
as used in `WorldHeight.cs`: **X east, Z south, origin at map centre.** Region boundaries are soft —
biome blends and threat-envelope edges, never walls.

The ordering is chosen so that **cost rises monotonically with act**. Act 1 regions are roads,
sheds and water; Act 4 is a city and a military complex. If the project runs short of time, the
cut lands on the expensive end and the game still has an ending.

| # | Region | Bounds (X, Z metres) | Area | POI/km² | Act | POIs (A/S/K) | Gate — the key that opens it |
|---|---|---|---|---|---|---|---|
| 1 | **Tidewater** | X −6500…−2400, Z +1600…+6500 | 20.1 km² | 0.90 | 1 | **18** (2/6/10) | None. Start here |
| 2 | **The Interchange** | X −2400…+1800, Z +1600…+6500 | 20.6 km² | **0.97** | 1 | **20** (2/7/11) | None. Small arms and technicals only |
| 3 | **The Weal** | X −2400…+1800, Z −3600…+1600 | 21.8 km² | 0.64 | 2 | **14** (1/5/8) | **MANPADS.** Flares + IR suppressor, *or* fly the hedgerows with terrain-masking charts |
| 4 | **The Pines** | X −6500…+2600, Z −6500…−3600 | 26.4 km² | **0.38** | 2 | **10** (1/4/5) | **Weather and a mobile IR team.** RWR to find it; charts to route round it |
| 5 | **Cold River** | X +1800…+6500, Z −3600…+1600 | 24.4 km² | 0.66 | 3 | **16** (1/6/9) | **Fixed radar SAM ring.** RWR + chaff |
| 6 | **Saltback Flats** | X +1800…+6500, Z +1600…+6500 | 23.0 km² | 0.52 | 3 | **12** (1/4/7) | **Long-range radar over ground with no masking.** Chaff + jammer, or night and low visibility |
| 7 | **Grayling Range** | X +2600…+6500, Z −6500…−3600 | 11.3 km² | 0.71 | 4 | **8** (1/4/3) | **Emitter locator.** The source of every countermeasure; the reason everything else is defended |
| 8 | **Ashmount** | X −6500…−2400, Z −3600…+1600 | 21.3 km² | **1.22** | 4 | **26** (2/9/15) | **Tethered aerostat + flak + drone patrol.** Jammer, then kill the aerostat. Endgame |
| | **Content envelope** | ±6,500 m | **168.9 km²** | **0.73** | | **124** (11/45/68) | |
| | **Border ring** | to ±8,192 m | 99.5 km² | 0 | — | 0 | Water · terrain above the 3,000 m hover ceiling · contamination band |
| | **Full terrain grid** | ±8,192 m | **268.4 km²** | | | | |

*(A = Anchor, S = Site, K = Kit POI — tiers defined in §3.3.)*

**Transit times across the envelope** at ~110 kt cruise: **3 m 49 s** edge to edge, **5 m 24 s**
corner to corner, and ~60–90 s for a typical mission leg. With the route inflation of §2.3 those
become roughly 8–12 minutes for a contested crossing, which is the number that actually matters.
Density varies **3.2×** across the map (0.38 in the Pines to 1.22 in Ashmount) — the sparse regions
are what make the dense ones read as dense.

### Region detail

**1 · Tidewater** — 20.1 km², 0.90 POI/km², Act 1, no gate.
A silted river mouth and the drowned suburbs behind it. Tidal flats, half-submerged cul-de-sacs,
a marina with boats on their sides, a causeway that floods twice a day, sunken car roofs breaking
the surface. The starting settlement is a marina and boatyard that has become a town, because the
only working infrastructure left is a slipway and a fuel bowser.
*Aerial signature:* water. Albedo does the work — the whole region reads as a pale delta from
6 km, with the causeway as its one strong line. *Landmark:* a bascule bridge stuck half-raised.
*Landing pressure:* soft ground, tide state, and a limited number of surfaces that will take the
skids. This is the tutorial for "arrival is the hard part" and it costs almost nothing to build.
*Anchors:* the marina town; the lock-keeper's compound.

**2 · The Interchange** — 20.6 km², 0.97 POI/km², Act 1, essentially no gate.
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

**3 · The Weal** — 21.8 km², 0.64 POI/km², Act 2, MANPADS gate.
Farm belt and low hill country. Hedgerows, silos, a grain terminal, an overgrown airstrip, a
village, wind-thrown orchards, a reservoir. The gate is IR-guided man-portable missiles held by
people who live there — which means the region is passable at *very* low level along hedge lines
and stream cuts, and lethal at 300 m. This is the region that teaches terrain masking, and it is
the first place the player learns that altitude is a decision rather than a comfort.
*Aerial signature:* field pattern. Regular, coloured, with hedge lines as a navigation grid.
*Landmark:* a grain elevator and a lone church tower. *Anchor:* the village.

**4 · The Pines** — 26.4 km², 0.38 POI/km², Act 2, weather + mobile IR gate.
Forest, ridges and a hydroelectric dam. **Deliberately the sparsest region on the map**, and the
one that proves empty country can be good: ridge lines you follow, valleys that are dead ground,
cloud that sits on the tops, a fire lookout on every third summit that doubles as a chart source.
The threat is one mobile IR team that relocates, so the region is never *cleared*, only read.
*Aerial signature:* relief and canopy. Almost no geometry; all silhouette and shadow. The cheapest
region to build and the most valuable per hour spent.
*Landmark:* the dam wall and its spillway. *Anchor:* the dam and its inspection galleries — the
first vertical-layered interior, and one a helicopter cannot skip.

**5 · Cold River** — 24.4 km², 0.66 POI/km², Act 3, radar SAM gate.
A river valley full of the machinery that used to run the country: a refinery with a tank farm, a
rail yard, a coal-fired power station with two cooling towers, a lift bridge, a barge terminal.
Defended by fixed radar SAMs, because this is where the fuel is — which ties the gate directly to
D-007's economy.
*Aerial signature:* the best in the game. Cooling towers and stacks visible from 8 km; the tank
farm's circles; the rail yard's parallel lines; the river as the one safe low route.
*Landmark:* the cooling towers. *Anchor:* the refinery, which is the endgame fuel source.

**6 · Saltback Flats** — 23.0 km², 0.52 POI/km², Act 3, long-range radar gate.
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

**7 · Grayling Range** — 11.3 km², 0.71 POI/km², Act 4, emitter-locator gate.
The smallest region and the densest in consequence: an army training area and depot in the
north-east corner. Ranges, bunkers, a magazine, an AA school, hardened shelters, and the stores
that every faction's air-defence hardware came out of. It answers the question the whole map has
been asking — *why is a dead country still defended?*
*Aerial signature:* the pattern of military land. Range fans, target arrays, berms, arrow-straight
perimeter road, hardened arches.
*Landmark:* a radar tower on the high ground. *Anchor:* the depot.

**8 · Ashmount** — 21.3 km² of which a **~8 km² dense core**, 1.22 POI/km², Act 4, aerostat gate.
The city. High-rise core, a stadium, a hospital with a rooftop helipad, a flooded metro, a
multi-storey car park stack, a cathedral, a river through the middle with four bridges in four
states of collapse. Defended by a tethered aerostat with look-down radar, flak, and drone patrols
— the only place where a helicopter is genuinely unwelcome at *every* altitude.

This is the most expensive region in the project by a wide margin and it must be built with that
understood. **Recommended construction:** a procedural block-and-canyon generator with a modest
kit and a hand-authored skyline — roughly a dozen landmark silhouettes, three authored districts,
and interiors at only five to eight specific places. This is the section of §4.2 that pays for
itself: a ruined city read from 200 m is a *pattern*, and patterns are what procedural generation
is good at. Eight km² of dense core is already generous; for scale, downtown Los Angeles is about
14 km² and Manhattan is 59 km². Do not build 20 km² of city.
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
- **Density that varies 3.2× across the map** (0.38 in the Pines to 1.22 in Ashmount) — sparse
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
| 1 | Keep the **16.384 km terrain grid** but reduce the **content envelope to a 13.0 km square (169 km²)**, ringed by water, terrain above the 3,000 m hover ceiling, and a contamination band | High — the §1 data is unambiguous | High — envelope is data, not terrain |
| 2 | Correct the design assumption from 60 kt to **~110 kt cruise**; re-derive every timing estimate that depends on it | Very high — measured | n/a |
| 3 | Target **124 named POIs (0.73/km² of envelope)**: 11 Anchors, 45 Sites, 68 Kit POIs | Medium-high | High early |
| 4 | Target **~2,500 unnamed procedural features (≈15/km²)** — this matters more for atmosphere than POI count does | High | High |
| 5 | Track **content-hours per km² (target ≥ 0.30)** as the primary scope metric, not POI count. It is the axis on which worlds actually fail | High | High |
| 6 | Promote **threat envelopes (D-010) to the second thing built**, not the seventh. It is the world-scale system, not a progression system | High | High |
| 7 | Build **landing-site evaluation and a wire/obstruction system** early. It converts POI count into event count at near-zero cost | High | High |
| 8 | Re-frame **fuel (D-007) as economy and load, not range**. One tank is 34 map diagonals | Very high — measured | n/a |
| 9 | Hold the **interior : exterior ratio near 1 : 4**. Interiors are where the money goes and where the aerial view earns nothing | Medium-high | Medium |
| 10 | Build **Ashmount procedurally** with a hand-authored skyline and 5–8 interiors. Cap the dense core at ~8 km² | High | Medium |
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

### What changed during this research, and why

The first draft of §3.1 recommended keeping the full 268 km² and planning for 65% of it to be
empty. The measured data in §1 overturned that, and it is worth recording the reasoning so the
decision can be revisited rather than inherited:

- Every reference world turned out to be **three to six times smaller than published**. The
  comparison set the plan was reasoning against did not exist.
- **Content-hours per km²** — not POIs per km² — is the axis on which worlds are judged, and the
  plan landed at 0.19: between *GTA V* (0.67) and *Just Cause 3* (0.067), but much nearer the
  failure case.
- The plan's POI density, 0.46/km², is **within 10% of Just Cause 3's 0.42 settlements/km²**.
  Shipping the same numbers as the genre's canonical failure demands a stronger argument than
  "a helicopter makes emptiness cheaper".
- *Skyrim* fills **38.7%** of its own worldspace; *Horizon Zero Dawn* was **cut down
  mid-development** because Guerrilla could not fill it; Avalanche said explicitly that *Mad Max*
  was scaled to density rather than size and then shipped the thinnest world in the set anyway.
  The precedent for building more terrain than you fill is overwhelming — and so is the precedent
  for regretting it.

The compromise — full terrain grid, smaller content envelope, diegetic border built from water and
the aircraft's own measured hover ceiling — keeps the horizon that makes the world read as a
country while bringing the density back into the band every successful game in §1 occupies.

---

## Sources

**Measured areas and methodology**
- Bethesda cell math: [GECK wiki Units](https://geckwiki.com/index.php/Units) · [fallout.wiki Creation Kit/Cell](https://fallout.wiki/wiki/Resource:Creation_Kit/Cell) · [archived cell-count analysis](https://web.archive.org/web/20180614074653id_/http://www.gamesas.com/fallout-map-size-anolysis-t393192.html) · [FO4Edit navmesh filter](https://steamcommunity.com/app/377160/discussions/0/350543319567332507/) · [sprint-timing cross-check](https://steamcommunity.com/app/377160/discussions/0/1648791520835992591/?ctp=14)
- Skyrim worldspace rectangle: [MapFight](https://www.mapfight.xyz/map/skyrim/) · [Reality is a Game](https://www.realityisagame.com/archives/648/the-geographic-size-of-skyrim/)
- New Vegas dev bounding box (Josh Sawyer): [archived Bethsoft forums](https://web.archive.org/web/20160329135212/http://forums.bethsoft.com/topic/1139270-new-vegas-is-linear/?p=16657713) · [fallout.wiki Mojave Wasteland](https://fallout.wiki/wiki/Mojave_Wasteland)
- Elden Ring re-measurement: [PC Gamer via inkl](https://www.inkl.com/news/elden-ring-geographer-tests-rigorous-calculation-against-weed-fueled-horse-math-to-determine-the-exact-size-of-the-lands-between) · [NeverAwakeMan independent analysis](https://note.com/neverawakeman/n/n5725ff38d5bc?hl=en) · [VGTimes](https://vgtimes.com/gaming-news/118846-enthusiast-measures-the-size-of-elden-rings-open-world-reveals-its-much-smaller-than-initially-thought.html) · [the 79 km² myth](https://screenrant.com/elden-ring-open-world-map-how-big/)
- GTA V land/water split: [IPS News, KeWiS figures](https://ipsnews.net/business/2020/07/25/gta-ranking-the-maps-in-order-of-size/) · [GTA VI Insider](https://gtaviinsider.com/gta-vi-map-size-vs-gta-v/) · [TheGamer](https://www.thegamer.com/which-grand-theft-auto-has-the-biggest-world-map/) · on why figures diverge, [gta6explained](https://gta6explained.com/blog/gta-map-size-comparison)
- STALKER SoC level extents from shipped config: [OpenXRay game_maps_single.ltx](https://github.com/OpenXRay/xray/blob/d7b23596a70374d8a7ffda0e98852f93ce985182/trunk/resources/config/game_maps_single.ltx) · [ixray mirror](https://github.com/ixray-team/ixray-1.0-stsoc/blob/a11547a2e4e6426b77ebe4ee19550ab1cab7fdef/src/resources/config/game_maps_single.ltx) · [GSC official FAQ](https://soc.stalker-game.com/?page=faq) · [Wikipedia on the 18-level structure](https://en.wikipedia.org/wiki/S.T.A.L.K.E.R.:_Shadow_of_Chernobyl)
- Death Stranding: [Beyond Satire measurement](https://www.beyondsatire.com/investigations/death-stranding-map-size/)
- Just Cause 3: [Steam store claim](https://store.steampowered.com/app/225540/Just_Cause_3/) · [archived Inquisitr, Lesterlin interview](https://web.archive.org/web/20211013010927/https://www.inquisitr.com/1847980/just-cause-3-director-discusses-the-map-size-of-the-games-new-setting/) · [archived GameSpot](https://web.archive.org/web/20210515084541/https://www.gamespot.com/articles/just-cause-3-dev-talks-world-size-destruction-and-/1100-6423646/) · [Medici land/water estimate](https://justcause.fandom.com/wiki/Medici)
- Witcher 3's disputed 136 km²: [GamingBolt, Apr 2015](https://gamingbolt.com/witcher-3-map-size-compared-to-gta5-skyrim-far-cry-4-new-screens-show-different-visual-settings)

**Location and POI counts**
- [Fallout 3 locations](https://fallout.fandom.com/wiki/Fallout_3_locations) · [Fallout: New Vegas locations](https://fallout.fandom.com/wiki/Fallout:_New_Vegas_locations) · [Skyrim map markers](https://elderscrolls.fandom.com/wiki/Map_(Skyrim)/Locations) · [UESP clearable dungeons](https://en.uesp.net/wiki/Skyrim:Dungeons)
- [Elden Ring Sites of Grace](https://game-checklists.com/elden-ring/all-sites-of-grace/) · [dungeon entrances](https://lootmap.gg/elden-ring/guides/dungeons-caves-and-catacombs/) · [catacombs](https://segmentnext.com/elden-ring-catacomb-locations-and-map/)
- [JC3 military bases](https://justcause.fandom.com/wiki/Military_bases_in_Medici) · [JC3 towns](https://justcause.fandom.com/wiki/Towns_in_Medici) · [JC3 collectables](https://justcause.fandom.com/wiki/Collectable_Items_in_Medici)
- [Death Stranding network nodes, MapGenie](https://mapgenie.io/death-stranding/maps/world) · [Knot Cities](https://deathstranding.fandom.com/wiki/Knot)
- [Witcher 3 markers, MapGenie](https://mapgenie.io/witcher-3) · [RDR2 Points of Interest](https://reddead.fandom.com/wiki/Point_of_Interest) · [RDR2 MapGenie](https://mapgenie.io/rdr2/maps/rdr2)
- [Mad Max minefields](https://madmax.fandom.com/wiki/Minefield_(Mad_Max_Game)) · [Mad Max 100% guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3157776055)
- [MSFS hand-crafted airports](https://flight.wiki.gg/wiki/Microsoft_Flight_Simulator_(2020)/List_of_hand-crafted_airports) · [what "hand-crafted" means](https://flyawaysimulation.com/ask/answers/handcrafted-airports-microsoft-flight-simulator/)

**Traversal times** — all from [How Big Is The Map](https://howbigisthemap.com/)
- [GTA V, flying](https://howbigisthemap.com/gta-v-fly-across-the-map/) · [Skyrim, walking](https://howbigisthemap.com/skyrim-walk-across-the-map/) · [Skyrim SE, running](https://howbigisthemap.com/skyrim-special-edition-run-across-the-map/) · [Just Cause 3, walking](https://howbigisthemap.com/just-cause-3-walk-across-the-map/) · [Far Cry 2, walking](https://howbigisthemap.com/far-cry-2-map-2-walk-across-the-map/) · [Far Cry 2, driving](https://howbigisthemap.com/far-cry-2-map-2-drive-across-map/) · [Death Stranding, walking Central](https://howbigisthemap.com/death-stranding-walk-across-the-map-map-2/) · [Mad Max, driving](https://howbigisthemap.com/mad-max-drive-across-map/) · [RDR2 on horseback, via Twinfinite](https://twinfinite.net/news/heres-long-takes-cross-red-dead-redemption-2s-map/)

**Hours** — HowLongToBeat, pulled 2026-09-18
- [Elden Ring](https://howlongtobeat.com/game/68151) · [GTA V](https://howlongtobeat.com/game/4064) · [STALKER SoC](https://howlongtobeat.com/game/8038) · [Just Cause 3](https://howlongtobeat.com/game/26404) · [Death Stranding](https://howlongtobeat.com/game/38061) · [Mad Max](https://howlongtobeat.com/game/17610) · [Horizon Zero Dawn](https://howlongtobeat.com/game/26784)

**Design commentary and theory**
- [Jaber, *The 40 Seconds Rule and Points of Interest in The Witcher 3*, Uppsala University](https://uu.diva-portal.org/smash/get/diva2:1569059/FULLTEXT01.pdf)
- [BotW's triangle rule, Nintendo Life](https://www.nintendolife.com/news/2017/10/zelda_breath_of_the_wilds_ingenious_design_is_all_about_triangles_apparently) · [5 design lessons from BotW, Game Developer](https://www.gamedeveloper.com/design/5-design-lessons-learned-from-i-the-legend-of-zelda-breath-of-the-wild-i-)
- [Miyazaki on the Erdtree as a navigation landmark, GamesRadar](https://www.gamesradar.com/elden-ring-fromsoftware-hidetaka-miyazaki-interview/) · [FromSoftware sightline design](https://medium.com/@Jamesroha/world-design-lessons-from-fromsoftware-78cadc8982df)
- [Guerrilla on deliberately hiding HZD's size, and the 10× vertical miniature](https://gamingbolt.com/horizon-zero-dawn-heres-why-guerrilla-didnt-revealed-the-actual-map-size-it-will-destroy-the-illusion) · [HZD's world was cut down mid-development](https://en.wikipedia.org/wiki/Horizon_Zero_Dawn)
- [Avalanche: Mad Max scaled to density, not size](https://en.wikipedia.org/wiki/Mad_Max_(2015_video_game))
- [Just Cause 3 review, GamesRadar 3/5](https://www.gamesradar.com/just-cause-3-review/) · [Eurogamer](https://www.eurogamer.net/just-cause-3-review) · [Screen Rant](https://screenrant.com/just-cause-3-review/)
- [Death Stranding: slow walking as content](https://harrygboulton.medium.com/slow-walking-and-world-wandering-in-death-stranding-9130bf61981) · [Gamepressure on gated, slow traversal](https://www.gamepressure.com/death-stranding/does-the-game-have-a-large-in-game-world/z1ccf5)
- [Far Cry 2's absent fast travel](https://www.wholebeangames.com/blog/thoughts_on_games/the-lack-of-fast-travel-in-far-cry-2/)
- [Burgess, *Skyrim's Modular Level Design*, GDC 2013](http://blog.joelburgess.com/2013/04/skyrims-modular-level-design-gdc-2013.html) · [Skyrim's 8-person dungeon team](https://www.thegamer.com/skyrim-development-trivia-stories/)
- [StraySpark, open-world pacing](https://www.strayspark.studio/blog/open-world-design-pacing-player-freedom) — studio blog, heuristic only

**Figures actively distrusted** — recorded so they are not re-imported later
- [TheGamer, "Every Fallout Game, Ranked By Total Map Size"](https://www.thegamer.com/every-fallout-map-size/) — square-mile/km² unit error, and real-world footprints rather than game area
- [Trucoteca on Horizon Zero Dawn](https://trucoteca.com/en/how-big-is-the-horizon-zero-dawn-map/) — three mutually contradictory totals on one page
- [ScreenRant, whole STALKER trilogy at 7.5 km²](https://screenrant.com/stalker-2-map-size-comparison-original-trilogy/) — below the measured figure for SoC alone
- [Just Cause Wiki "Game limits"](https://justcause.fandom.com/wiki/Game_limits) — units error implying a 1,000,000 km² map
- [Mad Max "78 km²"](https://steamcommunity.com/app/234140/discussions/0/537405286657082759/) — uncited forum assertion; treat as folklore
- fallout.wiki's "459 marked locations" for Fallout 3 — a `{{DPL Counter}}` pointing at a category that does not exist, silently counting all location articles. Use 163.

**Not verified this session** — HowLongToBeat figures for Fallout 3 (none found) and New Vegas
(undated forum citation only); RDR2 and Mad Max playable area (no credible measurement exists);
Horizon Zero Dawn playable area (Guerrilla withholds it deliberately); a land-only figure for
Witcher 3's Skellige; GTA V's district and collectible counts (search-snippet only); Mad Max camp,
scarecrow, sniper-tower and convoy counts (published only inside annotated map images); Fallout 3's
own HLTB entry. Reddit was unreachable from every tool this session, so no measurement thread was
read first-hand.
