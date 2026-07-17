# Accessway Pathfinding Useful-Height Envelope

Status: experimental shared V1/V2 optimization

Drafted: 2026-07-15

Related designs:

* [Accessway Pathfinding](accessway-pathfinding.md)
* [Accessway Pathfinding Side-Ray Landscaping Cost Amendment](accessway-pathfinding-side-ray-cost.md)
* [Accessway V2 Review and Staged Implementation Plan](accessway-v2-implementation-plan.md)
* [Unified Goal Search and Snapshot Potential Heuristic](unified-goal-search-snapshot-potential-heuristic.md)

## Summary

Precompute a spatial lower and upper useful pathing height for every tile in an
access-search snapshot. Generated terrain-work states (`V`) outside that band
are dominated: they have climbed above every terrain peak and terminal surface
that can justify their elevation, or descended below every terrain trough and
terminal surface that can justify their depth. Returning from that excess
height to the same useful surface requires at least as much travel and strictly
more landscaping work.

The envelope is shared snapshot-and-request infrastructure used only to prune
newly generated `V` nodes. Terrain/G and the selected start always define the
useful hull. Whether every provider-eligible fixed profile or only current
request targets also seed it remains an open reuse-versus-pruning decision.
Sources are not pruning candidates. V1 and V2 apply the same fields to their
different generated-state shapes, but use different lower-bound allowances. V1
may place a generated center up to `0.5` below the lower field and V2 up to
`1.0` below it. This bounded exception preserves the flat landing that a
descending ramp can need in order to turn toward a low fixed goal. Both widths
retain the exact upper field:

* V1 tests the one newly generated `AccessHeightProfile` before history and
  side-ray evaluation.
* V2 tests profiles introduced by transitions that move a lane center to a new
  location, such as straight and strafe transitions, before cloning history
  and evaluating the transition's work and exterior rays. In-place turns do
  not move a center and are not tested.
* Existing/fixed profiles and explicit `G` states are never rejected by the
  envelope. Captured terrain already includes G surfaces by definition. The
  selected start contributes an additional source. The fixed-profile source
  policy is deliberately unresolved below.

This is graph-domain pruning, not an A* heuristic. V1 and V2 therefore apply it
identically under A* and Dijkstra. The design deliberately uses canonical
center-height pruning rather than a whole-footprint dominance proof. A
pathological rigid-profile case could theoretically lose the cheapest route;
that small accepted approximation is preferable to retaining broad excess
search sheets. Keep the existing global vertical bounds as defense in depth and
use unpruned Dijkstra comparisons to measure, not forbid, any difference.

## Motivation

The current snapshot records one global generated-height interval:

```text
snapshot minimum terrain/fixed height - one level
snapshot maximum terrain/fixed height + one level
```

That prevents unbounded vertical search, but it gives every location the same
range. On a large low-elevation snapshot, a V branch near the map boundary can
continue climbing or digging through many states even after its legal slope
would pass above or below every useful surface in the snapshot. Search markers
make these broad, futile V sheets especially visible:

* V expands toward the snapshot or physical map edge with no terrain peak or
  terminal capable of needing that elevation.
* V continues above or below already useful G elevations even though it must
  later pay to return to G or a fixed frontage.
* Every excess state triggers profile feasibility, generated-history,
  landscaping-ray, cleanup, queue, and diagnostic work.

The existing A* potential improves ordering but cannot remove these states.
Dijkstra receives no benefit from that heuristic at all. A useful-height
envelope removes the states from both searches before their expensive work.

## Scope

This proposal covers:

* V1 and V2 generated flat and axis-aligned slope profiles;
* tower-ground, fixed-provider, and other request-target terminal surfaces;
* mining, dumping, and mixed leveling searches;
* A* and Dijkstra;
* snapshot preprocessing, rejection diagnostics, replay validation, fixtures,
  and live overlay support.

It does not:

* replace the V1 height-aware goal index or the V2 potential field;
* change path cost, side-ray scoring, durability, cleanup, handoff, or G-graph
  rules;
* make impassable terrain or buildings into envelope barriers;
* add G-to-V transitions where a search implementation does not already have
  them;
* initially exploit approach direction, generated history, or the current best
  goal cost to tighten the band;
* initially cover future corner or saddle designation sets (`V'` or `V''`).

