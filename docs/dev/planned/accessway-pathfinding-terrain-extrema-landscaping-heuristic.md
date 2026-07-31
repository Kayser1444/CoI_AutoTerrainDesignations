# Terrain-Extrema Landscaping Heuristic

Status: proposed landscaping heuristic; recursive diamond memoization is one
replaceable extrema-query strategy

Drafted: 2026-07-26

Architecture note (2026-07-30): V2 no longer has fixed-frontage terminals or
provider terminal fees. References to them below describe the earlier baseline;
the current equivalent is reaching compatible projected G/FV navigation, whose
exact suffix cost belongs to the route potential.

Related designs:

* [Accessway Pathfinding](../done/accessway-pathfinding.md)
* [Accessway Pathfinding Side-Ray Landscaping Cost Amendment](../done/accessway-pathfinding-side-ray-cost.md)
* [Accessway Pathfinding Useful-Height Envelope](../done/accessway-pathfinding-height-envelope.md)
* [Unified Goal Search and Snapshot Potential Heuristic](../in-progress/unified-goal-search-snapshot-potential-heuristic.md)

## Summary

Add a conservative lower bound for unpaid future landscaping work to the V2 A*
heuristic.

The heuristic proof is independent of the terrain-extrema evaluation strategy.
Direct evaluation of the exact relaxed transition envelope is the reference
semantics. Exact-key caching and opportunistic reuse must reproduce its extrema;
recursive Manhattan diamonds may instead evaluate a conservative superset,
which is safe but potentially weaker. Measurements select among them.

For a generated V2 state, split the estimate into two independent problems:

1. find favorable terrain extrema over charge-bounded relaxed V2 reach and prove
   how many charge-owning slices must occur before terrain or another compatible
   terminal can be reached; and
2. independently prove a conservative work gap and convert it, together with
   a charge horizon, into a lower bound for direct landscaping work plus
   ordinary exterior side-ray work.

The charge horizon and work gap are deliberately separate. Terrain-contact
geometry proves how many charge-owning slices are unavoidable while allowing
each to descend as favorably as possible. The terrain separation used by that
proof must not be reused as though every future landscaping sample had the same
gap.

Construct the exact charge-bounded relaxed V2 transition envelope. A legal
U-turn can occasionally be the cheapest route, so the relaxation cannot safely
remove its rearward states. The envelope preserves real transition geometry,
profile compatibility, charge ownership, and the post-turn ramp obligation, but
ignores obstacles, history, bounds, ocean, and candidate feasibility.

Precompute the relative contact and work offset sets for each relevant relaxed
state class and charge prefix. At runtime, translate those masks and directly
scan their exact terrain samples. Calculate both extrema during a scan:

```text
E(domain) = (
    minimum precise terrain height over domain,
    maximum precise terrain height over domain)
```

An above-ground dumping state uses the maximum. Its approximate below-ground
mining mirror uses the minimum. Calculating both requires no extra terrain
reads and one additional comparison accumulator.

Start without recursive spatial memoization. Measure exact scan cost, translated
query repetition, and overlap between neighbouring masks before adding whole-
query caching or incremental reuse. Recursive Manhattan diamonds remain an
optional conservative comparison backend; they do not define the first
implementation.

The initial dumping-work conversion is deliberately simple and fixed:

```text
integer work-gap grid: 0..8 physical levels, step 1
charge horizon K:      0..7 future charge-owning slices
lookup:                lower gap endpoint and capped charge prefix
interpolation:      none
```

Generate the table from the same synthetic straight maximum-grade V2 landing and
shared landscaping scorers used by the proof. Select the dumping material in the
same way as the real scorer, derive one conservative effective dumping slope,
and freeze it for the table. Cache or precompute the table by its immutable
configuration.

The corresponding mining table later uses a fixed slope from the least runny
normal in-ground material present in the tower area. This is the steepest
available cut slope and therefore gives the smallest synthetic cut wedge.

More precise gap grids, interpolation, exact large-gap evaluation, filtered
landing extrema, and alternate heuristic-only material slopes are later
refinements rather than first-pass requirements.

## Priority and purpose

Implement and measure the useful-height envelope first. It removes generated
states from both A* and Dijkstra before expensive candidate work and is expected
to have a larger first-order benefit.

This heuristic is a later A*-only ordering refinement for the V2 states that
remain inside the frozen request-effective envelope. It succeeds only if the
reduction in visited states, pending high-water, and total search time exceeds:

* terrain-extrema query time;
* terrain-mask scan and query-cache time;
* work-table construction or cache lookup time;
* additional cache memory for paired extrema; and
* any extra queue work caused by inconsistency.

The first implementation is a non-persistent experiment behind a runtime flag.
It adds no save-state data and performs no gameplay mutation. Any invariant,
configuration-identity, or required-coverage failure returns zero landscaping
heuristic for the affected evaluation.

Do not enable it by default until paired A* and Dijkstra validation agrees on
success, selected total cost, and cost breakdown, and live measurements show a
net reduction in end-to-end search time and queue pressure after including
mask-generation, mask-scan, and table-lookup overhead. A reduction in visited
states alone is not success.

## Initial implementation decisions

The first implementation settles the following choices:

1. Use the exact charge-bounded relaxed V2 transition envelope as the semantic
   contact domain.
2. Include every authoritative precise terrain sample selected by the semantic
   domain or its conservative query superset, including samples outside the
   tower area and samples on ocean tiles.
3. Do not run V-origin eligibility, tower-bound, ocean, building, history, or
   candidate-feasibility checks inside the extrema query.
4. Cache both exact minimum and exact maximum terrain heights for any reused
   query primitive.
5. Precompute exact relative contact and work offset sets for charge prefixes
   `1..7`.
