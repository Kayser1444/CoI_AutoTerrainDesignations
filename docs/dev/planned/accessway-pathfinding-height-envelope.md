# Accessway Pathfinding Useful-Height Envelope

Status: proposed shared V1/V2 optimization

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

The envelope is shared snapshot infrastructure. V1 and V2 use the same fields
and source data, but apply them to their different generated-state shapes:

* V1 tests the one newly generated `AccessHeightProfile` before history and
  side-ray evaluation.
* V2 tests every profile introduced by a straight, strafe, or turn transition
  before cloning history and evaluating the transition's work and exterior
  rays.
* Existing/fixed profiles and explicit `G` states are not rejected. Their
  surfaces instead contribute envelope sources.

This is graph-domain pruning, not an A* heuristic. It must therefore preserve
the cheapest valid route under both A* and Dijkstra. The implementation should
start with a deliberately conservative rejection test, retain the existing
global vertical bounds as defense in depth, and use exhaustive small-map
comparisons before enabling a stronger test.

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
* tower-ground, fixed-provider, and other snapshot-known terminal surfaces;
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
level per four horizontal tiles. Consider a terrain peak or required terminal
surface at position `q` and height `h(q)`. Its downward construction-grade cone
describes elevations that may be justified by approaching that high surface:

```text
upperCone(q, p) = h(q) - s * distance(p, q)
```

The union of all such cones is the upper useful-height surface:

```text
Upper(p) = max over source samples q of
           [ h(q) - s * distance(p, q) ]
```

Similarly, the upward cones from terrain troughs and required terminal surfaces
form the lower useful-height surface:

```text
Lower(p) = min over source samples q of
           [ h(q) + s * distance(p, q) ]
```

Use cardinal Manhattan distance for the first implementation. Generated V
movement is cardinal and its profiles use the same construction grade. Ignoring
obstacles and turn restrictions makes each cone at least as permissive as the
real graph.

Every captured terrain sample is itself a source. Consequently:

