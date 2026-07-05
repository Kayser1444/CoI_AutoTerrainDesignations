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

For an ordered corridor path, score only constructed interior exit edges:

* The starting edge has zero landscaping cost. Accessways always start from a handoff edge, whose mining/dumping proto does not work the ground-connecting edge, or from an intent-fixed profile that is already determined outside this scorer.
* Interior generated-to-generated exits are the only edges that need side-ray landscaping-cost scoring. The predecessor accounted for the shared entry edge, so scoring exits avoids double-counting interior segment boundaries.
* The terminal edge has zero landscaping cost. Accessways end through the same kind of 0-work handoff to V/G or at another cluster/fixed designation whose profile is already fixed; do not add side-ray or overhead work for the last handoff segment. Traversal length may still be paid separately by the main edge-cost formula.

For a scored interior segment moving in direction `d`, choose the two corners on the outgoing edge and shoot one lateral ray from each corner:

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

Use a short fixed step table rather than dense linear sampling, for example:

```text
1, 2, 3, 5, 8, 13, 16
```

This is Fibonacci-like at short range, then deliberately caps at `16` terrain tiles instead of continuing to `21`. The cap is tied to the terrain/designation scale: `16` tiles is four 4x4 designation widths, which is already a very large side wedge for an accessway scorer. Continuing to `21` may be useful as an experiment on extreme cliffs, but it should be a tuning variant rather than the first-pass default because it increases each unresolved ray's search radius by another full designation width plus one tile and can make far-away terrain dominate candidate ranking.

For each sample distance:

```text
stepLength = distance - previousDistance
ray_h      = corner_h +/- distance * materialSlope
 ground_h  = terrainHeight(corner_xy + lateralDirection * distance)
```

The sign of the ray slope depends on whether the side is fill or cut. Determine the side operation from the planned corner height versus the current terrain height at or near the corner:

* **Fill side:** planned corner is above terrain; the side ray slopes downward/outward until it reaches ground.
* **Cut side:** planned corner is below terrain; the side ray slopes upward/outward until it reaches ground/open terrain.

## Material run-slope selection

`materialSlope` should be chosen from the material that will actually form the side wedge, and fill/cut must use different material forms:

* **Dumping/fill:** inspect the active dumping rules for the tower/accessway, look up each allowed material's terrain properties, and use the material with the lowest stable slope/angle as the ray slope. This is the most conservative option because trucks may dump the runniest allowed material. Use the **disturbed** material properties for dumping; dumped material is loose/disturbed and is significantly more runny than the normal in-ground material.
* **Mining/cut:** sample the terrain material at the exit corner's `(x, y)` and the planned corner elevation used by the ray. This point is guaranteed to be underground for a cut side. Use the material's **normal** in-ground properties, not its disturbed/dumped variant.

If a material lookup fails, fall back to the conservative runniest known terrain-material slope and include diagnostics so the scorer does not silently become optimistic.

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

Recommended first-pass controls:

```text
maxRayDistance      = 16 terrain tiles by default; test 21 only if cliffs are under-penalized
maxRayCost          = tuned finite cap
unresolvedRayPenalty = tuned finite penalty added at max distance
```

## Deferred durability and obstruction integration

Do not combine side rays with durability or obstruction feasibility in the first implementation. Keep the initial side-ray work scoped to cost estimation until the routing, material-slope selection, and tuning are stable. The existing durability/hourglass and hard-obstacle checks should remain the authoritative feasibility filters during that phase.

Once the cost scorer is stable, the side-ray march is a candidate for a more accurate generated-edge landslide/support check. At that later stage, reuse the same ray samples for scoring and feasibility where possible: one bounded march can return the integrated landscaping cost plus any hard blocker it encountered.

Deferred feasibility rules to evaluate later:

* **Buildings:** if the ray/wedge crosses an occupied or planned building footprint, reject the candidate edge. Buildings remain hard obstacles, not soft cost.
* **Other designations:** if the ray/wedge crosses an active or fixed designation that is not the predecessor/successor profile already accounted for by the accessway, reject unless that designation's fixed target profile is explicitly compatible with the ray side. Do not assume future landslides may consume or overwrite unrelated work.
* **Current candidate designations:** compatible generated neighbours in the same candidate path are allowed; shared interior edges are already accounted for by the exit-only scoring rule.

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

The first implementation should score each constructed interior segment's exit edge in its own direction and leave start/terminal handoff or fixed edges at zero landscaping cost. Later refinements may add:

* an outside-corner ray for sharp turns and switchbacks,
* unique-edge accounting for branched or reused fixed profiles,
* separate cut and fill weights.

## Expected behavior

This amendment should make the pathfinder prefer routes that are longer but materially easier, such as contour-following paths and switchbacks, over short routes that slice across a mountainside. On hills, the side-ray scorer should also bias the route shape toward more filling near the base of the hill and more mining near the top: low routes that cut deeply into the uphill face should lose to modest fills/shoulders, while high routes that would require large exposed dumping shoulders should lose to controlled excavation into the hillside. The cost remains local, non-negative, bounded, and additive, preserving the shortest-path requirements while giving the search a much better signal for hillside work.