6. Directly scan translated exact masks first; add caching or overlap reuse only
   after measuring that baseline.
7. Implement dumping work first.
8. Use a fixed `Wdump` table over integer work gaps `0..8` and charge horizons
   `0..7`.
9. Use lower-endpoint lookup without interpolation.
10. Select the dumping material as the real scorer does and freeze one fixed
    conservative effective slope for the table.
11. Define the later mining counterpart with the least runny normal in-ground
    material found in the tower area.
12. Keep the charge-horizon proof separate from the work gap passed to
    `Wdump`.
13. Use the earliest-any-contact charge-indexed reach predicate to derive the
    baseline terrain charge horizon. Defer stronger complete-frontage
    aggregation until the baseline is validated.
14. Stop the charge horizon at the first useful projected-ground endpoint
    (physical G or FV). Do not use route-potential cost, contact distance, or
    a separate fixed-terminal charge count to derive that horizon.

These choices favor a small, verifiable implementation. Strengthening the
heuristic is deferred until the baseline has been validated against Dijkstra
and measured on live requests.

## Scope

The first implementation covers:

* V2 generated states above terrain;
* dumping-only future landscaping debt;
* tower-ground terrain terminals;
* direct generated-origin work;
* the two ordinary exterior side rays owned by a straight V2 slice;
* immutable precise terrain captured in the access snapshot;
* exact unfiltered extrema over charge-bounded relaxed V2 contact reach;
* exact unfiltered extrema over the swept charged work domain;
* precomputed relative offset masks and direct translated scans;
* a fixed `9 x 8` dumping-work table; and
* A* queue ordering only.

It deliberately does not initially include:

* mining or mixed-leveling work conversion;
* manually cropped geometric approximations of the relaxed envelopes;
* recursive Manhattan-diamond evaluation;
* extrema-query caching or incremental overlap reuse before baseline
  measurements;
* tower-area or ocean filtering in extrema queries;
* generated-history exclusions;
* visited-origin or neighbour removal;
* candidate feasibility, buildings, durability, cleanup, or projected-work
  checks inside the extrema query;
* turn-owned frontal rays;
* fixed generated overhead or traversal cost already represented by the
  existing potential;
* interpolation or adaptive delta intervals;
* exact work evaluation above `delta = 8`;
* a heuristic-only dumping material or slope different from the real scorer;
  or
* changes to real edge cost in `g`.
* a nonzero fixed-terminal charge horizon.

## Coordinates and units

Use a relaxed tile-centred coordinate system for the extrema proof and charge
horizon:

```text
one cardinal relaxed step = one terrain tile
maximum relaxed grade     = 0.25 physical height per step
one V2 origin stride      = 4 relaxed steps
```

Path profiles remain represented by the authoritative V2 height type. Convert
the relevant scalar profile height to the same physical-height unit used by the
snapshot's precise terrain samples before calculating a gap.

For a candidate charge count `k`, define:

```text
RelaxedStates(state, k)
    = states reachable after at most k charge-owning slices in the relaxed
      V2 transition graph

ContactReach(state, k)
    = union Scontact(s) for s in RelaxedStates(state, k)
```

The relaxed graph preserves real band displacement, orientation, enabled
flat/ramp profile compatibility, charge ownership, zero-charge turns, and the
authoritative requirement that the next move after a turn is a charge-owning
ramp straight. Zero-charge turns therefore cannot produce unbounded free
spatial drift. It ignores obstacles, concrete history availability, tower
bounds, ocean, and candidate feasibility, making the resulting set a superset
of real reach.

Because `k <= 7`, precompute the relative relaxed states and contact offsets by
enabled band profile, orientation, turn-pending state, and `k`, then translate
them to the current state. Direct extrema over those offsets define the
reference result. A proved geometric superset remains admissible but is a
separate, potentially weaker evaluation strategy.

Generate the relaxed states through the authoritative V2 straight, strafe, and
turn geometry—or a pure transition core shared with production search. Bypass
obstacle, history-availability, bound, ocean, and feasibility validators, but do
not duplicate:

* anchor displacement or orientation changes;
* enabled band-profile advancement;
* turn geometry or the post-turn ramp obligation; or
* generated-origin ownership.

Classify a transition as one charge exactly when its authoritative `Delta` is
nonempty. A turn with empty `Delta` consumes no charge even though it consumes a
traversal move.

The concrete V2 state has a nonzero two-lane footprint. Map it through two
separate support sets:

```text
Scontact = samples through which any legal terrain terminal can first form
Swork    = six unique physical corner positions of the current two-lane band
```

The sets serve different proofs and must not be merged merely for convenience.
They may nevertheless use the same atomic terrain-extrema cache.

Each of the two adjacent lane origins contributes the same four corner samples
used by the authoritative direct-work scorer. The two seam corners are shared,
so their geometric union contains six positions. Geometric deduplication applies
only to terrain-extrema queries: the synthetic cost scorer must preserve the
authoritative per-lane multiplicity and therefore charge each shared corner once
for each lane. The two ordinary straight exterior-ray roots are already members
of this six-position support; terrain beyond those roots is covered by the work
domain and its side-ray containment proof.

The contact ceiling for candidate `k` is the largest precise terrain height in
the charge-indexed contact reach:

```text
Fcontact(state, k) = max terrain(q) for q in ContactReach(state, k)
```

The work ceiling is calculated independently over a work domain seeded by
`Swork`:

```text
Fwork(state, WorkDomain) = max terrain(q) for q in WorkDomain
```

For the dumping baseline:

```text
deltaWork = max(0, currentBandFloor - Fwork(state, WorkDomain))
```

