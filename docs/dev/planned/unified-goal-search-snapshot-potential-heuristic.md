# Design Proposal: Unified Goal Search and Snapshot Potential Heuristic

## Status

**Implemented in V2; V1 sharing remains future work**

## Accepted V2 formulation

V2 already had combined tower-ground and fixed-frontage goal testing. The implemented change is the combined heuristic:

* Build the potential once per path request because fixed-provider frontages vary by cluster.
* Seed every goal-connected G center with its exact remaining G distance.
* Seed each fixed frontage at the canonical center of its exact matching state (`goal anchor + exposed direction`) with its implemented terminal cost: the final provider-entry slice plus optimistic downstream driving distance through the accepted provider network to tower ground.
* Propagate cardinally over the relaxed request bounds at the proven minimum V cost per tile, `1 + generated-flat-cost / 4`, ignoring V feasibility, history, disturbance, durability, and cleanup.
* Query this field directly for V states. Goal-connected G states use exact ground-graph distance; disconnected G states use the component-aware escape field described below because they can now transition back to V.
* Charge the canonical-center spoke at twice that minimum V rate, `2 + generated-flat-cost / 2`, covering the maximum two-tile Manhattan offset without making a handoff cheaper than ordinary V.

The relaxed V field does not itself route around cliffs, ocean, or buildings. Its ground seeds nevertheless carry the exact suffix cost around those obstacles once the route reaches G. Removing constraints and all landscaping costs above the unavoidable generated-origin overhead can only reduce the estimate, preserving admissibility. Concrete G and accepted-provider distance graphs use unit cardinal steps and `sqrt(2)` diagonals with conservative no-corner-cutting side-corridor validation; explicit G states query that exact octile metric rather than the cardinal V relaxation.

An explicit G state in a component containing a tower goal uses that exact octile suffix. A G state in a disconnected component uses a second request-scoped lower-bound field: `min_t(d_G(current,t) + 2F + VPotential(t))` over traversable centers `t` in the same component, where `F` is the unavoidable fixed cost of each of the two origins introduced by the current G-to-V edge. This allows cheap local G travel before G-to-V re-entry without assigning zero to the entire component or leaving every viable V continuation behind an artificial `2F` queue step. Concrete G-to-V expansion is available at any suitable G center, validates the reverse two-lane seam, and pays the same center spoke as V-to-G; this permits starting a grade before a mountainside when doing so avoids expensive terrain work. Feasibility, direct work, cleanup, history blockers, and that nonnegative spoke remain omitted from the lower bound. As in V1, labels are dominated by concrete state rather than full generated-history identity: the cheapest route to a G center or V band owns the history used for future expansion. A genuinely cheaper V shortcut may therefore leave and re-enter the same static G component, while a more expensive detour loses when it reaches an already cheaper concrete state. The V field covers every captured traversable/cleanup G center as well as the V-generation bounds, preventing outside-area zero seeds.

V1 still uses its older height-aware geometric goal index. Its distance grid covers the union of the tower's V-generation bounds and all captured traversable or cleanup-qualified G tiles, so clear outside-area search-margin nodes retain a real lower bound instead of falling back to zero. Its A* queue shares V2's equal-`f` rule: prefer lower remaining `h`, then lower `g`. These changes prevent constant-`f` ground plateaus from expanding outward from the start or receiving artificial priority outside the tower rectangle. Dijkstra is unchanged because every queued heuristic is zero.

---

# Background

The current accessway pathfinding architecture consists of two independent search implementations:

- **V1** (single-lane accessways)
- **V2** (band-based accessways)

Both can receive combined goal requests. V2 currently searches tower-reachable ground and exposed fixed terrain-work frontages, with future provider categories including:

- Tower-reachable ground
- Existing mining designations
- Existing dumping designations
- Existing leveling designations
- Other future access providers

At the same time, both implementations currently rely on relatively simple heuristics (primarily Manhattan distance), despite the snapshot already containing significantly richer information about the traversable world.

This document proposes introducing:

1. Combined multi-goal search.
2. A snapshot-wide potential field (heuristic map).
3. Shared heuristic infrastructure between V1 and V2.

The implementation should begin in V2, where the architectural model is currently evolving most rapidly, before being generalized into common infrastructure.

---

# Motivation

## Current Manhattan heuristic

Current A* guidance is purely geometric.

Although admissible, Manhattan distance ignores:

- cliffs
- oceans
- buildings
- disconnected terrain
- actual tower-reachable ground

