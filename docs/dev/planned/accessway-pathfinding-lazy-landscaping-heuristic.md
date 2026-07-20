# Lazy Unavoidable-Landscaping Heuristic

Status: proposed refinement after useful-height domain pruning

Drafted: 2026-07-16

Related designs:

* [Accessway Pathfinding](accessway-pathfinding.md)
* [Accessway Pathfinding Side-Ray Landscaping Cost Amendment](accessway-pathfinding-side-ray-cost.md)
* [Accessway Pathfinding Useful-Height Envelope](accessway-pathfinding-height-envelope.md)
* [Unified Goal Search and Snapshot Potential Heuristic](unified-goal-search-snapshot-potential-heuristic.md)

## Priority and purpose

Implement and measure the useful-height domain-pruning envelope first. It can
remove whole high and low generated-state sheets under both A* and Dijkstra
before profile, history, landscaping-ray, and queue work. This heuristic is a
later, complementary experiment intended to improve A* ordering among the `V`
states that remain inside that domain.

The existing V2 potential intentionally omits variable landscaping work. A
deep mining state or high filling state can therefore look geometrically close
to a goal even when every continuation must pay substantial direct terrain
work before it can reach usable ground. Add a conservative lower bound on that
unpaid future work to `h`, without changing the real edge costs in `g`.

The refinement succeeds only if the reduction in visited states, pending
high-water, and search time exceeds the cost of evaluating the extra bound.
It should remain optional until live measurements show a benefit.

## Core observation

For a current generated state, let `dh` be its operation-specific terrain-to-
profile gap: positive cut depth for mining or positive fill height for dumping.
Finite terrain variation and finite generated construction grade limit how
quickly that gap can disappear. While a conservative lower bound on the gap
remains positive, subsequent generated origins have unavoidable direct work
and a terrain/G handoff is not yet possible.

A global material-slope recurrence is safe but generally too weak. Undisturbed
uranium and titanium can admit very steep hypothetical terrain changes, so a
ten-level gap may disappear in the relaxation within one four-tile origin
step. The immutable access snapshot already contains the actual terrain
heights. Use those samples to give the relaxation the cheapest actual terrain
and the most favorable legal route profile instead of assuming that the
steepest material occurs everywhere.

For mining, one relaxed successor step from state `s` considers every admitted
successor `t` and minimizes:

```text
terrain-to-profile gap at t
= actual terrain around t
  - highest relaxed legal successor profile from s
```

Dumping is symmetric: use the highest favorable terrain and the lowest relaxed
legal successor profile. Mixed leveling evaluates the operation-specific
positive work that the real transition cost would charge.

Use the four precise terrain corners and the candidate's four profile corners,
not only its center. This directly matches the existing direct-work scorer:
each corner represents one quarter of a 4x4 origin footprint. Omit fixed origin
overhead, traversal, cleanup, unresolved penalties, durability, history, and
other feasibility costs from this direct-footprint component. The existing
potential and real `g` already account for the appropriate non-landscaping
terms, while omitting additional nonnegative costs preserves a lower bound.
(See below for an analytical side-ray extension.)

## Guaranteed horizon

The heuristic may sum work only across future generated origins that every
route must still encounter. Preserve a distance or relaxed-step horizon beside
the current cost potential rather than attempting to derive a V-origin count
by dividing the scalar potential value. The scalar combines V travel, exact G
suffix distance, fixed-provider terminal fees, and generated fixed overhead;
those terms do not identify how many future V origins are unavoidable.

Combine two facts:

1. the goal-distance relaxation says that no target can be reached before a
   minimum spatial or generated-step horizon; and
2. the positive terrain-to-profile gap says that an ordinary V-to-G handoff
   cannot occur during the proven part of that horizon.

Cap the landscaping sum at the shorter proof. Fixed V frontages remain valid
terminals before surfacing, so their distance and compatible target profiles
must participate in the horizon. For combined tower-ground and fixed-frontage
requests, use a minimum over goal classes or otherwise retain enough seed
identity that landscaping debt is never added past the cheapest compatible
terminal.

If a rigorous mandatory V-step horizon is not yet available, fail open with a
zero landscaping heuristic. Do not infer one from total potential cost.

## Analytical side-ray lower bound

The core four-corner calculation omits side-ray spreading costs to remain a simple lower bound. However, it is possible to calculate an analytical lower bound on the future side-ray work as well.

Given that a point `P` needs at least a distance `D` tiles to get down to the ground, there must be an upper bound on the actual ground height within that distance `D`. Using that upper bound for the ground height gives a lower bound on the height difference `dh(d)` for any distance `d` within `D` along the steepest legal ramp cone (e.g., at a 0.25 incline) from `P` to `D`.

Since all loose materials are runnier than any legal accessway slope, dumped material within `D` cannot run outside `D`. Thus, a lower bound can be calculated from `h_P` and `D` alone by assuming the best case: a straight slope down from `P`, `D` long, touching the ground at `D`. Assuming flat ground (which minimizes the required fill volume), the flat footprint, fixed origin, and ray costs can all be mathematically estimated and added to the unpaid work in `h`.

## Lazy evaluation

### Potentially

Calculate the bound only when an A* `V` state is enqueued and its current
operation-specific gap is positive. Expand a small relaxed successor frontier
one generated step at a time. Stop when any of the following occurs:

* the four-corner direct-work lower bound reaches zero;
* the guaranteed V-step/goal horizon is exhausted;
* the frontier is empty;
* a configurable experimental depth or work budget is reached.

Stopping early returns the accumulated prefix and only weakens the heuristic.
Ordinary surface states should therefore finish after one cheap check, while
deep or high states receive additional guidance only when it can matter.

