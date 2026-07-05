# Accessway Implementation Sequence

Status: planning stack for the next experimental accessway work.

This plan keeps the original order: props/debris behavior first, merged search
second, A* heuristic third, and clearance 2+ after the single-width behavior is
stable. Each block should be drafted as a separate branch that can be reviewed
and merged onto the previous branch. Later branches should not rewrite earlier
branch decisions except to fix defects found during review.

## Baseline assumptions

* The decompiled CoI source is not available in this public repository. Branches
  that depend on exact vanilla prop-removal behavior should introduce narrow ATD
  helper methods and keep the currently unknown values isolated there.
* Until the exact mining/dumping thresholds are verified from the decompiled
  source, stub the helper policy with a conservative one-level threshold:
  mining or dumping removes a prop only when the planned terrain delta for the
  relevant prop-occupied sample reaches at least one full level in the matching
  direction. When the real rules are known, update the helpers and their tests
  without changing callers.
* Treat the helper policy as implementation scaffolding, not gameplay truth. Dry
  run diagnostics should report when a route relies on a stubbed threshold so the
  behavior is easy to revisit.
* Keep cleanup `G` admission separate from generated `V` profile compatibility.
  Cleanup metadata may influence route cost and materialization, but it must not
  become a fixed profile, durability source, fight-invariant neighbor, or access
  provider.

## Branch stack

### 1. `access-prop-policy-helpers`

Goal: establish the narrow policy surface for prop cleanup/removal without
changing production routing.

Scope:

* Add prop-classification and removal-policy helpers used by accessway code.
* Represent cleanup classes as a set, so one origin can carry both tree and
  dense-debris metadata.
* Add the temporary one-level mining/dumping threshold helpers for unknown
  vanilla removal behavior.
* Add or use the public global `accessPropCleanupLandscapingCost` parameter as the
  cleanup material-cost knob instead of hard-coding the default `6`
  landscaping-cost value in the route search.
* Add diagnostics names for `ClearGround`, `TreeCleanup`, `DenseDebrisCleanup`,
  `HardBlocker`, and `StubbedTerrainRemovalThreshold`.

Acceptance:

* Existing access search behavior is unchanged when helpers are unused.
* Helper tests or synthetic fixtures cover removable debris, tree cleanup,
  mixed cleanup classes, non-removable blockers, and the stubbed one-level
  mining/dumping policy.

### 2. `access-prop-snapshot-overlay`

Goal: collect immutable cleanup metadata in the access snapshot while preserving
ordinary vanilla `G` nodes.

Scope:

* Snapshot prop-occupied terrain tiles and map them to 4x4 cleanup origins.
* Compute `propCleanupByOrigin`, cleanup-eligible origins, and hard-blocker
  reasons using the helper policy from branch 1.
* Keep ordinary `G` construction unchanged for already-pathable tiles.
* Add diagnostics for eligible cleanup origins, blocked origins by hard-blocker
  reason, and any origin classified with stubbed threshold assumptions.

Acceptance:

* No route changes yet unless a debug fixture directly inspects snapshot
  metadata.
* Snapshot metadata excludes buildings, active terrain designations, ocean,
  durability-blocked terrain, source work origins, and out-of-area footprints.

### 3. `access-cleanup-g-search`

Goal: admit cleanup-eligible single-width ground candidates as cleanup `G` and
cost them correctly.

Scope:

* Add cleanup `G` edge/path metadata without introducing a new geometry mode.
* Charge cleanup cost once per 4x4 cleanup origin, even when the path crosses
  multiple tiles in that origin.
* Preserve separate cost counters for traversal, generated `V` work, generated
  `V` fixed overhead, tree cleanup, and dense-debris cleanup.
* Keep non-cleanup hard blockers impassable.

Acceptance:

* A flat corridor blocked by one removable non-tree debris origin routes through
  cleanup `G` instead of a generated `V` bypass.
* A sparse forest route can choose cleanup/harvest `G` metadata instead of a
  straight generated `V` road when the cost model says that is cheaper.
* Non-removable blockers and origins with existing terrain designations remain
  impassable for cleanup `G`.

### 4. `access-cleanup-materialization`

Goal: materialize accepted cleanup metadata independently from generated `V`
accessway terrain designations.

Scope:

* Emit debris cleanup mining designations using the existing one-level-above-
  surface cleanup profile.
* Mark tree cleanup through the tree manager/harvest path once the exact safe
  API is verified; until then keep tree materialization behind a guard or dry-run
  diagnostic if the API is unavailable in the current build environment.
