# Integration debt

Things that are **built and tested but not yet wired in**, and the exact hook each one
needs. This file exists because several agents work in one checkout at once: an agent that
owns `sim/src/Salvage.cs` cannot safely edit `sim/src/Progress.cs` while another process is
mid-write in it, so the correct move is to finish the owned work, write the hook down here,
and let whoever owns the other file apply it.

A thing on this list is worse than a thing not built: it costs maintenance and returns
nothing until it is connected. Clear it, do not grow it.

---

## Salvage economy → the game (from `916f9ef`)

`sim/src/Salvage.cs` was complete and covered and **dead code in the running game**. Four
of the five hooks are now in. One is left, and it is the one that fills the cargo list.

1. ~~**Nothing wears components with flight time.**~~ **DONE.** Not as a per-frame tick —
   that was tried and it moved the autorotation rate of descent from 3193 to 3859 fpm off a
   1.5e-4 perturbation of blade condition (`02-damage.md`). Instead `DamageState` runs a
   Hobbs meter in `UpdateSystems` while the rotor turns and charges the hours through
   `Salvage.WearOver` the moment it stops (`AccrueFlightHours`). One wear model, booked at
   the moment D-003a says to charge the player — when he lands. Measured: a main rotor is
   good for 151 flight hours, an engine 112, and the aircraft as a whole — with the
   couplings, serviced between sorties — stops being airworthy at **53 flight hours**.
   Covered by `salvage_hours`.

2. **`SiteInteraction.AddSalvage` still rolls its own yields** — a `switch` on `SiteKind`
   with its own RNG. It should call `Salvage.SearchesAt` / `Salvage.Search` with
   `(SalvageSiteKind)(int)site.Kind` and `site.Tier`. The two enums are deliberately kept in
   the same order so that cast is valid. **This is now the only thing standing between the
   salvage economy and the game**: mass, fitting and the save all work on
   `Progress.Cargo`, and nothing puts a `SalvagePart` into it. Found parts still arrive as
   `Stock.Parts` at a nominal 6.5 kg each.
   **Owner:** `game/scripts/SiteInteraction.cs`.

3. ~~**`Progress.CarriedMass` does not know about cargo.**~~ **DONE.** `Progress.Cargo` is a
   `Cargo`, `CarriedMass` adds `Cargo.Mass`, and the Godot layer's existing
   `KeepMassInSync` carries it to the rotor with no second mass budget. Measured: a 200 kg
   haul costs 625 m of hover ceiling (3250 m → 2625 m) and 54% of the rate of climb at
   1500 m. Covered by `salvage_cargo`.

4. ~~**`SaveData` has no cargo field.**~~ **DONE.** `List<CargoPartSave> { Id, Condition }`,
   captured in `CaptureProgress` and applied in `ApplyProgress`. Only id and condition are
   stored, so retuning a part's mass retunes every existing save; an id that has left the
   catalog is dropped rather than throwing, and a save written before the field existed
   still opens. Covered by `salvage_cargosave`.

5. ~~**Unserviceable thresholds are baked into expressions.**~~ **DONE.** They are named
   constants on `DamageState` (`MainRotorFloor` and the rest) with
   `DamageState.UnserviceableAt`, and `Salvage.UnserviceableAt` forwards to it. One table.

**Noted, not a hook:** `Loadout.ModuleAtSite` uses `new Random(seed)`, whose sequence .NET
does not guarantee across versions — a runtime upgrade could reshuffle which sites hold
which modules. `Salvage` uses a fixed integer mix for exactly that reason.

---

## ~~⚠ `SiteInteraction.cs` is the bottleneck~~ **ALL FOUR DONE**

All four hooks are now wired in `SiteInteraction.cs`:

1. ~~**Salvage yields.**~~ `AddSalvage` calls `Salvage.SearchesAt`/`Salvage.Search` and puts
   `SalvagePart` into `Progress.Cargo`.
2. ~~**Named NPCs.**~~ `GetOrCreateNpc` consults `StoryPlaces.For(site.Id)` and
   `DialogueCorpus.Named(npcId)`.
3. ~~**Richer generic register.**~~ `DialogueCorpus.Settler` and `SettlerLines` with
   `RegionTag` and `SiteKindTag`.
4. ~~**Knowledge on search.**~~ `StoryPlaces.For(site.Id)?.GrantsOnSearch` is granted in
   `AddSalvage`.

---

## ~~`AlertState` → nothing drives it~~ **DONE** (D-057)

`ThreatWorld` now drives `AlertState`: detection per emitter with LOS, engagement spikes on
launch, game-time decay every frame. `DetectionScale` and `ReactionScale` feed into
`ThreatField.Update` via cached emitter→region mapping. Persisted in `SaveData.AlertLevels`.
Kneeboard MAP header shows raised regions. The radio announcer (`RadioDj`) already reads
`AlertState.Raised()` — it now has live data to work with.

---

## Story spine → the engine (from `c67607d`)

`docs/wiki/story.md` specifies places that must mean particular things. `SiteKit.Build`
switches on `SiteKind` alone and every name is seed-generated, so there is currently no way
to say "this site is the drowned ferry".

A `StoryPlaces` layer is in progress. When it lands it needs a hook in `SiteKit.Build`,
`AddSalvage` and `GetOrCreateNpc` to consult role bindings before falling back to generic
generation.

---

## The search thread → D-008 (from `D-050`)

`sim/src/SearchThread.cs` makes the search target a rival **helicopter pilot** and ends on
*"Two pilots, two aircraft"*. D-008 is locked by Fred and forbids it; D-010's gating
rationale depends on there being exactly one aircraft. The correction is content only — the
beat machinery, knowledge ids, staging and save format are sound and should be kept.
See D-050 and section 7 of `story.md`.
