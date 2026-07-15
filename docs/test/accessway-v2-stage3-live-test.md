# Accessway V2 Stage 3 live test

Purpose: validate width-two start and fixed-frontage discovery before V2 search or placement is enabled.

## Setup

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Use **T3**, or AUTO resolving to a Mega excavator.
3. Use an ordinary generated mining edge first. If convenient, also retain a one-origin, unfulfilled player terrain designation in the tower's managed area as an external access target and two adjacent fixed origins elsewhere as a potential provider. The endpoint may be far from the tower building; "managed area" means the tower's full editable rectangle.

## Action

Run **Create Designations** and let the diagnostic request conclude.

## Expected result

* `[ATD V2 Ground Graph] vehicleWidth=5` still appears.
* `[ATD V2 Frontages]` appears before the search result.
* A normal exposed mining edge reports `starts` greater than zero.
* A one-origin, unfulfilled external endpoint can report `syntheticStarts` greater than zero; discovery does not place that companion.
* Compatible adjacent work may report `existingPairStarts`; accessible adjacent fixed goals may report `fixedFrontages`.
* Rejected alternatives are summarized by categories such as `Building`, `Durability`, `FightInvariant`, `StartFrontageNotExposed`, or `OutOfAreaFrontage`.
* A structurally valid request still concludes deliberately with `V2GraphNotEnabled`.
* A request with no feasible companion concludes with `NoWidth2StartCompanion`.
* The mutation audit reports zero additions, removals, and changes. No terrain, cleanup, or harvest designation is placed by V2.

## Useful log extraction

```powershell
.\tools\get-mod-log.ps1 -Last 160
```

Capture the V2 ground-graph, frontage, result, and mutation-audit rows. Stage 4 should not begin until the discovered counts and samples match the visible mining edge.

## Result

Passed 2026-07-14. An exposed one-origin endpoint produced synthetic starts without mutation. Blocking every adjacent companion produced `NoWidth2StartCompanion`, confirming the positive and negative frontage-discovery paths.
