# Accessway Pathfinding (least-work corridor search)

Status: planned design note. Candidate replacement for the straight-corridor generator described under *Accessway Routing* in [../in-progress/access-framework.md](../in-progress/access-framework.md).

This document describes an alternative accessway **generation** strategy: instead of enumerating straight corridors and ranking them, treat "connect this origin cluster to tower-reachable ground" as a **least-work corridor search over the terrain heightfield**. The work of digging or dumping terrain becomes graph cost, and the slope rule becomes graph structure. The rest of the access framework - clustering, the grounded-reachability flood, completion, phase gating, diagnostics - is unchanged; only the *routing* step is swapped.

It is deliberately scoped as an A/B alternative behind the existing `AccessCandidate` interface, not a rewrite. The current generator stays until this one demonstrably wins on real saves.

## Public feature gate

Expose this generator as an experimental public toggle:

* **Setting:** `Turning ramps (experimental)`
* **Default:** off
* **Scope:** enables only the V1 search space today: vanilla flat/slope designations with `accessWayClearance = 1`.
* **Tooltip:** `When enabled, ATD may select and place experimental V1 turning or switchback accessways using vanilla flat and slope designations. Requires ramp width 1; corridor clearance is independent. Wider ramps and corner or saddle designations are not included.`

When the toggle is off, accessway generation uses the current straight-corridor generator unchanged. When it is on, the framework evaluates V1 alongside the straight generator, compares both through the production candidate ranking, and may place the V1 result. V1 placement is revalidated immediately before mutation and rolled back if placement or the post-placement reachability flood fails; the straight candidate remains the fallback.

V1 uses the source work operation only to constrain source work, not to force the whole accessway to be mining or dumping. Generated accessway bodies use leveling designations, allowing a single route to combine excavation and fill where required by the terrain. A generated V-to-G edge selects mining or dumping from the handoff origin's connecting edge, not from the route start or terminal profile center. The handoff edge is the only frontage that must attach to G; if that edge lies below current ground, the final V tile needs a mining proto, and if it lies above current ground, it needs a dumping proto. The terminal center is deliberately not used because it may already have crested or may still lie on either side of uneven ground. The search reconstructs vanilla's operation-specific fulfilled bitmap and admits the edge only through a fulfilled perimeter tile that is also tower-reachable G. That operation is carried through materialization and the final V tile is placed with the matching mining or dumping proto; it may not fall back to leveling. Existing leveling and specialized terminal designations are reusable providers. This keeps corridor geometry independent of whether the source cluster came from mining or dumping work while avoiding leveling overshoot at a ground attachment.

## Why

The straight-corridor generator has three known limits (see *Accessway Routing -> Current limitations*): no turning/switchback, no single-pass multi-bend chain, and no innate preference for cheaper geometry. A path search dissolves all three at once - turns, dog-legs, and switchbacks are simply cheaper paths through the lattice, and "cheapest" is the cost function itself. It also unifies *routing* and *selection*: the path cost is the ranking.

## It is 2.5D, not volumetric 3D

CoI terrain is a **heightfield** - every `(x, y)` column has a single surface height, and terrain designations can only raise or lower that surface. There are no tunnels or overhangs from terrain ops alone. So the search space is **`(origin tile x quantized height level)`**, not full voxels. This is the crucial simplification: the state space is bounded (area origins x a few tens of height levels between the cluster floor and surrounding ground), so A* / Dijkstra is tractable per pass.

True volumetric 3D (tunnels, overpasses, bridge **entities**) is explicitly **out of scope**; it would require placeable structures rather than terrain edits and is a different, much larger project.

## Core model

**Node.** The MVP graph is heterogeneous:

  * **V nodes** are `(origin, h, mode)` on the origin lattice, where `mode in { F, X+, X-, Y+, Y- }`.
  * **G nodes** are `(tile, h, G)` on the vanilla tile lattice, where `h` is the computed vanilla pathing height at that tile.

Augmenting the state with height is what makes the slope constraint local and keeps this a clean shortest-path problem; without it, adjacent target heights would be coupled constraints rather than edges. For the first **V1** implementation (flat + axis-aligned slopes only, single-lane clearance), `mode` is either vanilla ground/path reuse (`G`) or one of the emitted V designation families. The extra mode is what makes the "flat landing between differently-axised slopes" rule local enough for Dijkstra/A*.

**Edge.** A move from one node to a neighbouring node exists **iff** the step satisfies the relevant admissibility predicate (below). V-to-V moves advance one origin (4 tiles). G-to-G moves advance one tile through vanilla pathing. V-to-G and G-to-V moves cross between the generated origin-lattice corridor and vanilla tile-lattice pathing. The slope rule is therefore the graph's structure, not a post-filter. **Terrain-work steps are axis-aligned only** - digging, dumping, and leveling all proceed along X/Y grid axes, so a corridor edge that changes terrain advances `X+`, `X-`, `Y+`, or `Y-`, not diagonally. (Diagonal *adjacency* still matters for the fight invariant below, but a terrain-changing edge is never a diagonal move.)

**Two admissibility predicates - traversal vs construction.** These are different bounds and the search must keep them separate:

  * **Traversal admissibility (`<= 0.5` per step)** - the *access-check* rule from the framework's *Edge-compatible*. It governs whether a vehicle can drive an edge over terrain/designations that already exist or will exist. Use it for grounding and for reusing existing accessways.
  * **Construction admissibility (`<= 0.25` per step today)** - the rule for any edge that ATD must **build** by digging or dumping. Constructed terrain is bound by the in-game **allowed-slope parameter**, currently `1` (max within-designation delta `1`), which effectively caps the *buildable* slope at `0.25`. An edge that changes terrain is admissible only at the construction bound, not the looser traversal bound. A full 1-level change therefore needs at least two horizontal tiles, exactly as today.

  The two differ because a `0.5` slope is drivable but **not constructible/workable**: pushing the allowed-slope all the way to within-designation delta `2` (slope `0.5`) has **proven not to work** - excavation cannot take place on that slope. The **saddle designation** is the practical middle ground (slope stays `0.25`, but the *diagonal* corner delta may be `2`) and is the relaxation knob to experiment with later, not the full `0.5` slope.

