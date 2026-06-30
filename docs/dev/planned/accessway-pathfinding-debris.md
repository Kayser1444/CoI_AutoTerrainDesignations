# Accessway Pathfinding Debris Amendment

Status: planned amendment to [Accessway Pathfinding](accessway-pathfinding.md). This note narrows the existing open debris question into a route-search and materialization contract for the new accessway pathfinder.

## Problem

Debris props such as boulders and bushes can sit on top of otherwise usable ground. Vanilla vehicle pathability treats those props as blocking, so the current accessway snapshot excludes the affected ground tiles from the `G` graph. When a blocked ground strip lies between two clear ground regions, the pathfinder can route around it by switching from `G` to generated `V`, digging under or through the debris footprint, then switching back from `V` to `G`.

That behavior is valid but usually wasteful. Debris removal is cheap, does not require changing the terrain surface, and is already expressible as a mining designation one level above the current ground. The pathfinder should prefer clearing debris over unnecessary terraforming when the underlying terrain height is otherwise traversable.

## Existing implementation clues

ATD's standalone debris cleanup flow already has the two pieces this amendment needs:

* **Detection.** `CollectDebrisDesignationOrigins` enumerates terrain props in the tower area, ignores props whose proto does not block vehicles, converts occupied terrain tiles to 4x4 designation origins, and keeps only origins whose whole designation footprint is inside the managed area.
* **Materialization.** `CreateDebrisRemovalDesignationsCoroutine` places a mining designation at each debris origin when the origin has no ore designation, no existing terrain designation, and remains fully inside the managed area. Each corner target is the current surface height at that corner plus one level. Successful placements are registered as generated tower designations.

The accessway implementation already separates immutable search state from placement. `BuildExperimentalAccessSnapshot` captures ground heights, fixed designation profiles, occupied building tiles, ocean tiles, durability sources, and currently pathable `G` nodes before search. `AccessPathMaterializer.Materialize` then replays the accepted path against the snapshot, emits generated `V` designations, preserves reused `G` nodes as no-op path segments, and rejects snapshot-inconsistent paths before mutable placement begins.

## Desired behavior

Treat removable debris as a low-cost property of a ground node, not as a reason to manufacture terrain work:

1. A ground tile whose only blocker is removable debris may enter the search as a **debris-clearing G node** if the underlying terrain height and slope are otherwise valid for vehicle traversal.
2. Traversing that node pays normal G traversal length plus a small one-time debris cleanup work cost.
3. The cleanup cost is equivalent to `0.2` delta-h in the existing work-cost units. With the current cost formula, the incremental cost is `workDistanceScale * 0.2` once per affected debris designation origin, not once per tile step through the same origin.
4. Successful path materialization emits debris-removal mining designations for debris origins used by the accepted path, using the same target profile as the standalone cleanup flow: current corner surface height plus one.
5. Clearing debris makes the affected ground usable; it does **not** create a `V` provider, alter the ground height, consume a generated accessway origin, or participate in accessway geometry checks.
6. Generated debris cleanup designations are mining protos **in the air**. They remove props; they are not terrain-shaping V mining designations and must not be treated as fixed profiles, fight-invariant neighbors, or durability sources.

## Search model changes

Augment the snapshot with debris metadata instead of mutating the existing `G` meaning globally:

* `debrisOrigins`: the 4x4 terrain-designation origins occupied by vehicle-blocking removable props inside the tower area.
* `debrisTiles`: occupied terrain tiles mapped to their debris origin.
* `cleanupEligibleDebrisOrigins`: debris origins that are fully inside the tower area, have no existing terrain designation, are not source work origins, and are not occupied by buildings or ocean-blocked terrain.

`G` node construction should distinguish **terrain validity** from **current vanilla pathability**:

* Ordinary G nodes remain unchanged: terrain tile is pathable now, not inside a terrain designation origin, not durability-blocked, not ocean-blocked, and not occupied by a building.
* Debris-clearing G nodes are admitted when the tile fails vanilla pathability because of a removable debris prop, but the same tile would otherwise pass the ground-height, slope, area, designation-origin, durability, building, and ocean checks.
* Non-debris blockers remain hard blockers. Buildings, active terrain designations, non-removable props, ocean-below-minimum, durability exclusions, and out-of-area footprints must not be reclassified as debris-clearing G.