* Revalidate cleanup origins before placement and drop no-longer-needed cleanup
  when props disappeared.
* Roll back cleanup designations/actions together with generated `V` work when
  the accessway transaction fails.

Acceptance:

* Cleanup designations are registered for ATD ownership/rollback but never feed
  back into fixed-profile, durability, fight-invariant, or provider state.
* Rematerialization rejects newly conflicting designations and reports why.
* Existing save-removability constraints remain intact.

### 5. `access-prop-stability-fixtures`

Goal: stabilize the props/debris behavior before changing search topology.

Scope:

* Add deterministic fixtures for sparse forest, single debris origin, multiple
  tiles in the same cleanup origin, mixed tree-plus-debris origin, non-removable
  blocker, existing terrain designation conflict, zero-work generated `V` over
  props, disappearing cleanup, and rollback.
* Add dry-run diagnostics that compare selected cleanup costs against generated
  landscaping costs.

Acceptance:

* Props/debris fixtures pass with the current split-search topology.
* The branch becomes the baseline for comparing merged-search and A* behavior.

### 6. `access-merged-goals-dijkstra`

Goal: merge fixed-network and tower-grounded destination searches while keeping
Dijkstra as the validation baseline.

Scope:

* Introduce explicit goal kinds and a combined terminal-goal lookup.
* Collect fixed-network goals and tower-grounded goals into one goal set.
* Preserve diagnostics for which goal kind was reached.
* Keep `priority = g`; do not enable the height-aware heuristic in this branch.

Acceptance:

* Combined Dijkstra can reproduce or intentionally improve the old split-search
  choice with clear route/cost diagnostics.
* Empty or disconnected goal sets fail early with a diagnostic that distinguishes
  missing goals from exhausted search space.

### 7. `access-goal-pruning`

Goal: reduce the combined goal set only where reachability is preserved or the
policy explicitly accepts proxy-goal suboptimality.

Scope:

* Prune fixed-network goals to legal exposed boundary V-nodes, including
  hole-facing boundaries.
* Add diagonal V/G handoff filtering only in diagnostics mode until the handoff
  invariant is proven.
* Optionally add sparse tower-ground proxy goals as a policy mode, clearly logged
  as a proxy target rather than the full tower-ground goal set.

Acceptance:

* Fixed-network pruning preserves every legally connectable destination.
* Diagonal filtering is enabled only after fixtures prove every legal V/G handoff
  touches a retained diagonal goal.
* Proxy tower-ground goals report route-cost deltas against full goals during
  rollout.

### 8. `access-height-aware-astar`

Goal: add the height-aware A* heuristic after combined Dijkstra is stable.

Scope:

* Implement `h = min(ManhattanDistance + 2 * Abs(height2 delta))` against the
  same legal goal set used by terminal checks.
* Use Dijkstra (`h = 0`) as the fallback whenever the heuristic index cannot
  prove a lower bound.
* Add a debug switch or setting to force Dijkstra for side-by-side comparison.

Acceptance:

* A* selects the same route and cost as combined Dijkstra on deterministic
  fixtures unless an explicitly documented proxy-goal policy changes the target
  definition.
* Diagnostics report expansion counts and selected goal information for both
  Dijkstra and A* comparison runs.

### 9. `access-clearance-2-props`

Goal: extend cleanup routing to clearance 2+ only after single-width cleanup,
merged goals, and A* behavior are stable.

Scope:

* Evaluate cleanup `G` over the full vehicle footprint or ATD clearance brush.
* Charge cleanup cost once per cleanup origin even when wide footprints touch
  multiple tiles in that origin.
* Reject any wide footprint lane that contains a non-cleanup blocker.
* Prove V2+ G/V handoff width and prop legality across the whole exposed seam.

Acceptance:

* Clearance-2 fixtures validate the whole mega-vehicle footprint.
* Mixed wide footprints preserve dense-debris classification whenever any lane
  requires dense-debris cleanup.
* Sparse forest preference remains explainable for mega vehicles without relying
  on single-tile pathability assumptions.

## Merge discipline

* Prefer stacked branches in the order above. Each branch should be mergeable on
  top of the previous one and should keep its public surface small.
* Do not combine props/debris semantics, merged goal topology, heuristic
  acceleration, and clearance 2+ in one PR. Those changes affect different risk
  axes and need separate diagnostics.
* Every branch that changes routing behavior should add or update deterministic
  fixtures before enabling the behavior in production routing.
* If exact vanilla prop-removal thresholds become available mid-stack, update the
  helper-policy branch first, then rebase or merge subsequent branches over that
  corrected policy surface.
