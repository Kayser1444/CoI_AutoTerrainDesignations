# Ore spike filter prototype 01

Date: 2026-08-31

Status: positive-case parameter sweep. This experiment narrows the candidates;
it does not select a production algorithm, threshold, or setting default.

## Question and arena

Which local ore-bottom correction avoids the most final-plan mineable rock
while omitting the least raw target product?

All runs use **Ore quality Off** and **Bottom flattening Off**, as selected by
the maintainer. Both branches retain designation aggregation, connectivity,
and corner smoothing. The fixed captures do not contain every possible
exterior safety ray for changed candidate geometry, so the experiment compares
plans at `DirectSafety`: direct protected-origin checks and final corners are
included; candidate-dependent exterior building/ocean rays are excluded.

The material comparison uses the raw captured layers and clamps excavation to
the raw bedrock boundary. Volumes are terrain tile-volume, not truck cargo.

## Prototype filters

Every detector requires target ore in all eight neighboring terrain columns.
The prototype clips the derived target-product interpretation below its
corrected floor; it never modifies the raw scoring layers.

- **Strict clamp**: compare the center bottom with the deepest of all eight
  neighbor bottoms. This corrects only a center deeper than every neighbor by
  more than the allowed residual.
- **Median clamp**: compare the center bottom with the median neighbor bottom.
  This can correct adjacent anomaly groups, but can also act on legitimate
  local relief.
- **Profile clamp**: use the median clamp only when surface height remains
  within 2.25 levels, deepest-interval thickness within 1 level, and bedrock
  displacement tracks ore-bottom displacement within 1.25 levels. These gates
  encode the morphology seen in the three captures.

The allowed residual was swept over 1, 2, 3, 4, 5, 6, 8, and 10 terrain levels.
For example, `median-r10` leaves a center up to 10 levels below the neighbor
median and clips only the excess.

## Selected aggregate results

The table sums the iron, quartz, and copper positive cases. Higher rock saved
and lower target foregone are better.

| Candidate | Corrected columns | Rock saved | Target foregone | Rock per target | Changed excavation columns |
|---|---:|---:|---:|---:|---:|
| strict-r10 | 17 | 62,290.7 | 29.7 | 2,098.3 | 26,811 |
| strict-r8 | 28 | 98,319.3 | 43.4 | 2,265.9 | 27,078 |
| profile-r10 | 66 | 150,902.7 | 57.6 | 2,619.7 | 20,557 |
| median-r10 | 132 | 231,193.1 | 82.0 | 2,819.0 | 38,650 |
| median-r8 | 234 | 258,217.0 | 125.4 | 2,059.3 | 40,881 |

No listed candidate changed the designation count or added excavation. The
large ratio between corrected source columns and changed excavation columns is
real planner amplification: removing a deep local floor constraint changes
corner smoothing across much of a connected mine plan.

`median-r10` is the strongest positive-only balance in this sweep. It saves
more rock than the morphology-gated alternative for a modest increase in
foregone product. `profile-r10` is the more selective median candidate.
`strict-r10` makes the fewest corrections and has the lowest absolute product
cost.

## Per-case warning

Aggregate ratios conceal a large difference in the quartz capture. Rock saved
per unit of target product for `median-r10` is approximately 2,385.5 on iron,
170.2 on quartz, and 4,929.3 on copper. For `strict-r10` it is approximately
2,930.8, 45.4, and 12,492.9 respectively. The quartz case therefore constrains
the filter much more strongly than the other two and must remain visible in
all later comparisons.

## Decision boundary

Carry `strict-r10`, `profile-r10`, and `median-r10` into negative-control
testing. Include `strict-r8` or `median-r8` only if in-game inspection shows
that a 10-level residual leaves visible harmful spikes. Do not choose the
default until legitimate steep edges, thin seams, separated seams, and small
ore bodies have been captured and scored.

## Mop-up control

Sample 4 is a mostly excavated, high-quality mop-up scenario with no confirmed
spike. It is excluded from the positive aggregate above and provisionally
treated as a negative operational control.

At residuals 8 and 10, the strict, median, and profile candidates make no
correction and leave the final plan unchanged. At residual 5, the median
candidate corrects one source column and raises 432 final excavation columns,
apparently avoiding 802.3 rock with no target product. Without a confirmed
spike, that superficially excellent material score is a possible false
positive rather than evidence in the candidate's favor.

A separate bedrock-neighborhood experiment can identify outliers even after
surrounding ore has been excavated. Residuals 5 through 10 identify raw bedrock
outliers but do not change the final plan. At residual 4 it changes 432 final
columns, avoids 939.6 rock, and omits 0.44 target product. This establishes a
useful conservative boundary but does not prove that a bedrock-only detector is
safe on legitimate bedrock relief.

## Post-Medium known-spike case

Sample 6 records a known-spike deposit after Medium quality has already
excavated most of it without a spike filter. All ore-neighborhood candidates
fail because only 2,450 target-bearing columns remain and the spikes no longer
have eight ore-bearing neighbors. A bedrock-neighborhood candidate remains
effective: r6 avoids 46.38 rock for 1.08 target product, while r4 avoids 194.49
rock for 2.97 target product. See the
[sample 6 characterization](ore-spike-06-characterization.md) for candidate
coordinates and the missing pre-excavation capture record. The in-game viewer
confirmed all six plan-affecting bedrock-r4 sources as spikes; r6 finds only two
of those six.
