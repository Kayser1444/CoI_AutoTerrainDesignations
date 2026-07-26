# Recursive-Diamond Favorable-Ground Landscaping Heuristic

Status: proposed alternative to the lazy unavoidable-landscaping heuristic

Drafted: 2026-07-26

Related designs:

* [Accessway Pathfinding](accessway-pathfinding.md)
* [Accessway Pathfinding Side-Ray Landscaping Cost Amendment](accessway-pathfinding-side-ray-cost.md)
* [Accessway Pathfinding Useful-Height Envelope](accessway-pathfinding-height-envelope.md)
* [Lazy Unavoidable-Landscaping Heuristic](accessway-pathfinding-lazy-landscaping-heuristic.md)
* [Unified Goal Search and Snapshot Potential Heuristic](unified-goal-search-snapshot-potential-heuristic.md)

## Summary

Replace the profile-aware lazy successor relaxation with a cheaper scalar lower
bound for unavoidable future landscaping work.

For an above-ground V2 state, split the estimate into two independent problems:

1. find a favorable terrain ceiling `F` and a proven minimum relaxed distance
   before terrain or another compatible terminal can be reached; and
2. convert the residual height gap `delta = H - F` and the proven unpaid
   generated-origin horizon into a lower bound `W` for direct work plus exterior
   side-ray work.

The terrain ceiling is the maximum precise terrain height in a full Manhattan
diamond around the state. Full diamonds are required because a legal U-turn can
occasionally be the cheapest route; travel direction therefore cannot safely
remove the rear half of the relaxation.

The same spatial query is useful for the later below-ground mining extension.
A state above terrain needs the diamond maximum, while its approximate mirror
state below terrain needs the diamond minimum. Calculate and cache both extrema
whenever a diamond is evaluated:

```text
E(c, r) = (minimum terrain height, maximum terrain height) in D(c, r)
```

The extra minimum costs only one stored value and three additional comparisons
per recursive parent. It requires no additional recursive calls or terrain
reads. This is expected to be especially useful because above- and below-ground
states at similar positions can have similar total path costs and may both be
explored during the same request.

Diamond extrema have an exact four-child recurrence. Radius zero is read
directly from the immutable precise-terrain field and is not cached. Radius one
reads its five terrain samples directly. Every larger diamond is the union of
the four radius-one-smaller diamonds centered at its cardinal neighbours:

```text
E(c, 0) = (terrain(c), terrain(c))

E(c, 1) = extrema of terrain at { c, north, south, east, west }

E(c, r) = combine(
    E(c + north, r - 1),
    E(c + south, r - 1),
    E(c + east,  r - 1),
    E(c + west,  r - 1))                 for r >= 2
```

Memoize positive-radius values on demand. The expected A* evaluation order
should request many recursively produced subset diamonds before their
supersets. When the order is reversed, a recursive superset evaluation
prefetches parity-compatible subset values that its likely descendants can
reuse. Measure that hypothesis rather than assuming it.

For dumping, convert the remaining gap to landscaping work using an idealized
straight maximum-grade V2 landing over flat terrain at elevation `F`. The work
model uses the same direct-work normalization, effective dumping-material
slope, exterior-ray integration, weights, and caps as the real scorer, while
removing every constraint or cost that is not needed for the lower bound.

Implement dumping first. Mining later reuses the same extrema cache but requires
its own operation-specific conversion from the minimum terrain floor to work.

## Priority and purpose

Implement and measure the useful-height envelope first. It removes generated
states from both A* and Dijkstra before expensive candidate work and is expected
to have a larger first-order benefit.

This heuristic is a later A*-only ordering refinement for the V2 states that
remain inside the frozen request-effective envelope. It succeeds only if the
reduction in visited states, pending high-water, and total search time exceeds:

* favorable-ground query time;
* recursive-cache construction and lookup time;
* work-function lookup or evaluation time;
* additional cache memory for both terrain extrema; and
* any extra queue work caused by inconsistency.

