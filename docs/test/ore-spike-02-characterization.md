# Ore spike characterization: ore-spike-02

Source: `ore-spike-02-39f94e73bb772178.atd-access-case`, captured 2026-08-31.
Recorded DLL SHA-256: `64a2626050ec453b19a07f23b108788974fc36191c171efd1147f011ff29d2e4`.
The archived Debug DLL was hash-checked and used to load the capture with
payload, input, canonical-output and game-assembly validation. Ore quality is
OFF. No terrain, planner policy, or recorded baseline was modified.

Compare [ore-spike-01](ore-spike-01-characterization.md).

## Coverage and like-for-like survey

This is a separate quartz deposit: 117,288 captured columns, with bounds
x=3196..3639 and y=1286..1686, and no captured coordinates overlapping sample 1.
There are 71,687 quartz-bearing columns; 68,474 have quartz in all eight
immediate neighbors and qualify for the strict comparison. The capture's
non-bedrock products are quartz and rock. The analysis selects deposit products
by product identity; it does not require their IDs to contain the word Ore.

The strict one-tile drop is the deepest neighboring quartz bottom minus the
center's deepest quartz bottom, including diagonal neighbors. Missing columns
and neighbors without quartz are excluded. It is an elevation difference,
not an ore-layer count or an assumed uninterrupted ore thickness. All 95
candidates at the four-level threshold have their deepest quartz above the
first bedrock boundary. The survey found one unrelated column with quartz
below an earlier bedrock layer; it is not one of those candidates.

| Minimum strict drop | Sample 1: iron | Sample 2: quartz |
|---|---:|---:|
| 2 levels | 36 | 229 |
| 3 levels | 17 | 145 |
| 4 levels | 11 | 95 |
| 5 levels | 8 | 61 |
| 8 levels | 4 | 19 |
| 10 levels | 4 | 10 |
| 15 levels | 2 | 5 |
| 20 levels | 1 | 1 |

The four-level rates are 0.046% of eligible iron columns and 0.139% of eligible
quartz columns. These are descriptive rates in two selected areas, not an
estimate of map-wide generator failure rates. None of the cutoffs is yet a
chosen filter default.

## Same lower-layer pattern, different upper material

The isolated deep bottoms match the first sample, and the deeper interval
shows particularly consistent displacement:

- All 95 strong candidates have a deepest-quartz-interval thickness within
  two levels of the eight-neighbor median. Differences range from -0.67 to
  +1.84 levels; the median difference is -0.04.
- All 95 have the roof of that deepest interval displaced by within two levels
  of the bottom displacement, measured against neighboring medians.
- 92/95 have a bedrock-top displacement within one level of the quartz-bottom
  displacement. The other three are 3545,1515; 3449,1571; and 3446,1569, so
  bedrock agreement must not be treated as a universal identity.
- All 95 surfaces differ by less than two levels from neighboring medians.

Unlike the clearest iron examples, 94/95 have an uppermost quartz interval
marked `QuartzDisrupted_Terrain`. Ninety-three contain two quartz layer
records, one contains three, and one contains one. Contiguous normal and
disrupted layers remain separate records; two records do not always mean
two separated seams. Only 4/95 have *total* quartz thickness within two levels
of neighbors. A thickness-only detector would therefore behave differently
across the two samples depending on whether it sums all material or compares
the deeper interval.

The evidence is consistent with a lower stratigraphic stack displaced downward
under extra disrupted material. It does not establish when that material was
added or whether the displacement arose during generation or later simulation.
These captures contain disrupted terrain, not an untouched generation trace.

## Strongest example: 3312,1445

| Y / X | 3311 | 3312 | 3313 |
|---|---:|---:|---:|
| 1444 | -28.07 | -28.30 | -27.49 |
| 1445 | -28.83 | **-53.44** | -29.00 |
| 1446 | -28.28 | -29.57 | -29.35 |

Its deep end is confined to one terrain tile and extends 23.87 levels below
even its deepest neighbor. Relative to neighboring medians, its bottom and
bedrock are 24.88 levels lower, while the surface is only 0.60 levels lower.

Its captured profile is:

| Material | Top | Bottom | Thickness |
|---|---:|---:|---:|
| Disrupted quartz | -2.85 | -23.94 | 21.09 |
| Disrupted rock | -23.94 | -26.54 | 2.60 |
| Rock | -26.54 | -37.17 | 10.62 |
| Quartz | -37.17 | -53.44 | 16.28 |
| Bedrock | -53.44 | -1053.44 | 1000.00 |

For comparison, its eastern neighbor 3313,1445 has rock thickness 10.59 and
quartz thickness 16.24, followed by bedrock at -29.00. The ordinary lower-layer
thicknesses are nearly identical despite the large elevation difference.