This scalar is measured from the band floor. It does not flatten the ramp
geometry: the table generator reconstructs the exact favorable descending
profile and scores its corners through the shared work-cost helpers. Optional
strafes, turns, level moves, and climbs are not added to the mandatory
ramp-down-slice prefix represented by `Kterrain`.

The later mining counterparts use the corresponding cached minima over their
contact and work supports. The atomic cache remains keyed only by
`(tile, radius)`; footprint handling is a small constant number of extrema
lookups above it.

## Step 1: favorable terrain extrema and charge horizon

### Full unfiltered diamond

For a terrain sample position `c` and nonnegative integer radius `r`, define:

```text
D(c, r) = { q | ManhattanDistance(c, q) <= r }

L(c, r) = minimum exact precise terrain height over q in D(c, r)
U(c, r) = maximum exact precise terrain height over q in D(c, r)
E(c, r) = (L(c, r), U(c, r))
```

The initial extrema source includes every authoritative precise terrain sample
inside the geometric diamond. It does not ask whether the sample:

* lies inside the tower area;
* lies on an ocean tile;
* can host a generated V origin;
* is blocked by a building or designation; or
* remains reachable under the current history.

A position outside the tower area or on ocean cannot normally host a V landing.
Including its ground is nevertheless admissible:

* an additional high sample can only raise the dumping maximum, reduce the
  dumping gap, and weaken the heuristic; and
* an additional low sample can only lower the mining minimum, reduce the mining
  gap, and weaken the heuristic.

The useful-height envelope should already prevent many searches that would
require large translated masks outside the practical domain. Proper candidate
evaluation also restricts exploration into ocean. Avoid paying a per-sample
eligibility cost until measurements show that unfiltered extrema materially
weaken the heuristic.

For one candidate dumping charge count:

```text
Hfloor        = currentBandFloor
Fcontact(k)   = maximum precise terrain height in ContactReach(state, k)
safe(k)       = Hfloor - k > Fcontact(k)
```

`Fcontact(k)` is a favorable terrain ceiling, not necessarily the height of a
concrete reachable landing point. The point supplying the maximum may be
blocked, unreachable, outside the tower area, on ocean, or located in a
direction that the eventual route does not take. Those relaxations can only
raise `Fcontact(k)`, make `safe(k)` harder to prove, and weaken the dumping
heuristic.

For the later below-ground mining counterpart, use the symmetric floor and
reverse the inequality:

```text
Fcontact(k)   = minimum precise terrain height in ContactReach(state, k)
safe(k)       = Hceiling + k < Fcontact(k)
```

The same recursively constructed extrema entry therefore serves both operation
classes.

### Terrain-contact proof

Every charge-owning slice can lower the band floor by at most one physical
level. After at most `k` such slices, even its most favorable possible floor is:

```text
futureBandFloor >= currentBandFloor - k
```

Every support through which terrain contact could occur during those slices lies
inside `ContactReach(state, k)` and has terrain height no greater than
`Fcontact(k)`. If `safe(k)` holds, no legal terrain contact can occur through the
end of the `k`th charged slice.

The mining proof is sign-symmetric. This proves the charge count directly; no
separate contact-distance or contact-horizon quantity is introduced.

### Future complete-frontage strengthening

A legal terminal form may require several contacts. All contacts required by
one form are an AND condition, while the available terminal forms are
alternatives:

```text
blockedForm(t, k)
    = any mandatory contact c of form t is proved impossible through charge k

safeFrontage(k)
    = every legal terrain-terminal form t satisfies blockedForm(t, k)
```

One impossible mandatory contact blocks its complete terminal form, but every
alternative terminal form must be blocked before `k` is a safe charge horizon.
This is the charge-indexed equivalent of taking the slowest mandatory contact
inside a form and the earliest available form across alternatives; it does not
introduce a separate contact horizon.

Ordinary paired mining and dumping, leveling bridges, staggered extensions,
lateral exits, and post-turn exits do not automatically have the same required
contact set. Apply maximum-contact strengthening only to a terminal form whose
complete requirements and competing alternatives are covered by the proof.
An uncovered legal terminal form contributes zero until a safe lower bound is
available.

Do not pass a contact separation from this strengthening to the work table unless a
separate proof establishes that gap at every landscaping sample charged by the
table.

### Baseline terrain charge horizon

The first implementation does not enumerate terminal forms. It instead tests
each capped candidate charge count directly:

```text
safe(k) = currentBandFloor - k > Fcontact(state, k)

Kterrain = largest k in 1..7 for which safe(k) holds,
           or 0 if safe(1) does not hold
```

`currentBandFloor` is the minimum physical corner elevation of the current
enabled two-lane band. Enabled V2 bands have matching flat or uniform-ramp lane
profiles.

The transition proof is:

```text
straight ramp-down  lowers the band floor by at most 1 physical level
other straight     does not lower it faster
strafe             preserves the band floor
turn               preserves the band floor and requires a flat landing
every V2 move      advances 4 relaxed cardinal steps
```

The relaxation may therefore pretend that every charge-owning slice is a
straight ramp-down:

```text
bandFloorAfterCharges(k) >= currentBandFloor - k
```

Only a straight ramp-down can consume one level of separation, and that
transition owns a new slice. Strafe owns work but does not lower the band; turn
neither owns a new slice nor lowers the band. Because `safe(k)` proves that
contact has not occurred through the `k`th charged slice, all `k` slices may be
passed to the work table.

The conservative terrain query domain must include every support through which
a legal straight, strafe, turn, lateral exit, staggered exit, leveling bridge,
mining handoff, or dumping handoff could first contact terrain.

For the baseline, `Scontact` is the full discrete perimeter of the current
`4 x 8` two-lane band. Mining and dumping use edge-corner crest crossings,
leveling may bridge through any pathable sample along an edge, and lateral or
post-turn exits may make a side edge the outgoing frontage. Retain the rear edge
until a separate reversal-dominance proof permits removing it.

