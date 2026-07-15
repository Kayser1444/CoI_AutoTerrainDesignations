# Accessway V2 Stage 5 live test

Status: primary live gate passed 2026-07-14

Purpose: verify that width-two Dijkstra can end at tower-reachable Mega ground through a real two-lane G/V seam. This stage is still a dry run and must place no terrain or cleanup designations.

## Primary ground-handoff test

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Select **T3**, or use AUTO with a Mega excavator in the resolved vehicle pool.
3. Place one inaccessible terrain-work origin on terrain that has an uncomplicated ramp route to ordinary tower-reachable ground.
4. Do not provide a separate accessible two-origin terrain-designation frontage. This makes tower ground, rather than a fixed provider, the intended goal.
5. Click **Create Designations** and wait for the dry run to conclude.

Expected:

* `[ATD Experimental Access Width]` reports `requiredWidth=2`.
* `[ATD V2 Frontages]` may report zero fixed frontages; this is no longer an immediate failure when a ground seam is available.
* `[ATD V2 Search]` reports `algorithm=A* success=True` and ends with a non-`none` handoff such as `handoff=exit=(4,0) span=1 ops=Mining/Mining contacts=...`.
* The cost summary includes the two-cost handoff spoke in `travel` and any newly required seam prop removal in `cleanup`.
* The result remains `V2DryRunRouteFound` and produces zero terrain, cleanup, or harvest mutations.

## Follow-up geometry checks

Repeat where convenient:

* A diagonal natural-ground seam where the two lane origins may select different terminal operations.
* A route that can leave through the side of two consecutive aligned band rows; expect a lateral `exit` direction.
* A crest that requires two or three consecutive rows before both lane contacts connect; expect `span=2` or `span=3`.
* A visually broad exit divided by an impassable strip. It must not be accepted unless both contacts belong to the same tower-goal Mega component.
* A removable prop at one seam contact. The route may be accepted with one deduplicated cleanup charge; a hard blocker must reject it.

## Acceptance gate

Stage 5 is accepted when the primary test finds a plausible ground-terminal route, the reported handoff agrees with the visible terrain, and the dry run leaves the world unchanged. Any accepted seam that lacks five-tile clearance, joins different ground components, or reports only one workable lane blocks Stage 6.

## Recorded primary result

The explicit T3 test passed with `success=True`, three band states, four generated origins, and 22 visited states. The route used one straight and one strafe transition, then `exit=(4,0) span=1 ops=Leveling/Leveling`. Travel cost was `10`: four per generated transition plus the required two-cost center spoke. Direct work was `40`, fixed generated-origin overhead `4`, exterior rays `0.47`, cleanup `0`, and total cost `54.47`. The mutation audit reported one designation before and after with no additions, removals, or changes.
