# Merge Access Goal Searches and Add Height-Aware A* Heuristic

## Status

**Implemented.** Merged fixed-network and tower-grounded searches, and added the height-aware A* heuristic.

## Context

The experimental accessway search has two closely related destination concepts:

- fixed-network access: a route reaches an already-connected access cluster; and
- tower-grounded access: a route reaches the mine tower / tower-grounded area.

These were treated as separate searches, but the operational scenario is the same in both cases: starting from the work area, find the nearest usable access target, whether that target is an existing connected cluster or the tower itself. Keeping them separate makes the search easier to reason about locally, but it also duplicates work and can force policy decisions before the pathfinder has enough information to compare the real alternatives.

The pathfinder should instead treat both target classes as one set of acceptable goal nodes and let the cost model choose the best reachable destination.

## Proposed Direction

### 1. Merge fixed-network and tower-grounded searches

Status: Done

Build one access search snapshot that contains the union of all goal nodes:

```text
goals = fixedNetworkGoalNodes ∪ towerGroundedGoalNodes
```

A node is a successful terminal node if it belongs to either source set. The reconstructed route should still record which goal class was reached for diagnostics and follow-up behavior, but the path expansion should not run separate passes for the two cases.

Expected benefits:

- The nearest connected cluster and the nearest tower-grounded option compete in the same cost space.
- Search setup, cancellation, diagnostics, and fallback behavior become simpler.
- A* can terminate as soon as it dequeues the cheapest node in the combined goal set.
- Future goal types can be added by extending the goal-node collection instead of adding another search mode.

### 2. Replace tower gravity / hourglass penalties with an admissible heuristic

Status: Abandoned - Inadmissible. Ramps and slopes "cost" the same to traverse.

Do not add heuristic-like penalties to edge costs. Those penalties change the optimization target and can make the selected route worse even when a cheaper valid route exists.

Instead, keep the existing edge costs as the source of truth and guide A* with a lower-bound estimate to the nearest goal:

```text
h(n) = min over goals g of (ManhattanDistance(n, g) + 2 * Abs(height2(n) - height2(g)))
```

Where:

- `height2` is terrain height in half-levels.
- `Abs(height2(n) - height2(g))` is the half-level height difference.
- The `2 * dh` term represents the minimum horizontal travel needed to absorb vertical separation under the vehicle slope limit.

Rationale:

- Vehicles cannot drive on slopes steeper than the supported accessway slope.
- A vertical difference of one half-level requires at least two horizontal tiles of run at the 25% vehicle slope limit.
- Therefore `Manhattan + 2 * dh` should not overestimate the remaining travel cost when movement costs are at least one per horizontal tile.
- Because the heuristic is a lower bound rather than an added route penalty, A* remains aligned with the same objective as the reference Dijkstra search.

### 3. Prune fixed-network goals to exposed boundary V-nodes

Fixed-network access does not need every tile of an already-connected designation as a terminal candidate. The route only becomes connected when the new accessway can touch the existing connected component, so interior fixed-network nodes are redundant for both terminal checks and heuristic evaluation.

For fixed-network goals, collect only V-nodes that have at least one outside edge into traversable/searchable space:

```text
fixedNetworkGoalNodes = { v in connectedDesignationVNodes | HasOutsideEdge(v) }
```

This is an approved optimization with two constraints:

- The outside-edge test must be based on the same adjacency/connectivity model the accessway builder uses to join the new route to the existing designation. A visually exposed tile is not enough if the builder cannot legally connect across that edge.
- Include both outer boundaries and hole boundaries. A separate access cluster can be enclosed inside a hole of another connected designation, so excluding hole-facing edges can remove the only valid fixed-network target for that enclosed cluster. The outside-edge test should mean outside the connected designation component, not outside only the component's exterior perimeter.

This pruning preserves the reachable fixed-network destination set while reducing both the goal lookup and any heuristic index built over fixed-network goals. Tower-grounded goals should use their own reachability rule; this boundary pruning applies only to fixed-network designations. If a boundary edge faces a hole, keep it when the hole is searchable/traversable by the same connectivity rules as any other outside space.

### 4. Use diagonal mod-4 G-goals only after validating the handoff invariant

The mod-4 observation is useful, and the diagonal subset is the most plausible safe G-goal reduction. If every legal V-to-G handoff crosses a four-wide V crest edge, then that handoff footprint should touch at least one G node on the crest's diagonal residue set, such as the local `(0,0)`, `(1,1)`, `(2,2)`, `(3,3)` positions after applying the crest orientation/normal offset. Under that invariant, retaining only those diagonal G goals can preserve reachability while cutting the G-goal count substantially.

