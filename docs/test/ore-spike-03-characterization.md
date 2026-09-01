# Ore spike characterization: ore-spike-03 (fresh map)

Source: `ore-spike-03-08547bdff02f1b64.atd-access-case`, captured 2026-08-31.
Recorded DLL SHA-256: `64a2626050ec453b19a07f23b108788974fc36191c171efd1147f011ff29d2e4`.
The archived Debug DLL was hash-checked and used to load the case with payload,
request, canonical-output and game-assembly validation. Ore quality is OFF.
No terrain, raw capture, planner policy or baseline was modified.

The maintainer reports a freshly created map on January 1 with simulation
never advanced. This history comes from the maintainer, not the replay schema.

## Disrupted material is already present

Of 101,433 captured terrain columns, 48,554 (47.87%) contain material marked
Disrupted. The capture contains 39,762 `RockDisrupted_Terrain` layer records
and 25,144 `CopperOreDisrupted_Terrain` layer records. These are record counts,
not distinct-column counts. All 17 strict four-level spike candidates have
some disrupted material.

Given the reported fresh, unrun world, disrupted material is already present
in the initial map state. The material names cannot be used as evidence that
the player mined there or simulation advanced. This corrects the uncertainty
raised while inspecting samples 1 and 2: disrupted material does not itself
exclude a fresh-map control. It also cannot be a blanket filter condition,
since it appears in almost half the captured columns.

## Comparable survey

The capture covers x=3636..3969, y=1037..1389. Copper is its only deposit
product; other captured products are dirt and rock. There are 49,648
copper-bearing columns, including 47,048 with copper in all eight immediate
neighbors. The comparison uses product identity across normal and disrupted
copper layers. Missing columns and neighbors without copper are excluded.

As in previous samples, strict drop means the deepest neighboring ore-bottom
elevation minus this column's lowest ore elevation. It measures a one-tile
vertical extremum, including diagonal neighbors; it is not an assumed length
of continuous ore, nor a confirmed count of generator defects. Every strong
candidate's copper lies above its first bedrock boundary.

| Minimum strict drop | Iron sample 1 | Quartz sample 2 | Fresh copper sample 3 |
|---|---:|---:|---:|
| 2 levels | 36 | 229 | 49 |
| 3 levels | 17 | 145 | 25 |
| 4 levels | 11 | 95 | 17 |
| 5 levels | 8 | 61 | 10 |
| 8 levels | 4 | 19 | 5 |
| 10 levels | 4 | 10 | 3 |
| 15 levels | 2 | 5 | 0 |
| 20 levels | 1 | 1 | 0 |

These remain descriptive thresholds rather than filter defaults. The sampled
areas differ in size, deposit shape and history; raw counts are not directly
comparable generator failure rates.

## Same lower-stack displacement

All 17 strong copper candidates show the following:

- The roof of the deepest copper interval and its bottom are displaced by
  similar amounts: their deviations from neighboring medians agree within
  two elevation levels.
- The bedrock-top displacement agrees with the copper-bottom displacement
  within one level.
- The deepest copper interval's thickness differs from neighboring medians
  by only -0.46 to +0.56 levels (median difference -0.09).

Surface changes are much smaller: 16/17 lie within two levels of the neighbor
median; the remaining example, 3687,1114, differs by -2.19. Total copper
thickness stays within two levels in 13/17, but extra upper/disrupted intervals
make total thickness an unreliable universal signal.

This is consistent with the same downward-displaced lower stratigraphy seen
in the other two deposits. The fresh-map provenance supports the conclusion
that later player mining or post-start simulation is not required for this
pattern to exist. It does not identify the faulty generator code or prove a
particular insertion/settling mechanism.

## Illustrative column: 3713,1124

| Quantity | Column | Eight-neighbor median |
|---|---:|---:|
| Surface | 44.53 | 45.82 |
| Copper roof | -5.16 | 20.86 |
| Copper bottom | -31.36 | -5.37 |
| Copper thickness | 26.20 | 26.18 |
| Bedrock top | -69.05 | -43.12 |

The copper and bedrock are approximately 26 levels lower, while copper
thickness is nearly unchanged. The column contains a 23.13-level disrupted-rock
layer above ordinary rock and copper. This is an observed association, not
proof that discarding disrupted material would be a valid correction.

| Y / X | 3712 | 3713 | 3714 |
|---|---:|---:|---:|
| 1123 | -5.78 | -7.54 | -7.72 |
| 1124 | -4.90 | **-31.36** | -4.95 |
| 1125 | **-19.20** | -3.67 | -0.49 |

As in sample 1, a smaller diagonal dip reduces the strict eight-neighbor drop
(to 12.16) despite a roughly 26-level deviation from the neighborhood median.
A four-level median-residual test across the whole eligible region flags
206 columns in 120 singletons, 18 pairs, nine triples, two groups of four and
three groups of five. Those broader candidates still require review for
legitimate geometry.

## Strict candidates at least four levels below every neighbor

| Coordinate | Copper bottom | Deepest neighbor | Strict drop | Drop below neighbor median |
|---|---:|---:|---:|---:|
| 3746,1173 | -17.73 | -4.80 | 12.92 | 13.48 |
| 3713,1124 | -31.36 | -19.20 | 12.16 | 25.99 |
| 3687,1114 | -15.06 | -3.37 | 11.69 | 14.27 |
| 3732,1176 | -12.89 | -4.03 | 8.86 | 12.24 |
| 3740,1164 | -12.41 | -3.78 | 8.64 | 9.29 |
| 3749,1173 | -10.87 | -4.80 | 6.07 | 7.36 |
| 3767,1244 | -10.02 | -4.40 | 5.62 | 6.46 |
| 3819,1207 | -14.08 | -8.56 | 5.52 | 9.87 |
| 3686,1116 | -17.84 | -12.39 | 5.45 | 17.26 |
| 3754,1146 | -3.38 | 1.68 | 5.07 | 5.90 |
| 3696,1145 | 1.62 | 6.50 | 4.88 | 5.45 |
| 3762,1250 | -8.51 | -3.72 | 4.80 | 6.83 |
| 3688,1111 | -8.88 | -4.18 | 4.70 | 8.33 |
| 3884,1159 | -6.97 | -2.56 | 4.41 | 5.25 |
| 3878,1195 | -3.40 | 0.77 | 4.16 | 10.10 |
| 3717,1145 | -4.87 | -0.75 | 4.12 | 6.17 |
| 3752,1156 | -5.40 | -1.34 | 4.06 | 5.45 |

## Next step

The three samples now cover iron, quartz and copper, including a reported
fresh, unrun map. They provide enough positive examples for a first offline
filter experiment. Preserve legitimate steep/edge/separated-seam controls and
compare filter-off/on results across every ore-quality level before choosing
thresholds or a default. Do not mutate raw snapshots or infer that all
Disrupted material is faulty.

The agreed presentation and pipeline are in the
[ore spike filter design](../dev/done/ore-spike-filter.md): a world-setting
**Filter ore spikes** under **Vanilla issue correction**, applied to a derived
ore interpretation before the existing mining algorithm.

## Local reproducibility

Ignored `.scratch/` artifacts: `ore-spike-03-all.csv`,
`ore-spike-03-survey.json`, `ore-spike-03-candidates.csv`,
`survey-third-capture.py`, and `check-fresh-profiles.py`.
Run the latter two scripts after exporting through `InspectMining.cs` with
the case's archived DLL. No application-code build was needed for this analysis.
