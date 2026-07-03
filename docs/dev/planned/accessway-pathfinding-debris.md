# Accessway Pathfinding Debris Amendment

Status: planned amendment to [Accessway Pathfinding](accessway-pathfinding.md). This note narrows the existing open debris question into a route-search and materialization contract for the new accessway pathfinder.

## Problem

Terrain props are not all equivalent blockers. Trees are props, but CoI spaces trees far enough apart that even mega vehicles can normally weave through or around a forest without terrain work. Boulders, bushes, and similar debris props are different: they can sit densely enough on otherwise usable ground that vanilla vehicle pathability marks the occupied tiles blocked. The current accessway snapshot excludes those blocked tiles from the `G` graph. When a blocked ground strip lies between two clear ground regions, the pathfinder can route around it by switching from `G` to generated `V`, digging under or through the prop footprint, then switching back from `V` to `G`.

That behavior is valid but usually wasteful for removable debris. Debris removal is cheap, does not require changing the terrain surface, and is already expressible as a mining designation one level above the current ground. Forest traversal is a separate case: because trees are naturally sparse, a wiggly `G` route through forest should usually remain preferable to manufacturing a straight generated `V` dirt road solely to bypass tree pathability. If the selected route does remove trees, the removal can be materialized as cleanup/harvesting so the initially wiggly route gradually becomes a dirt service road instead of a mined trench.


## Prop classes

Treat blocking terrain props by class rather than flattening every prop into the same route cost:

* **Trees / forest.** Trees are terrain props, but their placement rules leave enough spacing for large vehicles to drive between them. A tree-occupied tile should not by itself force a generated `V` bypass. Prefer `G` traversal through or around forest; if the accepted route uses tree-blocked cleanup origins, materialize tree removal/harvesting as cleanup work so the route opens over time. This is an immersive outcome: the first trips may wiggle through forest, and the generated cleanup turns the used path into a dirt highway without terrain reshaping.
* **Boulders, bushes, and dense debris.** These props can genuinely block ground corridors. They should remain low-cost debris-clearing `G` nodes when removable, with cleanup designations emitted during materialization.
* **Non-removable or non-debris blockers.** These remain hard blockers and must not be converted into cleanup `G` unless the game exposes a safe removal designation for them.

The search should still distinguish already-clear `G`, cleanup `G`, and generated `V`: prefer clear `G` first, cleanup/harvest `G` second, and terrain-shaping `V` only when its extra fixed overhead and work cost are justified.

## Existing implementation clues

ATD's standalone debris cleanup flow already has the two pieces this amendment needs:

* **Detection.** `CollectDebrisDesignationOrigins` enumerates terrain props in the tower area, ignores props whose proto does not block vehicles, converts occupied terrain tiles to 4x4 designation origins, and keeps only origins whose whole designation footprint is inside the managed area.
* **Materialization.** `CreateDebrisRemovalDesignationsCoroutine` places a mining designation at each debris origin when the origin has no ore designation, no existing terrain designation, and remains fully inside the managed area. Each corner target is the current surface height at that corner plus one level. Successful placements are registered as generated tower designations.

AFD's forestry information panel provides the tree-harvest precedent to verify before implementation ([`CoI_AutoForestryDesignations/src/AFD.ForestryInfoPanel.cs`](https://github.com/Kayser1444/CoI_AutoForestryDesignations/blob/main/src/AFD.ForestryInfoPanel.cs)):

* It imports `Mafi.Core.Buildings.Forestry` and `Mafi.Core.Terrain.Trees`, works from a `ForestryTower`, and iterates `tower.Trees` through `TreesManager.Trees` while filtering to tiles covered by fulfilled forestry designations.
* Its UI bucket click path checks `TreesManager.IsTreeSelected(treeId)`, calls `TreesManager.AddToHarvest(treeId)` for unselected trees, calls `TreesManager.RemoveFromHarvest(treeId)` when toggling a fully selected bucket off, and then activates the harvest overlay.
* ATD should not copy AFD UI code, but the cleanup materializer can use the same manager-level operations as the implementation clue for tree cleanup actions: collect `TreeId`s in the selected cleanup footprint, mark them for harvest, and leave ordinary debris cleanup on the mining-designation path.

The accessway implementation already separates immutable search state from placement. `BuildExperimentalAccessSnapshot` captures ground heights, fixed designation profiles, occupied building tiles, ocean tiles, durability sources, and currently pathable `G` nodes before search. `AccessPathMaterializer.Materialize` then replays the accepted path against the snapshot, emits generated `V` designations, preserves reused `G` nodes as no-op path segments, and rejects snapshot-inconsistent paths before mutable placement begins.

