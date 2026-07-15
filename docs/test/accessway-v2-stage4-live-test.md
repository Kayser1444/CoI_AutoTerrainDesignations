# Accessway V2 Stage 4 live test

Purpose: validate the width-two production Dijkstra graph and cost/history integration without enabling V2 placement.

## Setup

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Select **T3**, or AUTO resolving to a Mega excavator.
3. Keep one terrain-work cluster inaccessible so it becomes the search start.
4. Provide a separate, tower-accessible fixed provider made from two adjacent compatible terrain-designation origins with an exposed two-origin edge. A short, flat, unobstructed arrangement is the clearest first test.

Without an accessible fixed frontage, Stage 4 correctly reports `V2NoFixedFrontageGoal`: ground handoffs are introduced in Stage 5.

## Action

Run **Create Designations** and let the dry-run search conclude. Do not expect an accessway to appear.

## Expected result

* `[ATD V2 Ground Graph] vehicleWidth=5` appears.
* `[ATD Experimental Access Width]` reports `resolvedVehicleWidth=5 requiredWidth=2`; the legacy ramp-width field does not control V1/V2 dispatch.
* `[ATD V2 Frontages]` reports `starts` greater than zero and `fixedFrontages` greater than zero.
* `[ATD V2 Search] algorithm=Dijkstra success=True` reports nonzero states, generated origins, cost, and visited nodes.
* `[ATD V2 Search Path]` shows a deterministic sequence of width-two band states. A simple flat case should remain on one axis; suitable geometry may exercise a strafe or flat turn.
* The ordinary search result concludes with `reason=V2DryRunRouteFound` and `success=False`. This is the deliberate materialization guard, not a route failure.
* The mutation audit reports zero additions, removals, and changes. No terrain, cleanup, or harvest designation is placed by V2.
* History and ray high-water counts remain plausible for the reported route rather than growing independently of it.

## Useful log extraction

```powershell
.\tools\get-mod-log.ps1 -Last 200
```

Capture the V2 ground graph, frontages, Dijkstra summary/path, ordinary result, and mutation audit. Stage 5 should not begin until a fixed-frontage route is found without mutations.

## Result

Passed 2026-07-14. AUTO resolved a fleet Mega to `resolvedVehicleWidth=5 requiredWidth=2`; six starts and ten fixed frontages were discovered. Dijkstra found a 16-state, 23-origin route with seven straight and eight strafe transitions. The origin count reconciled exactly with delta ownership, and the mutation audit reported zero additions, removals, or changes.