The semantic contact-query domain for candidate `k` is:

```text
ContactReach(state, k)
```

The baseline evaluates the exact relaxed envelope. A coarse backend may instead
prove a charge-indexed drift radius `Rcharge(k)` and use the union of
radius-`Rcharge(k)` Manhattan diamonds centered at every current
contact-support sample. Any conservative geometric superset is valid, but a
larger domain can only raise the dumping ceiling and weaken the charge horizon.

If even the lowest possible carried support remains above the highest favorable
terrain throughout the charge-indexed reach, no legal terrain-terminal form can
complete. This baseline is weaker than complete-frontage aggregation but yields
a charge count directly without using a separate contact distance.

After the baseline is validated, individual terminal forms may strengthen the
terrain charge count with the aggregation above. Measure that strengthening
independently so its pruning benefit is not confused with the work-table or
extrema-query strategy.

### Why reach cannot be cropped by the initial travel direction

V2 can construct legal 90-degree turns after a flat landing, and a sufficiently
extreme obstacle or terrain arrangement can make a U-turn the cheapest valid
route. A rearward state is therefore not generally dominated. The exact relaxed
envelope retains every orientation and position reachable within the tested
charge prefix; it is directional only where the authoritative transition rules
make it so.

Do not manually crop that envelope to a front half. A direction-specific subset
is valid only when it follows from the preserved transition rules or a separate
dominance proof.

### Projected-ground-aware endpoint horizon

`Kcharge` counts future charge-owning slices before a useful projected-ground
endpoint: physical G or FV. It is neither a route-distance estimate nor a
fixed-terminal charge count, and must not be derived from route potential.

The initial proof uses the earliest-any-contact charge-indexed reach predicate.
It may stop once a useful projected-ground endpoint is reachable, but it does
not crop the extrema domain to one chosen endpoint: ordinary side-ray support
can extend laterally beyond it. A tighter endpoint-specific domain requires a
separate containment proof.

### Charge-owning generated slices

The current state's landscaping work is already in `g` and must not be included
in the heuristic.

Straight and strafe transitions own new V2 origins and therefore own work.
Turns reorient already admitted terrain and own no new origins. A compatible
terminal may replace the slice at its boundary. A turn still consumes one
traversal move and expands the spatial reach of a charged prefix, but it does
not advance the charge index. `Kcharge` therefore counts only future
nonterminal work-owning slices; it is neither total move count nor a value
derived by dividing total travel distance.

## Step 2: synthetic landscaping-work lower bound

### Exact swept work domain

For a proven `Kcharge > 0`, collect every straight or strafe transition that
can appear among the first `Kcharge` charge-owning transitions of any path in
the exact relaxed V2 envelope:

```text
ChargedTransitions(state, Kcharge)
    = every straight or strafe transition whose charge ordinal is <= Kcharge
      on a relaxed continuation from state
```

Turns do not appear in that set, but their real displacement and orientation
change remain in the relaxed paths that produce it.

For each collected transition, add:

* all four direct-work corner samples of every owned origin;
* every applicable ordinary-straight or strafe exterior-ray root; and
* every terrain tile traced from each root through the configured
  `AccessCandidateRayMaxDistance`.

The exact geometric union is `WorkDomain(state, Kcharge)`. It is a swept,
usually directional shape rather than a Manhattan diamond. For example, when
the transition rules do not permit a U-turn within the charged prefix, the
domain retains that forward bias. Derive the shape from the transitions rather
than hard-coding a half-diamond assumption. Ray traces add narrow lateral arms
to the swept direct-work support.

As with contact reach, precompute relative work offsets by enabled band profile,
orientation, turn-pending state, and charge prefix, then translate them to the
current state. Ignore turn-owned frontal rays because their nonnegative work is
omitted from the heuristic.

At runtime:

```text
Fwork(state, Kcharge)
    = maximum precise terrain height in WorkDomain(state, Kcharge)

deltaWork
    = max(0, currentBandFloor - Fwork(state, Kcharge))
```

Use this one global maximum as the favorable terrain elevation for every direct
sample and ray in the complete charged prefix. A high sample that only one
continuation or one ray could exploit is therefore allowed to raise the
synthetic plane everywhere. That relaxation can substantially weaken the bound,
but it cannot strengthen it.

Do not initially calculate a separate ceiling per charged ordinal, transition,
origin, corner, or ray. Such localized extrema would require a vector-valued
work input or a more complex runtime evaluator and are a later strengthening to
measure independently.

If `Kcharge = 0`, return zero landscaping heuristic without constructing or
querying a work domain.

### Artifact ownership

Keep artifacts at the narrowest lifetime implied by their inputs:

* contact-mask templates are process-static and depend only on the compiled
  authoritative V2 transition geometry;
* work-mask templates are additionally keyed by configured ray-trace distance;
* `Wdump` is keyed by every scorer setting that changes synthetic values; and
* any later cache of translated terrain extrema is scoped to the immutable
  terrain snapshot and its coverage identity.

If an artifact key does not match, rebuild that artifact or return zero for the
affected heuristic evaluation. Never reuse a partially matching artifact.

### Synthetic straight landing

For each residual height gap and relaxed horizon, score an idealized two-lane
straight V2 descent over flat terrain at elevation zero.

Call the independently established residual input `deltaWork`. It must
lower-bound the terrain-to-profile gap at every direct-work and ordinary
exterior-ray sample charged by the synthetic route. It may be smaller than the
gap used to prove one or more mandatory terminal contacts.

The synthetic continuation is more favorable than real V2:

* it begins in the cheapest descending profile phase, as though the band were
  already on a uniform maximum-grade descent;