## Desired behavior

Treat removable debris as a low-cost property of a ground node, not as a reason to manufacture terrain work. The one exception is genuine terrain work that will remove the prop anyway: any amount of mining removes removable props. Dumping onto removable props, and any mining/dumping behavior for trees, still needs exact threshold verification in the decompiled game source before implementation, so this amendment names those unknowns explicitly instead of hard-coding values.

The route model therefore has two prop-safe ways through a blocked origin:

1. A ground tile whose only blocker is removable debris may enter the search as a **debris-clearing G node** if the underlying terrain height and slope are otherwise valid for vehicle traversal.
2. Traversing that node pays normal G traversal length plus a small one-time debris cleanup work cost.
3. A generated `V` node may overlap removable non-tree props without a separate cleanup action wherever it performs any mining at the prop-occupied sample tiles. It may overlap removable props via dumping, or trees via mining/dumping, only where the planned work meets the verified removal threshold for that prop/work class. Zero-work `V` must not ignore props.
4. The cleanup cost is equivalent to `0.2` delta-h in the existing work-cost units. With the current cost formula, the incremental cost is `workDistanceScale * 0.2` once per affected debris designation origin, not once per tile step through the same origin.
5. Successful path materialization emits debris-removal mining designations for debris origins used by the accepted path, using the same target profile as the standalone cleanup flow: current corner surface height plus one.
6. Clearing debris makes the affected ground usable; it does **not** create a `V` provider, alter the ground height, consume a generated accessway origin, or participate in accessway geometry checks.
7. Generated debris cleanup designations are mining protos **in the air**. They remove props; they are not terrain-shaping V mining designations and must not be treated as fixed profiles, fight-invariant neighbors, or durability sources.

## Search model changes

Augment the snapshot with debris metadata instead of mutating the existing `G` meaning globally:

* `debrisOrigins`: the 4x4 terrain-designation origins occupied by vehicle-blocking removable props inside the tower area.
* `debrisTiles`: occupied terrain tiles mapped to their debris origin.
* `propCleanupByOrigin`: a per-origin classification set, not a single enum. Rarely, trees and removable debris can occupy the same 4x4 origin, so an origin may carry both `Tree` and `DenseDebris` cleanup requirements.
* `treeOrigins` / `treeTiles`, if kept as convenience indexes, are derived views of `propCleanupByOrigin` and must not imply that the origin is tree-only.
* `cleanupEligibleDebrisOrigins`: debris origins that are fully inside the tower area, have no existing terrain designation, are not source work origins, and are not occupied by buildings or ocean-blocked terrain.
* `removablePropMiningRemoves`: a fixed rule that any amount of mining removes removable non-tree props.
* `removablePropDumpThreshold`: verified game threshold, sourced from decompiled code, for when terrain raising/dumping removes removable non-tree props.
* `treeMiningRemoveThreshold` / `treeDumpThreshold`: verified game thresholds, sourced from decompiled code, for when terrain lowering or raising removes tree props without a separate harvest action.

`G` node construction should distinguish **terrain validity** from **current vanilla pathability**:

* Ordinary G nodes remain unchanged: terrain tile is pathable now, not inside a terrain designation origin, not durability-blocked, not ocean-blocked, and not occupied by a building.
* Debris-clearing G nodes are admitted when the tile fails vanilla pathability because of a removable debris prop, but the same tile would otherwise pass the ground-height, slope, area, designation-origin, durability, building, and ocean checks. Tree-blocked G nodes use the same mechanism but should have a tree/harvest cleanup classification so diagnostics and materialization can distinguish forest opening from boulder/bush cleanup.
* Mixed tree-plus-debris origins are cleanup `G` nodes only if every blocker in the origin has a safe cleanup/harvest action. They inherit the dense-debris cleanup cost/category for route preference; the tree flag is additional materialization metadata, not a reason to treat the origin as cheap forest-only traversal.
* Non-debris blockers remain hard blockers. Buildings, active terrain designations, non-removable props, ocean-below-minimum, durability exclusions, and out-of-area footprints must not be reclassified as debris-clearing G.
* Generated `V` candidates evaluate prop-occupied sample tiles separately from `G`: a removable non-tree prop inside the candidate footprint is legal without cleanup if the candidate mines that tile by any amount, or if dumping reaches the verified removable-prop dump threshold. Tree props are legal without cleanup only if the candidate's mining/dumping reaches the verified tree removal threshold. Otherwise the candidate must either carry cleanup metadata for that origin or be rejected as prop-blocked.