**MVP edge cost.** Two cost terms, both real and deliberately simple. The MVP starts with center-height work to keep the first search small; the planned production refinement for mountain and cliff routing is specified in [Accessway Pathfinding Side-Ray Work Cost Amendment](accessway-pathfinding-side-ray-cost.md):

  * **Terrain work** - for a new V1 designation, approximate work by the absolute center-height delta between the current terrain center height and the candidate node height: `work = abs(h - terrainCenter(origin))`. Do **not** apply any useful-product rebate in the MVP. A pre-existing designation that the search reuses has **zero work cost** because it is already scheduled to be worked anyway.
  * **Traversal length** - every transition pays a positive driving cost equal to tile Manhattan length: `deltaX + deltaY`. A V-to-V origin step therefore costs `4` length units, while a G-to-G vanilla tile step costs `1`. A longer corridor lengthens every future haul: the mining trucks that will work the whole dig site drive this accessway repeatedly, so a long flat detour across prepared ground imposes a real downstream cost on the excavation/mining teams.

Combine them with one global tuning parameter that translates work into distance:

```text
edgeCost = (deltaX + deltaY) + workDistanceScale * work
```

Start with `workDistanceScale = 1`, meaning one product-unit of center-height work is treated like one tile of travel. Keep it configurable in public mod settings because it is the main behavioural knob. This cost is additive, local, and non-negative, which is the main A*/Dijkstra requirement. The exact footprint integral, distinct-corner accounting, and useful-product discount can be revisited later; they are intentionally outside the MVP cost function.

**Start / end.** For a cluster `C`, choose a representative start origin `S` by averaging the cluster origins' center coordinates `(x, y)` and taking the origin whose center has the smallest Manhattan distance to that average. The search runs from `S` toward the tower center `T`, but `T` itself is inside the tower and not pathable, so it is **not** the graph end. The end condition is any precomputed `G` node `E` in the tower-reachable vanilla flood. In other words: route from the cluster to tower-reachable pathable ground, not literally into the tower footprint.

Because every admissible edge already encodes slope, and cost already encodes work, **the path cost is the candidate's score** - routing and selection collapse into one search.



## Side-ray work-cost amendment

The center-height terrain-work term above is the MVP cost only. The planned production scorer is specified in [Accessway Pathfinding Side-Ray Work Cost Amendment](accessway-pathfinding-side-ray-cost.md). It scores each generated segment's lateral exit corners with bounded accelerating rays and integrates `stepLength * dh` until the material-slope ray reaches terrain, so hillside routes pay for side-wedge depth instead of only local vertical delta.

## Debris amendment

Debris pathfinding is specified in [Accessway Pathfinding Debris Amendment](accessway-pathfinding-debris.md). In short, debris that sits on otherwise drivable ground remains a **G-node route option** with a small cleanup cost, and materialization emits debris-removal mining designations rather than forcing a G/V/G dig-under detour.

## From path to designations (the corner problem)

A node carries a single reference height `h`, but a CoI designation is defined by **four corner heights**, and the fight invariant (next section) is stated over shared corners. The search therefore needs an explicit mapping from a path of height/mode nodes to concrete designations. This has two parts: the allowed **jumps** between adjacent nodes, and the **transformation** of a jump sequence into designation pieces.

### Search-space names

This document uses two separate naming axes:

* **Designation set** - `V` means vanilla flat/slope designations only; `V'` adds corner designations; `V''` adds saddles or other stronger shapes.
* **Clearance version** - `1` means `accessWayClearance = 1` (single lane); `2` means `accessWayClearance = 2` (double lane).

So **V1** means **V designation set + clearance 1**. The signed-mode graph and edge-profile math below describe V1. **V2** means **V designation set + clearance 2**; it reuses the same signed height-gradient idea, but each node is a 2x2 origin brush rather than a single origin. This is separate from `V'`, which changes available designation shapes.

### Lattice coordinates by clearance

The node lattice is offset by clearance parity, which makes *Clearance as a lattice-parity rule* concrete. V1 uses the first row:

* **Clearance 1 (single lane)** - nodes are origin **centers**: tiles with `x, y` in `2 + 4n`.
* **Clearance 2 (double lane)** - nodes sit on origin **edges/vertices**: tiles with `x, y` in `4n`.

Adjacent nodes are one origin apart (4 tiles) in an axis-aligned direction.

Distance and durability use tile units: one origin-lattice step is 4 horizontal tile units, one G-lattice step is 1 horizontal tile unit, and one elevation level is treated as 1 tile unit for slope/durability comparisons.

To avoid compass ambiguity, this note describes movement by coordinate delta rather than compass labels:

| Direction | Origin-grid delta | Terrain-tile delta |
|---|---:|---:|
| `X+` | `(+4, 0)` | `(+1, 0)` |
| `X-` | `(-4, 0)` | `(-1, 0)` |
| `Y+` | `(0, +4)` | `(0, +1)` |
| `Y-` | `(0, -4)` | `(0, -1)` |

Height changes use the same compact sign style:

| Height step | Meaning | Bound |
|---|---|---|
| `h0` | `h' == h` | no height change |
| `h+` | `h' > h` | `h' - h` must fit the construction slope limit |
| `h-` | `h' < h` | `h - h'` must fit the construction slope limit |