Keep the existing lazy-frontier proposal as a comparison mode until live
measurements select one formulation.

## Scope

The first implementation covers:

* V2 generated states above terrain;
* dumping-only future landscaping debt;
* tower-ground and compatible fixed-frontage terminal horizons;
* direct generated-origin work;
* the two ordinary exterior side rays owned by a straight V2 slice;
* immutable precise terrain captured in the access snapshot;
* full Manhattan-diamond minimum-and-maximum queries;
* exact request- or snapshot-scoped memoization; and
* A* queue ordering only.

It deliberately does not initially include:

* mining or mixed leveling work conversion;
* directionally cropped diamonds;
* generated-history exclusions;
* visited-origin or neighbour removal;
* candidate feasibility, buildings, durability, cleanup, or projected-work
  checks inside the extrema query;
* turn-owned frontal rays;
* fixed generated overhead or traversal cost already represented by the
  existing potential;
* a final decision on work-table discretization, interpolation, or analytical
  evaluation; or
* changes to real edge cost in `g`.

## Coordinates and units

Use a relaxed tile-centred V coordinate system for the proof and extrema query:

```text
one cardinal relaxed step = one terrain tile
maximum relaxed grade     = 0.25 physical height per step
one V2 origin stride      = 4 relaxed steps
```

Path profiles remain represented by the authoritative V2 height type. Convert
the relevant scalar profile height to the same physical-height unit used by the
snapshot's precise terrain samples before calculating a gap.

For the above-ground case, let:

```text
p       current relaxed representative position
H       conservative scalar current path height
G0      precise local terrain/support height below p
a       0.25, maximum favorable height reduction per relaxed step
R0      ceil(max(0, H - G0) / a)
```

`R0` is a safe maximum terrain-search radius because the local terrain below the
state supplies a relaxed fallback surface. If the local sample does not supply
a valid fallback under the current operation, ocean policy, or snapshot
coverage, fail open with zero landscaping heuristic until a separate proof is
provided.

The concrete V2 state has a nonzero two-lane footprint. Map it to the scalar
query through a small conservative support stencil. The stencil must cover the
terrain samples that can reduce direct work and the roots of ordinary exterior
side rays. An initial axis-independent stencil may cover both possible V2 band
orientations.

The dumping ceiling is the largest cached maximum over the stencil:

```text
Fmax(state, R) = max E(stencilPoint, R).Maximum
```

The later mining floor is the smallest cached minimum over the same stencil:

```text
Fmin(state, R) = min E(stencilPoint, R).Minimum
```

The atomic cache remains keyed only by `(tile, radius)`; footprint handling is a
small constant number of extrema lookups above it.

## Step 1: favorable terrain extrema and minimum horizon

### Full diamond

For a terrain sample position `c` and nonnegative integer radius `r`, define:

```text
D(c, r) = { q | ManhattanDistance(c, q) <= r }

L(c, r) = minimum exact precise terrain height over q in D(c, r)
U(c, r) = maximum exact precise terrain height over q in D(c, r)
E(c, r) = (L(c, r), U(c, r))
```

For the current above-ground state:

```text
F       = maximum U value over the V2 support stencil at radius R0
delta   = max(0, H - F)
dGround = ceil(delta / a)
```

`F` is a favorable terrain ceiling, not necessarily the height of a concrete
reachable landing point. The point supplying the maximum may be blocked,
unreachable, or located in a direction that the eventual route does not take.
Those relaxations can only raise `F`, reduce `delta`, and weaken the heuristic.

For the later below-ground mining extension, use the symmetric floor:

```text
F       = minimum L value over the support stencil
delta   = max(0, F - H)
dGround = ceil(delta / a)
```

The same recursively constructed extrema entry therefore serves both operation
classes.

### Ground-distance proof