The search state can remain `AccessSearchMode.Ground`; debris is edge/path metadata, not a new geometry mode. The predecessor record or accumulated path metadata must remember which cleanup origins were first entered so the route pays cleanup cost once and the materializer can emit exactly those cleanup designations. For mixed origins, "once" means once per origin for the route cost, while the remembered metadata still contains every required prop cleanup class for placement.

Prop cleanup designations must stay out of generated-profile compatibility inputs. Do not add their `surfaceHeight + 1` corner targets to fixed profiles, path-history corner maps, durability-corner sources, or fight-invariant checks. A true V mining handoff or generated accessway segment may start adjacent to a debris cleanup origin; it only needs to satisfy the normal V/G or V/V rules against real terrain or real V profiles, not share an edge with the cleanup designation.

## Vanilla pathfinder integration

The vanilla pathability provider remains authoritative for ordinary already-clear `G`, tower seeds, and final post-placement validation, but it should not be asked to pretend that cleanup props are absent. If vanilla exposes a safe query that ignores terrain props while retaining slope, ocean, building, and vehicle-clearance rules, ATD may use it as an optimization. Otherwise the accessway search needs an ATD overlay graph:

1. Query vanilla pathability normally and keep every passing tile/footprint as ordinary clear `G`.
2. For vanilla-blocked tiles/footprints, look up the blocker in the immutable prop snapshot. If every blocker is cleanup-eligible and the underlying terrain would satisfy ATD's terrain, slope, ocean, durability, building, area, and clearance checks, admit the tile/footprint as cleanup `G` with the appropriate cleanup metadata and cost.
3. Keep all other vanilla-blocked tiles/footprints blocked. Do not globally disable prop checks in vanilla pathing, because that would also hide non-removable blockers and would make the final route disagree with real vehicle pathing until cleanup/materialization has happened.

In other words, this amendment does not require replacing vanilla pathfinding wholesale, but it does require ATD-owned graph expansion for the speculative cleanup layer. Vanilla pathfinding can still validate the already-clear parts of the route and the completed world after cleanup/designation placement.

## Costing and tie-breaks

A cleanup origin has cost:

```text
debrisCleanupWork = 0.2
incrementalCost = workDistanceScale * debrisCleanupWork
```

Apply it the first time the candidate path enters any tile belonging to that debris origin. Subsequent G-to-G movement within the same debris origin pays only traversal length. This keeps a four-tile walk across one boulder from looking four times more expensive than clearing the boulder. If an origin contains both trees and dense removable debris, charge the dense-debris cleanup cost once for that origin rather than adding a second tree-only cost; diagnostics should still record that both cleanup classes were present.

This should be cheaper than a G/V/G detour that digs a generated origin only to bypass props, but not free. If the generated origin performs prop-removing terrain work (any mining for removable non-tree props; threshold-clearing dumping for removable non-tree props; threshold-clearing mining/dumping for trees), no separate cleanup cost is needed for that prop because the terrain work itself clears it. If two alternatives are otherwise identical, prefer already-clear ground over tree cleanup, prefer tree cleanup over denser debris cleanup when both preserve `G`, and prefer any cleanup `G` over terrain reshaping. A fixed overhead on newly generated `V` nodes should be high enough that a plausible wiggly `G` route through sparse forest beats a straight generated `V` road, while still allowing `V` when terrain height or true blockers make `G` unreasonable.

Diagnostics should include separate counters and costs:

* number of debris origins considered eligible;
* number of debris-clearing G nodes admitted;
* number of cleanup origins used by the selected path, split by tree/forest vs boulder/bush/debris when known;
* total debris cleanup/harvest cost, separate from generated-terrain work, generated-`V` fixed overhead, and traversal length.

## Materialization contract

The accepted path materializes in two independent buckets:

1. **Terrain accessway designations** from generated `V` nodes, exactly as today.
2. **Prop cleanup designations/actions** from cleanup origins used by traversed G nodes, including all cleanup classes recorded for mixed tree-plus-debris origins.

Before placement, rematerialization must revalidate that each cleanup origin still has the blocking prop classes recorded by the selected path, is still fully inside the area, and still has no terrain designation. For generated `V` paths that rely on terrain work to remove props, rematerialization must also re-check that the final target profile still performs the required prop-removing work: any mining for removable non-tree props, verified-threshold dumping for removable non-tree props, and verified-threshold mining/dumping for trees. If a cleanup origin became unnecessary because all relevant props disappeared, drop that cleanup item and continue. If only some classes disappeared from a mixed origin, keep the remaining cleanup actions and update diagnostics. If any cleanup origin became unsafe or conflicted with a terrain designation, reject the candidate and let the caller search again against a fresh snapshot or fall back.