## Core observation

Let the maximum generated construction grade be `s`, currently one terrain
level per four horizontal tiles. Let `q` be a source position, such as a
captured terrain sample or a sample from a fixed profile selected by the source
policy, with source height `h(q)`. Let `p` be the tile position where the
useful-height envelope is being evaluated. The source's downward
construction-grade cone describes elevations that may be justified at `p` by
approaching the high surface at `q`:

```text
upperCone(q, p) = h(q) - s * distance(p, q)
```

The union of all such cones is the upper useful-height surface:

```text
UpperUsefulHeight(p) = max over source samples q of
                       [ h(q) - s * distance(p, q) ]
```

Similarly, the upward cones from terrain troughs and required terminal surfaces
form the lower useful-height surface:

```text
LowerUsefulHeight(p) = min over source samples q of
                       [ h(q) + s * distance(p, q) ]
```

Use cardinal Manhattan distance for the first implementation. Generated V
movement is cardinal and its profiles use the same construction grade. Ignoring
obstacles and turn restrictions makes each cone at least as permissive as the
real graph.

Every captured terrain sample is itself a source. Consequently:

```text
LowerUsefulHeight(p) <= terrainHeight(p) <= UpperUsefulHeight(p)
```

at captured positions. On flat terrain both surfaces collapse to the terrain
height. Near a mountain, the upper surface grows into a maximum-grade approach
apron around its peaks. Near a deep valley, the lower surface creates the
inverse apron. Fixed and terminal profiles extend either apron where an
off-terrain handoff height actually matters.

This is intentionally not the min/max elevation of the snapshot. It is the
spatial union of optimistic slope cones around all surfaces that can justify a
generated elevation.

## Dominance argument

A route portion lying wholly above `UpperUsefulHeight` has elevation unsupported
by any terrain peak or required terminal surface. Lowering the excess portion
toward the upper hull:

* does not remove access to a higher source, because every source's optimistic
  maximum-grade approach is already represented in `UpperUsefulHeight`;
* cannot increase direct fill or cut magnitude toward the captured terrain;
* cannot increase the distance needed to return to a source surface;
* preserves the reason for any terminal-height match because terminal surfaces
  are sources.

The inverse applies below `LowerUsefulHeight`: raising an unsupported deep
portion toward the lower hull reduces excavation and does not remove access to
a lower source.

There is one geometry exception to that center-height dominance argument. A
descending ramp cannot turn until it reaches a flat landing. When approaching
a low fixed goal, the landing center can need to sit below the scalar lower
hull even though the route immediately turns back toward a represented terminal
surface. Keep a bounded lower allowance for this maneuver: `0.5` for V1 and
`1.0` for V2. V2 needs the larger allowance because its width-two band and flat
turn landing introduce two lane centers together. This is a lower-bound
exception only; it does not justify any state above the upper hull.

The graph uses the V state's canonical center height as the pathing-height
abstraction. A rigid flat or slope footprint can cross a locally non-planar hull
even when its center lies outside it, so center pruning is not a formal
whole-surface dominance proof. The design accepts the unlikely possibility that
such a crossing profile is necessary and, less likely again, belongs to the
cheapest route. Full 5x5 profile geometry remains authoritative for feasibility,
shared edges, landscaping work, and replay; it is not used to weaken the
height-pruning boundary.

## Envelope sources

Build the base envelope from captured terrain/G surfaces. The selected start
must be part of the effective envelope. Request targets must also be represented
unless the chosen reusable-base policy already includes them. Including extra
fixed-profile sources weakens pruning but cannot create a false rejection.

These sources define the hull; the rejection rule is applied only to newly
generated `V` profiles. Explicit G nodes, starts, goals, and other fixed
profiles do not need to be "inside" the envelope as candidates because they are
never pruned by it. The selected start and any request target not already
represented by the base must be added as sources. Whether other
provider-eligible fixed profiles seed the reusable base is left open below.