* each four-tile slice may lower its centre by one full physical level;
* it never turns or strafes;
* all terrain beneath and beside it is flat at the favorable ceiling;
* it has no buildings, ocean failures, durability, history, cleanup, or
  projected-work conflicts;
* it pays no traversal or generated fixed overhead; and
* it terminates without an additional landscaping charge.

Use the actual descending V2 profile geometry in the table generator, including
its outgoing edge and corner heights. Do not score a flat band at its centre
height when a descending ramp exposes a lower outgoing edge; that would
overstate the minimum side-ray work.

### Why straight descent dominates the synthetic alternatives

The straight-only route is a proved optimum of the synthetic relaxation, not an
assumption about real nonuniform terrain.

On the favorable flat terrain used by `Wdump`:

* a maximum-grade ramp-down straight lowers the band floor by one level per
  charged slice;
* a strafe consumes a charged slice while preserving the band elevation;
* straight and strafe each own two generated origins and two exterior rays;
* direct dumping work is nondecreasing in every profile sample height;
* ray dumping work is nondecreasing in its root height when terrain, effective
  material slope, weights, and caps are fixed; and
* positions and cardinal orientation are interchangeable on the unbounded
  isotropic plane.

Replacing a strafe by a maximum-grade ramp-down straight therefore cannot
increase either direct or ray work. A turn consumes no charge, preserves height,
and merely rotates the remaining descent, so removing it or rotating the suffix
cannot increase synthetic work either. Repeating these exchanges converts every
synthetic sequence with `k` charged slices into the straight maximum-grade
descent with cost no greater than the original sequence.

Turn-owned frontal rays are omitted from the relaxation. Their real cost is
nonnegative and cannot invalidate the dominance result.

For future slice `k`, the relaxed centre gap may be no greater than:

```text
centerGap(k) = max(0, deltaWork - k)
```

when `deltaWork` is expressed in physical levels and one V2 origin stride descends
one level. The authoritative table generator should construct the synthetic
profile and invoke shared cost helpers rather than rely on this scalar
expression for corner work.

### Direct work

Score both newly generated lane origins with the same direct-work normalization
used by the real V2 transition evaluator. Flat terrain at the maximum possible
terrain ceiling minimizes positive fill at every direct-work sample.

Apply:

* direct-work weight exactly once; and
* landscaping distance scale exactly once.

Do not include current-state work.

### Exterior side-ray work

Score only the ordinary exterior rays owned by a straight V2 slice. Use:

* the same cost-sample distances and rectangle-rule integration as the real
  scorer;
* the same maximum ray distance and cost cap;
* the same side-ray weight and landscaping distance scale; and
* one fixed effective material slope selected for the table configuration.

The initial lower-bound model may omit unresolved penalties and every fatal
condition. Their omission only weakens the heuristic. If later fixtures prove
that a synthetic unresolved penalty is itself unavoidable under the same
ceiling proof, it may be added separately.

Do not add turn-owned frontal rays. The synthetic route has no turns, while a
real turn can only add exposed-face work.

### Fixed dumping material and slope

Choose the dumping material in the same way as the real scorer for the current
tower/accessway configuration. In the current scorer design, this means the
runiest allowed disturbed dumping material.

Derive one effective slope from that material, including the scorer's fixed
conservative safety treatment, and freeze it for the entire `Wdump` table:

```text
mDump = fixed effective slope selected once for the dumping configuration
```

Do not inspect terrain material or vary the slope by synthetic slice or ray. The
fixed slope is part of the table key. Requests with the same immutable
landscaping configuration can reuse the same precomputed table.

Using the same material as the real scorer gives the strongest initial synthetic
bound under the simplified flat-terrain model. A future refinement may use a
steeper, less runny heuristic-only material slope. That would reduce synthetic
side work and weaken the heuristic, but could make a table reusable across a
wider set of dumping configurations.

### Full-trace containment proof

The exact work domain contains every direct terrain sample and every ray terrain
sample that any of the first `Kcharge` real charged transitions can inspect,
through the same configured trace-distance cap as the authoritative scorer.
The proof therefore does not depend on a material-slope-derived radius or on a
circular assumption about `deltaWork`.

Every actual terrain sample in `WorkDomain` is at or below `Fwork`. Replacing
all of them by flat terrain at `Fwork` can only:

* reduce positive direct fill;
* make each fill ray intersect terrain earlier; and
* reduce or preserve integrated side-ray cost.

The straight-dominance lemma then converts every such flat-terrain charged
prefix to the maximum-grade straight landing at no greater cost. That landing
therefore lower-bounds the unpaid direct-plus-side landscaping work of every
concrete continuation that cannot terminate earlier than the proven charge
horizon.

## Initial `Wdump` table

### Dimensions

For each immutable dumping configuration, precompute:

```text
WdumpTable[deltaIndex, K]

deltaIndex = 0..8
K          = 0..7 future charge-owning slices
```

The table contains `9 x 8 = 72` values. The maximum charge index is derived
from the work-gap cap: row eight has positive residual gap only for future
slices one through seven. Later prefixes add no baseline work.

Each row uses an exact integer synthetic gap in physical levels:

```text
deltaTable = deltaIndex
```

Each column contains the cumulative synthetic landscaping work of the selected
number of future charge-owning V2 slices:

```text
WdumpTable[d, K]
    = cumulative direct and exterior-side work
      of the first K synthetic future slices
```

The table generator may score one synthetic route per integer `delta` and store
all cumulative horizon prefixes from that run.

### Runtime lookup

Given exact runtime `deltaWork` and proven charge horizon `Kcharge`:

```text
d = min(8, floor(max(0, deltaWork)))
k = min(7, max(0, Kcharge))

H_land = WdumpTable[d, k]
```

