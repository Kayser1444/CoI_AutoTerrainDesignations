# Recursive-Diamond Terrain-Extrema Landscaping Heuristic

Status: proposed alternative to the lazy unavoidable-landscaping heuristic

Drafted: 2026-07-26

Related designs:

* [Accessway Pathfinding](accessway-pathfinding.md)
* [Accessway Pathfinding Side-Ray Landscaping Cost Amendment](accessway-pathfinding-side-ray-cost.md)
* [Accessway Pathfinding Useful-Height Envelope](accessway-pathfinding-height-envelope.md)
* [Lazy Unavoidable-Landscaping Heuristic](accessway-pathfinding-lazy-landscaping-heuristic.md)
* [Unified Goal Search and Snapshot Potential Heuristic](unified-goal-search-snapshot-potential-heuristic.md)

## Summary

Add a conservative lower bound for unpaid future landscaping work to the V2 A*
heuristic.

For a generated V2 state, split the estimate into two independent problems:

1. find favorable terrain extrema and prove a minimum relaxed distance before
   terrain or another compatible terminal can be reached; and
2. convert the residual height gap and proven horizon into a lower bound for
   direct landscaping work plus ordinary exterior side-ray work.

Use a full Manhattan diamond around the state. A legal U-turn can occasionally
be the cheapest route, so travel direction cannot safely remove the rear half of
the relaxation.

Calculate and cache both terrain extrema for every diamond:

```text
E(c, r) = (
    minimum precise terrain height in D(c, r),
    maximum precise terrain height in D(c, r))
```

An above-ground dumping state uses the maximum. Its approximate below-ground
mining mirror uses the minimum. Calculating both requires no extra recursive
calls or terrain reads and only one additional cached height plus three
additional comparisons per recursive parent.

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

The initial dumping-work conversion is deliberately simple and fixed:

```text
integer delta grid: 0..8 physical levels, step 1
relaxed horizon N:  0..32 cardinal tile steps
lookup:             lower delta endpoint, capped at delta 8 and N 32
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
* recursive-cache construction and lookup time;
* work-table construction or cache lookup time;
* additional cache memory for paired extrema; and
* any extra queue work caused by inconsistency.

Keep the older lazy-frontier proposal as a comparison mode until live
measurements select one formulation.

## Initial implementation decisions

The first implementation settles the following choices:

1. Use full, unfiltered Manhattan diamonds.
2. Include every authoritative precise terrain sample in the diamond, including
   samples outside the tower area and samples on ocean tiles.
3. Do not run V-origin eligibility, tower-bound, ocean, building, history, or
   candidate-feasibility checks inside the extrema query.
4. Cache both exact minimum and exact maximum terrain heights.
5. Do not cache radius-zero queries.
6. Use the exact four-child recurrence without the redundant center child.
7. Implement dumping work first.
8. Use a fixed `Wdump` table over integer gaps `0..8` and relaxed horizons
   `0..32`.
9. Use lower-endpoint lookup without interpolation.
10. Select the dumping material as the real scorer does and freeze one fixed
    conservative effective slope for the table.
11. Define the later mining counterpart with the least runny normal in-ground
    material found in the tower area.

These choices favor a small, verifiable implementation. Strengthening the
heuristic is deferred until the baseline has been validated against Dijkstra
and measured on live requests.

## Scope

The first implementation covers:

* V2 generated states above terrain;
* dumping-only future landscaping debt;
* tower-ground and compatible fixed-frontage terminal horizons;
* direct generated-origin work;
* the two ordinary exterior side rays owned by a straight V2 slice;
* immutable precise terrain captured in the access snapshot;
* full unfiltered Manhattan-diamond minimum-and-maximum queries;
* exact request- or snapshot-scoped extrema memoization;
* a fixed `9 x 33` dumping-work table per configuration; and
* A* queue ordering only.

It deliberately does not initially include:

* mining or mixed-leveling work conversion;
* directionally cropped diamonds;
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

## Coordinates and units

Use a relaxed tile-centred coordinate system for the extrema proof and terminal
horizon:

```text
one cardinal relaxed step = one terrain tile
maximum relaxed grade     = 0.25 physical height per step
one V2 origin stride      = 4 relaxed steps
```

Path profiles remain represented by the authoritative V2 height type. Convert
the relevant scalar profile height to the same physical-height unit used by the
snapshot's precise terrain samples before calculating a gap.

For an above-ground state, let:

```text
p       current relaxed representative position
H       conservative scalar current path height
G0      precise local terrain/support height below p
a       0.25, maximum favorable height reduction per relaxed step
R0      ceil(max(0, H - G0) / a)
```

`R0` is a safe maximum terrain-search radius because the local terrain below the
state supplies a relaxed fallback surface. If the local sample does not provide
a usable fallback under snapshot coverage, fail open with zero landscaping
heuristic until a separate proof is provided.

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
require large extrema diamonds outside the practical domain. Proper candidate
evaluation also restricts exploration into ocean. Avoid paying a per-sample
eligibility cost until measurements show that unfiltered extrema materially
weaken the heuristic.

For the current above-ground state:

```text
F       = maximum U value over the V2 support stencil at radius R0
delta   = max(0, H - F)
dGround = ceil(delta / a)
```

`F` is a favorable terrain ceiling, not necessarily the height of a concrete
reachable landing point. The point supplying the maximum may be blocked,
unreachable, outside the tower area, on ocean, or located in a direction that
the eventual route does not take. Those relaxations can only raise `F`, reduce
`delta`, and weaken the dumping heuristic.

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

Every terrain sample reachable within that many relaxed steps lies inside the
queried diamond and has terrain height no greater than `F`. No terrain handoff
can therefore occur before `dGround`.

The mining proof is sign-symmetric: a relaxed continuation can raise its scalar
profile by at most `a`, and every terrain sample in the queried domain is no
lower than the favorable floor.

This proves a minimum relaxed ground horizon without retaining travel axis,
entry direction, profile mode, generated history, or V-origin eligibility.

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

Define the proven terminal horizon in relaxed tile steps:

```text
N = min(dGround, Tfixed)
```

When no compatible fixed target exists, use `Tfixed = infinity`.

`N` remains in relaxed cardinal-step units. This is the same unit as the diamond
radius and naturally gives `N = 32` for an eight-level gap under a quarter-level
per-tile maximum grade.

### Fixed targets crop the work horizon, not initially the extrema query

For a direct-work-only bound, it may be safe to crop the extrema diamond to the
fixed-target horizon. The initial specification includes side rays, whose
support can extend laterally beyond a nearby terminal's centre distance.
Therefore:

* build the extrema from the full local fallback radius `R0`; and
* use `Tfixed` only to shorten the horizon passed to the work table.

A later tighter formulation may crop the extrema radius only after proving that
all direct-work samples and side-ray support capable of reducing pre-terminal
work remain inside the cropped domain.

### Charge-owning generated slices

The current state's landscaping work is already in `g` and must not be included
in the heuristic.

A future generated V2 slice is entered every four relaxed steps. A compatible
terminal exactly at a stride boundary may replace the generated slice at that
boundary. Therefore only future slice entries strictly before `N` are charged:

```text
K(N) = max(0, floor((N - 1) / 4))
```

The initial table is indexed by `N`, not by `K`, even though several neighbouring
`N` columns map to the same number of charge-owning slices. The table is only
`9 x 33`, and retaining relaxed-step indexing avoids repeated conversion and
boundary mistakes at runtime.

## Step 2: synthetic landscaping-work lower bound

### Synthetic straight landing

For each residual height gap and relaxed horizon, score an idealized two-lane
straight V2 descent over flat terrain at elevation zero.

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

For future slice `k`, the relaxed centre gap may be no greater than:

```text
centerGap(k) = max(0, delta - k)
```

when `delta` is expressed in physical levels and one V2 origin stride descends
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

### Material-slope containment proof

Let:

```text
a = 0.25, artificial maximum construction grade per relaxed tile
m = fixed effective material vertical fall per lateral tile
```

The fixed effective dumping slope must be no runnier than the artificial
construction envelope requires for containment:

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

## Initial `Wdump` table

### Dimensions

For each immutable dumping configuration, precompute:

```text
WdumpTable[deltaIndex, N]

deltaIndex = 0..8
N          = 0..32 relaxed tile steps
```

The table contains `9 x 33 = 297` values.

Each row uses an exact integer synthetic gap in physical levels:

```text
deltaTable = deltaIndex
```

Each column contains the cumulative synthetic landscaping work of the
charge-owning future V2 slices strictly before the relaxed horizon `N`:

```text
WdumpTable[d, N]
    = cumulative direct and exterior-side work
      of the first K(N) synthetic future slices

K(N) = max(0, floor((N - 1) / 4))
```

The table generator may score one synthetic route per integer `delta` and store
all cumulative horizon prefixes from that run.

### Runtime lookup

Given exact runtime `delta` and proven relaxed horizon `N`:

```text
d = min(8, floor(max(0, delta)))
n = min(32, max(0, N))