| Source | Upper source | Lower source | Notes |
|---|---:|---:|---|
| Precise captured terrain height | Yes | Yes | Primary peak/trough source. Use precise height, not rounded G height, before conservative fixed-point rounding. |
| Fixed start profile | Yes | Yes | The start is always an envelope source. Sample its full bilinear 5x5 target surface, even if it is not otherwise a reusable provider or goal. |
| Tower-reachable and other captured G | Already covered | Already covered | Raw terrain supplies the elevation. Add an explicit source only if its pathing height differs from captured terrain height. |
| Request target profile | Yes | Yes | Sample every accepted fixed/provider target's full bilinear 5x5 surface in the effective envelope: in the reusable base when already included there, otherwise in the request overlay. This also covers future synthetic target surfaces. |
| Other provider-eligible fixed profile | Open | Open | Including all such profiles weakens pruning but allows one hull to survive later promotion of a cluster/profile to a provider or target. A hybrid can omit them initially, then update only a hull side actually exceeded by a newly required source. |
| Existing-designation projected cut ceiling | No | No | An outward side ray from existing cut work records the maximum compatible candidate height at each disturbed tile. A candidate above it conflicts with the projected excavation and is rejected by ordinary profile feasibility. The material ray is no more permissive than the optimistic construction-grade cone and cannot expand the hull. |
| Existing-designation projected fill floor | No | No | An outward side ray from existing fill work records the minimum compatible candidate height at each disturbed tile. A candidate below it conflicts with the projected dumped slope and is rejected by ordinary profile feasibility. The material shoulder is no more permissive than the optimistic construction-grade cone and cannot expand the hull. |
| Ocean minimum drivable surface | Yes | No | Height `1` is a confirmed upper-hull source wherever a generated fill route may cross ocean: the minimum drivable surface legitimately justifies fill at that height. It does not justify deeper excavation. |
| Props and terrain-removal thresholds | No | No | A prop may rule out a low candidate and thereby raise the feasible trough, but it cannot justify V above the captured ground at that tile. A ground-level route through or removing the prop dominates extra landscaping performed solely to cross a removal threshold. |
| Buildings and hard blockers | No | No | They remain feasibility blockers; they do not justify an elevation. |
| Durability rays | No | No | Durability slopes are always steeper than the optimistic construction-grade envelope. They can reject a candidate but cannot force a useful path outside the hull or justify additional height/depth. |
| Speculative generated V | No | No | Letting search history seed the snapshot would be circular and request-order dependent. |

### Base snapshot versus request overlay

Terrain/G and any adopted global constraint sources already exist when
`AccessSearchSnapshot` is built. Three source policies remain under
consideration:

* **Tight request hull:** keep the reusable base terrain/G-only, then merge the
  selected start and accepted request targets into a request-scoped overlay.
  This gives stronger pruning but the overlay can expand when a later cluster
  gains a newly accepted fixed provider or target.
* **Reusable area hull:** seed the base with every snapshot-known fixed profile
  eligible to become a provider or target. Promoting a cluster/profile then
  changes connectivity but cannot expand the hull, allowing subsequent cluster
  searches to reuse it. The cost is weaker pruning near fixed profiles that are
  irrelevant to a particular request.
* **Monotone hybrid hull:** retain the current effective hull between cluster
  searches. Before adding a newly required start or target, sample its full 5x5
  surface against the cached fields. Reuse the upper field when every sample
  satisfies `h(q) <= UpperUsefulHeight(q)`, and reuse the lower field when every
  sample satisfies `h(q) >= LowerUsefulHeight(q)`. If only one condition fails,
  update only that side. Many later targets should already be contained and
  require no hull work at all.

Both V1 and V2 must query the same effective envelope. The selected start and
every request target must be represented under the chosen policy. A future
synthetic goal surface absent from the snapshot still requires an overlay.

Hull updates are monotone: upper updates can only raise `UpperUsefulHeight`, and
lower updates can only lower `LowerUsefulHeight`. An implementation may rerun
the affected distance transform or propagate cones only from violating samples;
the result must be identical to rebuilding that field from the complete current
source set. Use the same conservative fixed-point rounding for containment
tests as for source insertion so numeric conversion cannot incorrectly reuse a
too-narrow field.

### Closure under finder-generated surfaces

For the upper field, retain the following invariant: a new surface placed by
the finder cannot expand the upper hull because the finder can place it only at
or below the existing upper hull. More formally, a new upper source sample at
`q` satisfies
`h(q) <= UpperUsefulHeight(q)`. Since `UpperUsefulHeight` is already the
max-minus grade closure, its entire cone is dominated everywhere:

```text
h(q) - s * distance(p, q) <= UpperUsefulHeight(p)
```

The lower result is not symmetric after adding the downramp-turn allowance: a
finder-generated V1 sample may be `0.5` below `LowerUsefulHeight(q)`, and a V2
sample may be `1.0` below it. If such work later becomes a fixed/provider source,
it can lower the raw lower field. A cached or incrementally updated envelope
must therefore run the ordinary lower containment/update rule for these
profiles rather than assuming byte-identical lower-field closure. Repeated
provider promotion must be measured for cumulative lower-field widening; do
not weaken the upper-field closure or silently compound the per-query allowance.

## Numeric representation

Use physical height multiplied by 32 (`height32`) throughout the envelope:

* one physical terrain level is `32` units;
* the construction grade over one tile is `8` units;
* one four-tile V origin step changes at most `32` units;
* `AccessHeightProfile.GetHeight2NumeratorAt(x, y)` is already exactly
  `height32` at its 5x5 sample positions;
* a profile center is exactly `profile.Center2 * 16` in `height32`.

Convert precise terrain conservatively:

* round upper sources upward;
* round lower sources downward;
* round an upper result upward and a lower result downward if any transform
  operation can lose precision.

This widens the allowed band. It must never narrow it due only to conversion.
Centralize the directed terrain conversion so source insertion, containment
tests, overlays, diagnostics, and fixtures cannot acquire different rounding
rules. Profile samples and centers require no rounding.

## Field representation and ownership

Use one dense row-major `int[]` for each hull side over the inclusive snapshot
tile rectangle:

```text
index = (tile.Y - min.Y) * width + (tile.X - min.X)

AccessUsefulHeightEnvelope
  Min
  Width
  Height
  UpperHeight32[index]
  LowerHeight32[index]
  TryGetBand(tile, out lowerHeight32, out upperHeight32)
  IsV1CenterHeightUseful(center, centerHeight32, out rejection)
  IsV2CenterHeightUseful(center, centerHeight32, out rejection)
```

The tile lattice directly represents precise terrain and fixed-profile source
samples and permits an exact lookup at every V1 or V2 lane center. A candidate
center is `origin + (2, 2)`. Two 32-bit arrays cost eight bytes per captured
tile, about 8 MB per million tiles. Start with 32-bit values for clarity and
overflow safety; consider packed relative values only if measured memory makes
that worthwhile.

Searches are sequential. Own the latest fields in an
`AccessUsefulHeightEnvelopeCache` for the current snapshot/provision sequence.
The cache may mutate between cluster searches but is frozen while a search is
active:

```text
BeginRequest -> freeze hull updates and expose the current envelope
EndRequest   -> release after search, replay/materialization, and diagnostics
AddSources   -> assert that no request is active, then update as required
```

`AccessPathRequest` references the current envelope directly; copy-on-write,
locking, and versioned arrays are unnecessary. V1, V2, replay, and diagnostics
therefore see the same field instance. Discard the cache when its snapshot is
replaced or world changes invalidate the captured terrain or bounds.

## Preprocessing algorithm

The transform uses a rectangular tile array over the existing snapshot capture
bounds. Missing and physical-map-edge samples are not terrain sources. They do
not stop optimistic cone propagation; horizontal search bounds still reject
states outside the request.

Initialize:

```text
upper[p] = negative infinity
lower[p] = positive infinity
```

Merge source samples in place:

```text
upper[p] = max(upper[p], upperSourceHeight32)
lower[p] = min(lower[p], lowerSourceHeight32)
```

Use directed upper/lower terrain conversions, exact profile numerators, and an
upper-only height-`32` seed for eligible ocean tiles. Guard infinity sentinels
before adding or subtracting the grade step.

Then compute:

```text
UpperUsefulHeight[p] = max_q(upperSeed[q] - gradeStep * L1(p, q))
LowerUsefulHeight[p] = min_q(lowerSeed[q] + gradeStep * L1(p, q))
```

Because the metric is Manhattan and the grade is uniform, use a separable
distance transform rather than one priority-queue search per source:

1. Relax left-to-right and right-to-left along every row.
2. Relax top-to-bottom and bottom-to-top along every column.
3. Update upper and lower together in each sweep when both fields need work.