There is no interpolation.

The lookup uses the lower integer gap endpoint. Because synthetic work must be
nondecreasing in `deltaWork`, this cannot exceed the synthetic work at the exact
work gap. Capping a larger gap at `8` and a longer charge horizon at `7`
likewise returns only a safely accumulated prefix.

This baseline is intentionally weak for:

* gaps below one level;
* noninteger gaps near the next integer boundary;
* gaps above eight levels; and
* unavoidable horizons beyond seven charge-owning slices.

Those cases are expected to be less common or more expensive, but the actual
distribution must be measured.

### Table configuration and caching

The table key must include every immutable setting that changes the synthetic
score, including:

```text
V2 synthetic profile geometry/version
fixed effective dumping slope
selected dumping-material identity, when relevant
direct-work weight
side-ray weight
landscaping distance scale
side-ray sample schedule
maximum ray distance
post-termination ray buffer
maximum ray cost
unresolved-ray penalty and behavior
```

Precompute the table when the configuration is first encountered, then cache it
for reuse. If all relevant settings are global and immutable during a game
session, one table may serve every request with the same dumping material and
slope. Otherwise cache by configuration identity.

Table construction must use the same shared direct-work and side-ray scoring
helpers as the real scorer wherever practical. It must not duplicate a subtly
different numerical integration.

Generate the table at runtime; do not serialize its values. After calculating a
nonzero cell through the shared `float` scorers, store the next representable
`float` toward zero. Leave zero unchanged. This one-ULP weakening prevents a
cached cell from exceeding its generated scorer result while avoiding a
meaningful heuristic loss.

### Required monotonicity

Validate:

```text
WdumpTable[d + 1, K] >= WdumpTable[d, K]
WdumpTable[d, K + 1] >= WdumpTable[d, K]
```

A cap may make the function flat but must not make it decrease.

The safety of lower-endpoint and capped lookup depends on this monotonicity.
Treat a violation as a scorer/table-generation defect, not as a reason to sort
or repair values after generation.

## Later mining counterpart

The mining extension reuses the same exact masks and paired extrema evaluator
and defines a separate synthetic work table, referred to here as `Wmine`:

```text
WmineTable[deltaIndex, K]
```

Use the same initial dimensions and lookup policy unless measurement justifies a
different range:

```text
deltaIndex = 0..8, step 1
K          = 0..7 future charge-owning slices
lookup     = lower endpoint and cap
```

### Fixed mining material slope

The real mining scorer samples the normal in-ground material at each cut ray.
A location-independent heuristic table cannot do that.

Inspect the normal in-ground terrain materials present anywhere in the tower
area and select the least runny one:

```text
mMine = maximum effective stable slope among tower-area normal materials
```

With slope represented as vertical change per lateral tile, the least runny
material has the largest slope. It produces the shortest synthetic cut ray and
the smallest side wedge. Every actual local material in the tower area is at
least as runny or equal, so its real cut-side work cannot be lower merely because
of material slope.

Freeze `mMine` for the mining table and include it in the table key. Rebuild or
select another cached table when the tower-area material set changes.

If no authoritative tower-area material slope can be obtained, fail weak by
omitting the mining side-ray component or returning zero mining landscaping
heuristic. Do not guess a runnier slope, which could overstate unavoidable work.

### Mining synthetic route

The mining table is sign-symmetric in terrain gap but not identical in scorer
semantics. It should use:

* the cached minimum terrain floor;
* a straight maximum-grade synthetic ascent toward that floor;
* the same direct cut-work normalization as the real scorer;
* ordinary exterior cut rays only;
* the fixed least-runny tower-area normal-material slope; and
* mining-specific map-edge, ocean, cap, and unresolved behavior weakened as
  necessary for admissibility.

Implement and validate dumping first. Define the mining table now so the paired
extrema cache and table-cache architecture do not need to be redesigned later.

## Archived recursive-diamond backend

The recursive Manhattan-diamond comparison backend is archived in
[Recursive-Diamond Terrain-Extrema Backend](../superseded/accessway-pathfinding-terrain-extrema-diamond-backend.md).
The active plan deliberately uses exact translated swept-mask scans instead.
## Future refinements

### Filtered landing extrema

A position outside the tower area or on ocean cannot host a generated V landing.
A stronger future horizon proof may maintain separate values:

```text
F_landing = extremum over proven eligible landing positions
F_support = extremum over all terrain that may reduce direct or side-ray work
```

Use `F_landing` to strengthen the terrain charge horizon and `F_support` to
preserve the work lower bound. Do not simply filter the shared extremum: terrain at an
unlandable position may still physically reduce work performed by an in-bounds
slice.

Before implementing this split, measure:

* how often the selected extremum lies outside the tower area;
* how often it lies on ocean;
* how much those samples reduce `delta` and `H_land`; and
* the cost of eligibility checks compared with the saved A* expansions.

### Work-table precision

The fixed integer table is the baseline. Potential future improvements include:

* half-, quarter-, or adaptive gap intervals;
* per-horizon adaptive grids;
* a shared adaptive grid bounded by maximum work loss;
* exact evaluation above a configured large-gap threshold;
* exact lazy memoization for encountered gaps;
* a proven lower piecewise approximation; or
* interval-specific analytical integration matching the discrete scorer.

Ordinary linear interpolation between exact samples is not automatically safe.
Where the synthetic function is convex, the chord can lie above the true
function. Any interpolation must be proven not to exceed the synthetic scorer
throughout its interval.

### Alternative heuristic material slopes

The initial dumping table uses the same material selection as the real scorer.
A future heuristic-only table may use a less runny material or otherwise steeper
fixed slope. That produces a smaller wedge and a weaker lower bound but may:

* reduce table variants;
* improve cross-request cache reuse; or
* simplify configuration invalidation.

The mining table already deliberately uses the least runny tower-area material
rather than the actual local material. A still more conservative global slope
may be considered if tower material scanning is expensive or unstable.

### Handoff tolerance and numerical behavior

The dumping baseline uses the strict separation predicate:

```text
contactTolerance = 0.0001 physical height

safe(k)
    = currentBandFloor - k
      > Fcontact(state, k) + contactTolerance
```

The authoritative V2 corner-crest sign comparison and smooth-leveling height
compatibility both treat a profile and precise terrain within `0.0001` as
level. Reuse that semantic tolerance in the charge-indexed separation
predicate. It is graph behavior, not a heuristic-only floating-point guard.

Retain the authoritative numeric representations:

* V2 profile heights remain exact integer half-level values;
* precise terrain remains the captured `float`;
* charge comparisons widen values only when required by existing shared APIs,
  without changing the semantic tolerance;
* `Wdump` uses the shared runtime `float` scorers and the one-ULP downward guard
  defined by the table policy; and
* no additional heuristic epsilon or blanket terrain-extrema rounding is
  introduced.

Do not address these questions through blanket terrain-extrema rounding.
Preserve exact extrema and localize any weakening to the relevant conversion.

## History and blocked-origin exclusions

Do not remove already visited origins, adjacent origins, or generally blocked V
origins from the initial extrema field.

There are two distinct questions:

1. can an origin be used as a future generated or landing origin; and
2. can its terrain reduce direct or side-ray work performed nearby?

A history rule may reject an origin revisit while the terrain at or near that
origin can still support a legal future side wedge. Removing its height from the
maximum could increase the dumping gap beyond the real unavoidable gap.
Similarly, removing a low sample from the minimum could increase the mining gap.
Either change could break admissibility.

History-specific exclusions would also change the cache key from `(tile,
radius)` to a path-dependent identity, destroying most recursive reuse.

A later refinement may combine proven eligibility with the separate landing and
support extrema described above. Do not make the shared terrain-support cache
history-specific.

## Integration with V2 A*

For the first implementation, evaluate only nonterminal generated V2 states
under dumping semantics:

```text
h = existingPotential + H_land
```

The approved [Sparse V-Type Route Potential](accessway-pathfinding-sparse-v-route-potential.md)
will replace `existingPotential` with a sparse V/FV route field `P` and a
component-local G escape lookup. A separate component-conditioned commitment
idea remains deferred. The route field owns traversal and generated fixed
overhead; it does not move either cost into `H_land`.

The components cover disjoint cost portions:

* the route potential lower-bounds travel, generated fixed overhead, centre
  spokes, and exact G/FV suffix distance; and
* `H_land` lower-bounds only unpaid future direct and ordinary exterior-side
  landscaping work.

Use addition, not `max`, because every concrete continuation pays both disjoint
portions. Preserve that separation in implementation: do not add traversal,
generated fixed overhead, spokes, terminal fees, ground-suffix cost, cleanup,
or turn-owned frontal-ray cost to `Wdump`. Conversely, do not add direct or
ordinary exterior-ray landscaping to the route potential without revisiting the
composition proof.

Dijkstra continues to enqueue `h = 0` and remains the optimality reference for
the same useful-height-envelope and graph-pruning configuration.

Initially calculate `H_land` when a state is enqueued. If terrain-extrema
queries remain expensive for states that never pop, separately test deferred
calculation on first pop with priority reinsertion. Do not combine both changes
in the first experiment.

The heuristic need not initially be consistent. Continue accepting improved
`g` labels for the same concrete state and measure re-enqueueing. Validate where
practical:

```text
H_land(s) <= real landscaping edge cost(s, t) + H_land(t)
```

If inconsistency materially increases queue work, weaken the bound or adjust
the horizon/table conversion rather than closing states prematurely.

The mining extension later uses:

* the same exact contact/work offset masks and extrema evaluator;
* the paired minimum rather than maximum;
* a sign-symmetric terrain charge horizon; and
* the fixed-grid `Wmine` table using the least runny normal material in the
  tower area.

## Diagnostics

Record:

### Heuristic use

* calls, zero results, and nonzero results by operation;
* zero results caused by a compatible fixed frontage and the fraction of
  requests/states suppressed by that rule;
* total and average landscaping heuristic added;
* favorable extremum, exact terrain gap, work gap, terrain charge horizon,
  fixed charge horizon, and selected `Kcharge` distributions;
* table lookup time; and
* total heuristic evaluation time.

### Terrain extrema

* contact-mask scans and work-mask scans by charge prefix;
* translated offsets visited, unique terrain samples, and duplicate offsets
  removed during mask precomputation;
* exact whole-query cache hits and misses, if that later cache is enabled;
* overlap-reuse hits, if enabled;
* terrain samples read directly;
* samples outside the tower area;
* ocean samples;
* selected extrema outside the tower area;
* selected extrema on ocean;
* maximum and final cache entries;
* cache bytes per entry and total memory estimate;
* entries consumed by dumping queries;
* entries consumed by mining queries;
* entries consumed by both operation classes;
* incomplete-coverage fail-open count; and
* extrema calculation time.

The tower-area and ocean counters should initially be diagnostics only. Do not
add eligibility branches to the hot extrema path solely to populate them if the
snapshot cannot provide them cheaply.

### Work tables

* table configurations created and reused;
* table construction time;
* table memory;
* exact `delta` versus selected integer `d`;
* exact `Kcharge` versus capped table charge horizon seven;
* estimated weakening from gap flooring and caps;
* lookup counts by table cell;
* monotonicity validation results;
* requests with `delta > 8`;
* requests with `Kcharge > 7`; and
* dumping/mining table cache hits and misses.