```text
Lower(p) <= terrainHeight(p) <= Upper(p)
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

A route portion lying wholly above `Upper` has elevation unsupported by any
terrain peak or required terminal surface. Lowering the excess portion toward
the upper hull:

* does not remove access to a higher source, because every source's optimistic
  maximum-grade approach is already represented in `Upper`;
* cannot increase direct fill or cut magnitude toward the captured terrain;
* cannot increase the distance needed to return to a source surface;
* preserves the reason for any terminal-height match because terminal surfaces
  are sources.

The inverse applies below `Lower`: raising an unsupported deep portion toward
the lower hull reduces excavation and does not remove access to a lower source.

The continuous argument is persuasive, but graph pruning needs a discrete
proof for the actual flat/slope profile set, exact shared edges, operation
rules, and side-ray feasibility. A local state cannot simply be translated if
its predecessor is fixed at another height. The first implementation must
therefore use the conservative whole-profile rule below and validate optimal
cost equivalence exhaustively. The stronger center-height rule remains a
separate rollout stage.

## Envelope sources

Build the base envelope from every snapshot-known surface that can legitimately
justify height. Including extra sources weakens pruning but cannot create a
false rejection.

| Source | Upper | Lower | Notes |
|---|---:|---:|---|
| Precise captured terrain height | Yes | Yes | Primary peak/trough source. Use precise height, not rounded G height, before conservative fixed-point rounding. |
| Existing fixed terrain-work profile | Yes | Yes | Sample its full bilinear 5x5 target surface. Covers starts, reusable providers, and fixed goals even when their targets differ from current terrain. |
| Tower-reachable and other captured G | Already covered | Already covered | Raw terrain supplies the elevation. Add an explicit source only if its pathing height differs from captured terrain height. |
| Request fixed/provider terminal | Yes | Yes | Normally already present as a fixed profile. A request overlay is required for future synthetic terminal surfaces not present in the base snapshot. |
| Projected cut support ceiling | Question | Likely | May justify a required low join even when no fixed profile exists. Needs semantics review. |
| Projected fill surface floor | Likely | Question | May justify a required raised join even when no fixed profile exists. Needs semantics review. |
| Ocean minimum drivable surface | Likely | No | If a generated fill route may cross ocean, height `1` is a legitimate minimum surface source. Confirm against operation-specific ocean rules. |
| Prop terrain-removal threshold | If required | If required | Prefer a synthetic threshold source over an unrestricted exception. See the prop section. |
| Buildings and hard blockers | No | No | They remain feasibility blockers; they do not justify an elevation. |
| Durability rays | No initially | No initially | They constrain feasibility and work, but do not define a terminal surface. Review if a fixture finds a route whose only valid elevation is justified by durability clearance. |
| Speculative generated V | No | No | Letting search history seed the snapshot would be circular and request-order dependent. |

### Base snapshot versus request overlay

Most useful sources already exist when `AccessSearchSnapshot` is built:
terrain, all fixed profiles, tower ground, ocean, and projected designations.
Build a base envelope once with that snapshot.

Requests currently choose subsets of fixed profiles as goals, so including all
fixed profiles is conservative and reusable. If a future request introduces a
synthetic goal surface not represented in the snapshot, merge its cones into a
small request-scoped overlay. Both V1 and V2 must query the same effective
envelope. Never omit a request terminal merely to retain snapshot-only
preprocessing.

## Numeric representation

Use fixed point. The profile code already evaluates a target surface in units
fine enough to represent quarter-tile interpolation exactly. A candidate
representation is physical height multiplied by 32 (`height32`):

* one physical terrain level is `32` units;
* the construction grade over one tile is `8` units;
* one four-tile V origin step changes at most `32` units;
* every bilinear V profile sample can be compared without floating-point
  epsilon drift.

Convert precise terrain conservatively:

* round upper sources upward;
* round lower sources downward;
* round an upper result upward and a lower result downward if any transform
  operation can lose precision.

This widens the allowed band. It must never narrow it due only to conversion.
The exact scale should be confirmed against
`AccessHeightProfile.GetHeight2NumeratorAt`; reuse that numerator directly if
it already has the required units.

## Preprocessing algorithm

The transform uses a rectangular tile array over the existing snapshot capture
bounds. Missing and physical-map-edge samples are not terrain sources. They do
not stop optimistic cone propagation; horizontal search bounds still reject
states outside the request.

Initialize:

```text
upperSeed[p] = maximum source height sampled at p
lowerSeed[p] = minimum source height sampled at p
```

Then compute:

```text
Upper[p] = max_q(upperSeed[q] - gradeStep * L1(p, q))
Lower[p] = min_q(lowerSeed[q] + gradeStep * L1(p, q))
```

Because the metric is Manhattan and the grade is uniform, use a separable
distance transform rather than one priority-queue search per source:

1. Forward and backward relaxation along every row.
2. Forward and backward relaxation along every column.
3. Repeat for max-minus propagation (`Upper`) and min-plus propagation
   (`Lower`).

Each relaxation is constant work:

```text
upper[current] = max(upper[current], upper[neighbor] - gradeStep)
lower[current] = min(lower[current], lower[neighbor] + gradeStep)
```

The result is `O(snapshot tile count)` time and two dense fixed-point arrays.
If memory is material on the largest tower area, evaluate packed 16-bit values
relative to the snapshot minimum after measuring the real range. Start with
32-bit values for clarity and overflow safety.

Expose a shared helper similar to:

```text
AccessUsefulHeightEnvelope
  TryGetBand(tile, out lowerHeight, out upperHeight)
  IsProfilePotentiallyUseful(origin, profile, out rejection)
```

The envelope should be immutable after snapshot construction.

## Conservative rejection rule

Do not initially define legality as "every profile sample must be inside the
band." A rigid vanilla flat or slope profile may cross a locally non-planar
hull, and the current V set cannot reproduce arbitrary max/min-cone ridges.

For the first rollout, reject a newly generated profile only when all sampled
points in its 5x5 target surface are strictly separated from the relevant hull:

```text
AboveEnvelope when, for every profile sample p:
    targetHeight(p) > Upper(p) + outwardMargin

BelowEnvelope when, for every profile sample p:
    targetHeight(p) < Lower(p) - outwardMargin