Each relaxation is constant work:

```text
upper[current] = max(upper[current], upper[neighbor] - gradeStep)
lower[current] = min(lower[current], lower[neighbor] + gradeStep)
```

The result is `O(snapshot tile count)` time. A query outside the array or at an
unreached sentinel fails open and records `HeightEnvelopeMissingSample`; normal
horizontal and physical bounds remain authoritative.

### Selective monotone updates

Before a later cluster search, compare every sample of each newly required
source against the closed fields:

```text
expandsUpper when sourceHeight32 > upper[sourceTile]
expandsLower when sourceHeight32 < lower[sourceTile]
```

Reuse each side whose condition never fails. If a side expands, retain its
current closed array, merge only the violating source samples into it, and
rerun that side's row and column sweeps. A closed field is itself a valid seed:
the result is exactly the union of the old hull and the new source cones. There
is no need to retain the original seed arrays or rebuild from the complete
source list.

Update upper and lower independently. A target inside both fields costs only
its containment scan; an upper-only or lower-only violation transforms only
that array. Freeze the resulting effective envelope for the duration of the
next sequential cluster search.

## Center-height rejection rule

Query the envelope only at each newly reached V lane/profile center and require:

```text
V1: LowerUsefulHeight(center) - 0.5 <= profile.Center
                                      <= UpperUsefulHeight(center)
V2: LowerUsefulHeight(center) - 1.0 <= profile.Center
                                      <= UpperUsefulHeight(center)
```

Reject above or below on strict center separation after conservative fixed-point
rounding. The version-specific lower allowance is the only outward margin; do
not add a profile-level or upper margin. For V2, evaluate each newly reached
lane center independently; do not substitute one band-center sample for two
lane centers. V2 in-place turns inherit their existing centers and do not
require an envelope test.

The 5x5 bilinear target surface is still evaluated by ordinary profile
feasibility, fulfilled-work reconstruction, side rays, costing, and replay.
Those checks determine whether the centered state is physically valid; they do
not decide whether its pathing height is useful. This center-only decision is
the candidate rejection rule; fixed start/target source surfaces still seed all
of their samples so the hull represents their exact terminal seam heights.

## V1 integration

V1 generates candidate profiles in `AddOriginSuccessors`. The ideal order is:

1. Solve the exact successor profile from the current shared edge and mode.
2. Reject `HeightEnvelopeAbove` or `HeightEnvelopeBelow`.
3. Run ordinary snapshot profile feasibility.
4. Run generated-history compatibility.
5. Skip known no-better states.
6. Calculate side rays, cleanup, and full generated-entry cost.
7. Relax the queue.

Only generated `V` candidates are tested. Do not reject:

* the fixed start profile (which is already an envelope source);
* a fixed/existing-profile successor;
* explicit G nodes;
* a fixed goal during its terminal test.

The fixed start and fixed goals must have been included as envelope sources.
Other fixed successors and explicit G nodes remain exempt without becoming
sources. Replay and materialization should recheck the same envelope for
generated profiles as defense in depth and report a specific replay reason if
the immutable effective envelope and route disagree.

V1 turns do not need a separate field. Their newly generated profile is checked
at its center like any other newly reached V state. Outer-corner rays remain
ordinary feasibility and cost inputs.

## V2 integration

V2 transition forms interact with generated profiles as follows:

* straight: two new profiles;
* strafe: one new profile, with one retained lane as context;
* turn: changes direction over the existing flat landing without moving either
  lane center;
* synthetic start companion: one generated profile beside one immutable fixed
  seed.

Apply the shared envelope before `AccessV2History.TryApply` and before
`EvaluateV2Transition` performs work and exterior-ray scoring. Geometry that
does not require history may run first. Apply it only when the transition moves
a generated lane center to a new location. Skip V2 turn transitions because
both centers remain unchanged and were already admitted when they were reached.

For center-height pruning:

* Test every profile whose center is newly reached by the transition.
* Reject a center-moving transition if any newly reached lane center is above or
  below its envelope band.
* Do not retest a retained fixed or previously generated lane center; it is
  context, not a newly reached state.
* Do not test an in-place turn merely because it changes the band direction or
  transition representation.

Future asymmetric/deferred profile pairs use the same rule: test every newly
reached lane center independently.

