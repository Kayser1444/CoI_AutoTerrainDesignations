# Access prop cleanup steps 1-5 handoff

Status: draft patch implementing the planned props/debris scaffolding from `docs/dev/planned/accessway-implementation-sequence.md` steps 1-5.

## What changed

* Added the `AccessPropCleanupPolicy` helper surface with cleanup classes, blocker kinds, diagnostic names, synthetic prop samples, and the temporary one-level mining/dumping terrain-removal threshold.
* Extended `AccessSearchSnapshot` with immutable cleanup metadata keyed by 4x4 terrain origin. Ordinary ground membership remains separate; cleanup origins are exposed only through explicit cleanup lookups and `IsGroundOrCleanupNode`.
* Allowed single-width ground expansion and V/G handoff checks to traverse cleanup-eligible ground tiles, adding the configured `AccessPropCleanupLandscapingCost` through the existing landscaping distance scale when a path enters a cleanup origin.
* Extended search-result and materialization metadata with separate traversal, generated-work, generated-fixed-overhead, tree-cleanup, and dense-debris cleanup counters/collections.
* Added synthetic validation coverage for mixed tree+dense debris cleanup, hard blockers, the stubbed one-level threshold, cleanup snapshot overlay admission, and cleanup metadata preservation during materialization.

## Situation and complications

* The public repository still lacks decompiled vanilla terrain-prop removal details. The threshold helper is intentionally marked and named as stubbed scaffolding rather than gameplay truth.
* The patch does not yet wire live `TerrainPropsManager` enumeration into production snapshot construction. It adds the immutable overlay and synthetic fixtures so the exact prop API can be connected without changing search/materialization callers.
* Tree materialization remains a guarded future integration point. The safe vanilla tree-manager/harvest API was not verified in this environment.
* Cleanup materialization records accepted cleanup metadata separately from generated `V` designations, but live debris mining-designation emission and rollback integration still need to be connected at the runtime placement layer.
* Search charges cleanup when entering an eligible origin from another origin. This satisfies contiguous same-origin traversal in the drafted V1 topology, but should be revisited if later topology permits leaving and re-entering the same cleanup origin through a loop.

## Assumptions made

* Cleanup classes are flags because one 4x4 origin may contain both tree and dense-debris samples.
* Non-removable prop samples are hard blockers.
* Until vanilla rules are sourced, a prop is considered removed by terrain work only when mining lowers or dumping raises the occupied sample by at least one full terrain level in the matching direction.
* Cleanup `G` is metadata over ground traversal, not a new search geometry mode and not a generated `V` profile.
* Cleanup metadata must not feed fixed-profile compatibility, durability sources, fight-invariant checks, or access-provider state.

## Validations pending

* Compile/build in an environment with the .NET SDK and Captain of Industry managed assemblies available.
* Runtime dry-run against saves containing removable debris, trees, mixed origins, existing designations, and disappearing props.
* Verify live prop classification names/properties against decompiled CoI or publicized assemblies.
* Verify safe tree harvest/removal API before enabling tree materialization.
* Verify transaction rollback removes cleanup designations/actions together with generated accessway designations.

## Open questions

* Which vanilla prop proto fields distinguish sparse tree cleanup, dense debris cleanup, removable debris, and true hard blockers in all supported CoI versions?
* Does vanilla remove props at exactly one full level of terrain delta, or are there additional direction/material/footprint thresholds?
* Should tree cleanup and dense-debris cleanup share one configured cost or eventually split into independent knobs?
* Should cleanup-origin cost remain charged once per path, once per contiguous entry, or once per materialized origin if future topology allows loops?
* What dry-run UI/log format should expose `StubbedTerrainRemovalThreshold` strongly enough that users do not mistake it for verified vanilla behavior?

## Next suggested steps

1. Build locally with the CoI managed path configured and fix any API/compiler drift.
2. Wire live prop enumeration into snapshot construction behind the `AccessPropCleanupPolicy` surface.
3. Add production dry-run diagnostics for eligible cleanup origins, hard-blocker origins, and stubbed-threshold origins.
4. Implement guarded debris cleanup mining-designation emission using ATD ownership/rollback tracking.
5. Verify the tree harvest API and keep tree materialization dry-run-only until safe.
6. Capture deterministic save fixtures for the step-5 acceptance cases before moving on to merged-goal Dijkstra work.