Placement should use the mining proto and target `surfaceHeight + 1` at each of the four designation corners for debris cleanup, matching the existing cleanup flow. Tree cleanup should prefer the forestry harvest mechanism verified from AFD: resolve the `TreeId`s in the accepted cleanup footprint, call the tree manager's harvest-selection operation for each unselected tree, and activate/refresh the harvest overlay if needed. If the game cannot safely express tree harvesting together with debris cleanup for the same origin, the mixed origin should fall back to the debris cleanup action that removes all blocking props or be rejected and re-searched. Treat these placements/actions as cleanup work items rather than terrain profiles: they should be registered for ownership and rollback, but not reintroduced into the access snapshot as access providers, fixed V profiles, fight-invariant blockers, or durability-envelope sources. Successful debris cleanup placements should be registered through the same generated-designation ownership path used by ATD-generated mining/accessway designations so rollback, tower ownership, and save-removability behavior stay consistent.

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

## Higher clearance

Cleanup routing must be evaluated over the same clearance footprint as the access request, not over individual center tiles:

* For clearance 1, a cleanup `G` state can be admitted from the affected tile/origin metadata described above.
* For clearance 2+ / mega vehicles, the candidate `G` state is valid only if the whole vanilla vehicle footprint or ATD clearance brush can be occupied after the planned cleanup. Each footprint tile that vanilla reports blocked must map either to an already-clear pathability result, an eligible cleanup prop, or a generated `V` terrain edit that removes the prop under the mining/dumping rules above.
* Cleanup cost is still charged once per 4x4 cleanup origin, even if a wide vehicle footprint touches several tiles in that origin. A wide route that touches multiple cleanup origins pays for each distinct origin it requires.
* A mixed wide footprint must not be downgraded to tree-only just because one lane is forest: if any lane/footprint tile needs dense-debris cleanup, the route carries the dense-debris category and materialization metadata for that origin.
* V2+ G/V handoffs must prove both width and prop legality at the seam. It is not enough for one lane to be cleanup-valid; every lane in the exposed frontage must be clear, cleanup-eligible, or prop-removing through verified terrain work.

This keeps the sparse-forest preference for mega vehicles without assuming that single-tile pathability generalizes to higher clearance.

## Fixtures and acceptance criteria

Add deterministic fixtures before enabling this in production routing:

* A sparse forest corridor routes through `G` with tree cleanup/harvesting metadata instead of a generated `V` straightening pass, and materialization marks the selected `TreeId`s for harvest through the tree manager rather than emitting a terrain profile.
* If vanilla cannot ignore cleanup props selectively, the same sparse forest corridor is admitted through the ATD overlay graph rather than by globally disabling prop checks in the vanilla pathability provider.
* A flat ground corridor blocked by one non-tree debris origin routes through G with one cleanup designation and no generated V designations.
* A clearance-2 route through cleanup props validates the whole mega-vehicle footprint, charges each cleanup origin once, and rejects if any lane contains a non-cleanup blocker.
* The same corridor without debris routes through plain G with zero cleanup cost.
* A path that crosses multiple tiles of the same debris origin pays cleanup cost once.
* A mixed tree-plus-boulder origin is classified as cleanup `G`, pays the dense-debris cleanup cost once, preserves both cleanup classes for materialization/diagnostics, and is not treated as tree-only forest traversal.
* A zero-work generated `V` profile over blocking props is rejected or requires cleanup metadata; it must not ignore props merely because it is `V`.
* A generated `V` profile that mines every relevant removable non-tree prop-occupied tile by any amount may overlap those props without a separate cleanup designation.
* A generated `V` profile that only dumps onto removable non-tree props, or that mines/dumps trees, may overlap those props without separate cleanup only when the verified prop/work-class threshold is met.
* A non-removable vehicle blocker remains impassable and does not produce cleanup materialization.
* A debris origin that already has a terrain designation is not eligible for G cleanup routing.
* Rematerialization drops cleanup if debris disappeared, and rejects if a conflicting designation appeared.
* A true V mining handoff adjacent to, but not edge-sharing with, an in-air debris cleanup designation is valid when the real terrain/V handoff checks pass; the cleanup designation does not create a fight-invariant or durability rejection.
* Rollback removes cleanup designations along with any generated V accessway designations from the same transaction.