Every relaxed continuation can lower an above-ground scalar profile by at most
`a` per cardinal step. Before step `dGround`, even its most favorable possible
height is strictly above `F`:

```text
pathHeight(i) >= H - i * a > F
```

Every terrain point reachable within that many relaxed steps lies inside the
queried diamond and has terrain height no greater than `F`. No terrain handoff
can therefore occur before `dGround`.

The mining proof is sign-symmetric: a relaxed continuation can raise its scalar
profile by at most `a`, and every terrain sample in the queried domain is no
lower than the favorable floor.

This proves a minimum relaxed ground horizon without retaining travel axis,
entry direction, profile mode, or generated history.

### Why the diamond cannot be cropped by travel direction

V2 can construct legal 90-degree turns after a flat landing, and a sufficiently
extreme obstacle or terrain arrangement can make a U-turn the cheapest valid
route. A rear-half landing is therefore not generally dominated. The initial
heuristic must use the full diamond.

Direction-specific subsets may be reconsidered only if the authoritative graph
later adds a proven monotonic-progress rule or another dominance proof excludes
all useful rear-half continuations.

### Fixed terminals

Preserve or construct a trustworthy lower-bound distance to the nearest
compatible fixed terminal in the same relaxed cardinal-step units:

```text
Tfixed = minimum conservative relaxed V distance to any compatible fixed target
```

Do not derive `Tfixed` by dividing the scalar potential cost. The potential
mixes travel, generated fixed overhead, exact G suffix distance, centre spokes,
and fixed-provider terminal fees.

The earliest compatible terminal horizon is:

```text
dTerminal = min(dGround, Tfixed)
```

When no compatible fixed target exists, use `Tfixed = infinity`.

#### Fixed targets crop the work horizon, not initially the terrain query

For a direct-work-only bound, it can be safe to crop the terrain diamond to the
fixed-target horizon. The initial specification includes side rays, whose
support can extend laterally beyond a nearby terminal's centre distance.
Therefore:

* build the extrema from the full local fallback radius `R0`; and
* use `Tfixed` only to shorten the number of future generated slices charged by
  `W`.

A later tighter formulation may crop the extrema radius only after proving that
all direct-work samples and side-ray support capable of reducing pre-terminal
work remain inside the cropped domain.

### Conversion to unpaid generated slices

`dTerminal` counts relaxed cardinal tile steps. Landscaping is charged when a
new generated V2 slice is entered, at four relaxed steps per origin stride. A
terminal exactly at a stride boundary may replace that generated slice, so
count only complete future slice entries strictly before the earliest terminal:

```text
N = max(0, floor((dTerminal - 1) / 4))
```

This conversion is deliberately conservative. If later transition-specific
analysis can prove that another generated slice is unavoidable, strengthen the
conversion behind fixtures rather than assuming it.

The current state's landscaping work is already in `g` and is never included in
`N` or `W`.

## Step 2: landscaping work lower bound

### Synthetic straight landing

For each residual height gap and each unpaid-slice prefix, evaluate or retrieve
the work of an idealized two-lane straight V2 descent over flat terrain at
elevation zero.

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

Use the actual descending V2 profile geometry in the work evaluator, including
its outgoing edge and corner heights. Do not score a flat band at its centre
height when a descending ramp exposes a lower outgoing edge; that would
overstate the minimum side-ray work.

For future slice `k`, the relaxed centre gap may be no greater than:

```text
centerGap(k) = max(0, delta - k)
```

when `delta` is expressed in physical levels and one V2 origin stride descends
one level. The authoritative evaluator should construct the synthetic profile
and invoke shared cost helpers rather than rely on this scalar expression for
corner work.

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

* the same effective dumping-material slope selected by the real scorer,
  including its safety factor;
* the same cost-sample distances and rectangle-rule integration;
* the same maximum ray distance and cost cap; and
* the same side-ray weight and landscaping distance scale.

