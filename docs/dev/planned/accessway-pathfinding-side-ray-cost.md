# Accessway Pathfinding Side-Ray Landscaping Cost Amendment

Status: planned amendment to [Accessway Pathfinding](accessway-pathfinding.md). This note refines the landscaping-cost part of the path cost for mountain and cliff approaches where a center-height or corner-height delta is too weak to rank good routes.

## Motivation

The MVP pathfinding cost intentionally starts with a simple center-height delta, but that value can badly understate accessway work on mountainsides. When a corridor runs along or across a steep hillside, the local vertical delta at the center or corners can be small even though the designation must cut a long bench into the uphill side, dump a long shoulder on the downhill side, or both.

For these cases the meaningful cost is the lateral distance from the planned accessway edge to the point where a legal material slope reaches existing ground. This amendment models that distance with a small bounded ray march instead of attempting full landslide or material-volume simulation.

## Design principle

Use an additive local cost that remains cheap enough for A*/Dijkstra:

```text
edgeCost = traversalLength
         + landscapingCostDistanceScale * landscapingCost
```

`landscapingCost` is no longer only a vertical height delta. It is a lightweight estimate of accessway construction effort:

```text
landscapingCost = designationOverhead
                + directWorkCost
                + lateralSideRayCost
```

The exact weights are tuning parameters. The important behavioral change is that sidehill routes pay for lateral wedge depth, not just local vertical delta.

## Exit-corner sampling

For an ordered corridor path, charge each newly generated cell when the search
enters it. The predecessor determines travel direction `d`; score the entered
cell's opposite, outgoing edge:

* The shared edge through which the path enters the cell has zero landscaping
  cost. The predecessor already accounted for its own working edge, or it is a
  no-work handoff edge.
* A `G -> V1` handoff does pay side-ray cost for `V1`: its outgoing edge is a
  working edge, and direction is known from the ground predecessor and matched
  handoff geometry.
* A terminal `V2 -> G` handoff adds no further side-ray cost. `V2` was already
  scored when entered, while its outgoing ground-connecting edge is a no-work
  edge.

For a generated cell entered in direction `d`, choose the two corners on the
outgoing edge and shoot one lateral ray from each corner:

| Move direction | Exit edge | Exit corners | Lateral rays |
|---|---|---|---|
| `X+` | `X+` edge | `NE`, `SE` | `Y-` from `NE`, `Y+` from `SE` |
| `X-` | `X-` edge | `NW`, `SW` | `Y-` from `NW`, `Y+` from `SW` |
| `Y+` | `Y+` edge | `SW`, `SE` | `X-` from `SW`, `X+` from `SE` |
| `Y-` | `Y-` edge | `NW`, `NE` | `X-` from `NW`, `X+` from `NE` |

The side-ray cost is therefore direction-aware: the same origin and target profile can have a different landscaping cost depending on whether the corridor runs along the contour or across the hillside.

## Lateral-only ray march

Only lateral rays are sampled. Longitudinal start/end work is represented by handoff or intent-fixed profiles and has zero landscaping cost in this scorer; generated interior segments connect through shared edges. The missing signal is side exposure, especially on steep terrain.

Each lateral ray starts at a planned exit corner height and follows an idealized material slope outward until that slope intersects terrain or reaches a fixed maximum distance. This estimates the cross-sectional wedge that must be excavated or filled to make the accessway's side stable/workable.

## Accelerating samples

Use a short fixed step table for **cost integration**, for example:

```text
1, 2, 3, 5, 8, 13, 16
```

This is Fibonacci-like at short range, then deliberately caps at `16` terrain tiles instead of continuing to `21`. The cap is tied to the terrain/designation scale: `16` tiles is four 4x4 designation widths, which is already a very large side wedge for an accessway scorer. Continuing to `21` may be useful as an experiment on extreme cliffs, but it should be a tuning variant rather than the first-pass default because it increases each unresolved ray's search radius by another full designation width plus one tile and can make far-away terrain dominate candidate ranking.

Feasibility is nevertheless checked densely at every tile from 1 through the
same maximum range.  A ray must terminate at the first actual terrain
intersection, and any reached ocean, building, opposing designation, map edge,
or missing snapshot tile is handled immediately.  In particular, an ocean at a
non-Fibonacci distance is still fatal for a cut ray.  Only the non-negative
cost integration remains accelerated.