As a result, searches frequently spend effort exploring geometrically attractive regions that can never contribute to a valid route.

---

## Existing snapshot information

The snapshot already computes large amounts of useful information:

- tower-reachable ground
- vehicle-qualified ground graph
- cleanup graph
- projected designation blockers
- durability
- disturbance
- building occupancy

Only a very small portion of this information currently contributes to the heuristic.

---

## Desired behaviour

The heuristic should guide the search toward:

> "The cheapest possible optimistic route to *any* valid goal."

while intentionally ignoring expensive constraints such as:

- landscaping feasibility
- operation selection
- history
- disturbance
- durability
- cleanup

This preserves admissibility while dramatically improving guidance.

---

# High-Level Architecture

The proposal consists of three stages.

## Stage 1

Combined goal search in V2. **Implemented.**

## Stage 2

Introduce a request-scoped potential field for V2. **Implemented.**

## Stage 3

Share the heuristic infrastructure between V1 and V2.

---

# Stage 1 — Combined Goal Search (V2, implemented)

## Motivation

Currently V2 searches primarily toward tower-reachable ground.

Long term it should instead search toward the nearest valid provider.

Examples:

- tower ground
- existing mine
- existing dump
- future provider types

The search should no longer care which provider it eventually reaches.

Instead:

> Search for the cheapest valid access provider.

---

## Goal representation

Rather than a single goal set, V2 should construct a combined collection of goal seeds.

Example:

```
GoalSet

TowerGround

MiningProviders

DumpingProviders

LevelingProviders

FutureProviderTypes
```

Every goal should expose:

```
Location

GoalType

TerminalCost

Metadata
```

## Fixed-provider downstream travel cost

**V2 initial optimistic field: implemented.** The field is rebuilt for each
cluster request after prior successful clusters have materialized and been
accepted. Only accepted provider/accessway origins and the accepted cluster
frontages participate. The final four-tile entry from the exterior matching
center to the frontage interior is included in the terminal fee. Frontages
without a finite provider-to-ground continuation are omitted. The same fee is
enqueued as real traversal cost and seeds the request potential, preserving
A*/Dijkstra cost agreement.

Reaching a fixed frontage is not the end of the vehicle's real journey. Vehicles must continue through the established designation/provider network and then across tower-reachable ground. Fixed goals should therefore carry a downstream travel fee rather than always using a zero terminal cost.

Build a provider-distance layer by extending the tower-ground G field into the projected finished surfaces of existing fixed designations:

* start from the exact tower-reachable G distances;
* admit vehicle-center nodes over existing mining, dumping, and leveling profiles as established-provider nodes;
* connect compatible cardinal neighbors and provider/G boundaries; and
* propagate unit travel cost through the combined established-provider network.

The distance at a fixed frontage's interior entry center becomes that frontage's terminal cost. A candidate connecting to it then compares:

```
new accessway construction and travel cost
+ fixed-provider downstream travel distance
```

This prevents a geometrically close frontage with a long existing detour from unfairly beating a slightly farther provider with a short route to the tower.

There are two accuracy levels:

1. **Initial optimistic provider field.** Treat compatible fixed-designation centers as relaxed G-like nodes. This may ignore an internal narrow waist or other Mega-clearance defect, but it remains a useful lower bound and can be used as the defined terminal-distance proxy during the initial rollout.
2. **Clearance-exact provider field.** Project the finished designation profiles, validate the full resolved vehicle mask at every center and edge, and exclude disconnected or too-narrow provider interiors. Its frontage distance is the physical downstream driving cost and can replace the optimistic proxy without changing the search interface.

The provider-distance field describes already established terrain work; it does not itself add G-to-new-V transitions. Until V2 implements G-to-V, explicit G states continue toward tower-ground goals only, while V states may terminate at fixed frontages using these downstream fees.

---

## Search behaviour

The search itself should remain unchanged.

Only the goal test changes.

Instead of:

```
Reached tower?
```

it becomes:

```
Reached any valid goal?
```

The cheapest valid goal wins naturally.

---

## Verification

Verification should prove:

- identical behaviour for tower-only searches
- correct selection of nearer providers
- correct cost comparison between provider types
- deterministic goal selection
- no heuristic regressions

---

# Stage 2 — Request-Scoped Potential Field (implemented in V2)

## Concept

Instead of evaluating Manhattan distance during every expansion, construct a snapshot-wide heuristic field.

Conceptually:

```
Every traversable center
↓

Stores

Estimated remaining cost
```