The initial lower-bound model may omit unresolved penalties and every fatal
condition. Their omission only weakens the heuristic. If later fixtures prove
that a synthetic unresolved penalty is itself unavoidable under the same
ceiling proof, it may be added separately.

Do not add turn-owned frontal rays. The synthetic route has no turns, while a
real turn can only add exposed-face work.

### Material-slope containment proof

Let:

```text
a = 0.25, the artificial maximum construction grade per relaxed tile
m = effective dumping-material vertical fall per lateral tile
```

All permitted dumping materials are less runny than the artificial construction
slope, so:

```text
m >= a
```

At relaxed longitudinal distance `i`, a maximum-grade descent has residual gap
above the favorable ceiling:

```text
z(i) = max(0, delta - a * i)
```

Its material side ray reaches flat ceiling terrain within at most:

```text
z(i) / m <= z(i) / a
```

The furthest point of that ray is therefore within relaxed Manhattan distance:

```text
i + z(i) / m
<= i + z(i) / a
<= delta / a
```

from its atomic support-stencil root. Since `delta <= H - G0`, that complete
synthetic direct-work and side-ray half-cone stays inside the original full
local-fallback diamond used to obtain `F`.

Every actual terrain sample in that domain is at or below `F`. Replacing it by
flat terrain at `F` can only:

* reduce positive direct fill;
* make each fill ray intersect terrain earlier; and
* reduce or preserve integrated side-ray cost.

The straight maximum-grade synthetic landing therefore lower-bounds the unpaid
direct-plus-side landscaping work of every concrete continuation that cannot
terminate earlier than the proven horizon.

### Work-function representation

Conceptually define a cumulative function:

```text
Wdump(dumpingConfiguration, delta, sliceCount)
```

where:

```text
Wdump(delta, 0) = 0
Wdump(delta, n) = cumulative synthetic dumping work
                  of the first n unpaid future V2 slices
```

The dumping-configuration identity must include every setting that changes the
synthetic score, including:

* effective dumping-material slope;
* direct-work weight;
* side-ray weight;
* landscaping distance scale;
* side-ray sample schedule;
* maximum ray distance; and
* maximum ray cost.

The heuristic value is:

```text
H_land(state) = Wdump(configuration, delta, N)
```

The uncapped continuous shape is approximately:

```text
direct-work accumulation = O(delta^2)
side-wedge accumulation  = O(delta^3)
total W                   = A * delta^2 + B * delta^3
```

Discrete V2 profiles, side-ray sample distances, caps, and phase choices make a
simple fitted polynomial less reliable than evaluation based on the shared
scorers.