Synthetic companions are evaluated, but the immutable seed is not. The
immutable start seed must contribute to the effective envelope, so the
companion should normally touch an allowed cone where its exact shared edge
forces the height. Record separate diagnostics for start-companion envelope
rejection so a failure is not confused with ordinary search exhaustion.

V2 replay should validate every uniquely reached generated origin through the
same shared helper. It should not perform a second envelope test for an in-place
turn over already validated centers. Explicit G path nodes are outside the
envelope rule.

## Interaction with cost and feasibility

### Landscaping cost

The dominance claim relies on excess height being strictly worse. Direct work
is monotone when an above-hull candidate is lowered toward terrain or a
below-hull candidate is raised. Side-ray volume is expected to be monotone in
the same direction when the ray keeps the same operation and termination
surface.

It is not yet proven monotone when changing height changes:

* cut versus fill classification at a corner;
* selected terrain material layer and its normal slope;
* whether a ray terminates before a blocker;
* whether a ray changes from resolved to unresolved;
* ocean or projected-designation interaction.

Center-height differentials should record whether any of these effects produce
a different winning cost or success result. Rare mismatches are an accepted
approximation; repeated or representative failures require revisiting sources
or the boundary policy.

### Props and removal thresholds

Props are feasibility and cleanup inputs, not useful-height sources. A prop can
rule out a low candidate and thereby make the feasible trough locally higher,
but it cannot warrant a generated V profile above the captured ground at that
tile. Captured terrain already contributes that ground height to both fields.

A route that passes through or removes the prop at ground level dominates a
route that performs extra fill or excavation solely to cross the prop's
terrain-removal threshold; the latter pays a much larger landscaping cost without
improving target access. Apply prop blocking, cleanup, and removal semantics in
ordinary candidate feasibility and costing. Do not add prop threshold sources
or prop-specific envelope exemptions.

### Existing and projected designations

The selected start and fixed request targets are sources and remain immutable.
Other existing provider-eligible fixed profiles are sources only under the
reusable-area policy. Existing terrain designations also project their expected
cut or fill side slopes beyond their 4x4 target surfaces. The snapshot stores a
cut ceiling and/or fill floor at affected tiles, and
`IsProfileBlockedByProjectedDesignationHeight` rejects a candidate that crosses
one. These are compatibility constraints, not terminal surfaces, so they do not
seed either useful-height field. If the designation itself is a start, target,
or provider included by policy, its concrete 5x5 target profile is the source.
No supported material slope runs farther per unit height than the envelope's
optimistic construction slope, so every projected cut or fill ray is dominated
by the corresponding cone from that concrete surface and cannot expand the
hull.

### Durability and buildings

Buildings never justify climbing or digging around them in the vertical
dimension because direct occupancy and disturbance remain blocked. Durability
rays are likewise feasibility constraints, not useful-height sources. Their
slopes are always steeper than the envelope's optimistic construction grade, so
their projected surfaces are dominated by the corresponding useful-height
cones. Durability can reject a route inside the hull, but it cannot make an
otherwise unsupported route above or below the hull necessary.

### Operation-specific search

The same base envelope can serve mining, dumping, and mixed work, but one side
may be irrelevant:

* mining-only search primarily benefits from the lower boundary;
* dumping-only search primarily benefits from the upper boundary;
* mixed leveling benefits from both.

Keep both arrays initially. Operation-specific omission is a later memory
optimization and must account for terminal profiles that use a different
operation at handoff.

### A*, Dijkstra, and goal cost

The envelope does not contribute to `h` or alter `g`. It rejects graph nodes
before costing, identically for A* and Dijkstra. Dijkstra with the envelope
disabled remains the measurement oracle for the optimal route in the unpruned
graph, but exact equivalence is not a release requirement.

Do not initially tighten the envelope using the current incumbent goal cost.
That would be a valid branch-and-bound extension only after proving a lower
bound on the extra landscaping cost, and it would make the allowed domain
change during the search.

## Diagnostics and overlay

Add rejection counters:

```text
HeightEnvelopeAbove
HeightEnvelopeBelow
HeightEnvelopeMissingSample
V2StartCompanionHeightEnvelopeAbove
V2StartCompanionHeightEnvelopeBelow
```

