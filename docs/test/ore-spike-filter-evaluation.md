# Ore spike filter evaluation

Status: proposed laboratory protocol. No candidate filter, threshold, or
acceptance threshold has been selected.

## Question

How much unnecessary mineable rock does a spike-filter candidate remove from
the generated excavation plan, and how much target product does that
correction cause the plan to omit?

The experiment compares player-facing mine plans, not detector classifications.
This captures amplification from a single anomalous terrain column into a
4-by-4 designation and through bottom flattening and corner smoothing.

## Counterfactual pair

For one immutable raw mining replay case and one mining-policy configuration,
produce two final plans:

1. `off`: run the normal mining pipeline with spike filtering disabled;
2. `on`: apply one candidate spike filter to a derived ore interpretation,
   then run the same normal pipeline and policy.

Both branches must use the same raw columns, request, target products, tower
bounds, safety facts, and ordinary mining settings. Preserve the unfiltered
canonical result as the baseline.

Use **Ore quality Off** and **Bottom flattening Off** while selecting the spike
detector and its parameters. This is the primary scoring arena: target-product
loss then belongs directly to the filter and its propagation through
designation aggregation and corner smoothing. In a later composition phase,
repeat retained candidates at every ore-quality level and with bottom
flattening enabled. Report ore removed by the existing bottom-density rule
separately; do not charge that expected policy behavior to the detector.

## Column calculation

For each terrain column `c`, let `S(c)` be its captured surface height. Let
`T_off(c)` and `T_on(c)` be the final target surfaces produced by the two mine
plans, using the game's designation interpolation. If a branch has no mining
designation at that column, use `S(c)` as its target. Count every terrain
column once; do not double-count shared designation boundary samples.
Treat the interval as empty when a target is at or above the captured surface.

The excavation intervals are:

```text
E_off(c) = interval from T_off(c) up to S(c)
E_on(c)  = interval from T_on(c)  up to S(c)

avoided(c) = E_off(c) minus E_on(c)
added(c)   = E_on(c)  minus E_off(c)
```

Intersect `avoided(c)` and `added(c)` with the **raw** captured material-layer
intervals. Sum overlap thickness by material and multiply by one terrain
tile's area. This is a terrain tile-volume proxy, not a quantity of truck
cargo. The raw layers are the scoring authority even when the filter changes
the derived interpretation used by the planner.

## Primary result

For each case and policy, report:

```text
benefit = mineable rock in avoided
cost    = selected target product in avoided, by product and in total
exchange rate = benefit / cost
```

Higher benefit and lower cost are better. Label a positive-benefit,
zero-cost result as `zero target-product loss`; do not encode it as an infinite
ratio. Rank candidate configurations on the benefit/cost Pareto frontier before
making any product-value judgment. A later UI default can be selected from
that evidence without embedding an arbitrary rock-to-ore exchange rate in the
filter itself.

Also report these normalized measures:

```text
waste-rock reduction = benefit / rock excavated by off
target-product retention = 1 - cost / target product excavated by off
```

Report both over the complete plan and over only the affected columns. Include
absolute tile-volumes beside percentages.

## Guard measures

The report must also expose:

- every material in `avoided`, separating target product, other useful
  products, mineable rock, dirt, and other waste;
- every material in `added`, because a candidate must not hide newly deepened
  excavation by netting it against avoided work;
- any final target below the raw bedrock boundary;
- designation cells and target corners changed, added, or removed;
- maximum floor raise and the distance from a corrected source column to the
  furthest changed column; and
- exact geometry outside the candidate's correction neighborhood.

`Bedrock_Terrain` is an impassable boundary rather than mineable waste. Do not
credit it as saved rock. A target that penetrates it is a separate correctness
failure.

## Corpus and comparison

Begin with the three characterized iron, quartz, and copper captures as
positive cases. Retain results per case so a large deposit cannot dominate the
aggregate. Add negative controls containing legitimate steep deposit edges,
thin seams, separated seams, and small ore bodies. False corrections on those
controls are part of the product-loss and geometry-change results, not merely
detector statistics.

For each candidate family, sweep its parameters offline and retain all
nondominated configurations. A useful candidate should avoid substantial rock
on the positive cases, preserve nearly all raw target product, add negligible
excavation, and leave negative-control geometry unchanged. Numeric acceptance
thresholds and the setting's default state remain decisions to make from the
measured frontier and in-game inspection.
