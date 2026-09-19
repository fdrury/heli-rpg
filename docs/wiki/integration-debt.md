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

`sim/src/Salvage.cs` is complete and covered, and is currently **dead code in the running
game**. Five hooks, in the order that matters:

1. **Nothing wears components with flight time.** `Salvage.WearOver` is written and tested;
   the flight loop needs a per-component tick:
   `damage.Apply(c, Salvage.WearOver(c, health, hours), DamageCause.Wear)`.
   *Without this, condition never degrades and the entire demand side of the economy does
   not exist.* This is the one that matters; the rest are plumbing.
   **Owner:** whoever holds `sim/src/Helicopter.cs`.

2. **`SiteInteraction.AddSalvage` still rolls its own yields** — a `switch` on `SiteKind`
   with its own RNG. It should call `Salvage.SearchesAt` / `Salvage.Search` with
   `(SalvageSiteKind)(int)site.Kind` and `site.Tier`. The two enums are deliberately kept in
   the same order so that cast is valid.
   **Owner:** `game/scripts/SiteInteraction.cs`.

3. **`Progress.CarriedMass` does not know about cargo**, so salvaged parts weigh nothing.
   Cleanest fix: `Progress` gains `Cargo Cargo { get; } = new()` and `CarriedMass` adds
   `Cargo.Mass`. (The alternative — a `"cargo"` `MassItem` added from the Godot layer — puts
   the mass budget in two places.)
   **Owner:** `sim/src/Progress.cs`.

4. **`SaveData` has no cargo field.** Needs `List<CargoPartSave> { Id, Condition }` plus
   capture and apply. `Cargo.Restore` already takes exactly `(string Id, double Condition)`
   tuples.
   **Owner:** `sim/src/SaveData.cs`.

5. **Unserviceable thresholds are baked into expressions** in `Damage.cs` (`Airworthy`
   0.25/0.20/0.15, `SkidsServiceable` 0.25, `AvionicsWorking` 0.35). `Salvage` mirrors them
   with comments pointing back, which is two sources of truth. They want to be named
   constants on `DamageState`.
   **Owner:** `sim/src/Damage.cs`.

**Noted, not a hook:** `Loadout.ModuleAtSite` uses `new Random(seed)`, whose sequence .NET
does not guarantee across versions — a runtime upgrade could reshuffle which sites hold
which modules. `Salvage` uses a fixed integer mix for exactly that reason.

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
