# Accessway V2 Stage 2 live test

Purpose: validate the concrete Mega/T3 ground snapshot before V2 start formation or search is enabled.

## Setup

1. Confirm the latest Debug DLL is loaded by comparing the `[ATD]` startup `dll:` timestamp with `AutoTerrainDesignations.dll`.
2. Use a mine tower with experimental accessway pathfinding enabled.
3. Select **T3** clearance, or use AUTO with a Mega excavator in the resolved vehicle pool.
4. Prefer an area containing ordinary open ground, at least one tree or removable rock, and a building or other hard obstruction.

## Action

Run **Create Designations** once and let the diagnostic request finish.

## Expected result

* No V2 accessway, cleanup designation, or harvest marker is placed.
* The request no longer stops at `ExperimentalAccesswayWidthInsufficient`.
* The log contains `[ATD V2 Ground Graph] vehicleWidth=5` with non-negative values for:
  * `pathableCenters`;
  * `towerReachableCenters`;
  * `sparseTowerGoals`;
  * `cleanupEligibleCenters` and `cleanupBlockedCenters`; and
  * `distinctCleanupObjects`.
* On terrain that a small excavator can traverse but the Mega cannot, the exclusion summary contains `T1Only`; other impassable terrain remains `NotPathable`.
* The search session concludes deliberately with `V2GraphNotEnabled`.
* There is no `V2GeometrySelfTest` failure.
* The experimental mutation audit reports no added, removed, or changed terrain designations.

## Useful log extraction

```powershell
.\tools\get-mod-log.ps1 -Last 120
```

Capture the `[ATD V2 Ground Graph]`, search-result, and mutation-audit rows. Stage 3 should not begin until the graph counts look plausible around the selected tower and the mutation audit is clean.