For each cost sample distance:

```text
stepLength = distance - previousDistance
ray_h      = corner_h +/- distance * materialSlope
 ground_h  = terrainHeight(corner_xy + lateralDirection * distance)
```

The sign of the ray slope depends on whether the side is fill or cut. Determine the side operation from the planned corner height versus the current terrain height at or near the corner:

* **Fill side:** planned corner is above terrain; the side ray slopes downward/outward until it reaches ground.
* **Cut side:** planned corner is below terrain; the side ray slopes upward/outward until it reaches ground/open terrain.

Filter those corner operations through the designation proto that will perform
the work:

* a mining proto scores cut corners and ignores fill corners;
* a dumping proto scores fill corners and ignores cut corners;
* a leveling proto may score both cut and fill corners independently.

## Material run-slope selection

`materialSlope` should be chosen from the material that will actually form the side wedge, and fill/cut must use different material forms:

* **Dumping/fill:** inspect the active dumping rules for the tower/accessway, look up each allowed material's terrain properties, and use the material with the lowest stable slope/angle as the ray slope. This is the most conservative option because trucks may dump the runniest allowed material. Use the **disturbed** material properties for dumping; dumped material is loose/disturbed and is significantly more runny than the normal in-ground material.
* **Mining/cut:** sample the terrain material at the exit corner's `(x, y)` and the planned corner elevation used by the ray. This point is guaranteed to be underground for a cut side. Use the material's **normal** in-ground properties, not its disturbed/dumped variant.

If a material lookup fails, fall back to the conservative runniest known terrain-material slope and include diagnostics so the scorer does not silently become optimistic.

An explicitly empty tower dumping-rule set is not a lookup failure. It means no
fill material is available: reject every generated candidate with a fill corner,
including leveling cells, and continue searching for a cut-only or no-work
route. Use the conservative fallback only when the rule or material lookup
itself fails.

## Discrete cost integration

Use the actual sampled gap at each step. Do not average away the terrain samples for the first version.

For fill:

```text
dh = ray_h - ground_h
if dh <= 0: stop; the fill slope has reached ground
cost += stepLength * dh
```

For cut:

```text
dh = ground_h - ray_h
if dh <= 0: stop; the cut slope has reached open/acceptable ground
cost += stepLength * dh
```

This is a simple rectangle-rule integration of the positive work gap along the lateral ray. It is intentionally a ranking heuristic, not a physical volume solver. If accelerating samples make the score too jumpy in testing, the implementation may switch to trapezoid integration:

```text
cost += stepLength * (previousDh + dh) * 0.5
```

but the initial implementation should prefer the simpler `stepLength * dh` form.

## Caps and unresolved rays

Every ray must have a maximum distance and maximum contribution. If no intersection is found within the step table, add a large capped unresolved-ray penalty rather than infinity. This keeps all candidates comparable while strongly discouraging routes that cut across cliffs or require unbounded shoulders.

If a lateral ray reaches the edge of the map before it intersects terrain, handle it by operation type. For a **fill** side, the map edge is fatal: outside-map space cannot support an infinite dumping shoulder, so the candidate edge should be rejected or assigned an effectively infinite cost rather than the finite unresolved-ray penalty. For a **cut/mining** side, the map edge counts as success: the cut has reached open boundary space, so stop the ray and keep only the integrated in-bounds cost accumulated so far. Do not sample outside the map. A generated designation whose own footprint is outside the map remains invalid; this rule only covers side-ray scoring for otherwise valid in-bounds segments near the map edge.

Ocean handling is also operation-specific. For **cut/mining** rays, terminating on an ocean tile below level `+01` is fatal because water would flood the accessway; reject the candidate edge or assign effectively infinite cost. For **dumping/fill** rays, ocean is equivalent to air under CoI mechanics: water does not support or reduce the dumping run, so continue evaluating the fill ray exactly as if the ocean tile were empty space until the ray reaches actual ground, hits the map edge, or exhausts its maximum distance.

Recommended first-pass controls:

```text
maxRayDistance      = 16 terrain tiles by default; test 21 only if cliffs are under-penalized
maxRayCost          = tuned finite cap
unresolvedRayPenalty = tuned finite penalty added at max distance
```