The reference height `h` is the **center height** of the origin footprint: the average of its four integer corner heights. For V shapes this means flat nodes have integer `h`, while sloped nodes have half-integer `h`. Store the search height as a scaled integer rather than a float (`h2 = 2*h` is enough for V; use `h4 = 4*h` if V' corner shapes enter the graph later).

### Allowed jumps depend on the designation set

Which transitions are legal between adjacent origins depends on which designation shapes ATD is allowed to emit. Three sets, increasing in power:

* **V1 - vanilla shapes only (flat + slope), clearance 1.** Each non-ground non-flat origin is a slope descending along one coordinate axis, with the signed direction carried as the local height-gradient sign (`X+`, `X-`, `Y+`, or `Y-`). Transition rules:
  * From a **ground** tile `G`: follow vanilla pathing / access-check rules. `G` represents non-designation terrain outside durability-blocked zones. Its `h` is the computed vanilla pathing height for that tile. `G` nodes are precomputed before the A* search.
  * From a generated **flat** origin `F`: any axis-aligned direction is allowed. If `h(o) == h(o')` the successor may be flat or `G` (a level join); if `h(o) != h(o')` the generated successor **must be a slope** - only a slope can change height.
  * From a **slope** origin `o` on axis A:
    * moving **along** A (down or up the slope): `o'` must be **flat or a slope on the same axis** (`Y` -> `Y`, `X` -> `X`).
    * moving **perpendicular** to A (strafing): `o'` must be a slope on the **same signed direction** as `o` (`Y+` -> `Y+`, `Y-` -> `Y-`, `X+` -> `X+`, `X-` -> `X-`).
  This is the conservative set: a turn between two differently-axised slopes requires a flat landing in between.

  For implementation, the V1 search graph should use **ground plus signed slope modes**:

  ```text
  mode in { G, F, X+, X-, Y+, Y- }
  ```

  `G` is not an emitted designation shape; it is the vanilla pathing mode for traversing already-pathable non-designation ground. For the generated V modes, the sign is the **height-gradient sign**, not a travel direction:

  * `X+` - height increases as coordinate X increases.
  * `X-` - height increases as coordinate X decreases.
  * `Y+` - height increases as coordinate Y increases.
  * `Y-` - height increases as coordinate Y decreases.

  Equivalently, `X+` means the higher edge/corners are on the `X+` side of the designation and the lower edge/corners are on the `X-` side. The same convention applies to `Y+`/`Y-`. Because all corners are integer and `h` is the center average, every legal transition has an exact computable `h' - h`.

  Define each mode by its edge height offsets relative to `h`. For X edges, list the two corner offsets in `Y- -> Y+` order. For Y edges, list them in `X- -> X+` order.

  | Mode | `X-` edge | `X+` edge | `Y-` edge | `Y+` edge |
  |---|---|---|---|---|
  | **F** | `[0, 0]` | `[0, 0]` | `[0, 0]` | `[0, 0]` |
  | **X+** | `[-0.5, -0.5]` | `[+0.5, +0.5]` | `[-0.5, +0.5]` | `[-0.5, +0.5]` |
  | **X-** | `[+0.5, +0.5]` | `[-0.5, -0.5]` | `[+0.5, -0.5]` | `[+0.5, -0.5]` |
  | **Y+** | `[-0.5, +0.5]` | `[-0.5, +0.5]` | `[-0.5, -0.5]` | `[+0.5, +0.5]` |
  | **Y-** | `[+0.5, -0.5]` | `[+0.5, -0.5]` | `[+0.5, +0.5]` | `[-0.5, -0.5]` |

  Expansion is then mechanical. For a candidate move direction `d` and successor mode `m'`, compare the outgoing edge profile of current mode `m` with the incoming opposite edge profile of `m'`:

  ```text
  h + outEdge(m, d)[i] == h' + inEdge(m', opposite(d))[i]
  ```

  The transition is legal iff both shared corners imply the same `h' - h`. That value becomes the successor height delta. If the two corners imply different deltas, the transition is inadmissible because the shared edge would fight. For a transition from an existing designation or `G` node into a generated V node, use the existing edge profile as the fixed side of this same equation. In practice this can be an explicit compatibility table because there are only two canonical shared-edge forms to match:

  * **Level edge** - both shared corners have the same offset.
  * **Tilted edge** - the two shared corners differ by one slope step.

  Existing vanilla profiles reduce to those forms on each side: flat designations have only level sides; ramps and corners each have two level sides and two tilted sides (ramps have parallel level sides, corners have adjacent level sides); saddles have all sides tilted.

  Examples:

  * `F --X+--> X+` gives `h' - h = +0.5`.
  * `X+ --X+--> X+` gives `h' - h = +1.0`.
  * `X+ --X+--> X-` gives `h' - h = 0` (a ridge with both slopes meeting at the high edge).
  * `X+ --Y+--> X+` gives `h' - h = 0` (perpendicular strafe along the same signed slope).
  * `X+ --Y+--> X-` is inadmissible because the two shared corners imply different deltas.

  Every computed nonzero transition is still bounded by the construction slope limit and the global height search bounds. The earlier three-mode form `{F, X, Y}` is a useful explanatory shorthand, but it is too lossy for the real graph: it merges signed slope states that can have different corner heights and different fight-invariant results.

  V1 neighbor expansion is therefore:

  1. Enumerate axis-aligned direction `d`.
  2. Enumerate successor mode `mode'` from `{G, F, X+, X-, Y+, Y-}`.
  3. If `mode' == G`, accept only if the successor tile is in the precomputed G set and vanilla pathing permits the move.
  4. Otherwise solve the edge-profile equation for `h'`.
  5. Reject if no unique `h'` exists, if the construction slope bound fails, if `(origin', h', mode')` is outside search bounds, if the fight invariant fails, or if the durability envelope blocks any candidate corner.
  6. Assign the local edge cost and push the successor.

* **V2 - vanilla shapes only (flat + slope), clearance 2.** Selecting clearance 2+ means the player is asking ATD to provide access for mega vehicles, primarily mega excavators. Mega vehicles need 5 terrain tiles of clearance, which corresponds to two 4x4 designation origins side by side. Model this as pathing with a **2x2 origin brush**:
  * A V2 node is `(brushVertex, h, axis, profile)` where `brushVertex` is the lower/negative corner of a 2x2 origin footprint on the clearance-2 lattice, `h` is the brush's reference center height, `axis in { X, Y }` is the corridor travel axis for non-turn states, and `profile` is a width-2 string of V1 modes across that corridor's cross-section.
  * The profile string has one token per side-by-side lane. For clearance 2, examples are `FF`, `FX+`, `X+X+`, and `X+X-`. For a future clearance 3, the same notation extends naturally to `FFF`, `X+FX+`, `X+X+X+`, and so on. This is preferable to inventing special names for each width. When signs make a compact string hard to read, diagnostics may print the same profile as a delimited token list.
  * `FF` is a full flat 2x2 brush. `X+X+` is a two-lane uniform ramp whose covered origins all slope with height increasing in `X+`. `FX+` means one cross-section/lane is still flat while the adjacent one is ramping. Which physical origins those tokens occupy is determined by the node's `axis` and movement direction.
  * Initial clearance-2 profile set:

    ```text
    FF
    FX+  X+F  FX-  X-F  FY+  Y+F  FY-  Y-F
    X+X+ X-X- Y+Y+ Y-Y-
    X+X- X-X+ Y+Y- Y-Y+
    ```

    The first row is the flat turn/landing brush. The second row covers flat-to-ramp and ramp-to-flat transition brushes. The third row covers uniform two-lane ramps. The fourth row covers opposed same-axis pairs, which can be optimal when connecting two level profiles separated by two origins. Mixed-axis profiles such as `X+Y+` are deferred to a later full-band search space.
  * In the first V2 model, restrict profiles to coherent two-lane bands: every non-flat token in a profile must use the same axis family (`X` or `Y`), signs may differ, and the brush must remain drivable across its width. Fully arbitrary lane-asymmetric mixtures are a later full-band search space.
  * Straight travel advances the 2x2 brush by one origin step in the movement direction. The new brush overlaps the old brush by one 1x2 strip. The overlapping strip must have identical origin profiles, using the same edge-profile equation as V1 but lifted from a 1D shared edge to a shared strip.
  * Strafing is legal as a lateral one-origin step while maintaining the same corridor axis and profile string. The new brush overlaps the old brush by the side lane/strip; that overlap must be identical, and the newly exposed strip must pass the same construction slope, fight-invariant, durability, bounds, and workability checks as straight travel. This is the V2 analogue of V1 slope strafing (`X+ -> X+` while moving in `Y`, or `Y+ -> Y+` while moving in `X`).
  * A turn is legal only through `FF`. Because the node footprint itself is 2x2, a flat V2 node is the required 8x8-tile landing where a two-lane corridor can pivot without clipping the inside or outside lane. A slope-to-slope axis change remains illegal unless an intervening `FF` brush exists.

  This gives a compact and V1-compatible mode scheme:

  ```text
  V1 node: (origin, h, F|X+|X-|Y+|Y-)
  V2 node: (2x2 brush vertex, h, axis, profile-string)

  Clearance 2 profile examples: FF, FX+, X+X+, X+X-
  Clearance 3 profile examples: FFF, X+FX+, X+X+X+
  ```

  Avoid punctuation-heavy names such as `F_to_X+` or `F+X+` in the graph state. They are readable in prose but do not scale well to clearance 3+. The profile string itself is the name.

* **V' - corners allowed (future).** Adding corner designations (one corner raised or lowered relative to the other three) opens many more transitions. Note that the **current straight-corridor ramp generator does *not* use V'** - it emits only flat/slope shapes (V); the V' corner shapes appear in the *mining designation area*, which is a different algorithm altogether and not the routing path this note replaces. A reasonable first model for adopting V' here - to be verified against the actual corner proto rules - is that a corner acts as a quarter-turn between two slope axes: a slope may transition into a corner that begins reorienting its descent axis, and a corner may be followed by a slope on the *new* axis, giving an L-bend **without** an intervening flat landing. A single-corner height change can also satisfy the fight invariant against a diagonal neighbour that a flat/slope pair could not. The exact admissible corner-to-slope and corner-to-corner transitions should be enumerated from the game's corner designation definitions before the search relies on them.

* **V'' - saddles allowed (future).** Out of scope here; revisit once the V/V' designation sets are proven.

**The first implementation restricts itself to V1** (flat + slope, single lane). That set already beats the current generator - which is also limited to flat/slope but additionally to a *single straight segment* - because the search can turn and switchback within V1. V2, V', and V'' are later relaxations.

### Transformation

Given a validated jump sequence and the chosen per-node heights, emitting designations is the comparatively easy step: each node becomes the flat/slope/corner piece its incoming and outgoing jumps imply, with corner heights set so that (a) the along-path slope respects the construction bound and (b) every shared corner matches its neighbour (the fight invariant). Because the jump rules already constrain which piece each node can be, the transformation is largely a lookup from `(incoming axis, outgoing axis, height delta)` to a designation shape.

`G` nodes emit no designations. They are vanilla pathing segments in the returned candidate. A final candidate may therefore contain generated V segments joined to existing/pathable G segments. After the path is found, run a full-path validation pass over the generated V designations, existing fixed profiles, and G handoff seams. This catches non-consecutive side or diagonal self-contact that a local predecessor-only expansion might not have seen.

### Existing designations during search

Existing designations are **invariant during one search**. This means designations active in the vanilla game world when the search begins. Stored internal state, temporarily hidden designations, or speculative ATD designations do not count; there is no separate paused-designation state in the vanilla game. The generator may path along a pre-existing active designation if vanilla pathing says it is traversable; that segment is represented as a fixed existing designation profile and has no terrain-work cost. It still pays traversal length.

When the path leaves an existing designation into a newly generated designation, the existing designation's shared edge is fixed. For example, when moving in `X+`, the candidate node's `X-` edge profile must match the existing designation's `X+` edge profile. That restricts both the candidate mode and `h'`. This is the same edge-profile equation as the generated-to-generated case, with one side fixed by the existing designation rather than by a search mode.

### Crossing between V and G

`G` is tile-based while V1 generated designations are origin-based, so the transition between them is a prospective workability check rather than another origin-to-origin edge. Exact center or corner equality is not required. For each generated terminal profile:

1. Identify the generated origin edge that faces the G handoff. Compare that entire edge's target height to the current terrain along the same edge: below selects mining; otherwise select dumping. If one end of the edge needs mining while the other needs dumping, the frontage is not a valid single-operation handoff. If the handoff has no normal predecessor in the path, for example because the start itself is a fixed intent origin or an existing accessway, the same rule applies to the handoff origin's exposed edge. Do not classify from the terminal center because it may or may not have crested.
2. Reconstruct the selected proto's 25-bit fulfilled bitmap over the bilinear 5x5 designation profile by invoking the same vanilla fulfilled delegate used by a live designation.
3. Require the operation to be incomplete and at least one fulfilled perimeter bit (`0x1F8C63F`).
4. Emit V-to-G edges only for fulfilled perimeter tiles that belong to the tower-reachable G flood.

The selected operation and G tile are edge metadata. Materialization replays the same prospective check against the immutable search snapshot, and placement gives the final generated V tile the corresponding mining or dumping proto without a leveling fallback. Synthetic graph fixtures that do not configure the prospective evaluator retain exact-contact handoffs solely as a test fallback.

### V2 G handoffs and G-plane clearance

V2 must not reuse V1's single-tile G semantics. Width-2 access exists for mega vehicles, so every G-side test in a V2 request must be clearance-aware and use the effective mega-vehicle pathing parameters:

* **Vehicle target.** For explicit clearance 2+, assume the intended vehicle class is the mega excavator class. Future `Auto` clearance should derive this from vanilla vehicle research/availability and assigned/global vehicle requirements, then select the corresponding vanilla `VehiclePathFindingParams`.
* **Vanilla pathability reuse.** Use the selected vehicle's `PathabilityQueryMask` with vanilla `IPathabilityProvider.IsPathable(centerTile, mask)` for G occupancy. The mask already encodes the vehicle's `MinSizeClearance`, steepness, height-clearance, and ocean rules; do not use the single-tile mask for clearance-2/mega G checks.
* **G nodes / G flood.** Precompute a clearance-2 G graph, not only individual pathable tiles. A G state is valid only when the corresponding 5-tile mega vehicle footprint can occupy or traverse that position according to vanilla mega-vehicle pathability. The tower-reachable G flood, G-to-G expansion, and final goal set must all use this clearance-2 graph.
* **V2 -> G handoff.** A V2 path may leave generated V-space only through a width-2 workable frontage on one side of the terminal 2x2 brush. The exposed side has two adjacent origin edges, one per lane. Each lane selects mining or dumping from its exposed edge height relative to current terrain, then runs the same prospective workability check as V1. The two resulting contacts must be consecutive and compatible with the same clearance-2 G component.
* **G -> V2 handoff.** Entering generated V2 from G is symmetric: the first generated brush must expose a width-2 attachable frontage to a clearance-2 G component.
* **Operation selection.** Use the V1 exposed-edge rule per lane: if that lane's frontage lies below current terrain, select mining; otherwise select dumping. Mixed mining/dumping terminal lanes are allowed if each lane independently passes workability and the two terminal profiles share corners correctly.
* **Fixed-provider handoff.** Existing accessways and planned providers are fixed profile bands. A V2 brush may attach only when two consecutive provider edges match the exposed brush side and the provider chain is reachable with clearance 2.

In shorthand:

```text
V2 handoff = two adjacent V1-style handoffs on the same brush side
             + compatible terminal profiles
             + consecutive G/provider contact
             + clearance-2 reachability beyond the seam
```

The 8-tile physical width of a two-origin accessway gives useful slack over the 5-tile mega vehicle requirement, but the seam itself must still prove a real width-2 frontage. Otherwise the route can pinch exactly where it joins ground.

## Validation timing

Reject every condition at the earliest layer that has enough information, while retaining later replay as defense in depth:

* **Expansion-time:** all snapshot-pure node and edge constraints: materializable integer-corner profiles, horizontal/vertical bounds, work-operation compatibility, ocean floor, buildings, durability, fixed-neighbour fights, V/V edge profiles, origin revisits, nonconsecutive corner contact, and prospective G/V handoffs. Search and materialization must call the same predecessor-sensitive generated-profile feasibility helper so directional durability pruning cannot disagree with replay.
* **Goal-time:** reconstruct and fully materialize every reached goal before accepting it. This replays continuity, profiles, handoffs, duplicate origins, shared corners, final goal membership, and generated-profile feasibility over the complete path. If replay rejects the goal, record its exact reason, expand that G node normally, and continue A*/Dijkstra toward another goal.
* **Placement-time only:** conditions that depend on mutable world state or the designation API: manager availability, a designation appearing after the immutable snapshot, `AddOrReplaceDesignation` failure, and failed post-placement provider reachability. These retain transactional rollback and legacy fallback.

End validation is not removed when a condition moves into expansion; it verifies that graph generation and materialization continue to agree. A snapshot-pure rejection discovered only during placement is an implementation defect and should be moved forward.

## The fight invariant

Edge *geometry* admissibility is **phase-independent**: digging, leveling, and dumping are all legal in every phase, so phase never decides *which* edges exist. What a phase **does** control is the *fill material*, through the tower dumping rules: the **Prepare** phase wants access to all filling materials (rock, slag, etc.), while the **Filling** phase bans them absolutely and admits only soil. That distinction is a property of the dump designation's material, not of the corridor geometry, so it leaves the routing graph unchanged. (This corrects an earlier assumption that dumping edges were phase-restricted; phase gating remains, but it gates dump *material* and dump-rule ownership, not edge admissibility. In principle Prepare and Filling could share one phase if filling were restricted to soil/ocean or known to be negligible, but ATD phases them tightly for robustness - see the [Farmland Preparation Sub-Process](../in-progress/farmland-preparation-subprocess.md).)

The cross-designation constraint the search must respect is instead the **fight precondition invariant**:

> **Every pair of designations that share one or more corners must be height-aligned on all shared corners.**

There is **no same-type exemption**: a pair with one or more misaligned shared corners causes a landslide and risks irreparable disruption *even when both designations are of the same type*. Alignment on every shared corner is therefore required unconditionally.

A corridor the search lays down must satisfy this against (a) the existing designations it abuts and (b) itself. This is a local, per-node feasibility check during expansion: a node whose required corner heights would leave any shared corner misaligned with a neighbouring designation is **inadmissible**. Because the check is over *shared corners*, it includes **diagonal** neighbours (which share a single corner), not just the axis-adjacent ones a terrain-changing edge can move along. This replaces "phase coupling" as the cross-designation constraint.

## Durability: don't route where future terrain work will reshape it

The fight invariant prevents an *immediate* landslide between adjacent designations. A second, **temporal** hazard is just as damaging: an accessway built too close to future terrain work can collapse when deeper mining removes its support, or be buried when higher dumping or leveling work builds outward slopes. In game terms this can be effectively irreparable while the source designations remain active, forcing a whole new accessway to be routed.

The ideal model would approximate the future landslide shape from below and remove only nodes that would actually lose support. That is too expensive and too uncertain for the first implementation: the angle of repose varies by material (roughly 37-77 degrees), and the game also has some randomness. Use a conservative geometric envelope instead.

Treat every corner of every active or newly planned mining, dumping, or leveling designation as the waist of a symmetric hourglass exclusion volume. Occupied tiles of planned, construction, and completed buildings contribute equivalent sources at the building's fixed foundation height. For a fixed source at `(x, y, d)` and a candidate accessway node/corner `(x', y', d')`, use the absolute vertical separation:

```text
delta = abs(d' - d)
run = configured horizontal run per vertical level
blocked iff delta > 0 and abs(x' - x) < delta * run and abs(y' - y) < delta * run
```

This is a deliberately conservative approximation of both lost support below and future material spread above. A designation corner casts the same finite square envelope upward and downward. The public `accessLandslideRunPerHeight` parameter defaults to `1`, giving `max(abs(dx), abs(dy)) < delta`: a Chebyshev-distance approximation of a 45-degree hourglass. Values above `1` widen the exclusion volume and are more conservative; values below `1` narrow it. Clamp the public range to `0.05..2`; at the upper bound, a drivable G step can enlarge an exclusion radius no faster than it moves away from a source. Using `or` between the axis tests would create infinite exclusion strips and incorrectly block distant points that merely share X or Y with the designation corner. Keep the strict `<` boundary: a tiny amount of slide can often be tolerated, and width-2 mega access has extra physical slack (the designated two-origin band is 8 tiles wide while mega pathing needs 5 tiles). Switch to `<=` only if in-save testing shows boundary collapses are common. The rule is applied against concrete designation **corners**, not only origin centers, because corner failure is the damaging case and because V/V' shapes can have different corner heights even when their center height is the same. If multiple active designations disagree at a shared corner, retain every distinct target height as an exclusion source rather than collapsing them to one extreme.

For V1, expansion implements this as a local feasibility check after converting `(origin, h, mode)` to its four candidate corner heights. Strictly interior corners of a connected, compatible designation region are omitted: the region's drivable target profiles bound their height change, so those interior hourglasses cannot escape the perimeter envelope. Disagreeing shared-corner heights and all building-foundation samples are retained. The resulting source index filters `G`, because currently pathable ground may become unsafe after pending work. The first generated node and every fixed/G-to-generated handoff test the full source index. After a generated predecessor has been validated, a successor tests only sources ahead in its movement axis; with `run <= 2`, a drivable transition cannot enlarge the envelope faster than it moves away from sources behind. Traversal on an already designated origin uses that designation's fixed profile and is not rejected by its own hourglass. Building footprints remain hard obstacles and are never traversable graph nodes; sharing the landslide index does not change that distinction. This generalizes the existing *Ramp safety margin* roadmap item into the graph itself as a hard inadmissibility rule, rather than a soft cost penalty.

## Clearance as a lattice-parity rule

`corridorWidth` (from the `accessWayClearance` setting; see the framework) maps onto the search as a **parity of the lattice**, because an N-wide band centers differently for odd and even N:

| Clearance | Band centering | Lattice node position | Node cost footprint |
|---|---|---|---|
| **1** (odd) | centered on one origin | origin **centers** | the single covered origin |
| **2** (even) | centered on the seam between two origins, one lane each side | origin **vertices** | the two covered origins (one per side) |

So the horizontal node set shifts by half an origin with width parity: origin-centered for odd clearance, vertex-centered for even clearance. The V1 graph above covers only the clearance-1 row. V2 uses the clearance-2 row as a 2x2 brush lattice:

* **Cost spans the whole brush.** The node cost should eventually be the work to bring all four covered origins to the brush's target profiles. For the first V2 implementation, center-point `dh^2` is acceptable as the same cheap approximation V1 uses, scaled through the public `workDistanceScale` parameter. Add a refinement backlog item to estimate work from the covered origins' outer-corner deltas, for example `sum(dh_c^2)` over the outer footprint corners; that refinement would also improve V1 hillside routing.
* **The brush profile is a short V1-mode string.** V1 can describe one origin by `(h, mode)`. V2 describes a 2x2 brush by `(h, axis, profile)`, where the profile string has one V1-mode token per lane across the corridor width. This distinguishes uniform ramp brushes (`X+X+`) from transition brushes (`FX+`), which the single lifted-mode notation cannot represent.
* **Perpendicular drivability is explicit.** The lateral direction must remain drivable across all lanes in the brush. The first implementation should allow only coherent profiles whose tokens produce a valid side-by-side band; arbitrary mixtures are deferred.
* **Turns require a flat brush.** A V2 route may change slope axis only through `FF`. The flat 2x2 brush is the required 8x8-tile landing that lets the two-lane corridor turn without narrowing below clearance 2.
* **Edges compare shared strips, not just shared edges.** Moving a V2 brush by one origin creates a 1x2-origin overlap with the previous brush. The old and new profiles over that overlap must agree exactly. The two newly exposed origins are then checked for construction slope, fight invariants, durability, bounds, and workability.

Auto clearance derives the width from vanilla vehicle research/availability and assigned/global vehicles exactly as the framework's *Corridor width* describes. If mega excavators are relevant or available, Auto maps to clearance 2 and the V2 search uses the matching vanilla mega-vehicle pathing parameters; otherwise it remains at the narrower available requirement. The search just consumes the resulting integer width, vehicle pathing parameters, and lattice parity.

## Width handling strategy

Searching the full perpendicular band profile as state is exponential and is **not** the plan. In increasing order of cost/correctness:

1. **V2 brush search** (preferred for mega access). Search `(2x2 brush vertex, h, axis, profile)` directly, with each profile encoded as a short cross-section string of V1 modes (`FF`, `FX+`, `X+X+`, `X+X-`). This is more expensive than V1, but still bounded and avoids the correctness traps of thickening a centerline after the fact.
2. **Centerline + thicken + revalidate** (fallback/prototype only). Search a V1 `(origin, h, mode)` path, expand it to the `corridorWidth` band, re-check perpendicular slope/clearance and the fight invariant, and lateral-retry on failure. Cheap, but it can pick a centerline whose thickened corridor cannot turn or whose adjacent lane is not constructible.
3. **Full band-state search.** State = entire perpendicular profile, allowing lane-asymmetric leans and richer multi-lane geometry. This is the most general model but grows quickly; avoid until the profile-string brush model proves insufficient.

## Trunk-and-branch via reusable pathing

The MVP search can run per cluster from `S` toward any tower-reachable end `E`. Trunk reuse still emerges because existing accessways and existing designations are pathable through `G`: they have zero work cost and only pay traversal length, so a later cluster can cheaply attach to a corridor planned or built by an earlier cluster. The framework's closest-first trunk-and-branch behaviour remains compatible with this search direction; a later optimization can reintroduce a cached cost-to-ground field once the V1 single-cluster graph is proven.

## Performance and bounding

* Height augmentation multiplies node count by the number of quantized levels, so cap the search. The MVP horizontal bound is the tower area in `x/y`. The MVP vertical bound is `[lowestTraversable - 1, highestTraversable + 1]`, where `lowestTraversable` is the lowest active designation floor or ground height in the bound, and `highestTraversable` is the highest active designation floor or pathable ground height in the bound.
* V2 uses the same horizontal tower-area bound for generated designations, but the **entire 2x2 brush footprint** must fit inside the bounded designation area. Do not expand outside the managed area just to fit mega access; if no brush path fits, report a width-specific blocked reason such as `NoWidth2BrushPath` / `Width2BrushOutOfBounds` and let the normal legacy fallback compete unless suppressed.
* Precompute G nodes from the vanilla pathing flood before running the graph search. Seed that flood from vanilla-pathable terrain adjacent to the actual tower, even when the tower lies outside its managed area, then enter and traverse the managed area only through eligible G nodes. `E` is any G node reached by that flood; the nearest in-area tile to the tower is not sufficient proof of reachability.
* Start with Dijkstra (`heuristic = 0`) for the MVP. It is easier to validate because priority is exactly accumulated path cost.
* Keep A* available behind a public setting once the graph is validated. Use `min_E max(Manhattan(node, E), 2 * abs(h(node) - h(E)))`. The horizontal term is unavoidable literal tile travel. The vertical term is also a lower bound because even G traversal changes height by at most `0.5` per tile; V traversal is no steeper. Taking the maximum avoids double-counting travel that satisfies both bounds, and minimizing the paired bound over actual goals avoids combining horizontal distance to one goal with height distance to another. The heuristic ignores terrain work and remains admissible when existing designations have zero work cost.
* For future fixed-profile/provider goals, a cluster center with a representative height is useful for ordering and diagnostics, but distance to a center can overestimate distance to the nearest reachable profile edge. Keep center distance as a tie-break unless the heuristic is adjusted to a true lower bound, for example by seeding a multi-source lower-bound map from actual goal origins/profiles. A construction-only lower bound may use `max(Manhattan, 4 * abs(dh))`, but the mixed G/fixed-profile graph must use the weaker travel-safe height term (`2 * abs(dh)`) or prove the steeper reuse path is unavailable.
* Distance to tower center `T` is a good tie-break, but it is not automatically an admissible A* heuristic because the search stops at `E`, not at `T`. It can overestimate when a valid `E` is nearer than `T`. If a T-shaped heuristic is desired, use an adjusted lower bound such as `max(0, distance(node, T) - maxDistance(E, T))`, or keep raw distance-to-`T` only as a secondary ordering key after `f`.
* Do not include estimated terrain work in the heuristic: an existing designation may bridge the same height gap at zero work cost. The travel-only height term above is independent of `workDistanceScale` and remains admissible under reuse.
* The search re-runs each pass like the rest of the framework; keeping it bounded is what makes per-pass re-planning affordable.
* Quantize height to the designation grid's own vertical resolution so the lattice matches what designations can actually express.

## Diagnosability

The framework deliberately avoids an opaque numeric score (`decidedBy=<criterion>` instead). A single path cost regresses that. Mitigation: log the **cost breakdown** (center-height work vs traversal length, plus reused `G` segments) and the chosen path, so "why did it build this ramp" stays explainable. The `decidedBy` concept becomes "which cost term dominated", and the path geometry is reported alongside it. Equal-cost paths tie-break toward the shorter path. Preserving this explainability is a hard requirement, not a nicety - it is the reason the framework exists.

Failed searches should report the blocking class: no valid `S`, no tower-reachable `E`, horizontal bound exhausted, vertical bound exhausted, construction slope blocked, fight invariant blocked, durability envelope blocked, no G handoff, or final full-path validation failed. These map back onto existing `NoCandidate` / `MouthUnreachable`-style diagnostics.

Add an opt-in **pathfinding debug surface** for development builds and advanced troubleshooting:

* **Visualization layer toggle.** A keyboard shortcut opens a mod debug panel; the panel can enable/disable an in-world overlay for the last accessway search.
* **Cursor-coordinate toggle.** Reuse ATD's existing bottom-left cursor-position overlay (`ShowCursorOverlay`, currently controlled by `atd_cursor_overlay`) as a panel toggle alongside the pathfinding layers.
* **Axis compass toggle.** Show a compact screen-space rose for world `+X` and `+Y`. Derive arrow direction and length from the active camera projection on every draw so camera rotation and tilt are visible; label the axes as `X` and `Y` to avoid compass-direction ambiguity.
* **Overlay layers.** Show `S`, candidate `E` nodes, the chosen path, generated V segments, reused G segments, V/G handoff seams, durability-blocked zones, fight-invariant failures, construction-slope failures, and the final validation failure if any.
* **Cost heat / frontier view.** Optionally visualize accumulated path cost or Dijkstra frontier order inside the bounded search area. This is primarily for tuning `workDistanceScale` and bounds.
* **Decision dump buttons.** The panel can dump cached decision trees / rejection summaries to the log: selected `S`, candidate `E` set size, bounds, visited node count, best rejected blockers by class, final path cost breakdown, and tie-break decisions.
* **Last-search cache.** Keep only the most recent search details by default to avoid save bloat and runtime churn; allow an explicit "dump now" action before the next pass overwrites it.

## A/B rollout

Build behind the same `AccessCandidate` contract the framework already defines, gated by `Turning ramps (experimental)`, and compare against the current generator on real saves before promoting:

1. **V1 height-augmented Dijkstra** from cluster start `S` to a tower-reachable pathing end `E`, restricted to the **V** (flat + slope) designation set, `accessWayClearance = 1`, the construction-slope bound, and the durability envelope. Keep A* selectable through a public setting, initially off. Validate it reproduces today's straight ramps **and** discovers a switchback the current code cannot.
2. **Add V2 brush search** for `accessWayClearance = 2`, still within the V designation set, using a 2x2 origin brush, cross-section profile strings, flat-brush turns, and footprint work estimates. V2 competes with the legacy straight generator under the same selection/fallback logic as V1; if **Suppress legacy ramps** is enabled, V2 failures are exposed directly for testing.
3. **Reuse-aware trunk behaviour** through `G` segments and existing designations; defer any multi-source cost-to-ground field until the per-cluster V1 graph is proven.
4. **Fight-invariant feasibility, durability-envelope, and debug visualization diagnostics.** Fight-invariant and durability feasibility must be enforced from step 1 (they prevent irreparable landslides); this step adds the overlay, decision dumps, and cost-breakdown tooling needed to tune and trust the search.
5. **Compare** against the straight-corridor generator on representative saves; promote when it wins on cost, robustness, and explainability.

## Future expansion: generic path A to B

The V/G graph should eventually support a generic request such as "find the cheapest drivable or constructible path from A to B." Mine-tower access is then one adapter: the origin cluster supplies A and tower-reachable ground supplies the goal set B. The graph, transition validation, cost model, search, and materialization remain shared.

Introduce an `AccessPathRequest`-style boundary rather than a second pathfinder. A request should provide:

* one or more start endpoints and one or more goal endpoints;
* endpoint adapters for G tiles, fixed V profiles, areas, or generated candidate sets;
* explicit bounds or a bounded search-radius policy instead of implicit tower-area bounds;
* allowed construction modes, clearance/search-space version, and cost settings;
* intent: inspect an existing drivable route, plan a constructible route, or materialize/place that route.

Generic routing also requires fully symmetric G/V entry and exit. Production V1 currently exercises cluster-side V toward tower-side G most heavily; A-to-B must support either endpoint being G or V and may cross a G/V seam at either end. Tower-specific clustering, candidate comparison, notifications, fallback, and post-placement provider flooding stay outside the core request.

This expansion comes after traversal/goal validation is unified and covered by deterministic fixtures. A generic API must not expose paths that can still fail snapshot-pure materialization checks after search; only mutable-world placement failures may remain deferred.

### Rooted-network bridge (initial implementation)

The first extraction is deliberately narrower than fully symmetric A-to-B routing. An `AccessPathRequest` carries a frozen snapshot, typed start and goal endpoints, intent, bounds, and required corridor width. The mine-tower adapter supplies a set of fixed-profile work origins as the start and tower-reachable G tiles as the goal. This preserves the existing rooted-network problem while removing tower-specific assumptions from the search entry point.

`Create Designations` also treats every active, incomplete mining, dumping, or leveling designation inside the tower area as a fixed work endpoint. This supports access-only repair: with experimental turning ramps enabled and width 1, invoking the command can connect existing player-authored terrain work to the tower even when the ore scan produces no new mining plan. Its target profile is copied into the snapshot exactly. An existing designation may also remain an access provider when its target geometry already forms a tower-rooted chain; endpoint classification must not hide that valid provider. Accessway materialization owns only newly generated accessway origins and must not replace or remove the endpoint designation.

When generated mining and existing terrain work coexist, they participate in the same rooted reachability analysis and can form multiple clusters. Clusters containing an existing terrain-work endpoint use only the generic V1 request; they do not enter the legacy straight-ramp generator, whose mining-specific placement assumptions could rewrite or misinterpret the marker. Generated-mining-only clusters retain legacy comparison/fallback during the experimental rollout.

Initial constraints:

* The rooted request supports `FixedProfiles -> GroundTiles`, construction intent, and required width `1` only. Other endpoint combinations and widths fail explicitly rather than silently degrading.
* Existing designations that overlap a newly computed mining plan remain subject to the current mining regeneration behavior. Distinguishing player-authored markers from stale ATD output requires persisted provenance or a mining-parameter fingerprint; add that before promising selective mining regeneration.
* A future symmetric A-to-B caller may use a tower placed at either endpoint and an arbitrary terrain designation at the other, but it should build on the same request boundary rather than infer special marker semantics inside the graph.

## Open questions

* Exact vertical quantization to use for height levels.
* The initial public setting/default range for `workDistanceScale`.
* Whether the initial coherent V2 profile set (`FF`, flat/ramp transitions, uniform same-sign ramps, and opposed same-axis pairs) is sufficient in practice.
* The full **V' corner-to-slope / corner-to-corner** transition table, enumerated from the game's corner designation definitions.
* Whether centerline + thicken is useful as a fallback/debug comparator once direct V2 brush search exists. This is not important for correctness and can be skipped unless needed for diagnostics.
* How aggressively to bound the search radius before declaring `Blocked`, and how that maps onto the existing `NoCandidate` / `MouthUnreachable` reasons.
* Whether a later cost-to-ground field should be cached across passes and incrementally invalidated, or whether per-cluster `S -> E` Dijkstra/A* is fast enough.
* Work is estimated as `dh^2`, where `dh` is the absolute difference between the candidate and current terrain center heights. The public `workDistanceScale` parameter handles calibration, so the formula keeps no fixed `0.5` coefficient. It is still a center-point approximation; V2 may start with it for speed, but true optimal hillside routing needs a corner/footprint estimate such as `sum(dh_c^2)` over outer corners.
* Can we merge the tower-rooted-cluster and fixed-network-cluster into one by merging the goals and mapping out a common heuristic (manhattan+2*dh to closest goal tile).
* Continue investigating route quality and correctness for paths involving multiple V/G handoffs. Known symptoms include missed early G handoffs, unnecessary V stretches over pathable terrain, and handoff proto selection that needs to remain stable across repeated G/V transitions.
* Debris handling is specified by [Accessway Pathfinding Debris Amendment](accessway-pathfinding-debris.md); remaining work is implementation and fixtures for cleanup-cost routing and cleanup-designation materialization.

## Relationship to the access framework

This note changes only the **generation** step. It plugs into the framework at:

* **Provision Pipeline step 8** - the search *is* the missing-provider generator; the MVP runs per cluster from `S` to a pathable tower-side end `E`.
* **Accessway Routing** - this is the alternative routing engine; *What a routed candidate is* and *Two routed families* still describe the output (corridors; ramp/bridge), but they are now produced by search rather than straight enumeration.
* **Candidate Selection** - largely subsumed: the Valid filter becomes edge/fight/durability admissibility, and center-height work / traversal length become the MVP cost terms. Selection remains as the tie-break vocabulary and the diagnostic surface.
* **Accessway Routing -> Current limitations** - this is the planned removal of the no-turn / no-multi-bend / no-cheaper-geometry limits.

Everything else in the framework (clustering, the grounded-reachability fixpoint flood, completion, phase gating, removability) is untouched.