```

Use all 25 bilinear samples because feasibility and fulfilled work already
operate on that footprint. An optional one-discrete-profile-level outward
margin can be retained during the first live stage. A profile that touches or
crosses either hull remains searchable.

This rule still collapses large flat-map V sheets: the first transitional slope
may touch the terrain hull, but a continued flat or slope sequence wholly above
or below it is rejected. It is weaker than a center-height boundary, but much
easier to defend while the discrete proof is completed.

### Stronger rule after validation

The intended final rule is to query the envelope at the V state's canonical
pathing sample, normally the profile center, and require:

```text
Lower(center) <= profile.Center <= Upper(center)
```

For V2 this is evaluated for both lane centers or for a canonical band-center
representation proven equivalent to both lanes. This is the direct "every tile
has an allowed pathing-height band" formulation and should prune transitional
states more aggressively.

Enable it only after exhaustive fixtures show that the flat/slope-only graph
never needs to exceed the center envelope to approximate a non-planar hull,
join a fixed edge, form a V1 turn, form a V2 flat 2x2 turn landing, synthesize a
V2 companion, or construct a legal handoff span.

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

* the fixed start profile;
* a fixed/existing-profile successor;
* explicit G nodes;
* a fixed goal during its terminal test.

All of those surfaces must have been included as envelope sources. Replay and
materialization should recheck the same envelope for generated profiles as
defense in depth and report a specific replay reason if the immutable snapshot
and route disagree.

V1 turns do not need a separate field. Their newly generated profile is checked
normally. The conservative full-profile test avoids assuming that a turn's
outer-corner ray is monotone under a local center-height change.

## V2 integration

V2 creates transitions containing one or more newly introduced lane profiles:

* straight: two new profiles;
* strafe: one new profile, with one retained lane as context;
* turn: the first new outgoing slice, while prior flat landing origins remain
  history/context;
* synthetic start companion: one generated profile beside one immutable fixed
  seed.

Apply the shared envelope before `AccessV2History.TryApply` and before
`EvaluateV2Transition` performs work and exterior-ray scoring. Geometry that
does not require history may run first.

For the conservative rollout:

* Test every newly introduced profile.
* Reject a transition if a newly introduced profile is wholly above or wholly
  below its envelope footprint.
* Also inspect retained context when the dominance proof depends on translating
  the entire band. If the retained lane is fixed or previously generated at a
  non-dominated height, do not assume the new lane can be translated
  independently.

The last rule is an identified proof gap. Current enabled V2 bands use equal
flat pairs or equal uniform ramps, which greatly limits the ambiguity. Future
asymmetric/deferred profile pairs must not inherit per-lane pruning without new
fixtures and a band-level proof.

Synthetic companions are evaluated, but the immutable seed is not. Since the
seed profile contributes to the envelope, the companion should normally touch
an allowed cone where its exact shared edge forces the height. Record separate
diagnostics for start-companion envelope rejection so a failure is not confused
with ordinary search exhaustion.

V2 replay should validate every materialized generated origin through the same
shared helper. Explicit G path nodes are outside the envelope rule.

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

The conservative rule plus exhaustive cost-equivalence fixtures is required
until those cases are either proven or represented as envelope constraints.

### Props and removal thresholds

Props are the clearest apparent exception. A higher fill surface may cross the
verified dumping threshold that removes or buries a prop, while a cheaper lower
surface may leave the prop blocking the route. Mining has different removal
semantics.

Do not solve this by allowing arbitrary extra height around every prop. Prefer
one of these, in order:

1. Add the minimum operation-specific terrain-removal height as a synthetic
   envelope source at the affected footprint sample.
2. Prove from the maximum route cost that reaching the threshold can never be a
   winning route and reject it as policy.
3. Exempt only the affected candidate footprint from envelope pruning.

Option 1 preserves the optimization and states exactly how much height the prop
can justify. The snapshot already records prop cleanup/removal policy data, but
the exact elevation threshold available to preprocessing must be confirmed.

### Existing and projected designations

Existing fixed profiles are sources and remain immutable. Projected designation
support ceilings and fill floors need an explicit decision: if they can require
a V route to join at a height not represented by a fixed profile, they must
become sources. If they only reject conflicting candidates, ordinary profile
feasibility remains sufficient.

### Durability and buildings

Buildings never justify climbing or digging around them in the vertical
dimension because direct occupancy and disturbance remain blocked. Durability
may make the cheaper translated route infeasible even when the original excess
route is feasible; that possibility needs a targeted fixture. If confirmed,
either durability contributes synthetic constraint sources or the rejection
test must require that the translated direction remains durability-safe.

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
disabled remains the optimality oracle during rollout.

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

* flat terrain: `Lower == terrain == Upper` away from added sources;
* one peak: exact maximum-grade upper cone in all cardinal directions;
* one trough: exact lower cone;
* two peaks and two troughs: correct max/min union;
* fixed profile above and below terrain;
* target at the snapshot boundary;
* precise fractional terrain with conservative rounding;
* missing capture tiles and physical map edges;
* ocean minimum surface if adopted;
* prop threshold source if adopted.

### V1 fixtures

Cover:

* pointless flat-map climb and descent;
* pointless excavation below flat terrain;
* necessary climb toward a mountain or raised fixed target;
* necessary descent toward a trough or lowered fixed target;
* switchback and 90-degree flat turn near the hull;
* G-to-V and V-to-G handoffs;
* fixed-provider continuation;
* projected designation, ocean, building, durability, and prop interactions;
* both mining/dumping-only and mixed leveling requests.

### V2 fixtures

Repeat the V1 cases for:

* equal flat bands;
* uniform ramp bands;
* straight, strafe, and 2x2 flat turn transitions;
* synthetic start companions;
* fixed two-origin frontages;
* bounded handoff spans and explicit G continuation;
* future asymmetric profiles before they are enabled with envelope pruning.

### Optimality differential

Create small exhaustive or randomized snapshots with a deliberately generous
visited limit. For both widths and every supported operation mode:

1. Run Dijkstra with the envelope disabled.
2. Run Dijkstra with conservative envelope pruning.
3. Require equal success/failure and equal optimal total cost within the normal
   tolerance.
4. Replay both winning routes.
5. On mismatch, save the complete fixture seed and source/envelope arrays.

The route itself may differ when two routes have equal cost. Compare cost and
validity, not exact predecessor identity.

After the conservative rule passes, repeat the differential for the proposed
center-height rule. A single counterexample keeps the conservative rule or
motivates a wider margin/source set.

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
search work, futile boundary sheets disappear, and route cost/validity remain
unchanged.

## Rollout

### Stage 0 — Instrumentation

Add a feature flag, envelope diagnostics, and overlay inspection without
rejecting nodes.

### Stage 1 — Shared base transform

Build terrain and fixed-profile upper/lower fields in
`AccessSearchSnapshot`. Add transform fixtures and validate conservative
rounding.

### Stage 2 — Conservative V1 pruning

Enable whole-profile V1 rejection, differential-test against unpruned Dijkstra,
then live-test the known boundary expansions.

### Stage 3 — Conservative V2 pruning

Enable transition-delta checks for equal flat/ramp bands, synthetic companions,
strafe, and turns. Add band-specific differentials and replay checks.

### Stage 4 — Constraint sources

Resolve and add any required ocean, projected-designation, prop-threshold, or
durability sources found by fixtures.

### Stage 5 — Strong boundary

Evaluate center-height or per-lane-center pruning. Enable only if it preserves
optimal costs across the full differential suite and live routes.

### Stage 6 — Default-on cleanup

Remove temporary dual-run logging, retain the disable flag as a diagnostic
fallback for at least one release, and keep unpruned Dijkstra fixtures as the
regression oracle.

## Open questions and identified gaps

1. **What exactly is the discrete dominance test?** The whole-profile-separated
   rule is conservative but weaker. The center-height rule matches the intended
   per-tile boundary but still needs a flat/slope graph proof.
2. **Which coordinate lattice owns the field?** A tile field matches terrain,
   rays, and 5x5 profile samples. A four-tile origin/corner field is smaller and
   closer to legal profile transitions. The initial recommendation is a tile
   field; measure memory before reconsidering.
3. **What is the exact fixed-point unit?** Confirm the numerator returned by
   `GetHeight2NumeratorAt` and avoid duplicate conversion rules.
4. **Are all relevant targets known at snapshot construction?** Current fixed
   profiles are. Future synthetic/mobile goal types require a request overlay.
5. **Do G pathing heights ever differ materially from precise terrain?** If so,
   pathing height rather than raw terrain must seed G terminal samples.
6. **How should projected cut ceilings and fill floors participate?** Determine
   whether they can justify a required join height or only forbid conflicts.
7. **What exact prop elevation makes terrain work remove each prop class?** The
   preprocessing layer needs a stable operation-specific threshold to use
   synthetic sources safely.
8. **Can durability or side-ray termination make an excess-height state uniquely
   feasible?** Targeted fixtures must either rule this out or identify synthetic
   constraints/exemptions.
9. **How is a V2 band judged when one new lane is outside the envelope but its
   retained lane is fixed or valid?** Current equal-profile bands reduce the
   problem; asymmetric bands need a band-level dominance proof.
10. **Should the base envelope include every fixed profile or only accepted
    providers?** Every fixed profile is safer and reusable but weakens pruning
    near unrelated designations. Start with every fixed profile.
11. **Should raw terrain use every tile or extracted extrema only?** Every tile
    gives a simple exact linear transform. Peak/trough reduction is optional
    only if it produces byte-identical fields.
12. **What outward margin is needed during rollout?** Start with at least strict
    separation and conservative numeric rounding. Compare zero and one-profile-
    level margins in differential fixtures.
13. **Should missing envelope samples reject?** No. Fail open initially; missing
    data is a snapshot diagnostic, not a new pathfinding failure.
14. **Can the envelope later be direction-aware?** Yes. A state-mode/direction
    cone could prune an outward-rising slope earlier than the shared scalar
    field, but it is history-sensitive and outside this proposal.

## Acceptance criteria

The design is ready to become default behavior when:

* one shared immutable snapshot envelope serves both V1 and V2;
* every current terrain, start, fixed-provider, and goal surface is represented;
* preprocessing is linear in snapshot area and has measured acceptable memory;
* envelope checks run before history copying and side-ray cost;
* V1 and V2 replay enforce the same rule as search;
* unpruned and pruned Dijkstra agree on success and optimal cost across the
  exhaustive differential suite;
* prop, projected-designation, ocean, durability, and V2 retained-lane questions
  have explicit fixture-backed answers;
* live marker tests show the useless high/low boundary exploration removed;
* the feature can be disabled for diagnosis without changing any save data.