Missing envelope data should initially fail open and record a diagnostic. The
existing horizontal and physical bounds remain authoritative.

Snapshot diagnostics should report:

* preprocessing time;
* source counts by category;
* tile count and memory;
* minimum, maximum, and average allowed-band width;
* tiles whose band collapses to terrain or one profile level;
* any fixed/profile source outside the resulting band, which is a construction
  bug.

Search result/performance logs should report above/below rejection counts next
to visited and pending nodes.

Extend the experimental marker tooling with an optional useful-height view:

* at a selected tile, show terrain, lower, upper, and visited V height;
* optionally color a rejected explored candidate differently from V and G;
* provide a compact heatmap or sampled markers for band width rather than
  drawing two full surfaces by default.

The overlay is diagnostic only and must not change snapshot or search state.

## Validation plan

### Transform fixtures

Cover at least:

* flat terrain: `LowerUsefulHeight == terrain == UpperUsefulHeight` away from
  added sources;
* one peak: exact maximum-grade upper cone in all cardinal directions;
* one trough: exact lower cone;
* two peaks and two troughs: correct max/min union;
* selected start and target profiles above and below terrain;
* unrelated provider-eligible fixed profiles above and below the tight request
  hull, comparing both source policies;
* promotion of a cluster/profile to a provider without changing the reusable
  area hull;
* finder-generated surfaces added as providers, proving a byte-identical upper
  field and validating any required lower containment/update;
* hybrid reuse when a new target is inside both fields, above only, below only,
  and outside both, verifying selective field updates against a full rebuild;
* target at the snapshot boundary;
* precise fractional terrain with conservative rounding;
* missing capture tiles and physical map edges;
* confirmed ocean height-`1` upper source;
* props and removal thresholds, confirming that they do not change either
  field;

### V1 fixtures

Cover:

* pointless flat-map climb and descent;
* pointless excavation below flat terrain;
* necessary climb toward a mountain or raised fixed target;
* necessary descent toward a trough or lowered fixed target;
* switchback and 90-degree flat turn near the hull, including acceptance at
  exactly `0.5` below the lower field and rejection beyond it;
* G-to-V and V-to-G handoffs;
* fixed-provider continuation;
* projected designation, ocean, building, durability, and prop interactions;
* both mining/dumping-only and mixed leveling requests.

### V2 fixtures

Repeat the V1 cases for:

* equal flat bands;
* uniform ramp bands;
* acceptance at exactly `1.0` below the lower field and rejection beyond it;
* straight and strafe center-moving transitions;
* 2x2 flat turn transitions, verifying that turns inherit already admitted
  centers and are not independently envelope-tested;
* synthetic start companions;
* fixed two-origin frontages;
* bounded handoff spans and explicit G continuation;
* future asymmetric profiles before they are enabled with envelope pruning.

### Optimality differential

Create small exhaustive or randomized snapshots with a deliberately generous
visited limit. For both widths and every supported operation mode:

1. Run Dijkstra with the envelope disabled.
2. Run Dijkstra with center-height envelope pruning.
3. Compare success/failure and optimal total cost within the normal tolerance.
4. Replay both winning routes.
5. On mismatch, save the complete fixture seed and source/envelope arrays.

The route itself may differ when two routes have equal cost. Compare cost and
validity, not exact predecessor identity. A mismatch does not automatically
disable center pruning: classify whether it is the accepted rare rigid-profile
approximation, a missing source, or an implementation bug. Known representative
routes must remain valid, while randomized mismatch frequency and cost delta are
reported for an informed rollout decision.

### Live performance

Use the marker scenarios that motivated the design and record, separately for
V1 and V2:

* snapshot/envelope construction milliseconds;
* visited and pending nodes;
* above/below envelope rejections;
* profile-feasibility calls;
* side-ray samples and time;
* total search time and frame count;
* winning cost and reached goal kind.

The optimization succeeds if preprocessing is small relative to the eliminated
search work, futile boundary sheets disappear, representative route validity is
preserved, and any differential cost mismatches are rare and acceptable.

## Rollout

### Stage 0 — Instrumentation

Add a feature flag, envelope diagnostics, and overlay inspection without
rejecting nodes.

### Stage 1 — Shared base transform