## Material-aware durability and obstruction integration

Generated `V` retains a waist-preserving durability envelope around existing designation providers so routes can enter, leave, and pass through compatible fixed profiles. The old envelope's global run is replaced per designation corner with `1 / (materialSlope * 0.85)`, using the depth-selected mining material or snapshot dumping material. This keeps rock envelopes narrow, loose-material envelopes wide, and matching-height waists open. Building durability corners retain the configured fallback run. Ordinary `G` capture and cleanup-ground admission continue to use projected blocked tiles plus their existing durability protection.

Each side-ray sample also consults immutable obstruction masks. Building occupied tiles are expanded by two terrain tiles in every direction, producing at least a 5-by-5 obstacle even for a single occupied tile; this guarantees that the sparse distance sequence cannot step over a building that intersects the disturbed ray span. Terrain-designation footprints and projected disturbance are classified as cut or fill. A candidate cut ray may mix with existing/projected cut work but is blocked by fill work; a fill ray may mix with fill work but is blocked by cut work. Leveling footprints remain conservative hard blockers, while their projected rays are classified by the actual local operation. A blocker is fatal only while the sampled material slope still has positive cut/fill work at that distance; blockers beyond the resolved slope are irrelevant. Individual ray results, including obstruction failures, are cached by corner, planned half-level height, lateral direction, and work operation.

Candidate side-ray feasibility uses 85% of the selected material slope, extending
its predicted run by roughly 18% before it tests for termination or fatal
ocean/obstruction.  After the first terrain intersection it tests one further
tile as a safety buffer.  This is deliberately conservative for loose/runny
materials such as sand; a nearby ocean cannot become safe merely because the
nominal ray resolved one tile before it.

Snapshot construction projects the future terrain disturbance of existing mining, dumping, and leveling designations before searching. For every exposed designation edge, its two corner rays are traced with the same operation-aware, depth-selected material selection used by candidate `V` costing. Internal edges between adjacent snapshot designations are skipped. Unlike candidate cost rays, existing-designation projection samples every terrain tile rather than the sparse Fibonacci distances. Its effective slope is 85% of the selected material slope, deliberately extending the predicted horizontal run by roughly 18%, and its independent projection limit is 48 tiles. It blocks through the first tile where the slope resolves and one additional tile beyond that termination as a safety margin; an unresolved projection blocks through tile 49. Tiles in the resulting projected disturbance spans are removed from speculative `G` and added to the immutable designation obstruction mask seen by later candidate rays. This prevents a candidate from resolving against present-day ground that an existing designation will later destroy. The legacy durability envelope remains temporarily active for `G` as a validation guard; after projected-designation blocking is regression-tested, `G` can rely on the blocked snapshot tiles instead of the fixed-run envelope.

Convex outside corners receive an additional filled-quadrant projection rather than only the two orthogonal boundary rays. Every tile in the outward quadrant through the 48-tile projection limit is evaluated against the corner target height and safety-adjusted material slope using conservative Chebyshev distance. Predicted disturbed tiles are expanded by one tile in every direction. This intentionally over-approximates the rounded/fanned collapse around an outside corner and prevents routes from slipping diagonally between the two edge rays. Concave or designation-internal corners do not emit this quadrant.

The rules below describe the original staged rollout and the constraints retained by the implemented replacement.

Do not replace durability or external-obstruction feasibility with side rays in
the first implementation. The existing durability/hourglass and hard-obstacle
checks remain authoritative. One path-local exception is required for
correctness: later `G` traversal may not reuse terrain that this same candidate
path will disturb. Generated V footprints and the positive-work spans of their
side rays are therefore retained in generated-path history and exclude later
ground nodes. The immediate V-to-G handoff may overlap the current V footprint,
but not an earlier V footprint or any recorded ray wedge.

Once the cost scorer is stable, the side-ray march is a candidate for a more accurate generated-edge landslide/support check. At that later stage, reuse the same ray samples for scoring and feasibility where possible: one bounded march can return the integrated landscaping cost plus any hard blocker it encountered.

Deferred feasibility rules to evaluate later:

* **Buildings:** if the ray/wedge crosses an occupied or planned building footprint, reject the candidate edge. Buildings remain hard obstacles, not soft cost.
* **Other designations:** if the ray/wedge crosses an active or fixed designation that is not the predecessor/successor profile already accounted for by the accessway, reject unless that designation's fixed target profile is explicitly compatible with the ray side. Do not assume future landslides may consume or overwrite unrelated work.
* **Current candidate designations:** compatible generated V neighbours in the same candidate path are allowed; shared edges are already accounted for by the entry-time, outgoing-edge scoring rule. Ground traversal through any earlier generated footprint or ray wedge is not allowed.

The broad durability/hourglass index can remain as a cheap prefilter for sources not represented by the current side ray, for G-node safety, and for diagnostics. Only after the side-ray cost implementation has been validated should generated-edge durability consider preferring the side-ray result, because it follows the actual direction, operation, and material run slope of the candidate edge.

## Relationship to direct landscaping cost

The side-ray cost does not need to replace all direct landscaping cost. Keep a small direct term and a per-designation overhead so flat terrain and short transitions remain well behaved:

```text
landscapingCost = overhead
                + directWorkWeight * directVerticalWork
                + sideRayWeight * (leftExitRayCost + rightExitRayCost)
```

`directVerticalWork` may initially reuse the MVP center-height approximation. Later, it can be replaced with a small sample-grid estimate if necessary.

## Turns and special cases

The first implementation should score a generated cell's exit edge when the cell
is entered, using the predecessor-derived direction. If the path later turns,
that earlier score is not revised to model the newly exposed outside corner.
This is an accepted first-pass inaccuracy: V1 turns require flat cells and
usually add enough cells and total cost that excessive turning is already
suppressed. Later refinements may add:

* an outside-corner ray for sharp turns and switchbacks,
* unique-edge accounting for branched or reused fixed profiles,
* separate cut and fill weights.

## Expected behavior

This amendment should make the pathfinder prefer routes that are longer but materially easier, such as contour-following paths and switchbacks, over short routes that slice across a mountainside. On hills, the side-ray scorer should also bias the route shape toward more filling near the base of the hill and more mining near the top: low routes that cut deeply into the uphill face should lose to modest fills/shoulders, while high routes that would require large exposed dumping shoulders should lose to controlled excavation into the hillside. The cost remains local, non-negative, bounded, and additive, preserving the shortest-path requirements while giving the search a much better signal for hillside work.

## Review resolutions

The current search charges center-height work and fixed overhead when it enters a
new generated `V` node. Side-ray work uses that same accounting point: the
predecessor identifies the entered cell's forward direction and therefore its
outgoing edge. Implement the new terms with these ownership rules:

* Keep `directVerticalWork` and generated-`V` fixed overhead charged once when a
  new generated node is entered. A generated node does not become free merely
  because its outgoing edge is a terminal handoff.
* Charge side-ray work when entering any newly generated `V` from a predecessor
  whose handoff geometry establishes direction. `G`-to-`V` and
  generated-to-`V` transitions can therefore carry side-ray cost.
* Therefore a path `G -> V1 -> V2 -> G` scores `V1` on `G -> V1`, scores `V2`
  on `V1 -> V2`, and adds no new side-ray cost on `V2 -> G`.
* Apply the material operation only where the selected proto can perform it:
  mining counts cut corners, dumping counts fill corners, and leveling counts
  both.
* A corner whose planned and existing heights are equal within the scorer's
  epsilon has no lateral side operation and contributes zero ray cost.

The scorer must use immutable snapshot data. Search bounds, snapshot capture
bounds, and physical map bounds are different concepts: an absent snapshot
sample must never be treated as the map edge. Capture a ray halo of at least
`maxRayDistance` around every searchable generated origin, clipped to the real
terrain bounds, and represent sample results explicitly as terrain, ocean,
physical map edge, or missing snapshot data. Missing snapshot data is a
diagnostic failure, not a successful cut boundary.

Do not derive ray gaps from the existing half-level `groundHeight2` pathing
cache. Preserve terrain height with enough precision for cost integration,
while continuing to use `height2` for graph/profile compatibility. Likewise,
capture terrain-layer material data needed to resolve the normal in-ground
material at a cut corner's planned elevation; selecting only the current top
material is wrong for deep cuts.