### Search outcomes

* visited states;
* pending high-water;
* stale pops and improved-label re-enqueues;
* selected goal class;
* selected total cost and landscaping breakdown; and
* total search time.

Compare at least:

1. heuristic disabled;
2. direct diamond scan without cache;
3. exact-key cache with direct scans;
4. opportunistic one-child/crescent reuse;
5. four-child recursive memoization;
6. optional five-child recursive prefetch;
7. maximum-only versus paired-extrema cache memory and runtime;
8. fixed integer `Wdump` table versus direct synthetic evaluation in diagnostics;
9. unfiltered extrema versus diagnostics-estimated filtered strength.

## Validation

### Terrain-extrema fixtures

* flat terrain with analytically known extrema for every contact and work mask;
* one peak and one pit at every relative mask position;
* equal extrema at multiple positions;
* a higher peak and lower pit immediately outside the exact mask do not affect
  its extremum;
* rearward extrema are included exactly when the relaxed transition envelope
  can reach them within the tested charge prefix;
* extrema outside the tower area are included;
* ocean extrema are included;
* inclusion of unlandable samples never strengthens either operation's bound;
* fixed-target charge horizons shorter and longer than the terrain charge
  horizon;
* physical-map boundaries;
* missing required snapshot coverage returns zero heuristic for the affected
  operation; and
* translated direct scans equal a simple reference set scan on randomized
  terrain fields.

### Relaxed-envelope containment fixtures

For every authoritative straight, strafe, and turn transition produced across
all enabled band profiles, axes, entry directions, and turn-pending states:

* the transition's next contact support is contained in the corresponding
  charge-indexed relaxed contact mask;
* every direct-work corner and every configured straight/strafe ray-trace tile
  is contained in the corresponding swept work mask;
* `Delta.Count > 0` advances the charge ordinal exactly once;
* `Delta.Count == 0` does not advance it;
* a turn can affect later relative offsets but cannot be followed by a
  transition forbidden by the authoritative post-turn rule; and
* translating a relative mask preserves every expected absolute sample.

Any production transition not contained by the relaxed templates is an
admissibility failure and must fail validation rather than silently weaken
coverage.

### `Wdump` table fixtures

* the table has exactly 9 work-gap rows and 8 charge columns;
* row `d` is generated from exact synthetic gap `d`;
* column `K` includes exactly `K` future charge-owning slices;
* zero gap and zero horizon return zero;
* the current state is never charged;
* synthetic successor centre and corner heights use the favorable descending
  phase;
* direct work matches the shared real scorer on equivalent flat fixtures;
* exterior side-ray work matches the shared scorer with the frozen dumping
  slope;
* every row is nondecreasing in `K`;
* every column is nondecreasing in `deltaIndex`;
* runtime gaps use `floor(delta)` and never round upward;
* runtime gaps above eight use row eight;
* runtime horizons above seven use column seven;
* table caps match configured scorer caps;
* generated values never exceed independently scored synthetic routes; and
* table serialization or caching preserves exact configuration identity.

### `Wmine` preparation fixtures

* the selected material is the least runny normal material present in the tower
  area;
* the selected effective slope is greater than or equal to every local normal
  material slope under the chosen slope convention;
* changing the tower-area material set invalidates or changes the mining table
  key; and
* missing material information fails weak rather than choosing an unsafe slope.

### Search validation

* deep/high dumping fixtures with known unavoidable future work;
* later low/mining mirror fixtures using the same exact masks;
* terrain rising or falling as favorably as construction grade permits;
* high and low support terrain encountered by side rays;
* immediate ground and fixed-frontage terminals;
* terminals exactly on V2 stride boundaries;
* combined requests in which each goal class wins;
* U-turn routes whose rearward samples enter the exact masks only at the
  appropriate charged prefix;
* requests whose extrema come from outside the tower area or ocean;
* equality between A* and Dijkstra success, selected total cost, and cost
  breakdown under the same graph-pruning configuration;
* differential equality with the heuristic disabled; and
* live marker cases before and after enabling the useful-height envelope.

## Proposed implementation order

1. Implement and measure the useful-height envelope.
2. Precompute and fixture-test the exact relaxed V2 state, contact-support, and
   swept work-support offset sets for charge prefixes `1..7`.
3. Implement the charge-indexed earliest-any-contact predicate with the
   projected-ground-aware endpoint horizon.
4. Implement the synthetic `Wdump` generator with fixed slope, integer work
   gaps `0..8`, and charge horizons `0..7`.
5. Verify the table against the shared direct-work and side-ray scorers, the
   straight-dominance lemma, and required monotonicity.
6. Add direct exact swept-mask scans and calculate the dumping bound in
   diagnostics only.
7. Enable the direct-scan heuristic behind an experimental flag and compare A*
   with Dijkstra.
8. Measure repeated translated queries and overlap between neighbouring masks.
   Add exact whole-query caching or incremental overlap reuse only when those
   measurements justify it.
9. Add `Wmine` using paired exact-mask extrema and the least runny tower-area
   normal material.
10. Measure the strength lost to global work ceilings, unfiltered extrema,
   integer gap flooring, and the `deltaWork = 8` / `Kcharge = 7` caps.
11. Consider localized work ceilings, filtered landing extrema, finer/adaptive
    tables, exact large-gap evaluation, or alternate heuristic material slopes
    only when measurements justify the added complexity.

Keep this heuristic only if its measured pruning benefit survives terrain-
extrema lookup, fixed-table lookup, cache memory, and queue overhead after
useful-height pruning is already enabled.

If it does not improve end-to-end runtime and queue pressure, leave it disabled
or remove the implementation even when it lowers visited-state counts.
