# Ore spike sample 7 characterization

Date: 2026-09-01

## Provenance

This is the first successfully published intact/post-Medium pair for the same
deposit. Both captures use Ore quality Off and Bottom flattening Off. Between
them, the maintainer submitted the mine with Medium quality and allowed that
designation to be excavated before recording the mop-up.

The manifests contain the newly added `mapName` field, but it is empty because
the first runtime implementation checked only the legacy island-map sources.
The session log identifies the world-region map loaded before both captures as
`New Haven`. Capture manifests remain immutable; this fact is recorded here
instead of retroactively editing them. Future builds also check
`LoadedWorldMapName`.

Both cases reproduce their recorded canonical plans exactly through the
candidate DLL:

| Case | Captured columns with bedrock | Target-bearing columns | Recorded plan |
|---|---:|---:|---:|
| Intact | 41,048 | 22,940 | 1,360 designations |
| Post-Medium mop-up | 41,725 | 2,598 | 446 designations |

## Experimental boundary

Changing the intact request to Ore quality Off admits an edge component that
the recorded Medium plan did not require. Rectilinear hull filling reaches one
terrain cell outside the captured counterfactual facts, so the normal
`DirectSafety` experimental arena fails closed at `(1394,1646)`.

For this pair only, the sweep compares planner `Body` geometry followed by the
production corner-building and smoothing method. It excludes direct protected
origin removal as well as exterior safety rays. Raw captured layers still score
the material tradeoff. The recorded, unchanged cases themselves replay exactly;
this limitation applies only to the changed-policy counterfactual.

## Mop-up result

The mop-up reproduces sample 6's bedrock results exactly:

| Candidate | Raw corrections | Final designations | Rock avoided | Target omitted | Changed excavation columns |
|---|---:|---:|---:|---:|---:|
| bedrock-r4 | 32 | 443 | 194.49 | 2.97 | 174 |
| bedrock-r6 | 15 | 444 | 46.38 | 1.08 | 32 |
| bedrock-r8 | 12 | 446 | 0 | 0 | 0 |
| bedrock-r10 | 1 | 446 | 0 | 0 | 0 |

The six plan-affecting r4 sources are exactly the six previously confirmed
spikes from sample 6: `(1442,1668)`, `(1497,1715)`, `(1501,1766)`,
`(1528,1687)`, `(1533,1719)`, and `(1537,1721)`. This is useful
reproducibility evidence but supplies no false-positive candidate.

## Intact result

| Candidate | Raw corrections | Final designations | Rock avoided | Target omitted | Changed excavation columns |
|---|---:|---:|---:|---:|---:|
| bedrock-r4 | 180 | 1,526 | 1,926.60 | 0.38 | 2,477 |
| bedrock-r6 | 99 | 1,526 | 49.61 | 0 | 82 |
| bedrock-r8 | 66 | 1,526 | 0 | 0 | 0 |
| bedrock-r10 | 16 | 1,526 | 0 | 0 | 0 |

Only `(1528,1687)` changes its own final target at r6. At r4, 27 source
targets change. Five were already viewer-confirmed from the post-Medium case:
`(1442,1668)`, `(1501,1766)`, `(1528,1687)`, `(1533,1719)`, and
`(1537,1721)`. The sixth mop-up spike, `(1497,1715)`, is detected raw but has
no source-target effect in the intact smoothed plan.

The remaining 22 r4 plan-affecting sources need viewer classification:

- West pair: `(1421,1670)`, `(1424,1666)`
- Main cluster: `(1438,1657)`, `(1441,1656)`, `(1444,1654)`,
  `(1446,1654)`, `(1452,1654)`, `(1452,1655)`, `(1459,1664)`,
  `(1460,1662)`, `(1461,1661)`, `(1463,1660)`, `(1464,1667)`,
  `(1465,1660)`, `(1465,1664)`, `(1471,1674)`, `(1471,1676)`,
  `(1473,1678)`, `(1475,1677)`
- Northern outlier: `(1469,1649)`
- Eastern pair: `(1475,1659)`, `(1475,1663)`

These labels determine whether bedrock-r4's large intact-case saving is useful
spike removal or aggressive correction of legitimate bedrock relief.

## In-game review markers

The laboratory writes `.scratch/ore-spike-review-markers.csv` in the mod root.
After loading the matching world state, run:

```text
atd_ore_spike_review_markers
```

Yellow markers need classification, green markers are confirmed spikes that
affect the intact plan, and cyan marks a confirmed spike whose source target is
changed only in the mop-up plan. Hovering a marker shows its coordinate and
captured ore-bottom, bedrock, and r4 cutoff values. Run
`atd_ore_spike_review_markers clear` to remove them. The overlay is transient
and is cleared on world changes; it does not enter saves.