Resolve the allowed dumping materials once while building the snapshot; their
conservative slope is location-independent for that search.
`TerrainMaterialProto.GetApproxSlopeSteepness()` already resolves through
`DisruptedMaterialProto`, so it is suitable for this snapshot-wide dumping slope.
Mining must deliberately use the normal collapse properties of the in-ground
material intersected at the planned corner elevation instead of that helper,
because the helper switches to the disrupted form. Put these conversions behind
one narrow material-slope resolver and cover them with fixtures.

## Implementation plan

### 1. Extract the cost model and preserve baseline behavior

Status: implemented.

* Add an `AccessLandscapingCost` value/result type with separate direct,
  left-ray, right-ray, unresolved-penalty, and fatal-reason fields.
* Move the current `16 * abs(center delta)` calculation and generated fixed
  overhead behind one transition-cost helper in `AccessPathSearch`.
* Make both relaxation and result-cost breakdown use the same helper, removing
  the current duplicated reconstruction logic.
* Add baseline fixtures proving A* and Dijkstra still return the same route,
  total cost, and cost breakdown before side rays are enabled.

Acceptance:

* Side-ray weight zero reproduces the current route and cost exactly.
* Summed cost-breakdown fields equal the search result's total cost within the
  existing float epsilon.

### 2. Extend the immutable search snapshot

Status: implemented.

* Capture precise terrain heights for the searchable area plus a 16-tile ray
  halo, clipped to physical terrain bounds.
* Capture ocean state separately from terrain support state; ocean remains
  unsupported for fill rays even though a terrain height can be queried there.
* Capture per-column terrain layers in a compact form sufficient to select the
  normal material at a planned cut elevation.
* Resolve the tower's allowed dumpable products using the existing
  `DumpableProducts` configuration access, map them to terrain materials, and
  store the most conservative disrupted dumping slope.
* Store the conservative fallback slopes and material-resolution diagnostics in
  the snapshot. Do not retain live manager/proto callbacks in the search loop.

Acceptance:

* Synthetic snapshot tests distinguish physical map edge, ocean, and uncaptured
  data.
* Deep-cut fixtures select the material intersected at planned elevation rather
  than the surface layer.
* Mixed dumping rules select the lowest stable disrupted-material slope.

### 3. Implement the bounded ray integrator

Status: implemented.

* Add a pure scorer using distances `1, 2, 3, 5, 8, 13, 16`.
* Determine fill, cut, or no-op independently at each exit corner.
* Integrate `stepLength * positiveGap`, stopping at the first supported terrain
  intersection.
* Apply the operation-specific ocean and physical-map-edge rules from this
  document.
* Cap each ray contribution and add the finite unresolved penalty when the
  16-tile sample is still open. Return fatal status for fill-to-map-edge and
  cut-to-ocean.

Acceptance:

* Pure fixtures cover flat/no-op, resolved fill, resolved cut, unresolved cap,
  fill map edge, cut map edge, fill over ocean, cut into ocean, and fallback
  material resolution.
* Every non-fatal contribution is finite and non-negative.

### 4. Integrate predecessor-direction scoring

Status: implemented.

* Whenever expansion enters a new generated node, derive its forward direction
  from the predecessor and score the new profile's outgoing corners.
* For `G`-to-`V`, use the matched handoff geometry to determine the same
  predecessor-to-generated direction; reject ambiguous direction rather than
  silently omitting cost.
* Filter corner work through the proto operation selected for that generated
  cell: mining=cut only, dumping=fill only, leveling=both.
* Reject fatal ray results with stable rejection keys such as
  `SideRayFillMapEdge`, `SideRayCutOcean`, and `SideRaySnapshotMissing`.
* Do not add side-ray cost when leaving a generated node for `G` or an existing
  fixed profile.
* Extend result diagnostics with direct work, left/right side-ray work,
  unresolved penalties, fatal rejection counts, and ray sample counts.

Acceptance:

* Directional fixtures prove the same profile has different cost along-contour
  and across-slope.
* `G -> V1 -> V2 -> G` charges `V1` on entry, charges `V2` on entry, and
  charges nothing on the terminal handoff.
* A mining handoff ignores a would-be fill corner, and a dumping handoff ignores
  a would-be cut corner.
* A* and Dijkstra choose the same path and total cost with side rays enabled;
  the existing heuristic remains admissible because it ignores the new
  non-negative term.

### 5. Add settings and tuning diagnostics

Status: implemented.