This is different from arbitrary checkerboard sampling or a single `(x & 3, y & 3)` bucket. A checkerboard is not tied to the four-wide crest geometry, and a single residue can miss the handoff footprint. The diagonal pattern is approved as the preferred G-goal reduction candidate because it is derived from the V/G transition geometry, but it must be implemented as a geometry rule, not as a global coordinate trick.

Implementation requirements:

- Define the diagonal in the local frame of the V crest/handoff edge. Account for crest orientation and any `(nx, ny)` normal offset before mapping it back to world G coordinates.
- Keep a G goal if it is on at least one legal handoff diagonal for a V crest that can touch that goal. Do not discard a G goal merely because it is off a global `x mod 4 == y mod 4` diagonal.
- Validate the invariant against the same code that enumerates legal V-to-G handoffs. A useful assertion/test is: for every legal handoff footprint, at least one touched G node survives the diagonal filter.
- Until that invariant is proven, keep the full G-goal terminal lookup or run the diagonal filter in diagnostics-only mode and compare it against the full set.
- For the A* heuristic, evaluate against the reduced diagonal set only after the same invariant is proven. Otherwise, use the full goal set or return `h = 0`, because `min` over an unproven subset can overestimate the true nearest goal and break admissibility.

The likely robust path is: fixed-network boundary pruning first, diagonal V/G handoff filtering second, exact duplicate/proven-equivalent deduplication third, then a nearest-goal index over the remaining goals. Spatial buckets/windows, height buckets, cached nearest queries, or a precomputed lower-bound distance field can still be added on top of the diagonal-filtered set.

### 5. Allow sparse tower-ground proxy goals when slight suboptimality is acceptable

For tower-grounded access, the real gameplay destination is usually beyond the mine tower ground area: a processing plant, storage, or another logistics target. If the accessway only needs to reach a representative tower-ground anchor instead of the mathematically nearest ground tile, then defining sparse diagonal tower-ground tiles as the tower-grounded goal set is a valid simplification. This changes the target definition rather than the A* heuristic: the search remains optimal for the selected proxy goals, but it no longer promises the shortest route to any possible tower-ground tile.

Approved policy option:

```text
towerGroundedGoalNodes = diagonalProxyTiles(towerGroundArea)
```

Use this option only for tower-grounded goals, not fixed-network goals. Fixed-network goals represent an existing connected access cluster, so removing legally connectable boundary nodes can disconnect or mis-rank a real network target. Tower-grounded proxy goals are more forgiving because any route that reaches tower ground is only an approximation of the player's downstream logistics destination anyway.

Implementation notes:

- Prefer the same local diagonal pattern used by the V/G handoff rule so the proxy goals line up with likely crest handoffs.
- Keep enough proxy density to avoid obvious artifacts: at minimum, include all diagonal residues through the tower-ground area, and consider also keeping corners/edge midpoints if dry-run comparisons show visibly worse routes.
- Log that a tower-grounded result reached a proxy goal, and include the proxy spacing/pattern in diagnostics so suboptimal choices are explainable.
- Compare against the full tower-ground goal set during rollout. If the route-cost delta is small and search time improves substantially, the proxy-goal policy is acceptable.

### 6. Compute the heuristic against the optimized combined goal set

The heuristic must consider the nearest member of the unioned goal set after safe pruning/projection/proxy selection, not only the tower center:

```csharp
private static float EstimateRemainingCost(AccessSearchNode node, AccessSearchSnapshot snapshot)
{
    int best = int.MaxValue;

    foreach (AccessGoalNode goal in snapshot.GoalNodes)
    {
        int manhattan = Math.Abs(node.CostPosition.X - goal.Position.X)
            + Math.Abs(node.CostPosition.Y - goal.Position.Y);
        int dh2 = Math.Abs(node.Height2 - goal.Height2);
        int estimate = manhattan + 2 * dh2;

        if (estimate < best)
            best = estimate;
    }

    return best == int.MaxValue ? 0f : best;
}
```

If the goal set may become large, precompute or accelerate this calculation before enabling it by default. Correctness is more important than heuristic sharpness; falling back to `0` is equivalent to Dijkstra and is always safe.

## Data Model Sketch

### Goal representation

Add an explicit goal model instead of encoding the destination type in separate search methods:

```csharp
internal enum AccessGoalKind
{
    FixedNetwork,
    TowerGrounded,
}

internal readonly struct AccessGoalNode
{
    public readonly Tile2i Position;
    public readonly int Height2;
    public readonly AccessGoalKind Kind;
}
```

The snapshot should expose:

- `IReadOnlyList<AccessGoalNode> GoalNodes`, containing fixed-network goals after safe boundary pruning, plus tower-grounded goals after either full goal collection or the sparse diagonal proxy policy, then validated diagonal V/G handoff filtering where applicable and any exact duplicate/proven-equivalent deduplication.
- A fast membership lookup keyed by search node identity / tile / height as appropriate for terminal checks.
- Optional counts by `AccessGoalKind` for diagnostics.

