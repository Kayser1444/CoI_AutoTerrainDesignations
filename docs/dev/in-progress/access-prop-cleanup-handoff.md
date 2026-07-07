# Access prop cleanup steps 1-5 handoff

Status: draft patch implementing the planned props/debris scaffolding from `docs/dev/planned/accessway-implementation-sequence.md` steps 1-5. Follow-up runtime work has now completed the local build/API check and wired live prop/tree cleanup metadata into snapshot construction with diagnostics. Cleanup-dependent placement remains deliberately guarded until cleanup actions are emitted.

## What changed

* Added the `AccessPropCleanupPolicy` helper surface with cleanup classes, blocker kinds, diagnostic names, synthetic prop samples, and the temporary one-level mining/dumping terrain-removal threshold.
* Extended `AccessSearchSnapshot` with immutable cleanup metadata keyed by 4x4 terrain origin. Ordinary ground membership remains separate; cleanup origins are exposed only through explicit cleanup lookups and `IsGroundOrCleanupNode`.
* Allowed single-width ground expansion and V/G handoff checks to traverse cleanup-eligible ground tiles, adding the configured `AccessPropCleanupLandscapingCost` through the existing landscaping distance scale when a path enters a cleanup origin.
* Extended search-result and materialization metadata with separate traversal, generated-work, generated-fixed-overhead, tree-cleanup, and dense-debris cleanup counters/collections.
* Added synthetic validation coverage for mixed tree+dense debris cleanup, hard blockers, the stubbed one-level threshold, cleanup snapshot overlay admission, and cleanup metadata preservation during materialization.
* Fixed the cleanup metadata self-test fixture so production snapshot refresh no longer aborts on an invalid synthetic handoff.
* Resolved `TreesManager` alongside `TerrainPropsManager` and wired live vehicle-blocking terrain props plus live trees into snapshot cleanup metadata.
* Added production dry-run diagnostics for cleanup samples, eligible origins, hard-blocked origins, and cleanup cost in selected plans.
* Added a dense-debris placement guard so cleanup-aware debris routes are logged but not silently placed before debris cleanup materialization exists.
* Enabled experimental tree cleanup materialization by selecting trees in accepted cleanup origins for vanilla harvest before accessway designations are placed, with rollback for tree selections added by ATD if terrain placement fails.
* Rejected generated V candidates on cleanup-eligible origins so the search cannot silently place terrain designations over trees/props instead of entering cleanup `G`.

## Situation and complications

* Decompiled vanilla source is available locally. It shows terrain props are pruned by placement height plus `DespawnBuriedThreshold`, excavator mining can remove a prop directly, and tree harvest selection goes through `TreesManager.AddToHarvest`.
* Live `TerrainPropsManager` and `TreesManager` enumeration is now wired into production snapshot construction. Cleanup routes are still dry-run-only for placement.
* Tree materialization now uses the vanilla `TreesManager.AddToHarvest` / `RemoveFromHarvest` path. This needs in-game verification with tree harvesters assigned.
* Cleanup materialization records accepted cleanup metadata separately from generated `V` designations, but live debris mining-designation emission and rollback integration still need to be connected at the runtime placement layer.
* Search charges cleanup when entering an eligible origin from another origin. This satisfies contiguous same-origin traversal in the drafted V1 topology, but should be revisited if later topology permits leaving and re-entering the same cleanup origin through a loop.
* Generated V over cleanup origins is currently blocked rather than combined with cleanup metadata. If later terrain-work-plus-cleanup on the same 4x4 origin is needed, it should be implemented explicitly with materialization and rollback support.

## Assumptions made

* Cleanup classes are flags because one 4x4 origin may contain both tree and dense-debris samples.
* Non-removable prop samples are hard blockers.
* Until vanilla rules are sourced, a prop is considered removed by terrain work only when mining lowers or dumping raises the occupied sample by at least one full terrain level in the matching direction.
* Cleanup `G` is metadata over ground traversal, not a new search geometry mode and not a generated `V` profile.
* Cleanup metadata must not feed fixed-profile compatibility, durability sources, fight-invariant checks, or access-provider state.

## Validations pending

* Runtime dry-run against saves containing removable debris, trees, mixed origins, existing designations, and disappearing props.
* Verify that cleanup-aware dry-run chooses cleanup `G` through the forest instead of generated `V` cells and that selected trees are marked for harvest.
* Verify safe tree harvest/removal behavior in-game with assigned tree harvesters.
* Verify transaction rollback removes cleanup designations/actions together with generated accessway designations.

## Open questions

* Which vanilla prop proto fields distinguish sparse tree cleanup, dense debris cleanup, removable debris, and true hard blockers in all supported CoI versions?
* Does vanilla remove props at exactly one full level of terrain delta, or are there additional direction/material/footprint thresholds?
* Should tree cleanup and dense-debris cleanup share one configured cost or eventually split into independent knobs?
* Should cleanup-origin cost remain charged once per path, once per contiguous entry, or once per materialized origin if future topology allows loops?
* What dry-run UI/log format should expose `StubbedTerrainRemovalThreshold` strongly enough that users do not mistake it for verified vanilla behavior?

## Next suggested steps

1. Retest the current build and inspect `[ATD Experimental Access Cleanup]` plus plan cleanup-cost diagnostics.
2. Verify tree harvest materialization in-game and confirm rollback behavior if accessway placement fails after selecting trees.
3. Implement guarded debris cleanup mining-designation emission using ATD ownership/rollback tracking, then remove the placement guard for dense-debris-only plans.
4. Capture deterministic save fixtures for the step-5 acceptance cases before moving on to merged-goal Dijkstra work.
