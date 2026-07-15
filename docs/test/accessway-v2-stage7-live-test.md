# Accessway V2 Stage 7 live test

Status: ready for live verification

Stage 7 restores exact ground traversal to the primary route cost and enables A* for V2 tower-ground, fixed-frontage, and combined goals through the request-scoped potential field. Dijkstra remains the optimality reference.

## Trivial down-ramp responsiveness

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Leave `experimentalAccessUseAStar` enabled and select T3, or AUTO resolving to a Mega.
3. Re-run the flat trivial down-ramp that previously explored hundreds of states and timed out.
4. Confirm `[ATD Experimental Access]` and `[ATD V2 Search]` report `algorithm=A*`.
5. Record `cost`, `costs=[travel:...]`, `visited`, `pending`, `searchMs`, and the selected path.

Expected: the request concludes promptly, chooses the direct sensible ramp, and charges both four per full V transition and one per shortest G tile, plus the center spoke at `2 * (1 + generated-flat-cost / 4)`.

## Dijkstra oracle comparison

1. Save immediately before creating designations.
2. Set `experimentalAccessUseAStar` false, restart, and run the identical request.
3. Confirm `algorithm=Dijkstra` and record the same diagnostics.
4. Compare the selected state path, designations, total cost, and cost breakdown with the A* run.

Expected: Dijkstra and A* produce the same accepted plan and exact cost. A* should visit materially fewer states on a search with substantial irrelevant space. Small symmetric cases may differ only in tie-equivalent route orientation.

Observed before the quick-mask optimization: A* passed both the trivial and mining-body ramp tests. The trivial Dijkstra timed out after `180.02s` and `5,490` states. The mining-body Dijkstra completed after `4.15s` and `6,076` states with ten straight states, `groundTravel=9`, `travel=47`, and successful placement validation.

## Situation-pathability quick handoff

1. Repeat the trivial flat down-ramp with Dijkstra and the same timeout used for the earlier comparison.
2. Inspect `[ATD V2 Search] handoffs=[evaluated:...,quickAccepted:...,generalEvaluated:...]` and the selected handoff's `quick=True/False` field.
3. Confirm a flat all-green forward seam reports `quick=True`. The handoff is a local V-to-G transition; it must not require the entry center itself to have a precomputed finite tower-goal distance.
4. Place a prop, cleanup requirement, or projected ray across every eligible center lane and repeat. The quick path must reject it and the general seam solver must either find a valid qualified seam or reject the route.
5. Confirm the summary reports a non-empty `ground=[states:...,travel:...]` suffix, then landscape the route and confirm the resolved Mega remains pathable across the terminal and along that suffix to the tower goal.

## Non-trivial mining-body route

1. Re-enable `experimentalAccessUseAStar` and restart.
2. Generate a full mining body requiring a width-two ramp to tower-reachable ground.
3. Confirm the search remains responsive across frame slices and the resulting ramp passes the Stage 6 placement and Mega-pathability checks.
4. Start another search while one is active and confirm normal single-flight replacement/cancellation behavior remains intact.

## Fixed-provider potential

Provide an exposed adjacent two-origin fixed frontage as the goal and run a short V2 request. Confirm the log reports `algorithm=A*` and reaches the same frontage with the same exact cost as a Dijkstra control. Repeat with both fixed frontages and tower-ground goals present; the cheapest accepted provider must win naturally.

The `[ATD V2 Frontages]` line must report nonzero `providerNodes` and `providerConnected`, and each retained goal sample must include a two-decimal-or-shorter `terminal=` fee. Compare two accepted frontages where the geometrically nearer provider takes a longer established designation route to the tower. A* and Dijkstra must choose by construction/travel plus the logged terminal fee, not by geometric frontage distance alone.

For the Cluster 2 regression, first let Cluster 1 materialize its provider network, then run Cluster 2 with the normal combined goals. Confirm the request reports nonzero fixed frontages and `algorithm=A*`, rather than the former fixed-goal Dijkstra fallback. Record visited states, pending high-water, runtime, selected goal kind, and whether the accepted route connects directly to the Cluster 1 frontage.

## Acceptance gate

Stage 7 passes when the trivial down-ramp no longer times out under A*, A* and Dijkstra reconcile to the same accepted cost/plan on repeatable tower-ground and fixed-frontage cases, combined-goal search selects the cheapest provider, and the non-trivial route remains asynchronously responsive and Mega-pathable.
