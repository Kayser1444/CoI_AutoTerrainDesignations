# Merge Access Goal Searches and Add Height-Aware A* Heuristic

## Status

Planned design revision. This supersedes the earlier tower-gravity/hourglass-penalty idea.

## Context

The experimental accessway search has two closely related destination concepts:

- fixed-network access: a route reaches an already-connected access cluster; and
- tower-grounded access: a route reaches the mine tower / tower-grounded area.

These were treated as separate searches, but the operational scenario is the same in both cases: starting from the work area, find the nearest usable access target, whether that target is an existing connected cluster or the tower itself. Keeping them separate makes the search easier to reason about locally, but it also duplicates work and can force policy decisions before the pathfinder has enough information to compare the real alternatives.

The pathfinder should instead treat both target classes as one set of acceptable goal nodes and let the cost model choose the best reachable destination.

## Proposed Direction

### 1. Merge fixed-network and tower-grounded searches

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

### 3. Compute the heuristic against the combined goal set

The heuristic must consider the nearest member of the unioned goal set, not only the tower center:

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

- `IReadOnlyList<AccessGoalNode> GoalNodes`, containing both fixed-network and tower-grounded goals.
- A fast membership lookup keyed by search node identity / tile / height as appropriate for terminal checks.
- Optional counts by `AccessGoalKind` for diagnostics.

### Result representation

The selected path result should include:

- the reached goal node;
- the reached `AccessGoalKind`;
- total route cost;
- whether A* or Dijkstra produced the route; and
- enough debug data to compare behavior against the old split-search implementation during rollout.

## Algorithm Sketch

1. Build the normal access search snapshot.
2. Collect fixed-network goal nodes.
3. Collect tower-grounded goal nodes.
4. Deduplicate nodes that appear in both sets while preserving their combined diagnostic kind if useful.
5. Run one search from the work-area start frontier toward `GoalNodes`.
6. For Dijkstra mode, use `priority = g`.
7. For A* mode, use `priority = g + h`, where `h = min(Manhattan + 2 * dh2)` over all goal nodes.
8. When a dequeued node is in the goal lookup, terminate and reconstruct the route.
9. Report the reached goal kind in logs / dry-run output.

## Rollout Plan

1. Keep the existing Dijkstra path as the validation baseline.
2. Add the combined goal set behind the experimental accessway code path.
3. Compare route choices and costs against the old fixed-network and tower-grounded split behavior in dry-run logs.
4. Enable the height-aware A* heuristic only when the combined Dijkstra result is stable.
5. If necessary, add a setting or debug switch to force Dijkstra for side-by-side investigation.

## Risks and Open Questions

- **Why were the searches originally separated?** Re-check history before implementation. There may have been a practical reason such as different route post-processing, diagnostics, vehicle assignment semantics, or an edge case around tower footprint reachability.
- **Heuristic admissibility depends on units.** Confirm that `height2` really represents half-levels everywhere used by the pathfinder and that one half-level of vertical change requires at least two horizontal tiles of vehicle-drivable run.
- **Heuristic consistency should be verified.** The formula should be admissible, but implementation details such as multi-tile slope expansions, non-unit edge costs, or height sampling could make consistency less obvious. If uncertain, allow node reopening or fall back to Dijkstra.
- **Goal-set size may make naive heuristic evaluation expensive.** The `min over goals` loop is simple and safe, but it may need spatial indexing, bucketing by height, or precomputed distance fields if the combined goal set is large.
- **Deduplication can hide useful diagnostics.** A node may be both fixed-network and tower-grounded. The terminal check can deduplicate for performance, but logs should still be able to explain all goal classifications attached to the reached node.
- **Route post-processing may still differ by goal kind.** Even if the search is unified, follow-up behavior may need to branch based on whether the route reached an existing connected cluster or the tower-grounded target.
- **Disconnected or empty goal sets need clear fallback behavior.** If neither fixed-network nor tower-grounded goals are available, the search should fail early with a diagnostic that distinguishes missing goals from exhausted search space.
- **The heuristic should not become a hidden policy knob.** Avoid configurable gravity/penalty weights unless there is a separate route-preference feature. A* guidance should preserve the same optimal route according to the underlying cost model.
