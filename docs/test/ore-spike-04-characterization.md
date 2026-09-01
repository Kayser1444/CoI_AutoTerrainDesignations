# Ore spike sample 4 characterization

Date: 2026-09-01

## Provenance and role

The maintainer recorded this case during a high ore-quality mop-up run after
most of the deposit had already been excavated. No spike has been confirmed in
the in-game viewer. Until one is identified, this case is a **provisional
negative operational control**, not a positive spike sample.

The replay contains 40,083 captured columns with a bedrock boundary, including
13,891 columns containing a selected target product. The filter experiment
overrides the recorded planning policy to Ore quality Off and Bottom flattening
Off so it remains comparable with the detector-selection arena.

## Existing neighbor-ore candidates

The strict, median, and profile candidates originally require target ore in all
eight neighboring columns. This assumption is weak in a mop-up scenario because
the player has intentionally removed much of that neighborhood.

At allowed residuals 8 and 10, all three candidates make zero corrections and
leave the final plan unchanged. More aggressive median thresholds do act:

| Candidate | Corrected sources | Rock avoided | Target omitted | Changed excavation columns |
|---|---:|---:|---:|---:|
| median-r5 | 1 | 802.3 | 0 | 432 |
| median-r4 | 4 | 939.6 | 0.44 | 432 |
| median-r3 | 10 | 981.1 | 0.69 | 432 |

These are not benefits unless an affected source is confirmed as a spike. The
case demonstrates why a high rock-to-product ratio alone cannot establish a
true positive.

## Bedrock-neighborhood experiment

Bedrock remains present after overlying ore is excavated, so a prototype
compared each target-bearing center's bedrock elevation with all eight captured
neighbor bedrock elevations. It raised the derived ore bottom only by the
bedrock displacement beyond the allowed residual.

| Candidate | Corrected sources | Rock avoided | Target omitted | Changed excavation columns |
|---|---:|---:|---:|---:|
| bedrock-r10 | 29 | 0 | 0 | 0 |
| bedrock-r8 | 38 | 0 | 0 | 0 |
| bedrock-r5 | 50 | 0 | 0 | 0 |
| bedrock-r4 | 59 | 939.6 | 0.44 | 432 |
| bedrock-r2 | 158 | 1,594.6 | 3.37 | 677 |
| bedrock-r1 | 407 | 3,074.1 | 11.70 | 1,972 |

The r5-r10 raw detections have no player-facing geometry effect in this case.
The r4 transition is the first material plan change and is therefore a useful
conservative boundary for later inspection. It is not evidence that the 59
sources are defects.

Only four bedrock-r4 source corrections change their own final target surface:

- `640,901`
- `641,902`
- `638,900`
- `633,910`

The first three form a small local group. These four coordinates are the
remaining viewer check for whether sample 4 is a true negative at the r4
threshold.

## Current conclusion

Keep sample 4 outside the positive-case aggregate. It currently supports the
r10 candidates by showing no mine-plan change in disturbed mop-up terrain.
Bedrock-r4 changes four source targets whose status is not yet known. If the
viewer confirms a spike coordinate, reclassify that coordinate only; the rest
of the case can remain a control region.