H_land = WdumpTable[d, n]
```

There is no interpolation.

The lookup uses the lower integer gap endpoint. Because synthetic work must be
nondecreasing in `delta`, this cannot exceed the synthetic work at the exact
gap. Capping a larger gap at `8` and a longer horizon at `32` likewise returns
only a safely accumulated prefix.

This baseline is intentionally weak for:

* gaps below one level;
* noninteger gaps near the next integer boundary;
* gaps above eight levels; and
* unavoidable horizons beyond 32 relaxed steps.

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
maximum ray cost
```

Precompute the table when the configuration is first encountered, then cache it
for reuse. If all relevant settings are global and immutable during a game
session, one table may serve every request with the same dumping material and
slope. Otherwise cache by configuration identity.

Table construction must use the same shared direct-work and side-ray scoring
helpers as the real scorer wherever practical. It must not duplicate a subtly
different numerical integration.

### Required monotonicity

Validate:

```text
WdumpTable[d + 1, N] >= WdumpTable[d, N]
WdumpTable[d, N + 1] >= WdumpTable[d, N]
```

A cap may make the function flat but must not make it decrease.

The safety of lower-endpoint and capped lookup depends on this monotonicity.
Treat a violation as a scorer/table-generation defect, not as a reason to sort
or repair values after generation.

## Later mining counterpart

The mining extension reuses the same diamond-extrema cache and defines a
separate synthetic work table, referred to here as `Wmine`:

```text
WmineTable[deltaIndex, N]
```

Use the same initial dimensions and lookup policy unless measurement justifies a
different range:

