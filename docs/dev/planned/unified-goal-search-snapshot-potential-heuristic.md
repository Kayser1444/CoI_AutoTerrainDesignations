# Design Proposal: Unified Goal Search and Snapshot Potential Heuristic

## Status

**Proposed**

---

# Background

The current accessway pathfinding architecture consists of two independent search implementations:

- **V1** (single-lane accessways)
- **V2** (band-based accessways)

Both currently search towards a single class of goal (tower-reachable ground), although the long-term architecture envisions searches towards multiple goal types, including:

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

Implement combined goal search in V2.

## Stage 2

Introduce a snapshot potential field for V2.

## Stage 3

Share the heuristic infrastructure between V1 and V2.

---

# Stage 1 — Combined Goal Search (V2)

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

# Stage 2 — Snapshot Potential Field

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