* Introduce internal first-pass constants for ray distances, per-ray cap,
  unresolved penalty, direct-work weight, and side-ray weight. Expose only
  weights that prove necessary during save testing; avoid expanding public
  settings before stable defaults exist.
* Log total search timing, selected-route ray samples and cost components,
  unresolved counts, fatal rejection counts, and the selected dumping/fallback
  slopes in experimental diagnostics. The first pass deliberately remains
  uncached; add cache/hit diagnostics only if representative profiling shows
  repeated ray scoring is material.
* Add a temporary comparison mode or diagnostic that reports the selected route
  under center-only and side-ray costs without materializing both.

Acceptance:

* Side-ray scoring has a bounded number of terrain samples per generated-node
  entry.
* Diagnostics identify whether route changes came from direct work, resolved
  side wedges, unresolved penalties, or fatal boundaries.

### 6. Validate on representative terrain

Run Debug build verification, core synthetic fixtures, and manual dry-run
comparisons on:

* flat terrain, where the route should remain unchanged;
* a uniform sidehill, where contour-following should beat a direct cross-slope
  cut when the extra travel is reasonable;
* a convex hill, where lower routes prefer controlled fill and upper routes
  prefer controlled cut;
* a cliff and map boundary, exercising unresolved and fatal outcomes;
* coastal terrain, exercising cut/fill ocean asymmetry;
* mixed rock/dirt layers and multiple allowed dumping materials.

Record route cost breakdowns, visited-node counts, ray samples, and elapsed
search time. Tune caps and weights only after these cases preserve A*/Dijkstra
cost equality and show the intended route ordering.

### Deferred follow-up

V/G handoff feasibility is separate from side-ray costing.  A handoff cell has
two distinct faces:

* Its **V-facing / working edge** connects to the predecessor or successor V
  cell and must be mutually traversible after both cells finish.  For a mining
  handoff its planned height is at or below natural ground; for a dumping
  handoff it is at or above natural ground.
* Its opposite **G-facing / handoff edge** must be level with ground or lie on
  the opposite side of natural ground from the V-facing edge.  The profile then
  cuts the natural terrain somewhere in the cell, creating the bridge between
  artificial `V` terrain and natural `G` terrain.  It is incorrect to require
  the G-facing edge itself to be level.

The candidate must prove that this ground-crossing is adjacent to at least one
target-vehicle-mask-pathable `G` tile.  That tile may be immediately outside
the handoff cell (a conventional edge handoff), or it may lie within the
handoff cell where the final profile crosses rough natural terrain.  The latter
case is essential when entering or leaving a slope.  An in-cell `G` tile alone
is not sufficient: it must have a cardinal path out of the handoff cell after
the cell's work is accounted for.  The local path-out test treats every tile
the handoff designation will alter as blocked—mining: `target < ground`;
dumping: `target > ground`; leveling: either non-zero delta—and must reach a
target-vehicle-mask-pathable `G` tile outside the V corridor.  In particular,
the predecessor/successor V footprint that determines the working-edge
direction is also V, not a valid natural-G exit merely because its pre-work
snapshot terrain was green.  This prevents an apparent green crossing from
being accepted when its dumping/mining face seals it inside the handoff cell.

Only clearance-valid frontage lanes may establish the handoff.  For the
current one-cell V corridor, normal 3-wide excavators exclude one tile next to
each side.  The four terrain lanes are 0..3, so only middle lanes 1 and 2 are
drivable; lanes 0 and 3 provide clearance.  A Mega/T3 vehicle requires five
*terrain tiles* of clearance and therefore cannot use a single 4-tile-wide V
cell at all; the five sample positions on a profile are boundaries/samples, not
five traversible lanes.  The one-cell graph must reject such requests until a
separate 2+ V-cell-wide corridor design exists.  Corner-only contact is never
enough.  The returned search `G` node is still an actual pathable ground tile,
not an abstract contour intersection.  Vanilla's live pathability provider
cannot evaluate the future post-operation terrain profile without mutating the
world, so the crossing test is the conservative proxy until a prospective
vehicle profile query is available.

After cost behavior is stable, evaluate reusing the ray result for directional
durability and obstruction feasibility. That work should be a separate change:
it alters graph admissibility, whereas this plan changes only non-negative edge
cost and fatal environmental boundaries already defined above.
