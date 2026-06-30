# Accessway Pathfinding Side-Ray Work Cost Amendment

Status: planned amendment to [Accessway Pathfinding](accessway-pathfinding.md). This note refines the terrain-work part of the path cost for mountain and cliff approaches where a center-height or corner-height delta is too weak to rank good routes.

## Motivation

The MVP pathfinding cost intentionally starts with a simple center-height delta, but that value can badly understate accessway work on mountainsides. When a corridor runs along or across a steep hillside, the local vertical delta at the center or corners can be small even though the designation must cut a long bench into the uphill side, dump a long shoulder on the downhill side, or both.

For these cases the meaningful cost is the lateral distance from the planned accessway edge to the point where a legal material slope reaches existing ground. This amendment models that distance with a small bounded ray march instead of attempting full landslide or material-volume simulation.

## Design principle

Use an additive local cost that remains cheap enough for A*/Dijkstra:

```text
edgeCost = traversalLength
         + workDistanceScale * terrainWorkCost
```

`terrainWorkCost` is no longer only a vertical height delta. It is a lightweight estimate of accessway construction effort:

```text
terrainWorkCost = designationOverhead
                + directWorkCost
                + lateralSideRayCost
```

The exact weights are tuning parameters. The important behavioral change is that sidehill routes pay for lateral wedge depth, not just local vertical delta.

## Exit-corner sampling

For an ordered corridor path, each generated segment scores only its exit edge:

* The predecessor already accounted for the shared entry edge.
* Scoring only exits avoids double-counting interior segment boundaries.
* The first generated segment still pays the fixed designation overhead; a future implementation may also score its exposed entry edge if tests show undercounting at route starts.

For a segment moving in direction `d`, choose the two corners on the outgoing edge and shoot one lateral ray from each corner:

| Move direction | Exit edge | Exit corners | Lateral rays |
|---|---|---|---|
| `X+` | `X+` edge | `NE`, `SE` | `Y-` from `NE`, `Y+` from `SE` |
| `X-` | `X-` edge | `NW`, `SW` | `Y-` from `NW`, `Y+` from `SW` |
| `Y+` | `Y+` edge | `SW`, `SE` | `X-` from `SW`, `X+` from `SE` |
| `Y-` | `Y-` edge | `NW`, `NE` | `X-` from `NW`, `X+` from `NE` |

The side-ray cost is therefore direction-aware: the same origin and target profile can have a different work cost depending on whether the corridor runs along the contour or across the hillside.

## Lateral-only ray march

Only lateral rays are sampled. Longitudinal start/end work is already represented by the path's predecessor/successor structure: an accessway starts from ground, connects segment-to-segment through shared edges, and ends at ground. The missing signal is side exposure, especially on steep terrain.

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

Recommended first-pass controls:

```text
maxRayDistance      = 16 terrain tiles by default; test 21 only if cliffs are under-penalized
maxRayCost          = tuned finite cap
unresolvedRayPenalty = tuned finite penalty added at max distance
```

## Relationship to direct work cost

The side-ray cost does not need to replace all direct work cost. Keep a small direct term and a per-designation overhead so flat terrain and short transitions remain well behaved:

```text
terrainWorkCost = overhead
                + directWorkWeight * directVerticalWork
                + sideRayWeight * (leftExitRayCost + rightExitRayCost)
```

`directVerticalWork` may initially reuse the MVP center-height approximation. Later, it can be replaced with a small sample-grid estimate if necessary.

## Turns and special cases

The first implementation can score every segment's exit edge in its own direction and rely on the fixed overhead to cover remaining work. Later refinements may add:

* an exposed-entry score for the first generated segment,
* an outside-corner ray for sharp turns and switchbacks,
* unique-edge accounting for branched or reused fixed profiles,
* separate cut and fill weights,
* material-specific slope factors when reliable data is available.

## Expected behavior

This amendment should make the pathfinder prefer routes that are longer but materially easier, such as contour-following paths and switchbacks, over short routes that slice across a mountainside. On hills, the side-ray scorer should also bias the route shape toward more filling near the base of the hill and more mining near the top: low routes that cut deeply into the uphill face should lose to modest fills/shoulders, while high routes that would require large exposed dumping shoulders should lose to controlled excavation into the hillside. The cost remains local, non-negative, bounded, and additive, preserving the shortest-path requirements while giving the search a much better signal for hillside work.
