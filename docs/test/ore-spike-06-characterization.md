# Ore spike sample 6 characterization

Date: 2026-09-01

## Provenance

The maintainer identified small spikes in a deposit, excavated it using Medium
ore quality without a spike filter, then captured the remaining terrain with
Ore quality Off. Medium therefore followed the unnecessarily deep unfiltered
designation before this replay. The surviving spike evidence is expected to be
shorter and to have much less neighboring ore support than it had initially.

The completed replay contains 44,797 captured columns with bedrock and 2,450
columns containing selected target product. The Off/Bottom-flattening-Off
experimental baseline contains 468 designations.

The intended pre-excavation pair is unavailable. `ore-spike-05` was armed once,
captured 45,182 columns, and planned 1,550 designations at 06:11:36. Its
background recording was aborted at 06:12:30 when another mining request
started. A replay arm is consumed when recording begins and is not restored by
cancellation; the log contains no second `ore-spike-05` arm, so subsequent
Create Designations attempts did not record that name.

A later pre-excavation `ore-spike-06` attempt captured 44,339 columns and
planned 1,378 designations at 06:13:36, but was also aborted at 06:13:42. The
published sample 6 was explicitly re-armed after Medium excavation, captured
44,797 columns, planned 468 designations, and completed normally.

## Ore-neighborhood result

At residuals 4, 6, 8, and 10, the strict, median, and morphology-gated filters
make zero corrections. Requiring ore in all eight neighbors is therefore not a
viable mop-up detector: excavation removed the evidence it depends on.

## Bedrock-neighborhood result

A bedrock-neighborhood candidate compares the center bedrock boundary with all
eight physical neighbors and applies the excess displacement to the derived ore
bottom. Raw terrain remains unchanged and supplies the material score.

| Candidate | Raw corrections | Final designations | Rock avoided | Target omitted | Changed excavation columns |
|---|---:|---:|---:|---:|---:|
| bedrock-r10 | 1 | 468 | 0 | 0 | 0 |
| bedrock-r8 | 12 | 468 | 0 | 0 | 0 |
| bedrock-r6 | 15 | 466 | 46.38 | 1.08 | 32 |
| bedrock-r4 | 32 | 465 | 194.49 | 2.97 | 174 |

The r8 and r10 candidates see residual bedrock outliers but do not change the
mine plan, so they cannot remove any harmful spike still visible in this plan.
The r6 result avoids about 42.8 rock per target product; r4 avoids about 65.5.
These ratios are far below the untouched-deposit results, confirming that
partial excavation makes the correction substantially harder.

## Viewer-confirmed spikes

Only two r6 source corrections change their own final target surface:

- `1497,1715`
- `1528,1687`

The r4 result also changes the source targets at:

- `1537,1721`
- `1501,1766`
- `1442,1668`
- `1533,1719`

The maintainer inspected all six coordinates in the in-game viewer and
confirmed that they appear to be legitimate vanilla spikes. On the
plan-affecting sources in this case, bedrock-r4 therefore finds 6 of 6 confirmed
spikes, r6 finds 2 of 6, and r8/r10 find none. This is source recall within one
known case, not a general false-positive estimate.

## Current conclusion

The production detector cannot require an intact ore neighborhood if it must
work for mop-up. Bedrock continuity is the only tested signal that survives
this excavation. Residual 4 is required to catch all six confirmed
plan-affecting spikes here; r6 misses four. Bedrock-r4 still needs the sample 4
control coordinates and other legitimate bedrock relief checked before
adoption.
