# Decision log (ADRs)

Format: **D-nnn — Title** · *date* · Decision / Why / Reversibility.
Fred can veto any of these; entries marked **[LOCKED-IN BY FRED]** came from him directly.

---

### D-001 — Engine: Godot 4.7.2 (.NET/C#) · 2026-09-18 · **[LOCKED-IN BY FRED]**
Vulkan Forward+ renderer, text-based `.tscn`/`.tres` scene format, headless mode for CI.
**Why:** the only mainstream engine whose entire project is diffable plain text, which is what
makes multi-day autonomous development possible. C# gives a real test framework and the
performance headroom for blade-element physics and voxel construction.
**Reversibility:** high for the sim (it is a pure .NET library with zero engine references);
low for scenes/renderer.

### D-002 — Physics core lives outside the engine · 2026-09-18
`sim/` is a plain .NET 8 class library (`Rotorwash.Sim`) with **no Godot dependency**, using
`System.Numerics`. The Godot layer is a thin adapter that reads Godot state in, writes forces out.
**Why:** physics can be unit-tested and tuned headlessly at thousands of steps per second without
launching an editor. This is worth a small amount of marshalling cost.
**Reversibility:** high.

### D-003 — Target hardware & the two lighting paths · 2026-09-18 · **[LOCKED-IN BY FRED]**
Floor: **GTX 1080 / i5 / 32 GB at 1080p60** (Fred's test rig — Pascal, no hardware RT, no DLSS).
Ceiling: modern GPUs get realtime GI.
Decision: author every scene to look correct under **both** a baked-lightmap path (Low/Med) and a
**SDFGI + SSIL + volumetric fog** path (High/Ultra). Practically this means: light rigs use real
physical lights (never lightmap-only fakery), and lightmaps are baked from that same rig.
**Reversibility:** high early, expensive after a few dozen authored locations. Enforced by a
`QualityTier` autoload from day one.

### D-004 — Flight model: gamified blade-element · 2026-09-18 · **[LOCKED-IN BY FRED]**
Per-blade-element aerodynamics with real induced-flow, so translational lift, ETL, torque,
ground effect, VRS and autorotation emerge. Assists (attitude limiter, auto-trim, collective
governor) are a *layer on top* that the player can remove.
**Reversibility:** the assist layer is trivially tunable; the core model is the foundation.

### D-005 — Progression is gear-and-knowledge, not XP · 2026-09-18 · **[LOCKED-IN BY FRED]**
No experience points, no levels, no abstract skill tree. Capability comes from charts,
frequencies, schematics, tools, contacts, and physical parts. **Avionics hardware *is* the
skill tree** — an attitude-hold unit you scavenge and install is literally an assist unlock.
**Why Fred's call is right:** it makes looting meaningful, makes the block builder central
rather than cosmetic, and it is diegetic — the player's power is visible on the airframe.
**Reversibility:** medium. A light proficiency layer can be added later if progression
feels flat, but the default is that the *machine* levels up, not a hidden number.

### D-006 — Dialogue: baked corpus + local SLM "coda" · 2026-09-18 · **[LOCKED-IN BY FRED]**
Fred's design, and it is the good one: the NPC delivers a **pre-baked, hand-vetted line**
immediately, and that line's delivery time is used as **latency cover** while a small local
model streams in a 1–2 sentence personalised tail reacting to what the player actually said.
If the model is absent, slow, or produces something that fails validation, the baked line
simply ends naturally and nothing is lost. Zero token cost to anybody, ever.
**Reversibility:** high — the coda is strictly additive.

### D-007 — Fuel/wear is a core but forgiving loop · 2026-09-18 · **[LOCKED-IN BY FRED]**
Fuel, component wear and damage drive route planning and scavenging. No instant-death gotchas:
engine failure always leaves an autorotation, running dry strands rather than kills.
**Reversibility:** high (tuning constants).

### D-008 — No other helicopter pilots exist · 2026-09-18 · **[LOCKED-IN BY FRED]**
World-building constraint with mechanical teeth: air threats are ground-based AA, drones,
tethered balloons and weather — never rival helicopters.
**Reversibility:** low, by design. It is a pillar.

### D-009 — Working title "ROTORWASH" · 2026-09-18
Placeholder; rotorwash is the downwash that flattens grass, kicks up dust and announces your
arrival from a mile away — which is thematically the whole game. Trivially renameable
(one constant + folder name). **Reversibility:** high.
