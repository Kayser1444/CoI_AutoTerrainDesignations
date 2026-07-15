# Accessway V2 Stage 6 live test

Status: ready for live verification

Stage 6 enables real V2 placement. Use a disposable save or retain a save from immediately before the test.

## Primary placement test

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Reuse the accepted Stage 5 setup: explicit T3 (or AUTO resolving to a Mega), one inaccessible terrain-work origin, ordinary tower-reachable ground, and no separate fixed frontage.
3. Click **Create Designations** and allow placement to finish.

Expected:

* V2 Search reports `algorithm=A* success=True` with a non-`none` handoff for a tower-ground-only request. A request containing a fixed-provider goal reports `algorithm=Dijkstra`.
* `[ATD Experimental Access Plan]` reports `valid=True` and a plausible width-two designation count.
* The visible accessway is two origins wide. Each non-no-op origin appears only once.
* Terminal lanes use the operations reported by the V2 handoff. Equal operations normally look uniform; a mixed seam may visibly use different mining/dumping prototypes by lane.
* `[ATD V2 Placement Validation]` reports `success=True reason=ValidatedProfilesAndMegaSeam`.
* The cluster is accepted as provided rather than rolled back or sent to legacy fallback.

## Ownership and clear test

1. Before terrain work begins, click the tower trash-can without Shift.
2. Confirm that every V2 terrain designation, V2 debris-cleanup designation, and newly added V2 tree-harvest marker is removed.
3. Confirm that the original player terrain-work target remains and unrelated tree-harvest markers remain.

## Full mining-body integration test

1. Generate a non-trivial ATD mining body that requires a width-two accessway.
2. Prefer a route boundary with removable debris near the likely crest, so prop cleanup and generated terrain work overlap.
3. Confirm V2 placement succeeds without `DesignationAppeared` after the search result.
4. If the selected prop's canonical origin is a generated V2 terrain-work origin, confirm the cleanup diagnostic increments `coveredByTerrainWork` instead of placing a separate prop-removal designation there.
5. Confirm a removable prop on terrain that remains non-pathable for the resolved vehicle after removal is not accepted as a G handoff or escape lane. V2 must extend or choose another seam rather than emit a redundant side-lane terminal designation.
6. After landscaping, display T3 pathability and confirm every retained contact and escape-corridor center remains green across the projected five-by-five Mega footprint. The escape must continue until the whole mask, not only its center, is clear of projected terrain work. A nearby natural pit must reject any seam that would leave a narrow post-work waist. The canonical-center spoke remains a cost/heuristic abstraction, not additional physical cells; it costs `2 * (1 + generated-flat-cost / 4)`.
7. On otherwise equal trivial ramps, confirm the joint search minimizes the complete V+G route. The summary reports `ground=[states:...,travel:...]`; cardinal G transitions cost one and diagonal transitions cost `sqrt(2)`, and every transition must be included in the primary `travel` and total `cost`. Diagonal routes must not squeeze between orthogonally adjacent blockers.
8. When the natural-ground boundary is exactly level but the ramp-side edge uniformly requires excavation or fill, confirm the terminal lanes use mining or dumping rather than leveling.

## Failure/rollback check

If convenient, start another V2 search and place or change a conflicting designation before materialization/placement completes. A replay or placement failure must leave no partial V2 terrain designations, cleanup designations, harvest markers, or registered ownership. The log should report the rejection or rollback reason.

## Acceptance gate

Stage 6 passes when the primary route is visibly width two, placement validation succeeds, the ordinary clear action removes exactly ATD-owned work, and no partial state remains after an induced or naturally occurring failed placement.