```text
deltaIndex = 0..8, step 1
N          = 0..32 relaxed tile steps
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
rounding the minimum downward weakens the mining heuristic.

The first implementation weakens only the work lookup by selecting the lower
integer gap endpoint and capping the supported range. Handoff-distance numerical
behavior remains a separate graph-semantics issue.

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
* tower-area membership or ocean eligibility;
* fixed goals; or
* the current goal set.

It may therefore live on the immutable snapshot and be reused across sequential
cluster requests until terrain or snapshot coverage changes. If memory ownership
is simpler, start request-scoped and promote it only after measurements.

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

### Coverage and physical-map boundaries

Tower-area and ocean status do not affect the initial extrema query. Snapshot
coverage does.

If an in-physical-map terrain sample required by the geometric diamond is
missing from the immutable snapshot, the entry may hide a favorable extremum.
Mark the result incomplete and fail open for the affected operation:

* dumping uses a ceiling at least `H`; and
* mining uses a floor at most `H`.

Either produces zero landscaping heuristic.

Physical-map exterior has no terrain sample. Dumping and mining interact with
map edges differently through their real side-ray scorers, so do not infer a
stronger operation-independent extremum from missing exterior space. The first
implementation should normally avoid such queries through request bounds and
the useful-height envelope; otherwise fail weak where the proof does not cover
the operation-specific edge behavior.

## Future refinements

### Filtered landing extrema

A position outside the tower area or on ocean cannot host a generated V landing.
A stronger future horizon proof may maintain separate values:

```text
F_landing = extremum over proven eligible landing positions
F_support = extremum over all terrain that may reduce direct or side-ray work
```

Use `F_landing` to strengthen the ground horizon and `F_support` to preserve the
work lower bound. Do not simply filter the shared extremum: terrain at an
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

The simple horizon formula is:

```text
dGround = ceil(delta / a)
```

Determine whether the authoritative terrain handoff accepts a profile within a
nonzero semantic tolerance of terrain. If it does, the heuristic must use the
same tolerance when proving that another relaxed step is unavoidable. Do not
introduce an arbitrary floating-point epsilon unrelated to graph behavior.

Also determine:

* whether horizon calculations should use `float`, `double`, or an exact profile
  unit;
* whether the shared scorer is reproducible enough for table generation;
* whether table values need a tiny downward numerical guard; and
* how A* versus Dijkstra fixtures distinguish admissibility defects from
  floating-point tie noise.

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

The components cover disjoint cost portions:

* the existing potential lower-bounds travel, generated fixed overhead, centre
  spokes, exact G suffix distance, and fixed-terminal fees; and
* `H_land` lower-bounds only unpaid future direct and ordinary exterior-side
  landscaping work.

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

* the same `DiamondTerrainExtremaCache`;
* the cached minimum rather than maximum;
* a sign-symmetric minimum ground horizon; and
* the fixed-grid `Wmine` table using the least runny normal material in the
  tower area.

## Diagnostics

Record:

### Heuristic use

* calls, zero results, and nonzero results by operation;
* total and average landscaping heuristic added;
* favorable extremum, exact residual gap, table gap, ground horizon, fixed
  horizon, selected `N`, and `K(N)` distributions;
* table lookup time; and
* total heuristic evaluation time.

### Terrain extrema

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
* samples outside the tower area;
* ocean samples;
* selected extrema outside the tower area;
* selected extrema on ocean;
* maximum radius;
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
* exact `N` versus capped table horizon;
* estimated weakening from gap flooring and caps;
* lookup counts by table cell;
* monotonicity validation results;
* requests with `delta > 8`;
* requests with `N > 32`; and
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
9. unfiltered extrema versus diagnostics-estimated filtered strength; and
10. the older lazy successor-frontier heuristic.

## Validation

### Terrain-extrema fixtures

* flat terrain with analytically known extrema;
* one peak and one pit at every Manhattan boundary position;
* equal extrema at multiple positions;
* a higher peak and lower pit immediately outside the diamond;
* rear-half extrema required by cheapest U-turn cases;
* extrema outside the tower area are included;
* ocean extrema are included;
* inclusion of unlandable samples never strengthens either operation's bound;
* fixed-target horizons shorter and longer than the ground horizon;
* large local gaps and radius limits;
* physical-map boundaries;
* missing required snapshot coverage returns zero heuristic for the affected
  operation; and
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
* cache ownership is invalidated with terrain snapshot coverage.

### `Wdump` table fixtures

* the table has exactly 9 gap rows and 33 horizon columns;
* row `d` is generated from exact synthetic gap `d`;
* column `N` includes exactly `K(N) = floor((N - 1) / 4)` nonnegative future
  charge-owning slices;
* zero gap and zero horizon return zero;
* the current state is never charged;
* synthetic successor centre and corner heights use the favorable descending
  phase;
* direct work matches the shared real scorer on equivalent flat fixtures;
* exterior side-ray work matches the shared scorer with the frozen dumping
  slope;
* every row is nondecreasing in `N`;
* every column is nondecreasing in `deltaIndex`;
* runtime gaps use `floor(delta)` and never round upward;
* runtime gaps above eight use row eight;
* runtime horizons above 32 use column 32;
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
* later low/mining mirror fixtures using the same extrema cache;
* terrain rising or falling as favorably as construction grade permits;
* high and low support terrain encountered by side rays;
* immediate ground and fixed-frontage terminals;
* terminals exactly on V2 stride boundaries;
* combined requests in which each goal class wins;
* U-turn routes that invalidate a forward-half diamond;
* requests whose extrema come from outside the tower area or ocean;
* equality between A* and Dijkstra success, selected total cost, and cost
  breakdown under the same graph-pruning configuration;
* differential equality with the heuristic disabled; and
* live marker cases before and after enabling the useful-height envelope.

## Proposed implementation order

1. Implement and measure the useful-height envelope.
2. Preserve a trustworthy relaxed cardinal terminal-distance horizon, including
   compatible fixed frontages.
3. Implement the synthetic `Wdump` generator with fixed slope, integer gaps
   `0..8`, and relaxed horizons `0..32`.
4. Verify the table against the shared direct-work and side-ray scorers and its
   required monotonicity.
5. Add a direct-scan full unfiltered diamond-extrema query and calculate the new
   dumping bound in diagnostics only.
6. Enable the direct-scan heuristic behind an experimental flag and compare A*
   with Dijkstra.
7. Add exact positive-radius `(tile, radius)` paired-extrema memoization without
   recursive prefetch and measure natural query reuse.
8. Add opportunistic child/crescent reuse.
9. Add four-child recursive memoization with budgets and measure prefetch
   utilization, opposite-parity misses, paired-extrema reuse, and total runtime.
10. Test the omitted centre child only as an optional additional prefetch mode.
11. Select the cheapest extrema-query strategy from live measurements; do not
    retain recursive prefetch merely because it has a high cache-hit rate.
12. Compare the selected scalar-diamond formulation with the older lazy
    profile-aware successor frontier.
13. Add `Wmine` using the existing paired-extrema cache and the least runny
    tower-area normal material.
14. Measure the strength lost to unfiltered extrema, integer gap flooring, and
    the `delta = 8` / `N = 32` caps.
15. Consider filtered landing extrema, finer/adaptive tables, exact large-gap
    evaluation, or alternate heuristic material slopes only when measurements
    justify the added complexity.

Keep this heuristic only if its measured pruning benefit survives terrain-
extrema lookup, fixed-table lookup, cache memory, and queue overhead after
useful-height pruning is already enabled.
