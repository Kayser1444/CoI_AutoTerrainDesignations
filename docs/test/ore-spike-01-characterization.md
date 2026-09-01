# Ore spike characterization: ore-spike-01

Source: `ore-spike-01-009be278dd8873e4.atd-access-case`, captured 2026-08-31.
Payload SHA-256: `009be278dd8873e484e56043f3aeb74df0aa0246242acff9df5e71f27bc7eb39`.
Recorded DLL SHA-256: `43c2b8c45804ce81d7fbeedc8b67a8191235a8ce104a62db09a9977d4f1c3a5c`.
The archived Debug DLL was hash-checked and used to load the capture, validating
payload, input, expected-output and game-assembly hashes. No world or recorded
baseline was changed. Ore quality is OFF in this case.

## Method and coverage

The capture contains 78,583 terrain columns, including 26,173 iron-bearing
columns. Iron is the only captured ore product. Of those, 24,164 have iron in
all eight immediate neighboring columns and qualify for this conservative
comparison. Missing columns and neighbors without this ore are excluded,
never treated as zero depth or empty material at a guessed elevation.

For each eligible column, compare the lowest iron-ore elevation with the
lowest iron-ore elevation in each of its eight neighbors, including diagonals.
Use product identity across normal and disrupted iron material. The strict
one-tile drop is the **deepest neighbor's bottom minus this column's bottom**.
A positive value means some iron at this tile lies below every neighboring
iron column. Coordinates refer to individual terrain tiles, not 4-by-4
terrain designations. Values below are elevation levels, not counts of layer
records. A column may contain multiple ore intervals; the comparison uses the
deepest, without assuming that the entire vertical gap contains ore.

These are morphology candidates, not proof that each feature is a generator
defect. A local minimum test cannot distinguish all legitimate geology, and
it undercounts adjoining spikes. Counts apply only to this capture and the
eligible interior columns.

| Minimum strict one-tile drop | Candidate count |
|---|---:|
| 2 levels | 36 |
| 3 levels | 17 |
| 4 levels | 11 |
| 5 levels | 8 |
| 8 levels | 4 |
| 10 levels | 4 |
| 15 levels | 2 |
| 20 levels | 1 |

These thresholds are descriptive sensitivity checks, not proposed defaults.

## Candidates at least four levels below every neighbor

| Coordinate | Ore bottom | Deepest of eight neighbors | Strict drop | Neighbor median | Drop below median |
|---|---:|---:|---:|---:|---:|
| 3340,1053 | -35.84 | -14.58 | 21.25 | -12.04 | 23.79 |
| 3343,1048 | -26.04 | -7.25 | 18.79 | -0.54 | 25.50 |
| 3341,1051 | -24.95 | -14.43 | 10.52 | -7.23 | 17.73 |
| 3212,1092 | -36.25 | -25.82 | 10.43 | -18.05 | 18.20 |
| 3280,1068 | -22.73 | -14.85 | 7.88 | -13.50 | 9.23 |
| 3230,1157 | -29.48 | -22.32 | 7.16 | -21.11 | 8.37 |
| 3204,1079 | -29.90 | -23.68 | 6.22 | -16.47 | 13.43 |
| 3338,1057 | -19.78 | -13.83 | 5.95 | -13.16 | 6.63 |
| 3206,1102 | -18.14 | -13.42 | 4.72 | -13.17 | 4.97 |
| 3229,1117 | -23.11 | -18.48 | 4.62 | -13.22 | 9.89 |
| 3328,1080 | -22.90 | -18.90 | 4.00 | -17.77 | 5.13 |

## The reported tile: 3212,1092

Its bottom is -36.25. Its four cardinal neighbors bottom out between -19.93
and -16.85: the drop below the deepest cardinal neighbor is 16.32 levels.
Including diagonals, the deepest neighbor is 3211,1093 at -25.82, leaving a
10.43-level portion of the deep end confined to the single tile.

Neighboring iron bottoms:

| Y / X | 3211 | 3212 | 3213 |
|---|---:|---:|---:|
| 1091 | -17.22 | -18.37 | -19.46 |
| 1092 | -17.73 | **-36.25** | -19.93 |
| 1093 | **-25.82** | -16.85 | -16.52 |

The diagonal tile is itself 8.54 levels below its eight-neighbor median.
Therefore the deepest tip is 1-by-1, but a broader anomaly test identifies a
pair of diagonal outliers. A rule requiring every neighbor to be shallower
would miss that smaller one, because its neighbor contains the larger spike.

The recorded designation at 3212,1092 has depth -37 and all four final corner
heights -37. Nearby corners also descend; no counterfactual rerun has yet
measured how much excavation is attributable solely to this spike.

## Layer-profile evidence

For 3212,1092, compared with the median of its eight neighbors:

| Quantity | Spike tile | Neighbor median | Difference |
|---|---:|---:|---:|
| Surface elevation | 32.72 | 34.69 | -1.98 |
| Topmost iron elevation | 16.58 | 34.62 | -18.04 |
| Lowest iron elevation | -36.25 | -18.05 | -18.20 |
| Total iron thickness | 52.83 | 51.67 | +1.16 |
| Bedrock top elevation | -67.68 | -49.27 | -18.40 |

This resembles a downward displacement of the ore interval and underlying
bedrock, rather than an extra 18 levels of iron thickness. The surface is
only about two levels lower. This is an inference from the captured profile,
not a diagnosis of the generator's implementation.

Two stronger candidates show the same broad pattern:

- 3340,1053: ore top -23.60, bottom -23.79 and bedrock top -23.72 levels
  relative to neighboring medians; total ore thickness differs by +0.25.
- 3343,1048: ore top -24.60, bottom -25.50 and bedrock top -24.20 levels;
  total ore thickness differs by -0.36. Its iron interval is only 9.63 levels
  thick and entirely below the deepest neighboring iron bottom. The 18.79
  bottom displacement must not be confused with an 18.79-thick ore tail.

Some weaker candidates include disrupted material and split intervals. Their
ore roof is not always similarly displaced. A future detector must not assume
that every anomalous column is a single continuous, extra-thick ore layer.

## Implications for filter experiments

Start with spatial support for ore-bottom depth, before 4-by-4 aggregation.
Thickness alone would miss the strongest displaced-column examples. Retain
untouched raw captures and change only the derived interpretation for planning.

Compare a strict isolated-minimum clamp with a robust neighborhood or small-
component approach. The latter is needed to investigate adjoining anomalies:
a median-residual threshold of four levels flags 89 eligible columns in this
capture, grouped with eight-connectivity into 44 singletons, ten pairs, four
triples, two groups of four and one group of five. This broader set can include
legitimate steep boundaries; it is a review set, not permission to discard all
89 columns. It includes the diagonal pair at the reported location.

Before selecting a filter, review these candidates in game and obtain control
cases with legitimate deep seams, steep deposit edges, thin deposits and
multiple ore products. Compare retained ore, planned waste excavation and
final designation geometry over all quality settings. No filter or terrain
mutation has been implemented as part of this characterization.

## Local analysis artifacts

The temporary analysis files are in the repository's ignored `.scratch/`:
`InspectMining.cs`, `ore-spike-01-all.csv`, `survey-ore-spikes.py`,
`spike-profiles.py`, `ore-spike-survey.json`, and `ore-spike-candidates.csv`.
The survey can be rerun with `python .scratch/survey-ore-spikes.py` after export.