The simplest version independently takes the least direct work at each depth:

```text
H_land(s, N) = sum for k = 1..N of
               min LBDirectWork(t)
               over relaxed states t reachable at depth k from s
```

This remains a lower bound even when different depths select different relaxed
paths: every concrete path's work at a depth is at least that depth's global
minimum. It is intentionally weak and cheap.

A later, stronger memoized recurrence may retain path continuity:

```text
H_land(s, N) = min over relaxed successors t of
               [ LBDirectWork(t) + H_land(t, N - 1) ]
```

Both formulations must apply the configured landscaping distance scale and
direct-work weight exactly once. They must not add the current state's already
charged landscaping cost: that cost is part of `g`; only unpaid successor work
belongs in `h`.

## Request-scoped memoization

Cache lazy results for the immutable request snapshot and goal set. A candidate
key may contain:

```text
canonical/lane centers
travel axis and direction
lane profile modes and heights
remaining proven horizon
operation class
```

Do not include generated history in the initial relaxation. Ignoring history
allows more optimistic continuations and improves cache reuse. Discard the
cache when the snapshot, effective goals, relevant settings, or frozen useful-
height envelope changes.

Bound cache size and relaxed-frontier work. Cache saturation or budget
exhaustion must fail weak by returning the safely accumulated prefix or zero;
it must never substitute a penalty.

## Later restriction to eligible V origins

After the useful-height envelope is implemented, measured, and frozen for a
request, reduce lazy frontier work by considering only eligible V origins.
This can materially reduce the number of terrain/profile samples evaluated by
the heuristic and avoid spending time on relaxed nodes that the real pruned
search graph cannot enqueue.

Start with cheap, request-immutable eligibility rules already authoritative for
the search domain:

* request and physical-map bounds;
* the correct V-origin lattice and transition geometry;
* the frozen useful-height envelope at each newly reached lane center;
* immutable fixed/start ownership rules;
* static exclusions that are proven to reject the same generated origin for
  every history reaching that concrete state.

Do not initially run full candidate feasibility, side-ray integration,
generated-history compatibility, cleanup, durability, or history-dependent
projected-work checks inside the heuristic. Those checks could cost as much as
real expansion, fragment the cache, and accidentally make the relaxation
history-specific.

Restricting the heuristic to origins removed by the enabled useful-height
envelope is admissible relative to that already-pruned graph. Any additional
eligibility filter can raise the lower bound and therefore requires a proof
that every excluded origin is impossible in the authoritative search graph,
not merely unlikely or expensive. If that proof is absent, retain the origin
in the relaxed frontier.

Compare three measured modes after envelope rollout:

1. all relaxed V origins inside horizontal bounds;
2. origins admitted by the frozen useful-height envelope;
3. envelope-admitted origins plus proven cheap static eligibility filters.

The most restrictive mode is worthwhile only if it reduces total search time,
not merely heuristic frontier size.

## Interaction with A* and Dijkstra

Add the landscaping component only to A*:

```text
h = existingPotential + H_land
```

The components cover disjoint portions of real edge cost: the existing
potential lower-bounds travel, fixed-origin overhead, and goal suffix/terminal
fees, while `H_land` lower-bounds only unpaid weighted direct work. Dijkstra
continues to enqueue `h = 0` and remains the optimality reference for the same
enabled domain-pruning configuration.

Initially prefer admissibility over consistency. The search already accepts a
better `g` label for a concrete state, but fixtures should also check the local
consistency inequality where practical:

```text
H_land(s) <= real direct-work edge cost(s, t) + H_land(t)
```

If inconsistent values cause excessive re-enqueueing, weaken the bound or
change the recurrence rather than closing states prematurely.

## Diagnostics and validation

Add request/search diagnostics for:

* heuristic calls and nonzero results;
* cache hits, misses, entries, and saturation;
* relaxed states and origins examined;
* origins removed by envelope/static eligibility;
* maximum and average lazy depth;
* early-stop reason counts;
* total landscaping heuristic added;
* heuristic evaluation time;
* visited states, pending high-water, and total search time.

Validation should include:

* flat surface states returning zero promptly;
* deep mining and high filling fixtures with known future direct work;
* terrain that descends or rises as favorably as possible;
* mixed-operation profiles and four-corner nonuniform terrain;
* immediate and nearby tower-ground handoffs;
* compatible underground/elevated fixed frontages;
* combined goal requests in which a different goal class wins;
* equality between A* and Dijkstra success, selected total cost, and cost
  breakdown on the same envelope/pruning configuration;
* differential results with the landscaping heuristic disabled;
* identical results between the all-origin and eligible-origin lazy modes;
* live marker cases recorded before and after useful-height envelope rollout.

## Proposed implementation order

1. Implement and evaluate the useful-height domain-pruning envelope.
2. Preserve a trustworthy relaxed distance/V-step horizon with goal-class or
   seed information sufficient for fixed frontages.
3. Add diagnostics-only lazy four-corner bounds without affecting queue order.
4. Enable the cheap independent-depth landscaping heuristic behind an
   experimental flag and compare A* with Dijkstra.
5. Memoize and tune the depth/work/cache budgets from live measurements.
6. Restrict the lazy frontier to useful-height-envelope-eligible V origins and
   measure total runtime.
7. Add only proven cheap static eligibility filters that improve end-to-end
   time.
8. Consider the path-continuous recurrence only if the cheap bound is useful
   but materially too weak.

Do not implement this refinement merely because the bound is available. Keep
it only if its measured pruning benefit survives the calculation overhead after
the more promising domain envelope is already active.