### Result representation

The selected path result should include:

- the reached goal node;
- the reached `AccessGoalKind`;
- whether the reached tower-grounded goal was a full goal or sparse proxy goal;
- total route cost;
- whether A* or Dijkstra produced the route; and
- enough debug data to compare behavior against the old split-search implementation during rollout.

## Algorithm Sketch

1. Build the normal access search snapshot.
2. Collect fixed-network goal nodes, keeping only boundary V-nodes that have a legal outside connection edge, including hole-facing boundaries.
3. Collect tower-grounded goal nodes using either the full tower-ground reachability rule or the sparse diagonal proxy policy.
4. Apply diagonal V/G handoff filtering only if the implementation proves that every legal V-to-G handoff touches at least one retained diagonal G node; otherwise keep the full G-goal set for non-proxy goals.
5. Deduplicate exact duplicate goals, or proven-equivalent goals that have the same reachable terminal identity and effective terminal cost, while preserving their combined diagnostic kind if useful.
6. Build the exact terminal-goal lookup from the legal filtered-or-full goal set.
7. Build any heuristic acceleration index from the same goal set.
8. Run one search from the work-area start frontier toward `GoalNodes`.
9. For Dijkstra mode, use `priority = g`.
10. For A* mode, use `priority = g + h`, where `h = min(Manhattan + 2 * dh2)` over the same filtered-or-full legal goal set, an equivalent exact nearest-goal query, or `0` if the accelerated index cannot produce a proven lower bound.
11. When a dequeued node is in the exact goal lookup, terminate and reconstruct the route.
12. Report the reached goal kind in logs / dry-run output.

## Rollout Plan

1. Keep the existing Dijkstra path as the validation baseline.
2. Add the combined goal set behind the experimental accessway code path.
3. Compare route choices and costs against the old fixed-network and tower-grounded split behavior in dry-run logs.
4. If using sparse tower-ground proxy goals, compare them against the full tower-ground goal set and log route-cost deltas before enabling them by default.
5. Enable the height-aware A* heuristic only when the combined Dijkstra result is stable.
6. If necessary, add a setting or debug switch to force Dijkstra or full tower-ground goals for side-by-side investigation.

## Risks and Open Questions

- **Why were the searches originally separated?** Re-check history before implementation. There may have been a practical reason such as different route post-processing, diagnostics, vehicle assignment semantics, or an edge case around tower footprint reachability.
- **Heuristic admissibility depends on units.** Confirm that `height2` really represents half-levels everywhere used by the pathfinder and that one half-level of vertical change requires at least two horizontal tiles of vehicle-drivable run.
- **Heuristic consistency should be verified.** The formula should be admissible, but implementation details such as multi-tile slope expansions, non-unit edge costs, or height sampling could make consistency less obvious. If uncertain, allow node reopening or fall back to Dijkstra.
- **Goal-set size may make naive heuristic evaluation expensive.** The `min over goals` loop is simple and safe, but it may need fixed-network boundary pruning, sparse tower-ground proxy goals, validated diagonal V/G handoff filtering, exact duplicate/proven-equivalent deduplication, spatial indexing, bucketing by height, cached nearest queries, or precomputed lower-bound distance fields if the combined goal set is large.
- **Fixed-network interior goals are unnecessary work, but hole boundaries are still boundaries.** Treat only legally connectable boundary V-nodes as fixed-network goals, and include hole-facing boundaries because another access cluster can be enclosed inside a hole of the connected component.
- **Sparse tower-ground proxy goals are a policy tradeoff, not an admissibility proof.** They simplify the tower-ground destination and can be acceptable because the real gameplay destination is beyond tower ground, but they intentionally stop optimizing against every tower-ground tile. Track route-cost deltas against the full tower-ground goal set.
- **Diagonal mod-4 filtering depends on the handoff invariant.** The diagonal pattern appears safe if every legal V-to-G handoff footprint touches at least one retained diagonal G node in the crest-local frame. Validate that invariant against the handoff enumerator; otherwise keep the full G-goal set or use Dijkstra/`h = 0` when uncertain.
- **Deduplication can hide useful diagnostics.** A node may be both fixed-network and tower-grounded. The terminal check can deduplicate for performance, but logs should still be able to explain all goal classifications attached to the reached node.
- **Route post-processing may still differ by goal kind.** Even if the search is unified, follow-up behavior may need to branch based on whether the route reached an existing connected cluster or the tower-grounded target.
- **Disconnected or empty goal sets need clear fallback behavior.** If neither fixed-network nor tower-grounded goals are available, the search should fail early with a diagnostic that distinguishes missing goals from exhausted search space.
- **The heuristic should not become a hidden policy knob.** Avoid configurable gravity/penalty weights unless there is a separate route-preference feature. A* guidance should preserve the same optimal route according to the underlying cost model.