The exact representation of `Wdump` remains an open question. Do not encode a
rounding or interpolation policy as a settled requirement yet; see
[Open question: precision, discretization, and interpolation](#open-question-precision-discretization-and-interpolation).

## Exact recursive diamond extrema

### Base cases and recurrence

Radius zero is not cached:

```text
E(c, 0) = (terrain(c), terrain(c))
```

The precise terrain value is already available in the immutable snapshot.
Caching it would duplicate terrain storage while replacing a direct terrain
lookup with a cache lookup.

Radius one is calculated directly and may be cached as the first reusable
level:

```text
samples = terrain at { c, north, south, east, west }

E(c, 1) = (
    minimum sample,
    maximum sample)
```

For `r >= 2`:

```text
children = {
    E(c + north, r - 1),
    E(c + south, r - 1),
    E(c + east,  r - 1),
    E(c + west,  r - 1)
}

E(c, r) = (
    minimum child.Minimum,
    maximum child.Maximum)
```

The centre subdiamond `E(c, r - 1)` is unnecessary. Every non-centre point in
`D(c, r)` is one step closer to at least one cardinal neighbour. For `r >= 2`,
the centre itself is also inside every cardinal child diamond. The four child
diamonds therefore exactly cover `D(c, r)`.

A parent assembled from cached children costs four reads, three minimum
comparisons, and three maximum comparisons. Calculating both extrema does not
change the recursive state family or require additional terrain reads.

The omitted centre child may be tested only as an optional prefetch strategy if
measurements later show that its extra subset family is frequently consumed.

### Exact terrain values

Store the extrema using the same precise terrain values supplied to the real
landscaping scorer. The recurrence performs only comparisons and selection; its
result is always one of the original terrain samples and does not accumulate
numerical error.

Do not round the cached minimum or maximum merely to make the result
conservative. Rounding the maximum upward weakens the dumping heuristic, and
rounding the minimum downward weakens the mining heuristic. Any necessary
conservatism belongs at the later interfaces where an exact continuous gap is
converted to a handoff horizon or a discrete/approximated work representation.
Those choices remain open pending examination of the real handoff tolerance and
work-function implementation.

### Subset relation

Every cached query satisfying:

```text
ManhattanDistance(childCenter, parentCenter) + childRadius <= parentRadius
```

is an exact subset of the parent diamond.

A radius-`r` diamond contains:

```text
2r^2 + 2r + 1
```

terrain positions. One immediate radius-`r - 1` directional child differs from
it by only `4r` positions, which also permits a cheaper opportunistic crescent
mode when full recursive prefetch is disabled.

### Recursive evaluation cone and parity

A cold recursive query for `(c, R)` does not create every geometrically
contained `(tile, radius)` key. Each recursive edge moves the centre by one
cardinal tile while reducing the radius by one. It therefore creates exactly
the positive-radius states `(q, k)` satisfying:

```text
ManhattanDistance(c, q) <= R - k
and
ManhattanDistance(c, q) has the same parity as R - k
and
1 <= k <= R
```

The opposite-parity contained subsets are not needed to calculate the parent.
They remain available for later on-demand evaluation if A* requests them.

At recursive depth `t = R - k`, the number of reachable centres is:

```text
(t + 1)^2
```

Therefore a cold radius-`R` query creates:

```text
sum from i = 1 to R of i^2
= R * (R + 1) * (2R + 1) / 6
```

cached positive-radius entries.

This remains `O(R^3)`, but is approximately half the leading-order cache work
of the earlier five-child full-cone recurrence. Radius-zero terrain samples are
read as leaves and are not stored as cache entries.

A single direct scan is still only `O(R^2)`. Recursive evaluation becomes
attractive when the search later requests a meaningful fraction of the
prefetched parity-compatible subset family.

### Expected A* access pattern

The heuristic itself is expected to favor lower-gap descendants and postpone
larger-gap supersets. Consequently:

* many required child queries should already be cached when a superset is
  evaluated;
* a superset assembled from four cached children costs only four reads and six
  extrema comparisons; and
* when a superset is evaluated first, its recursively generated
  parity-compatible subsets are likely to become top-level heuristic queries
  before the request finishes.

There is a second reuse hypothesis: when an above-ground state is evaluated, a
nearby or mirrored below-ground state may also be competitive because their
non-landscaping path costs are similar. Storing both extrema means the later
query can reuse the same spatial cache even though it uses the opposite bound.

These are performance hypotheses, not admissibility assumptions. Instrument
prefetch utilization and compare it with direct scanning. Also record misses
caused specifically by a requested subset belonging to the opposite-parity
family and reuse of entries by both dumping and mining queries.

### Cache ownership and key

The atomic terrain-extrema cache depends only on immutable precise terrain and
snapshot coverage:

```text
key   = (tile, positiveRadius)
value = (
    minimumTerrainHeight,
    maximumTerrainHeight,
    coverageStatus)
```

Optional `argminTile` and `argmaxTile` fields may be added if they support a
measured containment shortcut. Omit them initially to keep each entry compact.

Radius zero is never inserted. The cache does not depend on:

* current path height;
* travel axis or direction;
* profile mode;
* operation class;
* operation history;
* generated origins already used;
* fixed goals; or
* the current goal set.

It may therefore live on the immutable snapshot and be reused across sequential
cluster requests until terrain or snapshot bounds invalidate it. If memory
ownership is simpler, start request-scoped and promote it only after
measurements.

### Data structure

Start with a packed-key dictionary or a sparse per-centre radius vector. Avoid
allocating a dense `(x, y, radius)` volume over the complete snapshot.

If profiling shows dictionary overhead dominates, replace it with:

* one sparse radius array per touched centre; or
* radius layers over the locally warmed search region.

The logical recurrence must remain identical.

### Stack and work budgets

Implement the logical recursion with an explicit post-order stack if radii can
be large enough to risk call-stack growth.

Track:

* maximum cache entries per snapshot or request;
* maximum new recursive entries per top-level query; and
* optional maximum recursive radius.

When a recursive budget is exhausted, fall back to an exact direct diamond scan
for the requested key. Calculate both extrema during that scan and cache the
top-level pair if space permits. A budget must affect performance only; it must
not replace an exact extremum by a weaker or unsafe value.

### Missing and boundary samples

A missing in-map terrain sample could hide either:

* a higher support surface that reduces real dumping work; or
* a lower open surface that reduces real mining work.

Mark the extrema entry as incomplete if any required in-map sample lacks
authoritative coverage. A dumping query must then fail open with a ceiling at
least `H`, and a mining query must fail open with a floor at most `H`, producing
zero landscaping heuristic.

Physical map exterior is not usable terrain and need not seed either extremum.
Generated-centre bounds remain authoritative. Ocean support must use the same
precise terrain/floor interpretation as the real landscaping scorer; do not use
water-surface height as solid ground unless the real scorer does.

## Open question: precision, discretization, and interpolation

The extrema cache should preserve exact snapshot terrain values. The unresolved
precision questions arise after the exact residual gap has been calculated.

### Handoff-distance conversion

The simple formula is:

```text
dGround = ceil(delta / a)
```

Determine whether the authoritative terrain handoff accepts a profile within a
nonzero epsilon of terrain. If it does, the heuristic must use the same semantic
tolerance when proving that another V step is unavoidable. Do not introduce an
arbitrary floating-point epsilon unrelated to graph behavior.

Questions to resolve:

* What exact terrain/profile comparison defines a legal handoff?
* Is its tolerance expressed in physical height, half-levels, or another unit?
* Can the calculation remain in an exact integral profile unit while retaining
  the precise terrain float?
* Which boundary cases make `ceil(delta / a)` change by one step?

### Work-function evaluation

Possible implementations for `Wdump(configuration, delta, N)` include:

1. evaluate the synthetic scorer directly for every encountered exact `delta`;
2. lazily memoize work values by an exact or canonicalized gap key;
3. use a finely discretized lower-endpoint table;
4. derive an exact piecewise function from V2 profile phases and side-ray sample
   boundaries;
5. construct a proven lower piecewise approximation; or
6. use a hybrid table plus exact evaluation near discontinuities.

A table indexed by a rounded-up gap is unsafe because `W` is nondecreasing. A
table indexed by the lower endpoint is safe but can materially weaken a cubic
heuristic. Ordinary linear interpolation between exact samples is not
necessarily safe either: for a convex cubic region, the chord lies above the
true function. Any interpolation must be proven to remain below the synthetic
scorer throughout the interval.

Potential safe approaches include:

* exact evaluation;
* lower-endpoint lookup;
* tangent or otherwise proven lower-envelope segments where differentiability
  and convexity are established;
* interval-specific analytical integration matching the discrete scorer; or
* downward-adjusted interpolation with a proven error bound.

Measurements should compare heuristic strength and evaluation cost, especially
for large gaps where a small loss in `delta` can cause a much larger loss in the
cubic side-work component.

### Numerical reproducibility

Also determine:

* whether the cache and work evaluator should use `float` or `double`;
* whether shared scorer operations are reproducible enough for direct reuse;
* whether the final heuristic needs a tiny downward numerical guard even when
  the mathematical model is admissible; and
* how A* versus Dijkstra fixtures should distinguish a real admissibility defect
  from floating-point tie noise.

Do not settle these questions through blanket terrain rounding. Preserve exact
extrema and make any later weakening explicit, localized, and measurable.

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

A later refinement may introduce separate fields:

```text
F_landing = extremum over proven eligible landing surfaces
F_support = extremum over every terrain sample that may reduce landscaping work
```

Use a stronger landing field only after proving how it combines with the
operation-specific work function. Static exclusions may be folded into the
atomic terrain source only when an excluded sample is proven incapable of
reducing work for every history reaching the same concrete state.

## Integration with V2 A*

For the first implementation, evaluate only nonterminal generated V2 states
under dumping semantics:

```text
h = existingPotential + H_land
```

The components cover disjoint cost portions:

* the existing potential lower-bounds travel, generated fixed overhead, centre
  spokes, exact G suffix distance, and fixed-terminal fees; and
* `H_land` lower-bounds only unpaid future direct and exterior-side landscaping
  work.

Dijkstra continues to enqueue `h = 0` and remains the optimality reference for
the same useful-height-envelope and graph-pruning configuration.

Initially calculate `H_land` when a state is enqueued. If favorable-ground
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
the scalar phase conversion rather than closing states prematurely.

The mining extension later uses:

* the same `DiamondTerrainExtremaCache`;
* the cached minimum rather than maximum;
* a sign-symmetric minimum ground horizon; and
* a separate mining work evaluator because material selection, side-ray
  behavior, ocean rules, and depth dependence differ from dumping.

## Diagnostics

Record:

### Heuristic use

* calls, zero results, and nonzero results by operation;
* total and average landscaping heuristic added;
* favorable extremum, residual gap, ground horizon, fixed horizon, terminal
  horizon, and unpaid-slice count distributions;
* work-function lookup or evaluation time; and
* total heuristic evaluation time.

### Diamond queries

* top-level queries;
* exact cache hits and misses;
* radius-one direct evaluations;
* recursive positive-radius entries created;
* entries later requested as top-level queries;
* prefetch utilization ratio;
* opposite-parity top-level misses;
* queries assembled entirely from four cached immediate children;
* direct-scan fallbacks;
* crescent-reuse queries, if enabled;
* terrain samples read directly;
* maximum radius;
* maximum and final cache entries;
* cache bytes per entry and total memory estimate;
* entries consumed by dumping queries;
* entries consumed by mining queries;
* entries consumed by both operation classes;
* missing-coverage fail-open count; and
* extrema calculation time.

### Precision and work representation

* exact residual-gap distribution;
* any canonicalized or table-indexed gap distribution;
* weakening introduced by lower-endpoint lookup or another approximation;
* interpolation or evaluation mode;
* direct synthetic scorer calls;
* work-cache hits and misses; and
* downward numerical guards applied.

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
8. candidate `W` precision/evaluation strategies; and
9. the older lazy successor-frontier heuristic.

## Validation

### Terrain-extrema fixtures

* flat terrain with analytically known extrema;
* one peak and one pit at every Manhattan boundary position;
* equal extrema at multiple positions;
* a higher peak and lower pit immediately outside the diamond;
* rear-half extrema required by cheapest U-turn cases;
* fixed-target horizons shorter and longer than the ground horizon;
* large local gaps and radius limits;
* physical-map boundaries;
* missing snapshot coverage returning zero heuristic for either operation; and
* direct-scan equality with recursive extrema on randomized terrain fields.

### Recurrence and cache fixtures

* radius zero reads exact terrain directly and creates no cache entry;
* radius one equals the minimum and maximum of the centre and four cardinal
  terrain samples;
* every radius-at-least-two parent extrema pair equals the combined extrema of
  its four directional children;
* four directional child diamonds exactly cover every tested parent diamond;
* a cold superset build contains every parity-compatible subset predicted by the
  recurrence and need not contain opposite-parity subsets;
* child-first evaluation makes a parent constant-work;
* parent-first evaluation records later top-level consumption of prefetched
  children;
* the recursive entry count matches `R * (R + 1) * (2R + 1) / 6`;
* each cached extremum is an exact original terrain sample;
* a paired-extrema entry supports both dumping and mining mirror queries;
* cache saturation falls back to an exact direct extrema result; and
* cache ownership is invalidated with the snapshot.

### Work-function fixtures

* zero gap and zero-slice prefixes return zero;
* the current state is never charged;
* synthetic successor centre and corner heights use the favorable descending
  phase;
* direct work matches the shared real scorer on equivalent flat fixtures;
* exterior side-ray work matches the shared scorer using the selected dumping
  slope;
* every prefix is nondecreasing in slice count;
* exact synthetic work is nondecreasing in residual gap;
* each candidate approximation is proven or exhaustively checked not to exceed
  exact synthetic work over its supported domain;
* fixed-terminal cropping returns the correct strict-before-terminal prefix;
* caps match the configured side-ray cap; and
* generated work values never exceed independently scored synthetic routes.

### Precision fixtures

* terrain extrema remain unrounded through the recursive cache;
* handoff-distance boundaries immediately below, at, and above every quarter
  level use the authoritative graph tolerance;
* any lower-endpoint table reports the measured heuristic loss;
* interpolation is rejected where it exceeds the exact synthetic function;
* numerical guards never make the heuristic negative; and
* equivalent `float` and `double` test paths identify material discrepancies.

### Search validation

* deep/high dumping fixtures with known unavoidable future work;
* later low/mining mirror fixtures using the same extrema cache;
* terrain rising or falling as favorably as construction grade permits;
* high and low support terrain encountered by side rays;
* immediate ground and fixed-frontage terminals;
* combined requests in which each goal class wins;
* U-turn routes that would invalidate a forward-half diamond;
* equality between A* and Dijkstra success, selected total cost, and cost
  breakdown under the same graph-pruning configuration;
* differential equality with the heuristic disabled; and
* live marker cases before and after enabling the useful-height envelope.

## Proposed implementation order

1. Implement and measure the useful-height envelope.
2. Preserve a trustworthy relaxed cardinal terminal-distance horizon, including
   compatible fixed frontages.
3. Resolve or experimentally compare the open precision and work-function
   representation options sufficiently to implement a diagnostics-only dumping
   bound.
4. Implement the synthetic dumping work evaluator and verify it against shared
   direct-work and side-ray scorers.
5. Add a direct-scan full-diamond terrain-extrema query and calculate the new
   dumping bound in diagnostics only.
6. Enable the direct-scan heuristic behind an experimental flag and compare A*
   with Dijkstra.
7. Add exact positive-radius `(tile, radius)` paired-extrema memoization without
   recursive prefetch and measure natural query reuse.
8. Add opportunistic child/crescent reuse.
9. Add four-child recursive memoization with budgets and measure prefetch
   utilization, opposite-parity misses, paired-extrema reuse, and total runtime.
10. Test the omitted centre child only as an optional additional prefetch mode.
11. Select the cheapest query and work-evaluation strategies from live
    measurements; do not retain recursive prefetch merely because it has a high
    cache-hit rate.
12. Compare the selected scalar-diamond formulation with the older lazy
    profile-aware successor frontier.
13. Add mining using the existing paired-extrema cache and a separate
    minimum-terrain work conversion.
14. Consider only proven static eligibility or history refinements that improve
    end-to-end time without weakening cache reuse.

Keep this heuristic only if its measured pruning benefit survives terrain-
extrema lookup, work evaluation, cache memory, and queue overhead after useful-
height pruning is already enabled.