The search state can remain `AccessSearchMode.Ground`; debris is edge/path metadata, not a new geometry mode. The predecessor record or accumulated path metadata must remember which cleanup origins were first entered so the route pays cleanup cost once and the materializer can emit exactly those cleanup designations.

Debris cleanup designations must stay out of generated-profile compatibility inputs. Do not add their `surfaceHeight + 1` corner targets to fixed profiles, path-history corner maps, durability-corner sources, or fight-invariant checks. A true V mining handoff or generated accessway segment may start adjacent to a debris cleanup origin; it only needs to satisfy the normal V/G or V/V rules against real terrain or real V profiles, not share an edge with the cleanup designation.

## Costing and tie-breaks

A cleanup origin has cost:

```text
debrisCleanupWork = 0.2
incrementalCost = workDistanceScale * debrisCleanupWork
```

Apply it the first time the candidate path enters any tile belonging to that debris origin. Subsequent G-to-G movement within the same debris origin pays only traversal length. This keeps a four-tile walk across one boulder from looking four times more expensive than clearing the boulder.

This should be cheaper than a G/V/G detour that digs a generated origin, but not free. If two alternatives are otherwise identical, prefer already-clear ground over debris cleanup, and prefer debris cleanup over terrain reshaping.

Diagnostics should include separate counters and costs:

* number of debris origins considered eligible;
* number of debris-clearing G nodes admitted;
* number of cleanup origins used by the selected path;
* total debris cleanup cost, separate from generated-terrain work and traversal length.

## Materialization contract

The accepted path materializes in two independent buckets:

1. **Terrain accessway designations** from generated `V` nodes, exactly as today.
2. **Debris cleanup designations** from cleanup origins used by traversed G nodes.

Before placement, rematerialization must revalidate that each cleanup origin still has vehicle-blocking debris, is still fully inside the area, and still has no terrain designation. If any cleanup origin became unnecessary because debris disappeared, drop that cleanup item and continue. If any cleanup origin became unsafe or conflicted with a terrain designation, reject the candidate and let the caller search again against a fresh snapshot or fall back.

Placement should use the mining proto and target `surfaceHeight + 1` at each of the four designation corners, matching the existing cleanup flow. Treat these placements as cleanup work items rather than terrain profiles: they should be registered for ownership and rollback, but not reintroduced into the access snapshot as access providers, fixed V profiles, fight-invariant blockers, or durability-envelope sources. Successful debris cleanup placements should be registered through the same generated-designation ownership path used by ATD-generated mining/accessway designations so rollback, tower ownership, and save-removability behavior stay consistent.

Rollback must remove both generated `V` designations and debris cleanup designations placed for the failed accessway transaction.

## Interaction with G/V handoffs

Debris-clearing G nodes are still G nodes. They may appear before a V segment, after a V segment, between two V segments, or as the entire route from a work origin to tower-reachable ground. A debris tile should not force a V/G boundary by itself.

This directly avoids the current inefficient pattern:

```text
G -> V (dig under debris) -> G
```

when the cheaper and more faithful route is:

```text
G(debris cleanup) -> G
```

The V/G handoff rules remain unchanged for genuine terrain edits. Handoff operation selection still applies only to generated V terminal designations, not to debris cleanup G nodes.

## Fixtures and acceptance criteria

Add deterministic fixtures before enabling this in production routing:

* A flat ground corridor blocked by one debris origin routes through G with one cleanup designation and no generated V designations.
* The same corridor without debris routes through plain G with zero cleanup cost.
* A path that crosses multiple tiles of the same debris origin pays cleanup cost once.
* A non-removable vehicle blocker remains impassable and does not produce cleanup materialization.
* A debris origin that already has a terrain designation is not eligible for G cleanup routing.
* Rematerialization drops cleanup if debris disappeared, and rejects if a conflicting designation appeared.
* A true V mining handoff adjacent to, but not edge-sharing with, an in-air debris cleanup designation is valid when the real terrain/V handoff checks pass; the cleanup designation does not create a fight-invariant or durability rejection.
* Rollback removes cleanup designations along with any generated V accessway designations from the same transaction.