Build dense tile-lattice `height32` upper/lower fields and the sequential
snapshot-scoped cache. Support selected-start and request-target sources,
freeze the effective envelope during each search, and update it only between
searches. Compare the tight-request, reusable-area, and monotone-hybrid
fixed-profile policies. Add transform, closure, containment, selective-update,
lifecycle-guard, and conservative-rounding fixtures.

### Stage 2 — Center-height V1 pruning

Enable V1 center rejection, differential-test against unpruned Dijkstra, then
live-test the known boundary expansions and representative fixed seams.

### Stage 3 — Per-lane-center V2 pruning

Enable transition-delta checks for equal flat/ramp straight and strafe moves and
synthetic companions. Confirm that in-place turns inherit admitted centers and
do not run an envelope check. Add band-specific differentials and replay checks.

### Stage 4 — Constraint sources

Verify the confirmed ocean upper source and the constraint-only treatment of
projected designations, props, buildings, and durability.

### Stage 5 — Default-on cleanup

Remove temporary dual-run logging, retain the disable flag as a diagnostic
fallback for at least one release, and keep unpruned Dijkstra fixtures as the
regression oracle.

## Open questions and identified gaps

1. **Are all relevant targets known when the request overlay is built?** Current
   fixed profiles are. Future synthetic/mobile goal types must expose their
   target surfaces to the overlay.
2. **Do G pathing heights ever differ materially from precise terrain?** If so,
   pathing height rather than raw terrain must seed G terminal samples.
3. **Which fixed-profile source policy should be used?** A tight request hull
    excludes non-target profiles and may need an overlay update as clusters
    become providers. A reusable area hull includes every provider-eligible
    profile and remains stable under role promotion but weakens pruning. A
    monotone hybrid retains the current hull, checks new sources for containment,
    and updates only the upper and/or lower field that would expand. Keep this
    open until preprocessing cost, cluster count, containment frequency, and
    lost pruning are measured. Finder-generated surfaces cannot expand either
    field under any policy.
4. **Should raw terrain use every tile or extracted extrema only?** Every tile
    gives a simple exact linear transform. Peak/trough reduction is optional
    only if it produces byte-identical fields.
5. **Should missing envelope samples reject?** No. Fail open initially; missing
    data is a snapshot diagnostic, not a new pathfinding failure.
6. **How should repeated lower-field promotion be bounded?** The turn allowance
   means a finder-generated profile can become a later fixed source below the
   previous raw lower field. Measure whether repeated snapshot/provider cycles
   cause material cumulative widening. If they do, distinguish original
   terminal sources from finder-generated sources or retain provenance so the
   allowance is applied once rather than recursively.
7. **Can the envelope later be direction-aware?** Yes. A state-mode/direction
    cone could prune an outward-rising slope earlier than the shared scalar
    field, but it is history-sensitive and outside this proposal.

## Acceptance criteria

The design is ready to become default behavior when:

* one shared immutable effective envelope serves both V1 and V2, backed by a
  snapshot base and any overlay required by the selected source policy;
* every current terrain/G surface, selected start, and request target is
  represented;
* the fixed-profile source policy is selected from measured preprocessing reuse
  and pruning results rather than assumed upfront;
* hybrid containment checks and selective upper/lower updates produce fields
  identical to a full rebuild from the same sources;
* adding a finder-generated surface as a later provider leaves the upper field
  unchanged and applies the normal containment/update rule to the lower field;
* the sequential cache permits mutation only between cluster searches, and V1,
  V2, replay, and diagnostics share the same frozen envelope during a search;
* preprocessing is linear in snapshot area and has measured acceptable memory;
* envelope checks run before history copying and side-ray cost;
* V1 and V2 replay enforce the same rule as search;
* unpruned-versus-pruned Dijkstra mismatches are captured and classified, with
  no implementation/source defects and no unacceptable representative-route
  regressions; exact exhaustive optimal-cost equivalence is not required;
* fixtures cover V2 per-lane centers, retained-lane and in-place-turn behavior,
  the exact V1 `0.5` and V2 `1.0` lower allowances,
  the confirmed ocean upper source, and the constraint-only treatment of
  projected designations, props, buildings, and durability;
* live marker tests show the useless high/low boundary exploration removed;
* the feature can be disabled for diagnosis without changing any save data.