A* then becomes:

```
h = HeuristicField[currentCenter]
```

No runtime heuristic computation.

Only an array lookup.

---

# Construction

Construction occurs once during snapshot generation.

The process consists of two passes.

---

## Pass 1 — Exact G distance

Compute the exact shortest distance over tower-pathable ground.

This already largely exists.

Result:

```
GroundDistance[x]
```

This value is exact.

---

## Pass 2 — Relaxed propagation

Seed every reachable ground center using GroundDistance.

Then propagate outward across the entire snapshot.

Propagation ignores:

- landscaping
- history
- disturbance
- durability
- cleanup

Each cardinal move costs one.

Result:

```
PotentialField[x]
```

Conceptually:

```
PotentialField[x]

=

minimum optimistic cost

from x

to any goal
```

---

# Why it remains admissible

The propagation intentionally removes constraints.

Relaxed problems always produce costs less than or equal to the real problem.

Therefore:

```
PotentialField

≤

ActualPathCost
```

which satisfies the admissibility requirement.

---

# Why it is stronger than Manhattan

Manhattan assumes:

```
Shortest geometric route
```

The potential field instead assumes:

```
Shortest geometric route

+

Actual shortest remaining ground route
```

It therefore naturally bends around:

- disconnected regions
- lakes
- cliffs
- inaccessible plateaus

without introducing overestimation.

---

# Runtime

Current:

```
Calculate Manhattan

every node
```

Proposed:

```
heuristic = field[x]
```

One lookup.

---

# Verification

Comparison against Manhattan.

Metrics:

- visited nodes
- runtime
- queue size
- heuristic values
- optimality

Fixtures should verify identical path costs.

---

# Stage 3 — Shared Heuristic Infrastructure

Once V2 has been validated, the heuristic should become snapshot infrastructure rather than V2 infrastructure.

Conceptually:

```
Snapshot

Ground Graph

Cleanup Graph

Potential Field
```

Both V1 and V2 simply query:

```
Snapshot.GetPotential(center)
```

Neither implementation computes heuristics independently.

---

# V1 Migration

V1 should continue using its current implementation initially.

After V2 validation:

Replace:

```
Manhattan(center, tower)
```

with:

```
PotentialField(center)
```

Nothing else should change.

This isolates heuristic changes from behavioural changes.

---

# Future Extensions

The field naturally supports:

- multiple provider types
- different terminal costs
- future provider categories
- dynamic provider enable/disable

Only the seed set changes.

The propagation algorithm remains identical.

---

# Design Considerations

## Why V2 first?

V2 is currently undergoing active architectural work.

Introducing the field there first provides:

- easier benchmarking
- fewer compatibility constraints
- better fixture coverage

Once stable, the same field becomes shared infrastructure.

---

## Why not implement directly for both?

Doing both simultaneously makes it harder to identify regressions.

The recommended sequence is:

1. V2 combined goals.
2. V2 potential field.
3. Validate.
4. Share infrastructure.
5. Migrate V1.
6. Remove duplicate heuristic code.

---

# Future Improvements

The field described here is intentionally conservative.

Possible future enhancements include:

## Vehicle speed

Traversal cost:

```
V

4 × s

G

1 × s

s = normalized slowness
```

where:

```
s = 1 / vehicleSpeed
```

allowing slower Mega excavators to naturally prefer shorter travel.

---

## Goal weighting

Certain provider types may eventually receive:

- fixed penalties
- priorities
- preference multipliers

without modifying the search algorithm itself.

---

## Dynamic fields

Future work could generate fields based on:

- different vehicle classes
- cleanup-enabled searches
- optional avoidance settings
- temporary construction exclusions

---

# Verification Plan

## Stage 1

Combined goals.

Verify:

- nearest provider selected
- optimal cost
- deterministic behaviour

---

## Stage 2

Potential field.

Compare against Manhattan.

Record:

- nodes expanded
- maximum queue size
- runtime
- fixture equivalence

---

## Stage 3

Migration to V1.

Verify:

- identical routes
- identical costs
- fewer expansions
- no behavioural regressions

---

# Expected Benefits

- Shared heuristic implementation.
- Elimination of duplicated heuristic logic.
- Constant-time heuristic evaluation.
- Better guidance than Manhattan.
- Natural support for multiple provider types.
- Improved scalability for larger maps.
- Cleaner separation between snapshot preprocessing and search execution.
- Strong foundation for future pathfinding enhancements.