## Adjacent candidates and filter implications

A broader four-level drop below the neighbor median flags 475 columns, in
219 singletons, 49 pairs, 26 triples, ten groups of four and eight groups of
five under eight-connectivity. Those broader groups are review candidates,
not confirmed defects; legitimate deposit edges can also satisfy that test.
The larger sample reinforces the need to investigate small adjacent groups,
not just strict isolated minima.

A filter experiment should work from spatial support for deposit depth before
4-by-4 aggregation, preserve raw layers, handle normal/disrupted layers and
separated seams, and evaluate both isolated and clustered anomalies. The
similar lower-interval thickness and modest surface changes are useful
characterization features, not sufficient conditions to delete ore. No filter
has been implemented or selected here.

## All strict candidates at least four levels below every neighbor

| Coordinate | Quartz bottom | Deepest neighbor | Strict drop | Drop below neighbor median |
|---|---:|---:|---:|---:|
| 3312,1445 | -53.44 | -29.57 | 23.87 | 24.88 |
| 3488,1445 | -44.78 | -25.10 | 19.68 | 20.36 |
| 3430,1595 | -50.44 | -32.50 | 17.94 | 19.70 |
| 3480,1428 | -38.12 | -22.07 | 16.05 | 16.89 |
| 3487,1442 | -40.69 | -24.83 | 15.86 | 16.71 |
| 3429,1527 | -35.74 | -20.92 | 14.82 | 16.02 |
| 3430,1525 | -34.65 | -22.58 | 12.07 | 13.60 |
| 3406,1420 | -33.61 | -21.84 | 11.77 | 11.92 |
| 3424,1529 | -29.44 | -17.98 | 11.47 | 12.18 |
| 3429,1515 | -36.57 | -25.75 | 10.82 | 11.71 |
| 3421,1536 | -30.23 | -20.43 | 9.79 | 10.83 |
| 3498,1451 | -35.67 | -26.18 | 9.50 | 12.61 |
| 3522,1580 | -37.92 | -28.92 | 8.99 | 9.91 |
| 3320,1428 | -32.26 | -23.36 | 8.90 | 10.96 |
| 3432,1521 | -36.24 | -27.60 | 8.64 | 11.28 |
| 3377,1452 | -32.96 | -24.33 | 8.63 | 9.41 |
| 3480,1435 | -35.27 | -27.18 | 8.08 | 9.49 |
| 3391,1457 | -36.97 | -28.89 | 8.08 | 8.97 |
| 3389,1443 | -34.02 | -25.95 | 8.08 | 9.22 |
| 3545,1515 | -32.86 | -24.89 | 7.96 | 9.03 |
| 3434,1538 | -33.72 | -25.94 | 7.78 | 8.63 |
| 3516,1539 | -39.56 | -31.98 | 7.57 | 10.32 |
| 3422,1534 | -29.22 | -21.74 | 7.48 | 10.25 |
| 3359,1453 | -32.97 | -25.69 | 7.28 | 7.99 |
| 3489,1448 | -36.39 | -29.14 | 7.24 | 8.63 |
| 3494,1444 | -31.96 | -24.77 | 7.18 | 8.59 |
| 3490,1432 | -34.04 | -26.87 | 7.17 | 13.56 |
| 3421,1529 | -25.17 | -18.06 | 7.12 | 7.58 |
| 3473,1446 | -39.40 | -32.59 | 6.80 | 7.30 |
| 3481,1439 | -34.48 | -27.69 | 6.79 | 8.01 |
| 3463,1457 | -33.79 | -27.22 | 6.57 | 8.49 |
| 3318,1510 | -39.39 | -32.88 | 6.51 | 7.98 |
| 3458,1459 | -34.99 | -28.59 | 6.40 | 8.34 |
| 3466,1416 | -33.21 | -27.17 | 6.04 | 6.95 |
| 3514,1575 | -36.03 | -30.05 | 5.97 | 6.10 |
| 3380,1449 | -33.17 | -27.22 | 5.95 | 9.60 |
| 3501,1454 | -32.22 | -26.27 | 5.94 | 7.03 |
| 3389,1543 | -37.37 | -31.52 | 5.85 | 7.07 |
| 3532,1542 | -35.76 | -29.98 | 5.79 | 11.47 |
| 3482,1423 | -30.59 | -24.86 | 5.73 | 8.46 |
| 3484,1432 | -29.98 | -24.28 | 5.70 | 6.17 |
| 3493,1441 | -31.15 | -25.51 | 5.63 | 6.61 |
| 3492,1546 | -31.96 | -26.33 | 5.63 | 5.79 |
| 3359,1451 | -30.89 | -25.27 | 5.62 | 6.88 |
| 3367,1402 | -37.79 | -32.18 | 5.61 | 9.34 |
| 3403,1427 | -30.21 | -24.61 | 5.60 | 6.63 |
| 3444,1570 | -29.98 | -24.39 | 5.59 | 6.18 |
| 3426,1621 | -34.26 | -28.67 | 5.59 | 5.94 |
| 3422,1628 | -36.68 | -31.12 | 5.56 | 8.26 |
| 3385,1445 | -35.79 | -30.27 | 5.52 | 14.05 |
| 3468,1415 | -32.46 | -26.98 | 5.47 | 6.91 |
| 3513,1535 | -34.39 | -28.92 | 5.47 | 7.23 |
| 3449,1571 | -31.88 | -26.45 | 5.44 | 6.40 |
| 3485,1451 | -37.25 | -31.82 | 5.43 | 10.02 |
| 3423,1532 | -27.14 | -21.74 | 5.40 | 9.34 |
| 3387,1399 | -32.17 | -26.78 | 5.39 | 5.88 |
| 3394,1427 | -31.71 | -26.66 | 5.05 | 6.52 |
| 3387,1442 | -33.26 | -28.23 | 5.03 | 8.47 |
| 3527,1571 | -34.11 | -29.09 | 5.02 | 5.58 |
| 3420,1531 | -23.18 | -18.17 | 5.01 | 5.47 |
| 3467,1407 | -30.04 | -25.04 | 5.00 | 5.47 |
| 3311,1538 | -44.39 | -39.40 | 4.99 | 12.49 |
| 3526,1451 | -29.70 | -24.76 | 4.94 | 5.44 |
| 3461,1386 | -39.87 | -34.96 | 4.91 | 10.52 |
| 3427,1617 | -33.73 | -28.83 | 4.90 | 5.54 |
| 3410,1582 | -29.95 | -25.06 | 4.89 | 6.62 |
| 3477,1426 | -32.35 | -27.51 | 4.84 | 9.41 |
| 3446,1569 | -29.97 | -25.13 | 4.84 | 6.06 |
| 3489,1434 | -35.81 | -31.07 | 4.74 | 14.31 |
| 3475,1427 | -32.98 | -28.32 | 4.66 | 9.06 |
| 3421,1608 | -35.28 | -30.62 | 4.66 | 7.23 |
| 3277,1512 | -41.54 | -36.92 | 4.62 | 11.14 |
| 3499,1446 | -26.90 | -22.32 | 4.58 | 5.49 |
| 3416,1523 | -32.93 | -28.44 | 4.49 | 6.96 |
| 3487,1436 | -39.69 | -35.26 | 4.44 | 16.49 |
| 3427,1520 | -28.38 | -23.99 | 4.39 | 5.94 |
| 3380,1457 | -37.65 | -33.29 | 4.37 | 9.59 |
| 3468,1458 | -30.03 | -25.68 | 4.35 | 5.44 |
| 3480,1425 | -26.05 | -21.78 | 4.27 | 5.30 |
| 3503,1444 | -29.67 | -25.40 | 4.27 | 7.96 |
| 3381,1461 | -35.04 | -30.79 | 4.24 | 5.22 |
| 3417,1547 | -31.78 | -27.54 | 4.24 | 4.71 |
| 3512,1448 | -30.67 | -26.45 | 4.22 | 7.34 |
| 3289,1440 | -40.05 | -35.84 | 4.21 | 10.42 |
| 3515,1467 | -32.99 | -28.81 | 4.19 | 6.01 |
| 3511,1451 | -29.08 | -24.90 | 4.18 | 4.94 |
| 3529,1451 | -28.97 | -24.79 | 4.18 | 6.01 |
| 3505,1442 | -29.54 | -25.40 | 4.15 | 6.10 |
| 3407,1536 | -30.49 | -26.35 | 4.14 | 5.70 |
| 3382,1450 | -30.43 | -26.30 | 4.12 | 5.77 |
| 3506,1446 | -31.00 | -26.91 | 4.10 | 8.96 |
| 3395,1424 | -29.46 | -25.37 | 4.09 | 4.75 |
| 3304,1435 | -34.01 | -29.95 | 4.06 | 5.91 |
| 3389,1403 | -35.55 | -31.53 | 4.02 | 9.54 |
| 3502,1446 | -26.37 | -22.36 | 4.00 | 4.78 |

## Local analysis artifacts

Ignored `.scratch/` files: `ore-spike-02-all.csv`,
`ore-spike-02-survey.json`, `ore-spike-02-candidates.csv`,
`survey-second-capture.py`, `check-quartz-profiles.py`, and
`check-quartz-bedrock.py`. The export uses `InspectMining.cs` and the archived
capture DLL. Rerun `python .scratch/survey-second-capture.py` after export to
recompute candidate counts, profiles, connected groups and capture overlap.
